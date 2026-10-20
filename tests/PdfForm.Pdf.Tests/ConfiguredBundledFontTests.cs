using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using PdfForm.Core;
using PdfForm.Pdf;

// Source-only integration controls. Root owns runner wiring and separately
// admitted execution against the complete frozen Core/inspector successors.
internal static class ConfiguredBundledFontTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(ScheduleBEmbeddedFont.Supports(32) && ScheduleBEmbeddedFont.Supports(126)
            && ScheduleBEmbeddedFont.Supports(0x00c9) && !ScheduleBEmbeddedFont.Supports(0x00e9)
            && !ScheduleBEmbeddedFont.Supports(31) && !ScheduleBEmbeddedFont.Supports(127),
            "configured strategy shares the accepted engine-fixed ASCII plus U+00C9 repertoire");
        foreach (float size in new[] { 1f, 10, 12.5f, 72 })
        {
            var template = SyntheticTemplate(size, size);
            var configuration = Configuration(template);
            var keys = configuration.ParseProfile().Keys;
            var values = keys.ToDictionary(key => key.Key, _ => "AÉ", StringComparer.Ordinal);
            var prepared = VerifiedFillPipeline.Prepare(template, configuration, Data(configuration, values), flatten: false);
            check(prepared.ReadyToPublish && prepared.ExitCode == 0
                && prepared.Findings.Any(f => f.Code == "W_FONT_EXPLICIT_REPLACEMENT" && f.Severity == "warning"),
                "configured pinned-font rendering and explicit warning: " + size);
            if (prepared.Candidate is not { } candidate) continue;
            check(candidate.ManifestForFixture.Targets.Values.All(target => target.Font is
                { Policy: ScheduleBEmbeddedFont.ConfiguredPolicy, PrivateUnembeddedException: false } font
                && font.Size == size && font.EmbeddedDigest == ScheduleBEmbeddedFont.ProgramSha256),
                "every witness retains the exact pinned identity and observed source size: " + size);
            VerifySharedProgram(check, candidate, "populated " + size);

            var blank = VerifiedFillPipeline.Prepare(template, configuration,
                Data(configuration, keys.ToDictionary(key => key.Key, _ => "", StringComparer.Ordinal)), flatten: false);
            check(blank.ReadyToPublish, "blank configured targets retain independently verified editable defaults: " + size);
            if (blank.Candidate is { } blankCandidate)
            {
                VerifySharedProgram(check, blankCandidate, "blank " + size);
                if (size == 12.5f) RejectDefaultMutations(check, blankCandidate);
            }
            var fit = VerifiedFillPipeline.CheckValueFit(template, configuration, keys[0].Key, JsonSerializer.SerializeToElement("AÉ"));
            check(fit.All(f => f.Severity != "error"), "per-value fit paints using the same pinned implementation: " + size);
            if (size == 10) RejectSourceIdentityClaims(check, template, configuration, values);
            var unsupported = VerifiedFillPipeline.CheckValueFit(template, configuration, keys[0].Key, JsonSerializer.SerializeToElement("é"));
            check(unsupported.Any(f => f.Code == "E_FONT_GLYPH_UNQUALIFIED"),
                "bundled glyph inventory cannot broaden the accepted repertoire: " + size);
            if (size == 72)
            {
                var overflow = VerifiedFillPipeline.CheckValueFit(template, configuration, keys[0].Key,
                    JsonSerializer.SerializeToElement(new string('W', 64)));
                check(overflow.Any(f => f.Code == "E_TEXT_OVERFLOW"), "configured fit rejects overflow without shrinking or fallback");
            }
            var policy = configuration.ParsePolicy();
            double originalLeft = policy.TextFields[0].Rectangle[0];
            policy.TextFields[0].Rectangle[0] += 100;
            check(configuration.ParsePolicy().TextFields[0].Rectangle[0] == originalLeft,
                "configured placement trees cannot mutate the sealed source identity");
        }

        var mixed = SyntheticTemplate(10, 12.5f);
        var mixedConfiguration = Configuration(mixed);
        var mixedValues = mixedConfiguration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "AÉ", StringComparer.Ordinal);
        var mixedResult = VerifiedFillPipeline.Prepare(mixed, mixedConfiguration, Data(mixedConfiguration, mixedValues), flatten: false);
        check(mixedResult.ReadyToPublish && mixedResult.Candidate!.ManifestForFixture.Targets.Values
            .Select(target => target.Font!.Size).Order().SequenceEqual(new[] { 10f, 12.5f }),
            "one document may share one font program while retaining distinct field-local fixed sizes");
        if (mixedResult.Candidate is { } mixedCandidate) VerifySharedProgram(check, mixedCandidate, "mixed sizes");
        var flattened = VerifiedFillPipeline.Prepare(mixed, mixedConfiguration, Data(mixedConfiguration, mixedValues), flatten: true);
        check(flattened.ReadyToPublish && flattened.Candidate!.Flattened,
            "configured pinned font preserves editable verification before flattening");

        using var first = new PdfDocument(new PdfWriter(new MemoryStream()));
        using var second = new PdfDocument(new PdfWriter(new MemoryStream()));
        var shared = new ScheduleBEmbeddedFont.DocumentFont(first);
        string? crossDocumentCode = null;
        try { shared.Get(second, out _); } catch (VerifiedFillException error) { crossDocumentCode = error.Code; }
        check(crossDocumentCode == "E_FONT_DOCUMENT", "the shared font cannot leak into another authoring document");
        first.AddNewPage(); second.AddNewPage();
    }

    // Additive v6 registration: RunExtensions(check, root). Exactly56 healthy-
    // path checks:24 font/AP controls +12 overflow controls +18 date controls +2 blank-default controls.
    internal static void RunExtensions(Action<bool, string> check, string root)
    {
        foreach (string strategy in new[] { "preserve-embedded-simple-v1", "bundled-liberation-serif-ascii-v1" })
        foreach (float size in new[] { 1f, 10, 72 })
        {
            var template = ExtensionTemplate(root, strategy, size);
            var configuration = ExtensionConfiguration(template, strategy);
            var values = configuration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "11", StringComparer.Ordinal);
            foreach (bool flatten in new[] { false, true })
            {
                var result = VerifiedFillPipeline.Prepare(template, configuration, Data(configuration, values), flatten);
                check(result.ReadyToPublish && result.Candidate is { } candidate
                    && ExtensionExactFonts(candidate, configuration, size, flatten),
                    "v6/exact-appearance-font/" + strategy + "/" + size + "/" + flatten);
                var ready = result.Candidate ?? throw new InvalidOperationException("Positive font control failed.");
                check(ExtensionRejectFontSwap(ready, root, strategy, flatten),
                    "v6/appearance-only-font-swap/" + strategy + "/" + size + "/" + flatten);
            }

            var narrow = ExtensionTemplate(root, strategy, size, narrow: true);
            var narrowConfiguration = ExtensionConfiguration(narrow, strategy);
            var one = narrowConfiguration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "W", StringComparer.Ordinal);
            var fitting = VerifiedFillPipeline.Prepare(narrow, narrowConfiguration, Data(narrowConfiguration, one), flatten: false);
            check(fitting.ReadyToPublish && fitting.Candidate is { } fittingCandidate
                && ExtensionExactFonts(fittingCandidate, narrowConfiguration, size, flatten: false),
                "v6/narrow-positive/" + strategy + "/" + size);
            var overlong = one.ToDictionary(pair => pair.Key, _ => new string('W', 64), StringComparer.Ordinal);
            var overflow = VerifiedFillPipeline.Prepare(narrow, narrowConfiguration, Data(narrowConfiguration, overlong), flatten: false);
            check(!overflow.ReadyToPublish && overflow.Findings.Any(f => f.Code == "E_TEXT_OVERFLOW"),
                "v6/overflow-without-shrink/" + strategy + "/" + size);

            foreach (bool eightCells in new[] { false, true })
            {
                var dateTemplate = ExtensionTemplate(root, strategy, size, dates: true);
                var dateConfiguration = ExtensionConfiguration(dateTemplate, strategy, dates: true, eightCells: eightCells);
                var dates = dateConfiguration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "2026-10-20", StringComparer.Ordinal);
                var dateResult = VerifiedFillPipeline.Prepare(dateTemplate, dateConfiguration, Data(dateConfiguration, dates), flatten: false);
                check(dateResult.ReadyToPublish && dateResult.Candidate is { } dateCandidate
                    && ExtensionExactFonts(dateCandidate, dateConfiguration, size, flatten: false)
                    && ExtensionDatePaint(dateCandidate, eightCells),
                    "v6/date-paint/" + strategy + "/" + size + "/" + eightCells);
                if (eightCells)
                    check(ExtensionRejectCellShift(dateResult.Candidate ?? throw new InvalidOperationException("Positive date control failed.")),
                        "v6/date-cell-position/" + strategy + "/" + size);
            }
        }
        foreach (float size in new[] { 1f, 72 })
        {
            var template = ExtensionTemplate(root, ScheduleBEmbeddedFont.ConfiguredPolicy, size);
            var configuration = ExtensionConfiguration(template, ScheduleBEmbeddedFont.ConfiguredPolicy);
            var values = configuration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "", StringComparer.Ordinal);
            var blank = VerifiedFillPipeline.Prepare(template, configuration, Data(configuration, values), flatten: false);
            check(blank.Candidate is { } candidate && ExtensionRejectBlankDefault(candidate, size),
                "v6/blank-replacement-local-default/" + size);
        }
    }

    private static TemplateSnapshot ExtensionTemplate(string root, string strategy, float size, bool narrow = false, bool dates = false)
    {
        using var output = new BoundedPdfMemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(SyntheticTemplate(size, size))), new PdfWriter(output)))
        {
            var form = PdfAcroForm.GetAcroForm(pdf, false)!;
            if (strategy == "preserve-embedded-simple-v1")
            {
                var approved = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", "f941.pdf"));
                if (approved.Sha256 != ApprovedTemplates.Form941Sha256) throw new InvalidOperationException("Exact font fixture required.");
                using var source = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(approved)));
                var original = PdfAcroForm.GetAcroForm(source, false)!.GetPdfObject().GetAsDictionary(PdfName.DR)!
                    .GetAsDictionary(PdfName.Font)!.GetAsDictionary(new PdfName("TimesNewRomanPSMT"))!;
                form.GetPdfObject().GetAsDictionary(PdfName.DR)!.GetAsDictionary(PdfName.Font)!
                    .Put(new PdfName("AuthoredEmbedded"), original.CopyTo(pdf));
            }
            var fields = Fields(form).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < fields.Length; i++)
            {
                string resource = strategy == "preserve-embedded-simple-v1" ? "AuthoredEmbedded" : "Helvetica";
                fields[i].Value.GetPdfObject().Put(PdfName.DA,
                    new PdfString("/" + resource + " " + size.ToString(CultureInfo.InvariantCulture) + " Tf 0 g"));
                double left = dates ? 10 : 10 + 290 * i;
                double bottom = dates ? 10 + 190 * i : 10;
                double width = narrow ? Math.Min(270, 20 * size + 8) : dates ? 590 : 270;
                fields[i].Value.GetWidgets()[0].GetPdfObject().Put(PdfName.Rect,
                    new PdfArray(new double[] { left, bottom, left + width, bottom + 140 }));
            }
        }
        return TemplateSnapshot.FromBytes(output.ToArray());
    }

    private static FormConfigurationSnapshot ExtensionConfiguration(TemplateSnapshot template, string strategy,
        bool dates = false, bool eightCells = false)
    {
        var draft = new PdfTemplateInspector().DiscoverConfiguration(template);
        var profile = new ProfileDefinition
        {
            Schema = "pdfform.profile/3", Id = "synthetic-font-extensions", Revision = "one", ProfileVersion = 1,
            Title = "Synthetic rendering extensions", Template = new()
            {
                Sha256Allowlist = [template.Sha256], Title = draft.TemplateTitle, PageCount = draft.Pages,
                StructureFingerprintV1 = draft.StructureFingerprintV1 ?? throw new InvalidOperationException("Synthetic fingerprint missing.")
            }, Capabilities = ["text.singleWidget"], FilingYears = [], Options = new()
            {
                ZeroAmounts = "blank", DateCellLayouts = eightCells ? draft.Targets.Select(target => new DateCellLayout
                {
                    Target = target.Target, TemplateSha256 = template.Sha256,
                    Cells = Enumerable.Range(0, 8).Select(i => new double[]
                    {
                        target.Rectangle[0] + i * (target.Rectangle[2] - target.Rectangle[0]) / 8,
                        target.Rectangle[1], target.Rectangle[0] + (i + 1) * (target.Rectangle[2] - target.Rectangle[0]) / 8,
                        target.Rectangle[3]
                    }).ToArray()
                }).ToArray() : null
            }, Keys = draft.Targets.Select(target => new KeyDefinition
            {
                Key = target.Key, Kind = dates ? "date" : "text", Required = false, Sensitive = true,
                Format = dates ? eightCells ? "MMddyyyy" : "MM/dd/yyyy" : null, MaxLength = dates ? null : 64,
                Targets = JsonSerializer.SerializeToElement(new[] { target.Target })
            }).ToArray(), Rules = [], Unmapped = [], Source = JsonSerializer.SerializeToElement(new { }), Extensions = JsonSerializer.SerializeToElement(new { })
        };
        byte[] profileBytes = ProfileParser.SerializeProfile(profile);
        var catalog = new FormCatalogDefinition
        {
            Schema = "pdfform.catalog/1", Template = new("template.pdf", template.ByteCount, template.Sha256),
            Profile = new("profile.json", profileBytes.Length, Convert.ToHexStringLower(SHA256.HashData(profileBytes)), profile.Id, profile.Revision, profile.ProfileVersion),
            Review = new("reviewed", []), Policy = new()
            {
                Id = "configured-acroform-v1", Normalization = "none-v1", CheckboxFields = [],
                TextFields = draft.Targets.Select(target => new TextFieldPolicy
                {
                    Target = target.Target, Page = target.Page ?? throw new InvalidOperationException("Page required."), Rectangle = target.Rectangle.ToArray(),
                    Strategy = strategy, Layout = eightCells ? "eight-date-cells-v1" : "single-line-v1",
                    ResourceName = target.Font!.ResourceName, BaseFont = target.Font.BaseFont, Subtype = target.Font.Subtype,
                    Encoding = target.Font.Encoding, FontSize = target.Font.FontSize ?? throw new InvalidOperationException("Fixed size required."),
                    ProgramSha256 = target.Font.ProgramSha256.Length == 0 ? null : target.Font.ProgramSha256,
                    MetricsSha256 = target.Font.MetricsSha256, Repertoire = strategy == "preserve-embedded-simple-v1" ? "encoded-font-v1" : "ascii-plus-eacute-v1"
                }).ToArray()
            }
        };
        return FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options), profileBytes, template.Sha256, template.ByteCount).Value
            ?? throw new InvalidOperationException("Synthetic extension configuration refused.");
    }

    private static List<PdfStream> ExtensionAppearances(PdfDocument pdf, RenderManifest manifest, bool flatten)
    {
        if (!flatten)
        {
            var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!);
            return manifest.Targets.Keys.Select(key => fields[key].GetWidgets()[0].GetPdfObject()
                .GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!).ToList();
        }
        var result = new List<PdfStream>();
        for (int page = 1; page <= pdf.GetNumberOfPages(); page++)
        {
            var objects = pdf.GetPage(page).GetResources().GetPdfObject().GetAsDictionary(PdfName.XObject);
            if (objects is null) continue;
            foreach (var name in objects.KeySet())
                if (objects.GetAsStream(name) is { } stream && PdfName.Form.Equals(stream.GetAsName(PdfName.Subtype))) result.Add(stream);
        }
        return result;
    }

    private static bool ExtensionExactFonts(VerifiedFillCandidate candidate, FormConfigurationSnapshot configuration, float size, bool flatten)
    {
        var policies = configuration.ParsePolicy().TextFields;
        string expectedProgram = policies[0].Strategy == "preserve-embedded-simple-v1" ? policies[0].ProgramSha256! : ScheduleBEmbeddedFont.ProgramSha256;
        string expectedName = policies[0].Strategy == "preserve-embedded-simple-v1" ? policies[0].BaseFont : ScheduleBEmbeddedFont.Name;
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()));
        var appearances = ExtensionAppearances(pdf, candidate.ManifestForFixture, flatten);
        if (appearances.Count != policies.Length || candidate.Flattened != flatten) return false;
        foreach (var appearance in appearances)
        {
            var sizes = GeneratedContentPolicy.Commands(appearance.GetBytes()).Where(command => command[^1].ToString() == "Tf").ToArray();
            if (sizes.Length == 0) return false;
            foreach (var command in sizes)
            {
                if (command.Count != 3 || command[0] is not PdfName resource || command[1] is not PdfNumber number || number.DoubleValue() != size) return false;
                var font = appearance.GetAsDictionary(PdfName.Resources)?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(resource);
                var descriptor = font?.GetAsDictionary(PdfName.FontDescriptor);
                var program = descriptor?.GetAsStream(PdfName.FontFile2);
                if (font?.GetAsName(PdfName.BaseFont)?.GetValue() != expectedName || !PdfName.TrueType.Equals(font?.GetAsName(PdfName.Subtype))
                    || !PdfName.WinAnsiEncoding.Equals(font?.GetAsName(PdfName.Encoding)) || font!.ContainsKey(PdfName.ToUnicode)
                    || program is null || descriptor!.ContainsKey(PdfName.FontFile) || descriptor.ContainsKey(PdfName.FontFile3)
                    || VerifiedFormRenderer.Digest(program.GetBytes()) != expectedProgram) return false;
            }
        }
        return true;
    }

    private static bool ExtensionRejectFontSwap(VerifiedFillCandidate candidate, string root, string strategy, bool flatten)
    {
        bool isolated = false;
        byte[] changed = ExtensionMutate(candidate, pdf =>
        {
            var appearance = ExtensionAppearances(pdf, candidate.ManifestForFixture, flatten).First();
            string before = VerifiedFormRenderer.Digest(appearance.GetBytes());
            var resource = (PdfName)GeneratedContentPolicy.Commands(appearance.GetBytes()).First(command => command[^1].ToString() == "Tf")[0];
            PdfDictionary replacement;
            if (strategy == "preserve-embedded-simple-v1") replacement = new ScheduleBEmbeddedFont.DocumentFont(pdf).Get(pdf, out _).GetPdfObject();
            else
            {
                var approved = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", "f941.pdf"));
                if (approved.Sha256 != ApprovedTemplates.Form941Sha256) throw new InvalidOperationException("Exact font fixture required.");
                using var source = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(approved)));
                replacement = (PdfDictionary)PdfAcroForm.GetAcroForm(source, false)!.GetPdfObject().GetAsDictionary(PdfName.DR)!
                    .GetAsDictionary(PdfName.Font)!.GetAsDictionary(new PdfName("TimesNewRomanPSMT"))!.CopyTo(pdf);
            }
            appearance.GetAsDictionary(PdfName.Resources)!.GetAsDictionary(PdfName.Font)!.Put(resource, replacement);
            isolated = before == VerifiedFormRenderer.Digest(appearance.GetBytes());
        });
        return isolated && ExtensionVerificationCode(changed, candidate.ManifestForFixture, flatten) == "E_VERIFY_AP_FONT";
    }

    private static bool ExtensionDatePaint(VerifiedFillCandidate candidate, bool eightCells)
    {
        string expectedText = eightCells ? "10202026" : "10/20/2026";
        if (!candidate.ManifestForFixture.Targets.Values.All(target => target.Assignment.Text == expectedText
            && (eightCells ? target.DateCells is { Length: 8 } : target.DateCells is null))) return false;
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()));
        return ExtensionAppearances(pdf, candidate.ManifestForFixture, flatten: false).All(appearance =>
        {
            var commands = GeneratedContentPolicy.Commands(appearance.GetBytes()).ToArray();
            int shows = commands.Count(command => command[^1].ToString() is "Tj" or "TJ");
            int matrices = commands.Count(command => command[^1].ToString() == "Tm");
            return shows == (eightCells ? 8 : 1) && (!eightCells || matrices == 8);
        });
    }

    private static bool ExtensionRejectCellShift(VerifiedFillCandidate candidate)
    {
        bool moved = false;
        byte[] changed = ExtensionMutate(candidate, pdf =>
        {
            var appearance = ExtensionAppearances(pdf, candidate.ManifestForFixture, flatten: false).First();
            string content = Encoding.ASCII.GetString(appearance.GetBytes());
            var matrix = new Regex(@"\b1\s+0\s+0\s+1\s+(?<x>[-+]?[0-9]+(?:\.[0-9]+)?)\s+(?<y>[-+]?[0-9]+(?:\.[0-9]+)?)\s+Tm\b");
            string altered = matrix.Replace(content, match =>
            {
                moved = true;
                double x = double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture) + 1;
                return "1 0 0 1 " + x.ToString("0.#########", CultureInfo.InvariantCulture) + " " + match.Groups["y"].Value + " Tm";
            }, 1);
            appearance.SetData(Encoding.ASCII.GetBytes(altered));
        });
        return moved && ExtensionVerificationCode(changed, candidate.ManifestForFixture, flatten: false) == "E_VERIFY_DATE_POSITION";
    }

    private static bool ExtensionRejectBlankDefault(VerifiedFillCandidate candidate, float size)
    {
        bool blankUnchanged = false;
        byte[] changed = ExtensionMutate(candidate, pdf =>
        {
            var field = Fields(PdfAcroForm.GetAcroForm(pdf, false)!).OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
            var appearance = field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!;
            string before = VerifiedFormRenderer.Digest(appearance.GetBytes());
            field.GetPdfObject().Put(PdfName.DA, new PdfString("/PdfFormScheduleB " + (size == 1 ? "10" : "1") + " Tf 0 g"));
            blankUnchanged = field.GetPdfObject().GetAsString(PdfName.V)?.ToUnicodeString() == ""
                && before == VerifiedFormRenderer.Digest(appearance.GetBytes())
                && !GeneratedContentPolicy.Commands(appearance.GetBytes()).Any(command => command[^1].ToString() is "Tj" or "TJ");
        });
        return blankUnchanged && ExtensionVerificationCode(changed, candidate.ManifestForFixture, flatten: false) == "E_VERIFY_FIELD_FONT_DEFAULT";
    }

    private static byte[] ExtensionBytes(TemplateSnapshot snapshot)
    {
        using var input = snapshot.OpenRead();
        using var output = new MemoryStream();
        input.CopyTo(output); return output.ToArray();
    }

    private static byte[] ExtensionMutate(VerifiedFillCandidate candidate, Action<PdfDocument> mutation)
    {
        using var output = new BoundedPdfMemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()), new PdfWriter(output))) mutation(pdf);
        return output.ToArray();
    }

    private static string? ExtensionVerificationCode(byte[] bytes, RenderManifest manifest, bool flatten)
    {
        try
        {
            if (flatten) VerifiedFormRenderer.VerifyFlattened(bytes, manifest);
            else VerifiedFormRenderer.VerifyEditable(bytes, manifest);
            return null;
        }
        catch (VerifiedFillException error) { return error.Code; }
    }

    // Additive correction registration: RunPreservedDefaultCorrection(check, root).
    // Exactly42 checks:3 sizes *2 blank/populated states *(1 no-op +2 nodes *3 faults).
    internal static void RunPreservedDefaultCorrection(Action<bool, string> check, string root)
    {
        foreach (float size in new[] { 1f, 10, 72 })
        foreach (bool populated in new[] { false, true })
        {
            var template = CorrectionSplitWidgets(ExtensionTemplate(root, "preserve-embedded-simple-v1", size));
            var configuration = ExtensionConfiguration(template, "preserve-embedded-simple-v1");
            var values = configuration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => populated ? "11" : "", StringComparer.Ordinal);
            var result = VerifiedFillPipeline.Prepare(template, configuration, Data(configuration, values), flatten: false);
            var candidate = result.Candidate ?? throw new InvalidOperationException("Preserved-default positive setup failed.");
            string fieldKey = candidate.ManifestForFixture.Targets.Keys.Order(StringComparer.Ordinal).First();
            byte[] roundtrip = CorrectionMutate(candidate.CopyForFixture(), pdf => { _ = Fields(PdfAcroForm.GetAcroForm(pdf, false)!); });
            string expectedState = CorrectionInvariant(roundtrip, candidate.ManifestForFixture);
            var fit = VerifiedFillPipeline.CheckValueFit(template, configuration, configuration.ParseProfile().Keys[0].Key,
                JsonSerializer.SerializeToElement(populated ? "11" : ""));
            check(result.ReadyToPublish && CorrectionDistinctWidgets(roundtrip)
                && ExtensionVerificationCode(roundtrip, candidate.ManifestForFixture, flatten: false) is null
                && CorrectionDefaultsCode(roundtrip, candidate.ManifestForFixture, fieldKey) is null
                && CorrectionApPass(roundtrip, candidate.ManifestForFixture)
                && CorrectionInvariant(candidate.CopyForFixture(), candidate.ManifestForFixture) == expectedState
                && fit.All(finding => finding.Severity != "error"),
                "v7/preserved-default-no-op/" + size + "/" + populated);

            for (int nodeIndex = 0; nodeIndex < 2; nodeIndex++)
            for (int fault = 0; fault < 3; fault++)
            {
                byte[] changed = CorrectionMutate(roundtrip, pdf =>
                {
                    var field = Fields(PdfAcroForm.GetAcroForm(pdf, false)!)[fieldKey];
                    var node = nodeIndex == 0 ? field.GetPdfObject() : field.GetWidgets()[0].GetPdfObject();
                    if (fault == 0)
                        node.Put(PdfName.DA, new PdfString("/PdfFormPreserved " + (size == 1 ? "10" : "1") + " Tf 0 g"));
                    else if (fault == 1)
                        node.Put(PdfName.DA, new PdfString("/PdfFormPreserved " + size.ToString(CultureInfo.InvariantCulture) + " Tf .5 g"));
                    else
                    {
                        // A valid alternate program isolates default identity from parser failure.
                        var other = new ScheduleBEmbeddedFont.DocumentFont(pdf).Get(pdf, out _);
                        var fonts = new PdfDictionary(); fonts.Put(NativeTextAppearance.PreservedResourceName, other.GetPdfObject());
                        var resources = new PdfDictionary(); resources.Put(PdfName.Font, fonts);
                        node.Put(PdfName.DR, resources);
                    }
                });
                string expectedCode = fault == 2 ? "E_VERIFY_AP_FONT" : "E_VERIFY_FIELD_FONT_DEFAULT";
                check(CorrectionInvariant(changed, candidate.ManifestForFixture) == expectedState
                    && CorrectionApPass(changed, candidate.ManifestForFixture)
                    && CorrectionDefaultsCode(changed, candidate.ManifestForFixture, fieldKey) == expectedCode
                    && ExtensionVerificationCode(changed, candidate.ManifestForFixture, flatten: false) == expectedCode,
                    "v7/preserved-default-tamper/" + size + "/" + populated + "/" + nodeIndex + "/" + fault);
            }
        }
    }

    private static TemplateSnapshot CorrectionSplitWidgets(TemplateSnapshot snapshot)
    {
        using var output = new BoundedPdfMemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(snapshot)), new PdfWriter(output)))
        {
            var form = PdfAcroForm.GetAcroForm(pdf, false)!;
            foreach (var field in Fields(form).Values)
            {
                var canonical = field.GetPdfObject();
                if (!PdfName.Widget.Equals(canonical.GetAsName(PdfName.Subtype))) continue;
                var widget = new PdfDictionary(); widget.Put(PdfName.Type, PdfName.Annot); widget.Put(PdfName.Subtype, PdfName.Widget);
                foreach (var key in new[] { PdfName.Rect, PdfName.AP, PdfName.AS, PdfName.F, PdfName.BS, PdfName.Border, PdfName.MK, PdfName.P, PdfName.DA, PdfName.DR })
                    if (canonical.Get(key, true) is { } value) widget.Put(key, value);
                widget.Put(PdfName.Parent, canonical); widget.MakeIndirect(pdf);
                var kids = new PdfArray(); kids.Add(widget); canonical.Put(PdfName.Kids, kids);
                bool replaced = false;
                for (int page = 1; page <= pdf.GetNumberOfPages(); page++)
                {
                    var annotations = pdf.GetPage(page).GetPdfObject().GetAsArray(PdfName.Annots);
                    if (annotations is null) continue;
                    for (int i = 0; i < annotations.Size(); i++)
                        if (annotations.GetAsDictionary(i)?.GetIndirectReference()?.Equals(canonical.GetIndirectReference()) == true)
                        {
                            annotations.Set(i, widget); widget.Put(PdfName.P, pdf.GetPage(page).GetPdfObject()); replaced = true;
                        }
                }
                if (!replaced) throw new InvalidOperationException("Synthetic merged widget page reference missing.");
                foreach (var key in new[] { PdfName.Type, PdfName.Subtype, PdfName.Rect, PdfName.AP, PdfName.AS, PdfName.F, PdfName.BS, PdfName.Border, PdfName.MK, PdfName.P }) canonical.Remove(key);
            }
        }
        return TemplateSnapshot.FromBytes(output.ToArray());
    }

    private static bool CorrectionDistinctWidgets(byte[] bytes)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        return Fields(PdfAcroForm.GetAcroForm(pdf, false)!).Values.All(field =>
            !ReferenceEquals(field.GetPdfObject(), field.GetWidgets()[0].GetPdfObject())
            && !Equals(field.GetPdfObject().GetIndirectReference(), field.GetWidgets()[0].GetPdfObject().GetIndirectReference()));
    }

    private static byte[] CorrectionMutate(byte[] bytes, Action<PdfDocument> mutation)
    {
        using var output = new BoundedPdfMemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes), new PdfWriter(output))) mutation(pdf);
        return output.ToArray();
    }

    private static string CorrectionInvariant(byte[] bytes, RenderManifest manifest)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!);
        var rows = new List<string>();
        foreach (string key in manifest.Targets.Keys.Order(StringComparer.Ordinal))
        {
            var field = fields[key]; var widget = field.GetWidgets()[0].GetPdfObject();
            var appearance = widget.GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!;
            var fonts = appearance.GetAsDictionary(PdfName.Resources)?.GetAsDictionary(PdfName.Font);
            var graph = new List<string>();
            if (fonts is not null)
                foreach (var name in fonts.KeySet().OrderBy(name => name.GetValue(), StringComparer.Ordinal))
                {
                    var font = fonts.GetAsDictionary(name)!;
                    var program = font.GetAsDictionary(PdfName.FontDescriptor)?.GetAsStream(PdfName.FontFile2);
                    graph.Add(name.GetValue() + ":" + NativeTextAppearance.SourceMetricsSha256(font) + ":"
                        + (program is null ? "<none>" : VerifiedFormRenderer.Digest(program.GetBytes())));
                }
            rows.Add(key + "|" + field.GetPdfObject().Get(PdfName.V)?.ToString() + "|" + field.GetPdfObject().Get(PdfName.DV)?.ToString()
                + "|" + widget.GetAsArray(PdfName.Rect) + "|" + VerifiedFormRenderer.Digest(appearance.GetBytes()) + "|" + string.Join(",", graph));
        }
        return string.Join("\n", rows);
    }

    private static string? CorrectionDefaultsCode(byte[] bytes, RenderManifest manifest, string key)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        var field = Fields(PdfAcroForm.GetAcroForm(pdf, false)!)[key];
        try { NativeTextAppearance.VerifyPreservedDefaults(field, manifest.Targets[key].Font!, new()); return null; }
        catch (VerifiedFillException error) { return error.Code; }
    }

    private static bool CorrectionApPass(byte[] bytes, RenderManifest manifest)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!);
        var context = new NativeTextAppearance.VerificationContext();
        foreach (var pair in manifest.Targets)
            NativeTextAppearance.Verify(fields[pair.Key].GetWidgets()[0].GetPdfObject()
                .GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!, pair.Value, context);
        return true;
    }

    private static void VerifySharedProgram(Action<bool, string> check, VerifiedFillCandidate candidate, string label)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()));
        var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!);
        var context = new NativeTextAppearance.VerificationContext();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in candidate.ManifestForFixture.Targets)
        {
            var dictionary = ScheduleBEmbeddedFont.VerifyDefaults(pdf, fields[pair.Key], pair.Value.Font!, context);
            identities.Add(dictionary.GetIndirectReference()?.ToString()
                ?? throw new InvalidOperationException("The shared imported font must have a document identity."));
            var stream = dictionary.GetAsDictionary(PdfName.FontDescriptor)!.GetAsStream(PdfName.FontFile2)!;
            check(stream.GetBytes().Length == ScheduleBEmbeddedFont.ProgramBytes
                && VerifiedFormRenderer.Digest(stream.GetBytes()) == ScheduleBEmbeddedFont.ProgramSha256,
                "full unchanged pinned font bytes are embedded: " + label);
        }
        check(identities.Count == 1, "all configured defaults share one document font graph: " + label);
    }

    private static void RejectDefaultMutations(Action<bool, string> check, VerifiedFillCandidate candidate)
    {
        foreach (int nodeIndex in new[] { 0, 1, 2 })
        foreach (bool removeResource in new[] { false, true })
        {
            byte[] changed;
            string beforeAppearance, afterAppearance;
            using (var output = new MemoryStream())
            {
                using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()), new PdfWriter(output)))
                {
                    var form = PdfAcroForm.GetAcroForm(pdf, false)!;
                    var field = Fields(form).OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
                    var widget = field.GetWidgets()[0].GetPdfObject();
                    beforeAppearance = VerifiedFormRenderer.Digest(widget.GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!.GetBytes());
                    var node = new[] { form.GetPdfObject(), field.GetPdfObject(), widget }[nodeIndex];
                    if (removeResource) node.Remove(PdfName.DR);
                    else node.Put(PdfName.DA, new PdfString("/PdfFormScheduleB 12.5 Tf .5 g"));
                    afterAppearance = VerifiedFormRenderer.Digest(widget.GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!.GetBytes());
                }
                changed = output.ToArray();
            }
            string? code = null;
            try { VerifiedFormRenderer.VerifyEditable(changed, candidate.ManifestForFixture); }
            catch (VerifiedFillException error) { code = error.Code; }
            check(beforeAppearance == afterAppearance && code == (removeResource ? "E_VERIFY_FIELD_FONT_RESOURCE" : "E_VERIFY_FIELD_FONT_DEFAULT"),
                "blank q/Q appearance cannot mask damaged font defaults/resources: " + nodeIndex + "/" + removeResource);
        }
    }

    private static void RejectSourceIdentityClaims(Action<bool, string> check, TemplateSnapshot template,
        FormConfigurationSnapshot configuration, Dictionary<string, string> values)
    {
        byte[] profile;
        using (var input = configuration.OpenProfileRead())
        using (var output = new MemoryStream()) { input.CopyTo(output); profile = output.ToArray(); }
        foreach (string claim in new[] { "metricsSha256", "programSha256" })
        {
            using var input = configuration.OpenCatalogRead();
            var catalog = JsonNode.Parse(input)!;
            catalog["policy"]!["textFields"]![0]![claim] = claim == "programSha256"
                ? ScheduleBEmbeddedFont.ProgramSha256 : new string('0', 64);
            var changed = FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options), profile,
                template.Sha256, template.ByteCount).Value ?? throw new InvalidOperationException("Synthetic identity claim setup failed.");
            var refused = VerifiedFillPipeline.Prepare(template, changed, Data(changed, values), flatten: false);
            check(refused.ExitCode == 3 && !refused.ReadyToPublish
                && refused.Findings.Any(finding => finding.Code == "E_CONFIG_FONT_IDENTITY"),
                "pinned output font cannot override a false source identity claim: " + claim);
        }
    }

    private static TemplateSnapshot SyntheticTemplate(float firstSize, float secondSize)
    {
        using var output = new MemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(Fixture.Make()), new PdfWriter(output)))
        {
            var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < fields.Length; i++)
            {
                float size = i == 0 ? firstSize : secondSize;
                var field = fields[i].Value;
                field.GetPdfObject().Put(PdfName.DA, new PdfString("/Helvetica " + size.ToString(CultureInfo.InvariantCulture) + " Tf 0 g"));
                field.GetPdfObject().Put(PdfName.Ff, new PdfNumber(0));
                field.GetPdfObject().Put(PdfName.Q, new PdfNumber(0));
                field.GetPdfObject().Put(PdfName.MaxLen, new PdfNumber(64));
                field.GetWidgets()[0].GetPdfObject().Put(PdfName.Rect, new PdfArray(new double[] { 10 + 290 * i, 10, 280 + 290 * i, 150 }));
            }
        }
        return TemplateSnapshot.FromBytes(output.ToArray());
    }

    private static FormConfigurationSnapshot Configuration(TemplateSnapshot template)
    {
        var draft = new PdfTemplateInspector().DiscoverConfiguration(template);
        var profile = new ProfileDefinition
        {
            Schema = "pdfform.profile/3", Id = "synthetic-render-font", Revision = "one", ProfileVersion = 1,
            Title = "Synthetic font rendering", Template = new()
            {
                Sha256Allowlist = [template.Sha256], Title = draft.TemplateTitle, PageCount = draft.Pages,
                StructureFingerprintV1 = draft.StructureFingerprintV1 ?? throw new InvalidOperationException("Synthetic fingerprint missing.")
            }, Capabilities = ["text.singleWidget"], FilingYears = [], Options = new() { ZeroAmounts = "blank" },
            Keys = draft.Targets.Select(target => new KeyDefinition
            {
                Key = target.Key, Kind = "text", Required = false, Sensitive = true, MaxLength = 64,
                Targets = JsonSerializer.SerializeToElement(new[] { target.Target })
            }).ToArray(), Rules = [], Unmapped = [], Source = JsonSerializer.SerializeToElement(new { }), Extensions = JsonSerializer.SerializeToElement(new { })
        };
        byte[] profileBytes = ProfileParser.SerializeProfile(profile);
        var catalog = new
        {
            schema = "pdfform.catalog/1", template = new { path = "template.pdf", bytes = template.ByteCount, sha256 = template.Sha256 },
            profile = new { path = "profile.json", bytes = profileBytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(profileBytes)), id = profile.Id, revision = profile.Revision, profileVersion = profile.ProfileVersion },
            review = new { state = "reviewed", unresolved = Array.Empty<object>() }, policy = new
            {
                id = "configured-acroform-v1", normalization = "none-v1",
                textFields = draft.Targets.Select(target => new
                {
                    target = target.Target, page = target.Page, rectangle = target.Rectangle, strategy = ScheduleBEmbeddedFont.ConfiguredPolicy,
                    layout = "single-line-v1", resourceName = target.Font!.ResourceName, baseFont = target.Font.BaseFont,
                    subtype = target.Font.Subtype, encoding = target.Font.Encoding, fontSize = target.Font.FontSize,
                    metricsSha256 = target.Font.MetricsSha256, repertoire = "ascii-plus-eacute-v1"
                }).ToArray(), checkboxFields = Array.Empty<object>()
            }
        };
        return FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options), profileBytes, template.Sha256, template.ByteCount).Value
            ?? throw new InvalidOperationException("Synthetic rendering configuration was refused.");
    }

    private static Dictionary<string, PdfFormField> Fields(PdfAcroForm form) => form.GetAllFormFields()
        .Where(pair => pair.Value.IsTerminalFormField()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static byte[] Data(FormConfigurationSnapshot configuration, Dictionary<string, string> values) =>
        JsonSerializer.SerializeToUtf8Bytes(new { schema = "pdfform.data/3", profile = configuration.ParseProfile().Identity, values }, StrictJson.Options);
}
