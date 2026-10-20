using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using iText.Forms;
using iText.Kernel.Pdf;
using PdfForm.Core;
using PdfForm.Pdf;

/// <summary>Uses the frozen Core consumer API and the existing PDF test entry point; no new runner.</summary>
internal static class ConfiguredBindingTests
{
    internal static void Run(Action<bool, string> check, string root)
    {
        // The trailer contains no Info reference at all; no display title is invented from the profile.
        var titlelessBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Fixture.Make()).Replace(" /Info 13 0 R", "", StringComparison.Ordinal));
        var snapshot = TemplateSnapshot.FromBytes(titlelessBytes);
        var inspector = new PdfTemplateInspector();
        var draft = inspector.DiscoverConfiguration(snapshot);
        check(!Encoding.UTF8.GetString(titlelessBytes).Contains("/Info", StringComparison.Ordinal)
            && !Encoding.UTF8.GetString(titlelessBytes).Contains("/Title", StringComparison.Ordinal) && draft.TemplateTitle == "",
            "genuinely title-less bytes discover an empty observed title, separate from human profile title");
        check(inspector.Inspect(snapshot).ActualTitle is null, "legacy inspection retains the nullable absent-title representation");
        check(!Encoding.UTF8.GetString(titlelessBytes).Contains("/Encoding", StringComparison.Ordinal)
            && draft.Targets.All(t => t.Font is { Encoding: "absent" }),
            "a genuinely missing source Encoding is observed as the frozen absent literal");
        var package = Package(snapshot, draft);
        var bound = FormCatalog.Bind(package.Catalog, package.Profile, snapshot.Sha256, snapshot.ByteCount);
        check(bound.IsValid, "reviewed generic synthetic catalog binds exact profile and template bytes");
        var configuration = bound.Value ?? throw new InvalidOperationException("Synthetic configuration setup failed.");
        check(configuration.ParsePolicy().TextFields.All(t => t.Strategy == "bundled-liberation-serif-ascii-v1" && t.Encoding == "absent"),
            "the no-Encoding fixture binds the bundled strategy using the exact Core absent literal");
        check(!Encoding.UTF8.GetString(titlelessBytes).Contains("/FontDescriptor", StringComparison.Ordinal)
            && draft.Targets.All(t => t.Font is { Embedded: false, ProgramKind: "none", ProgramSha256: "" })
            && configuration.ParsePolicy().TextFields.All(t => t.ProgramSha256 is null),
            "only genuinely absent source programs permit omitted program identity in the bundled policy");
        check(snapshot.MatchesConfiguration(configuration), "snapshot compares exact configured hash and length");
        var result = new TemplateBinder().Bind(snapshot, configuration);
        check(inspector.Inspect(snapshot, configuration).ActualTitle == "", "generic inspection normalizes absent title to its v3 empty representation");
        check(result.Bound && result.Inspection.ExactApprovedTemplate, "clean non-IRS AcroForm passes configured structural binding");
        var report = inspector.CheckCompatibility(snapshot, configuration);
        check(report.Status == "supported" && report.ExitCode == 0 && report.NotAssessed.Contains("suppliedValueFit"),
            "configured structural success does not claim actual value fit");
        check(!JsonSerializer.Serialize(report).Contains("group.amountA", StringComparison.Ordinal),
            "configured compatibility still withholds private target names");
        using (var output = new MemoryStream())
        {
            using (var pdf = ApprovedTemplateNormalizer.OpenForNormalization(snapshot, output, configuration))
                check(PdfAcroForm.GetAcroForm(pdf, false) is not null, "clean normalization requires and preserves the canonical form");
            check(output.ToArray().Length > 0, "clean normalization creates a nonempty serialization without IRS exceptions");
        }

        var different = TemplateSnapshot.FromBytes(Fixture.Make(swap: true));
        check(!snapshot.MatchesConfiguration(Configuration(different, draft)), "an exact catalog for other bytes cannot match this snapshot");
        check(inspector.CheckCompatibility(different, configuration).Status == "invalid",
            "same field structure with different template bytes fails before generic admission");
        check(!FormCatalog.Bind(package.Catalog, package.Profile.Concat(new byte[] { (byte)' ' }).ToArray(), snapshot.Sha256, snapshot.ByteCount).IsValid,
            "different exact profile bytes fail catalog admission despite equivalent JSON meaning");

        var wrongMetrics = JsonNode.Parse(package.Catalog)!;
        wrongMetrics["policy"]!["textFields"]![0]!["metricsSha256"] = new string('a', 64);
        var changed = FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(wrongMetrics, StrictJson.Options), package.Profile, snapshot.Sha256, snapshot.ByteCount);
        check(changed.IsValid, "syntactically valid metric claim reaches independent PDF observation");
        var changedConfiguration = changed.Value ?? throw new InvalidOperationException("Synthetic metric setup failed.");
        var rejected = new TemplateBinder().Bind(snapshot, changedConfiguration);
        check(!rejected.Bound && rejected.Findings.Any(f => f.Code == "E_CONFIG_FONT_IDENTITY"),
            "catalog font identity cannot override the observed source resource");
        check(inspector.CheckCompatibility(snapshot, changedConfiguration).Status == "invalid",
            "wrong source-font identity is a configuration mismatch rather than claimed support");

        var wrongEncoding = JsonNode.Parse(package.Catalog)!;
        foreach (var text in wrongEncoding["policy"]!["textFields"]!.AsArray()) text!["encoding"] = "WinAnsiEncoding";
        var wrongEncodingBound = FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(wrongEncoding, StrictJson.Options), package.Profile, snapshot.Sha256, snapshot.ByteCount);
        check(wrongEncodingBound.IsValid, "an explicit supported encoding claim is schema-valid and reaches independent observation");
        var wrongEncodingConfiguration = wrongEncodingBound.Value ?? throw new InvalidOperationException("Synthetic encoding setup failed.");
        var wrongEncodingBinding = new TemplateBinder().Bind(snapshot, wrongEncodingConfiguration);
        check(!wrongEncodingBinding.Bound && wrongEncodingBinding.Findings.Any(f => f.Code == "E_CONFIG_FONT_IDENTITY"),
            "missing source Encoding cannot be silently relabeled as explicit WinAnsiEncoding");

        var explicitEncoding = TemplateSnapshot.FromBytes(ExplicitEncodingBytes());
        var explicitDraft = inspector.DiscoverConfiguration(explicitEncoding);
        check(explicitDraft.Targets.All(t => t.Font is { Encoding: "WinAnsiEncoding" })
            && new TemplateBinder().Bind(explicitEncoding, Configuration(explicitEncoding, explicitDraft)).Bound,
            "explicit source WinAnsiEncoding remains distinct and supported by the same bundled strategy");

        foreach (var malformedFont in ConfigurationInspectionTests.MalformedFontStructures())
        {
            var malformedSnapshot = TemplateSnapshot.FromBytes(malformedFont.Bytes);
            // This catalog claims source-program absence under the bundled strategy; observation must disprove it.
            var malformedConfiguration = Configuration(malformedSnapshot, draft);
            var malformedBinding = new TemplateBinder().Bind(malformedSnapshot, malformedConfiguration);
            check(!malformedBinding.Bound && malformedBinding.Findings.Any(f => f.Code == "E_CONFIG_FONT_STRUCTURE"),
                "replacement policy cannot waive malformed source descriptor/program entries: " + malformedFont.Name);
            bool opened = false;
            try { using var output = new MemoryStream(); using var pdf = ApprovedTemplateNormalizer.OpenForNormalization(malformedSnapshot, output, malformedConfiguration); opened = true; }
            catch (TemplateInputException) { }
            check(!opened, "malformed source font structures cannot reach configured normalization: " + malformedFont.Name);
        }

        var incomplete = JsonNode.Parse(package.Catalog)!;
        incomplete["policy"]!["textFields"]!.AsArray().RemoveAt(0);
        check(!FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(incomplete, StrictJson.Options), package.Profile, snapshot.Sha256, snapshot.ByteCount).IsValid,
            "reviewed metadata cannot admit incomplete policy coverage");

        var xfa = TemplateSnapshot.FromBytes(Fixture.Make(xfa: "<template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\"><subform/></template>"));
        var xfaConfiguration = Configuration(xfa, draft);
        var xfaBinding = new TemplateBinder().Bind(xfa, xfaConfiguration);
        check(!xfaBinding.Bound && xfaBinding.Findings.Any(f => f.Code == "E_XFA_UNSUPPORTED")
            && !xfaBinding.Findings.Any(f => f.Code == "E_PROFILE_CAPABILITY"),
            "generic XFA is refused without asking for a forbidden legacy XFA capability");
        bool normalizedXfa = false;
        try { using var output = new MemoryStream(); using var pdf = ApprovedTemplateNormalizer.OpenForNormalization(xfa, output, xfaConfiguration); normalizedXfa = true; }
        catch (TemplateInputException) { }
        check(!normalizedXfa, "configured no-normalization mode refuses XFA rather than stripping it");
        var xfaCompatibility = inspector.CheckCompatibility(xfa, xfaConfiguration);
        check(xfaCompatibility.Status == "unsupported" && xfaCompatibility.ExitCode == 3
            && xfaCompatibility.Issues.Any(f => f.Code == "E_XFA_UNSUPPORTED")
            && !xfaCompatibility.Issues.Any(f => f.Code == "E_PROFILE_CAPABILITY"),
            "generic XFA remains unsupported rather than invalid through a derivative legacy capability finding");

        var checkbox = TemplateSnapshot.FromBytes(CheckboxBytes());
        var checkboxDraft = inspector.DiscoverConfiguration(checkbox);
        var checkboxConfiguration = Configuration(checkbox, checkboxDraft);
        check(new TemplateBinder().Bind(checkbox, checkboxConfiguration).Bound,
            "observed normal export state and fixed 10-point checkbox geometry bind through configuration");

        var prefilled = TemplateSnapshot.FromBytes(Fixture.Make(sensitive: true));
        var prefilledConfiguration = Configuration(prefilled, draft);
        var prefilledBinding = new TemplateBinder().Bind(prefilled, prefilledConfiguration);
        check(!prefilledBinding.Bound && prefilledBinding.Findings.Any(f => f.Code == "E_TEMPLATE_PREFILLED"),
            "a reviewed catalog cannot waive independently observed prefilling");
        check(!JsonSerializer.Serialize(inspector.CheckCompatibility(prefilled, prefilledConfiguration)).Contains(Fixture.Sentinel, StringComparison.Ordinal),
            "prefilled configured failures retain value/name privacy");

        foreach (var pair in new[] { (Pdf: "f941.pdf", Profile: "irs-941-2026-03.json"), (Pdf: "f941sb.pdf", Profile: "irs-941sb-2024-03.json") })
        {
            var original = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", pair.Pdf));
            var bytes = File.ReadAllBytes(Path.Combine(root, "profiles", pair.Profile));
            var legacy = FormCatalog.BindDefault(bytes, original.Sha256, original.ByteCount);
            check(legacy.IsValid && legacy.Value!.IsBuiltIn, "default adapter retains exact legacy pair: " + pair.Pdf);
            check(new TemplateBinder().Bind(original, legacy.Value!).Bound, "default configured binder preserves IRS exceptions: " + pair.Pdf);
            using var output = new MemoryStream();
            using var normalized = ApprovedTemplateNormalizer.OpenForNormalization(original, output, legacy.Value!);
            check(!PdfAcroForm.GetAcroForm(normalized, false)!.GetPdfObject().ContainsKey(iText.Kernel.Pdf.PdfName.XFA),
                "default normalizer retains existing reviewed XFA removal: " + pair.Pdf);
        }
    }

    private static FormConfigurationSnapshot Configuration(TemplateSnapshot snapshot, StructuralConfigurationDraft draft)
    {
        var package = Package(snapshot, draft);
        return FormCatalog.Bind(package.Catalog, package.Profile, snapshot.Sha256, snapshot.ByteCount).Value
            ?? throw new InvalidOperationException("Synthetic configuration setup failed.");
    }

    private static (byte[] Catalog, byte[] Profile) Package(TemplateSnapshot snapshot, StructuralConfigurationDraft draft)
    {
        var profile = new ProfileDefinition
        {
            Schema = "pdfform.profile/3", Id = "synthetic-third-form", Revision = "one", ProfileVersion = 1,
            Title = "Synthetic non-tax form", Template = new TemplateDefinition
            {
                Sha256Allowlist = [snapshot.Sha256], Title = draft.TemplateTitle, PageCount = draft.Pages,
                StructureFingerprintV1 = draft.StructureFingerprintV1 ?? throw new InvalidOperationException("Synthetic fingerprint missing.")
            },
            Capabilities = draft.Targets.Select(t => t.Type + ".singleWidget").Distinct(StringComparer.Ordinal).ToArray(), FilingYears = [], Options = new() { ZeroAmounts = "blank" },
            Keys = draft.Targets.Select(t => new KeyDefinition
            {
                Key = t.Key, Kind = t.Type, Required = t.Required, Sensitive = true, MaxLength = t.Type == "text" ? t.MaxLength : null,
                Targets = t.Type == "text" ? JsonSerializer.SerializeToElement(new[] { t.Target }) : default,
                Target = t.Type == "checkbox" ? t.Target : null, On = t.Type == "checkbox" ? t.OnStates.Single() : null
            }).ToArray(), Rules = [], Unmapped = [], Source = JsonSerializer.SerializeToElement(new { }), Extensions = JsonSerializer.SerializeToElement(new { })
        };
        byte[] profileBytes = ProfileParser.SerializeProfile(profile);
        var catalog = new
        {
            schema = "pdfform.catalog/1",
            template = new { path = "template.pdf", bytes = snapshot.ByteCount, sha256 = snapshot.Sha256 },
            profile = new { path = "profile.json", bytes = profileBytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(profileBytes)), id = profile.Id, revision = profile.Revision, profileVersion = profile.ProfileVersion },
            review = new { state = "reviewed", unresolved = Array.Empty<object>() },
            policy = new
            {
                id = "configured-acroform-v1", normalization = "none-v1",
                textFields = draft.Targets.Where(t => t.Type == "text").Select(t => new
                {
                    target = t.Target, page = t.Page, rectangle = t.Rectangle, strategy = "bundled-liberation-serif-ascii-v1",
                    layout = "single-line-v1", resourceName = t.Font!.ResourceName, baseFont = t.Font.BaseFont,
                    subtype = t.Font.Subtype, encoding = t.Font.Encoding, fontSize = t.Font.FontSize,
                    metricsSha256 = t.Font.MetricsSha256, repertoire = "ascii-plus-eacute-v1"
                }).ToArray(), checkboxFields = draft.Targets.Where(t => t.Type == "checkbox").Select(t => new
                {
                    target = t.Target, page = t.Page, rectangle = t.Rectangle, onState = t.OnStates.Single(), mark = "cross-10-v1"
                }).ToArray()
            }
        };
        return (JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options), profileBytes);
    }

    private static byte[] CheckboxBytes()
    {
        using var output = new MemoryStream();
        using (var input = new MemoryStream(Fixture.Make()))
        using (var pdf = new PdfDocument(new PdfReader(input), new PdfWriter(output)))
        {
            var field = PdfAcroForm.GetAcroForm(pdf, false)!.GetField("group.amountA").GetPdfObject();
            field.Put(PdfName.FT, PdfName.Btn);
            field.Put(PdfName.Rect, new PdfArray(new double[] { 10, 10, 20, 20 }));
            field.Put(PdfName.AS, new PdfName("Off"));
            var normal = new PdfDictionary();
            normal.Put(new PdfName("Off"), new PdfStream(Array.Empty<byte>()));
            normal.Put(new PdfName("Yes"), new PdfStream(Array.Empty<byte>()));
            var appearance = new PdfDictionary(); appearance.Put(PdfName.N, normal); field.Put(PdfName.AP, appearance);
        }
        return output.ToArray();
    }

    private static byte[] ExplicitEncodingBytes()
    {
        using var output = new MemoryStream();
        using (var input = new MemoryStream(Fixture.Make()))
        using (var pdf = new PdfDocument(new PdfReader(input), new PdfWriter(output)))
        {
            var font = PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject().GetAsDictionary(PdfName.DR)
                .GetAsDictionary(PdfName.Font).GetAsDictionary(new PdfName("Helvetica"));
            font.Put(PdfName.Encoding, new PdfName("WinAnsiEncoding"));
        }
        return output.ToArray();
    }
}
