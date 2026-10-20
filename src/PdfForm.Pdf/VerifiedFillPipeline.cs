using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdfForm.Core;
using PdfForm.Publication;

namespace PdfForm.Pdf;

/// <summary>Value-free result. A candidate can only leave this boundary through verified create-new publication.</summary>
public sealed class VerifiedFillResult
{
    internal VerifiedFillResult(int exitCode, string status, IReadOnlyList<Finding> findings,
        IReadOnlyDictionary<string, long> stats, VerifiedFillCandidate? candidate = null, IReadOnlyDictionary<string, string>? levels = null)
    {
        ExitCode = exitCode; Status = status; Findings = findings; Stats = stats; Candidate = candidate;
        ValidationLevels = levels ?? NewLevels();
    }
    internal static Dictionary<string, string> NewLevels() => new()
    { ["syntax"] = "not_run", ["profile"] = "passed", ["data"] = "not_run", ["binding"] = "not_run", ["bindingStructure"] = "not_run", ["packet"] = "not_run", ["render"] = "not_run" };
    public int ExitCode { get; }
    public string Status { get; }
    public bool ReadyToPublish => Candidate is not null && ExitCode == 0;
    public IReadOnlyList<Finding> Findings { get; }
    public IReadOnlyDictionary<string, long> Stats { get; }
    public IReadOnlyDictionary<string, string> ValidationLevels { get; }
    public string FilingReadiness => "not_assessed";
    [JsonIgnore] internal VerifiedFillCandidate? Candidate { get; }
}

/// <summary>
/// Immutable-input fill boundary. Only explicit pinned font replacement; no fallback selection, profile enrollment, force replacement,
/// runtime downloads, source-file recycling or external actions. This is not viewer, host, print or filing qualification.
/// </summary>
public static class VerifiedFillPipeline
{
    static VerifiedFillPipeline() => iText.Commons.ITextLogManager.SetLoggerFactory(
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

    public static VerifiedFillResult Prepare(TemplateSnapshot template, ProductionProfileSnapshot trustedProfile,
        ReadOnlySpan<byte> dataJson, bool flatten)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(trustedProfile);
        if (trustedProfile.Identity.TemplateSha256 != template.Sha256)
            return Failure(3, "E_TEMPLATE_PROFILE", "binding", "The admitted profile is bound to different template bytes.");
        try { return PrepareCore(template, trustedProfile.ParseDefinition(), dataJson, flatten); }
        catch { return Failure(4, "E_FILL_INTERNAL", "render", "Fill preparation failed without publication."); }
    }

