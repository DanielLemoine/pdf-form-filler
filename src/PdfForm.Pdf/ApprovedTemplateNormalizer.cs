using iText.Forms;
using iText.Kernel.Pdf;

namespace PdfForm.Pdf;

/// <summary>Narrow normalization factory for the two immutable approved blanks. Does not fill, flatten, execute or publish.</summary>
public static class ApprovedTemplateNormalizer
{
    public static PdfDocument OpenForNormalization(TemplateSnapshot snapshot, Stream output)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(output);
        var inspection = new PdfTemplateInspector().Inspect(snapshot);
        if (!inspection.ExactApprovedTemplate || !inspection.InitialStructureSupported
            || inspection.Xfa.Kind != "staticHybridApproved" || !inspection.CatalogJavaScript.ApprovedRemovalRequired)
            throw new TemplateInputException("E_NORMALIZATION_UNAPPROVED");
        var pdf = new PdfDocument(new PdfReader(snapshot.OpenRead()), new PdfWriter(output));
        try
        {
            // Fresh non-incremental output; unreachable original script/XFA/signature payloads must not be copied.
            pdf.SetFlushUnusedObjects(false);
            var catalog = pdf.GetCatalog().GetPdfObject();
            var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw new TemplateInputException("E_NORMALIZATION_FORM");
            form.RemoveXfaForm(); // Also clears the iText AcroForm's cached XFA representation.
            var names = catalog.GetAsDictionary(PdfName.Names);
            names?.Remove(PdfName.JavaScript); // Preserve every unrelated name-tree entry.
            var permissions = catalog.GetAsDictionary(PdfName.Perms);
            foreach (string key in inspection.Signatures.RemovableUsageRightsKeys) permissions?.Remove(new PdfName(key));
            // Preserve the permissions dictionary, including any unrelated entries; original support guard rejects unknown combinations.
            if (form.GetPdfObject().ContainsKey(PdfName.XFA) || names?.ContainsKey(PdfName.JavaScript) == true)
                throw new TemplateInputException("E_NORMALIZATION_VERIFY");
            return pdf;
        }
        catch { pdf.Close(); throw; }
    }

    /// <summary>Independent structural verification only; never authorizes a normalized output as a new input template.</summary>
    public static bool HasNoActiveContent(TemplateSnapshot candidate)
    {
        var report = new PdfTemplateInspector().Inspect(candidate);
        // A final flattened output intentionally has no form. These two input-binding diagnostics
        // are acceptable only together with independently observed absence of every field/widget.
        bool cleanNoFormShape = !report.HasAcroForm && report.TerminalFieldCount == 0 && report.WidgetCount == 0;
        bool acceptedFindings = report.Findings.All(f => f.Severity != "error"
            || (cleanNoFormShape && (f.Code is "E_NO_ACROFORM" or "E_NO_FIELDS")));
        return report.Pages > 0 && !report.Encrypted && !report.Xfa.Present && report.CatalogJavaScriptCount == 0
            && report.Signatures.ApprovalSignatureCount == 0 && !report.Signatures.CertificationPresent
            && report.Signatures.UsageRightsSignatureCount == 0 && !report.Signatures.UnknownPermissionEntry
            && report.Capabilities.Where(c => c.Capability is "active.action" or "embedded.file").All(c => c.Count == 0)
            && acceptedFindings;
    }
}
