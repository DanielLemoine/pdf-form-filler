using System.Globalization;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using PdfForm.Core;
using PdfForm.Pdf;

// Prospective source-only helper. Root owns Program/project wiring and admitted
// execution. It uses only authored values and the hash-bound approved blank.
internal static class GenericRenderingParameterTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Field = "authored.date";

    internal static void Run(Action<bool, string> check, string root)
    {
        void Reject(Action action, string code, string label)
        {
            string? observed = null;
            try { action(); } catch (VerifiedFillException error) { observed = error.Code; }
            check(observed == code, label);
        }
        check(VectorCheckboxAppearance.ResolveConfiguredMark("cross-10-v1") == CheckboxMark.Cross,
            "exact frozen cross token");
        check(VectorCheckboxAppearance.ResolveConfiguredMark("circle-12-v1") == CheckboxMark.Circle,
            "exact frozen circle token");
        foreach (string token in new[] { "cross", "circle", "cross-11-v1", "circle-10-v1", "" })
            Reject(() => VectorCheckboxAppearance.ResolveConfiguredMark(token), "E_CHECKBOX_VECTOR_STRATEGY",
                "unfrozen checkbox token denied: " + token);
        FontInspection Font(double? size) => new("AuthoredFont", size, "TrueType", true, true, "fieldOrWidget");
        foreach (double size in new[] { 1d, 10, 12.5, 72 })
            check(NativeTextAppearance.ObserveFixedSize(Font(size), size) == (float)size, "observed fixed size: " + size);
        foreach (double size in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, .999, 72.001 })
            Reject(() => NativeTextAppearance.ObserveFixedSize(Font(size), size), "E_FONT_FIXED_SIZE", "unsupported size denied: " + size);
        Reject(() => NativeTextAppearance.ObserveFixedSize(Font(null), 10), "E_FONT_FIXED_SIZE", "absent size cannot become a 10pt default");
        Reject(() => NativeTextAppearance.ObserveFixedSize(Font(0), 10), "E_FONT_FIXED_SIZE", "auto size cannot become a fixed default");
        Reject(() => NativeTextAppearance.ObserveFixedSize(Font(10), 12), "E_FONT_FIXED_SIZE", "policy cannot change the observed source size");

        var date = new KeyDefinition { Key = "date", Kind = "date", Required = false, Sensitive = false,
            Target = Field, Format = "MMddyyyy" };
        var widget = Widget([0, 0, 80, 14]);
        double[][] Cells() => Enumerable.Range(0, 8).Select(i => new double[] { i * 10, 0, i * 10 + 10, 14 }).ToArray();
        DateCellLayout Layout(double[][] cells) => new() { Target = Field, TemplateSha256 = Hash, Cells = cells };
        var digits = new FieldAssignment(Field, "date", "text", "12312026");
        var blank = digits with { Text = "" };
        check(NativeTextAppearance.DateCells(Hash, NativeTextAppearance.SingleLineLayout, date with { Format = "MM/dd/yyyy" },
            digits with { Text = "12/31/2026" }, widget, null) is null, "ordinary date strings select the single-line path");
        Reject(() => NativeTextAppearance.DateCells(Hash, "automatic", date, digits, widget, null),
            "E_TEXT_LAYOUT", "layout never inferred from date kind");
        Reject(() => NativeTextAppearance.DateCells(Hash, NativeTextAppearance.EightDateCellsLayout, date, blank, widget, null),
            "E_DATE_LAYOUT_REQUIRED", "blank eight-cell date still requires all rectangles");
        Reject(() => NativeTextAppearance.DateCells(Hash, NativeTextAppearance.EightDateCellsLayout, date with { Format = "MM/dd/yyyy" }, digits, widget, [Layout(Cells())]),
            "E_DATE_LAYOUT_UNSUPPORTED", "eight-cell mode requires MMddyyyy");
        Reject(() => NativeTextAppearance.DateCells(Hash, NativeTextAppearance.EightDateCellsLayout, date, digits with { Text = "12/31/26" }, widget, [Layout(Cells())]),
            "E_DATE_LAYOUT_VALUE", "eight-cell mode requires eight ASCII digits");
        foreach (int count in new[] { 7, 9 })
            Reject(() => NativeTextAppearance.DateCells(Hash, NativeTextAppearance.EightDateCellsLayout, date, digits, widget,
                [Layout(Enumerable.Range(0, count).Select(i => new double[] { i * 8, 0, i * 8 + 8, 14 }).ToArray())]),
                "E_DATE_LAYOUT_UNREVIEWED", "exactly eight cells required: " + count);
        Reject(() => NativeTextAppearance.DateCells(Hash, NativeTextAppearance.EightDateCellsLayout, date, digits, widget,
            [Layout(Cells()) with { TemplateSha256 = new string('b', 64) }]), "E_DATE_LAYOUT_UNREVIEWED", "cell template identity cannot drift");
        Reject(() => NativeTextAppearance.DateCells(Hash, NativeTextAppearance.EightDateCellsLayout, date, digits, widget,
            [Layout(Cells()), Layout(Cells())]), "E_DATE_LAYOUT_REQUIRED", "duplicate target layouts denied");
        foreach (string mutation in new[] { "nan", "outside", "inverted", "overlap", "different-row" })
        {
            var cells = Cells();
            if (mutation == "nan") cells[1][0] = double.NaN;
            if (mutation == "outside") cells[7][2] = 81;
            if (mutation == "inverted") cells[1][2] = cells[1][0];
            if (mutation == "overlap") cells[1][0] = 9;
            if (mutation == "different-row") cells[1][1] = 1;
            Reject(() => NativeTextAppearance.DateCells(Hash, NativeTextAppearance.EightDateCellsLayout, date, digits, widget, [Layout(cells)]),
                mutation is "overlap" or "different-row" ? "E_DATE_LAYOUT_GEOMETRY" : "E_DATE_LAYOUT_BOUNDS", "date geometry denied: " + mutation);
        }
        var input = Layout(Cells());
        var retained = NativeTextAppearance.DateCells(Hash, NativeTextAppearance.EightDateCellsLayout, date, blank, widget, [input]);
        input.Cells[0][0] = 99;
        check(retained is { Length: 8 } && retained[0][0] == 0, "date geometry is defensively retained even for blank values");

        foreach (var mark in new[] { CheckboxMark.Cross, CheckboxMark.Circle })
        {
            double size = mark == CheckboxMark.Cross ? 10 : 12;
            var box = Widget([0, 0, size, size]);
            var resolved = VectorCheckboxAppearance.ResolveBound(Hash, Hash, "authored.check", "authored.check", "Yes", "Yes", 1, box.Rectangle, box, mark);
            check(resolved == mark, "configured supported checkbox family: " + mark);
            var target = new TargetWitness(new("authored.check", "check", "checkbox", "", true, "Yes"), box,
                0, null, 0, "Yes", null, null, null, null, mark);
            VectorCheckboxAppearance.Verify(VectorCheckboxAppearance.Create(box, resolved, true), target, true);
            VectorCheckboxAppearance.Verify(VectorCheckboxAppearance.Create(box, resolved, false), target, false);
            check(true, "semantic checkbox verification: " + mark);
        }
        var cross = Widget([0, 0, 10, 10]);
        Reject(() => VectorCheckboxAppearance.ResolveBound(Hash, new string('b', 64), "authored.check", "authored.check", "Yes", "Yes", 1,
            cross.Rectangle, cross, CheckboxMark.Cross), "E_CHECKBOX_VECTOR_SCOPE", "checkbox template binding exact");
        Reject(() => VectorCheckboxAppearance.ResolveBound(Hash, Hash, "authored.check", "other", "Yes", "Yes", 1,
            cross.Rectangle, cross, CheckboxMark.Cross), "E_CHECKBOX_VECTOR_SCOPE", "checkbox target binding exact");
        Reject(() => VectorCheckboxAppearance.ResolveBound(Hash, Hash, "authored.check", "authored.check", "Yes", "Other", 1,
            cross.Rectangle, cross, CheckboxMark.Cross), "E_CHECKBOX_VECTOR_SCOPE", "checkbox export state binding exact");
        Reject(() => VectorCheckboxAppearance.ResolveBound(Hash, Hash, "authored.check", "authored.check", "Yes", "Yes", 2,
            cross.Rectangle, cross, CheckboxMark.Cross), "E_CHECKBOX_VECTOR_SCOPE", "checkbox page binding exact");
        Reject(() => VectorCheckboxAppearance.ResolveBound(Hash, Hash, "authored.check", "authored.check", "Yes", "Yes", 1,
            [1, 0, 11, 10], cross, CheckboxMark.Cross), "E_CHECKBOX_VECTOR_SCOPE", "checkbox rectangle binding exact");
        Reject(() => VectorCheckboxAppearance.Create(Widget([0, 0, 11, 11]), CheckboxMark.Cross, true),
            "E_CHECKBOX_VECTOR_GEOMETRY", "unsupported geometry is never scaled to a reviewed family");
        Reject(() => VectorCheckboxAppearance.Create(cross, (CheckboxMark)99, true),
            "E_CHECKBOX_VECTOR_GEOMETRY", "unknown mark is never coerced to a cross or circle");
        Reject(() => VectorCheckboxAppearance.Create(cross with { Rotation = 90 }, CheckboxMark.Cross, true),
            "E_CHECKBOX_VECTOR_GEOMETRY", "rotated checkbox geometry denied");

        FixedSizeAppearances(check, Reject, root);
        OwnerMetricIdentity(check);
        SourceFontStructure(check, Reject);
    }

    // Additive v6 registration: RunExtensions(check, root). Exactly26 checks on
    // the healthy path:2 strategies * (5 invalid sizes +8 source-structure faults).
    internal static void RunExtensions(Action<bool, string> check, string root)
    {
        var snapshot = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", "f941.pdf"));
        if (snapshot.Sha256 != ApprovedTemplates.Form941Sha256) throw new InvalidOperationException("Exact font fixture required.");
        var original = new PdfTemplateInspector().Inspect(snapshot).NamedFields.First(pair => pair.Value.Type == "text"
            && pair.Value.Widgets.Count == 1 && pair.Value.Effective.Font.Embedded
            && pair.Value.Effective.Font.Name == "TimesNewRomanPSMT");

        void WithSource(string strategy, Action<PdfDocument, PdfFormField, WidgetInspection, PdfDictionary, TextFieldPolicy> assertion)
        {
            using var output = new BoundedPdfMemoryStream();
            using var pdf = ApprovedTemplateNormalizer.OpenForNormalization(snapshot, output);
            var form = PdfAcroForm.GetAcroForm(pdf, false)!;
            var field = form.GetAllFormFields()[original.Key];
            var widget = original.Value.Widgets.Single();
            var resources = (PdfDictionary)VerifiedFormRenderer.Inherited(field.GetPdfObject(), PdfName.DR, form.GetPdfObject())!;
            var dictionary = resources.GetAsDictionary(PdfName.Font)!.GetAsDictionary(new PdfName(widget.Effective.Font.Name!))!;
            var program = dictionary.GetAsDictionary(PdfName.FontDescriptor)!.GetAsStream(PdfName.FontFile2)!;
            var policy = new TextFieldPolicy
            {
                Target = original.Key, Page = widget.Page ?? throw new InvalidOperationException("Page required."),
                Rectangle = widget.Rectangle.ToArray(), Strategy = strategy, Layout = "single-line-v1",
                ResourceName = widget.Effective.Font.Name!, BaseFont = dictionary.GetAsName(PdfName.BaseFont)!.GetValue(),
                Subtype = "TrueType", Encoding = "WinAnsiEncoding", FontSize = 10,
                MetricsSha256 = NativeTextAppearance.SourceMetricsSha256(dictionary), ProgramSha256 = VerifiedFormRenderer.Digest(program.GetBytes()),
                Repertoire = strategy == "preserve-embedded-simple-v1" ? "encoded-font-v1" : "ascii-plus-eacute-v1"
            };
            assertion(pdf, field, widget, dictionary, policy);
        }
        static string State(PdfFormField field)
        {
            var appearance = field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)?.GetAsStream(PdfName.N);
            return (field.GetPdfObject().Get(PdfName.DA)?.ToString() ?? "<inherited>") + "|"
                + (appearance is null ? "<absent>" : VerifiedFormRenderer.Digest(appearance.GetBytes()));
        }
        foreach (string strategy in new[] { "preserve-embedded-simple-v1", "bundled-liberation-serif-ascii-v1" })
        {
            foreach (double size in new[] { 0d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 72.001 })
                WithSource(strategy, (pdf, field, widget, dictionary, policy) =>
                {
                    string before = State(field);
                    var observed = widget with { Effective = widget.Effective with { Font = widget.Effective.Font with { Size = size } } };
                    string? code = null; int? exit = null;
                    try { NativeTextAppearance.ResolveConfiguredFont(pdf, field, observed, policy with { FontSize = size }, "11", out _, new(pdf)); }
                    catch (VerifiedFillException error) { code = error.Code; exit = error.ExitCode; }
                    check(code == "E_FONT_FIXED_SIZE" && exit == 3 && State(field) == before,
                        "v6/resolver-invalid-size/" + strategy + "/" + size.ToString(CultureInfo.InvariantCulture));
                });

            for (int fault = 0; fault < 8; fault++)
                WithSource(strategy, (pdf, field, widget, dictionary, policy) =>
                {
                    var descriptor = dictionary.GetAsDictionary(PdfName.FontDescriptor)!;
                    switch (fault)
                    {
                        case 0: dictionary.Put(PdfName.FontDescriptor, PdfNull.PDF_NULL); break;
                        case 1: dictionary.Put(PdfName.FontDescriptor, new PdfNumber(1)); break;
                        case 2: dictionary.Put(PdfName.FontDescriptor, new PdfStream(new byte[] { 1 })); break;
                        case 3: descriptor.Put(PdfName.FontFile2, PdfNull.PDF_NULL); break;
                        case 4: descriptor.Put(PdfName.FontFile2, new PdfNumber(1)); break;
                        case 5: descriptor.Put(PdfName.FontFile2, new PdfDictionary()); break;
                        case 6: descriptor.Put(PdfName.FontFile3, new PdfStream(new byte[] { 1 })); break;
                        case 7: descriptor.Remove(PdfName.FontFile2); break;
                    }
                    string before = State(field);
                    string? code = null; int? exit = null;
                    try { NativeTextAppearance.ResolveConfiguredFont(pdf, field, widget, policy, "11", out _, new(pdf)); }
                    catch (VerifiedFillException error) { code = error.Code; exit = error.ExitCode; }
                    check(code == (fault == 7 ? "E_FONT_RESOURCE_IDENTITY" : "E_FONT_PROGRAM") && exit == 3 && State(field) == before,
                        "v6/resolver-source-fault/" + strategy + "/" + fault);
                });
        }
    }

    private static WidgetInspection Widget(double[] rectangle) => new("authored-widget", 1, rectangle, true,
        new(new(false, false, false, false, false, false, false), 64, "left",
            new("AuthoredFont", 10, "TrueType", true, true, "fieldOrWidget")), 4, 0, true, true, ["Yes"], 1, "merged");

    private static void OwnerMetricIdentity(Action<bool, string> check)
    {
        byte[] bytes = Fixture.Make();
        var draft = new PdfTemplateInspector().DiscoverConfiguration(TemplateSnapshot.FromBytes(bytes));
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        var font = PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject().GetAsDictionary(PdfName.DR)!
            .GetAsDictionary(PdfName.Font)!.GetAsDictionary(new PdfName("Helvetica"))!;
        string hash = NativeTextAppearance.SourceMetricsSha256(font);
        check(draft.Targets.Count != 0 && draft.Targets.Where(target => target.Font is not null)
            .All(target => target.Font!.MetricsSha256 == hash && target.Font.Encoding == "absent"),
            "renderer source metrics and absent encoding agree with owner discovery");
        // ToUnicode presence is a separate source fact, not an extra metric
        // hash input. Exact template identity separately binds its bytes.
        font.Put(PdfName.ToUnicode, new PdfStream(System.Text.Encoding.ASCII.GetBytes("authored metadata fixture")));
        check(NativeTextAppearance.SourceMetricsSha256(font) == hash,
            "source metrics preserve the owner's exact mapping-independent algorithm");
    }

    private static void SourceFontStructure(Action<bool, string> check, Action<Action, string, string> reject)
    {
        var font = new PdfDictionary();
        check(NativeTextAppearance.ObserveSourceEncoding(font) == "absent", "missing source encoding uses frozen absent token");
        font.Put(PdfName.Encoding, PdfName.WinAnsiEncoding);
        check(NativeTextAppearance.ObserveSourceEncoding(font) == "WinAnsiEncoding", "source encoding name is exact");
        foreach (PdfObject encoding in new PdfObject[] { new PdfNumber(1), new PdfDictionary(), new PdfString("") })
        {
            font.Put(PdfName.Encoding, encoding);
            check(NativeTextAppearance.ObserveSourceEncoding(font) == "unsupported", "malformed source encoding is never absence");
        }
        check(NativeTextAppearance.ReadSourceFontProgram(font, out var absentKind) is null && absentKind is null,
            "absent descriptor has no font program");
        font.Put(PdfName.FontDescriptor, new PdfDictionary());
        check(NativeTextAppearance.ReadSourceFontProgram(font, out var emptyKind) is null && emptyKind is null,
            "empty descriptor has no font program");
        foreach (PdfObject descriptor in new PdfObject[] { PdfNull.PDF_NULL, new PdfNumber(1), new PdfString(""), new PdfStream() })
        {
            font.Put(PdfName.FontDescriptor, descriptor);
            reject(() => NativeTextAppearance.ReadSourceFontProgram(font, out _), "E_FONT_PROGRAM",
                "present malformed descriptor cannot select an unembedded strategy");
        }
        foreach (var key in new[] { PdfName.FontFile, PdfName.FontFile2, PdfName.FontFile3 })
        {
            var descriptor = new PdfDictionary();
            font.Put(PdfName.FontDescriptor, descriptor);
            foreach (PdfObject program in new PdfObject[] { PdfNull.PDF_NULL, new PdfNumber(1), new PdfDictionary(), new PdfString("") })
            {
                descriptor.Put(key, program);
                reject(() => NativeTextAppearance.ReadSourceFontProgram(font, out _), "E_FONT_PROGRAM",
                    "present non-stream font program cannot become absence");
            }
            var stream = new PdfStream(new byte[] { 1, 2, 3 });
            descriptor.Put(key, stream);
            check(ReferenceEquals(NativeTextAppearance.ReadSourceFontProgram(font, out var observedKind), stream)
                && key.Equals(observedKind), "one exact font-program stream is observed");
            var otherKey = key.Equals(PdfName.FontFile) ? PdfName.FontFile2 : PdfName.FontFile;
            descriptor.Put(otherKey, new PdfStream(new byte[] { 4 }));
            reject(() => NativeTextAppearance.ReadSourceFontProgram(font, out _), "E_FONT_PROGRAM",
                "multiple font-program entries are rejected");
            descriptor.Put(otherKey, PdfNull.PDF_NULL);
            reject(() => NativeTextAppearance.ReadSourceFontProgram(font, out _), "E_FONT_PROGRAM",
                "a second malformed program entry is still ambiguous");
        }
    }

    private static void FixedSizeAppearances(Action<bool, string> check, Action<Action, string, string> reject, string root)
    {
        var snapshot = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", "f941.pdf"));
        check(snapshot.Sha256 == ApprovedTemplates.Form941Sha256, "font fixture uses exact approved blank bytes");
        var inspection = new PdfTemplateInspector().Inspect(snapshot);
        var original = inspection.NamedFields.First(pair => pair.Value.Type == "text"
            && pair.Value.Widgets.Count == 1 && pair.Value.Effective.Font.Embedded
            && pair.Value.Effective.Font.Name == "TimesNewRomanPSMT");
        foreach (float size in new[] { 1f, 10, 12.5f, 72 })
        {
            using var output = new BoundedPdfMemoryStream();
            using var pdf = ApprovedTemplateNormalizer.OpenForNormalization(snapshot, output);
            var form = PdfAcroForm.GetAcroForm(pdf, false)!;
            var field = form.GetAllFormFields()[original.Key];
            var attributes = original.Value.Effective with
            {
                Font = original.Value.Effective.Font with { Size = size },
                Flags = new(false, false, false, false, false, false, false), MaxLength = 64, Alignment = "left"
            };
            var widget = original.Value.Widgets.Single() with { Rectangle = new double[] { 100, 100, 500, 300 }, Effective = attributes };
            field.GetPdfObject().Put(PdfName.Ff, new PdfNumber(0));
            field.GetPdfObject().Put(PdfName.Q, new PdfNumber(0));
            field.GetPdfObject().Put(PdfName.MaxLen, new PdfNumber(64));
            field.GetPdfObject().Put(PdfName.DA, new PdfString("/TimesNewRomanPSMT " + size.ToString(CultureInfo.InvariantCulture) + " Tf 0 g"));
            field.GetWidgets()[0].GetPdfObject().Put(PdfName.Rect, new PdfArray(widget.Rectangle.ToArray()));
            var resources = (PdfDictionary)VerifiedFormRenderer.Inherited(field.GetPdfObject(), PdfName.DR, form.GetPdfObject())!;
            var dictionary = resources.GetAsDictionary(PdfName.Font)!.GetAsDictionary(new PdfName("TimesNewRomanPSMT"))!;
            string metrics = NativeTextAppearance.SourceMetricsSha256(dictionary);
            string program = VerifiedFormRenderer.Digest(dictionary.GetAsDictionary(PdfName.FontDescriptor)!.GetAsStream(PdfName.FontFile2)!.GetBytes());
            var font = NativeTextAppearance.ResolvePreservedFont(pdf, field, attributes.Font,
                "TimesNewRomanPSMT", "TimesNewRomanPSMT", size, "WinAnsiEncoding", metrics, program, out var witness);
            reject(() => NativeTextAppearance.ResolvePreservedFont(pdf, field, attributes.Font,
                "TimesNewRomanPSMT", "TimesNewRomanPSMT", size, "WinAnsiEncoding", new string('0', 64), program, out _),
                "E_FONT_RESOURCE_IDENTITY", "source metric hash cannot drift: " + size);
            reject(() => NativeTextAppearance.ResolvePreservedFont(pdf, field, attributes.Font,
                "TimesNewRomanPSMT", "TimesNewRomanPSMT", size, "WinAnsiEncoding", metrics, new string('0', 64), out _),
                "E_FONT_RESOURCE_IDENTITY", "source program hash cannot drift: " + size);
            check(witness.Size == size && !witness.PrivateUnembeddedException && witness.EmbeddedDigest is not null,
                "embedded preserve witness retains exact size: " + size);
            NativeTextAppearance.CheckFit(field, widget, "11", font, witness);
            var target = new TargetWitness(new(original.Key, "authored.text", "text", "11"), widget,
                0, 64, 0, null, null, null, witness, null);
            field.SetValue("11", font, witness.Size);
            PdfStream Appearance() => field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!;
            NativeTextAppearance.Verify(Appearance(), target, new());
            check(true, "actual generated text AP verifies at fixed size: " + size);
            field.SetValue("11", font, size == 72 ? 71 : size + 1);
            reject(() => NativeTextAppearance.Verify(Appearance(), target, new()), "E_VERIFY_AP_TEXT_STATE",
                "wrong AP point size fails despite unchanged text: " + size);
            double narrowWidth = font.GetWidth("11", size) + 2 - .1;
            field.GetWidgets()[0].GetPdfObject().Put(PdfName.Rect, new PdfArray(new double[] { 100, 100, 100 + narrowWidth, 300 }));
            reject(() => NativeTextAppearance.CheckFit(field, widget with { Rectangle = new double[] { 100, 100, 100 + narrowWidth, 300 } }, "11", font, witness),
                "E_TEXT_OVERFLOW", "overflow refuses instead of shrinking at fixed size: " + size);
        }
    }
}
