using System.Text;
using iText.Forms;
using iText.Forms.Fields;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using static PdfForm.Pdf.VerifiedFormRenderer;

namespace PdfForm.Pdf;

/// <summary>One explicit, bundled asset for the exact Schedule B template. No font discovery or fallback.</summary>
internal static class ScheduleBEmbeddedFont
{
    internal const string Policy = "schedule-b-liberation-serif-ascii-v1";
    internal const string Name = "LiberationSerif";
    internal const string ProgramSha256 = "9caef765d2e891c10dd73658894f01e660fe0c1c83e0bda7d6edf561d8f623d4";
    internal const int ProgramBytes = 393692;
    internal const int MaximumSeedBytes = 1024 * 1024;
    internal const string DefaultAppearance = "/PdfFormScheduleB 10 Tf 0 g";
    internal static readonly PdfName ResourceName = new("PdfFormScheduleB");
    private const string Resource = "PdfForm.Pdf.Fonts.LiberationSerif-Regular.ttf";
    private static readonly Lazy<Seed> Materialized = new(CreateSeed);
    private sealed record Seed(byte[] Bytes, FontProgram Program, string Metrics);
    internal static bool Supports(int codepoint) => codepoint is >= 0x20 and <= 0x7e or 0x00c9;
    internal static string Repertoire => string.Concat(Enumerable.Range(32, 95).Select(char.ConvertFromUtf32)) + "É";

    /// <summary>Owned by one authoring document. The imported font graph is shared by all 208 text fields.</summary>
    internal sealed class DocumentFont(PdfDocument document)
    {
        private PdfFont? font;
        private NativeFontWitness? witness;
        internal PdfFont Get(PdfDocument owner, out NativeFontWitness observed)
        {
            Require(ReferenceEquals(document, owner), "E_FONT_DOCUMENT", "A font cannot be reused across authoring documents.");
            if (font is null)
            {
                var seed = Materialized.Value;
                using var source = new PdfDocument(Reader(seed.Bytes));
                var sourceFonts = source.GetPage(1).GetResources().GetPdfObject().GetAsDictionary(PdfName.Font);
                Require(sourceFonts is not null && sourceFonts.Size() == 1, "E_FONT_SEED", "The bundled font resource is invalid.");
                var original = sourceFonts!.GetAsDictionary(sourceFonts.KeySet().Single());
                var imported = (PdfDictionary)original.CopyTo(document);
                font = PdfFontFactory.CreateFont(imported);
                // Copying an accepted seed is not itself proof of the copied program, widths or mapping.
                CheckProgram(font, seed.Program);
                Require(NativeTextAppearance.MetricsDigest(imported) == seed.Metrics,
                    "E_FONT_IMPORT", "Imported bundled font metrics differ from their materialized identity.");
                witness = new(Name, seed.Metrics, ProgramSha256, seed.Program, false, Policy);
            }
            observed = witness!;
            return font;
        }
    }

