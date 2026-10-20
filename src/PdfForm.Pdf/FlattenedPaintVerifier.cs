using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using static PdfForm.Pdf.VerifiedFormRenderer;

namespace PdfForm.Pdf;

internal sealed record FlattenPaintWitness(string Field, string StreamDigest);

/// <summary>Checks each engine-authored flatten invocation against the already verified editable normal appearance.</summary>
internal static class FlattenedPaintVerifier
{
    internal static RenderManifest Capture(byte[] editable, RenderManifest manifest)
    {
        using var pdf = new PdfDocument(Reader(editable));
        var fields = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false).GetAllFormFields();
        var witnesses = new List<FlattenPaintWitness>();
        foreach (var pair in manifest.Targets)
        {
            var ap = fields[pair.Key].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)!;
            var stream = pair.Value.Assignment.Kind == "checkbox"
                ? ap.GetAsDictionary(PdfName.N)!.GetAsStream(new PdfName(pair.Value.Assignment.Checked ? pair.Value.OnState! : "Off"))
                : ap.GetAsStream(PdfName.N);
            Require(stream is not null, "E_VERIFY_FLATTEN_AP", "A verified appearance is missing before flattening.");
            witnesses.Add(new(pair.Key, Digest(stream!.GetBytes())));
        }
        return manifest with { FlattenPaint = witnesses.ToArray() };
    }

    internal static void Verify(PdfDocument pdf, RenderManifest manifest)
    {
        Require(manifest.FlattenPaint is not null && manifest.FlattenPaint.Length == manifest.Targets.Count,
            "E_VERIFY_FLATTEN_WITNESS", "Flatten verification requires every verified editable appearance.");
        var fontVerification = new NativeTextAppearance.VerificationContext();
        var remaining = manifest.FlattenPaint!.ToDictionary(x => x.Field, x => x.StreamDigest, StringComparer.Ordinal);
        for (int pageNumber = 1; pageNumber <= pdf.GetNumberOfPages(); pageNumber++)
        {
            var page = pdf.GetPage(pageNumber);
            var original = manifest.Pages[pageNumber - 1].ContentDigests;
            int originalIndex = 0;
            using var added = new MemoryStream();
            for (int i = 0; i < page.GetContentStreamCount(); i++)
            {
                byte[] bytes = page.GetContentStream(i).GetBytes();
                if (originalIndex < original.Length && Digest(bytes) == original[originalIndex]) originalIndex++;
                else { added.Write(bytes); added.WriteByte((byte)'\n'); }
            }
            Require(originalIndex == original.Length, "E_VERIFY_PAGE_CONTENT", "Original static streams are missing.");
            var stack = new Stack<Matrix>();
            var ctm = new Matrix();
            bool marked = false;
            var mcids = new HashSet<int>();
            foreach (var operands in GeneratedContentPolicy.Commands(added.ToArray()))
            {
                string op = operands[^1].ToString() ?? "";
                switch (op)
                {
                    case "q":
                        Require(operands.Count == 1 && stack.Count < 16, "E_VERIFY_FLATTEN_STATE", "Flatten save-state is unsupported.");
                        stack.Push(ctm); break;
                    case "Q":
                        Require(operands.Count == 1 && stack.Count > 0, "E_VERIFY_FLATTEN_STATE", "Flatten restore-state is unbalanced.");
                        ctm = stack.Pop(); break;
                    case "cm":
                        Require(operands.Count == 7 && operands.Take(6).All(x => x is PdfNumber), "E_VERIFY_FLATTEN_MATRIX", "Flatten placement is malformed.");
                        float[] m = operands.Take(6).Cast<PdfNumber>().Select(x => x.FloatValue()).ToArray();
                        Require(m.All(float.IsFinite), "E_VERIFY_FLATTEN_MATRIX", "Flatten placement is nonfinite.");
                        ctm = new Matrix(m[0], m[1], m[2], m[3], m[4], m[5]).Multiply(ctm); break;
                    case "BDC":
                        // iText preserves tagged-form accessibility with /Form << /MCID integer >> BDC.
                        // No optional-content tag, named property resource or additional dictionary entry is admitted.
                        Require(!marked && operands.Count == 3 && PdfName.Form.Equals(operands[0])
                            && operands[1] is PdfDictionary and not PdfStream,
                            "E_VERIFY_FLATTEN_MARKED_CONTENT", "Flatten marked-content metadata is outside the supported shape.");
                        var properties = (PdfDictionary)operands[1];
                        var mcid = properties.GetAsNumber(PdfName.MCID);
                        Require(properties.Size() == 1 && properties.GetIndirectReference() is null && mcid is not null
                            && mcid.DoubleValue() == mcid.IntValue() && mcid.IntValue() >= 0 && mcids.Add(mcid.IntValue()),
                            "E_VERIFY_FLATTEN_MARKED_CONTENT", "Flatten marked-content metadata contains unqualified properties.");
                        marked = true; break;
                    case "EMC":
                        Require(marked && operands.Count == 1, "E_VERIFY_FLATTEN_MARKED_CONTENT", "Flatten marked-content metadata is unbalanced.");
                        marked = false; break;
                    case "Do":
                        Require(operands.Count == 2 && operands[0] is PdfName, "E_VERIFY_FLATTEN_PAINT", "Flatten appearance invocation is malformed.");
                        var stream = page.GetResources().GetPdfObject().GetAsDictionary(PdfName.XObject)?.GetAsStream((PdfName)operands[0]);
                        Require(stream is not null && PdfName.Form.Equals(stream.GetAsName(PdfName.Subtype)), "E_VERIFY_FLATTEN_PAINT", "A flattened appearance resource is absent or unsupported.");
                        string digest = Digest(stream!.GetBytes());
                        var matches = remaining.Where(p => p.Value == digest && manifest.Targets[p.Key].Widget.Page == pageNumber
                            && PlacementMatches(ctm, stream, manifest.Targets[p.Key])).Select(x => x.Key).ToArray();
                        Require(matches.Length == 1, "E_VERIFY_FLATTEN_PLACEMENT", "A flattened appearance does not uniquely match its expected page and rectangle.");
                        var target = manifest.Targets[matches[0]];
                        VerifyAppearanceBox(stream, target.Widget);
                        if (target.Assignment.Kind != "checkbox") NativeTextAppearance.Verify(stream, target, fontVerification);
                        else
                        {
                            VectorCheckboxAppearance.Verify(stream, target, target.Assignment.Checked);
                            if (target.Assignment.Checked) Require(AppearanceResourceIdentity.Compute(stream) == target.OnResourceIdentity,
                                "E_VERIFY_FLATTEN_CHECKBOX_RESOURCE", "A flattened checkbox appearance changed its authored resources.");
                        }
                        remaining.Remove(matches[0]);
                        break;
                    default:
                        throw new VerifiedFillException("E_VERIFY_FLATTEN_OPERATOR", "The flattened additions contain an unsupported painting or graphics-state operator.");
                }
            }
            Require(stack.Count == 0 && !marked, "E_VERIFY_FLATTEN_STATE", "Flatten state is not balanced.");
        }
        Require(remaining.Count == 0, "E_VERIFY_FLATTEN_PAINT_MISSING", "One or more verified field appearances were not painted into the final pages.");
    }

    private static bool PlacementMatches(Matrix m, PdfStream appearance, TargetWitness target)
    {
        var box = appearance.GetAsArray(PdfName.BBox)?.ToDoubleArray();
        if (box is not { Length: 4 } || box[2] <= box[0] || box[3] <= box[1]) return false;
        var r = target.Widget.Rectangle;
        return Math.Abs(m.Get(Matrix.I11) - (r[2] - r[0]) / (box[2] - box[0])) < .001
            && Math.Abs(m.Get(Matrix.I12)) < .00001 && Math.Abs(m.Get(Matrix.I21)) < .00001
            && Math.Abs(m.Get(Matrix.I22) - (r[3] - r[1]) / (box[3] - box[1])) < .001
            && Math.Abs(m.Get(Matrix.I31) - r[0]) < .02 && Math.Abs(m.Get(Matrix.I32) - r[1]) < .02;
    }
}
