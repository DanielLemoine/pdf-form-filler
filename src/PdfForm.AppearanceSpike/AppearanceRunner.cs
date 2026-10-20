using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using iText.Forms;
using iText.Forms.Fields;
using iText.IO.Font;
using iText.Kernel.Font;
using Rectangle = iText.Kernel.Geom.Rectangle;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Xobject;
using PdfForm.Pdf;

namespace PdfForm.AppearanceSpike;

/// <summary>Isolated synthetic engine investigation, deliberately not a public filler or a profile implementation.</summary>
internal sealed class AppearanceRunner(string fixtureRoot, string output, FixtureManifest manifest)
{
    private const float FontSize = 10;
    private static readonly PdfName OffState = new("Off");
    private const long OutputLimit = 128L * 1024 * 1024;
    private readonly List<CheckResult> checks = [];
    private readonly List<FontEvidence> fonts = [];
    private readonly List<ArtifactEvidence> artifacts = [];
    private readonly HashSet<string> verifiedEditables = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, FontProgram> OutlineFonts = new(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> Approved = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["f941"] = "38a3d8cf7a455101d52543c8c48e66202bc25c189ead878627e35c797c88e2ad",
        ["f941sb"] = "95a62159c8367fc50e6b5445f7d80f9a00ac7cdf56d224240fc2776cb545ab63"
    };

    internal SpikeReport Run()
    {
        foreach (var fixture in manifest.Templates)
        {
            try { RunTemplate(fixture); }
            catch (Exception ex) { checks.Add(new("template-execution", "fail", SafeCode(ex), fixture.Id)); }
            if (Directory.EnumerateFiles(output).Sum(p => new FileInfo(p).Length) > OutputLimit)
                throw new SpikeFailure("E_SPIKE_OUTPUT_CAP");
        }
        bool failures = checks.Any(c => c.Status == "fail");
        return new("pdfform.appearance-spike-result/1", failures ? "incomplete" : "completed",
            failures ? "no-go-until-recorded-failures-resolved" : "fix-font-portability-and-independent-visual-gates",
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            typeof(PdfDocument).Assembly.GetName().Version?.ToString() ?? "unknown",
            checks.ToArray(), fonts.ToArray(), artifacts.ToArray(),
            ["Schedule B TimesLTStd-Roman is not embedded: authorize an exact distributable font resource or an explicit reviewed substitute; no silent substitution is accepted.",
             "Independent raster review of every candidate and date slash geometry is required; this program does not claim visual acceptance.",
             "Windows/Adobe editable reset, save, reload and flattened review remain required.",
             "Physical-print, licensing, publication, supported-host and production-filler acceptance remain not run."]);
    }

    private void RunTemplate(TemplateFixture fixture)
    {
        Require(Approved.TryGetValue(fixture.Id, out var hash) && hash == fixture.Sha256, "E_SPIKE_TEMPLATE_NOT_APPROVED");
        string path = Path.GetFullPath(Path.Combine(fixtureRoot, fixture.File));
        Require(path.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal), "E_SPIKE_FIXTURE_PATH");
        var snapshot = TemplateSnapshot.Load(path);
        using (var read = snapshot.OpenRead()) Require(Hash(read) == fixture.Sha256, "E_SPIKE_TEMPLATE_HASH");
        var inspection = new PdfTemplateInspector().Inspect(snapshot);
        Require(inspection.ExactApprovedTemplate, "E_SPIKE_TEMPLATE_NOT_APPROVED");
        checks.Add(new("exact-template-identity", "pass", "OK", fixture.Id));
        int maximumNameLength;
        // Read-only native metrics remain useful even when active-content classification blocks mutation.
        // This does not execute document actions and does not grant a rendering exception.
        using (var read = snapshot.OpenRead())
        using (var pdf = new PdfDocument(new PdfReader(read))) maximumNameLength = Characterize(fixture, pdf);
        Require(inspection.InitialStructureSupported, "E_SPIKE_STRUCTURE_UNSUPPORTED");
        checks.Add(new("supported-structure", "pass", "OK", fixture.Id));

