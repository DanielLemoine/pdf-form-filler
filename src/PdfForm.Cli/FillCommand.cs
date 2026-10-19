using System.Text.Json;
using PdfForm.Core;
using PdfForm.Pdf;
using PdfForm.Publication;

namespace PdfForm.Cli;

public static partial class CliApplication
{
    private static CliExecution ExecuteFill(string[] args)
    {
        var findings = new List<Finding>();
        var levels = new Dictionary<string, string> { ["syntax"] = "not_run", ["profile"] = "not_run", ["data"] = "not_run", ["binding"] = "not_run", ["packet"] = "not_run" };
        var profiles = new List<ProfileIdentity>();
        var templates = new List<TemplateIdentity>();
        var stats = new Dictionary<string, long>();
        OutputArtifact[] outputs = [];
        string artifactState = "none";
        bool reviewedMetadata = false;
        CliExecution Result(int exit, string status)
        {
            Finding Safe(Finding f) => reviewedMetadata ? f : f with { Key = null, Field = null, RuleId = null };
            return new(exit, JsonSerializer.Serialize(new ResultDocument
            {
                Operation = "fill", Status = status, ExitCode = exit, ArtifactState = artifactState,
                ValidationLevels = levels, Tool = Tool, Profiles = profiles.ToArray(), Templates = templates.ToArray(),
                Outputs = outputs, Errors = findings.Where(f => f.Severity == "error").Select(Safe).ToArray(),
                Warnings = findings.Where(f => f.Severity == "warning").Select(Safe).ToArray(), Stats = stats
            }, StrictJson.Options), artifactState);
        }
        CliExecution InputError(string code, string message)
        { levels["syntax"] = "failed"; findings.Add(new(code, "syntax", message)); return Result(2, "input_error"); }
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                return InputError("E_ARGUMENTS", "Fill requires one PDF input and explicit profile, data and new output paths.");
            if (args.Contains("--force", StringComparer.Ordinal))
            {
                findings.Add(new("E_FORCE_UNQUALIFIED", "publication", "Replacement publication is not qualified by this development build."));
                return Result(3, "unsupported");
            }
            bool flatten = false;
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--flatten")
                {
                    if (flatten) return InputError("E_ARGUMENTS", "Fill options are duplicated or unsupported.");
                    flatten = true; continue;
                }
                if (args[i] is not ("--profile" or "--data" or "--out") || i + 1 >= args.Length ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(args[i], args[++i]))
                    return InputError("E_ARGUMENTS", "Fill options are missing, duplicated or unsupported.");
            }
            if (!options.TryGetValue("--profile", out var profilePath) || !options.TryGetValue("--data", out var dataPath) ||
                !options.TryGetValue("--out", out var destination))
                return InputError("E_ARGUMENTS", "Fill requires explicit profile, data and new output paths.");

            var preflight = new SafePublisher().PreflightSingle(destination, [args[1], profilePath, dataPath]);
            if (!preflight.IsReady)
            {
                findings.Add(new(preflight.ErrorCode ?? "E_PUBLICATION_PREFLIGHT", "syntax", "Input and output paths do not satisfy the create-new publication contract."));
                levels["syntax"] = "failed";
                return Result(preflight.ExitCode, preflight.ExitCode == 2 ? "input_error" : preflight.ExitCode == 3 ? "unsupported" : "internal_error");
            }

            byte[]? profileBytes = null;
            InputException? profileReadFailure = null;
            try { profileBytes = ReadJson(profilePath, "profile"); }
            catch (InputException ex) when (ex.ExitCode == 3) { profileReadFailure = ex; }
            var dataBytes = ReadJson(dataPath, "data");
            var template = TemplateSnapshot.Load(args[1]);
            templates.Add(new("template", template.Sha256));

            var parsedData = DataValidator.ParseData(dataBytes);
            var parsedProfile = profileReadFailure is null ? ProfileParser.ParseProfile(profileBytes!)
                : new ParseResult<ProfileDefinition>(null, [new(profileReadFailure.Code, "profile", "Profile exceeds the declared resource limit.")]);
            if (!parsedData.IsValid)
            {
                findings.AddRange(parsedData.Findings); findings.AddRange(parsedProfile.Findings);
                levels["syntax"] = "failed"; return Result(2, "input_error");
            }
            levels["syntax"] = "passed";
            if (!parsedProfile.IsValid)
            { findings.AddRange(parsedProfile.Findings); levels["profile"] = "failed"; return Result(3, "profile_invalid"); }
            var admitted = ProductionProfileRegistry.Bind(profileBytes!, template.Sha256);
            if (!admitted.IsValid)
            { findings.AddRange(admitted.Findings); levels["profile"] = "failed"; return Result(3, "mismatch"); }
            reviewedMetadata = true;
            var identity = admitted.Value!.Identity;
            profiles.Add(new(identity.Id, identity.Revision, identity.ProfileVersion, identity.ProfileSha256));
            levels["profile"] = "passed";

            var prepared = VerifiedFillPipeline.Prepare(template, admitted.Value, dataBytes, flatten);
            findings.AddRange(prepared.Findings);
            foreach (var level in prepared.ValidationLevels) levels[level.Key] = level.Value;
            foreach (var stat in prepared.Stats) stats[stat.Key] = stat.Value;
            if (!prepared.ReadyToPublish) return Result(prepared.ExitCode, prepared.Status);

            var published = VerifiedFillPipeline.PublishCreateNew(prepared, destination, [args[1], profilePath, dataPath]);
            artifactState = published.ArtifactState;
            // Standalone diagnostics expose a logical label, never the caller's possibly sensitive destination basename.
            outputs = published.Outputs.Select(o => new OutputArtifact("document", "document.pdf", o.Bytes, o.Sha256)).ToArray();
            if (published.CleanupIncomplete)
                findings.Add(new("W_OWNED_CLEANUP_INCOMPLETE", "publication", "Owned staging cleanup is incomplete; final artifact state is reported separately.", Severity: "warning"));
            if (!published.Success)
            {
                findings.Add(new(published.ErrorCode ?? "E_PUBLICATION", "publication", "Publication did not complete under its declared artifact contract."));
                return Result(published.ExitCode, published.ExitCode == 2 ? "input_error" : published.ExitCode == 3 ? "unsupported" : "internal_error");
            }
            return Result(0, "ok");
        }
        catch (InputException ex)
        {
            levels[ex.ExitCode == 3 ? "profile" : "syntax"] = "failed";
            findings.Add(new(ex.Code, ex.ExitCode == 3 ? "profile" : "syntax", "An input could not be read within the declared resource limits."));
            return Result(ex.ExitCode, ex.ExitCode == 3 ? "profile_invalid" : "input_error");
        }
        catch (TemplateInputException ex) { return InputError(ex.Code, "PDF input could not be read within the declared resource limits."); }
        catch
        {
            findings.Add(new("E_INTERNAL", "internal", "Fill failed; the reported artifact state identifies any completed publication."));
            return Result(4, "internal_error");
        }
    }
}
