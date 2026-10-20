// Standalone synthetic fixture source. NOT BUILT OR EXECUTED; no project wiring or product run.
// Pinned API target: iText 9.8.0. No product assembly references, file/network/system-font access or Main.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;

namespace PdfForm.StandaloneSyntheticFixtures;

public enum WorkshopVariant
{
    Blank, TextCurrentPrefilled, TextDefaultPrefilled, CheckboxPrefilled,
    UnsupportedRadio, NoInteractiveForm, WidgetGeometryChanged, MetadataHashOnlyChanged
}

// Caller supplies already-approved exact TrueType bytes, independent expected SHA-256 and
// an opaque approval reference. A matching digest/reference is NOT license/approval proof:
// root must supply that external approval. No font bytes or approval are bundled or assumed.
public sealed record ApprovedFixtureFont(ReadOnlyMemory<byte> TrueTypeBytes,
    string ExpectedSha256, string ApprovalReference);

/// <summary>One fixed fictional workshop form and seven discriminating variants.</summary>
public static class ThirdWorkshopFixtureGenerator
{
    public const string Revision = "third-workshop-fixture/1";
    public const string Title = "FICTIONAL CEDAR WORKSHOP - Request";
    public const string ParticipantField = "grove_entry_alpha";
    public const string ProjectField = "grove_entry_beta";
    // Deliberately ordinary text: no date name/tooltip/script/format metadata or split cells.
    public const string ReferenceField = "grove_entry_gamma";
    public const string BadgeField = "grove_choice_delta";
    public const string RadioField = "grove_unsupported_epsilon";
    public const string CheckboxOn = "Yes";
    public const string FictionalOldText = "FICTIONAL OLD ENTRY";
    private const string FontResource = "FThird";
    private const string DefaultAppearance = "/FThird 10 Tf 0 g";
    private const string FixedDate = "D:20000101000000Z";
    private const int MaximumFontBytes = 2 * 1024 * 1024;
    private const int MaximumPdfBytes = 4 * 1024 * 1024;
    private static readonly PdfName Off = new("Off");

