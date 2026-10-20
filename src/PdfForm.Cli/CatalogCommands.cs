using System.Security.Cryptography;
using System.Text.Json;
using PdfForm.Core;
using PdfForm.Pdf;
using PdfForm.Publication;

namespace PdfForm.Cli;

public static partial class CliApplication
{
    private static CliExecution? TryExecuteOnboarding(CliOnboardingRequest request) => request.Operation switch
    {
        CliOnboardingOperation.Check => ExecuteCompatibilityCheck(request),
        CliOnboardingOperation.CatalogValidate => ExecuteCatalogValidate(request),
        CliOnboardingOperation.SampleInput => ExecuteSampleInput(request),
        CliOnboardingOperation.SaveConfig => ExecuteSaveConfiguration(request),
        CliOnboardingOperation.Configure => ExecuteConfigure(request),
        _ => null
    };

    private static CliExecution OnboardingArgumentFailure(string operation, CliOnboardingParse parsed) =>
        operation is "configure" or "save-config"
            ? ConfigurationResult(operation, parsed.ExitCode, parsed.Finding is null ? [] : [parsed.Finding],
                configurationNeeded: parsed.ExitCode == 1)
            : operation == "check" ? CompatibilityFailure(parsed.ExitCode, parsed.Finding is null ? [] : [parsed.Finding], "invalid")
            : OnboardingResult(operation, parsed.ExitCode, parsed.Finding is null ? [] : [parsed.Finding]);

    private static Dictionary<string, string> OnboardingLevels() => new(StringComparer.Ordinal)
    {
        ["syntax"] = "not_run", ["profile"] = "not_run", ["binding"] = "not_run", ["bindingStructure"] = "not_run",
        ["data"] = "not_run", ["packet"] = "not_run", ["render"] = "not_run"
    };

    private static void ConfigurationRejected(Dictionary<string, string> levels, IEnumerable<Finding> findings)
    {
        if (findings.Any(f => f.Severity == "error" && f.Stage == "syntax")) levels["syntax"] = "failed";
        else levels["profile"] = "failed";
    }

