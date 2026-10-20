using System.Security.Cryptography;
using System.Text.Json;
using PdfForm.Core;
using PdfForm.Pdf;
using PdfForm.Publication;

namespace PdfForm.Cli;

/// <summary>CLI wire adapter for the frozen Core pdfform.configuration-result/1 shape, checked
/// against publication successor 002. It does not manufacture run identities or render evidence.</summary>
public static class CliConfigurationReceipt
{
    public static byte[] Published(string operation, TemplateSnapshot template, ReadOnlyMemory<byte> profileJson,
        IReadOnlyList<ArtifactDescriptor> outputs, IEnumerable<Finding> findings, bool structurallyBound)
    {
        if (operation is not ("configure" or "save-config")) throw new ArgumentException("E_CONFIGURATION_OPERATION");
        if (operation == "save-config" && !structurallyBound)
            throw new ArgumentException("E_CONFIGURATION_RESULT_CONTRACT");
        var parsed = ProfileParser.ParseProfile(profileJson.Span);
        if (!parsed.IsValid || findings.Any(f => f.Severity == "error")) throw new ArgumentException("E_CONFIGURATION_RESULT_CONTRACT");
        var expected = ConfigurationPublisher.ExpectedArtifacts(operation);
        if (outputs.Count != expected.Count || outputs.Any(o => !expected.Contains(new(o.Id, o.Path))))
            throw new ArgumentException("E_CONFIGURATION_RESULT_CONTRACT");
        var profile = outputs.Single(o => o.Id == "profile");
        if (profile.Bytes != profileJson.Length || profile.Sha256 != Hash(profileJson.Span))
            throw new ArgumentException("E_CONFIGURATION_RESULT_CONTRACT");
        if (operation == "save-config")
        {
            var savedTemplate = outputs.Single(o => o.Id == "template");
            if (savedTemplate.Bytes != template.ByteCount || savedTemplate.Sha256 != template.Sha256)
                throw new ArgumentException("E_CONFIGURATION_RESULT_CONTRACT");
        }
        return Serialize(operation, 0, "published", template, parsed.Value!, profileJson,
            outputs, findings, ConfigurationPublicationStages.Create("passed", "passed",
                structurallyBound ? "passed" : "not_run", structurallyBound ? "passed" : "not_run"));
    }

    public static byte[] Terminal(string operation, int exitCode, string artifactState, TemplateSnapshot? template,
        ReadOnlyMemory<byte>? profileJson, IReadOnlyList<ArtifactDescriptor> outputs, IEnumerable<Finding> findings,
        IReadOnlyDictionary<string, string> levels, bool configurationNeeded = false)
    {
        ProfileDefinition? profile = null;
        if (profileJson is { } bytes)
        {
            var parsed = ProfileParser.ParseProfile(bytes.Span);
            if (parsed.IsValid) profile = parsed.Value;
        }
        ArtifactDescriptor[] reportable = artifactState == "unknown" ? [] : outputs.Where(o => o.Path is "catalog.json" or "profile.json" or "template.pdf").ToArray();
        var diagnostics = reportable.Length == outputs.Count || artifactState == "unknown" ? findings
            : findings.Append(new Finding("E_CONFIGURATION_RESULT_OUTPUTS", "internal", "E_CONFIGURATION_RESULT_OUTPUTS"));
        // Terminal reporting must retain a known namespace outcome even if descriptor metadata is
        // unusable. The committed receipt remains untouched and this failure cannot be completion.
        return Serialize(operation, exitCode, artifactState, template, profile, profileJson,
            reportable, diagnostics, levels, configurationNeeded);
    }

