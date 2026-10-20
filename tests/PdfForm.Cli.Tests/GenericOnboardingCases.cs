using System.Text.Json;
using System.Text.Json.Nodes;
using PdfForm.Cli;
using PdfForm.Core;

namespace PdfForm.Cli.Tests;

// Focused source leaf. The common executable test Program remains untouched; its owner can register
// Register(Test) when the exact integrated candidate and runtime admission are ready.
public static class GenericOnboardingCases
{
    public static void Register(Action<string, Action> test)
    {
        test("onboarding check preserves spaced paths", () =>
        {
            var parsed = CliOnboardingArguments.Parse(["check", "C:\\synthetic folder\\blank.pdf", "--catalog", "C:\\configuration folder\\catalog.json"]);
            Require(parsed.Request is { Operation: CliOnboardingOperation.Check, Pdf: "C:\\synthetic folder\\blank.pdf", Catalog: "C:\\configuration folder\\catalog.json" });
        });
        test("onboarding configure requires new destination argument", () =>
        {
            Require(CliOnboardingArguments.Parse(["configure", "blank.pdf"]).ExitCode == 2);
            Require(CliOnboardingArguments.Parse(["configure", "blank.pdf", "--out", "new draft"]).Request?.Operation == CliOnboardingOperation.Configure);
        });
        test("onboarding review is explicit and not duplicated", () =>
        {
            var missing = CliOnboardingArguments.Parse(["save-config", "blank.pdf", "--draft", "draft", "--out", "package"]);
            Require(missing is { ExitCode: 1, Status: "configuration_needed", Request: null });
            var reviewed = CliOnboardingArguments.Parse(["save-config", "blank.pdf", "--draft", "draft", "--out", "package", "--reviewed"]);
            Require(reviewed.Request is { Reviewed: true, Operation: CliOnboardingOperation.SaveConfig });
            Require(CliOnboardingArguments.Parse(["save-config", "blank.pdf", "--draft", "draft", "--out", "package", "--reviewed", "--reviewed"]).ExitCode == 2);
        });
        test("onboarding catalog fill and validate are selected without weakening profile route", () =>
        {
            Require(!CliOnboardingArguments.Parse(["fill", "blank.pdf", "--profile", "profile.json", "--data", "data.json", "--out", "new.pdf"]).Handled);
            Require(CliOnboardingArguments.Parse(["fill", "blank.pdf", "--catalog", "catalog.json", "--data", "data.json", "--out", "new.pdf", "--flatten"]).Request is { Operation: CliOnboardingOperation.CatalogFill, Flatten: true });
            Require(CliOnboardingArguments.Parse(["validate", "--catalog", "catalog.json", "--data", "data.json", "--pdf", "blank.pdf"]).Request?.Pdf == "blank.pdf");
            Require(CliOnboardingArguments.Parse(["fill", "blank.pdf", "--catalog", "catalog.json", "--profile", "other.json", "--data", "data.json", "--out", "new.pdf"]).ExitCode == 2);
        });
        test("onboarding rejects incomplete duplicate and unknown options", () =>
        {
            foreach (string[] args in new[]
            {
                new[] { "check", "blank.pdf", "--catalog" },
                new[] { "check", "blank.pdf", "--catalog", "a.json", "--catalog", "b.json" },
                new[] { "configure", "blank.pdf", "--out", "new", "--unchecked" },
                new[] { "sample-input", "--catalog", "catalog.json" },
                new[] { "sample-input", "--catalog", "catalog.json", "--out", "" }
            }) Require(CliOnboardingArguments.Parse(args).ExitCode == 2);
            Require(CliOnboardingArguments.Parse(["configure", "blank.pdf", "--out", "new", "--force"]).ExitCode == 3);
        });
        test("onboarding error text contains no private argument", () =>
        {
            var parsed = CliOnboardingArguments.Parse(["configure", "PRIVATE_SENTINEL.pdf", "--unknown", "PRIVATE_SENTINEL"]);
            Require(parsed.Request is null && !JsonSerializer.Serialize(parsed).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
        });
        test("draft keys preserve exact canonical identity without business inference", () =>
        {
            string a = CliDraftKeys.FromCanonicalField("EmployerName");
            Require(a.Length == 70 && a.StartsWith("field_", StringComparison.Ordinal));
            Require(a == CliDraftKeys.FromCanonicalField("EmployerName"));
            Require(a != CliDraftKeys.FromCanonicalField("employername"));
            Require(a != CliDraftKeys.FromCanonicalField("EmployerName "));
            Require(CliDraftKeys.FromCanonicalField("\u00e9") != CliDraftKeys.FromCanonicalField("e\u0301"));
            Require(!a.Contains("Employer", StringComparison.Ordinal));
            Require(CliDraftKeys.Build(["b", "a"]).Count == 2);
            bool duplicate = false; try { CliDraftKeys.Build(["same", "same"]); } catch (ArgumentException) { duplicate = true; }
            Require(duplicate);
        });
        test("synthetic sample deterministic rules and owned bytes", () =>
        {
            var profile = Profile(); var context = new FilingContext { TaxYear = "2000", Quarter = "1", LiabilityReporting = "monthly" };
            bool Fits(KeyDefinition key, JsonElement value) => key.Kind != "text" || value.GetString()?.Length == 1;
            var a = CliSyntheticSample.Build(profile, context, Fits, _ => []);
            var b = CliSyntheticSample.Build(profile, context, Fits, _ => []);
            Require(a.Ready && b.Ready && a.CopyData()!.AsSpan().SequenceEqual(b.CopyData()));
            byte[] changed = a.CopyData()!; changed[0] ^= 1;
            Require(!changed.AsSpan().SequenceEqual(a.CopyData()));
            using var json = JsonDocument.Parse(a.CopyData()!);
            Require(json.RootElement.GetProperty("values").GetProperty("field_text").GetString() == "A");
            Require(json.RootElement.GetProperty("values").GetProperty("field_total").GetString() == "0.00");
            Require(json.RootElement.GetProperty("values").GetProperty("field_check").GetBoolean());
        });
        test("synthetic sample requires actual fit and whole form checks", () =>
        {
            var context = new FilingContext { TaxYear = "2000", Quarter = "1", LiabilityReporting = "monthly" };
            var noFit = CliSyntheticSample.Build(Profile(), context, (_, _) => false, _ => []);
            Require(!noFit.Ready && noFit.CopyData() is null && noFit.Findings.Any(f => f.Code == "E_SAMPLE_NO_CANDIDATE"));
            var rejected = CliSyntheticSample.Build(Profile(), context, (_, _) => true,
                _ => [new Finding("E_SYNTHETIC_GEOMETRY_REJECTED", "sample", "Synthetic geometry refusal.")]);
            Require(!rejected.Ready && rejected.CopyData() is null && rejected.Findings.Any(f => f.Code == "E_SYNTHETIC_GEOMETRY_REJECTED"));
        });
        test("synthetic sample preserves explicit legacy context requirement", () =>
        {
            var rejected = CliSyntheticSample.Build(Profile(), null, (_, _) => true, _ => []);
            Require(!rejected.Ready && rejected.Findings.Any(f => f.Code == "E_SAMPLE_CONTEXT_REQUIRED"));
        });
        test("synthetic sample bounds rejected candidates before fit", () =>
        {
            var text = Profile().Keys[0] with { Required = false, Pattern = "Z{256}", MaxLength = 256 };
            var profile = Profile() with
            {
                Keys = Enumerable.Range(0, 500).Select(i => text with { Key = "field_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray(),
                Rules = []
            };
            int fitCalls = 0;
            var rejected = CliSyntheticSample.Build(profile,
                new() { TaxYear = "2000", Quarter = "1", LiabilityReporting = "monthly" },
                (_, _) => { fitCalls++; return true; }, _ => []);
            Require(!rejected.Ready && rejected.CopyData() is null && fitCalls == 0);
            Require(rejected.Findings.Any(f => f.Code == "E_SAMPLE_BUDGET"));
        });
        test("synthetic sample callback failure withholds bytes and private details", () =>
        {
            var context = new FilingContext { TaxYear = "2000", Quarter = "1", LiabilityReporting = "monthly" };
            var fitFailure = CliSyntheticSample.Build(Profile(), context,
                (_, _) => throw new InvalidOperationException("PRIVATE_SENTINEL"), _ => []);
            var wholeFailure = CliSyntheticSample.Build(Profile(), context,
                (_, _) => true, _ => throw new InvalidOperationException("PRIVATE_SENTINEL"));
            foreach (var result in new[] { fitFailure, wholeFailure })
            {
                Require(!result.Ready && result.ExitCode == 4 && result.CopyData() is null);
                Require(!JsonSerializer.Serialize(result).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            }
        });
        test("onboarding parsed paths are withheld from serialized requests", () =>
        {
            var parsed = CliOnboardingArguments.Parse(["fill", "PRIVATE_SENTINEL.pdf", "--catalog", "PRIVATE_SENTINEL/catalog.json", "--data", "PRIVATE_SENTINEL/data.json", "--out", "PRIVATE_SENTINEL/new.pdf"]);
            Require(parsed.Request is not null && !JsonSerializer.Serialize(parsed).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
        });
        test("synthetic sample v3 omits context", () =>
        {
            var profile = Profile() with { Schema = "pdfform.profile/3", FilingYears = [] };
            var result = CliSyntheticSample.Build(profile, null, (_, _) => true, _ => []);
            Require(result.Ready);
            using var document = JsonDocument.Parse(result.CopyData()!);
            Require(document.RootElement.GetProperty("schema").GetString() == "pdfform.data/3" && !document.RootElement.TryGetProperty("context", out _));
        });
        test("synthetic sample tries an explicit hint through normalization and fit before generation", () =>
        {
            var source = Profile();
            var generic = source with { Schema = "pdfform.profile/3", FilingYears = [], Rules = [],
                Template = source.Template with { StructureFingerprintV1 = "sha256:" + new string('b', 64) },
                Keys = [source.Keys[0] with { Pattern = "Z{2}", MaxLength = 2 }] };
            var node = JsonNode.Parse(ProfileParser.SerializeProfile(generic))!;
            node["sampleHints"] = new JsonObject { ["field_text"] = "ZZ" };
            var parsed = ProfileParser.ParseProfile(JsonSerializer.SerializeToUtf8Bytes(node, StrictJson.Options));
            Require(parsed.IsValid);
            var seen = new List<string>();
            var accepted = CliSyntheticSample.Build(parsed.Value!, null, (_, value) => { seen.Add(value.GetString()!); return true; }, _ => []);
            Require(accepted.Ready && seen.SequenceEqual(new[] { "ZZ" }));
            using var data = JsonDocument.Parse(accepted.CopyData()!);
            Require(data.RootElement.GetProperty("values").GetProperty("field_text").GetString() == "ZZ");
            var rejected = CliSyntheticSample.Build(parsed.Value!, null, (_, _) => false, _ => []);
            Require(!rejected.Ready && rejected.ExitCode == 3 && rejected.CopyData() is null);
        });
        test("synthetic sample preserves returned per-value internal failures as exit four", () =>
        {
            var context = new FilingContext { TaxYear = "2000", Quarter = "1", LiabilityReporting = "monthly" };
            var result = CliSyntheticSample.Build(Profile(), context,
                (_, _) => CliSyntheticSample.AcceptFit([new("E_FIT_INTERNAL", "render", "PRIVATE_SENTINEL")]), _ => []);
            Require(!result.Ready && result.ExitCode == 4 && result.Status == "internal_error" && result.CopyData() is null &&
                result.Findings.Any(f => f.Code == "E_FIT_INTERNAL"));
            Require(!JsonSerializer.Serialize(result).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            var constraint = CliSyntheticSample.Build(Profile(), context,
                (_, _) => CliSyntheticSample.AcceptFit([new("E_TEXT_OVERFLOW", "render", "E_TEXT_OVERFLOW")]), _ => []);
            Require(!constraint.Ready && constraint.ExitCode == 3 && constraint.CopyData() is null);
        });
        test("synthetic sample preserves returned whole-form internal failures as exit four", () =>
        {
            var context = new FilingContext { TaxYear = "2000", Quarter = "1", LiabilityReporting = "monthly" };
            var result = CliSyntheticSample.Build(Profile(), context, (_, _) => true,
                _ => [new("E_FILL_INTERNAL", "render", "PRIVATE_SENTINEL")]);
            Require(!result.Ready && result.ExitCode == 4 && result.CopyData() is null && result.Findings.Any(f => f.Code == "E_FILL_INTERNAL"));
            Require(!JsonSerializer.Serialize(result).Contains("PRIVATE_SENTINEL", StringComparison.Ordinal));
            var constraint = CliSyntheticSample.Build(Profile(), context, (_, _) => true,
                _ => [new("E_TEXT_OVERFLOW", "render", "E_TEXT_OVERFLOW")]);
            Require(!constraint.Ready && constraint.ExitCode == 3 && constraint.CopyData() is null);
        });
    }

    private static ProfileDefinition Profile() => new()
    {
        Schema = "pdfform.profile/2", Id = "synthetic-onboarding", Revision = "1", ProfileVersion = 1, Title = "Synthetic",
        Template = new() { Sha256Allowlist = [new string('a', 64)], Title = "Synthetic", PageCount = 1, StructureFingerprintV1 = new string('b', 64) },
        Capabilities = [], FilingYears = ["2000"], Options = new() { ZeroAmounts = "blank" }, Unmapped = [],
        Keys =
        [
            new() { Key = "field_text", Kind = "text", Required = true, Sensitive = false, MaxLength = 3, Pattern = "[A-Z]+", Targets = JsonSerializer.SerializeToElement(new[] { "text" }) },
            new() { Key = "field_check", Kind = "checkbox", Required = true, Sensitive = false, Target = "check" },
            new() { Key = "field_a", Kind = "money", Required = true, Sensitive = false, Dollars = "a", Cents = "ac" },
            new() { Key = "field_b", Kind = "money", Required = true, Sensitive = false, Dollars = "b", Cents = "bc" },
            new() { Key = "field_total", Kind = "money", Required = true, Sensitive = false, Dollars = "total", Cents = "totalc" }
        ],
        Rules = [new() { Id = "synthetic-sum", Kind = "sum", Severity = "error", Total = "field_total", Terms = [new() { Key = "field_a", Sign = 1 }, new() { Key = "field_b", Sign = 1 }] }]
    };
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Generic onboarding source regression."); }
}
