using System.Collections.Concurrent;
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
    FontProgram? ExactOutlines, bool PrivateUnembeddedException);

/// <summary>Fixed native-resource rendering only. A narrowly scoped private exception does not establish portable font fidelity.</summary>
internal static class NativeTextAppearance
{
    internal const float FontSize = 10;
    private const float Padding = 1;
    private const float DatePadding = .5f;
    private const string DateTarget = "topmostSubform[0].Page2[0].f2_9[0]";
    // Explicit exact-template policy: printable ASCII plus U+00C9. All 96 glyphs have bounded
    // dual-renderer visibility evidence; each supplied value must still pass native glyph and fit checks.
    // This does not qualify the unembedded font's substituted outlines or arbitrary Unicode.
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
        FontInspection inspected, out NativeFontWitness witness)
    {
        Require(inspected.Size == FontSize && inspected.ResourcePresent && inspected.Name is not null,
            "E_FONT_FIXED_SIZE", "The renderer requires a supported fixed 10-point template font.", 3);
        string expectedName = templateSha256 == ApprovedTemplates.Form941Sha256 ? "TimesNewRomanPSMT"
            : templateSha256 == ApprovedTemplates.ScheduleBSha256 ? "TimesLTStd-Roman" : "";
        var acro = PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject();
        var resources = Inherited(field.GetPdfObject(), PdfName.DR, acro) as PdfDictionary;
        var dict = resources?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(new PdfName(inspected.Name!));
        Require(dict is not null && inspected.Name == expectedName && dict.GetAsName(PdfName.BaseFont)?.GetValue() == expectedName,
            "E_FONT_RESOURCE", "The effective font is not the exact expected native resource.", 3);
        Require(!dict!.ContainsKey(PdfName.ToUnicode), "E_FONT_MAPPING", "The exact native font requires the reviewed absence of a Unicode remapping stream.", 3);
        var font = PdfFontFactory.CreateFont(dict);
        var stream = dict!.GetAsDictionary(PdfName.FontDescriptor)?.GetAsStream(PdfName.FontFile2);
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
            // Explicit bounded-development adoption. Changing the release scope cannot silently inherit this exception.
            privateException = templateSha256 == ApprovedTemplates.ScheduleBSha256
                && ProductionProfileRegistry.AdoptionScope == "bounded_private_development"
                && dict.GetAsName(PdfName.Subtype)?.GetValue() == "Type1"
                && dict.GetAsName(PdfName.Encoding)?.GetValue() == "WinAnsiEncoding" && !font.IsEmbedded();
            Require(privateException, "E_FONT_NOT_EMBEDDED", "No approved portable font strategy is available for this resource.", 3);
        }
        witness = new(expectedName, MetricsDigest(dict), embeddedDigest, program, privateException);
        return font;
    }

    internal static void CheckFit(PdfFormField field, WidgetInspection widget, string value, PdfFont font, NativeFontWitness witness)
    {
        var runes = value.EnumerateRunes().ToArray();
        Require(!value.Any(char.IsControl), "E_TEXT_CONTROL", "Control characters are unsupported in text appearances.", 3);
        Require(runes.All(r => font.ContainsGlyph(r.Value)), "E_FONT_GLYPH", "A supplied character has no glyph in the exact native font.", 3);
        if (witness.PrivateUnembeddedException)
            Require(runes.All(r => r.Value is >= 0x20 and <= 0x7e or 0x00c9),
                "E_FONT_GLYPH_UNQUALIFIED", "Schedule B supports printable ASCII (U+0020-U+007E) and U+00C9 only under its unembedded-font policy.", 3);
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
            Require(runes.All(r => font.GetWidth(r.Value, FontSize) <= width - Padding),
                "E_TEXT_COMB_OVERFLOW", "A character does not fit its fixed-size comb cell.");
        }
        else Require(font.GetWidth(value, FontSize) <= rect.GetWidth() - 2 * Padding,
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
        var rect = field.GetWidgets()[0].GetRectangle().ToRectangle();
        var box = new PdfFormXObject(new Rectangle(rect.GetWidth(), rect.GetHeight()));
        var canvas = new PdfCanvas(box, pdf);
        var ink = InkBounds(font, target.Font!, target.Assignment.Text);
        float baseline = (rect.GetHeight() - (ink.Top - ink.Bottom)) / 2 - ink.Bottom;
        canvas.SetFillColorGray(0).BeginText().SetFontAndSize(font, FontSize);
        for (int i = 0; i < 8; i++)
        {
            string character = target.Assignment.Text[i].ToString();
            var cell = target.DateCells![i];
            float width = font.GetWidth(character, FontSize);
            float left = (float)((cell[0] + cell[2]) / 2) - width / 2;
            var glyph = target.Font!.ExactOutlines!.GetGlyph(character[0])?.GetBbox();
            Require(glyph is { Length: 4 } && left + glyph[0] * FontSize / 1000 >= cell[0] + DatePadding
                && left + glyph[2] * FontSize / 1000 <= cell[2] - DatePadding
                && rect.GetBottom() + baseline + glyph[1] * FontSize / 1000 >= cell[1] - .002
                && rect.GetBottom() + baseline + glyph[3] * FontSize / 1000 <= cell[3] + .002,
                "E_DATE_GLYPH_BOUNDS", "A date glyph does not fit inside its reviewed slash-aware cell.");
            canvas.SetTextMatrix(left - rect.GetLeft(), baseline).ShowText(character);
        }
        canvas.EndText().Release();
        var ap = new PdfDictionary(); ap.Put(PdfName.N, box.GetPdfObject());
        field.GetWidgets()[0].GetPdfObject().Put(PdfName.AP, ap);
    }

    internal static void Verify(PdfStream appearance, TargetWitness target, VerificationContext context)
    {
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
        if (witness.ExactOutlines is null) return (font.GetDescent(value, FontSize), font.GetAscent(value, FontSize));
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        foreach (var rune in value.EnumerateRunes())
        {
            var glyph = witness.ExactOutlines.GetGlyph(rune.Value);
            Require(glyph is not null, "E_FONT_OUTLINE_GLYPH", "An embedded native glyph cannot be measured.", 3);
            var box = glyph!.GetBbox();
            if (box is null) { Require(rune.Value == ' ', "E_FONT_OUTLINE_GLYPH", "A non-space glyph has no measurable outline.", 3); continue; }
            bottom = Math.Min(bottom, box[1] * FontSize / 1000);
            top = Math.Max(top, box[3] * FontSize / 1000);
        }
        return float.IsFinite(bottom) ? (bottom, top) : (0, 0);
    }
    private static string MetricsDigest(PdfDictionary font)
    {
        // Both exact approved native resources and their authored output lack ToUnicode.
        // A later remapping must not let extracted characters stand in for different painted codes.
        Require(!font.ContainsKey(PdfName.ToUnicode), "E_VERIFY_AP_FONT_MAPPING", "The native font Unicode mapping differs from its reviewed absence.");
        var text = new StringBuilder();
        foreach (var key in new[] { PdfName.BaseFont, PdfName.Subtype, PdfName.Encoding, PdfName.FirstChar, PdfName.LastChar, PdfName.Widths })
            text.Append(key.GetValue()).Append('=').Append(font.Get(key, true)?.ToString()).Append(';');
        var descriptor = font.GetAsDictionary(PdfName.FontDescriptor);
        foreach (var key in new[] { PdfName.FontName, PdfName.Flags, PdfName.FontBBox, PdfName.ItalicAngle, PdfName.Ascent, PdfName.Descent, PdfName.CapHeight, PdfName.StemV })
            text.Append(key.GetValue()).Append('=').Append(descriptor?.Get(key, true)?.ToString()).Append(';');
        return Digest(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>Read-only-document-local memoization avoids repeatedly decompressing the same verified font per glyph.</summary>
    internal sealed class VerificationContext
    {
        private readonly Dictionary<PdfDictionary, string> metrics = new(ReferenceEqualityComparer.Instance);
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
            var state = Processor.GetGraphicsState();
            Require(Math.Abs(state.GetFontSize() - FontSize) < .001 && Math.Abs(state.GetHorizontalScaling() - 100) < .001
                && Math.Abs(state.GetCharSpacing()) < .001 && Math.Abs(state.GetWordSpacing()) < .001
                && info.GetTextRenderMode() == 0 && Math.Abs(info.GetRise()) < .001,
                "E_VERIFY_AP_TEXT_STATE", "The appearance changed fixed-size text painting policy.");
            Require(font.GetPdfObject().GetAsName(PdfName.BaseFont)?.GetValue() == target.Font!.Name
                && context.Metrics(font.GetPdfObject()) == target.Font.MetricsDigest,
                "E_VERIFY_AP_FONT", "The appearance font differs from the exact native resource.");
            var embedded = font.GetPdfObject().GetAsDictionary(PdfName.FontDescriptor)?.GetAsStream(PdfName.FontFile2);
            Require(target.Font.EmbeddedDigest is null ? embedded is null && !font.IsEmbedded()
                : embedded is not null && context.EmbeddedDigest(embedded) == target.Font.EmbeddedDigest,
                "E_VERIFY_AP_FONT_PROGRAM", "The appearance embedded-font identity changed.");
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
                float advance = font.GetWidth(runes[0].Value, FontSize);
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
                var glyphBox = target.Font.ExactOutlines?.GetGlyph(runes[0].Value)?.GetBbox();
                if (glyphBox is not null)
                {
                    left = start.Get(0) + glyphBox[0] * FontSize / 1000;
                    right = start.Get(0) + glyphBox[2] * FontSize / 1000;
                    bottom = start.Get(1) + glyphBox[1] * FontSize / 1000;
                    top = start.Get(1) + glyphBox[3] * FontSize / 1000;
                }
                else if (runes[0].Value == ' ') { GlyphCount++; continue; }
                else
                {
                    Require(target.Font.PrivateUnembeddedException, "E_VERIFY_AP_OUTLINE", "A native glyph outline cannot be verified.");
                    // Descriptor bounds are deliberately not described as actual outlines under the private exception.
                    left = start.Get(0); right = end.Get(0);
                    bottom = start.Get(1) + font.GetDescent(character.GetText(), FontSize);
                    top = start.Get(1) + font.GetAscent(character.GetText(), FontSize);
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
                }
                GlyphCount++;
            }
        }
    }
}
