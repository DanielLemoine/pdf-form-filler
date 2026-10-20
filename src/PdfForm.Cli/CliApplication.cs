using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PdfForm.Core;
using PdfForm.Pdf;

namespace PdfForm.Cli;

/// <summary>Value-free command adapters. PDF mutation is confined to the verified fill boundary.</summary>
public static partial class CliApplication
{
    static CliApplication()
    {
        // Untrusted PDF parser logs can contain document strings. Only our safe findings are diagnostics.
        iText.Commons.ITextLogManager.SetLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
    }
    private static readonly ToolIdentity Tool = new("pdfform", "0.1.0-dev", "development-local", "iText 9.8.0", RuntimeInformation.FrameworkDescription, "unpublished-source");
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        CliExecution? execution = null;
        try
        {
            execution = Execute(args);
            stdout.WriteLine(execution.Text);
            return execution.ExitCode;
        }
        catch
        {
            // A diagnostic writer failure must not recurse through another arbitrary destination.
            string code = execution?.ArtifactState switch
            {
                "published" => "E_DIAGNOSTIC_WRITE artifactState=published",
                "staged" => "E_DIAGNOSTIC_WRITE artifactState=staged",
                _ => "E_DIAGNOSTIC_WRITE"
            };
            try { stderr.WriteLine(code); } catch { }
            return 4;
        }
    }

    public static CliExecution Execute(string[] arguments)
    {
        string operation = "unknown";
        var findings = new List<Finding>();
        var profiles = new List<ProfileIdentity>();
        var templates = new List<TemplateIdentity>();
        var levels = new Dictionary<string, string> { ["syntax"] = "not_run", ["profile"] = "not_run", ["data"] = "not_run", ["binding"] = "not_run", ["packet"] = "not_run" };
        var stats = new Dictionary<string, long>();
        bool reviewedMetadata = false;
        CliExecution Result(int exit, string status)
        {
            var result = new ResultDocument
            {
                Operation = operation, Status = status, ExitCode = exit, ArtifactState = "none", ValidationLevels = levels,
                Tool = Tool, Profiles = profiles.ToArray(), Templates = templates.ToArray(), Outputs = [],
                Errors = findings.Where(f => f.Severity == "error").Select(SafeFinding).ToArray(), Warnings = findings.Where(f => f.Severity == "warning").Select(SafeFinding).ToArray(), Stats = stats
            };
            return new(exit, JsonSerializer.Serialize(result, StrictJson.Options));
        }
        Finding SafeFinding(Finding finding) => reviewedMetadata ? finding : finding with { Key = null, Field = null, RuleId = null };
        void Error(string code, string stage, string message) => findings.Add(new(code, stage, message));
        try
        {
            if (arguments.Length == 0 || arguments.SequenceEqual(new[] { "--help" })) return new(0, Help);
            if (arguments.SequenceEqual(new[] { "--version" })) return new(0, JsonSerializer.Serialize(Tool, StrictJson.Options));
            if (arguments.Length > 32 || arguments.Any(a => a.Length > 4096))
            {
                var logicalArguments = arguments.Where(a => a != "--verbose").ToArray();
                var selected = CliOnboardingArguments.Parse(logicalArguments);
                if (selected.Handled) return OnboardingArgumentFailure(logicalArguments[0],
                    new(true, null, 2, "invalid", new("E_ARGUMENTS", "syntax", "Argument count or length limit exceeded.")));
                Error("E_ARGUMENTS", "syntax", "Argument count or length limit exceeded."); return Result(2, "input_error");
            }
            var args = arguments.Where(a => a != "--verbose").ToArray();
            if (args.Length == 0) return new(0, Help);
            var onboarding = CliOnboardingArguments.Parse(args);
            if (onboarding.Handled && onboarding.Request is null) return OnboardingArgumentFailure(args[0], onboarding);
            if (onboarding.Request is { Operation: not CliOnboardingOperation.CatalogFill } request)
            {
                var routed = TryExecuteOnboarding(request);
                if (routed is not null) return routed;
            }
            operation = args[0] is "validate" or "inspect" or "check-profile" or "fill" or "run" or "read" or "diff" or "describe" or "template" ? args[0] : "unknown";
            if (operation is not ("validate" or "inspect" or "check-profile" or "fill"))
            { Error("E_COMMAND_UNSUPPORTED", "syntax", "This command is not implemented by this development slice."); return Result(2, "input_error"); }
            if (args.Skip(1).Any(a => a is "--result" or "--json" or "--md"))
            { Error("E_OUTPUT_UNSUPPORTED", "syntax", "Diagnostic file publication is not enabled in this development slice."); return Result(2, "input_error"); }
            if (operation == "fill") return ExecuteFill(args);
            string? profilePath = null, dataPath = null, pdfPath = null;
            if (operation == "inspect")
            {
                if (args.Length != 2 || args[1].StartsWith("--", StringComparison.Ordinal)) { Error("E_ARGUMENTS", "syntax", "Inspect requires exactly one PDF input."); return Result(2, "input_error"); }
                pdfPath = args[1];
            }
            else if (operation == "check-profile")
            {
                if (args.Length != 3 || args.Skip(1).Any(x => x.StartsWith("--", StringComparison.Ordinal))) { Error("E_ARGUMENTS", "syntax", "Check-profile requires PDF and profile inputs."); return Result(2, "input_error"); }
                pdfPath = args[1]; profilePath = args[2];
            }
            else
            {
                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 1; i < args.Length; i += 2)
                    if (i + 1 >= args.Length || args[i] is not ("--profile" or "--data" or "--pdf") || !options.TryAdd(args[i], args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    { Error("E_ARGUMENTS", "syntax", "Validate options are missing, duplicated or unsupported."); return Result(2, "input_error"); }
                if (!options.TryGetValue("--profile", out profilePath) || !options.TryGetValue("--data", out dataPath))
                { Error("E_ARGUMENTS", "syntax", "Validate requires profile and data inputs."); return Result(2, "input_error"); }
                options.TryGetValue("--pdf", out pdfPath);
            }
            byte[]? profileBytes = null, dataBytes = null;
            InputException? profileReadFailure = null;
            TemplateSnapshot? snapshot = null;
            // Establish every input snapshot first: inaccessible input takes precedence over profile/data findings.
            if (profilePath is not null)
            {
                try { profileBytes = ReadJson(profilePath, "profile"); }
                catch (InputException ex) when (ex.ExitCode == 3) { profileReadFailure = ex; }
            }
            if (dataPath is not null) dataBytes = ReadJson(dataPath, "data");
            if (pdfPath is not null) snapshot = TemplateSnapshot.Load(pdfPath);
            if (snapshot is not null) templates.Add(new("template", snapshot.Sha256));
            if (operation == "inspect")
            {
                var inspection = new PdfTemplateInspector().Inspect(snapshot!);
                int exit = inspection.Findings.Any(f => f.Severity == "error") ? 3 : 0;
                return new(exit, JsonSerializer.Serialize(inspection, StrictJson.Options));
            }
            var parsedData = dataBytes is null ? null : DataValidator.ParseData(dataBytes);
            var parsedProfile = profileReadFailure is null ? ProfileParser.ParseProfile(profileBytes!)
                : new ParseResult<ProfileDefinition>(null, [new(profileReadFailure.Code, "profile", "Profile exceeds the declared resource limit.")]);
            if (parsedData is { IsValid: false })
            {
                findings.AddRange(parsedData.Findings); findings.AddRange(parsedProfile.Findings); levels["syntax"] = "failed";
                return Result(2, "input_error");
            }
            levels["syntax"] = "passed";
            if (!parsedProfile.IsValid) { findings.AddRange(parsedProfile.Findings); levels["profile"] = "failed"; return Result(3, "profile_invalid"); }
            var profile = parsedProfile.Value!;
            var authority = ProductionProfileRegistry.Check(profileBytes!, profile.Template.Sha256Allowlist[0]);
            reviewedMetadata = authority.ExactCandidateMatch;
            if (authority.ExactCandidateMatch)
                profiles.Add(new(profile.Id, profile.Revision, profile.ProfileVersion, Convert.ToHexStringLower(SHA256.HashData(profileBytes!))));
            if (!authority.ProductionTrusted)
                findings.Add(new("W_AUTHORING_VALIDATION_ONLY", "profile", "Authoring validation does not grant production profile approval; unregistered profile identifiers are withheld.", Severity: "warning"));
            levels["profile"] = "passed";
            stats["keysDeclared"] = profile.Keys.Length; stats["targetsDeclared"] = profile.EnumerateTargets().Count();
            ValidationReport? validated = null;
            if (parsedData is not null)
            {
                validated = DataValidator.Validate(profile, parsedData.Value!); findings.AddRange(validated.Findings);
                levels["data"] = validated.IsValid ? "passed" : "failed";
                stats["rulesSkipped"] = validated.Rules.Count(r => r.State.StartsWith("skipped_", StringComparison.Ordinal));
            }
            if (snapshot is not null)
            {
                var binding = new TemplateBinder().Bind(snapshot, profile); findings.AddRange(binding.Findings);
                levels["bindingStructure"] = binding.Bound ? "passed" : "failed";
                stats["terminalFieldsInspected"] = binding.Inspection.TerminalFieldCount;
                stats["widgetsInspected"] = binding.Inspection.WidgetCount;
                if (!binding.Bound)
                {
                    bool mismatch = binding.Findings.Any(f => f.Code.StartsWith("E_TEMPLATE_", StringComparison.Ordinal) || f.Code == "E_REVIEWED_STRUCTURE");
                    return Result(3, mismatch ? "mismatch" : "unsupported");
                }
                findings.Add(new("W_RENDER_CHECKS_NOT_RUN", "binding", "Static binding does not assess supplied glyph coverage, text fit, output appearances or viewer acceptance.", Severity: "warning"));
            }
            if (validated is { IsValid: false }) return Result(1, "validation_failed");
            return Result(0, "ok");
        }
        catch (InputException ex) { levels[ex.ExitCode == 3 ? "profile" : "syntax"] = "failed"; Error(ex.Code, ex.ExitCode == 3 ? "profile" : "syntax", "An input could not be read within the declared resource limits."); return Result(ex.ExitCode, ex.ExitCode == 3 ? "profile_invalid" : "input_error"); }
        catch (TemplateInputException ex) { Error(ex.Code, "syntax", "PDF input could not be read within the declared resource limits."); return Result(2, "input_error"); }
        catch { Error("E_INTERNAL", "internal", "Operation failed safely without publishing an artifact."); return Result(4, "internal_error"); }
    }

    private static byte[] ReadJson(string path, string role)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > Limits.MaxJsonBytes) throw new InputException("E_JSON_SIZE", role == "profile" ? 3 : 2);
            using var copy = new MemoryStream((int)file.Length);
            var buffer = new byte[64 * 1024]; int count;
            while ((count = file.Read(buffer, 0, buffer.Length)) > 0)
            { if (copy.Length + count > Limits.MaxJsonBytes) throw new InputException("E_JSON_SIZE", role == "profile" ? 3 : 2); copy.Write(buffer, 0, count); }
            return copy.ToArray();
        }
        catch (InputException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new InputException("E_INPUT_FILE"); }
    }
    private sealed class InputException(string code, int exitCode = 2) : Exception(code) { public string Code { get; } = code; public int ExitCode { get; } = exitCode; }
    private const string Help = """
        pdfform development slice

        inspect <pdf>
        check <pdf> [--catalog <catalog.json>]
        configure <pdf> --out <new-draft-directory>
        save-config <pdf> --draft <draft-directory> --out <new-package-directory> --reviewed
        check-profile <pdf> <profile>
        validate --profile <profile> --data <data> [--pdf <pdf>]
        validate --catalog <catalog.json> --data <data> [--pdf <pdf>]
        fill <pdf> --profile <profile> --data <data> --out <new-pdf> [--flatten]
        fill <pdf> --catalog <catalog.json> --data <data> --out <new-pdf> [--flatten]
        sample-input --catalog <catalog.json> --out <new-data.json>
        --help | --version

        JSON diagnostics are written to stdout. --verbose does not expose field values.
        PDF-free validation reports binding not_run. Static inspection is not appearance or filing acceptance.
        Fill is bounded private development; --force, run, read, diff, describe, template and diagnostic file-output flags are not yet enabled.
        """;
}
public sealed record CliExecution(int ExitCode, string Text, string ArtifactState = "none");