        foreach (string scenario in fixture.DateTarget is null ? new[] { "native-fixed-all-on", "maximum-width", "cleared" } : new[] { "native-fixed-all-on", "date-cells-all-on", "maximum-width", "cleared" })
        {
            string? activeField = null;
            try
            {
                // The only recycled input is an owned, synthetic candidate inside this private engine test.
                // There is no production bypass or arbitrary external-input fill mode.
                string? derived = scenario == "cleared" ? Path.Combine(output, fixture.Id + "-native-fixed-all-on.editable.pdf") : null;
                Require(derived is null || verifiedEditables.Contains(derived), "E_SPIKE_TRANSITION_SOURCE_NOT_VERIFIED");
                var expected = fixture.Targets.ToDictionary(t => t.Name,
                    t => scenario == "cleared" ? "" : t.SyntheticValue, StringComparer.Ordinal);
                if (scenario == "maximum-width") expected[fixture.NameTarget] = new string('W', maximumNameLength);
                var selected = fixture.Targets.Where(t => t.Type == "Btn").ToDictionary(t => t.Name,
                    _ => scenario != "cleared", StringComparer.Ordinal);
                string editable = Path.Combine(output, fixture.Id + "-" + scenario + ".editable.pdf");
                string flattened = Path.Combine(output, fixture.Id + "-" + scenario + ".flattened.pdf");
                int transitions;
                using (var writerStream = new FileStream(editable, FileMode.CreateNew, FileAccess.Write))
                using (var pdf = derived is null
                    ? ApprovedTemplateNormalizer.OpenForNormalization(snapshot, writerStream)
                    : new PdfDocument(new PdfReader(derived), new PdfWriter(writerStream)))
                {
                    pdf.SetFlushUnusedObjects(false);
                    var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw new SpikeFailure("E_SPIKE_FORM_MISSING");
                    Require(!form.GetPdfObject().ContainsKey(PdfName.XFA), "E_SPIKE_XFA_REMAINS");
                    Require(pdf.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Names)?.ContainsKey(PdfName.JavaScript) != true,
                        "E_SPIKE_CATALOG_SCRIPT_REMAINS");
                    form.GetPdfObject().Put(PdfName.NeedAppearances, PdfBoolean.FALSE);
                    var fields = TerminalFields(form);
                    Require(fields.Count == fixture.Targets.Length, "E_SPIKE_FIELD_COUNT");
                    transitions = 0;
                    foreach (var target in fixture.Targets)
                    {
                        activeField = target.Name;
                        Require(fields.TryGetValue(target.Name, out var field), "E_SPIKE_FIELD_MISSING");
                        Require(field!.GetWidgets().Count == 1, "E_SPIKE_WIDGET_COUNT");
                        AssertRectangle(target, field);
                        if (target.Type == "Btn")
                        {
                            RepairOff(pdf, field, target);
                            // Each of the 28 individual checkboxes is exercised twice in both directions.
                            for (int i = 0; i < 2; i++)
                            {
                                SetButton(field, target, true); VerifyButton(field, target, true);
                                SetButton(field, target, false); VerifyButton(field, target, false); transitions += 2;
                            }
                            SetButton(field, target, selected[target.Name]);
                        }
                        else
                        {
                            var font = ResolveFont(pdf, fixture);
                            string value = expected[target.Name];
                            CheckFit(field, target, value, font);
                            field.SetValue(value, font, FontSize);
                            ClearResetData(field, false);
                            if (scenario == "date-cells-all-on" && target.Name == fixture.DateTarget)
                                SetDateAppearance(pdf, field, target, value, font, fixture);
                        }
                    }
                    activeField = null;
                }
                // Mandatory ordering: serialize and close editable, independently reopen and verify all assignments,
                // then flatten, then independently reopen and inspect final structure.
                VerifyEditable(editable, fixture, expected, selected);
                verifiedEditables.Add(editable);
                checks.Add(new(scenario + ":editable-readback-before-flatten", "pass", "OK", fixture.Id));
                FlattenVerified(editable, flattened, fixture);
                string assignments = fixture.Id + "-" + scenario + ".assignments.json";
                File.WriteAllText(Path.Combine(output, assignments), JsonSerializer.Serialize(new
                {
                    schema = "pdfform.synthetic-assignment-manifest/1", template = fixture.Id, scenario,
                    fictionalOnly = true, text = expected, checkboxes = selected
                }, Program.JsonOptions));
                artifacts.Add(new(fixture.Id, scenario, Path.GetFileName(editable), Path.GetFileName(flattened),
                    HashFile(editable), HashFile(flattened), fixture.Targets.Length, fixture.Targets.Length, transitions, assignments));
                checks.Add(new(scenario + ":flattened-structure", "pass", "OK", fixture.Id));
            }
            catch (Exception ex) { checks.Add(new(scenario, "fail", SafeCode(ex), fixture.Id, activeField)); }
        }
    }

    private int Characterize(TemplateFixture fixture, PdfDocument pdf)
    {
        var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw new SpikeFailure("E_SPIKE_FORM_MISSING");
        var fields = TerminalFields(form);
        var font = ResolveFont(pdf, fixture);
        bool digits = "0123456789".EnumerateRunes().All(r => font.ContainsGlyph(r.Value));
        bool latin = "FICTIONAL TEST EMPLOYER -0.02".EnumerateRunes().All(r => font.ContainsGlyph(r.Value));
        bool missingEmoji = !font.ContainsGlyph(0x1F680);
        var digitBounds = InkBounds(font, "0123456789");
        fonts.Add(new(fixture.Id, fixture.ExpectedFont, font.IsEmbedded(), digits, latin, missingEmoji, FontSize,
            font.IsEmbedded() ? "embedded-template-resource" : "unembedded-review-required",
            digitBounds.Exact ? "embedded-font-program-glyph-boxes" : "font-descriptor-fallback-unproven-outline",
            digitBounds.Top - digitBounds.Bottom));
        Require(digits && latin && missingEmoji, "E_SPIKE_GLYPH_PRECONDITION");
        checks.Add(new("font-glyph-probe", "pass", "OK", fixture.Id));
        foreach (var t in fixture.Targets.Where(t => t.Type == "Btn"))
        {
            var field = fields[t.Name];
            var n = field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)?.GetAsDictionary(PdfName.N);
            Require(n is not null && !n.ContainsKey(OffState) && n.GetAsStream(new PdfName(t.OnState!)) is not null,
                "E_SPIKE_EXPECTED_MISSING_NORMAL_OFF");
        }
        checks.Add(new("all-original-checkbox-normal-off-absent", "pass", "OK", fixture.Id));
        var target = fixture.Targets.Single(t => t.Name == fixture.NameTarget);
        var fieldForName = fields[target.Name];
        ExpectReject("unsupported-glyph", () => CheckFit(fieldForName, target, "FICTIONAL 🚀", font), "E_SPIKE_GLYPH_UNSUPPORTED", fixture.Id);
        ExpectReject("fixed10-long-text-overflow", () => CheckFit(fieldForName, target, new string('W', 256), font), "E_SPIKE_TEXT_OVERFLOW", fixture.Id);
        foreach (var t in fixture.Targets.Where(t => (t.Flags & (1 << 24)) != 0))
            ExpectReject("comb-maxlength-reject", () => CheckFit(fields[t.Name], t, new string('1', t.MaxLength + 1), font), "E_SPIKE_MAX_LENGTH", fixture.Id, t.Name);
        // The largest fitting ASCII W probe is measured at 10pt and never shrunk or truncated.
        int max = 0;
        for (int count = 1; count <= 256; count++)
        {
            try { CheckFit(fieldForName, target, new string('W', count), font); max = count; }
            catch (SpikeFailure ex) when (ex.Code is "E_SPIKE_TEXT_OVERFLOW" or "E_SPIKE_MAX_LENGTH") { break; }
        }
        Require(max > 0, "E_SPIKE_NO_TEXT_FITS");
        checks.Add(new("fixed10-largest-W-probe-" + max.ToString(CultureInfo.InvariantCulture), "pass", "OK", fixture.Id));
        return max;
    }

    private static Dictionary<string, PdfFormField> TerminalFields(PdfAcroForm form) => form.GetAllFormFields()
        .Where(p => p.Value.IsTerminalFormField()).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static PdfFont ResolveFont(PdfDocument pdf, TemplateFixture fixture)
    {
        var dict = pdf.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.AcroForm)?.GetAsDictionary(PdfName.DR)
            ?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(new PdfName(fixture.ExpectedFont))
            ?? throw new SpikeFailure("E_SPIKE_FONT_RESOURCE_MISSING");
        Require(dict.GetAsName(PdfName.BaseFont)?.GetValue() == fixture.ExpectedFont, "E_SPIKE_FONT_IDENTITY");
        return PdfFontFactory.CreateFont(dict);
    }

    private static void CheckFit(PdfFormField field, TargetFixture target, string value, PdfFont font)
    {
        if (value.Length == 0) return;
        var runes = value.EnumerateRunes().ToArray();
        Require(runes.All(r => font.ContainsGlyph(r.Value)), "E_SPIKE_GLYPH_UNSUPPORTED");
        Require(!value.Any(c => char.IsControl(c)), "E_SPIKE_CONTROL_TEXT");
        if (target.MaxLength > 0) Require(runes.Length <= target.MaxLength, "E_SPIKE_MAX_LENGTH");
        var rect = field.GetWidgets()[0].GetRectangle().ToRectangle();
        const float padding = 1f;
        if ((target.Flags & (1 << 24)) != 0)
        {
            Require(target.MaxLength > 0, "E_SPIKE_COMB_CAPACITY");
            float cellWidth = rect.GetWidth() / target.MaxLength;
            Require(runes.All(r => font.GetWidth(r.Value, FontSize) <= cellWidth - padding), "E_SPIKE_COMB_OVERFLOW");
        }
        else Require(font.GetWidth(value, FontSize) <= rect.GetWidth() - 2 * padding, "E_SPIKE_TEXT_OVERFLOW");
        // This is a measured feasibility precheck; raster review still verifies actual painting and clipping.
        var ink = InkBounds(font, value);
        Require(ink.Top - ink.Bottom <= rect.GetHeight() - 1, "E_SPIKE_VERTICAL_OVERFLOW");
    }

    private static (float Bottom, float Top, bool Exact) InkBounds(PdfFont font, string value)
    {
        // A PdfFont reconstructed from /Widths and /FontDescriptor can return whole-font ascender/descender
        // rather than the actual entered glyph bounds. Parse the exact already-embedded TTF for that check.
        // No workstation font lookup, font replacement, or additional font embedding takes place here.
        var stream = font.GetPdfObject().GetAsDictionary(PdfName.FontDescriptor)?.GetAsStream(PdfName.FontFile2);
        if (stream is null) return (font.GetDescent(value, FontSize), font.GetAscent(value, FontSize), false);
        var bytes = stream.GetBytes();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!OutlineFonts.TryGetValue(hash, out var program))
        {
            program = FontProgramFactory.CreateFont(bytes, false);
            OutlineFonts.Add(hash, program);
        }
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        foreach (var rune in value.EnumerateRunes())
        {
            var box = program.GetGlyph(rune.Value)?.GetBbox();
            if (box is null) continue; // Space and other empty-outline glyphs contribute advance width only.
            bottom = Math.Min(bottom, box[1] * FontSize / 1000);
            top = Math.Max(top, box[3] * FontSize / 1000);
        }
        return float.IsFinite(bottom) ? (bottom, top, true) : (0, 0, true);
    }

    private static void AssertRectangle(TargetFixture target, PdfFormField field)
    {
        var r = field.GetWidgets()[0].GetRectangle().ToRectangle();
        float[] actual = [r.GetLeft(), r.GetBottom(), r.GetRight(), r.GetTop()];
        Require(actual.Zip(target.Rect).All(p => Math.Abs(p.First - p.Second) < .002), "E_SPIKE_GEOMETRY_MISMATCH");
    }

    private static void RepairOff(PdfDocument pdf, PdfFormField field, TargetFixture target)
    {
        var widget = field.GetWidgets()[0].GetPdfObject();
        var ap = widget.GetAsDictionary(PdfName.AP) ?? throw new SpikeFailure("E_SPIKE_CHECKBOX_AP_MISSING");
        var normal = ap.GetAsDictionary(PdfName.N) ?? throw new SpikeFailure("E_SPIKE_CHECKBOX_N_MISSING");
        Require(normal.GetAsStream(new PdfName(target.OnState!)) is not null, "E_SPIKE_CHECKBOX_ON_MISSING");
        var rect = field.GetWidgets()[0].GetRectangle().ToRectangle();
        var off = new PdfFormXObject(new Rectangle(rect.GetWidth(), rect.GetHeight()));
        new PdfCanvas(off, pdf).SaveState().RestoreState().Release();
        normal.Put(OffState, off.GetPdfObject());
        // Off is newly authored empty normal appearance, never copied from pressed /AP/D/Off.
    }

    private static void ClearResetData(PdfFormField field, bool button)
    {
        var dict = field.GetPdfObject();
        dict.Put(PdfName.DV, button ? OffState : new PdfString(""));
        dict.Remove(PdfName.RV);
        foreach (var widget in field.GetWidgets())
        {
            var wd = widget.GetPdfObject();
            wd.Remove(PdfName.RV);
            if (!ReferenceEquals(wd, dict)) { wd.Remove(PdfName.DV); wd.Remove(PdfName.V); }
        }
    }

    private static void SetButton(PdfFormField field, TargetFixture target, bool selected)
    {
        var state = selected ? new PdfName(target.OnState!) : OffState;
        field.GetPdfObject().Put(PdfName.V, state);
        field.GetWidgets()[0].GetPdfObject().Put(PdfName.AS, state);
        ClearResetData(field, true);
    }

    private static void VerifyButton(PdfFormField field, TargetFixture target, bool selected)
    {
        var expected = selected ? new PdfName(target.OnState!) : OffState;
        var wd = field.GetWidgets()[0].GetPdfObject();
        Require(expected.Equals(field.GetPdfObject().GetAsName(PdfName.V)) && expected.Equals(wd.GetAsName(PdfName.AS)), "E_SPIKE_CHECKBOX_VALUE_STATE");
        var normal = wd.GetAsDictionary(PdfName.AP)?.GetAsDictionary(PdfName.N);
        Require(normal?.GetAsStream(expected) is not null, "E_SPIKE_CHECKBOX_SELECTED_AP");
        var onBytes = normal!.GetAsStream(new PdfName(target.OnState!))?.GetBytes();
        Require(onBytes is not null && Convert.ToHexString(SHA256.HashData(onBytes)).ToLowerInvariant() == target.OnAppearanceSha256,
            "E_SPIKE_ON_AP_CHANGED");
        if (!selected)
        {
            string content = Encoding.ASCII.GetString(normal!.GetAsStream(expected).GetBytes()).Trim();
            Require(Regex.IsMatch(content, @"\Aq\s+Q\z", RegexOptions.CultureInvariant), "E_SPIKE_OFF_NOT_EMPTY");
        }
        Require(OffState.Equals(field.GetPdfObject().GetAsName(PdfName.DV)), "E_SPIKE_CHECKBOX_RESET_DEFAULT");
    }

    private void SetDateAppearance(PdfDocument pdf, PdfFormField field, TargetFixture target, string value, PdfFont font, TemplateFixture fixture)
    {
        var g = manifest.DateGeometry;
        Require(g.TemplateSha256 == fixture.Sha256 && g.Field == target.Name && value.Length == 8, "E_SPIKE_DATE_OVERRIDE_BINDING");
        Require(g.Cells.Length == 8 && g.SlashRectangles.Length == 2, "E_SPIKE_DATE_GEOMETRY");
        var r = field.GetWidgets()[0].GetRectangle().ToRectangle();
        var xobject = new PdfFormXObject(new Rectangle(r.GetWidth(), r.GetHeight()));
        var canvas = new PdfCanvas(xobject, pdf);
        var ink = InkBounds(font, value);
        float baseline = (r.GetHeight() - (ink.Top - ink.Bottom)) / 2 - ink.Bottom;
        canvas.BeginText().SetFontAndSize(font, FontSize);
        for (int i = 0; i < value.Length; i++)
        {
            float glyphWidth = font.GetWidth(value[i], FontSize);
            float center = (g.Cells[i][0] + g.Cells[i][1]) / 2;
            Require(glyphWidth <= g.Cells[i][1] - g.Cells[i][0] - 2 * g.PaddingPt, "E_SPIKE_DATE_CELL_OVERFLOW");
            float left = center - glyphWidth / 2;
            foreach (var slash in g.SlashRectangles)
                Require(left + glyphWidth <= slash[0] - g.SlashClearancePt || left >= slash[2] + g.SlashClearancePt,
                    "E_SPIKE_DATE_SLASH_COLLISION");
            canvas.SetTextMatrix(left - r.GetLeft(), baseline).ShowText(value[i].ToString());
        }
        canvas.EndText().Release();
        field.GetWidgets()[0].GetPdfObject().Put(PdfName.AP, new PdfDictionary());
        field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).Put(PdfName.N, xobject.GetPdfObject());
    }

    private static void VerifyEditable(string path, TemplateFixture fixture, Dictionary<string, string> text, Dictionary<string, bool> selected)
    {
        Require(ApprovedTemplateNormalizer.HasNoActiveContent(TemplateSnapshot.Load(path)), "E_SPIKE_EDITABLE_ACTIVE_CONTENT");
        using var pdf = new PdfDocument(new PdfReader(path));
        var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw new SpikeFailure("E_SPIKE_EDITABLE_FORM_MISSING");
        Require(!form.GetPdfObject().ContainsKey(PdfName.XFA), "E_SPIKE_XFA_REMAINS");
        Require(pdf.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Names)?.ContainsKey(PdfName.JavaScript) != true,
            "E_SPIKE_CATALOG_SCRIPT_REMAINS");
        Require(form.GetPdfObject().GetAsBoolean(PdfName.NeedAppearances)?.GetValue() != true, "E_SPIKE_NEED_APPEARANCES_TRUE");
        var permissions = pdf.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Perms);
        Require(permissions is null || permissions.Size() == 0, "E_SPIKE_PERMISSIONS_REMAIN");
        var fields = TerminalFields(form);
        Require(fields.Count == fixture.Targets.Length, "E_SPIKE_EDITABLE_COUNT");
        foreach (var t in fixture.Targets)
        {
            Require(fields.TryGetValue(t.Name, out var field), "E_SPIKE_EDITABLE_FIELD_MISSING");
            Require(field!.GetWidgets().Count == 1, "E_SPIKE_EDITABLE_WIDGETS");
            if (t.Type == "Btn") VerifyButton(field, t, selected[t.Name]);
            else
            {
                Require(field.GetValueAsString() == text[t.Name], "E_SPIKE_TEXT_READBACK");
                Require(field.GetPdfObject().GetAsString(PdfName.DV)?.ToUnicodeString() == "", "E_SPIKE_TEXT_RESET_DEFAULT");
                Require(!field.GetPdfObject().ContainsKey(PdfName.RV), "E_SPIKE_RICH_VALUE_REMAINS");
                var ap = field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)?.GetAsStream(PdfName.N);
                Require(ap is not null && ap.GetBytes().Length > 0, "E_SPIKE_TEXT_AP_MISSING");
                var witness = new AppearanceTextWitness();
                new PdfCanvasProcessor(witness).ProcessContent(ap!.GetBytes(), new PdfResources(ap.GetAsDictionary(PdfName.Resources)));
                Require(text[t.Name].Length == 0 ? witness.Text.All(char.IsWhiteSpace) : witness.Text == text[t.Name],
                    "E_SPIKE_AP_TEXT_READBACK");
                if (text[t.Name].Length > 0)
                {
                    string content = Encoding.Latin1.GetString(ap!.GetBytes());
                    Require(Regex.IsMatch(content, @"/\S+\s+10(?:\.0+)?\s+Tf\b", RegexOptions.CultureInvariant), "E_SPIKE_FONT_SIZE_NOT_TEN");
                    var fs = ap.GetAsDictionary(PdfName.Resources)?.GetAsDictionary(PdfName.Font);
                    Require(fs is not null && fs.KeySet().Any(k => fs.GetAsDictionary(k)?.GetAsName(PdfName.BaseFont)?.GetValue() == fixture.ExpectedFont),
                        "E_SPIKE_AP_FONT_CHANGED");
                }
            }
        }
        int widgets = 0;
        for (int i = 1; i <= pdf.GetNumberOfPages(); i++) widgets += pdf.GetPage(i).GetAnnotations().Count(a => PdfName.Widget.Equals(a.GetSubtype()));
        Require(widgets == fixture.Targets.Length, "E_SPIKE_PAGE_WIDGET_COUNT");
    }

    private static void FlattenVerified(string editable, string flattened, TemplateFixture fixture)
    {
        using (var stream = new FileStream(flattened, FileMode.CreateNew, FileAccess.Write))
        using (var pdf = new PdfDocument(new PdfReader(editable), new PdfWriter(stream)))
        {
            pdf.SetFlushUnusedObjects(false);
            var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw new SpikeFailure("E_SPIKE_FLATTEN_INPUT");
            form.FlattenFields();
        }
        Require(ApprovedTemplateNormalizer.HasNoActiveContent(TemplateSnapshot.Load(flattened)), "E_SPIKE_FINAL_ACTIVE_CONTENT");
        using var final = new PdfDocument(new PdfReader(flattened));
        Require(final.GetNumberOfPages() == fixture.Pages, "E_SPIKE_FINAL_PAGE_COUNT");
        var acro = final.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.AcroForm);
        Require(acro is null || (!acro.ContainsKey(PdfName.XFA) && (acro.GetAsArray(PdfName.Fields)?.Size() ?? 0) == 0), "E_SPIKE_FINAL_FORM_REMAINS");
        Require(final.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Names)?.ContainsKey(PdfName.JavaScript) != true,
            "E_SPIKE_FINAL_CATALOG_SCRIPT_REMAINS");
        for (int i = 1; i <= final.GetNumberOfPages(); i++)
            Require(!final.GetPage(i).GetAnnotations().Any(a => PdfName.Widget.Equals(a.GetSubtype())), "E_SPIKE_FINAL_WIDGET_REMAINS");
        Require(!string.IsNullOrWhiteSpace(final.GetDocumentInfo().GetProducer()), "E_SPIKE_PRODUCER_MISSING");
    }

    private void ExpectReject(string id, Action test, string code, string template, string? field = null)
    {
        try { test(); checks.Add(new(id, "fail", "E_SPIKE_NEGATIVE_ACCEPTED", template, field)); }
        catch (SpikeFailure ex) { checks.Add(new(id, ex.Code == code ? "pass" : "fail", ex.Code, template, field)); }
    }
    private static string SafeCode(Exception ex) => ex is SpikeFailure sf ? sf.Code : "E_SPIKE_ENGINE_" + ex.GetType().Name;
    private static void Require(bool condition, string code) { if (!condition) throw new SpikeFailure(code); }
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    private static string HashFile(string path) { using var s = File.OpenRead(path); return Hash(s); }

    private sealed class AppearanceTextWitness : IEventListener
    {
        private readonly StringBuilder text = new();
        public string Text => text.ToString();
        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_TEXT && data is TextRenderInfo info) text.Append(info.GetText());
        }
        public ICollection<EventType> GetSupportedEvents() => new HashSet<EventType> { EventType.RENDER_TEXT };
    }
}
