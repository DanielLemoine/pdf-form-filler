using System.Security.Cryptography;
using System.Text;
using System.Xml;
using iText.IO.Font;
using iText.Kernel.Pdf;
using PdfForm.Core;

namespace PdfForm.Pdf;

/// <summary>Value-free observations. Stable field IDs never encode a PDF value, default or tooltip.</summary>
public sealed record CompatibilityField(string Id, string Type, int WidgetCount, int? Page,
    IReadOnlyList<double> Rectangle, int? MaxLength, bool Required, bool Comb);

public sealed record ConfigurationCompatibility(string Schema, string Status, int ExitCode,
    string TemplateSha256, int TemplateBytes, int Pages, bool Prefilled,
    IReadOnlyList<CompatibilityField> Fields, IReadOnlyList<Finding> Issues,
    IReadOnlyList<string> MissingEngineFeatures, IReadOnlyList<string> RequiredConfiguration,
    IReadOnlyList<string> NotAssessed);

/// <summary>Structural font identity, not a font substitution or a rendering-approval constructor.</summary>
public sealed record ConfigurationFontFacts(string ResourceName, string BaseFont, string Subtype,
    string Encoding, double? FontSize, string ProgramKind, string ProgramSha256,
    string MetricsSha256, bool HasUnicodeMapping, bool Embedded, bool ProgramUsable);

/// <summary>Only the explicitly requested private draft exposes real target/resource names.</summary>
public sealed record ConfigurationTargetFacts(string Key, string Target, string Type, int? Page,
    IReadOnlyList<double> Rectangle, int? MaxLength, bool Required, bool Comb,
    IReadOnlyList<string> OnStates, ConfigurationFontFacts? Font);

public sealed record StructuralConfigurationDraft(string Schema, string TemplateSha256,
    int TemplateBytes, string TemplateTitle, int Pages, string? StructureFingerprintV1,
    IReadOnlyList<ConfigurationTargetFacts> Targets, ConfigurationCompatibility Compatibility);

public sealed partial class PdfTemplateInspector
{
    private sealed class ConfigurationFontStructureException : Exception { }

    private sealed class ConfigurationInspectionSession
    {
        internal bool ExternalConfiguration;
        internal bool Admitted;
        internal string? ProfileId;
        internal FormConfigurationSnapshot? Configuration;
        internal string Title = "";
        internal bool Prefilled;
        internal readonly List<ConfigurationTargetFacts> Targets = [];
        internal readonly HashSet<string> TargetIds = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, ConfigurationFontFacts> FontCache = new(StringComparer.Ordinal);
    }

    public static string ConfigurationFieldKey(string canonicalFieldName)
    {
        ArgumentNullException.ThrowIfNull(canonicalFieldName);
        return "field_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalFieldName)));
    }

    /// <summary>Read-only structural discovery. No current/default field values are copied into this result.</summary>
    public StructuralConfigurationDraft DiscoverConfiguration(TemplateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var session = new ConfigurationInspectionSession { ExternalConfiguration = true };
        var inspection = InspectCore(snapshot, session);
        var compatibility = ConfigurationReport(snapshot, inspection, session, configured: false);
        // A failed/prefilled document cannot accidentally become a value-bearing configuration artifact.
        var targets = compatibility.Status is "configuration_needed" or "supported"
            ? session.Targets.OrderBy(t => t.Target, StringComparer.Ordinal).ToArray() : [];
        return new("pdfform.configuration-draft-facts/1", snapshot.Sha256, snapshot.ByteCount,
            targets.Length == 0 ? "" : session.Title, inspection.Pages, inspection.StructureFingerprintV1,
            Array.AsReadOnly(targets), compatibility);
    }

    public ConfigurationCompatibility CheckCompatibility(TemplateSnapshot snapshot)
        => DiscoverConfiguration(snapshot).Compatibility;

