using iText.IO.Source;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser.Util;
using static PdfForm.Pdf.VerifiedFormRenderer;

namespace PdfForm.Pdf;

/// <summary>Accepts only the narrow operator vocabulary authored by this renderer; not an arbitrary PDF visual verifier.</summary>
internal static class GeneratedContentPolicy
{
    internal static IEnumerable<IList<PdfObject>> Commands(byte[] bytes)
    {
        Require(bytes.Length <= 1024 * 1024, "E_VERIFY_CONTENT_LIMIT", "Generated appearance content exceeds its bounded size.");
        using var tokenizer = new PdfTokenizer(new RandomAccessFileOrArray(new RandomAccessSourceFactory().CreateSource(bytes)));
        var parser = new PdfCanvasParser(tokenizer);
        int count = 0;
        while (true)
        {
            IList<PdfObject> operands = new List<PdfObject>();
            parser.Parse(operands);
            if (operands.Count == 0) yield break;
            Require(++count <= 20_000, "E_VERIFY_CONTENT_LIMIT", "Generated appearance operator count exceeds its bound.");
            yield return operands;
        }
    }

    /// <summary>Returns the conservative intersection of all rectangular clips. Narrowing only can never hide an unchecked clip.</summary>
    internal static double[] CheckTextAppearance(PdfStream appearance)
    {
        var box = appearance.GetAsArray(PdfName.BBox)!.ToDoubleArray();
        double[] clip = box.ToArray();
        double[]? rectangle = null;
        bool clipping = false;
        int saves = 0, texts = 0;
        foreach (var operands in Commands(appearance.GetBytes()))
        {
            string op = operands[^1].ToString() ?? "";
            switch (op)
            {
                case "q": Require(operands.Count == 1 && ++saves <= 16, "E_VERIFY_AP_STATE", "Generated appearance state nesting is unsupported."); break;
                case "Q": Require(operands.Count == 1 && --saves >= 0, "E_VERIFY_AP_STATE", "Generated appearance state is unbalanced."); break;
                case "BT": Require(++texts == 1, "E_VERIFY_AP_STATE", "Generated text state is unbalanced."); break;
                case "ET": Require(--texts == 0, "E_VERIFY_AP_STATE", "Generated text state is unbalanced."); break;
                case "BMC": case "EMC": break;
                case "re":
                    Require(rectangle is null && operands.Count == 5 && operands.Take(4).All(x => x is PdfNumber), "E_VERIFY_AP_CLIP", "The generated rectangle is invalid.");
                    var values = operands.Take(4).Cast<PdfNumber>().Select(x => x.DoubleValue()).ToArray();
                    Require(values.All(double.IsFinite) && values[2] > 0 && values[3] > 0,
                        "E_VERIFY_AP_CLIP", "An appearance rectangle has zero or invalid area.");
                    rectangle = [values[0], values[1], values[0] + values[2], values[1] + values[3]];
                    break;
                case "W": case "W*":
                    Require(rectangle is not null && !clipping, "E_VERIFY_AP_CLIP", "Only a single explicit rectangular clipping path is supported.");
                    clipping = true; break;
                case "n":
                    if (clipping)
                    {
                        clip = [Math.Max(clip[0], rectangle![0]), Math.Max(clip[1], rectangle[1]), Math.Min(clip[2], rectangle[2]), Math.Min(clip[3], rectangle[3])];
                        Require(clip[0] < clip[2] && clip[1] < clip[3], "E_VERIFY_AP_CLIP", "The normal appearance has an empty clip.");
                    }
                    rectangle = null; clipping = false; break;
                case "Tm":
                    Require(operands.Count == 7 && operands.Take(6).All(x => x is PdfNumber), "E_VERIFY_AP_TRANSFORM", "A text matrix is malformed.");
                    double[] m = operands.Take(6).Cast<PdfNumber>().Select(x => x.DoubleValue()).ToArray();
                    Require(m.All(double.IsFinite) && Math.Abs(m[0] - 1) < .00001 && Math.Abs(m[1]) < .00001
                        && Math.Abs(m[2]) < .00001 && Math.Abs(m[3] - 1) < .00001,
                        "E_VERIFY_AP_TRANSFORM", "Generated text cannot be scaled, rotated or sheared.");
                    break;
                case "Tf": case "Td": case "Tj": case "TJ": case "Tc": case "Tw": case "Tz": case "TL": case "Tr": case "Ts":
                case "g": case "rg": case "k":
                    Require(texts == 1 || op is "g" or "rg" or "k", "E_VERIFY_AP_STATE", "Generated text operators occur outside a text object.");
                    break;
                default:
                    // In particular: no cm, gs, Do, image, path paint, optional-content properties or unknown operators.
                    throw new VerifiedFillException("E_VERIFY_AP_OPERATOR", "The appearance contains an unqualified painting or graphics-state operator.");
            }
        }
        Require(saves == 0 && texts == 0 && !clipping && rectangle is null, "E_VERIFY_AP_STATE", "Generated appearance state is incomplete.");
        return clip;
    }
}
