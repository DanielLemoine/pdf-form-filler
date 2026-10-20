using System.Security.Cryptography;
using System.Text;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;
using PdfForm.Core;

namespace PdfForm.Pdf;

internal sealed record PageWitness(float[][] Boxes, int Rotation, float UserUnit, string[] ContentDigests, int OtherAnnotations);
internal sealed record TargetWitness(FieldAssignment Assignment, WidgetInspection Widget, int Flags, int? MaxLength,
    int Alignment, string? OnState, string? OnAppearanceDigest, string? OnResourceIdentity, NativeFontWitness? Font, double[][]? DateCells, CheckboxMark? CheckboxMark = null);
internal sealed record RenderManifest(string TemplateSha256, string? Title, PageWitness[] Pages,
    IReadOnlyDictionary<string, TargetWitness> Targets, FlattenPaintWitness[]? FlattenPaint = null);

/// <summary>Exact-template engine. Internal admission is for synthetic friend tests; public admission is in VerifiedFillPipeline.</summary>
internal static class VerifiedFormRenderer
{
    private static readonly PdfName Off = new("Off");
    private static readonly PdfName[] Boxes = [PdfName.MediaBox, PdfName.CropBox, PdfName.BleedBox, PdfName.TrimBox, PdfName.ArtBox];

    internal static VerifiedFillCandidate Render(TemplateSnapshot template, ProfileDefinition profile, BindingResult bound,
        IReadOnlyList<FieldAssignment> assignments, bool flatten) => RenderCore(template, profile, bound, assignments, flatten, null, null);

