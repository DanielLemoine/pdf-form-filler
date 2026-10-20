using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfForm.Cli;
using PdfForm.Core;

namespace PdfForm.Cli.Tests;

// Source-only workflow fixtures for root's later serial runtime admission. These deliberately
// exercise real product file/publication paths and must not be run by this delegated source lane.
public static class OnboardingWorkflowCases
{
    public static void Register(Action<string, Action> test)
    {
        test("onboarding workflow separates draft from reviewed three-file configuration", () => InWorkspace(root =>
        {
            string input = Path.Combine(root, "synthetic.pdf"), draft = Path.Combine(root, "draft"), package = Path.Combine(root, "package");
            byte[] original = TemplateBytes(); File.WriteAllBytes(input, original);
            var created = CliApplication.Execute(["configure", input, "--out", draft]); Require(created.ExitCode == 0);
            Require(Directory.EnumerateFiles(draft).Select(p => Path.GetFileName(p)!).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "catalog.json", "profile.json", "result.json", "completion.json" }));
            Require(!File.Exists(Path.Combine(draft, "template.pdf")));
            using (var catalog = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(draft, "catalog.json"))))
                Require(catalog.RootElement.GetProperty("review").GetProperty("state").GetString() == "draft");
            var checkDraft = CliApplication.Execute(["check", input, "--catalog", Path.Combine(draft, "catalog.json")]);
            Require(checkDraft.ExitCode == 1);
            using (var assessment = JsonDocument.Parse(checkDraft.Text))
            {
                Require(assessment.RootElement.GetProperty("schema").GetString() == "pdfform.compatibility/1" &&
                    assessment.RootElement.GetProperty("configurationState").GetString() == "draft" &&
                    assessment.RootElement.GetProperty("counts").GetProperty("textFields").GetInt64() == 1);
            }
            string draftProfilePath = Path.Combine(draft, "profile.json");
            byte[] editedProfile = File.ReadAllBytes(draftProfilePath).Concat(new byte[] { (byte)'\n' }).ToArray();
            File.WriteAllBytes(draftProfilePath, editedProfile);
            var unreviewed = CliApplication.Execute(["save-config", input, "--draft", draft, "--out", package]);
            Require(unreviewed.ExitCode == 1 && !Directory.Exists(package));
            var saved = CliApplication.Execute(["save-config", input, "--draft", draft, "--out", package, "--reviewed"]);
            Require(saved.ExitCode == 0);
            Require(Directory.EnumerateFiles(package).Select(p => Path.GetFileName(p)!).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "catalog.json", "profile.json", "template.pdf", "result.json", "completion.json" }));
            Require(File.ReadAllBytes(Path.Combine(package, "template.pdf")).AsSpan().SequenceEqual(original));
            Require(File.ReadAllBytes(input).AsSpan().SequenceEqual(original));
            Require(File.ReadAllBytes(Path.Combine(package, "profile.json")).AsSpan().SequenceEqual(editedProfile) &&
                File.ReadAllBytes(draftProfilePath).AsSpan().SequenceEqual(editedProfile));
            using (var stillDraft = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(draft, "catalog.json"))))
                Require(stillDraft.RootElement.GetProperty("review").GetProperty("state").GetString() == "draft");
            var checkedPackage = CliApplication.Execute(["check", input, "--catalog", Path.Combine(package, "catalog.json")]);
            Require(checkedPackage.ExitCode == 0);
            using (var assessment = JsonDocument.Parse(checkedPackage.Text))
                Require(assessment.RootElement.GetProperty("configurationState").GetString() == "reviewed");
            using (var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package, "result.json"))))
            {
                Require(receipt.RootElement.GetProperty("schema").GetString() == "pdfform.configuration-result/1");
                foreach (string stage in new[] { "data", "packet", "render" })
                    Require(receipt.RootElement.GetProperty("validationLevels").GetProperty(stage).GetString() == "not_run");
            }
            byte[] protectedData = Encoding.UTF8.GetBytes("SYNTHETIC_EXISTING_OUTPUT");
            string dataPath = Path.Combine(root, "existing-data.json"); File.WriteAllBytes(dataPath, protectedData);
            var refusedSample = CliApplication.Execute(["sample-input", "--catalog", Path.Combine(package, "catalog.json"), "--out", dataPath]);
            Require(refusedSample.ExitCode == 2 && File.ReadAllBytes(dataPath).AsSpan().SequenceEqual(protectedData));
            var packageBefore = Directory.EnumerateFiles(package).ToDictionary(p => Path.GetFileName(p)!, p => Hash(File.ReadAllBytes(p)), StringComparer.Ordinal);
            var repeated = CliApplication.Execute(["save-config", input, "--draft", draft, "--out", package, "--reviewed"]);
            Require(repeated.ExitCode == 2 && Directory.EnumerateFiles(package).All(p => packageBefore[Path.GetFileName(p)!] == Hash(File.ReadAllBytes(p))));
            Require(!created.Text.Contains("SYNTHETIC_PRIVATE_FIELD", StringComparison.Ordinal) && !saved.Text.Contains("SYNTHETIC_PRIVATE_FIELD", StringComparison.Ordinal));
        }));
        test("onboarding workflow refuses changed template identity and unresolved draft without publication", () => InWorkspace(root =>
        {
            string input = Path.Combine(root, "synthetic.pdf"), draft = Path.Combine(root, "draft"), package = Path.Combine(root, "package");
            File.WriteAllBytes(input, TemplateBytes());
            Require(CliApplication.Execute(["configure", input, "--out", draft]).ExitCode == 0);
            string path = Path.Combine(draft, "catalog.json"); byte[] originalCatalog = File.ReadAllBytes(path);
            var catalog = JsonNode.Parse(originalCatalog)!;
            catalog["template"]!["sha256"] = new string('0', 64);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options));
            var mismatch = CliApplication.Execute(["save-config", input, "--draft", draft, "--out", package, "--reviewed"]);
            Require(mismatch.ExitCode != 0 && !Directory.Exists(package));
            catalog = JsonNode.Parse(originalCatalog)!;
            catalog["review"]!["unresolved"] = new JsonArray(new JsonObject
            { ["code"] = "SYNTHETIC_PRIVATE_ISSUE", ["capability"] = "SYNTHETIC_PRIVATE_CAPABILITY" });
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options));
            var unresolved = CliApplication.Execute(["save-config", input, "--draft", draft, "--out", package, "--reviewed"]);
            Require(unresolved.ExitCode != 0 && !Directory.Exists(package) && !unresolved.Text.Contains("SYNTHETIC_PRIVATE", StringComparison.Ordinal));
        }));
        test("draft check validates exact profile identity and target consistency before incomplete classification", () => InWorkspace(root =>
        {
            string input = Path.Combine(root, "synthetic.pdf"), draft = Path.Combine(root, "draft");
            File.WriteAllBytes(input, TemplateBytes());
            Require(CliApplication.Execute(["configure", input, "--out", draft]).ExitCode == 0);
            string catalogPath = Path.Combine(draft, "catalog.json"), profilePath = Path.Combine(draft, "profile.json");
            byte[] originalCatalog = File.ReadAllBytes(catalogPath), originalProfile = File.ReadAllBytes(profilePath);
            void Check(int expectedExit, string state, string expectedIssue)
            {
                var result = CliApplication.Execute(["check", input, "--catalog", catalogPath]); Require(result.ExitCode == expectedExit);
                using var json = JsonDocument.Parse(result.Text); var value = json.RootElement;
                Require(value.GetProperty("configurationState").GetString() == state &&
                    value.GetProperty("issues").EnumerateArray().Any(i => i.GetProperty("code").GetString() == expectedIssue));
            }
            File.WriteAllBytes(profilePath, originalProfile.Concat(new byte[] { (byte)' ' }).ToArray());
            Check(2, "invalid", "E_CATALOG_PROFILE_HASH");
            File.WriteAllBytes(profilePath, originalProfile);
            var catalog = JsonNode.Parse(originalCatalog)!;
            catalog["profile"]!["id"] = "different-synthetic-profile";
            File.WriteAllBytes(catalogPath, JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options));
            Check(2, "invalid", "E_CATALOG_PROFILE_IDENTITY");
            catalog = JsonNode.Parse(originalCatalog)!;
            catalog["policy"]!["textFields"]![0]!["target"] = "SYNTHETIC_OTHER_TARGET";
            File.WriteAllBytes(catalogPath, JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options));
            Check(2, "invalid", "E_CATALOG_TARGET_COVERAGE");
            catalog = JsonNode.Parse(originalCatalog)!;
            catalog["policy"]!["textFields"]![0]!["page"] = 2;
            File.WriteAllBytes(catalogPath, JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options));
            Check(2, "invalid", "E_CATALOG_TARGET_PAGE");
            catalog = JsonNode.Parse(originalCatalog)!;
            catalog["policy"]!["textFields"] = new JsonArray();
            File.WriteAllBytes(catalogPath, JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options));
            Check(1, "draft", "E_CATALOG_TARGET_REQUIRED");
            File.WriteAllBytes(catalogPath, originalCatalog);
            Check(1, "draft", "E_CATALOG_REVIEW_REQUIRED");
            string data = Path.Combine(root, "synthetic-data.json"); File.WriteAllText(data, "{}");
            var validation = CliApplication.Execute(["validate", "--catalog", catalogPath, "--data", data, "--pdf", input]);
            Require(validation.ExitCode == 1);
            using var legacy = JsonDocument.Parse(validation.Text);
            Require(legacy.RootElement.GetProperty("schema").GetString() == "pdfform.result/2" &&
                legacy.RootElement.GetProperty("status").GetString() == "validation_failed");
            Require(legacy.RootElement.GetProperty("errors").EnumerateArray().All(e => e.GetProperty("stage").GetString() != "bindingStructure"));
        }));
        test("check unsupported catalog strategies retain Core exit three and observed identities", () => InWorkspace(root =>
        {
            string input = Path.Combine(root, "synthetic.pdf"), draft = Path.Combine(root, "draft");
            byte[] original = TemplateBytes(); File.WriteAllBytes(input, original);
            Require(CliApplication.Execute(["configure", input, "--out", draft]).ExitCode == 0);
            string catalogPath = Path.Combine(draft, "catalog.json"), profilePath = Path.Combine(draft, "profile.json");
            byte[] catalogBytes = File.ReadAllBytes(catalogPath), profileBytes = File.ReadAllBytes(profilePath);
            foreach (bool normalization in new[] { false, true })
            {
                var catalog = JsonNode.Parse(catalogBytes)!;
                if (normalization) catalog["policy"]!["normalization"] = "unimplemented-normalization-v1";
                else catalog["policy"]!["textFields"]![0]!["strategy"] = "unimplemented-font-v1";
                File.WriteAllBytes(catalogPath, JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options));
                var result = CliApplication.Execute(["check", input, "--catalog", catalogPath]); Require(result.ExitCode == 3);
                using var json = JsonDocument.Parse(result.Text); var value = json.RootElement;
                Require(value.GetProperty("schema").GetString() == "pdfform.compatibility/1" && value.GetProperty("status").GetString() == "unsupported");
                Require(value.GetProperty("templateSha256").GetString() == Hash(original) && value.GetProperty("templateBytes").GetInt64() == original.Length);
                Require(value.GetProperty("issues").EnumerateArray().Any(i => i.GetProperty("code").GetString() == "E_CATALOG_POLICY_UNSUPPORTED"));
            }
            File.WriteAllText(profilePath, "{}");
            var invalid = CliApplication.Execute(["check", input, "--catalog", catalogPath]);
            Require(invalid.ExitCode == 2); // A real profile error dominates an unsupported policy.
            File.WriteAllBytes(profilePath, profileBytes);
            Require(File.ReadAllBytes(input).AsSpan().SequenceEqual(original));
        }));
        test("save-config required date geometry reports configuration-needed without publication", () => InWorkspace(root =>
        {
            string input = Path.Combine(root, "synthetic.pdf"), draft = Path.Combine(root, "draft"), destination = Path.Combine(root, "package");
            File.WriteAllBytes(input, TemplateBytes());
            Require(CliApplication.Execute(["configure", input, "--out", draft]).ExitCode == 0);
            string catalogPath = Path.Combine(draft, "catalog.json"), profilePath = Path.Combine(draft, "profile.json");
            var profile = JsonNode.Parse(File.ReadAllBytes(profilePath))!;
            profile["keys"]![0]!["kind"] = "date";
            profile["keys"]![0]!["format"] = "MMddyyyy";
            profile["options"]!.AsObject().Remove("dateCellLayouts");
            File.WriteAllBytes(profilePath, JsonSerializer.SerializeToUtf8Bytes(profile, StrictJson.Options));
            var catalog = JsonNode.Parse(File.ReadAllBytes(catalogPath))!;
            catalog["policy"]!["textFields"]![0]!["layout"] = "eight-date-cells-v1";
            File.WriteAllBytes(catalogPath, JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options));
            var result = CliApplication.Execute(["save-config", input, "--draft", draft, "--out", destination, "--reviewed"]);
            Require(result.ExitCode == 1 && !Directory.Exists(destination));
            using var json = JsonDocument.Parse(result.Text); var value = json.RootElement;
            Require(value.GetProperty("schema").GetString() == "pdfform.configuration-result/1" &&
                value.GetProperty("status").GetString() == "configuration_needed" && value.GetProperty("artifactState").GetString() == "none");
            Require(value.GetProperty("errors").EnumerateArray().Any(e => e.GetProperty("code").GetString() == "E_CATALOG_DATE_LAYOUT_REQUIRED"));
            foreach (string stage in new[] { "data", "packet", "render" })
                Require(value.GetProperty("validationLevels").GetProperty(stage).GetString() == "not_run");
        }));
    }

    private static byte[] TemplateBytes()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 7 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << >> /Annots [4 0 R] /Contents 6 0 R >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (SYNTHETIC_PRIVATE_FIELD) /Rect [20 80 240 105] /P 3 0 R /F 4 /Ff 0 /MaxLen 30 /DA (/F1 10 Tf 0 g) >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Length 0 >>\nstream\n\nendstream",
            "<< /Fields [4 0 R] /DA (/F1 10 Tf 0 g) /DR << /Font << /F1 5 0 R >> >> /NeedAppearances false >>"
        ];
        using var bytes = new MemoryStream();
        void Write(string value) => bytes.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.4\n"); var offsets = new List<long> { 0 };
        for (int i = 0; i < objects.Length; i++)
        { offsets.Add(bytes.Position); Write((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + objects[i] + "\nendobj\n"); }
        long xref = bytes.Position; Write("xref\n0 8\n0000000000 65535 f \n");
        foreach (long offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        return bytes.ToArray();
    }
    private static void InWorkspace(Action<string> body)
    {
        string parent = Path.GetFullPath(Path.GetTempPath());
        string name = "pdfform-cli-source-" + Guid.NewGuid().ToString("N");
        string root = Path.GetFullPath(Path.Combine(parent, name)); Directory.CreateDirectory(root);
        try { body(root); }
        finally
        {
            if (Path.GetDirectoryName(root) != Path.TrimEndingDirectorySeparator(parent) || Path.GetFileName(root) != name)
                throw new InvalidOperationException("Synthetic workspace ownership mismatch.");
            Directory.Delete(root, recursive: true);
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition)
    { if (!condition) throw new InvalidOperationException("Onboarding workflow source regression."); }
}
