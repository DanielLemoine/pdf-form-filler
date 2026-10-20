using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using iText.Forms;
using iText.Forms.Fields;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Xobject;
using PdfForm.Core;
using static PdfForm.Pdf.VerifiedFormRenderer;

namespace PdfForm.Pdf;

internal sealed record NativeFontWitness(string Name, string MetricsDigest, string? EmbeddedDigest,
    FontProgram? ExactOutlines, bool PrivateUnembeddedException, float Size = NativeTextAppearance.FontSize,
    string Policy = "native-template");

/// <summary>Exact source-font admission with explicit fixed-size preserved or pinned embedded rendering.</summary>
internal static class NativeTextAppearance
{
    internal const float FontSize = 10;
    internal const string SingleLineLayout = "single-line-v1";
    internal const string EightDateCellsLayout = "eight-date-cells-v1";
    internal const string PreservedFontPolicy = "preserve-embedded-simple-v1";
    internal static readonly PdfName PreservedResourceName = new("PdfFormPreserved");
    private const float Padding = 1;
    private const float DatePadding = .5f;
    private const string DateTarget = "topmostSubform[0].Page2[0].f2_9[0]";
    private static readonly ConcurrentDictionary<string, FontProgram> Programs = new(StringComparer.Ordinal);
    private static readonly double[][] ReviewedDateCells =
    [
        [209.8,447.966,217.7499984741211,461.967],
        [217.7499984741211,447.966,225.6999969482422,461.967],
        [232.53050231933594,447.966,238.801025390625,461.967],
        [238.801025390625,447.966,245.07154846191406,461.967],
        [251.9020538330078,447.966,260.67654037475586,461.967],
        [260.67654037475586,447.966,269.4510269165039,461.967],
        [269.4510269165039,447.966,278.22551345825195,461.967],
        [278.22551345825195,447.966,287,461.967]
    ];

    internal static PdfFont ResolveFont(string templateSha256, PdfDocument pdf, PdfFormField field,
        FontInspection inspected, out NativeFontWitness witness, ScheduleBEmbeddedFont.DocumentFont? replacement = null)
    {
        _ = ObserveFixedSize(inspected, FontSize);
        Require(inspected.ResourcePresent && inspected.Name is not null,
            "E_FONT_FIXED_SIZE", "The renderer requires a supported fixed 10-point template font.", 3);
        string expectedName = templateSha256 == ApprovedTemplates.Form941Sha256 ? "TimesNewRomanPSMT"
            : templateSha256 == ApprovedTemplates.ScheduleBSha256 ? "TimesLTStd-Roman" : "";
        var acro = PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject();
        var resources = Inherited(field.GetPdfObject(), PdfName.DR, acro) as PdfDictionary;
        var dict = resources?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(new PdfName(inspected.Name!));
        Require(dict is not null && inspected.Name == expectedName && dict.GetAsName(PdfName.BaseFont)?.GetValue() == expectedName,
            "E_FONT_RESOURCE", "The effective font is not the exact expected native resource.", 3);
        Require(!dict!.ContainsKey(PdfName.ToUnicode), "E_FONT_MAPPING", "The exact native font requires the reviewed absence of a Unicode remapping stream.", 3);
        var stream = ReadSourceFontProgram(dict!, out var programKind);
        Require(stream is null || PdfName.FontFile2.Equals(programKind),
            "E_FONT_PROGRAM", "The native resource has an unsupported font program kind.", 3);
        var font = PdfFontFactory.CreateFont(dict);
        FontProgram? program = null;
        string? embeddedDigest = null;
        bool privateException = false;
        if (stream is not null)
        {
            byte[] bytes = stream.GetBytes();
            Require(bytes.Length is > 0 and <= 10 * 1024 * 1024, "E_FONT_PROGRAM_LIMIT", "Embedded font program exceeds its bounded size.", 3);
            embeddedDigest = Digest(bytes);
            program = Programs.GetOrAdd(embeddedDigest, _ => FontProgramFactory.CreateFont(bytes, false));
            Require(font.IsEmbedded() && expectedName == "TimesNewRomanPSMT", "E_FONT_PROGRAM", "The embedded font strategy is unsupported.", 3);
        }
        else
        {
            // Admit the exact reviewed source independently before deliberate replacement.
            bool approvedSource = templateSha256 == ApprovedTemplates.ScheduleBSha256
                && ProductionProfileRegistry.AdoptionScope == "bounded_private_development"
                && dict.GetAsName(PdfName.Subtype)?.GetValue() == "Type1"
                && dict.GetAsName(PdfName.Encoding)?.GetValue() == "WinAnsiEncoding" && !font.IsEmbedded();
            Require(approvedSource && replacement is not null, "E_FONT_NOT_EMBEDDED", "No explicit embedded font policy is available for this resource.", 3);
            return replacement!.Get(pdf, out witness);
        }
        witness = new(expectedName, MetricsDigest(dict), embeddedDigest, program, privateException);
        return font;
    }