    public TemplateInspection Inspect(TemplateSnapshot snapshot, FormConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!snapshot.MatchesConfiguration(configuration)) return Failure(snapshot, "E_CONFIGURATION_TEMPLATE");
        if (configuration.IsBuiltIn) return Inspect(snapshot);
        var profile = configuration.ParseProfile();
        var session = new ConfigurationInspectionSession
        {
            ExternalConfiguration = true, Admitted = true, ProfileId = profile.Id, Configuration = configuration
        };
        return InspectCore(snapshot, session);
    }

    public ConfigurationCompatibility CheckCompatibility(TemplateSnapshot snapshot, FormConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(configuration);
        var session = new ConfigurationInspectionSession { ExternalConfiguration = !configuration.IsBuiltIn, Admitted = true };
        if (!snapshot.MatchesConfiguration(configuration))
            return ConfigurationReport(snapshot, Failure(snapshot, "E_CONFIGURATION_TEMPLATE"), session, configured: false);
        var binding = new TemplateBinder().Bind(snapshot, configuration);
        session.Prefilled = binding.Findings.Any(f => f.Code == "E_TEMPLATE_PREFILLED");
        return ConfigurationReport(snapshot, binding.Inspection, session, configured: binding.Bound, binding.Findings);
    }

    private static ConfigurationCompatibility ConfigurationReport(TemplateSnapshot snapshot,
        TemplateInspection inspection, ConfigurationInspectionSession session, bool configured, IReadOnlyList<Finding>? bindingFindings = null)
    {
        var findings = bindingFindings ?? inspection.Findings;
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var field in inspection.Fields)
        {
            if (field.Type is not ("text" or "checkbox")) missing.Add("field." + field.Type);
            if (field.Widgets.Count != 1) missing.Add("field.singleWidget");
            if (field.Effective.Flags.Multiline) missing.Add("text.multiline");
            if (field.Effective.Flags.RichText) missing.Add("text.richText");
            if (field.Effective.Flags.Password) missing.Add("text.password");
            if (field.Effective.Flags.ReadOnly) missing.Add("field.readOnly");
            if (field.Effective.Flags.NoExport) missing.Add("field.noExport");
            if (field.Type == "text" && !field.Effective.Font.ResourcePresent) missing.Add("font.resource");
            if (field.Type == "text" && field.Effective.Font.Size is not (>= 1 and <= 72)) missing.Add("font.fixedSize1To72");
            if (field.Widgets.Any(w => w.Rotation != 0)) missing.Add("widget.rotation");
        }
        foreach (var target in session.Targets)
        {
            if (target.Font is { } font)
            {
                if (font.Subtype is not ("Type1" or "TrueType")) missing.Add("font.simpleType1OrTrueType");
                if (font.Encoding is not ("absent" or "WinAnsiEncoding")) missing.Add("font.supportedSourceEncoding");
                if (font.Embedded && !font.ProgramUsable) missing.Add("font.validSourceProgram");
            }
            if (target.Type == "checkbox")
            {
                if (target.OnStates.Count == 0) missing.Add("checkbox.normalExportState");
                if (target.Rectangle.Count == 4)
                {
                    double width = target.Rectangle[2] - target.Rectangle[0], height = target.Rectangle[3] - target.Rectangle[1];
                    if (!(Math.Abs(width - 10) < .002 && Math.Abs(height - 10) < .002)
                        && !(Math.Abs(width - 12) < .002 && Math.Abs(height - 12) < .002))
                        missing.Add("checkbox.fixedMarkGeometry");
                }
            }
        }
        if (!inspection.HasAcroForm && inspection.Pages > 0) missing.Add("form.acroform");
        if (inspection.Xfa.Present && (session.ExternalConfiguration || inspection.Xfa.Kind != "staticHybridApproved")) missing.Add("xfa.configuredAcroform");
        if (inspection.CatalogJavaScriptCount > 0 && (session.ExternalConfiguration || !inspection.CatalogJavaScript.ApprovedRemovalRequired)
            || inspection.Capabilities.Any(c => c.Capability == "active.action" && c.Count > 0))
            missing.Add("active.action");
        if (inspection.Signatures.ApprovalSignatureCount > 0 || inspection.Signatures.CertificationPresent
            || inspection.Signatures.UsageRightsSignatureCount > 0 && (session.ExternalConfiguration || !inspection.Signatures.ApprovedUsageRightsException)
            || inspection.Signatures.UnknownPermissionEntry)
            missing.Add("signature.configuredAcroform");
        if (inspection.Encrypted) missing.Add("pdf.encryption");
        var invalidCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "E_PDF_PARSE", "E_PDF_LIMIT", "E_PDF_BOOLEAN", "E_FIELD_TREE", "E_FIELD_CYCLE", "E_FIELD_PARENT",
            "E_FIELD_NAME", "E_FIELD_TYPE", "E_FIELD_ATTRIBUTE", "E_WIDGET_DUPLICATE", "E_WIDGET_PARENT",
            "E_WIDGET_NOT_ON_PAGE", "E_FIELD_NO_WIDGET", "E_WIDGET_GEOMETRY", "E_ORPHAN_WIDGET",
            "E_CONFIGURATION_TEMPLATE", "E_CONFIGURATION_POLICY", "E_CONFIGURATION_FIELD_ID", "E_TEMPLATE_HASH", "E_TEMPLATE_TITLE",
            "E_TEMPLATE_PAGES", "E_TEMPLATE_STRUCTURE", "E_TEMPLATE_PROFILE", "E_TARGET_MISSING", "E_TARGET_TYPE",
            "E_TARGET_OVERLAP", "E_TARGET_UNMAPPED", "E_PROFILE_CAPABILITY", "E_CONFIG_FONT_IDENTITY",
            "E_CONFIG_TEXT_TARGET", "E_CONFIG_CHECKBOX_TARGET", "E_CONFIG_DATE_LAYOUT", "E_CONFIG_FONT_STRUCTURE"
        };
        bool invalid = findings.Any(f => f.Severity == "error" && invalidCodes.Contains(f.Code));
        bool unsupported = missing.Count != 0 || session.Prefilled || findings.Any(f => f.Severity == "error");
        string status = invalid ? "invalid" : unsupported ? "unsupported" : configured ? "supported" : "configuration_needed";
        int exit = status switch { "supported" => 0, "configuration_needed" => 1, "invalid" => 2, _ => 3 };
        var canonicalIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in inspection.NamedFields) canonicalIds.TryAdd(pair.Value.Id, ConfigurationFieldKey(pair.Key));
        var safeFields = inspection.Fields.Select(f => new CompatibilityField(canonicalIds.GetValueOrDefault(f.Id) ?? ConfigurationFieldKey("invalid:" + f.Id), f.Type, f.Widgets.Count,
            f.Widgets.Count == 1 ? f.Widgets[0].Page : null,
            Array.AsReadOnly(f.Widgets.Count == 1 ? f.Widgets[0].Rectangle.ToArray() : []),
            f.Effective.MaxLength, f.Effective.Flags.Required, f.Effective.Flags.Comb)).ToArray();
        // Do not relay arbitrary field/metadata names, exception text, source paths or values.
        var safeKeys = safeFields.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var safeIssues = findings.Select(f => new Finding(f.Code, "binding", f.Message,
            Key: f.Key is { } key && safeKeys.Contains(key) ? key
                : f.Field is { } target && inspection.NamedFields.ContainsKey(target) ? ConfigurationFieldKey(target) : null,
            Severity: f.Severity)).ToArray();
        return new("pdfform.compatibility/1", status, exit, snapshot.Sha256, snapshot.ByteCount, inspection.Pages,
            session.Prefilled, Array.AsReadOnly(safeFields), Array.AsReadOnly(safeIssues), Array.AsReadOnly(missing.ToArray()),
            Array.AsReadOnly(status == "configuration_needed" ? new[] { "exactCatalogProfileBinding", "renderPolicy", "operatorReview" } : []),
            Array.AsReadOnly(new[] { "businessMeaning", "suppliedValueFit", "viewerAcceptance", "physicalPrint", "filingReadiness", "staticPageBlankness" }));
    }

    private static void CollectConfigurationField(ConfigurationInspectionSession session, FieldNode field,
        FieldInspection report, PdfDictionary? af, List<Finding> findings)
    {
        if (field.Name.Length > 1024) throw new InspectionLimitException();
        if (!session.TargetIds.Add(field.Id))
            findings.Add(new("E_CONFIGURATION_FIELD_ID", "binding", "Canonical field identifiers are missing, duplicated or collide."));
        bool control = report.Type is "checkbox" or "radio";
        bool populated = Populated(Inherited(field.Dictionary, "V", af), control)
            || Populated(Inherited(field.Dictionary, "DV", af), control);
        foreach (var widget in field.Widgets)
        {
            populated |= Populated(Inherited(widget, "V", af), control)
                || Populated(Inherited(widget, "DV", af), control);
            if (report.Type is "checkbox" or "radio")
            {
                var state = widget.Get(N("AS"), true);
                populated |= state is not null && (state is not PdfName name || name.GetValue() != "Off");
            }
        }
        if (populated)
        {
            session.Prefilled = true;
            findings.Add(new("E_TEMPLATE_PREFILLED", "binding", "A current/default or selected control value is present; use a blank original."));
        }
        ConfigurationFontFacts? font = report.Type == "text" ? ObserveFont(session, field.Dictionary, af, report.Effective.Font) : null;
        var widgetReport = report.Widgets.Count == 1 ? report.Widgets[0] : null;
        session.Targets.Add(new(ConfigurationFieldKey(field.Name), field.Name, report.Type, widgetReport?.Page,
            Array.AsReadOnly(widgetReport?.Rectangle.ToArray() ?? []), report.Effective.MaxLength,
            report.Effective.Flags.Required, report.Effective.Flags.Comb,
            Array.AsReadOnly(report.Type == "checkbox" && field.Widgets.Count == 1
                ? NormalOnStates(field.Widgets[0]) : report.AllowedOnStates.ToArray()), font));
    }

    private static string[] NormalOnStates(PdfDictionary widget)
    {
        if (widget.GetAsDictionary(PdfName.AP)?.Get(PdfName.N, true) is not PdfDictionary normal || normal is PdfStream) return [];
        return normal.KeySet().Where(key => key.GetValue() != "Off" && normal.GetAsStream(key) is not null)
            .Select(key => key.GetValue()).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool Populated(PdfObject? value, bool checkbox) => value switch
    {
        null or PdfNull => false,
        PdfString text => text.ToUnicodeString().Length != 0,
        PdfName name => !checkbox || name.GetValue() != "Off",
        PdfArray array => array.Size() != 0,
        _ => true
    };

    private static ConfigurationFontFacts ObserveFont(ConfigurationInspectionSession session, PdfDictionary field, PdfDictionary? af, FontInspection inspected)
    {
        string resource = inspected.Name ?? "";
        var dictionary = (Inherited(field, "DR", af) as PdfDictionary)?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(new PdfName(resource));
        if (dictionary is null) return new(resource, "", "", "", inspected.Size, "none", "", "", false, false, false);
        string cacheKey = ObjectId(dictionary);
        if (session.FontCache.TryGetValue(cacheKey, out var cached))
            return cached with { ResourceName = resource, FontSize = inspected.Size };
        PdfDictionary? descriptor = null;
        if (dictionary.ContainsKey(PdfName.FontDescriptor))
        {
            if (dictionary.Get(PdfName.FontDescriptor, true) is not PdfDictionary value || value is PdfStream)
                throw new ConfigurationFontStructureException();
            descriptor = value;
        }
        string kind = "none"; PdfStream? program = null;
        var programKeys = new[] { PdfName.FontFile, PdfName.FontFile2, PdfName.FontFile3 }
            .Where(key => descriptor?.ContainsKey(key) == true).ToArray();
        if (programKeys.Length > 1) throw new ConfigurationFontStructureException();
        if (programKeys.Length == 1)
        {
            var key = programKeys[0];
            if (descriptor!.Get(key, true) is not PdfStream candidate)
                throw new ConfigurationFontStructureException();
            program = candidate; kind = key.GetValue();
        }
        string programHash = "";
        bool programUsable = false;
        if (program is not null)
        {
            var bytes = program.GetBytes();
            if (bytes.Length is < 1 or > 10 * 1024 * 1024) throw new InspectionLimitException();
            programHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            try { _ = FontProgramFactory.CreateFont(bytes, false); programUsable = true; }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
        }
        var text = new StringBuilder();
        foreach (var key in new[] { PdfName.BaseFont, PdfName.Subtype, PdfName.Encoding, PdfName.FirstChar, PdfName.LastChar, PdfName.Widths })
            text.Append(key.GetValue()).Append('=').Append(dictionary.Get(key, true)?.ToString()).Append(';');
        foreach (var key in new[] { PdfName.FontName, PdfName.Flags, PdfName.FontBBox, PdfName.ItalicAngle, PdfName.Ascent, PdfName.Descent, PdfName.CapHeight, PdfName.StemV })
            text.Append(key.GetValue()).Append('=').Append(descriptor?.Get(key, true)?.ToString()).Append(';');
        var encoding = dictionary.Get(PdfName.Encoding, true);
        var observed = new ConfigurationFontFacts(resource, dictionary.GetAsName(PdfName.BaseFont)?.GetValue() ?? "", dictionary.GetAsName(PdfName.Subtype)?.GetValue() ?? "",
            encoding is null ? "absent" : encoding is PdfName encodingName ? encodingName.GetValue() : "unsupported", inspected.Size, kind, programHash,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))),
            dictionary.ContainsKey(PdfName.ToUnicode), program is not null, programUsable);
        if (observed.ResourceName.Length > 1024 || observed.BaseFont.Length > 1024 || observed.Encoding.Length > 1024)
            throw new InspectionLimitException();
        session.FontCache.Add(cacheKey, observed);
        return observed;
    }

    private static void ObserveConfigurationDatasets(ConfigurationInspectionSession session, PdfDictionary? af, List<Finding> findings)
    {
        if (af?.Get(N("XFA"), true) is not PdfArray packets) return;
        if (packets.Size() > 256 || packets.Size() % 2 != 0) return;
        for (int i = 0; i < packets.Size(); i += 2)
        {
            if (packets.GetAsString(i)?.ToUnicodeString() != "datasets") continue;
            var stream = packets.GetAsStream(i + 1);
            if (stream is null) continue;
            var bytes = stream.GetBytes();
            if (bytes.Length > 10 * 1024 * 1024) throw new InspectionLimitException();
            using var input = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10 * 1024 * 1024,
                MaxCharactersFromEntities = 0, IgnoreComments = true, IgnoreProcessingInstructions = true
            });
            try
            {
                while (reader.Read())
                {
                    if (reader.Depth > 64) throw new InspectionLimitException();
                    if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA && !string.IsNullOrWhiteSpace(reader.Value))
                    {
                        session.Prefilled = true;
                        findings.Add(new("E_TEMPLATE_PREFILLED", "binding", "A populated dataset is present; use a blank original."));
                        break;
                    }
                }
            }
            catch (XmlException) { findings.Add(new("E_XFA_XML", "binding", "Dataset XML is malformed or outside the safe parser boundary.")); }
        }
    }

    private static void CheckConfigurationGeometryAndCapabilities(ConfigurationInspectionSession session,
        IReadOnlyList<FieldInspection> fields, PdfDocument pdf, List<Finding> findings)
    {
        if (session.Title.Length > 1024) throw new InspectionLimitException();
        foreach (var field in fields)
        {
            if (field.Type == "text" && field.Effective.Flags.Multiline)
                findings.Add(new("E_CONFIG_TEXT_LAYOUT", "binding", "Multiline text is unsupported by configured rendering."));
            if (field.Type == "text" && field.Effective.Font.Size is not (>= 1 and <= 72))
                findings.Add(new("E_CONFIG_FONT_SIZE", "binding", "Configured text requires an observed fixed font size from 1 through 72 points."));
            foreach (var widget in field.Widgets)
            {
                if (widget.Effective.Flags != field.Effective.Flags || widget.Effective.MaxLength != field.Effective.MaxLength
                    || widget.Effective.Alignment != field.Effective.Alignment || widget.Effective.Font.Name != field.Effective.Font.Name
                    || widget.Effective.Font.Size != field.Effective.Font.Size || widget.Effective.Font.Subtype != field.Effective.Font.Subtype
                    || widget.Effective.Font.ResourcePresent != field.Effective.Font.ResourcePresent || widget.Effective.Font.Embedded != field.Effective.Font.Embedded)
                    findings.Add(new("E_CONFIG_WIDGET_ATTRIBUTES", "binding", "Widget-local attributes differ from the canonical field attributes.", Key: field.Id));
                if (widget.Rotation != 0)
                    findings.Add(new("E_CONFIG_WIDGET_ROTATION", "binding", "Rotated widgets are unsupported by configured rendering."));
                if (widget.Page is not { } page || page < 1 || page > pdf.GetNumberOfPages() || widget.Rectangle.Count != 4) continue;
                var crop = pdf.GetPage(page).GetCropBox();
                if (widget.Rectangle[0] < crop.GetLeft() || widget.Rectangle[1] < crop.GetBottom()
                    || widget.Rectangle[2] > crop.GetRight() || widget.Rectangle[3] > crop.GetTop())
                    findings.Add(new("E_CONFIG_WIDGET_BOUNDS", "binding", "A widget falls outside its page crop box."));
            }
        }
    }
}