    public static VerifiedFillResult Prepare(TemplateSnapshot template, FormConfigurationSnapshot configuration,
        ReadOnlySpan<byte> dataJson, bool flatten)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!template.MatchesConfiguration(configuration))
        {
            var levels = VerifiedFillResult.NewLevels(); levels["binding"] = "failed";
            return new(3, "unsupported", [new("E_TEMPLATE_PROFILE", "binding", "Configuration is bound to different template bytes.")],
                new Dictionary<string, long>(), levels: levels);
        }
        try
        {
            var result = PrepareCore(template, configuration.ParseProfile(), dataJson, flatten, configuration);
            return new(result.ExitCode, result.Status, SafeFindings(result.Findings), result.Stats,
                result.Candidate, result.ValidationLevels);
        }
        catch { return Failure(4, "E_FILL_INTERNAL", "render", "Configured fill preparation failed without publication."); }
    }

    /// <summary>Advisory per-key normalization and mapped-target fit only. No cross-key rule, completed form or publication claim.</summary>
    public static IReadOnlyList<Finding> CheckValueFit(TemplateSnapshot template, FormConfigurationSnapshot configuration,
        string key, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            if (!template.MatchesConfiguration(configuration)) return [new("E_TEMPLATE_PROFILE", "binding", "Configuration is bound to different template bytes.")];
            var profile = configuration.ParseProfile();
            var definition = profile.Keys.SingleOrDefault(item => item.Key == key);
            if (definition is null) return [new("E_FIT_KEY", "data", "The logical input key is absent from the configured profile.")];
            if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > Limits.MaxTextScalars * 2)
                return [new("E_TEXT_LIMIT", "data", "Value exceeds the supported scalar length limit.", Key: DiagnosticFieldId(key))];
            var normalized = ValueNormalizer.Normalize(definition, value, supplied: true);
            if (!normalized.Value.IsValid) return SafeFindings(normalized.Findings);
            var bound = new TemplateBinder().Bind(template, configuration);
            if (!bound.Bound) return SafeFindings(bound.Findings);
            // Reuse the canonical formatter/planner for split, money, date and
            // oneOf targets. The scratch report is never a data-validation result.
            var focusedProfile = profile with { Keys = [definition], Rules = [], Unmapped = [] };
            var scratch = new DataDocument { Schema = "pdfform.data/3", Profile = profile.Identity,
                Context = null, Values = new(StringComparer.Ordinal) { [key] = value.Clone() } };
            var report = new ValidationReport(focusedProfile, scratch,
                new Dictionary<string, NormalizedValue>(StringComparer.Ordinal) { [key] = normalized.Value }, [], []);
            var assignments = AssignmentPlanner.Build(report);
            VerifiedFormRenderer.CheckConfiguredFit(template, configuration, bound, assignments);
            return [];
        }
        catch (VerifiedFillException error)
        {
            return SafeFindings([new(error.Code, error.ExitCode == 3 ? "binding" : "render", "Configured target fit was refused.",
                Field: error.ApprovedField)]);
        }
        catch (TemplateInputException) { return [new("E_FILL_TEMPLATE", "binding", "The configured template is unsupported.")]; }
        catch { return [new("E_FIT_INTERNAL", "render", "Configured fit assessment could not be completed.")]; }
    }

    /// <summary>Full sample validation and exact editable-appearance verification; returns findings only and publishes nothing.</summary>
    public static IReadOnlyList<Finding> CheckSampleFit(TemplateSnapshot template, FormConfigurationSnapshot configuration,
        ReadOnlySpan<byte> dataJson) => Prepare(template, configuration, dataJson, flatten: false).Findings;

    internal static string DiagnosticFieldId(string value) => "field_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static IReadOnlyList<Finding> SafeFindings(IEnumerable<Finding> findings)
    {
        static string? Identifier(string? value) => value is null ? null
            : value.Length == 70 && value.StartsWith("field_", StringComparison.Ordinal)
                && value.Skip(6).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? value : DiagnosticFieldId(value);
        return findings.Select(finding => new Finding(finding.Code, finding.Stage,
            finding.Code switch
            {
                "W_FONT_EXPLICIT_REPLACEMENT" => "Configured output deliberately uses bundled embedded Liberation Serif Regular at the fixed source size; only printable ASCII and U+00C9 are admitted. Viewer and filing acceptance remain separate.",
                "E_TEXT_OVERFLOW" or "E_TEXT_COMB_OVERFLOW" or "E_TEXT_VERTICAL_OVERFLOW" => "The supplied value does not fit at the configured fixed size.",
                "E_FONT_GLYPH" or "E_FONT_GLYPH_UNQUALIFIED" => "A supplied character is outside the supported font repertoire.",
                "W_ACCEPTANCE_NOT_COMPLETE" => "Structural rendering checks do not establish viewer, print or filing acceptance.",
                _ => "The configured operation reported this fixed diagnostic."
            }, Identifier(finding.Key), Identifier(finding.Field), Identifier(finding.RuleId),
            finding.Severity == "warning" ? "warning" : "error")).ToArray();
    }

    /// <summary>Source-internal engine fixture seam. It still requires exact reviewed blank bytes, complete binding and valid data.</summary>
    internal static VerifiedFillResult PrepareCore(TemplateSnapshot template, ProfileDefinition profile,
        ReadOnlySpan<byte> dataJson, bool flatten) => PrepareCore(template, profile, dataJson, flatten, null);

    private static VerifiedFillResult PrepareCore(TemplateSnapshot template, ProfileDefinition profile,
        ReadOnlySpan<byte> dataJson, bool flatten, FormConfigurationSnapshot? configuration)
    {
        var findings = new List<Finding>();
        var stats = new Dictionary<string, long>();
        var levels = VerifiedFillResult.NewLevels();
        try
        {
            var parsed = DataValidator.ParseData(dataJson);
            if (!parsed.IsValid) { levels["syntax"] = "failed"; return new(2, "input_error", parsed.Findings, stats, levels: levels); }
            levels["syntax"] = "passed";
            var bound = configuration is null ? new TemplateBinder().Bind(template, profile) : new TemplateBinder().Bind(template, configuration);
            findings.AddRange(bound.Findings);
            if (!bound.Bound) { levels["bindingStructure"] = "failed"; levels["binding"] = "failed"; return new(3, "unsupported", findings.AsReadOnly(), stats, levels: levels); }
            levels["bindingStructure"] = "passed";
            var report = DataValidator.Validate(profile, parsed.Value!);
            findings.AddRange(report.Findings);
            if (!report.IsValid) { levels["data"] = "failed"; return new(1, "validation_failed", findings.AsReadOnly(), stats, levels: levels); }
            levels["data"] = "passed";
            var assignments = AssignmentPlanner.Build(report);
            stats["targetsExpected"] = assignments.Count;
            stats["targetsCleared"] = assignments.Count(x => x.IsClear);
            stats["targetsSet"] = assignments.Count(x => !x.IsClear);
            var candidate = configuration is null ? VerifiedFormRenderer.Render(template, profile, bound, assignments, flatten)
                : VerifiedFormRenderer.Render(template, configuration, bound, assignments, flatten);
            levels["binding"] = "passed";
            levels["render"] = "passed";
            if (candidate.UsesNativeCheckboxAppearances)
                findings.Add(new("W_CHECKBOX_FONT_PORTABILITY_UNQUALIFIED", "render",
                    "Preserved native checkbox appearances use an unembedded symbol font with an observed viewer-dependent visibility failure; cross-viewer qualification remains incomplete.", Severity: "warning"));
            if (candidate.UsesPrivateUnembeddedFont)
                findings.Add(new("W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED", "render",
                    "Bounded private output preserves the exact unembedded native font and only evidenced glyphs; actual font outlines and cross-viewer portability remain unqualified.", Severity: "warning"));
            if (candidate.UsesExplicitEmbeddedFont)
                findings.Add(new("W_FONT_EXPLICIT_REPLACEMENT", "render",
                    "Output deliberately replaces the admitted source text resource with bundled embedded Liberation Serif Regular at its fixed size; only printable ASCII and U+00C9 are admitted. This is not a viewer or filing qualification.", Severity: "warning"));
            stats["targetsVerified"] = assignments.Count;
            stats["normalAppearancesVerified"] = assignments.Count;
            stats["pagesVerified"] = bound.Inspection.Pages;
            findings.Add(new("W_ACCEPTANCE_NOT_COMPLETE", "render",
                "Runtime structural and content checks passed; viewer, physical-print, supported-host and filing acceptance are not assessed.", Severity: "warning"));
            return new(0, "ok", findings.AsReadOnly(), stats, candidate, levels);
        }
        catch (VerifiedFillException ex)
        {
            levels["render"] = "failed";
            if (ex.ExitCode == 3) levels["binding"] = "failed";
            findings.Add(new(ex.Code, ex.ExitCode == 3 ? "binding" : "render", ex.SafeMessage, Field: ex.ApprovedField));
            return new(ex.ExitCode, ex.ExitCode == 3 ? "unsupported" : "internal_error", findings.AsReadOnly(), stats, levels: levels);
        }
        catch (TemplateInputException)
        {
            levels["render"] = "failed";
            findings.Add(new("E_FILL_TEMPLATE", "render", "The immutable template could not be normalized within the supported boundary."));
            return new(4, "internal_error", findings.AsReadOnly(), stats, levels: levels);
        }
        catch
        {
            levels["render"] = "failed";
            findings.Add(new("E_FILL_INTERNAL", "render", "Fill preparation failed without publication."));
            return new(4, "internal_error", findings.AsReadOnly(), stats, levels: levels);
        }
    }

    /// <summary>Existing destinations fail without modification. Optional report remains a separate publication contract.</summary>
    public static PublicationOutcome PublishCreateNew(VerifiedFillResult prepared, string destination,
        IReadOnlyList<string> inputPaths, string? resultPath = null, ReadOnlyMemory<byte>? resultBytes = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!prepared.ReadyToPublish || prepared.Candidate is null)
            return new(false, 4, "none", "E_FILL_NOT_VERIFIED", []);
        return new SafePublisher().PublishSingle(new(destination, inputPaths,
            prepared.Candidate.Artifact("document", "document.pdf"), Force: false, ResultPath: resultPath, ResultBytes: resultBytes));
    }

    private static VerifiedFillResult Failure(int exit, string code, string stage, string message) =>
        new(exit, exit == 4 ? "internal_error" : "unsupported", [new(code, stage, message)], new Dictionary<string, long>());
}