    // The generic caller must first bind the sealed configuration's exact
    // template/target/page/rectangle/source-font facts. This helper preserves
    // one embedded simple TrueType resource; no replacement font is chosen here.
    internal static PdfFont ResolvePreservedFont(PdfDocument pdf, PdfFormField field, FontInspection inspected,
        string expectedResourceName, string expectedBaseFontName, double expectedSize, string expectedEncoding,
        string expectedMetricsSha256, string expectedProgramSha256, out NativeFontWitness witness)
    {
        float size = ObserveFixedSize(inspected, expectedSize);
        Require(inspected.ResourcePresent && inspected.Embedded && inspected.Subtype == "TrueType"
            && !string.IsNullOrWhiteSpace(expectedResourceName)
            && inspected.Name == expectedResourceName && !string.IsNullOrWhiteSpace(expectedBaseFontName),
            "E_FONT_RESOURCE", "The observed font differs from the exact configured embedded resource.", 3);
        var acro = PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject();
        var resources = Inherited(field.GetPdfObject(), PdfName.DR, acro) as PdfDictionary;
        var dictionary = resources?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(new PdfName(expectedResourceName));
        Require(dictionary is not null && PdfName.TrueType.Equals(dictionary.GetAsName(PdfName.Subtype))
            && dictionary.GetAsName(PdfName.BaseFont)?.GetValue() == expectedBaseFontName
            && expectedEncoding == "WinAnsiEncoding" && PdfName.WinAnsiEncoding.Equals(dictionary.GetAsName(PdfName.Encoding))
            && !dictionary.ContainsKey(PdfName.ToUnicode),
            "E_FONT_RESOURCE", "The configured simple font resource or encoding is unsupported.", 3);
        var stream = ReadSourceFontProgram(dictionary!, out var programKind);
        Require(stream is not null && PdfName.FontFile2.Equals(programKind),
            "E_FONT_NOT_EMBEDDED", "The preserve strategy requires its exact embedded TrueType program.", 3);
        byte[] bytes = stream!.GetBytes();
        Require(bytes.Length is > 0 and <= 10 * 1024 * 1024, "E_FONT_PROGRAM_LIMIT", "Embedded font program exceeds its bounded size.", 3);
        string digest = Digest(bytes);
        string metrics = MetricsDigest(dictionary!);
        Require(digest == expectedProgramSha256 && metrics == expectedMetricsSha256,
            "E_FONT_RESOURCE_IDENTITY", "The source font metrics or program differs from the configured exact identity.", 3);
        var font = PdfFontFactory.CreateFont(dictionary!);
        Require(font.IsEmbedded(), "E_FONT_NOT_EMBEDDED", "The preserve strategy cannot substitute a font.", 3);
        var program = Programs.GetOrAdd(digest, _ => FontProgramFactory.CreateFont(bytes, false));
        witness = new(expectedBaseFontName, metrics, digest, program, false, size, PreservedFontPolicy);
        return font;
    }

    internal static PdfFont ResolveConfiguredFont(PdfDocument pdf, PdfFormField field, WidgetInspection widget,
        TextFieldPolicy policy, string value, out NativeFontWitness witness, ScheduleBEmbeddedFont.DocumentFont replacement)
    {
        var inspected = widget.Effective.Font;
        _ = ObserveFixedSize(inspected, policy.FontSize);
        Require(inspected.ResourcePresent && inspected.Name == policy.ResourceName && inspected.Subtype == policy.Subtype,
            "E_FONT_RESOURCE", "The observed source font differs from its configured resource and subtype.", 3);
        var acro = PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject();
        var resources = Inherited(field.GetPdfObject(), PdfName.DR, acro) as PdfDictionary;
        var dictionary = resources?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(new PdfName(policy.ResourceName));
        Require(dictionary is not null && dictionary.GetAsName(PdfName.BaseFont)?.GetValue() == policy.BaseFont
            && dictionary.GetAsName(PdfName.Subtype)?.GetValue() == policy.Subtype,
            "E_FONT_RESOURCE", "The source font dictionary differs from its configured exact identity.", 3);
        string encoding = ObserveSourceEncoding(dictionary!);
        Require(encoding == policy.Encoding && encoding is "WinAnsiEncoding" or "absent",
            "E_FONT_MAPPING", "The observed source encoding differs from its explicit configured value.", 3);
        var stream = ReadSourceFontProgram(dictionary!, out _);
        string? program = null;
        if (stream is not null)
        {
            byte[] bytes = stream.GetBytes();
            Require(bytes.Length is > 0 and <= 10 * 1024 * 1024, "E_FONT_PROGRAM_LIMIT", "The source font exceeds its bounded program size.", 3);
            program = Digest(bytes);
        }
        Require(inspected.Embedded == (stream is not null) && program == policy.ProgramSha256
            && SourceMetricsSha256(dictionary!) == policy.MetricsSha256,
            "E_FONT_RESOURCE_IDENTITY", "The source font program or metrics differs from its exact configured identity.", 3);
        if (policy.Strategy == "preserve-embedded-simple-v1")
        {
            Require(policy.Repertoire == "encoded-font-v1" && policy.Subtype == "TrueType" && policy.Encoding == "WinAnsiEncoding"
                && policy.ProgramSha256 is not null, "E_FONT_STRATEGY", "The configured preserved-font strategy is unsupported.", 3);
            return ResolvePreservedFont(pdf, field, inspected, policy.ResourceName, policy.BaseFont, policy.FontSize,
                policy.Encoding, policy.MetricsSha256, policy.ProgramSha256!, out witness);
        }
        Require(policy.Strategy == "bundled-liberation-serif-ascii-v1" && policy.Repertoire == "ascii-plus-eacute-v1"
            && policy.Subtype is "Type1" or "TrueType", "E_FONT_STRATEGY", "The configured font strategy is unsupported.", 3);
        Require(value.EnumerateRunes().All(rune => ScheduleBEmbeddedFont.Supports(rune.Value)),
            "E_FONT_GLYPH_UNQUALIFIED", "A supplied character is outside the engine-fixed bundled repertoire.", 3);
        return replacement.Get(pdf, out witness, (float)policy.FontSize, ScheduleBEmbeddedFont.ConfiguredPolicy);
    }