    private static byte[] Serialize(string operation, int exitCode, string artifactState, TemplateSnapshot? template,
        ProfileDefinition? profile, ReadOnlyMemory<byte>? profileJson, IReadOnlyList<ArtifactDescriptor> outputs,
        IEnumerable<Finding> findings, IReadOnlyDictionary<string, string> levels, bool configurationNeeded = false)
    {
        var all = findings.ToArray();
        var errors = all.Where(f => f.Severity == "error").Select(Safe).Distinct()
            .OrderBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Stage, StringComparer.Ordinal)
            .ThenBy(f => f.Key ?? "", StringComparer.Ordinal).ThenBy(f => f.Field ?? "", StringComparer.Ordinal).ToArray();
        if (exitCode == 0 && errors.Length != 0) throw new ArgumentException("E_CONFIGURATION_RESULT_CONTRACT");
        if (errors.Length > 1000)
            errors = errors.Take(999).Append(new Finding("E_CONFIGURATION_DIAGNOSTIC_LIMIT", "internal", "E_CONFIGURATION_DIAGNOSTIC_LIMIT")).ToArray();
        object? templateIdentity = template is null ? null : new { path = "template.pdf", bytes = template.ByteCount, sha256 = template.Sha256 };
        object? profileIdentity = profile is null || profileJson is null ? null : new
        {
            path = "profile.json", bytes = profileJson.Value.Length, sha256 = Hash(profileJson.Value.Span),
            id = profile.Id, revision = profile.Revision, profileVersion = profile.ProfileVersion
        };
        string status = exitCode switch { 0 => "ok", 1 => configurationNeeded ? "configuration_needed" : "validation_failed",
            2 => "input_error", 3 => "unsupported", _ => "internal_error" };
        // Only these four assessment stages are caller-controlled. Configuration never consumes
        // data or prepares a packet/appearance, including in postcommit stdout diagnostics.
        var stages = ConfigurationPublicationStages.Create(levels.GetValueOrDefault("syntax", "not_run"),
            levels.GetValueOrDefault("profile", "not_run"), levels.GetValueOrDefault("binding", "not_run"),
            levels.GetValueOrDefault("bindingStructure", "not_run"));
        if (exitCode == 0 && (stages["syntax"] != "passed" || stages["profile"] != "passed" ||
                operation == "save-config" && (stages["binding"] != "passed" || stages["bindingStructure"] != "passed")))
            throw new ArgumentException("E_CONFIGURATION_RESULT_CONTRACT");
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "pdfform.configuration-result/1", operation, status, exitCode, artifactState,
            validationLevels = stages, template = templateIdentity, profile = profileIdentity,
            outputs = outputs.Select(o => new { id = SafeId(o.Path), path = SafePath(o.Path), bytes = o.Bytes, sha256 = o.Sha256 }).ToArray(),
            errors,
            warnings = all.Where(f => f.Severity == "warning").Select(Safe).Distinct().Take(1000).ToArray(),
            filingReadiness = "not_assessed"
        }, StrictJson.Options);
    }

    public static Finding Safe(Finding finding)
    {
        static bool Field(string? value) => value is { Length: 70 } && value.StartsWith("field_", StringComparison.Ordinal) &&
            value.AsSpan(6).ToArray().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        string? target = null;
        if (Field(finding.Field)) target = finding.Field;
        else if (finding.Field is not null)
        { try { target = CliDraftKeys.FromCanonicalField(finding.Field); } catch (ArgumentException) { } }
        string stage = finding.Stage == "bindingStructure" ? "binding"
            : finding.Stage is "syntax" or "profile" or "binding" or "data" or "packet" or "render" or "internal" ? finding.Stage : "internal";
        string code = CliCompatibilityProjection.IsKnownEngineCode(finding.Code) ? finding.Code
            : finding.Severity == "warning" ? "W_CONFIGURATION_ASSESSMENT" : "E_CONFIGURATION_ASSESSMENT";
        return finding with { Code = code, Message = code, Stage = stage, Key = Field(finding.Key) ? finding.Key : null, Field = target, RuleId = null };
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string SafePath(string path) => path is "catalog.json" or "profile.json" or "template.pdf"
        ? path : throw new ArgumentException("E_CONFIGURATION_RESULT_OUTPUTS");
    private static string SafeId(string path) => path switch { "catalog.json" => "catalog", "profile.json" => "profile", "template.pdf" => "template",
        _ => throw new ArgumentException("E_CONFIGURATION_RESULT_OUTPUTS") };
}
