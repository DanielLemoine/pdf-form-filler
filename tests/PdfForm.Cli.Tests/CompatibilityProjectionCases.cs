using System.Text;
using System.Text.Json;
using PdfForm.Cli;
using PdfForm.Core;
using PdfForm.Pdf;

namespace PdfForm.Cli.Tests;

// Pure internal-report to public-DTO projection. No parser, filesystem, publication or native call.
public static class CompatibilityProjectionCases
{
    public static void Register(Action<string, Action> test)
    {
        test("public compatibility projects stable counts without internal field facts", () =>
        {
            var report = Report();
            var result = CliCompatibilityProjection.Project(report, "absent");
            Require(result.Counts["pages"] == 2 && result.Counts["terminalFields"] == 3 &&
                result.Counts["textFields"] == 1 && result.Counts["checkboxFields"] == 1 &&
                result.Counts["otherFields"] == 1 && result.Counts["widgets"] == 4 &&
                result.Counts["requiredFields"] == 1 && result.Counts["combFields"] == 1);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, StrictJson.Options));
            var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Require(names.SetEquals(new[] { "schema", "status", "exitCode", "templateSha256", "templateBytes", "counts",
                "configurationState", "issues", "missingEngineFeatures", "requiredConfiguration", "notAssessed" }));
            Require(!names.Contains("fields") && !names.Contains("pages") && !names.Contains("prefilled"));
        });
        test("public compatibility withholds arbitrary issue and capability text", () =>
        {
            var field = CliDraftKeys.FromCanonicalField("PRIVATE_SENTINEL");
            var report = Report() with
            {
                Status = "PRIVATE_SENTINEL",
                Fields = [new("PRIVATE_SENTINEL", "PRIVATE_SENTINEL", 1, 1, new double[] { 0, 0, 1, 1 }, 1, false, false)],
                Issues = [new("E_PRIVATE_SENTINEL", "PRIVATE_SENTINEL", "PRIVATE_SENTINEL", "PRIVATE_SENTINEL", "PRIVATE_SENTINEL", "PRIVATE_SENTINEL"),
                    new("E_CONFIG_FONT_IDENTITY", "binding", "PRIVATE_SENTINEL", Key: field)],
                MissingEngineFeatures = ["PRIVATE_SENTINEL", "font.resource"],
                RequiredConfiguration = ["PRIVATE_SENTINEL", "operatorReview"], NotAssessed = ["PRIVATE_SENTINEL"]
            };
            var result = CliCompatibilityProjection.Project(report, "draft");
            string json = JsonSerializer.Serialize(result, StrictJson.Options);
            Require(!json.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            Require(result.Issues.Any(i => i.Code == "E_CONFIG_FONT_IDENTITY" && i.FieldId == field && i.Capability == "font.source"));
            Require(result.Issues.Any(i => i.Code == "E_COMPATIBILITY_ASSESSMENT"));
            Require(result.MissingEngineFeatures.Contains("engine.unsupportedCapability") && result.NotAssessed.Contains("suppliedValueFit"));
        });
        test("public compatibility distinguishes absent draft and reviewed states", () =>
        {
            var observed = Report() with { ExitCode = 0, Status = "supported" };
            var absent = CliCompatibilityProjection.Project(observed, "absent");
            var draft = CliCompatibilityProjection.Project(observed, "draft");
            var reviewed = CliCompatibilityProjection.Project(observed, "reviewed");
            Require(absent.ConfigurationState == "absent" && reviewed.ConfigurationState == "reviewed" && reviewed.ExitCode == 0);
            Require(draft.ConfigurationState == "draft" && draft.ExitCode == 1 && draft.Status == "configuration_needed" &&
                draft.Issues.Any(i => i.Code == "E_CONFIGURATION_REVIEW_REQUIRED"));
            Require(CliCompatibilityProjection.CatalogState(Encoding.UTF8.GetBytes("{\"review\":{\"state\":\"draft\"}}")) == "draft");
            Require(CliCompatibilityProjection.CatalogState(Encoding.UTF8.GetBytes("{\"review\":{\"state\":\"reviewed\"}}")) == "reviewed");
            Require(CliCompatibilityProjection.CatalogState(Encoding.UTF8.GetBytes("{\"review\":{\"state\":\"PRIVATE_SENTINEL\"}}")) == "invalid");
        });
        test("public compatibility failure does not invent observed counts or template identity", () =>
        {
            var result = CliCompatibilityProjection.Failure(4, [new("E_CHECK_INTERNAL", "internal", "PRIVATE_SENTINEL")], "invalid");
            Require(result.Counts.Count == 0 && result.TemplateBytes is null && result.TemplateSha256 is null && result.Status == "internal_error");
            Require(!JsonSerializer.Serialize(result, StrictJson.Options).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
        });
        test("check argument failures use the public compatibility document before input access", () =>
        {
            var result = CliApplication.Execute(["check", "PRIVATE_SENTINEL.pdf", "--catalog"]);
            Require(result.ExitCode == 2 && !result.Text.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(result.Text);
            Require(json.RootElement.GetProperty("schema").GetString() == "pdfform.compatibility/1" &&
                json.RootElement.GetProperty("configurationState").GetString() == "invalid");
        });
        test("public compatibility issue cap preserves a late blocking condition", () =>
        {
            string blocked = CliDraftKeys.FromCanonicalField("late-blocking");
            var warnings = Enumerable.Range(0, 1100).Select(i => new Finding("W_FONT_NOT_EMBEDDED", "binding", "W_FONT_NOT_EMBEDDED",
                Key: CliDraftKeys.FromCanonicalField("warning-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Severity: "warning"));
            var source = Report() with { Status = "supported", ExitCode = 0,
                Issues = warnings.Append(new("E_CONFIG_FONT_IDENTITY", "binding", "E_CONFIG_FONT_IDENTITY", Key: blocked)).ToArray() };
            var result = CliCompatibilityProjection.Project(source, "reviewed");
            Require(result.ExitCode != 0 && result.Status != "supported" && result.Issues.Length == 1000);
            Require(result.Issues.Any(i => i.Code == "E_CONFIG_FONT_IDENTITY" && i.FieldId == blocked));
            Require(result.Issues.Any(i => i.Code == "E_COMPATIBILITY_ISSUE_LIMIT"));
            var failure = CliCompatibilityProjection.Failure(2, source.Issues, "invalid");
            Require(failure.ExitCode == 2 && failure.Issues.Length == 1000 && failure.Issues.Any(i => i.FieldId == blocked));
        });
        test("public compatibility warning cap does not invent a blocking failure", () =>
        {
            var source = Report() with { Status = "supported", ExitCode = 0,
                Issues = Enumerable.Range(0, 1100).Select(i => new Finding("W_FONT_NOT_EMBEDDED", "binding", "W_FONT_NOT_EMBEDDED",
                    Key: CliDraftKeys.FromCanonicalField("warning-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), Severity: "warning")).ToArray() };
            var result = CliCompatibilityProjection.Project(source, "reviewed");
            Require(result.ExitCode == 0 && result.Status == "supported" && result.Issues.Length == 1000);
            Require(result.Issues.All(i => i.Code.StartsWith("W_", StringComparison.Ordinal)) && result.Issues.Any(i => i.Code == "W_COMPATIBILITY_ISSUE_LIMIT"));
        });
        test("public compatibility uses only the frozen state enum and accepted choice capabilities", () =>
        {
            bool refused = false;
            try { _ = CliCompatibilityProjection.Failure(2, [], "unavailable"); }
            catch (ArgumentException) { refused = true; }
            Require(refused);
            var result = CliCompatibilityProjection.Project(Report() with { MissingEngineFeatures = ["field.combo", "field.list", "PRIVATE_SENTINEL"] }, "draft");
            Require(result.MissingEngineFeatures.Contains("field.combo") && result.MissingEngineFeatures.Contains("field.list"));
            Require(!JsonSerializer.Serialize(result, StrictJson.Options).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
        });
        test("check force refusal satisfies the exact pre-observation compatibility shape", () =>
        {
            var result = CliApplication.Execute(["check", "blank.pdf", "--force"]);
            Require(result.ExitCode == 2);
            byte[] wire = Encoding.UTF8.GetBytes(result.Text);
            var parsed = StrictJson.Parse<CompatibilityDocument>(wire, "compatibility");
            Require(parsed.IsValid && parsed.Value!.Status == "invalid" && parsed.Value.ConfigurationState == "invalid" &&
                parsed.Value.TemplateSha256 is null && parsed.Value.TemplateBytes is null && parsed.Value.Counts.Count == 0);
            using var json = JsonDocument.Parse(wire); var root = json.RootElement;
            Require(root.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(new[]
                { "schema", "status", "exitCode", "counts", "configurationState", "issues", "missingEngineFeatures", "requiredConfiguration", "notAssessed" }));
            Require(root.GetProperty("schema").GetString() == "pdfform.compatibility/1" && root.GetProperty("issues").GetArrayLength() is >= 1 and <= 1000);
            foreach (var issue in root.GetProperty("issues").EnumerateArray())
                Require(issue.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "code", "capability" }));
        });
        test("argument count and length refusals retain logical command schema with leading verbose", () =>
        {
            string[][] commands =
            [
                ["check", "PRIVATE_SENTINEL.pdf"],
                ["configure", "PRIVATE_SENTINEL.pdf", "--out", "PRIVATE_SENTINEL-draft"],
                ["configure", "PRIVATE_SENTINEL.pdf", "--out", "PRIVATE_SENTINEL-draft", "--force"],
                ["save-config", "PRIVATE_SENTINEL.pdf", "--draft", "PRIVATE_SENTINEL-draft", "--out", "PRIVATE_SENTINEL-package", "--reviewed"],
                ["save-config", "PRIVATE_SENTINEL.pdf", "--draft", "PRIVATE_SENTINEL-draft", "--out", "PRIVATE_SENTINEL-package"],
                ["sample-input", "--catalog", "PRIVATE_SENTINEL/catalog.json", "--out", "PRIVATE_SENTINEL-data.json"],
                ["fill", "PRIVATE_SENTINEL.pdf", "--catalog", "PRIVATE_SENTINEL/catalog.json", "--data", "PRIVATE_SENTINEL-data.json", "--out", "PRIVATE_SENTINEL-new.pdf"],
                ["validate", "--catalog", "PRIVATE_SENTINEL/catalog.json", "--data", "PRIVATE_SENTINEL-data.json"]
            ];
            foreach (var command in commands)
            {
                string[] byCount = command.Concat(Enumerable.Repeat("--verbose", 33 - command.Length)).ToArray();
                string[] byLength = command.ToArray();
                byLength[Array.FindIndex(byLength, a => a.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal))] = "PRIVATE_SENTINEL" + new string('x', 4097);
                string[] leadingCount = new[] { "--verbose" }.Concat(command).Concat(Enumerable.Repeat("--verbose", 32 - command.Length)).ToArray();
                string[] leadingLength = new[] { "--verbose" }.Concat(byLength).ToArray();
                foreach (var arguments in new[] { byCount, byLength, leadingCount, leadingLength })
                {
                    var execution = CliApplication.Execute(arguments);
                    Require(execution.ExitCode == 2 && !execution.Text.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
                    byte[] wire = Encoding.UTF8.GetBytes(execution.Text);
                    using var json = JsonDocument.Parse(wire); var root = json.RootElement;
                    if (command[0] == "check")
                    {
                        var parsed = StrictJson.Parse<CompatibilityDocument>(wire, "compatibility");
                        Require(parsed.IsValid && parsed.Value!.Status == "invalid" && parsed.Value.ConfigurationState == "invalid" &&
                            parsed.Value.TemplateSha256 is null && parsed.Value.TemplateBytes is null && parsed.Value.Issues.Any(i => i.Code == "E_ARGUMENTS"));
                        Require(root.GetProperty("schema").GetString() == "pdfform.compatibility/1");
                    }
                    else if (command[0] is "configure" or "save-config")
                    {
                        var parsed = StrictJson.Parse<ConfigurationResultDocument>(wire, "configuration");
                        Require(parsed.IsValid && parsed.Value!.Status == "input_error" && parsed.Value.Operation == command[0] &&
                            parsed.Value.Template is null && parsed.Value.Profile is null && parsed.Value.Errors.Any(e => e.Code == "E_ARGUMENTS"));
                        Require(root.GetProperty("schema").GetString() == "pdfform.configuration-result/1");
                    }
                    else
                    {
                        var parsed = StrictJson.Parse<ResultDocument>(wire, "result");
                        Require(parsed.IsValid && parsed.Value!.Status == "input_error" && parsed.Value.Operation == command[0]);
                        Require(root.GetProperty("schema").GetString() == "pdfform.result/2");
                    }
                }
            }
            var legacy = CliApplication.Execute(["validate", "--profile", new string('x', 4097), "--data", "data.json"]);
            using var unchanged = JsonDocument.Parse(legacy.Text);
            Require(legacy.ExitCode == 2 && unchanged.RootElement.GetProperty("schema").GetString() == "pdfform.result/2");
        });
    }

    private static ConfigurationCompatibility Report() => new("pdfform.compatibility/1", "configuration_needed", 1,
        new string('a', 64), 123, 2, false,
        [new("field_" + new string('b', 64), "text", 1, 1, new double[] { 1, 2, 3, 4 }, 10, true, true),
         new("field_" + new string('c', 64), "checkbox", 1, 1, new double[] { 1, 2, 3, 4 }, null, false, false),
         new("field_" + new string('d', 64), "radio", 2, null, [], null, false, false)],
        [], ["field.radio"], ["exactCatalogProfileBinding", "renderPolicy", "operatorReview"], ["suppliedValueFit"]);
    private static void Require(bool condition)
    { if (!condition) throw new InvalidOperationException("Compatibility projection source regression."); }
}