    internal static string ObserveSourceEncoding(PdfDictionary font)
    {
        var encoding = font.Get(PdfName.Encoding, true);
        return encoding is null ? "absent" : encoding is PdfName name ? name.GetValue() : "unsupported";
    }

    // Match the inspector's structural observation before a missing source
    // program can select any strategy. A present malformed entry is never absence.
    internal static PdfStream? ReadSourceFontProgram(PdfDictionary font, out PdfName? kind)
    {
        PdfDictionary? descriptor = null;
        if (font.ContainsKey(PdfName.FontDescriptor))
        {
            var value = font.Get(PdfName.FontDescriptor, true);
            Require(value is PdfDictionary && value is not PdfStream,
                "E_FONT_PROGRAM", "The source font descriptor is malformed.", 3);
            descriptor = (PdfDictionary)value!;
        }
        var keys = new[] { PdfName.FontFile, PdfName.FontFile2, PdfName.FontFile3 }
            .Where(key => descriptor?.ContainsKey(key) == true).ToArray();
        Require(keys.Length <= 1, "E_FONT_PROGRAM", "The source has ambiguous font programs.", 3);
        kind = keys.Length == 0 ? null : keys[0];
        if (kind is null) return null;
        var program = descriptor!.Get(kind, true);
        Require(program is PdfStream, "E_FONT_PROGRAM", "The source font program is not a supported stream.", 3);
        return (PdfStream)program!;
    }

    internal static float ObserveFixedSize(FontInspection inspected, double expectedSize)
    {
        Require(double.IsFinite(expectedSize) && expectedSize is >= 1 and <= 72
            && inspected.Size is { } actual && double.IsFinite(actual) && actual == expectedSize,
            "E_FONT_FIXED_SIZE", "Text requires the exact observed fixed font size from 1 through 72 points.", 3);
        return (float)expectedSize;
    }

    private static float WitnessSize(WidgetInspection widget, NativeFontWitness witness)
    {
        Require(float.IsFinite(witness.Size) && witness.Size is >= 1 and <= 72
            && widget.Effective.Font.Size is { } observed && double.IsFinite(observed)
            && observed is >= 1 and <= 72 && (float)observed == witness.Size,
            "E_FONT_FIXED_SIZE", "The text witness differs from its observed fixed source font size.", 3);
        return witness.Size;
    }

    internal static void CheckFit(PdfFormField field, WidgetInspection widget, string value, PdfFont font, NativeFontWitness witness)
    {
        float fontSize = WitnessSize(widget, witness);
        var runes = value.EnumerateRunes().ToArray();
        Require(!value.Any(char.IsControl), "E_TEXT_CONTROL", "Control characters are unsupported in text appearances.", 3);
        Require(runes.All(r => font.ContainsGlyph(r.Value)), "E_FONT_GLYPH", "A supplied character has no glyph in the exact native font.", 3);
        if (ScheduleBEmbeddedFont.IsReplacementPolicy(witness.Policy))
            Require(runes.All(r => ScheduleBEmbeddedFont.Supports(r.Value)),
                "E_FONT_GLYPH_UNQUALIFIED", "The explicit embedded-font policy admits printable ASCII and U+00C9 only.", 3);
        if (widget.Effective.MaxLength is > 0)
            Require(runes.Length <= widget.Effective.MaxLength, "E_TEXT_MAX_LENGTH", "A supplied text value exceeds the target capacity.");
        if (value.Length == 0) return;
        var rect = field.GetWidgets()[0].GetRectangle().ToRectangle();
        Require(widget.Effective.Alignment is "left" or "center" or "right" && !widget.Effective.Flags.Multiline,
            "E_TEXT_LAYOUT", "The effective text layout is unsupported.", 3);
        if (widget.Effective.Flags.Comb)
        {
            Require(widget.Effective.MaxLength is > 0, "E_TEXT_COMB_CAPACITY", "A comb target lacks explicit capacity.", 3);
            float width = rect.GetWidth() / widget.Effective.MaxLength!.Value;
            Require(runes.All(r => font.GetWidth(r.Value, fontSize) <= width - Padding),
                "E_TEXT_COMB_OVERFLOW", "A character does not fit its fixed-size comb cell.");
        }
        else Require(font.GetWidth(value, fontSize) <= rect.GetWidth() - 2 * Padding,
            "E_TEXT_OVERFLOW", "A value does not fit at the approved fixed font size.");
        var ink = InkBounds(font, witness, value);
        Require(ink.Top - ink.Bottom <= rect.GetHeight() - 1,
            "E_TEXT_VERTICAL_OVERFLOW", "Text height exceeds the widget's available space.");
    }