    // No arbitrary fields, layout, imports, font lookup or designer options are accepted.
    public static byte[] Generate(ApprovedFixtureFont approvedFont, WorkshopVariant variant = WorkshopVariant.Blank)
    {
        ArgumentNullException.ThrowIfNull(approvedFont);
        if (!Enum.IsDefined(typeof(WorkshopVariant), variant)) throw new ArgumentOutOfRangeException(nameof(variant));
        byte[] fontBytes = approvedFont.TrueTypeBytes.ToArray();
        if (fontBytes.Length is < 12 or > MaximumFontBytes ||
            fontBytes[0] != 0 || fontBytes[1] != 1 || fontBytes[2] != 0 || fontBytes[3] != 0)
            throw new ArgumentException("E_FIXTURE_TRUETYPE_INPUT");
        if (approvedFont.ExpectedSha256 is null || approvedFont.ExpectedSha256.Length != 64 ||
            !approvedFont.ExpectedSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            Digest(fontBytes) != approvedFont.ExpectedSha256)
            throw new ArgumentException("E_FIXTURE_FONT_HASH");
        if (string.IsNullOrWhiteSpace(approvedFont.ApprovalReference) || approvedFont.ApprovalReference.Length > 80 ||
            !approvedFont.ApprovalReference.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ArgumentException("E_FIXTURE_FONT_APPROVAL_REFERENCE");

        // Fixed trailer IDs, fixed info dates, no font subsetting, compression/XMP randomness,
        // locale formatting, wall clock, GUIDs or input paths. Repeat-byte determinism still
        // requires the acceptance owner's pinned iText/runtime closure and later repeat test.
        byte[] id = SHA256.HashData(Encoding.ASCII.GetBytes(Revision + "/" + variant + "/" + approvedFont.ExpectedSha256));
        var properties = new WriterProperties().SetPdfVersion(PdfVersion.PDF_1_7)
            .SetCompressionLevel(CompressionConstants.NO_COMPRESSION).SetFullCompressionMode(false)
            .SetInitialDocumentId(new PdfString(id.Take(16).ToArray()))
            .SetModifiedDocumentId(new PdfString(id.Take(16).ToArray()));
        using var output = new MemoryStream();
        using (var pdf = new PdfDocument(new PdfWriter(output, properties)))
        {
            var info = pdf.GetDocumentInfo();
            info.SetTitle(Title).SetAuthor("Fictional Fixture Team").SetCreator(Revision)
                .SetSubject(variant == WorkshopVariant.MetadataHashOnlyChanged
                    ? "Synthetic non-tax fixture; metadata-only revision" : "Synthetic non-tax fixture");
            info.SetMoreInfo("CreationDate", FixedDate);
            info.SetMoreInfo("ModDate", FixedDate);
            var page = pdf.AddNewPage(new PageSize(612, 792));
            var font = PdfFontFactory.CreateFont(fontBytes, PdfEncodings.WINANSI,
                PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED, false);
            font.SetSubset(false);
            if (!font.IsEmbedded() || Enumerable.Range(32, 95).Any(c => !font.ContainsGlyph(c)))
                throw new InvalidOperationException("E_FIXTURE_FONT_COVERAGE_OR_EMBEDDING");
            font.GetPdfObject().MakeIndirect(pdf);
            float referenceX = variant == WorkshopVariant.WidgetGeometryChanged ? 76 : 72;
            DrawStaticPage(page, font, referenceX, variant == WorkshopVariant.UnsupportedRadio);
            if (variant != WorkshopVariant.NoInteractiveForm)
            {
                var fields = new PdfArray();
                var form = new PdfDictionary();
                form.Put(PdfName.Fields, fields);
                form.Put(PdfName.NeedAppearances, PdfBoolean.FALSE);
                form.Put(PdfName.DA, new PdfString(DefaultAppearance));
                form.Put(PdfName.DR, FontResources(font));
                form.MakeIndirect(pdf);
                pdf.GetCatalog().GetPdfObject().Put(PdfName.AcroForm, form);
                AddText(pdf, page, fields, font, ParticipantField, new Rectangle(72, 624, 360, 20), 48,
                    variant == WorkshopVariant.TextCurrentPrefilled ? FictionalOldText : "",
                    variant == WorkshopVariant.TextDefaultPrefilled ? FictionalOldText : "");
                AddText(pdf, page, fields, font, ProjectField, new Rectangle(72, 552, 200, 20), 20, "", "");
                AddText(pdf, page, fields, font, ReferenceField, new Rectangle(referenceX, 480, 200, 20), 10, "", "");
                AddCheckbox(pdf, page, fields, variant == WorkshopVariant.CheckboxPrefilled);
                if (variant == WorkshopVariant.UnsupportedRadio) AddRadioNegative(pdf, page, fields);
            }
            // This freshly authored PDF has no imported content, XFA, actions, signatures,
            // attachments, encryption, JavaScript, tooltips or executable format metadata.
        }
        byte[] bytes = output.ToArray(); // MemoryStream.ToArray remains valid after writer close.
        if (bytes.Length is < 1 or > MaximumPdfBytes) throw new InvalidOperationException("E_FIXTURE_PDF_SIZE");
        return bytes;
    }

    public static string SuggestedFileName(WorkshopVariant variant) => variant switch
    {
        WorkshopVariant.Blank => "cedar-workshop-blank.pdf",
        WorkshopVariant.TextCurrentPrefilled => "cedar-workshop-prefilled-current.pdf",
        WorkshopVariant.TextDefaultPrefilled => "cedar-workshop-prefilled-default.pdf",
        WorkshopVariant.CheckboxPrefilled => "cedar-workshop-prefilled-checkbox.pdf",
        WorkshopVariant.UnsupportedRadio => "cedar-workshop-radio.pdf",
        WorkshopVariant.NoInteractiveForm => "cedar-workshop-static.pdf",
        WorkshopVariant.WidgetGeometryChanged => "cedar-workshop-geometry-change.pdf",
        WorkshopVariant.MetadataHashOnlyChanged => "cedar-workshop-hash-only-change.pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(variant))
    };

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static PdfDictionary FontResources(PdfFont font)
    {
        var fonts = new PdfDictionary(); fonts.Put(new PdfName(FontResource), font.GetPdfObject());
        var resources = new PdfDictionary(); resources.Put(PdfName.Font, fonts); return resources;
    }

    private static void DrawStaticPage(PdfPage page, PdfFont font, float referenceX, bool radio)
    {
        var canvas = new PdfCanvas(page);
        Label(canvas, font, 14, 72, 730, Title);
        Label(canvas, font, 10, 72, 706, "FICTIONAL TEST FORM - DO NOT SUBMIT");
        Label(canvas, font, 10, 72, 654, "Participant");
        Label(canvas, font, 10, 72, 582, "Project code");
        Label(canvas, font, 10, 72, 510, "Reference entry");
        Label(canvas, font, 10, 90, 420, "Include printed badge");
        Label(canvas, font, 10, 72, 100, "Synthetic workshop request. All example people and entries are fictional.");
        canvas.SetStrokeColorGray(0).SetLineWidth(.75f);
        canvas.Rectangle(72, 624, 360, 20).Rectangle(72, 552, 200, 20)
            .Rectangle(referenceX, 480, 200, 20).Rectangle(72, 420, 10, 10).Stroke();
        if (radio)
        {
            Label(canvas, font, 10, 72, 380, "Unsupported radio negative control");
            Label(canvas, font, 10, 90, 350, "A"); Label(canvas, font, 10, 150, 350, "B");
            canvas.Rectangle(72, 350, 10, 10).Rectangle(132, 350, 10, 10).Stroke();
        }
        canvas.Release();
    }

    private static void Label(PdfCanvas canvas, PdfFont font, float size, float x, float y, string text)
    {
        if (font.GetWidth(text, size) > 468) throw new InvalidOperationException("E_FIXTURE_LABEL_FIT");
        canvas.SetFillColorGray(0).BeginText().SetFontAndSize(font, size).SetTextMatrix(x, y).ShowText(text).EndText();
    }

    // Merged field/widget dictionaries are canonical /Fields entries and the very same
    // indirect /Annots objects: no orphan reconstruction, duplicate tree, or detached values.
    private static PdfWidgetAnnotation Widget(PdfDocument pdf, PdfPage page, Rectangle rectangle)
    {
        var widget = new PdfWidgetAnnotation(rectangle);
        var dictionary = widget.GetPdfObject();
        dictionary.Put(PdfName.F, new PdfNumber(4)); // Print; visible, no Hidden/NoView.
        dictionary.Put(PdfName.Border, new PdfArray(new double[] { 0, 0, 0 }));
        dictionary.MakeIndirect(pdf);
        page.AddAnnotation(widget);
        return widget;
    }

    private static void AddText(PdfDocument pdf, PdfPage page, PdfArray fields, PdfFont font,
        string name, Rectangle rectangle, int capacity, string current, string resetDefault)
    {
        var dictionary = Widget(pdf, page, rectangle).GetPdfObject();
        dictionary.Put(PdfName.FT, PdfName.Tx); dictionary.Put(PdfName.T, new PdfString(name));
        dictionary.Put(PdfName.Ff, new PdfNumber(0)); dictionary.Put(PdfName.Q, new PdfNumber(0));
        dictionary.Put(PdfName.MaxLen, new PdfNumber(capacity));
        dictionary.Put(PdfName.DA, new PdfString(DefaultAppearance));
        dictionary.Put(PdfName.V, new PdfString(current)); dictionary.Put(PdfName.DV, new PdfString(resetDefault));
        var appearance = Appearance(pdf, rectangle.GetWidth(), rectangle.GetHeight(), "q\nQ\n");
        if (current.Length != 0)
        {
            if (font.GetWidth(current, 10) > rectangle.GetWidth() - 4 ||
                font.GetAscent(current, 10) - font.GetDescent(current, 10) > rectangle.GetHeight() - 4)
                throw new InvalidOperationException("E_FIXTURE_PREFILL_FIT");
            var resources = FontResources(font);
            appearance.Put(PdfName.Resources, resources);
            var canvas = new PdfCanvas(appearance, new PdfResources(resources), pdf);
            canvas.SaveState().Rectangle(0, 0, rectangle.GetWidth(), rectangle.GetHeight()).Clip().EndPath()
                .SetFillColorGray(0).BeginText().SetFontAndSize(font, 10).SetTextMatrix(2, 6)
                .ShowText(current).EndText().RestoreState().Release();
        }
        var ap = new PdfDictionary(); ap.Put(PdfName.N, appearance); dictionary.Put(PdfName.AP, ap);
        fields.Add(dictionary);
    }

    private static void AddCheckbox(PdfDocument pdf, PdfPage page, PdfArray fields, bool prefilled)
    {
        var dictionary = Widget(pdf, page, new Rectangle(72, 420, 10, 10)).GetPdfObject();
        dictionary.Put(PdfName.FT, PdfName.Btn); dictionary.Put(PdfName.T, new PdfString(BadgeField));
        dictionary.Put(PdfName.Ff, new PdfNumber(0));
        dictionary.Put(PdfName.V, prefilled ? new PdfName(CheckboxOn) : Off); dictionary.Put(PdfName.DV, Off);
        dictionary.Put(PdfName.AS, prefilled ? new PdfName(CheckboxOn) : Off);
        dictionary.Put(PdfName.AP, ButtonAppearances(pdf, CheckboxOn)); fields.Add(dictionary);
    }

    private static PdfDictionary ButtonAppearances(PdfDocument pdf, string on)
    {
        var normal = new PdfDictionary();
        normal.Put(Off, Appearance(pdf, 10, 10, "q\nQ\n"));
        normal.Put(new PdfName(on), Appearance(pdf, 10, 10,
            "q\n0 G\n0 g\n1.25 w\n0 J\n0 j\n2 2 m\n8 8 l\nS\n2 8 m\n8 2 l\nS\nQ\n"));
        var ap = new PdfDictionary(); ap.Put(PdfName.N, normal); return ap;
    }

    private static PdfStream Appearance(PdfDocument pdf, float width, float height, string content)
    {
        var stream = new PdfStream(Encoding.ASCII.GetBytes(content));
        stream.Put(PdfName.Type, PdfName.XObject); stream.Put(PdfName.Subtype, PdfName.Form);
        stream.Put(PdfName.FormType, new PdfNumber(1));
        stream.Put(PdfName.BBox, new PdfArray(new double[] { 0, 0, width, height }));
        stream.Put(PdfName.Matrix, new PdfArray(new double[] { 1, 0, 0, 1, 0, 0 }));
        stream.Put(PdfName.Resources, new PdfDictionary()); stream.MakeIndirect(pdf); return stream;
    }

    private static void AddRadioNegative(PdfDocument pdf, PdfPage page, PdfArray fields)
    {
        var group = new PdfDictionary(); var children = new PdfArray();
        group.Put(PdfName.FT, PdfName.Btn); group.Put(PdfName.T, new PdfString(RadioField));
        group.Put(PdfName.Ff, new PdfNumber(1 << 15)); group.Put(PdfName.Kids, children);
        group.Put(PdfName.V, Off); group.Put(PdfName.DV, Off); group.MakeIndirect(pdf);
        for (int index = 0; index < 2; index++)
        {
            var widget = Widget(pdf, page, new Rectangle(72 + 60 * index, 350, 10, 10)).GetPdfObject();
            widget.Put(PdfName.Parent, group); widget.Put(PdfName.AS, Off);
            widget.Put(PdfName.AP, ButtonAppearances(pdf, index == 0 ? "ChoiceA" : "ChoiceB"));
            children.Add(widget);
        }
        fields.Add(group);
    }
}