    private static CliExecution OnboardingResult(string operation, int exitCode, IEnumerable<Finding> findings,
        string artifactState = "none", Dictionary<string, string>? levels = null, TemplateSnapshot? template = null,
        IEnumerable<OutputArtifact>? outputs = null)
    {
        Finding Safe(Finding finding)
        {
            static bool FieldId(string? value) => value is { Length: 70 } && value.StartsWith("field_", StringComparison.Ordinal)
                && value.AsSpan(6).ToArray().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
            string? field = finding.Field is null ? null : FieldId(finding.Field) ? finding.Field : HashTarget(finding.Field);
            string? key = FieldId(finding.Key) ? finding.Key : null;
            // Findings must originate in the owning Core/PDF APIs, never catalog issue-text arrays.
            // Preserve their fixed code, but do not print arbitrary messages, targets, keys or rule labels.
            return finding with
            {
                Code = CliCompatibilityProjection.IsKnownEngineCode(finding.Code) ? finding.Code
                    : finding.Severity == "warning" ? "W_CONFIGURATION_ASSESSMENT" : "E_CONFIGURATION_ASSESSMENT",
                Stage = finding.Stage == "bindingStructure" ? "binding"
                    : finding.Stage is "syntax" or "profile" or "data" or "binding" or "packet" or "render" or "publication" or "internal" ? finding.Stage : "internal",
                Severity = finding.Severity == "warning" ? "warning" : "error",
                Message = SafeOnboardingMessage(finding),
                Key = key, Field = field, RuleId = null
            };
        }
        var safe = findings.Select(Safe).ToArray();
        string status = exitCode switch { 0 => "ok", 1 => "validation_failed", 2 => "input_error", 3 => "unsupported", _ => "internal_error" };
        var result = new ResultDocument
        {
            Operation = operation, Status = status, ExitCode = exitCode, ArtifactState = artifactState,
            ValidationLevels = levels ?? OnboardingLevels(), Tool = Tool, Profiles = [],
            Templates = template is null ? [] : [new("template", template.Sha256)], Outputs = outputs?.ToArray() ?? [],
            Errors = safe.Where(f => f.Severity == "error").ToArray(), Warnings = safe.Where(f => f.Severity == "warning").ToArray(),
            Stats = new Dictionary<string, long>()
        };
        return new(exitCode, JsonSerializer.Serialize(result, StrictJson.Options), artifactState);
    }
    private static string? HashTarget(string target)
    { try { return CliDraftKeys.FromCanonicalField(target); } catch (ArgumentException) { return null; } }
    private static string SafeOnboardingMessage(Finding finding) => finding.Code switch
    {
        "E_ARGUMENTS" => "Check --help for required and supported command options.",
        "E_CONFIGURATION_REVIEW_REQUIRED" => "Review the draft and supply --reviewed before saving configuration.",
        "E_CONFIGURATION_TEMPLATE_REQUIRED" => "Save the exact template bytes with an independent template verifier before publishing configuration.",
        "E_CONFIGURATION_INPUT" or "E_CONFIGURATION_LAYOUT" => "Select catalog.json with sibling profile.json and template.pdf.",
        "E_CONFIGURATION_TEMPLATE_MISMATCH" => "The supplied PDF must match the configuration package template exactly.",
        "E_SAMPLE_CONTEXT_REQUIRED" => "A version 2 sample needs explicit synthetic filing context; provide input data or use a reviewed synthetic sample.",
        "E_SAMPLE_NO_CANDIDATE" or "E_SAMPLE_FIT" => "No bounded synthetic value satisfies this field's constraints; provide input data or revise the reviewed constraints.",
        "E_SAMPLE_RULE" or "E_SAMPLE_RULE_BUDGET" or "E_SAMPLE_BUDGET" or "E_SAMPLE_UNSATISFIED" => "The bounded generator cannot satisfy all constraints; provide input data explicitly.",
        "E_FIT_INTERNAL" or "E_FILL_INTERNAL" or "E_SAMPLE_INTERNAL" => "The internal fit check could not complete safely.",
        _ when finding.Code.StartsWith("E_FONT_", StringComparison.Ordinal) => "Review the indicated field's explicit font strategy and glyph repertoire; no fallback font was used.",
        _ when finding.Code.StartsWith("E_TEXT_", StringComparison.Ordinal) => "Shorten the indicated input or review its fixed font and geometry constraints; text was not shrunk or clipped.",
        _ when finding.Severity == "warning" => "Review the indicated compatibility limitation.",
        _ => "The operation did not satisfy the indicated input or configuration constraint."
    };