    internal static double[][]? DateCells(string templateSha256, KeyDefinition key, FieldAssignment assignment,
        WidgetInspection widget, DateCellLayout[]? layouts)
    {
        if (key.Kind != "date" || assignment.Text.Length == 0) return null;
        Require(templateSha256 == ApprovedTemplates.Form941Sha256 && assignment.Field == DateTarget && key.Format == "MMddyyyy",
            "E_DATE_LAYOUT_UNSUPPORTED", "The date target or format has no reviewed appearance layout.", 3);
        Require(assignment.Text.Length == 8 && assignment.Text.All(c => c is >= '0' and <= '9'),
            "E_DATE_LAYOUT_VALUE", "The reviewed date layout requires exactly eight ASCII digits.");
        var matches = (layouts ?? []).Where(x => x.Target == assignment.Field).ToArray();
        Require(matches.Length == 1, "E_DATE_LAYOUT_REQUIRED", "The populated date requires its reviewed template-bound cell layout.", 3);
        var layout = matches[0];
        Require(layout.TemplateSha256 == templateSha256 && layout.Cells.Length == 8
            && layout.Cells.Zip(ReviewedDateCells).All(x => x.First.Length == 4 && x.First.Zip(x.Second).All(v => Math.Abs(v.First - v.Second) < .000001)),
            "E_DATE_LAYOUT_UNREVIEWED", "Date-cell geometry differs from the reviewed exact-template layout.", 3);
        Require(widget.Rectangle.Count == 4 && layout.Cells.All(c => c[0] >= widget.Rectangle[0] && c[2] <= widget.Rectangle[2]
            && c[1] >= widget.Rectangle[1] - .002 && c[3] <= widget.Rectangle[3] + .002),
            "E_DATE_LAYOUT_BOUNDS", "Reviewed date cells fall outside the bound widget.");
        return layout.Cells.Select(x => x.ToArray()).ToArray();
    }

    // Generic layout selection is explicit. An ordinary date string does not
    // acquire the Form941 eight-cell exception merely because its key is a date.
    internal static double[][]? DateCells(string templateSha256, string layoutMode, KeyDefinition key,
        FieldAssignment assignment, WidgetInspection widget, DateCellLayout[]? layouts)
    {
        Require(layoutMode is SingleLineLayout or EightDateCellsLayout,
            "E_TEXT_LAYOUT", "The configured text layout is unsupported.", 3);
        if (layoutMode == SingleLineLayout) return null;
        Require(key.Kind == "date" && key.Format == "MMddyyyy",
            "E_DATE_LAYOUT_UNSUPPORTED", "The eight-cell layout requires an explicit MMddyyyy date key.", 3);
        Require(assignment.Text.Length == 0 || assignment.Text.Length == 8 && assignment.Text.All(c => c is >= '0' and <= '9'),
            "E_DATE_LAYOUT_VALUE", "The eight-cell date requires exactly eight ASCII digits or an empty value.");
        var matches = (layouts ?? []).Where(x => x is not null && x.Target == assignment.Field).ToArray();
        Require(matches.Length == 1, "E_DATE_LAYOUT_REQUIRED", "The eight-cell date requires all eight exact-template rectangles.", 3);
        var layout = matches[0];
        Require(layout.TemplateSha256 == templateSha256 && layout.Cells is { Length: 8 }
            && widget.Rectangle.Count == 4 && widget.Rectangle.All(double.IsFinite)
            && widget.Rectangle[0] < widget.Rectangle[2] && widget.Rectangle[1] < widget.Rectangle[3],
            "E_DATE_LAYOUT_UNREVIEWED", "Date geometry does not bind the exact template and widget.", 3);
        for (int i = 0; i < 8; i++)
        {
            var cell = layout.Cells[i];
            Require(cell is { Length: 4 } && cell.All(double.IsFinite) && cell[0] < cell[2] && cell[1] < cell[3]
                && cell[0] >= widget.Rectangle[0] - .002 && cell[2] <= widget.Rectangle[2] + .002
                && cell[1] >= widget.Rectangle[1] - .002 && cell[3] <= widget.Rectangle[3] + .002,
                "E_DATE_LAYOUT_BOUNDS", "An eight-cell date rectangle is invalid or outside the bound widget.", 3);
            if (i > 0)
                Require(cell[0] >= layout.Cells[i - 1][2] - .002
                    && Math.Abs(cell[1] - layout.Cells[0][1]) < .002 && Math.Abs(cell[3] - layout.Cells[0][3]) < .002,
                    "E_DATE_LAYOUT_GEOMETRY", "Eight-cell dates require one ordered, non-overlapping horizontal row.", 3);
        }
        // Validate even an empty date, and own the geometry used during painting
        // and readback rather than retaining caller-owned array references.
        return layout.Cells.Select(cell => cell.ToArray()).ToArray();
    }

