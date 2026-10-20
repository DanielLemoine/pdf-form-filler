using System.Text.Json;
using PdfForm.Core;

namespace PdfForm.Pdf;

/// <summary>Static binding only. Callers must load a reviewed release profile and separately pass data/appearance/fit gates.</summary>
public sealed class TemplateBinder
{
    public BindingResult Bind(TemplateSnapshot snapshot, ProfileDefinition profile) => BindCore(snapshot, profile, null);

    public BindingResult Bind(TemplateSnapshot snapshot, FormConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return BindCore(snapshot, configuration.ParseProfile(), configuration);
    }

    private BindingResult BindCore(TemplateSnapshot snapshot, ProfileDefinition profile, FormConfigurationSnapshot? configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(profile);
        var inspection = configuration is null ? new PdfTemplateInspector().Inspect(snapshot)
            : new PdfTemplateInspector().Inspect(snapshot, configuration);
        var findings = new List<Finding>(inspection.Findings);
        bool external = configuration is { IsBuiltIn: false };
        var approved = external ? null : ApprovedTemplates.Find(snapshot.Sha256);
        void Error(string code, string message, string? field = null) => findings.Add(new(code, "binding", message, Field: field));
        bool identity = profile.Template.Sha256Allowlist.Length > 0
            && profile.Template.Sha256Allowlist.Contains(snapshot.Sha256, StringComparer.Ordinal);
        if (!identity) Error("E_TEMPLATE_HASH", "Exact opened template bytes are absent from the mandatory allowlist.");
        if (external)
        {
            if (!snapshot.MatchesConfiguration(configuration!))
                Error("E_CONFIGURATION_TEMPLATE", "The exact external configuration refers to different immutable template bytes.");
            else if (!inspection.ExactApprovedTemplate && inspection.Findings.All(f => f.Severity != "error"))
                Error("E_CONFIGURATION_POLICY", "The external configuration did not independently pass document inspection.");
        }
        else if (approved is null) Error("E_TEMPLATE_NOT_REVIEWED", "Template bytes are absent from the initial release-owned review registry.");
        if (approved is not null && (profile.Id != approved.Id || profile.Revision != approved.Revision))
            Error("E_TEMPLATE_PROFILE", "The profile identity does not match the reviewed template.");
        if (profile.Template.Title != (external ? inspection.ActualTitle ?? "" : inspection.ActualTitle)) Error("E_TEMPLATE_TITLE", "Template title differs from the reviewed profile.");
        if (profile.Template.PageCount != inspection.Pages) Error("E_TEMPLATE_PAGES", "Template page count differs from the reviewed profile.");
        if (profile.Template.StructureFingerprintV1 != inspection.StructureFingerprintV1)
            Error("E_TEMPLATE_STRUCTURE", "Legacy comparison fingerprint differs; it does not authorize byte identity or placement.");
        if (approved is not null && (approved.Pages != inspection.Pages || approved.Fields != inspection.TerminalFieldCount || approved.Fingerprint != inspection.StructureFingerprintV1))
            Error("E_REVIEWED_STRUCTURE", "The parser result differs from the release-owned reviewed baseline.");
        var capabilities = new HashSet<string>(profile.Capabilities, StringComparer.Ordinal);
        if (profile.Capabilities.Length != capabilities.Count || capabilities.Any(x => x is not ("text.singleWidget" or "checkbox.singleWidget" or "xfa.staticHybrid")))
            Error("E_PROFILE_CAPABILITY", "Profile capabilities contain unsupported or duplicate declarations.");
        if (inspection.Fields.Any(f => f.Type == "text") && !capabilities.Contains("text.singleWidget"))
            Error("E_PROFILE_CAPABILITY", "Profile does not declare the required text capability.");
        if (inspection.Fields.Any(f => f.Type == "checkbox") && !capabilities.Contains("checkbox.singleWidget"))
            Error("E_PROFILE_CAPABILITY", "Profile does not declare the required checkbox capability.");
        if (!external && inspection.Xfa.Present && !capabilities.Contains("xfa.staticHybrid"))
            Error("E_PROFILE_CAPABILITY", "Profile does not declare the required static-XFA capability.");
        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in profile.Keys)
        {
            bool button = key.Kind is "checkbox" or "oneOf";
            foreach (var target in key.EnumerateTargets())
            {
                if (!owned.Add(target)) Error("E_TARGET_OVERLAP", "A terminal field has multiple assignment owners.");
                if (!inspection.NamedFields.TryGetValue(target, out var field))
                { Error("E_TARGET_MISSING", "A mapped target is absent from the canonical terminal-field inventory."); continue; }
                string safeId = field.Name ?? field.Id;
                if (field.Type != (button ? "checkbox" : "text")) Error("E_TARGET_TYPE", "A target has the wrong field type.", safeId);
                if (field.Widgets.Count != 1) Error("E_TARGET_WIDGETS", "A mapped target must have exactly one canonical page widget.", safeId);
                if (field.Effective.Flags.Required && !key.Required) Error("E_TARGET_REQUIRED", "A required PDF field needs an explicitly required profile key.", safeId);
                if (field.Effective.Flags.ReadOnly || field.Effective.Flags.NoExport) Error("E_TARGET_FLAGS", "A mapped field has unsupported effective permission flags.", safeId);
                if (!button)
                {
                    if (field.Effective.Font.Size is not > 0 || !field.Effective.Font.ResourcePresent)
                        Error("E_TARGET_FONT", "A target lacks a fixed-size effective font resource.", safeId);
                    if (field.Effective.Alignment == "invalid") Error("E_TARGET_ALIGNMENT", "Effective field alignment is unsupported.", safeId);
                    if (key.Kind == "text" && key.MaxLength is { } max && field.Effective.MaxLength is { } cap && max > cap)
                        Error("E_TARGET_LENGTH", "Profile text capacity exceeds the target capacity.", safeId);
                    if (key.Kind == "date" && field.Effective.MaxLength is { } dateCap && (key.Format == "MMddyyyy" ? 8 : 10) > dateCap)
                        Error("E_TARGET_LENGTH", "Date formatting exceeds the target capacity.", safeId);
                }
            }
            if (key.Kind == "checkbox" && key.Target is not null) CheckOn(key.Target, key.On);
            if (key.Kind == "oneOf" && key.Options is not null)
                foreach (var assignment in key.Options.Values.SelectMany(x => x)) CheckOn(assignment.Target, assignment.On);
            if (key.Kind == "split" && key.Parts is not null && key.Targets.ValueKind == JsonValueKind.Array)
            {
                var groups = key.Targets.EnumerateArray().ToArray();
                IEnumerable<JsonElement[]> copies = groups.All(x => x.ValueKind == JsonValueKind.String)
                    ? new[] { groups } : groups.Where(x => x.ValueKind == JsonValueKind.Array).Select(x => x.EnumerateArray().ToArray());
                foreach (var copy in copies)
                    for (int i = 0; i < copy.Length && i < key.Parts.Length; ++i)
                        if (copy[i].ValueKind == JsonValueKind.String && inspection.NamedFields.TryGetValue(copy[i].GetString()!, out var field)
                            && field.Effective.MaxLength is { } cap && key.Parts[i] > cap)
                            Error("E_SPLIT_CAPACITY", "A split component exceeds its target capacity.", field.Name ?? field.Id);
            }
        }
        foreach (var pair in inspection.NamedFields)
            if (!owned.Contains(pair.Key)) Error("E_TARGET_UNMAPPED", "Every initial-template terminal field requires exactly one owning key.", pair.Value.Name ?? pair.Value.Id);
        if (profile.Unmapped.Length > 0) Error("E_UNMAPPED_UNSUPPORTED", "Initial profiles do not permit unmapped fields.");
        foreach (var layout in profile.Options.DateCellLayouts ?? [])
        {
            if (layout.TemplateSha256 != snapshot.Sha256 || !identity)
                Error("E_DATE_LAYOUT_HASH", "Date-cell geometry is not bound to these exact approved bytes.");
            if (!inspection.NamedFields.TryGetValue(layout.Target, out var field) || field.Widgets.Count != 1)
            { Error("E_DATE_LAYOUT_TARGET", "Date-cell geometry requires a single mapped widget."); continue; }
            var box = field.Widgets[0].Rectangle;
            if (box.Count != 4 || layout.Cells.Length != 8 || layout.Cells.Any(c => c.Length != 4 || c.Any(v => !double.IsFinite(v))
                || c[0] >= c[2] || c[1] >= c[3] || c[0] < box[0] || c[1] < box[1] || c[2] > box[2] || c[3] > box[3]))
                Error("E_DATE_LAYOUT_GEOMETRY", "Date-cell geometry falls outside the approved widget rectangle.", field.Name ?? field.Id);
        }
        void CheckOn(string target, string? on)
        {
            if (!inspection.NamedFields.TryGetValue(target, out var field)) return;
            if (on is null ? field.OnStateCount != 1 : !field.AllowedOnStates.Contains(on, StringComparer.Ordinal))
                Error("E_CHECKBOX_ON_STATE", "A checkbox assignment does not resolve to one allowed export state.", field.Name ?? field.Id);
        }
        return new("pdfform.binding/2", snapshot.Sha256, findings.All(f => f.Severity != "error"),
            ["exactTemplateBytes", external ? "configurationPolicyAndBlankness" : "reviewRegistry", "canonicalFieldsAndWidgets", "effectiveAttributes", "profileCoverage", "targetTypes", "staticCapacity", "capabilityClassification"],
            ["dataValidation", "glyphCoverage", "textFit", "appearanceGeneration", "viewerAcceptance", "physicalPrint", "filingReadiness"], findings.AsReadOnly(), inspection);
    }
}
