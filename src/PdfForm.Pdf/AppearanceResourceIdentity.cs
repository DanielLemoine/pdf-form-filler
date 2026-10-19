using System.Text;
using iText.Kernel.Pdf;
using static PdfForm.Pdf.VerifiedFormRenderer;

namespace PdfForm.Pdf;

/// <summary>Reference-number-independent identity for the bounded reviewed checkbox appearance graph.</summary>
internal static class AppearanceResourceIdentity
{
    internal static string Compute(PdfStream appearance)
    {
        var text = new StringBuilder();
        var ancestors = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        int nodes = 0;
        void Append(string value)
        {
            Require(value.Length <= 20 * 1024 * 1024 - text.Length,
                "E_VERIFY_AP_RESOURCE_LIMIT", "Appearance resource identity exceeds its bounded buffer.");
            text.Append(value);
        }
        void Visit(PdfObject? value, int depth)
        {
            Require(depth <= 32 && ++nodes <= 20_000 && text.Length <= 20 * 1024 * 1024,
                "E_VERIFY_AP_RESOURCE_LIMIT", "Appearance resources exceed the bounded identity check.");
            if (value is null) { Append("null;"); return; }
            Require(ancestors.Add(value), "E_VERIFY_AP_RESOURCE_CYCLE", "Cyclic appearance resources are unsupported.");
            if (value is PdfDictionary dictionary)
            {
                Append(value is PdfStream ? "stream{" : "dict{");
                foreach (var key in dictionary.KeySet().OrderBy(k => k.GetValue(), StringComparer.Ordinal))
                {
                    if (value is PdfStream && (key.Equals(PdfName.Length) || key.Equals(PdfName.Filter) || key.Equals(PdfName.DecodeParms))) continue;
                    Append(key.GetValue().Length + ":" + key.GetValue() + "=");
                    Visit(dictionary.Get(key, true), depth + 1);
                }
                if (value is PdfStream stream) Append("decoded:" + Digest(stream.GetBytes()));
                Append("}");
            }
            else if (value is PdfArray array)
            {
                Append("[");
                for (int i = 0; i < array.Size(); i++) Visit(array.Get(i, true), depth + 1);
                Append("]");
            }
            else
            {
                string raw = value.ToString() ?? "";
                Append(value.GetObjectType() + ":" + raw.Length + ":"); Append(raw); Append(";");
            }
            ancestors.Remove(value);
        }
        Visit(appearance, 0);
        return Digest(Encoding.UTF8.GetBytes(text.ToString()));
    }
}