    internal static void SetEmptyAppearance(PdfDocument pdf, PdfFormField field)
    {
        // The native engine emits a nonbreaking-space placeholder for empty text. Author an actually empty
        // normal stream instead, so cleared values do not depend on placeholder glyph or viewer behavior.
        var rect = field.GetWidgets()[0].GetRectangle().ToRectangle();
        var empty = new PdfFormXObject(new Rectangle(rect.GetWidth(), rect.GetHeight()));
        new PdfCanvas(empty, pdf).SaveState().RestoreState().Release();
        var ap = new PdfDictionary(); ap.Put(PdfName.N, empty.GetPdfObject());
        field.GetWidgets()[0].GetPdfObject().Put(PdfName.AP, ap);
    }

    internal static void SetDateAppearance(PdfDocument pdf, PdfFormField field, TargetWitness target, PdfFont font)
    {
        float fontSize = WitnessSize(target.Widget, target.Font!);
        Require(target.Font!.ExactOutlines is not null && target.DateCells is { Length: 8 },
            "E_DATE_FONT_PROGRAM", "Eight-cell date painting requires exact glyph outlines and all eight cells.", 3);
        var rect = field.GetWidgets()[0].GetRectangle().ToRectangle();
        var box = new PdfFormXObject(new Rectangle(rect.GetWidth(), rect.GetHeight()));
        var canvas = new PdfCanvas(box, pdf);
        var ink = InkBounds(font, target.Font!, target.Assignment.Text);
        var cells = target.DateCells!;
        float baseline = (float)((cells[0][1] + cells[0][3]) / 2)
            - rect.GetBottom() - (ink.Top + ink.Bottom) / 2;
        canvas.SetFillColorGray(0).BeginText().SetFontAndSize(font, fontSize);
        for (int i = 0; i < 8; i++)
        {
            string character = target.Assignment.Text[i].ToString();
            var cell = target.DateCells![i];
            float width = font.GetWidth(character, fontSize);
            float left = (float)((cell[0] + cell[2]) / 2) - width / 2;
            var glyph = target.Font!.ExactOutlines!.GetGlyph(character[0])?.GetBbox();
            Require(glyph is { Length: 4 } && left + glyph[0] * fontSize / 1000 >= cell[0] + DatePadding
                && left + glyph[2] * fontSize / 1000 <= cell[2] - DatePadding
                && rect.GetBottom() + baseline + glyph[1] * fontSize / 1000 >= cell[1] - .002
                && rect.GetBottom() + baseline + glyph[3] * fontSize / 1000 <= cell[3] + .002,
                "E_DATE_GLYPH_BOUNDS", "A date glyph does not fit inside its reviewed slash-aware cell.");
            canvas.SetTextMatrix(left - rect.GetLeft(), baseline).ShowText(character);
        }
        canvas.EndText().Release();
        var ap = new PdfDictionary(); ap.Put(PdfName.N, box.GetPdfObject());
        field.GetWidgets()[0].GetPdfObject().Put(PdfName.AP, ap);
    }

    internal static void Verify(PdfStream appearance, TargetWitness target, VerificationContext context)
    {
        _ = WitnessSize(target.Widget, target.Font!);
        VerifyAppearanceBox(appearance, target.Widget);
        var clip = GeneratedContentPolicy.CheckTextAppearance(appearance);
        var witness = new AppearanceWitness(target, clip, context);
        var processor = new PdfCanvasProcessor(witness);
        witness.Processor = processor;
        var resources = appearance.GetAsDictionary(PdfName.Resources) ?? new PdfDictionary();
        processor.ProcessContent(appearance.GetBytes(), new PdfResources(resources));
        Require(witness.Text == target.Assignment.Text, "E_VERIFY_AP_TEXT", "The normal appearance does not paint exactly the expected text or blank.");
        Require(!witness.HasImage, "E_VERIFY_AP_IMAGE", "A text appearance contains an unsupported image.");
        Require(target.Assignment.Text.Length == 0 || witness.GlyphCount == target.Assignment.Text.EnumerateRunes().Count(),
            "E_VERIFY_AP_GLYPHS", "The normal appearance character count differs from the assignment.");
    }

    private static (float Bottom, float Top) InkBounds(PdfFont font, NativeFontWitness witness, string value)
    {
        float fontSize = witness.Size;
        Require(float.IsFinite(fontSize) && fontSize is >= 1 and <= 72,
            "E_FONT_FIXED_SIZE", "The glyph measurement requires a supported fixed font size.", 3);
        if (witness.ExactOutlines is null) return (font.GetDescent(value, fontSize), font.GetAscent(value, fontSize));
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        foreach (var rune in value.EnumerateRunes())
        {
            var glyph = witness.ExactOutlines.GetGlyph(rune.Value);
            Require(glyph is not null, "E_FONT_OUTLINE_GLYPH", "An embedded native glyph cannot be measured.", 3);
            var box = glyph!.GetBbox();
            if (box is null) { Require(rune.Value == ' ', "E_FONT_OUTLINE_GLYPH", "A non-space glyph has no measurable outline.", 3); continue; }
            bottom = Math.Min(bottom, box[1] * fontSize / 1000);
            top = Math.Max(top, box[3] * fontSize / 1000);
        }
        return float.IsFinite(bottom) ? (bottom, top) : (0, 0);
    }
    internal static string MetricsDigest(PdfDictionary font)
    {
        // Both exact approved native resources and their authored output lack ToUnicode.
        // A later remapping must not let extracted characters stand in for different painted codes.
        Require(!font.ContainsKey(PdfName.ToUnicode), "E_VERIFY_AP_FONT_MAPPING", "The native font Unicode mapping differs from its reviewed absence.");
        return SourceMetricsSha256(font);
    }