    private static CliExecution ExecuteCompatibilityCheck(CliOnboardingRequest request)
    {
        TemplateSnapshot? template = null; string state = request.Catalog is null ? "absent" : "invalid";
        try
        {
            var inspector = new PdfTemplateInspector();
            if (request.Catalog is null)
            {
                template = TemplateSnapshot.Load(request.Pdf!);
                return CompatibilityResult(CliCompatibilityProjection.Project(inspector.CheckCompatibility(template), state));
            }
            var package = CliCatalogPackage.Load(request.Catalog, request.Pdf); template = package.Template;
            byte[] catalog = package.CopyCatalog(), profile = package.CopyProfile();
            state = CliCompatibilityProjection.CatalogState(catalog);
            var parsedCatalog = FormCatalog.ParseCatalog(catalog);
            var parsedProfile = ProfileParser.ParseProfile(profile);
            if (!parsedCatalog.IsValid || !parsedProfile.IsValid)
            {
                var inputFindings = parsedCatalog.Findings.Concat(parsedProfile.Findings).ToArray();
                return CompatibilityFailure(FormCatalog.AssessmentExitCode(inputFindings), inputFindings, "invalid", template);
            }
            // Review is an admission gate, not permission to skip exact profile/hash/target checks.
            // Bind classifies genuine missing draft policies/review as 1 and inconsistency as 2.
            var configuration = FormCatalog.Bind(catalog, profile, template.Sha256, template.ByteCount);
            int bindingExit = FormCatalog.AssessmentExitCode(configuration.Findings);
            if (!configuration.IsValid && bindingExit != 1)
                return CompatibilityFailure(bindingExit, configuration.Findings, bindingExit == 2 ? "invalid" : state, template);
            if (state == "draft")
            {
                var unconfigured = inspector.CheckCompatibility(template);
                return CompatibilityResult(CliCompatibilityProjection.Project(unconfigured with
                {
                    ExitCode = unconfigured.ExitCode == 0 ? 1 : unconfigured.ExitCode,
                    Issues = unconfigured.Issues.Concat(configuration.Findings).ToArray()
                }, state));
            }
            if (!configuration.IsValid)
                return CompatibilityFailure(bindingExit, configuration.Findings, state, template);
            return CompatibilityResult(CliCompatibilityProjection.Project(inspector.CheckCompatibility(template, configuration.Value!), state));
        }
        catch (CliConfigurationInputException ex) { return CompatibilityFailure(2, [new(ex.Code, "syntax", ex.Code)], state, template); }
        catch (TemplateInputException ex) { return CompatibilityFailure(2, [new(ex.Code, "syntax", ex.Code)], state, template); }
        catch { return CompatibilityFailure(4, [new("E_CHECK_INTERNAL", "internal", "E_CHECK_INTERNAL")], state, template); }
    }

    private static CliExecution CompatibilityResult(CompatibilityDocument result) =>
        new(result.ExitCode, JsonSerializer.Serialize(result, StrictJson.Options));
    private static CliExecution CompatibilityFailure(int exitCode, IEnumerable<Finding> findings, string state, TemplateSnapshot? template = null) =>
        CompatibilityResult(CliCompatibilityProjection.Failure(exitCode, findings, state, template));

    private static CliExecution ExecuteCatalogValidate(CliOnboardingRequest request)
    {
        var levels = OnboardingLevels(); TemplateSnapshot? template = null;
        try
        {
            var package = CliCatalogPackage.Load(request.Catalog!, request.Pdf); template = package.Template;
            byte[] data = ReadJson(request.Data!, "data"); levels["syntax"] = "passed";
            var configuration = FormCatalog.Bind(package.CopyCatalog(), package.CopyProfile(), template.Sha256, template.ByteCount);
            if (!configuration.IsValid)
            {
                ConfigurationRejected(levels, configuration.Findings);
                return OnboardingResult("validate", FormCatalog.AssessmentExitCode(configuration.Findings), configuration.Findings, levels: levels, template: template);
            }
            levels["profile"] = "passed";
            var binding = new TemplateBinder().Bind(template, configuration.Value!);
            levels["binding"] = levels["bindingStructure"] = binding.Bound ? "passed" : "failed";
            if (!binding.Bound) return OnboardingResult("validate", FormCatalog.AssessmentExitCode(binding.Findings), binding.Findings, levels: levels, template: template);
            var parsed = DataValidator.ParseData(data);
            if (!parsed.IsValid)
            {
                levels["syntax"] = "failed";
                return OnboardingResult("validate", 2, parsed.Findings, levels: levels, template: template);
            }
            var validation = DataValidator.Validate(configuration.Value!.ParseProfile(), parsed.Value!);
            levels["data"] = validation.IsValid ? "passed" : "failed";
            return OnboardingResult("validate", validation.IsValid ? 0 : 1, binding.Findings.Concat(validation.Findings), levels: levels, template: template);
        }
        catch (CliConfigurationInputException ex) { return OnboardingResult("validate", 2, [new(ex.Code, "syntax", "Configuration input is unavailable.")], levels: levels, template: template); }
        catch (InputException ex) { return OnboardingResult("validate", 2, [new(ex.Code, "syntax", "Data input is unavailable.")], levels: levels, template: template); }
        catch (TemplateInputException ex) { return OnboardingResult("validate", 2, [new(ex.Code, "syntax", "PDF input is unavailable.")], levels: levels); }
        catch { return OnboardingResult("validate", 4, [new("E_VALIDATE_INTERNAL", "internal", "Validation could not complete safely.")], levels: levels, template: template); }
    }

