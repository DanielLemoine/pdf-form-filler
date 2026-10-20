using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfForm.Cli;
using PdfForm.Core;
using PdfForm.Pdf;
using PdfForm.Publication;

namespace PdfForm.Cli.Tests;

// Synthetic DTO/byte tests only. No PDF parser, publication, native API or real filesystem.
public static class ConfigurationArtifactCases
{
    public static void Register(Action<string, Action> test)
    {
        const string draftReviewState = "draft";
        test("configuration candidate uses structural keys without business meaning", () =>
        {
            var candidate = Candidate(draftReviewState);
            var profile = ProfileParser.ParseProfile(candidate.CopyProfile()).Value!;
            Require(profile.Schema == "pdfform.profile/3" && profile.Keys.All(k => k.Kind is "text" or "checkbox"));
            Require(profile.Keys.All(k => k.Key.StartsWith("field_", StringComparison.Ordinal) && k.Label is null && k.Sensitive));
            Require(profile.Keys.Single(k => k.Kind == "text").Key == CliDraftKeys.FromCanonicalField("PRIVATE_AMOUNT_DATE_NAME"));
            Require(profile.Rules.Length == 0 && profile.Template.Title == "" && profile.Title == "Configured form");
            byte[] copy = candidate.CopyCatalog(); copy[0] ^= 1;
            Require(!copy.AsSpan().SequenceEqual(candidate.CopyCatalog()));
        });
        test("configuration candidate pins source font identity and versioned checkbox marks", () =>
        {
            foreach (bool embedded in new[] { false, true })
            {
                var candidate = CliConfigurationDraft.Create(Facts(embedded: embedded), draftReviewState);
                Require(candidate.IsValid);
                using var catalog = JsonDocument.Parse(candidate.Value!.CopyCatalog());
                var policy = catalog.RootElement.GetProperty("policy");
                var text = policy.GetProperty("textFields")[0];
                Require(text.GetProperty("strategy").GetString() == (embedded ? "preserve-embedded-simple-v1" : "bundled-liberation-serif-ascii-v1"));
                Require(text.GetProperty("fontSize").GetDouble() == 10 && text.GetProperty("metricsSha256").GetString() == new string('b', 64));
                Require(text.TryGetProperty("programSha256", out _) == embedded);
                Require(policy.GetProperty("checkboxFields")[0].GetProperty("mark").GetString() == "cross-10-v1");
            }
            var circle = CliConfigurationDraft.Create(Facts(checkboxSize: 12), draftReviewState);
            Require(circle.IsValid);
            using var circleJson = JsonDocument.Parse(circle.Value!.CopyCatalog());
            Require(circleJson.RootElement.GetProperty("policy").GetProperty("checkboxFields")[0].GetProperty("mark").GetString() == "circle-12-v1");
        });
        test("configuration candidate refuses automatic review and ambiguous checkbox export", () =>
        {
            Require(!CliConfigurationDraft.Create(Facts(), "reviewed").IsValid);
            var facts = Facts();
            var targets = facts.Targets.Select(t => t.Type == "checkbox" ? t with { OnStates = new[] { "Yes", "Other" } } : t).ToArray();
            var refused = CliConfigurationDraft.Create(facts with { Targets = targets }, draftReviewState);
            Require(!refused.IsValid && refused.Findings.Any(f => f.Code == "E_CONFIGURATION_ON_STATE"));
        });
        test("configuration candidate uses observed absent encoding without substitution", () =>
        {
            var facts = Facts();
            var absent = facts with { Targets = facts.Targets.Select(t => t.Font is { } font ? t with { Font = font with { Encoding = "absent" } } : t).ToArray() };
            var accepted = CliConfigurationDraft.Create(absent);
            Require(accepted.IsValid);
            using var json = JsonDocument.Parse(accepted.Value!.CopyCatalog());
            Require(json.RootElement.GetProperty("policy").GetProperty("textFields")[0].GetProperty("encoding").GetString() == "absent");
            var old = absent with { Targets = absent.Targets.Select(t => t.Font is { } font ? t with { Font = font with { Encoding = "" } } : t).ToArray() };
            Require(!CliConfigurationDraft.Create(old).IsValid);
        });
        test("configuration review binds edited profile bytes and refuses changed template", () =>
        {
            var candidate = Candidate(draftReviewState);
            var edited = candidate.CopyProfile().Concat(new byte[] { (byte)' ' }).ToArray();
            var reviewed = CliConfigurationDraft.Review(candidate.CopyCatalog(), edited, Template());
            Require(reviewed.IsValid && reviewed.Value!.CopyProfile().AsSpan().SequenceEqual(edited));
            using var json = JsonDocument.Parse(reviewed.Value!.CopyCatalog());
            Require(json.RootElement.GetProperty("review").GetProperty("state").GetString() == "reviewed");
            Require(json.RootElement.GetProperty("profile").GetProperty("sha256").GetString() == Hash(edited));
            var wrongTemplate = TemplateSnapshot.FromBytes(Encoding.UTF8.GetBytes("different synthetic bytes"));
            Require(!CliConfigurationDraft.Review(candidate.CopyCatalog(), edited, wrongTemplate).IsValid);
        });
        test("configuration review refuses unresolved or duplicate data without relaying private text", () =>
        {
            var candidate = Candidate(draftReviewState);
            var node = JsonNode.Parse(candidate.CopyCatalog())!;
            node["review"]!["unresolved"] = new JsonArray("PRIVATE_SENTINEL");
            var unresolved = CliConfigurationDraft.Review(JsonSerializer.SerializeToUtf8Bytes(node, StrictJson.Options), candidate.CopyProfile(), Template());
            Require(!unresolved.IsValid && !JsonSerializer.Serialize(unresolved.Findings).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            byte[] duplicate = Encoding.UTF8.GetBytes("{\"schema\":\"PRIVATE_SENTINEL\",\"schema\":\"pdfform.catalog/1\"}");
            var invalid = CliConfigurationDraft.Review(duplicate, candidate.CopyProfile(), Template());
            Require(!invalid.IsValid && !JsonSerializer.Serialize(invalid.Findings).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
        });
        test("configuration receipt has exact singular identities and never render evidence", () =>
        {
            var candidate = Candidate(draftReviewState); byte[] profile = candidate.CopyProfile(); var template = Template();
            ArtifactDescriptor[] outputs = [new("catalog", "catalog.json", candidate.CopyCatalog().Length, Hash(candidate.CopyCatalog())),
                new("profile", "profile.json", profile.Length, Hash(profile))];
            byte[] receipt = CliConfigurationReceipt.Published("configure", template, profile, outputs,
                [new("W_SYNTHETIC", "binding", "PRIVATE_SENTINEL", Field: "PRIVATE_SENTINEL", Severity: "warning")], structurallyBound: false);
            string text = Encoding.UTF8.GetString(receipt); Require(!text.Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(receipt); var root = json.RootElement;
            Require(root.GetProperty("schema").GetString() == "pdfform.configuration-result/1");
            Require(!root.TryGetProperty("tool", out _) && !root.TryGetProperty("profiles", out _) && !root.TryGetProperty("requestId", out _));
            Require(root.GetProperty("template").GetProperty("bytes").GetInt32() == template.ByteCount);
            Require(root.GetProperty("profile").GetProperty("path").GetString() == "profile.json");
            foreach (string stage in new[] { "binding", "bindingStructure" })
                Require(root.GetProperty("validationLevels").GetProperty(stage).GetString() == "not_run");
            foreach (string stage in new[] { "data", "packet", "render" })
                Require(root.GetProperty("validationLevels").GetProperty(stage).GetString() == "not_run");
        });
        test("save-config success receipt requires completed structural assessment", () =>
        {
            var candidate = Candidate(draftReviewState); byte[] profile = candidate.CopyProfile(); var template = Template();
            ArtifactDescriptor[] outputs = [new("catalog", "catalog.json", candidate.CopyCatalog().Length, Hash(candidate.CopyCatalog())),
                new("profile", "profile.json", profile.Length, Hash(profile)), new("template", "template.pdf", template.ByteCount, template.Sha256)];
            bool refused = false;
            try { _ = CliConfigurationReceipt.Published("save-config", template, profile, outputs, [], structurallyBound: false); }
            catch (ArgumentException ex) { refused = ex.Message == "E_CONFIGURATION_RESULT_CONTRACT"; }
            Require(refused);
            refused = false;
            try { _ = CliConfigurationReceipt.Terminal("save-config", 0, "published", template, profile, outputs, [],
                ConfigurationPublicationStages.Create("passed", "passed", "not_run", "not_run")); }
            catch (ArgumentException ex) { refused = ex.Message == "E_CONFIGURATION_RESULT_CONTRACT"; }
            Require(refused);
            byte[] receipt = CliConfigurationReceipt.Published("save-config", template, profile, outputs, [], structurallyBound: true);
            using var json = JsonDocument.Parse(receipt); var root = json.RootElement;
            foreach (string stage in new[] { "syntax", "profile", "binding", "bindingStructure" })
                Require(root.GetProperty("validationLevels").GetProperty(stage).GetString() == "passed");
            foreach (string stage in new[] { "data", "packet", "render" })
                Require(root.GetProperty("validationLevels").GetProperty(stage).GetString() == "not_run");
            Require(root.GetProperty("template").GetProperty("path").GetString() == "template.pdf" &&
                root.GetProperty("profile").GetProperty("path").GetString() == "profile.json");
        });
        test("configuration unknown receipt withholds outputs even when caller supplied descriptors", () =>
        {
            byte[] receipt = CliConfigurationReceipt.Terminal("save-config", 4, "unknown", null, null,
                [new("PRIVATE_SENTINEL", "PRIVATE_SENTINEL", 1, new string('a', 64))], [],
                ConfigurationPublicationStages.Create("passed", "passed", "passed", "passed"));
            Require(!Encoding.UTF8.GetString(receipt).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(receipt);
            Require(json.RootElement.GetProperty("outputs").GetArrayLength() == 0 &&
                json.RootElement.GetProperty("artifactState").GetString() == "unknown");
        });
        test("configuration terminal diagnostics retain known published state with unusable descriptor text", () =>
        {
            byte[] receipt = CliConfigurationReceipt.Terminal("save-config", 4, "published", null, null,
                [new("PRIVATE_SENTINEL", "PRIVATE_SENTINEL", 1, new string('a', 64))], [],
                ConfigurationPublicationStages.Create("passed", "passed", "passed", "passed"));
            Require(!Encoding.UTF8.GetString(receipt).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(receipt);
            Require(json.RootElement.GetProperty("artifactState").GetString() == "published" &&
                json.RootElement.GetProperty("exitCode").GetInt32() == 4 && json.RootElement.GetProperty("outputs").GetArrayLength() == 0);
        });
        test("configuration terminal errors are bounded without turning failure into success", () =>
        {
            var findings = Enumerable.Range(0, 1100).Select(i => new Finding("E_CONFIG_FONT_IDENTITY", "bindingStructure", "PRIVATE_SENTINEL",
                Key: CliDraftKeys.FromCanonicalField("error-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ToArray();
            var stages = ConfigurationPublicationStages.Create("passed", "passed", "passed", "passed");
            byte[] wire = CliConfigurationReceipt.Terminal("save-config", 4, "published", null, null, [], findings, stages);
            using var json = JsonDocument.Parse(wire); var root = json.RootElement;
            Require(root.GetProperty("exitCode").GetInt32() == 4 && root.GetProperty("artifactState").GetString() == "published");
            var errors = root.GetProperty("errors"); Require(errors.GetArrayLength() == 1000);
            Require(errors.EnumerateArray().Any(e => e.GetProperty("code").GetString() == "E_CONFIGURATION_DIAGNOSTIC_LIMIT"));
            Require(errors.EnumerateArray().All(e => e.GetProperty("stage").GetString() != "bindingStructure"));
            Require(root.GetProperty("validationLevels").GetProperty("bindingStructure").GetString() == "passed");
            bool refused = false;
            try { _ = CliConfigurationReceipt.Terminal("save-config", 0, "published", null, null, [], findings, stages); }
            catch (ArgumentException) { refused = true; }
            Require(refused && !System.Text.Encoding.UTF8.GetString(wire).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
        });
    }

    private static CliConfigurationDraft Candidate(string state)
    {
        var result = CliConfigurationDraft.Create(Facts(), state);
        Require(result.IsValid); return result.Value!;
    }
    private static TemplateSnapshot Template() => TemplateSnapshot.FromBytes(Encoding.UTF8.GetBytes("synthetic immutable template identity bytes"));
    private static StructuralConfigurationDraft Facts(bool embedded = false, double checkboxSize = 10)
    {
        var template = Template();
        var font = new ConfigurationFontFacts("F1", "Helvetica", embedded ? "TrueType" : "Type1", "WinAnsiEncoding", 10,
            embedded ? "FontFile2" : "none", embedded ? new string('a', 64) : "", new string('b', 64), false, embedded, embedded);
        ConfigurationTargetFacts[] targets =
        [
            new(CliDraftKeys.FromCanonicalField("PRIVATE_AMOUNT_DATE_NAME"), "PRIVATE_AMOUNT_DATE_NAME", "text", 1, new double[] { 10, 10, 200, 30 }, 20, true, false, [], font),
            new(CliDraftKeys.FromCanonicalField("PRIVATE_CHECK"), "PRIVATE_CHECK", "checkbox", 1, new[] { 10d, 40d, 10 + checkboxSize, 40 + checkboxSize }, null, false, false, ["Yes"], null)
        ];
        var compatibility = new ConfigurationCompatibility("pdfform.compatibility/1", "configuration_needed", 1,
            template.Sha256, template.ByteCount, 1, false, [], [], [], [], ["suppliedValueFit"]);
        return new("pdfform.configuration-draft-facts/1", template.Sha256, template.ByteCount, "", 1,
            "sha256:" + new string('c', 64), targets, compatibility);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition)
    { if (!condition) throw new InvalidOperationException("Configuration artifact source regression."); }
}