    private static Seed CreateSeed()
    {
        using var resource = typeof(ScheduleBEmbeddedFont).Assembly.GetManifestResourceStream(Resource);
        Require(resource is not null && resource.Length == ProgramBytes, "E_FONT_ASSET", "The pinned bundled font asset is missing or has the wrong size.", 3);
        byte[] bytes = new byte[ProgramBytes];
        resource!.ReadExactly(bytes);
        Require(resource.ReadByte() == -1 && Digest(bytes) == ProgramSha256,
            "E_FONT_ASSET", "The bundled font asset differs from its admitted identity.", 3);
        var program = FontProgramFactory.CreateFont(bytes, false);
        byte[] serialized;
        using (var output = new SeedStream())
        {
            using (var pdf = new PdfDocument(new PdfWriter(output)))
            {
                var font = PdfFontFactory.CreateFont(program, PdfEncodings.WINANSI, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
                font.SetSubset(false);
                new PdfCanvas(pdf.AddNewPage()).BeginText().SetFontAndSize(font, 10).MoveText(10, 10)
                    .ShowText(Repertoire).EndText().Release();
            }
            serialized = output.ToArray();
        }
        using var reopened = new PdfDocument(Reader(serialized));
        var resources = reopened.GetPage(1).GetResources().GetPdfObject().GetAsDictionary(PdfName.Font);
        Require(resources is not null && resources.Size() == 1, "E_FONT_SEED", "The bundled font seed has an unexpected resource set.");
        var dictionary = resources!.GetAsDictionary(resources.KeySet().Single());
        CheckProgram(PdfFontFactory.CreateFont(dictionary), program);
        return new(serialized, program, NativeTextAppearance.MetricsDigest(dictionary));
    }

    private static void CheckProgram(PdfFont font, FontProgram program)
    {
        var dictionary = font.GetPdfObject();
        var descriptor = dictionary.GetAsDictionary(PdfName.FontDescriptor);
        var stream = descriptor?.GetAsStream(PdfName.FontFile2);
        Require(font.IsEmbedded() && dictionary.GetAsName(PdfName.BaseFont)?.GetValue() == Name
            && PdfName.TrueType.Equals(dictionary.GetAsName(PdfName.Subtype))
            && PdfName.WinAnsiEncoding.Equals(dictionary.GetAsName(PdfName.Encoding))
            && !dictionary.ContainsKey(PdfName.ToUnicode) && descriptor is not null && stream is not null
            && descriptor?.GetAsName(PdfName.FontName)?.GetValue() == Name
            && !descriptor.ContainsKey(PdfName.FontFile) && !descriptor.ContainsKey(PdfName.FontFile3),
            "E_FONT_EMBEDDED_POLICY", "The bundled font must be a full embedded TrueType WinAnsi resource without remapping.");
        byte[] embedded = stream!.GetBytes();
        Require(embedded.Length == ProgramBytes && Digest(embedded) == ProgramSha256,
            "E_FONT_PROGRAM", "The embedded program differs from the exact bundled font.");
        foreach (var rune in Repertoire.EnumerateRunes())
        {
            var glyph = program.GetGlyph(rune.Value);
            Require(font.ContainsGlyph(rune.Value) && glyph is not null
                && (rune.Value == ' ' || glyph.GetBbox() is { Length: 4 })
                && font.GetWidth(rune.Value) == glyph.GetWidth(),
                "E_FONT_METRICS", "A bundled glyph or advance differs from its pinned program.");
        }
    }

    internal static void SetDefaults(PdfDocument pdf, PdfFormField field, PdfFont font)
    {
        var acro = PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject();
        var resources = acro.GetAsDictionary(PdfName.DR) ?? new PdfDictionary();
        var fonts = resources.GetAsDictionary(PdfName.Font) ?? new PdfDictionary();
        fonts.Put(ResourceName, font.GetPdfObject()); resources.Put(PdfName.Font, fonts);
        acro.Put(PdfName.DR, resources); acro.Put(PdfName.DA, new PdfString(DefaultAppearance));
        // Explicit local resources prevent inherited template defaults from selecting the old font.
        var localFonts = new PdfDictionary(); localFonts.Put(ResourceName, font.GetPdfObject());
        var localResources = new PdfDictionary(); localResources.Put(PdfName.Font, localFonts);
        foreach (var node in new[] { field.GetPdfObject(), field.GetWidgets()[0].GetPdfObject() })
        {
            node.Put(PdfName.DA, new PdfString(DefaultAppearance));
            node.Put(PdfName.DR, localResources);
        }
    }

    /// <summary>Independent of text events, so empty q/Q appearances still require the correct editable font.</summary>
    internal static PdfDictionary VerifyDefaults(PdfDocument pdf, PdfFormField field, NativeFontWitness witness,
        NativeTextAppearance.VerificationContext context)
    {
        Require(witness.Policy == Policy && witness.Name == Name && witness.EmbeddedDigest == ProgramSha256,
            "E_VERIFY_FONT_POLICY", "The Schedule B output-font policy is inconsistent.");
        var acro = PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject();
        PdfDictionary? expected = null;
        foreach (var node in new[] { acro, field.GetPdfObject(), field.GetWidgets()[0].GetPdfObject() })
        {
            Require(node.GetAsString(PdfName.DA)?.ToUnicodeString() == DefaultAppearance,
                "E_VERIFY_FIELD_FONT_DEFAULT", "An editable default appearance differs from the explicit font policy.");
            var dictionary = node.GetAsDictionary(PdfName.DR)?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(ResourceName);
            Require(dictionary is not null, "E_VERIFY_FIELD_FONT_RESOURCE", "An editable default font resource is missing.");
            NativeTextAppearance.VerifyFont(context.Font(dictionary!), witness, context);
            Require(expected is null || ReferenceEquals(expected, dictionary)
                || expected.GetIndirectReference() is { } reference && reference.Equals(dictionary!.GetIndirectReference()),
                "E_VERIFY_FIELD_FONT_RESOURCE", "Editable defaults do not share the same document font resource.");
            expected = dictionary;
        }
        return expected!;
    }

    private sealed class SeedStream : MemoryStream
    {
        private void Check(long count)
        {
            Require(count >= 0 && Position + count <= MaximumSeedBytes,
                "E_FONT_SEED_LIMIT", "The single bundled font seed exceeds its finite byte budget.");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override void SetLength(long value) { Require(value is >= 0 and <= MaximumSeedBytes, "E_FONT_SEED_LIMIT", "The bundled font seed exceeds its byte budget."); base.SetLength(value); }
    }
}