    private static CliExecution ExecuteCatalogFill(CliOnboardingRequest request)
    {
        var levels = OnboardingLevels(); TemplateSnapshot? template = null; string state = "none";
        try
        {
            // Lexical/readiness preflight remains separate from authority and final create-new commit.
            string directory = Path.GetDirectoryName(Path.GetFullPath(request.Catalog!))!;
            string profilePath = Path.Combine(directory, "profile.json");
            var inputs = new[] { request.Pdf!, request.Catalog!, profilePath, Path.Combine(directory, "template.pdf"), request.Data! };
            var preflight = new SafePublisher().PreflightSingle(request.Output!, inputs);
            if (!preflight.IsReady) return OnboardingResult("fill", preflight.ExitCode, [new(preflight.ErrorCode ?? "E_PUBLICATION_PREFLIGHT", "syntax", "Output preflight refused.")]);
            var package = CliCatalogPackage.Load(request.Catalog!, request.Pdf); template = package.Template;
            byte[] data = ReadJson(request.Data!, "data"); levels["syntax"] = "passed";
            var configuration = FormCatalog.Bind(package.CopyCatalog(), package.CopyProfile(), template.Sha256, template.ByteCount);
            if (!configuration.IsValid)
            {
                ConfigurationRejected(levels, configuration.Findings);
                return OnboardingResult("fill", FormCatalog.AssessmentExitCode(configuration.Findings), configuration.Findings, levels: levels, template: template);
            }
            levels["profile"] = "passed";
            var prepared = VerifiedFillPipeline.Prepare(template, configuration.Value!, data, request.Flatten);
            foreach (var level in prepared.ValidationLevels) levels[level.Key] = level.Value;
            if (!prepared.ReadyToPublish) return OnboardingResult("fill", prepared.ExitCode,
                prepared.Findings, levels: levels, template: template);
            var published = VerifiedFillPipeline.PublishCreateNew(prepared, request.Output!, inputs);
            state = published.ArtifactState;
            var findings = prepared.Findings.ToList();
            if (!published.Success) findings.Add(new(published.ErrorCode ?? "E_PUBLICATION", "publication", "Publication did not complete."));
            if (published.CleanupIncomplete) findings.Add(new("W_OWNED_CLEANUP_INCOMPLETE", "publication", "Owned cleanup is incomplete.", Severity: "warning"));
            return OnboardingResult("fill", published.Success ? 0 : published.ExitCode, findings, state, levels, template,
                published.Outputs.Select(o => new OutputArtifact("document", "document.pdf", o.Bytes, o.Sha256)));
        }
        catch (CliConfigurationInputException ex) { return OnboardingResult("fill", 2, [new(ex.Code, "syntax", "Configuration input is unavailable.")], state, levels, template); }
        catch (InputException ex) { return OnboardingResult("fill", 2, [new(ex.Code, "syntax", "Data input is unavailable.")], state, levels, template); }
        catch (TemplateInputException ex) { return OnboardingResult("fill", 2, [new(ex.Code, "syntax", "PDF input is unavailable.")], state, levels); }
        catch { return OnboardingResult("fill", 4, [new("E_FILL_INTERNAL", "internal", "Fill could not complete safely.")], state, levels, template); }
    }