internal sealed class VerifiedFillCandidate
{
    private readonly byte[] bytes;
    private readonly string digest;
    private readonly RenderManifest manifest;
    internal VerifiedFillCandidate(byte[] ownedBytes, RenderManifest verifiedManifest, bool flattened)
    { bytes = ownedBytes; manifest = verifiedManifest; Flattened = flattened; digest = Hash(bytes); }
    internal bool Flattened { get; }
    internal bool UsesNativeCheckboxAppearances => manifest.Targets.Values.Any(t => t.Assignment.Kind == "checkbox" && t.CheckboxMark is null);
    internal bool UsesPrivateUnembeddedFont => manifest.Targets.Values.Any(t => t.Font?.PrivateUnembeddedException == true);
    internal bool UsesExplicitEmbeddedFont => manifest.Targets.Values.Any(t => t.Font is { } font && ScheduleBEmbeddedFont.IsReplacementPolicy(font.Policy));
    internal CandidateArtifact Artifact(string id, string path) => new(id, path, bytes, Verify);
    internal RenderManifest ManifestForFixture => manifest;
    internal byte[] CopyForFixture() => bytes.ToArray();
    internal bool Verify(Stream reopened)
    {
        try
        {
            using var copy = new BoundedPdfMemoryStream();
            reopened.CopyTo(copy);
            byte[] observed = copy.ToArray();
            if (!string.Equals(Hash(observed), digest, StringComparison.Ordinal)) return false;
            if (Flattened) VerifiedFormRenderer.VerifyFlattened(observed, manifest);
            else VerifiedFormRenderer.VerifyEditable(observed, manifest);
            return true;
        }
        catch { return false; }
    }
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}

internal sealed class VerifiedFillException(string code, string safeMessage, int exitCode = 4) : Exception(safeMessage)
{
    internal string? ApprovedField { get; set; }
    internal string Code { get; } = code;
    internal string SafeMessage { get; } = safeMessage;
    internal int ExitCode { get; } = exitCode;
}

/// <summary>Every serialization has a finite cap; all candidates are memory-owned until SafePublisher stages exclusively.</summary>
internal sealed class BoundedPdfMemoryStream : MemoryStream
{
    private const int Maximum = TemplateSnapshot.MaximumBytes;
    private void Check(long count)
    {
        if (count < 0 || Position + count > Maximum)
            throw new VerifiedFillException("E_FILL_OUTPUT_LIMIT", "PDF candidate exceeds its bounded byte limit.");
    }
    public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    public override void SetLength(long value) { if (value > Maximum) Check(Maximum + 1L); base.SetLength(value); }
}