    // Exactly the existing ConfigurationInspection.ObserveFont metric
    // serialization: these ordered font/descriptor keys, '=' and ';', UTF-8,
    // SHA-256 lowercase. Mapping presence is a separate observed fact; the
    // preserve strategy still rejects it above, while source hashing does not.
    internal static string SourceMetricsSha256(PdfDictionary font)
    {
        var text = new StringBuilder();
        foreach (var key in new[] { PdfName.BaseFont, PdfName.Subtype, PdfName.Encoding, PdfName.FirstChar, PdfName.LastChar, PdfName.Widths })
            text.Append(key.GetValue()).Append('=').Append(font.Get(key, true)?.ToString()).Append(';');
        var descriptor = font.GetAsDictionary(PdfName.FontDescriptor);
        foreach (var key in new[] { PdfName.FontName, PdfName.Flags, PdfName.FontBBox, PdfName.ItalicAngle, PdfName.Ascent, PdfName.Descent, PdfName.CapHeight, PdfName.StemV })
            text.Append(key.GetValue()).Append('=').Append(descriptor?.Get(key, true)?.ToString()).Append(';');
        return Digest(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static string PreservedDefaultAppearance(NativeFontWitness witness)
    {
        Require(witness.Policy == PreservedFontPolicy && witness.EmbeddedDigest is not null && !witness.PrivateUnembeddedException
            && float.IsFinite(witness.Size) && witness.Size is >= 1 and <= 72,
            "E_VERIFY_FONT_POLICY", "The preserved output-font policy is inconsistent.");
        return "/PdfFormPreserved " + witness.Size.ToString("0.#########", CultureInfo.InvariantCulture) + " Tf 0 g";
    }

    // Local resources preserve this exact admitted source font. A document may
    // contain different preserved fonts/sizes, so no shared AcroForm default is changed.
    internal static void SetPreservedDefaults(PdfFormField field, PdfFont font, NativeFontWitness witness)
    {
        string appearance = PreservedDefaultAppearance(witness);
        VerifyFont(font, witness, new VerificationContext());
        var fonts = new PdfDictionary(); fonts.Put(PreservedResourceName, font.GetPdfObject());
        var resources = new PdfDictionary(); resources.Put(PdfName.Font, fonts);
        foreach (var node in new[] { field.GetPdfObject(), field.GetWidgets()[0].GetPdfObject() })
        {
            node.Put(PdfName.DA, new PdfString(appearance));
            node.Put(PdfName.DR, resources);
        }
    }

    // Independent of AP text events: a cleared q/Q stream still has editable defaults.
    internal static void VerifyPreservedDefaults(PdfFormField field, NativeFontWitness witness, VerificationContext context)
    {
        string appearance = PreservedDefaultAppearance(witness);
        PdfDictionary? expected = null;
        foreach (var node in new[] { field.GetPdfObject(), field.GetWidgets()[0].GetPdfObject() })
        {
            Require(node.GetAsString(PdfName.DA)?.ToUnicodeString() == appearance,
                "E_VERIFY_FIELD_FONT_DEFAULT", "An editable preserved-font default differs from its fixed policy.");
            var resources = node.GetAsDictionary(PdfName.DR);
            var fonts = resources?.GetAsDictionary(PdfName.Font);
            var dictionary = fonts?.GetAsDictionary(PreservedResourceName);
            Require(resources is not null && resources is not PdfStream && fonts is not null && fonts is not PdfStream
                && dictionary is not null && dictionary is not PdfStream,
                "E_VERIFY_FIELD_FONT_RESOURCE", "An editable preserved-font resource is missing or malformed.");
            VerifyFont(context.Font(dictionary!), witness, context);
            Require(expected is null || ReferenceEquals(expected, dictionary)
                || expected.GetIndirectReference() is { } reference && reference.Equals(dictionary!.GetIndirectReference()),
                "E_VERIFY_FIELD_FONT_RESOURCE", "Preserved field and widget defaults do not share their exact font resource.");
            expected = dictionary;
        }
    }

    internal static void VerifyFont(PdfFont font, NativeFontWitness expected, VerificationContext context)
    {
        var dictionary = font.GetPdfObject();
        Require(dictionary.GetAsName(PdfName.BaseFont)?.GetValue() == expected.Name
            && context.Metrics(dictionary) == expected.MetricsDigest,
            "E_VERIFY_AP_FONT", "The output font differs from its explicit resource policy.");
        var descriptor = dictionary.GetAsDictionary(PdfName.FontDescriptor);
        Require(!dictionary.ContainsKey(PdfName.FontDescriptor) || descriptor is not null && descriptor is not PdfStream,
            "E_VERIFY_AP_FONT_PROGRAM", "The output font descriptor is malformed.");
        var embedded = descriptor?.GetAsStream(PdfName.FontFile2);
        Require(expected.EmbeddedDigest is null ? embedded is null && !font.IsEmbedded()
            : embedded is not null && font.IsEmbedded() && context.EmbeddedDigest(embedded) == expected.EmbeddedDigest,
            "E_VERIFY_AP_FONT_PROGRAM", "The appearance embedded-font identity changed.");
        if (expected.EmbeddedDigest is not null)
            Require(PdfName.TrueType.Equals(dictionary.GetAsName(PdfName.Subtype))
                && PdfName.WinAnsiEncoding.Equals(dictionary.GetAsName(PdfName.Encoding))
                && descriptor is not null && !descriptor.ContainsKey(PdfName.FontFile) && !descriptor.ContainsKey(PdfName.FontFile3),
                "E_VERIFY_AP_FONT_PROGRAM", "The explicit embedded-font representation changed.");
    }

    /// <summary>Read-only-document-local memoization avoids repeatedly decompressing the same verified font per glyph.</summary>
    internal sealed class VerificationContext
    {
        private readonly Dictionary<PdfDictionary, string> metrics = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PdfDictionary, PdfFont> fonts = new(ReferenceEqualityComparer.Instance);
        internal PdfFont Font(PdfDictionary dictionary)
        {
            if (fonts.TryGetValue(dictionary, out var font)) return font;
            Require(fonts.Count < 32, "E_VERIFY_FONT_LIMIT", "The candidate has too many distinct font resources.");
            return fonts[dictionary] = PdfFontFactory.CreateFont(dictionary);
        }
        private readonly Dictionary<PdfStream, string> embedded = new(ReferenceEqualityComparer.Instance);
        private long decodedFontBytes;
        internal string Metrics(PdfDictionary font)
        {
            if (metrics.TryGetValue(font, out var hash)) return hash;
            Require(metrics.Count < 32, "E_VERIFY_FONT_LIMIT", "The candidate has too many distinct font resources.");
            return metrics[font] = MetricsDigest(font);
        }
        internal string EmbeddedDigest(PdfStream stream)
        {
            if (embedded.TryGetValue(stream, out var hash)) return hash;
            Require(embedded.Count < 32, "E_VERIFY_FONT_LIMIT", "The candidate has too many embedded font resources.");
            byte[] bytes = stream.GetBytes();
            decodedFontBytes += bytes.Length;
            Require(bytes.Length <= 10 * 1024 * 1024 && decodedFontBytes <= 20 * 1024 * 1024,
                "E_VERIFY_FONT_LIMIT", "Decoded font resources exceed the bounded verification budget.");
            return embedded[stream] = Digest(bytes);
        }
    }

    private sealed class AppearanceWitness(TargetWitness target, double[] clip, VerificationContext context) : IEventListener
    {
        private readonly StringBuilder text = new();
        internal PdfCanvasProcessor Processor { private get; set; } = null!;
        internal string Text => text.ToString();
        internal bool HasImage { get; private set; }
        internal int GlyphCount { get; private set; }
        public ICollection<EventType> GetSupportedEvents() => new HashSet<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_IMAGE) { HasImage = true; return; }
            if (type != EventType.RENDER_TEXT || data is not TextRenderInfo info) return;
            text.Append(info.GetText());
            var font = info.GetFont();
            float fontSize = WitnessSize(target.Widget, target.Font!);
            var state = Processor.GetGraphicsState();
            Require(Math.Abs(state.GetFontSize() - fontSize) < .001 && Math.Abs(state.GetHorizontalScaling() - 100) < .001
                && Math.Abs(state.GetCharSpacing()) < .001 && Math.Abs(state.GetWordSpacing()) < .001
                && info.GetTextRenderMode() == 0 && Math.Abs(info.GetRise()) < .001,
                "E_VERIFY_AP_TEXT_STATE", "The appearance changed fixed-size text painting policy.");
            var expectedFont = target.Font ?? throw new VerifiedFillException("E_VERIFY_AP_FONT", "The expected text-font witness is missing.");
            VerifyFont(font, expectedFont, context);
            Require(info.GetFillColor() is iText.Kernel.Colors.DeviceGray gray
                && gray.GetColorValue() is [var black] && black == 0,
                "E_VERIFY_AP_COLOR", "The appearance must paint exact authored DeviceGray black text.");
            foreach (var character in info.GetCharacterRenderInfos())
            {
                var runes = character.GetText().EnumerateRunes().ToArray();
                Require(runes.Length == 1, "E_VERIFY_AP_ENCODING", "A native character could not be independently decoded.");
                var start = character.GetBaseline().GetStartPoint();
                var end = character.GetBaseline().GetEndPoint();
                Require(Math.Abs(end.Get(1) - start.Get(1)) < .02 && end.Get(0) >= start.Get(0),
                    "E_VERIFY_AP_ORIENTATION", "The appearance contains an unsupported text transform.");
                float advance = font.GetWidth(runes[0].Value, fontSize);
                Require(Math.Abs((end.Get(0) - start.Get(0)) - advance) < .03,
                    "E_VERIFY_AP_SCALE", "The appearance changes native glyph advance widths.");
                var matrix = character.GetTextMatrix();
                var ctm = state.GetCtm();
                Require(Math.Abs(matrix.Get(Matrix.I11) - 1) < .00001 && Math.Abs(matrix.Get(Matrix.I12)) < .00001
                    && Math.Abs(matrix.Get(Matrix.I21)) < .00001 && Math.Abs(matrix.Get(Matrix.I22) - 1) < .00001
                    && Math.Abs(ctm.Get(Matrix.I11) - 1) < .00001 && Math.Abs(ctm.Get(Matrix.I12)) < .00001
                    && Math.Abs(ctm.Get(Matrix.I21)) < .00001 && Math.Abs(ctm.Get(Matrix.I22) - 1) < .00001,
                    "E_VERIFY_AP_TRANSFORM", "The native appearance has an unsupported full text transform.");
                float left, right, bottom, top;
                var glyphBox = expectedFont.ExactOutlines?.GetGlyph(runes[0].Value)?.GetBbox();
                if (glyphBox is not null)
                {
                    left = start.Get(0) + glyphBox[0] * fontSize / 1000;
                    right = start.Get(0) + glyphBox[2] * fontSize / 1000;
                    bottom = start.Get(1) + glyphBox[1] * fontSize / 1000;
                    top = start.Get(1) + glyphBox[3] * fontSize / 1000;
                }
                else if (runes[0].Value == ' ') { GlyphCount++; continue; }
                else
                {
                    Require(expectedFont.PrivateUnembeddedException, "E_VERIFY_AP_OUTLINE", "A native glyph outline cannot be verified.");
                    // Descriptor bounds are deliberately not described as actual outlines under the private exception.
                    left = start.Get(0); right = end.Get(0);
                    bottom = start.Get(1) + font.GetDescent(character.GetText(), fontSize);
                    top = start.Get(1) + font.GetAscent(character.GetText(), fontSize);
                }
                double width = target.Widget.Rectangle[2] - target.Widget.Rectangle[0];
                double height = target.Widget.Rectangle[3] - target.Widget.Rectangle[1];
                Require(left >= -.02 && right <= width + .02 && bottom >= -.02 && top <= height + .02,
                    "E_VERIFY_AP_INK_BOUNDS", "An observed glyph exceeds the normal appearance bounds.");
                Require(left >= clip[0] - .02 && right <= clip[2] + .02 && bottom >= clip[1] - .02 && top <= clip[3] + .02,
                    "E_VERIFY_AP_CLIPPED_INK", "The generated clip hides part of the expected glyph bounds.");
                if (target.DateCells is null && target.Widget.Effective.Flags.Comb)
                {
                    int slots = target.MaxLength ?? 0;
                    int count = target.Assignment.Text.EnumerateRunes().Count();
                    int startCell = target.Alignment == 2 ? slots - count : target.Alignment == 1 ? (slots - count) / 2 : 0;
                    Require(slots > 0 && startCell >= 0 && GlyphCount < count,
                        "E_VERIFY_COMB_CAPACITY", "The comb appearance has an unsupported glyph count.");
                    double cellWidth = width / slots;
                    double cellLeft = (startCell + GlyphCount) * cellWidth;
                    double cellRight = cellLeft + cellWidth;
                    Require(left >= cellLeft - .02 && right <= cellRight + .02
                        && Math.Abs((start.Get(0) + end.Get(0)) / 2 - (cellLeft + cellRight) / 2) <= .1,
                        "E_VERIFY_COMB_CELL", "A painted comb glyph is outside its expected centered cell.");
                }
                if (target.DateCells is not null)
                {
                    Require(GlyphCount < 8, "E_VERIFY_DATE_GLYPHS", "The date appearance has too many glyphs.");
                    var cell = target.DateCells[GlyphCount];
                    Require(left + target.Widget.Rectangle[0] >= cell[0] + DatePadding - .02
                        && right + target.Widget.Rectangle[0] <= cell[2] - DatePadding + .02
                        && bottom + target.Widget.Rectangle[1] >= cell[1] - .02 && top + target.Widget.Rectangle[1] <= cell[3] + .02,
                        "E_VERIFY_DATE_CELL", "A painted date glyph exceeds its reviewed slash-aware cell.");
                    var ink = InkBounds(font, target.Font!, target.Assignment.Text);
                    double expectedBaseline = (cell[1] + cell[3]) / 2 - target.Widget.Rectangle[1] - (ink.Top + ink.Bottom) / 2;
                    Require(Math.Abs((start.Get(0) + end.Get(0)) / 2 + target.Widget.Rectangle[0] - (cell[0] + cell[2]) / 2) < .1
                        && Math.Abs(start.Get(1) - expectedBaseline) < .02,
                        "E_VERIFY_DATE_POSITION", "A painted date glyph differs from its fixed-size centered cell baseline.");
                }
                GlyphCount++;
            }
        }
    }
}
