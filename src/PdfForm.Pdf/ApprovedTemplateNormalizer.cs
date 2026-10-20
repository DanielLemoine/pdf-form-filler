using iText.Forms;
using iText.Kernel.Pdf;
using PdfForm.Core;

namespace PdfForm.Pdf;

/// <summary>Narrow normalization factory for the two immutable approved blanks. Does not fill, flatten, execute or publish.</summary>
public static class ApprovedTemplateNormalizer
{
    /// <summary>Configured clean AcroForms use no normalization exceptions or active-content removal.</summary>
    public static PdfDocument OpenForNormalization(TemplateSnapshot snapshot, Stream output, FormConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!snapshot.MatchesConfiguration(configuration)) throw new TemplateInputException("E_CONFIGURATION_TEMPLATE");
        if (configuration.IsBuiltIn) return OpenForNormalization(snapshot, output);
        var binding = new TemplateBinder().Bind(snapshot, configuration);
        var inspection = binding.Inspection;
        if (!binding.Bound || !inspection.ExactApprovedTemplate || !inspection.InitialStructureSupported || inspection.Xfa.Present
            || inspection.CatalogJavaScriptCount != 0 || inspection.Signatures.ApprovalSignatureCount != 0
            || inspection.Signatures.CertificationPresent || inspection.Signatures.UsageRightsSignatureCount != 0
            || inspection.Signatures.UnknownPermissionEntry)
            throw new TemplateInputException("E_NORMALIZATION_UNAPPROVED");
        var limits = new ConfigurationNormalizationLimits();
        limits.SetMaxSizeOfSingleDecompressedPdfStream(10 * 1024 * 1024);
        limits.SetMaxSizeOfDecompressedPdfStreamsSum(50 * 1024 * 1024);
        limits.SetMaxNumberOfElementsInXrefStructure(100_000);
        var pdf = new PdfDocument(new PdfReader(snapshot.OpenRead(), new ReaderProperties().SetMemoryLimitsAwareHandler(limits)), new PdfWriter(output));
        try
        {
            pdf.SetFlushUnusedObjects(false);
            _ = PdfAcroForm.GetAcroForm(pdf, false) ?? throw new TemplateInputException("E_NORMALIZATION_FORM");
            return pdf;
        }
        catch { pdf.Close(); throw; }
    }

    private sealed class ConfigurationNormalizationLimits : MemoryLimitsAwareHandler
    {
        [Obsolete("Compatibility override for the pinned engine; every decoded stream remains bounded.")]
        public override bool IsMemoryLimitsAwarenessRequiredOnDecompression(PdfArray filters) => true;
    }

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