    internal static VerifiedFillCandidate Render(TemplateSnapshot template, FormConfigurationSnapshot configuration, BindingResult bound,
        IReadOnlyList<FieldAssignment> assignments, bool flatten)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Require(template.MatchesConfiguration(configuration), "E_TEMPLATE_PROFILE", "Configuration is bound to different template bytes.", 3);
        return RenderCore(template, configuration.ParseProfile(), bound, assignments, flatten, configuration,
            configuration.IsBuiltIn ? null : configuration.ParsePolicy());
    }

    private static VerifiedFillCandidate RenderCore(TemplateSnapshot template, ProfileDefinition profile, BindingResult bound,
        IReadOnlyList<FieldAssignment> assignments, bool flatten, FormConfigurationSnapshot? configuration, FormPolicyDefinition? policy)
    {
        Require(bound.Bound && bound.TemplateSha256 == template.Sha256
            && (configuration is null ? bound.Inspection.ExactApprovedTemplate : template.MatchesConfiguration(configuration)),
            "E_FILL_BINDING", "The renderer requires exact bound template bytes.", 3);
        Require(assignments.Count == bound.Inspection.TerminalFieldCount && assignments.Select(a => a.Field).Distinct(StringComparer.Ordinal).Count() == assignments.Count,
            "E_FILL_ASSIGNMENTS", "The assignment manifest must cover every terminal field exactly once.");
        if (policy is not null) CheckPolicyCoverage(policy, bound);
        RenderManifest manifest;
        byte[] editable;
        using (var output = new BoundedPdfMemoryStream())
        {
            using (var pdf = configuration is null ? ApprovedTemplateNormalizer.OpenForNormalization(template, output)
                : ApprovedTemplateNormalizer.OpenForNormalization(template, output, configuration))
            {
                var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw Error("E_FILL_FORM", "The bound form is missing.");
                var fields = TerminalFields(form);
                Require(fields.Count == assignments.Count && assignments.All(a => fields.ContainsKey(a.Field)),
                    "E_FILL_ASSIGNMENTS", "The assignment manifest and canonical field inventory disagree.");
                var expected = new Dictionary<string, TargetWitness>(StringComparer.Ordinal);
                var fonts = new Dictionary<string, PdfFont>(StringComparer.Ordinal);
                var replacement = new ScheduleBEmbeddedFont.DocumentFont(pdf);
                foreach (var assignment in assignments)
                {
                    var target = PrepareTarget(template, profile, bound, pdf, fields, assignment, policy, replacement, out var font);
                    if (font is not null) fonts.Add(assignment.Field, font);
                    expected.Add(assignment.Field, target);
                }
                manifest = new(template.Sha256, pdf.GetDocumentInfo().GetTitle(), CapturePages(pdf), expected);
                ReplaceAll(pdf, manifest, fonts);
            }
            editable = output.ToArray();
        }
        // No flatten operation can run until every closed editable assignment/default/state/AP has passed readback.
        VerifyEditable(editable, manifest);
        if (!flatten) return new(editable, manifest, false);
        manifest = FlattenedPaintVerifier.Capture(editable, manifest);
        byte[] final;
        using (var output = new BoundedPdfMemoryStream())
        {
            using (var pdf = new PdfDocument(Reader(editable), new PdfWriter(output)))
            {
                pdf.SetFlushUnusedObjects(false);
                var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw Error("E_FLATTEN_FORM", "The verified editable form is missing.");
                form.SetGenerateAppearance(false); // Use exactly the already verified normal appearances.
                form.FlattenFields();
            }
            final = output.ToArray();
        }
        VerifyFlattened(final, manifest);
        return new(final, manifest, true);
    }

    private static void CheckPolicyCoverage(FormPolicyDefinition policy, BindingResult bound)
    {
        var targets = policy.TextFields.Select(value => value.Target).Concat(policy.CheckboxFields.Select(value => value.Target)).ToArray();
        Require(targets.Length == bound.Inspection.TerminalFieldCount
            && targets.Distinct(StringComparer.Ordinal).Count() == targets.Length
            && targets.All(bound.Inspection.NamedFields.ContainsKey),
            "E_FILL_POLICY_COVERAGE", "Configuration must cover every canonical target exactly once.", 3);
    }

    internal static void CheckConfiguredFit(TemplateSnapshot template, FormConfigurationSnapshot configuration,
        BindingResult bound, IReadOnlyList<FieldAssignment> assignments)
    {
        Require(template.MatchesConfiguration(configuration) && bound.Bound && bound.TemplateSha256 == template.Sha256,
            "E_FILL_BINDING", "Fit checks require the exact configured template and binding.", 3);
        var profile = configuration.ParseProfile();
        var policy = configuration.IsBuiltIn ? null : configuration.ParsePolicy();
        if (policy is not null) CheckPolicyCoverage(policy, bound);
        Require(assignments.Select(value => value.Field).Distinct(StringComparer.Ordinal).Count() == assignments.Count
            && assignments.All(value => bound.Inspection.NamedFields.ContainsKey(value.Field)),
            "E_FILL_ASSIGNMENTS", "Fit assignments must be distinct configured targets.", 3);
        using var output = new BoundedPdfMemoryStream();
        using var pdf = ApprovedTemplateNormalizer.OpenForNormalization(template, output, configuration);
        var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw Error("E_FILL_FORM", "The configured form is missing.");
        var fields = TerminalFields(form);
        var replacement = new ScheduleBEmbeddedFont.DocumentFont(pdf);
        foreach (var assignment in assignments)
        {
            var target = PrepareTarget(template, profile, bound, pdf, fields, assignment, policy, replacement, out var font);
            // Paint and inspect only this key's mapped targets in bounded memory.
            // This proves physical fit without a completed-form or publication claim.
            var field = fields[assignment.Field];
            if (font is not null)
            {
                ReplaceText(pdf, field, target, font);
                var context = new NativeTextAppearance.VerificationContext();
                if (ScheduleBEmbeddedFont.IsReplacementPolicy(target.Font!.Policy))
                    ScheduleBEmbeddedFont.VerifyDefaults(pdf, field, target.Font, context);
                else if (target.Font.Policy == NativeTextAppearance.PreservedFontPolicy)
                    NativeTextAppearance.VerifyPreservedDefaults(field, target.Font, context);
                var appearance = field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)?.GetAsStream(PdfName.N);
                Require(appearance is not null, "E_VERIFY_TEXT_AP", "The target has no normal appearance stream.");
                NativeTextAppearance.Verify(appearance!, target, context);
            }
            else
            {
                SetCheckbox(pdf, field, target);
                VerifyCheckbox(field, target);
            }
        }
    }

    private static TargetWitness PrepareTarget(TemplateSnapshot template, ProfileDefinition profile, BindingResult bound,
        PdfDocument pdf, IReadOnlyDictionary<string, PdfFormField> fields, FieldAssignment assignment,
        FormPolicyDefinition? policy, ScheduleBEmbeddedFont.DocumentFont replacement, out PdfFont? font)
    {
        font = null;
        try
        {
            Require(assignment.Kind is "text" or "checkbox", "E_FILL_ASSIGNMENTS", "The assignment type is unsupported.", 3);
            var original = bound.Inspection.NamedFields[assignment.Field];
            if (policy is not null)
                Require(original.Type == assignment.Kind, "E_FILL_FIELD_TYPE", "The assignment differs from the configured source field type.", 3);
            Require(original.Widgets.Count == 1 && fields.ContainsKey(assignment.Field),
                "E_FILL_WIDGETS", "Exactly one canonical widget is required per assignment.", 3);
            var widget = original.Widgets.Single();
            var field = fields[assignment.Field];
            Require(widget.Page is > 0 && widget.Rotation == 0 && widget.GeometryValid,
                "E_FILL_GEOMETRY", "Widget geometry or rotation is unsupported.", 3);
            Require(widget.Effective.Flags == original.Effective.Flags && widget.Effective.MaxLength == original.Effective.MaxLength
                && widget.Effective.Alignment == original.Effective.Alignment && widget.Effective.Font.Name == original.Effective.Font.Name
                && widget.Effective.Font.Size == original.Effective.Font.Size && widget.Effective.Font.Subtype == original.Effective.Font.Subtype
                && widget.Effective.Font.ResourcePresent == original.Effective.Font.ResourcePresent && widget.Effective.Font.Embedded == original.Effective.Font.Embedded,
                "E_FILL_WIDGET_ATTRIBUTES", "Widget-local attributes differ from canonical field attributes.", 3);
            Require(field.GetWidgets().Count == 1, "E_FILL_WIDGETS", "Exactly one widget is required per field.", 3);
            var form = PdfAcroForm.GetAcroForm(pdf, false)!;
            int flags = Integer(Inherited(field.GetPdfObject(), PdfName.Ff, form.GetPdfObject())) ?? 0;
            int alignment = Integer(Inherited(field.GetPdfObject(), PdfName.Q, form.GetPdfObject())) ?? 0;
            string? on = null, onHash = null, onResources = null;
            CheckboxMark? checkboxMark = null;
            NativeFontWitness? fontWitness = null;
            double[][]? dateCells = null;
            if (assignment.Kind == "checkbox")
            {
                on = assignment.On ?? original.AllowedOnStates.Single();
                Require(original.AllowedOnStates.Contains(on, StringComparer.Ordinal), "E_FILL_ON_STATE", "Checkbox on-state is not configured.", 3);
                var normal = field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)?.GetAsDictionary(PdfName.N);
                Require(normal?.GetAsStream(new PdfName(on)) is not null, "E_FILL_ON_AP", "The configured checkbox on appearance is missing.", 3);
                if (policy is null) checkboxMark = VectorCheckboxAppearance.Resolve(template.Sha256, assignment.Field, on, widget);
                else
                {
                    var configured = policy.CheckboxFields.SingleOrDefault(value => value.Target == assignment.Field)
                        ?? throw Error("E_FILL_POLICY_TARGET", "The configured checkbox policy is missing.", 3);
                    var mark = VectorCheckboxAppearance.ResolveConfiguredMark(configured.Mark);
                    checkboxMark = VectorCheckboxAppearance.ResolveBound(template.Sha256, template.Sha256, assignment.Field,
                        configured.Target, on, configured.OnState, configured.Page, configured.Rectangle, widget, mark);
                }
                var authored = VectorCheckboxAppearance.Create(widget, checkboxMark.Value, true);
                onHash = Digest(authored.GetBytes());
                onResources = AppearanceResourceIdentity.Compute(authored);
            }
            else
            {
                if (policy is null)
                {
                    font = NativeTextAppearance.ResolveFont(template.Sha256, pdf, field, widget.Effective.Font, out fontWitness, replacement);
                    NativeTextAppearance.CheckFit(field, widget, assignment.Text, font, fontWitness);
                    var key = profile.Keys.Single(value => value.Key == assignment.Key);
                    dateCells = NativeTextAppearance.DateCells(template.Sha256, key, assignment, widget, profile.Options.DateCellLayouts);
                }
                else
                {
                    var configured = policy.TextFields.SingleOrDefault(value => value.Target == assignment.Field)
                        ?? throw Error("E_FILL_POLICY_TARGET", "The configured text policy is missing.", 3);
                    Require(configured.Page == widget.Page && widget.Rectangle.SequenceEqual(configured.Rectangle),
                        "E_FILL_POLICY_TARGET", "Text policy must match its exact target, page and rectangle.", 3);
                    Require(widget.Effective.Alignment is "left" or "center" or "right"
                        && !widget.Effective.Flags.Multiline && !widget.Effective.Flags.Password && !widget.Effective.Flags.RichText,
                        "E_TEXT_LAYOUT", "Configured text requires a supported single-line source field, including when blank.", 3);
                    Require(!widget.Effective.Flags.Comb || widget.Effective.MaxLength is > 0,
                        "E_TEXT_COMB_CAPACITY", "A configured comb source requires explicit capacity, including when blank.", 3);
                    font = NativeTextAppearance.ResolveConfiguredFont(pdf, field, widget, configured, assignment.Text, out fontWitness, replacement);
                    NativeTextAppearance.CheckFit(field, widget, assignment.Text, font, fontWitness);
                    var key = profile.Keys.Single(value => value.Key == assignment.Key);
                    dateCells = NativeTextAppearance.DateCells(template.Sha256, configured.Layout, key, assignment, widget, profile.Options.DateCellLayouts);
                }
            }
            return new(assignment, widget, flags, original.Effective.MaxLength, alignment,
                on, onHash, onResources, fontWitness, dateCells, checkboxMark);
        }
        catch (VerifiedFillException error)
        {
            if (policy is not null) error.ApprovedField ??= VerifiedFillPipeline.DiagnosticFieldId(assignment.Field);
            throw;
        }
    }

    /// <summary>Complete replacement shared with controlled synthetic transition tests; never a public template-admission path.</summary>
    internal static void ReplaceAll(PdfDocument pdf, RenderManifest manifest, IReadOnlyDictionary<string, PdfFont> fonts)
    {
        var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw Error("E_FILL_FORM", "The bound form is missing.");
        var fields = TerminalFields(form);
        Require(fields.Count == manifest.Targets.Count && manifest.Targets.Keys.All(fields.ContainsKey),
            "E_FILL_ASSIGNMENTS", "Replacement requires every canonical field exactly once.");
        form.GetPdfObject().Put(PdfName.NeedAppearances, PdfBoolean.FALSE);
        ClearAncestorDefaults(fields.Values);
        foreach (var pair in manifest.Targets)
        {
            var field = fields[pair.Key];
            var target = pair.Value;
            if (target.Assignment.Kind == "checkbox") SetCheckbox(pdf, field, target);
            else ReplaceText(pdf, field, target, fonts[pair.Key]);
        }
    }
    private static void ReplaceText(PdfDocument pdf, PdfFormField field, TargetWitness target, PdfFont font)
    {
        CheckTargetForReplacement(field, target, font);
        field.SetValue(target.Assignment.Text, font, target.Font!.Size);
        if (ScheduleBEmbeddedFont.IsReplacementPolicy(target.Font.Policy))
            ScheduleBEmbeddedFont.SetDefaults(pdf, field, font, target.Font.Size);
        else if (target.Font.Policy == NativeTextAppearance.PreservedFontPolicy)
            NativeTextAppearance.SetPreservedDefaults(field, font, target.Font);
        ClearResetData(field, false);
        if (target.Assignment.Text.Length == 0) NativeTextAppearance.SetEmptyAppearance(pdf, field);
        else if (target.DateCells is not null)
            NativeTextAppearance.SetDateAppearance(pdf, field, target, font);
        RemoveAlternateAppearances(field);
    }
    private static void CheckTargetForReplacement(PdfFormField field, TargetWitness target, PdfFont font)
    {
        NativeTextAppearance.CheckFit(field, target.Widget, target.Assignment.Text, font, target.Font!);
    }

    internal static void VerifyEditable(byte[] bytes, RenderManifest manifest)
    {
        Require(ApprovedTemplateNormalizer.HasNoActiveContent(TemplateSnapshot.FromBytes(bytes)),
            "E_VERIFY_ACTIVE_CONTENT", "The candidate contains unsupported active or signed content.");
        using var pdf = new PdfDocument(Reader(bytes));
        VerifyPages(pdf, manifest, false);
        var form = PdfAcroForm.GetAcroForm(pdf, false) ?? throw Error("E_VERIFY_FORM", "The editable candidate has no form.");
        Require(form.GetPdfObject().GetAsBoolean(PdfName.NeedAppearances)?.GetValue() == false,
            "E_VERIFY_NEED_APPEARANCES", "The candidate must explicitly disable viewer-generated appearances.");
        var fields = TerminalFields(form);
        Require(fields.Count == manifest.Targets.Count && manifest.Targets.Keys.All(fields.ContainsKey),
            "E_VERIFY_FIELD_SET", "The editable candidate field set differs from the complete manifest.");
        var visitedWidgets = new HashSet<string>(StringComparer.Ordinal);
        var fontVerification = new NativeTextAppearance.VerificationContext();
        foreach (var pair in manifest.Targets)
        {
            var target = pair.Value;
            var field = fields[pair.Key];
            Require(field.GetWidgets().Count == 1, "E_VERIFY_WIDGETS", "The candidate widget set differs from the manifest.");
            var widget = field.GetWidgets()[0].GetPdfObject();
            Require(visitedWidgets.Add(ObjectId(widget)), "E_VERIFY_WIDGET_ALIAS", "Multiple canonical fields refer to the same widget.");
            VerifyWidgetGeometry(widget, target.Widget);
            Require((Integer(Inherited(field.GetPdfObject(), PdfName.Ff, form.GetPdfObject())) ?? 0) == target.Flags
                && Integer(Inherited(field.GetPdfObject(), PdfName.MaxLen, form.GetPdfObject())) == target.MaxLength
                && (Integer(Inherited(field.GetPdfObject(), PdfName.Q, form.GetPdfObject())) ?? 0) == target.Alignment,
                "E_VERIFY_FIELD_ATTRIBUTES", "Effective field attributes changed unexpectedly.");
            VerifyNoInheritedValues(field);
            if (target.Assignment.Kind == "checkbox") VerifyCheckbox(field, target);
            else
            {
                Require(PdfName.Tx.Equals(Inherited(field.GetPdfObject(), PdfName.FT, null)), "E_VERIFY_FIELD_TYPE", "A text target changed field type.");
                Require(field.GetPdfObject().GetAsString(PdfName.V)?.ToUnicodeString() == target.Assignment.Text,
                    "E_VERIFY_TEXT_VALUE", "A text value or expected blank failed exact readback.");
                Require(field.GetPdfObject().GetAsString(PdfName.DV)?.ToUnicodeString() == "",
                    "E_VERIFY_RESET_DEFAULT", "An editable reset default is not empty.");
                Require(!widget.ContainsKey(PdfName.AS), "E_VERIFY_TEXT_STATE", "A text widget retains an unsupported appearance state.");
                var ap = widget.GetAsDictionary(PdfName.AP)?.GetAsStream(PdfName.N);
                Require(ap is not null, "E_VERIFY_TEXT_AP", "A text target has no normal appearance stream.");
                try
                {
                    if (ScheduleBEmbeddedFont.IsReplacementPolicy(target.Font!.Policy))
                        ScheduleBEmbeddedFont.VerifyDefaults(pdf, field, target.Font, fontVerification);
                    else if (target.Font.Policy == NativeTextAppearance.PreservedFontPolicy)
                        NativeTextAppearance.VerifyPreservedDefaults(field, target.Font, fontVerification);
                    NativeTextAppearance.Verify(ap!, target, fontVerification);
                }
                catch (VerifiedFillException ex) { ex.ApprovedField ??= target.Assignment.Field; throw; }
            }
            Require(!field.GetPdfObject().ContainsKey(PdfName.RV) && !widget.ContainsKey(PdfName.RV),
                "E_VERIFY_RICH_VALUE", "A value-bearing rich-text remnant remains.");
            if (!SameObject(widget, field.GetPdfObject()))
                Require(!widget.ContainsKey(PdfName.V) && !widget.ContainsKey(PdfName.DV),
                    "E_VERIFY_WIDGET_VALUE", "A separate widget retains a value or reset default.");
            var appearances = widget.GetAsDictionary(PdfName.AP);
            Require(appearances is not null && appearances.Size() == 1 && appearances.ContainsKey(PdfName.N),
                "E_VERIFY_ALTERNATE_AP", "An unsupported alternate appearance remains.");
        }
        var pageWidgets = new HashSet<string>(StringComparer.Ordinal);
        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
            foreach (var annotation in pdf.GetPage(p).GetAnnotations().Where(a => PdfName.Widget.Equals(a.GetSubtype())))
            {
                string id = ObjectId(annotation.GetPdfObject());
                Require(pageWidgets.Add(id) && visitedWidgets.Contains(id), "E_VERIFY_ORPHAN_WIDGET", "An unowned or duplicate page widget remains.");
                var owner = fields.Single(f => SameObject(f.Value.GetWidgets()[0].GetPdfObject(), annotation.GetPdfObject())).Key;
                Require(manifest.Targets[owner].Widget.Page == p, "E_VERIFY_WIDGET_PAGE", "A widget moved to another page.");
            }
        Require(pageWidgets.SetEquals(visitedWidgets), "E_VERIFY_WIDGET_LINKAGE", "Canonical widgets and page annotations do not agree.");
    }

    internal static void VerifyFlattened(byte[] bytes, RenderManifest manifest)
    {
        Require(ApprovedTemplateNormalizer.HasNoActiveContent(TemplateSnapshot.FromBytes(bytes)),
            "E_VERIFY_FINAL_ACTIVE_CONTENT", "The final candidate contains unsupported active or signed content.");
        using var pdf = new PdfDocument(Reader(bytes));
        VerifyPages(pdf, manifest, true);
        FlattenedPaintVerifier.Verify(pdf, manifest);
        var acro = pdf.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.AcroForm);
        Require(acro is null || (!acro.ContainsKey(PdfName.XFA) && (acro.GetAsArray(PdfName.Fields)?.Size() ?? 0) == 0),
            "E_VERIFY_FINAL_FIELDS", "Interactive fields or XFA remain in the flattened candidate.");
        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
            Require(!pdf.GetPage(p).GetAnnotations().Any(a => PdfName.Widget.Equals(a.GetSubtype())),
                "E_VERIFY_FINAL_WIDGETS", "A widget remains in the flattened candidate.");
    }

    private static void SetCheckbox(PdfDocument pdf, PdfFormField field, TargetWitness target)
    {
        var widget = field.GetWidgets()[0];
        var normal = widget.GetPdfObject().GetAsDictionary(PdfName.AP)!.GetAsDictionary(PdfName.N)!;
        Require(target.CheckboxMark is not null, "E_FILL_CHECKBOX_POLICY", "The checkbox mark policy is missing.");
        normal.Put(new PdfName(target.OnState!), VectorCheckboxAppearance.Create(target.Widget, target.CheckboxMark!.Value, true).MakeIndirect(pdf));
        normal.Put(Off, VectorCheckboxAppearance.Create(target.Widget, target.CheckboxMark.Value, false).MakeIndirect(pdf));
        var state = target.Assignment.Checked ? new PdfName(target.OnState!) : Off;
        field.GetPdfObject().Put(PdfName.V, state);
        widget.GetPdfObject().Put(PdfName.AS, state);
        ClearResetData(field, true);
        RemoveAlternateAppearances(field);
    }
    private static void VerifyCheckbox(PdfFormField field, TargetWitness target)
    {
        Require(PdfName.Btn.Equals(Inherited(field.GetPdfObject(), PdfName.FT, null)), "E_VERIFY_FIELD_TYPE", "A checkbox target changed field type.");
        var widget = field.GetWidgets()[0].GetPdfObject();
        var state = target.Assignment.Checked ? new PdfName(target.OnState!) : Off;
        Require(state.Equals(field.GetPdfObject().GetAsName(PdfName.V)) && state.Equals(widget.GetAsName(PdfName.AS)),
            "E_VERIFY_CHECKBOX_STATE", "Checkbox value and widget state do not match the expected assignment.");
        Require(Off.Equals(field.GetPdfObject().GetAsName(PdfName.DV)), "E_VERIFY_RESET_DEFAULT", "A checkbox reset default is not Off.");
        var normal = widget.GetAsDictionary(PdfName.AP)?.GetAsDictionary(PdfName.N);
        Require(normal is not null && normal.Size() == 2 && normal.GetAsStream(state) is not null,
            "E_VERIFY_CHECKBOX_AP", "The selected checkbox normal appearance is unavailable.");
        var on = normal!.GetAsStream(new PdfName(target.OnState!));
        Require(on is not null && Digest(on.GetBytes()) == target.OnAppearanceDigest && AppearanceResourceIdentity.Compute(on) == target.OnResourceIdentity,
            "E_VERIFY_ON_AP_CHANGED", "The authored checkbox on appearance changed.");
        VectorCheckboxAppearance.Verify(on!, target, true);
        var off = normal.GetAsStream(Off);
        Require(off is not null && Encoding.ASCII.GetString(off.GetBytes()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).SequenceEqual(new[] { "q", "Q" }),
            "E_VERIFY_OFF_AP", "The authored normal Off appearance is not empty.");
        VectorCheckboxAppearance.Verify(off!, target, false);
    }
    private static void ClearResetData(PdfFormField field, bool checkbox)
    {
        var canonical = field.GetPdfObject();
        canonical.Put(PdfName.DV, checkbox ? Off : new PdfString(""));
        canonical.Remove(PdfName.RV);
        foreach (var widget in field.GetWidgets())
        {
            var dict = widget.GetPdfObject();
            dict.Remove(PdfName.RV);
            if (!checkbox) dict.Remove(PdfName.AS);
            if (!SameObject(dict, canonical)) { dict.Remove(PdfName.V); dict.Remove(PdfName.DV); }
        }
    }
    private static void ClearAncestorDefaults(IEnumerable<PdfFormField> fields)
    {
        var terminal = fields.Select(f => ObjectId(f.GetPdfObject())).ToHashSet(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (var node = field.GetPdfObject().GetAsDictionary(PdfName.Parent); node is not null; node = node.GetAsDictionary(PdfName.Parent))
            {
                Require(visited.Count < 64 && visited.Add(ObjectId(node)) && !terminal.Contains(ObjectId(node)),
                    "E_FILL_FIELD_TREE", "The canonical field ancestry is unsupported.");
                node.Remove(PdfName.V); node.Remove(PdfName.DV); node.Remove(PdfName.RV);
            }
        }
    }
    private static void VerifyNoInheritedValues(PdfFormField field)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var node = field.GetPdfObject().GetAsDictionary(PdfName.Parent); node is not null; node = node.GetAsDictionary(PdfName.Parent))
        {
            Require(visited.Count < 64 && visited.Add(ObjectId(node)), "E_VERIFY_FIELD_TREE", "The candidate field ancestry is unsupported.");
            Require(!node.ContainsKey(PdfName.V) && !node.ContainsKey(PdfName.DV) && !node.ContainsKey(PdfName.RV),
                "E_VERIFY_ANCESTOR_VALUE", "An ancestor retains a value-bearing default or remnant.");
        }
    }
    private static void RemoveAlternateAppearances(PdfFormField field)
    {
        var ap = field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP);
        ap?.Remove(PdfName.D); ap?.Remove(PdfName.R);
    }
    private static void VerifyWidgetGeometry(PdfDictionary widget, WidgetInspection expected)
    {
        Require(!widget.ContainsKey(PdfName.OC), "E_VERIFY_WIDGET_VISIBILITY", "Managed widgets cannot introduce optional-content visibility.");
        var rect = widget.GetAsArray(PdfName.Rect)?.ToDoubleArray();
        Require(rect is not null && rect.Length == 4 && rect.Zip(expected.Rectangle).All(x => Math.Abs(x.First - x.Second) <= .002),
            "E_VERIFY_WIDGET_GEOMETRY", "A widget rectangle changed unexpectedly.");
        Require((widget.GetAsDictionary(PdfName.MK)?.GetAsNumber(PdfName.R)?.IntValue() ?? 0) == expected.Rotation
            && (widget.GetAsNumber(PdfName.F)?.IntValue() ?? 0) == expected.AnnotationFlags,
            "E_VERIFY_WIDGET_ATTRIBUTES", "Widget rotation or annotation flags changed unexpectedly.");
    }
    internal static void VerifyAppearanceBox(PdfStream appearance, WidgetInspection widget)
    {
        Require(!appearance.ContainsKey(PdfName.OC) && !appearance.ContainsKey(PdfName.Group),
            "E_VERIFY_AP_VISIBILITY", "Authored appearances cannot introduce optional-content or transparency groups.");
        var box = appearance.GetAsArray(PdfName.BBox)?.ToDoubleArray();
        Require(box is { Length: 4 } && Math.Abs(box[0]) < .002 && Math.Abs(box[1]) < .002
            && Math.Abs(box[2] - (widget.Rectangle[2] - widget.Rectangle[0])) <= .02
            && Math.Abs(box[3] - (widget.Rectangle[3] - widget.Rectangle[1])) <= .02,
            "E_VERIFY_AP_BBOX", "The normal appearance has an unsupported bounding box.");
        var matrix = appearance.GetAsArray(PdfName.Matrix)?.ToDoubleArray();
        Require(matrix is null || matrix.SequenceEqual(new double[] { 1, 0, 0, 1, 0, 0 }),
            "E_VERIFY_AP_TRANSFORM", "The normal appearance has an unsupported transform.");
    }
    private static PageWitness[] CapturePages(PdfDocument pdf)
    {
        return Enumerable.Range(1, pdf.GetNumberOfPages()).Select(p =>
        {
            var page = pdf.GetPage(p);
            Require(page.GetRotation() == 0 && (page.GetPdfObject().GetAsNumber(PdfName.UserUnit)?.FloatValue() ?? 1) == 1,
                "E_FILL_PAGE_TRANSFORM", "Rotated or scaled pages are not supported by the renderer.", 3);
            return new PageWitness(Boxes.Select(n => PageBox(page, n)).ToArray(), page.GetRotation(),
                page.GetPdfObject().GetAsNumber(PdfName.UserUnit)?.FloatValue() ?? 1,
                Enumerable.Range(0, page.GetContentStreamCount()).Select(i => Digest(page.GetContentStream(i).GetBytes())).ToArray(),
                page.GetAnnotations().Count(a => !PdfName.Widget.Equals(a.GetSubtype())));
        }).ToArray();
    }
    private static float[] PageBox(PdfPage page, PdfName name)
    {
        var rect = name.Equals(PdfName.MediaBox) ? page.GetMediaBox() : name.Equals(PdfName.CropBox) ? page.GetCropBox()
            : name.Equals(PdfName.BleedBox) ? page.GetBleedBox() : name.Equals(PdfName.TrimBox) ? page.GetTrimBox() : page.GetArtBox();
        return [rect.GetLeft(), rect.GetBottom(), rect.GetRight(), rect.GetTop()];
    }
    private static void VerifyPages(PdfDocument pdf, RenderManifest expected, bool flattened)
    {
        Require(pdf.GetNumberOfPages() == expected.Pages.Length, "E_VERIFY_PAGE_COUNT", "Candidate page count changed.");
        Require(pdf.GetDocumentInfo().GetTitle() == expected.Title && !string.IsNullOrWhiteSpace(pdf.GetDocumentInfo().GetProducer()),
            "E_VERIFY_METADATA", "Required source title or producer metadata was not preserved.");
        var current = CapturePages(pdf);
        for (int p = 0; p < current.Length; p++)
        {
            var a = current[p]; var b = expected.Pages[p];
            Require(a.Rotation == b.Rotation && a.UserUnit == b.UserUnit
                && a.Boxes.Zip(b.Boxes).All(x => x.First.SequenceEqual(x.Second)) && a.OtherAnnotations == b.OtherAnnotations,
                "E_VERIFY_PAGE_GEOMETRY", "Candidate page boxes, transforms or non-widget annotations changed.");
            Require(flattened ? IsOrderedSubsequence(b.ContentDigests, a.ContentDigests) : b.ContentDigests.SequenceEqual(a.ContentDigests),
                "E_VERIFY_PAGE_CONTENT", "Original static page content was not preserved byte-for-byte.");
        }
    }
    private static bool IsOrderedSubsequence(string[] original, string[] current)
    {
        int next = 0;
        foreach (string digest in current) if (next < original.Length && original[next] == digest) next++;
        return next == original.Length;
    }
    // iText may wrap a nameless widget with local /DA or /FT as a form field.
    // Traverse its raw dictionary ownership with the independent inspector's
    // partition, rather than using the flattened name map or wrapper terminality.
    private static Dictionary<string, PdfFormField> TerminalFields(PdfAcroForm form)
    {
        var result = new Dictionary<string, PdfFormField>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int entries = 0;
        void Reserve(int count)
        {
            Require(count >= 0 && count <= 30_000 - entries, "E_FILL_FIELD_TREE", "The canonical field tree exceeds its entry bound.", 3);
            entries += count;
        }
        static bool Annotation(PdfDictionary node) => PdfName.Widget.Equals(node.GetAsName(PdfName.Subtype))
            && !node.ContainsKey(PdfName.T);
        Dictionary<string, PdfFormField> Index(IEnumerable<PdfFormField> fields, int maximum)
        {
            var indexed = new Dictionary<string, PdfFormField>(StringComparer.Ordinal);
            foreach (var field in fields)
                Require(indexed.Count < maximum && indexed.TryAdd(ObjectId(field.GetPdfObject()), field),
                    "E_FILL_FIELD_TREE", "Canonical wrapper identities are missing, repeated or excessive.", 3);
            return indexed;
        }
        void Walk(PdfFormField field, string prefix, int depth, PdfDictionary? expectedParent)
        {
            var node = field.GetPdfObject();
            Require(depth <= 64 && seen.Add(ObjectId(node)), "E_FILL_FIELD_TREE", "Canonical field ancestry repeats or exceeds its depth bound.", 3);
            Require(expectedParent is null || node.GetAsDictionary(PdfName.Parent) is { } parent && SameObject(parent, expectedParent),
                "E_FILL_FIELD_TREE", "A canonical field does not link to its owning parent.", 3);
            var part = node.GetAsString(PdfName.T)?.ToUnicodeString();
            Require(part is null || part.Length <= 1024, "E_FILL_FIELD_TREE", "A canonical field name exceeds its bound.", 3);
            string name = part is null ? prefix : prefix.Length == 0 ? part : prefix + "." + part;
            var kids = node.GetAsArray(PdfName.Kids);
            int count = kids?.Size() ?? 0;
            Reserve(count);
            var wrapped = Index(field.GetChildFormFields(), count);
            var children = new List<PdfFormField>();
            int widgets = 0;
            for (int i = 0; i < count; i++)
            {
                var child = kids!.GetAsDictionary(i);
                Require(child is not null, "E_FILL_FIELD_TREE", "A canonical child is not a dictionary.", 3);
                string identity = ObjectId(child!);
                if (Annotation(child!))
                {
                    widgets++;
                    Require(seen.Add(identity) && child!.GetAsDictionary(PdfName.Parent) is { } owner && SameObject(owner, node),
                        "E_FILL_FIELD_TREE", "A canonical widget has repeated or inconsistent ownership.", 3);
                    wrapped.Remove(identity);
                }
                else
                {
                    Require(wrapped.Remove(identity, out var childField), "E_FILL_FIELD_TREE", "A canonical child has no exact wrapper.", 3);
                    children.Add(childField!);
                }
            }
            Require(wrapped.Count == 0, "E_FILL_FIELD_TREE", "A wrapper is absent from the canonical child inventory.", 3);
            if (children.Count > 0)
            {
                Require(widgets == 0 && !PdfName.Widget.Equals(node.GetAsName(PdfName.Subtype)),
                    "E_FILL_FIELD_TREE", "A field container mixes widgets and named children.", 3);
                foreach (var child in children) Walk(child, name, depth + 1, node);
                return;
            }
            Require(Inherited(node, PdfName.FT, null) is PdfName && name.Length > 0
                && result.Count < Limits.MaxKeysOrTargets && result.TryAdd(name, field),
                "E_FILL_FIELD_TREE", "Canonical terminal field identity or type is missing, repeated or excessive.", 3);
        }
        var roots = form.GetPdfObject().GetAsArray(PdfName.Fields);
        Require(roots is not null, "E_FILL_FIELD_TREE", "Canonical field roots are missing.", 3);
        Reserve(roots!.Size());
        var rootWrappers = Index(form.GetRootFormFields().Values, roots.Size());
        for (int i = 0; i < roots.Size(); i++)
        {
            var node = roots.GetAsDictionary(i);
            Require(node is not null, "E_FILL_FIELD_TREE", "A canonical root is not a dictionary.", 3);
            Require(rootWrappers.Remove(ObjectId(node!), out var wrapper),
                "E_FILL_FIELD_TREE", "A canonical root has no exact unique wrapper.", 3);
            // Root names are never reconstructed from iText's flattened child map.
            Walk(wrapper!, "", 0, null);
        }
        Require(rootWrappers.Count == 0, "E_FILL_FIELD_TREE", "A wrapper is absent from the canonical root inventory.", 3);
        return result;
    }
    internal static PdfObject? Inherited(PdfDictionary? node, PdfName key, PdfDictionary? acro)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (node is not null)
        {
            Require(visited.Count < 64 && visited.Add(ObjectId(node)), "E_FILL_FIELD_TREE", "Field ancestry exceeds the supported boundary.", 3);
            if (node.ContainsKey(key)) return node.Get(key, true);
            node = node.GetAsDictionary(PdfName.Parent);
        }
        return key.Equals(PdfName.DA) || key.Equals(PdfName.Q) || key.Equals(PdfName.DR) ? acro?.Get(key, true) : null;
    }
    private static int? Integer(PdfObject? value) => value is PdfNumber number ? number.IntValue() : null;
    private static string ObjectId(PdfDictionary dict) => dict.GetIndirectReference() is { } r
        ? r.GetObjNumber() + ":" + r.GetGenNumber() : "direct:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(dict);
    private static bool SameObject(PdfDictionary a, PdfDictionary b) => ReferenceEquals(a, b) || ObjectId(a) == ObjectId(b);
    internal static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static PdfReader Reader(byte[] bytes)
    {
        var limits = new OutputMemoryLimits();
        limits.SetMaxSizeOfSingleDecompressedPdfStream(10 * 1024 * 1024);
        limits.SetMaxSizeOfDecompressedPdfStreamsSum(50 * 1024 * 1024);
        limits.SetMaxNumberOfElementsInXrefStructure(100_000);
        return new PdfReader(new MemoryStream(bytes, writable: false), new ReaderProperties().SetMemoryLimitsAwareHandler(limits));
    }
    private sealed class OutputMemoryLimits : MemoryLimitsAwareHandler
    {
        [Obsolete("Pinned-engine accounting override; every decoded stream is bounded.")]
        public override bool IsMemoryLimitsAwarenessRequiredOnDecompression(PdfArray filters) => true;
    }
    internal static void Require(bool value, string code, string safeMessage, int exit = 4)
    { if (!value) throw Error(code, safeMessage, exit); }
    private static VerifiedFillException Error(string code, string safeMessage, int exit = 4) => new(code, safeMessage, exit);
}