    private static CliExecution ExecuteSampleInput(CliOnboardingRequest request)
    {
        var levels = OnboardingLevels(); TemplateSnapshot? template = null; string state = "none";
        try
        {
            var package = CliCatalogPackage.Load(request.Catalog!); template = package.Template;
            var preflight = new SafePublisher().PreflightSingle(request.Output!, package.InputPaths);
            if (!preflight.IsReady) return OnboardingResult("sample-input", preflight.ExitCode, [new(preflight.ErrorCode ?? "E_PUBLICATION_PREFLIGHT", "syntax", "Output preflight refused.")]);
            levels["syntax"] = "passed";
            var bound = FormCatalog.Bind(package.CopyCatalog(), package.CopyProfile(), template.Sha256, template.ByteCount);
            if (!bound.IsValid)
            {
                ConfigurationRejected(levels, bound.Findings);
                return OnboardingResult("sample-input", FormCatalog.AssessmentExitCode(bound.Findings), bound.Findings, levels: levels, template: template);
            }
            var configuration = bound.Value!; levels["profile"] = "passed";
            var compatibility = new PdfTemplateInspector().CheckCompatibility(template, configuration);
            int exit = compatibility.ExitCode;
            if (exit != 0) return OnboardingResult("sample-input", exit, compatibility.Issues, levels: levels, template: template);
            levels["binding"] = levels["bindingStructure"] = "passed";
            var sample = CliSyntheticSample.Build(configuration.ParseProfile(), legacyContext: null,
                (key, value) => CliSyntheticSample.AcceptFit(VerifiedFillPipeline.CheckValueFit(template, configuration, key.Key, value)),
                data => VerifiedFillPipeline.CheckSampleFit(template, configuration, data.Span));
            if (!sample.Ready) return OnboardingResult("sample-input", sample.ExitCode, sample.Findings, levels: levels, template: template);
            byte[] bytes = sample.CopyData()!; string expectedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            bool Verify(Stream source)
            {
                using var copy = new MemoryStream(); var buffer = new byte[65_536]; int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                { if (copy.Length + count > Limits.MaxJsonBytes) return false; copy.Write(buffer, 0, count); }
                byte[] observed = copy.ToArray();
                return Convert.ToHexStringLower(SHA256.HashData(observed)) == expectedHash &&
                    !VerifiedFillPipeline.CheckSampleFit(template, configuration, observed).Any(f => f.Severity == "error");
            }
            var published = new SafePublisher().PublishSingle(new(request.Output!, package.InputPaths, new CandidateArtifact("data", "data.json", bytes, Verify)));
            state = published.ArtifactState;
            var findings = sample.Findings.Concat(compatibility.Issues).ToList();
            if (!published.Success) findings.Add(new(published.ErrorCode ?? "E_PUBLICATION", "publication", "Synthetic input publication did not complete."));
            if (published.CleanupIncomplete) findings.Add(new("W_OWNED_CLEANUP_INCOMPLETE", "publication", "Owned cleanup is incomplete.", Severity: "warning"));
            levels["data"] = "passed"; // Only data generation/validation ran; packet/render remain not_run.
            return OnboardingResult("sample-input", published.Success ? 0 : published.ExitCode, findings, state, levels, template,
                published.Outputs.Select(o => new OutputArtifact("data", "data.json", o.Bytes, o.Sha256)));
        }
        catch (CliConfigurationInputException ex) { return OnboardingResult("sample-input", 2, [new(ex.Code, "syntax", "Configuration input is unavailable.")], state, levels, template); }
        catch (TemplateInputException ex) { return OnboardingResult("sample-input", 2, [new(ex.Code, "syntax", "PDF input is unavailable.")], state, levels); }
        catch { return OnboardingResult("sample-input", 4, [new("E_SAMPLE_INTERNAL", "internal", "Synthetic input could not complete safely.")], state, levels, template); }
    }
}
