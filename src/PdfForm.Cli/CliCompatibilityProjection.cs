using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using PdfForm.Core;
using PdfForm.Pdf;

namespace PdfForm.Cli;

/// <summary>Explicit projection from internal PDF observations to Core's public compatibility
/// document. Geometry, font facts, names, values, issue text and caller paths never cross it.</summary>
public static class CliCompatibilityProjection
{
    private static readonly HashSet<string> EngineCodes = new("""
        E_ACTIVE_CONTENT E_APPROVAL_SIGNATURE E_ARGUMENTS E_CERTIFICATION E_CHECK_INTERNAL
        E_CHECKBOX_ON_STATE E_CONFIG_CHECKBOX_GEOMETRY E_CONFIG_CHECKBOX_TARGET E_CONFIG_DATE_LAYOUT
        E_CONFIG_FONT_IDENTITY E_CONFIG_FONT_PROGRAM E_CONFIG_FONT_SIZE E_CONFIG_FONT_STRATEGY
        E_CONFIG_FONT_STRUCTURE E_CONFIG_TEXT_LAYOUT E_CONFIG_TEXT_TARGET E_CONFIG_WIDGET_ATTRIBUTES
        E_CONFIG_WIDGET_BOUNDS E_CONFIG_WIDGET_ROTATION E_CONFIGURATION_FIELD_ID E_CONFIGURATION_INPUT
        E_CONFIGURATION_LAYOUT E_CONFIGURATION_POLICY E_CONFIGURATION_REVIEW_REQUIRED E_CONFIGURATION_REVIEW_STATE
        E_CONFIGURATION_SIZE E_CONFIGURATION_TEMPLATE E_CONFIGURATION_TEMPLATE_MISMATCH E_CONFIGURATION_UNRESOLVED
        E_CONFIGURATION_CHECKBOX_GEOMETRY E_CONFIGURATION_DESTINATION E_CONFIGURATION_DISCOVERY E_CONFIGURATION_DRAFT
        E_CONFIGURATION_FONT E_CONFIGURATION_INTERNAL E_CONFIGURATION_ON_STATE E_CONFIGURATION_OPERATION
        E_CONFIGURATION_PUBLICATION E_CONFIGURATION_RESULT_CONTRACT E_CONFIGURATION_RESULT_OUTPUTS E_CONFIGURATION_TEMPLATE_REQUIRED
        E_CONFIGURATION_PUBLICATION_UNCONFIRMED E_POSTCOMMIT_REPORTING
        E_CATALOG_SCHEMA E_CATALOG_TEMPLATE E_CATALOG_PROFILE E_CATALOG_REVIEW E_CATALOG_ISSUES E_CATALOG_POLICY_UNSUPPORTED
        E_CATALOG_TARGETS E_CATALOG_FONT E_CATALOG_MARK_GEOMETRY E_CATALOG_TEMPLATE_HASH E_CATALOG_PROFILE_HASH
        E_CATALOG_PROFILE_IDENTITY E_CATALOG_REVIEW_REQUIRED E_CATALOG_BUILTIN_IDENTITY E_CATALOG_TARGET_COVERAGE
        E_CATALOG_TARGET_REQUIRED E_CATALOG_TARGET_TYPE E_CATALOG_TARGET_PAGE E_CATALOG_ON_STATE
        E_CATALOG_DATE_LAYOUT_REQUIRED E_CATALOG_DATE_LAYOUT_CONFLICT E_CATALOG_DATE_GEOMETRY
        E_CONFIGURATION_DIAGNOSTIC_LIMIT W_CONFIGURATION_DIAGNOSTIC_LIMIT
        E_FIT_INTERNAL
        E_EMBEDDED_CONTENT E_EXTENSIONS E_FIELD_ATTRIBUTE E_FIELD_CAPABILITY E_FIELD_CYCLE E_FIELD_NAME
        E_FIELD_NO_WIDGET E_FIELD_PARENT E_FIELD_POLICY E_FIELD_TREE E_FIELD_TYPE E_FONT_FIXED_SIZE
        E_FONT_GLYPH E_FONT_MAPPING E_FONT_NOT_EMBEDDED E_FONT_PROGRAM E_FONT_RESOURCE E_INPUT_FILE
        E_INPUT_JSON E_JSON_DUPLICATE E_JSON_MEMBERS E_JSON_SIZE E_JSON_UNICODE E_JSON_UTF8
        E_NO_ACROFORM E_NO_FIELDS E_ORPHAN_WIDGET E_PDF_BOOLEAN E_PDF_ENCRYPTED E_PDF_INPUT E_PDF_LIMIT
        E_PDF_PARSE E_PDF_SIZE E_PERMISSION_UNKNOWN E_PROFILE_CAPABILITY E_PROFILE_CHECKBOX
        E_PROFILE_CONDITION E_PROFILE_DATE E_PROFILE_DATE_LAYOUT E_PROFILE_IDENTITY E_PROFILE_JSON E_PROFILE_KEY
        E_PROFILE_KIND E_PROFILE_KIND_MEMBER E_PROFILE_LIMIT E_PROFILE_MONEY E_PROFILE_NORMALIZATION
        E_PROFILE_NOT_RELEASED E_PROFILE_ONEOF E_PROFILE_PATTERN E_PROFILE_REVIEW_PENDING E_PROFILE_RULE_ID
        E_PROFILE_RULE_KIND E_PROFILE_RULE_OPERANDS E_PROFILE_RULE_SEVERITY E_PROFILE_SCHEMA E_PROFILE_SIZE
        E_PROFILE_SOURCE E_PROFILE_SPLIT E_PROFILE_TARGET_OWNER E_PROFILE_TARGETS E_PROFILE_UNMAPPED
        E_PROFILE_YEARS E_PROFILE_ZERO E_RELEASE_PROFILE_INVALID E_REVIEWED_STRUCTURE
        E_TARGET_ALIGNMENT E_TARGET_FLAGS E_TARGET_FONT E_TARGET_LENGTH E_TARGET_MISSING E_TARGET_OVERLAP
        E_TARGET_REQUIRED E_TARGET_TYPE E_TARGET_UNMAPPED E_TARGET_WIDGETS E_TEMPLATE_ALLOWLIST
        E_TEMPLATE_CONTRACT E_TEMPLATE_HASH E_TEMPLATE_NOT_REVIEWED E_TEMPLATE_PAGES E_TEMPLATE_PREFILLED
        E_TEMPLATE_PROFILE E_TEMPLATE_STRUCTURE E_TEMPLATE_TITLE E_UNMAPPED_UNSUPPORTED E_USAGE_RIGHTS_UNAPPROVED
        E_WIDGET_DUPLICATE E_WIDGET_GEOMETRY E_WIDGET_NOT_ON_PAGE E_WIDGET_PARENT E_XFA_UNSUPPORTED E_XFA_XML
        W_ACCEPTANCE_NOT_COMPLETE W_AUTHORING_VALIDATION_ONLY W_CATALOG_JAVASCRIPT_REMOVAL
        W_CHECKBOX_FONT_PORTABILITY_UNQUALIFIED W_CONFIGURATION_DRAFT_REVIEW W_FONT_NOT_EMBEDDED
        W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED W_NORMAL_OFF_MISSING W_RENDER_CHECKS_NOT_RUN
        E_ALL_OR_NONE E_CHECKBOX E_CONTEXT E_CONTEXT_QUARTER E_CONTEXT_SCHEDULE E_CONTEXT_SCHEDULE_B E_CONTEXT_YEAR
        E_DATA_LIMIT E_DATA_PROFILE E_DATA_SCHEMA E_DATE E_EQUAL E_FILING_YEAR E_INACTIVE_SECTION E_MONEY E_MONEY_NEGATIVE
        E_OPTION E_PATTERN E_PATTERN_TIMEOUT E_PROFILE_IDENTITY E_REQUIRED E_REQUIRED_IF E_SPLIT_CAPACITY E_SPLIT_LENGTH E_SUM
        E_TEXT_COMB_CAPACITY E_TEXT_COMB_OVERFLOW E_TEXT_CONTROL E_TEXT_LAYOUT E_TEXT_LENGTH E_TEXT_LIMIT E_TEXT_MAX_LENGTH
        E_TEXT_OVERFLOW E_TEXT_VERTICAL_OVERFLOW E_UNKNOWN_KEY E_VALUE_TYPE E_FONT_GLYPH_UNQUALIFIED E_FONT_OUTLINE_GLYPH
        E_SAMPLE_BUDGET E_SAMPLE_CONTEXT_REQUIRED E_SAMPLE_CONTEXT_RULE E_SAMPLE_CONTEXT_UNEXPECTED E_SAMPLE_FIT
        E_SAMPLE_INTERNAL E_SAMPLE_LIMIT E_SAMPLE_NO_CANDIDATE E_SAMPLE_PROFILE E_SAMPLE_RULE E_SAMPLE_RULE_BUDGET
        E_SAMPLE_RULE_UNSUPPORTED E_SAMPLE_UNSATISFIED E_FILL_INTERNAL E_VALIDATE_INTERNAL E_FORCE_UNQUALIFIED
        E_PUBLICATION_PREFLIGHT E_PUBLICATION W_CONFIGURATION_CLEANUP_INCOMPLETE W_OWNED_CLEANUP_INCOMPLETE W_SAMPLE_OPTIONAL_OMITTED
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static readonly HashSet<string> Features = new(StringComparer.Ordinal)
    {
        "field.radio", "field.pushbutton", "field.choice", "field.combo", "field.list", "field.signature", "field.unknown", "field.singleWidget",
        "field.readOnly", "field.noExport", "text.multiline", "text.richText", "text.password", "font.resource",
        "font.fixedSize1To72", "widget.rotation", "font.simpleType1OrTrueType", "font.supportedSourceEncoding",
        "font.validSourceProgram", "checkbox.normalExportState", "checkbox.fixedMarkGeometry", "form.acroform",
        "xfa.configuredAcroform", "active.action", "signature.configuredAcroform", "pdf.encryption"
    };
    private static readonly string[] Requirements = ["exactCatalogProfileBinding", "renderPolicy", "operatorReview"];
    private static readonly string[] Unassessed = ["businessMeaning", "suppliedValueFit", "viewerAcceptance", "physicalPrint", "filingReadiness", "staticPageBlankness"];

    public static CompatibilityDocument Project(ConfigurationCompatibility observed, string configurationState)
    {
        ArgumentNullException.ThrowIfNull(observed); State(configurationState);
        int exit = observed.ExitCode is >= 0 and <= 4 ? observed.ExitCode : 4;
        // Even a malformed internal assessment must not turn a retained/omitted error into success.
        if (exit == 0 && observed.Issues.Any(f => f.Severity == "error")) exit = 2;
        if (configurationState == "draft" && exit == 0) exit = 1;
        var issues = observed.Issues.Select(Issue).ToList();
        if (configurationState == "draft") issues.Add(new("E_CONFIGURATION_REVIEW_REQUIRED", "configuration.review"));
        var counts = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["pages"] = Math.Max(0, observed.Pages), ["terminalFields"] = observed.Fields.Count,
            ["textFields"] = observed.Fields.LongCount(f => f.Type == "text"),
            ["checkboxFields"] = observed.Fields.LongCount(f => f.Type == "checkbox"),
            ["otherFields"] = observed.Fields.LongCount(f => f.Type is not ("text" or "checkbox")),
            ["widgets"] = observed.Fields.Sum(f => (long)Math.Max(0, f.WidgetCount)),
            ["requiredFields"] = observed.Fields.LongCount(f => f.Required),
            ["combFields"] = observed.Fields.LongCount(f => f.Comb)
        };
        return new CompatibilityDocument
        {
            Status = Status(exit), ExitCode = exit,
            TemplateSha256 = Hash(observed.TemplateSha256) ? observed.TemplateSha256 : null,
            TemplateBytes = observed.TemplateBytes > 0 ? (long)observed.TemplateBytes : null,
            Counts = new ReadOnlyDictionary<string, long>(counts), ConfigurationState = configurationState,
            Issues = Ordered(issues),
            MissingEngineFeatures = observed.MissingEngineFeatures.Select(f => Features.Contains(f) ? f : "engine.unsupportedCapability")
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            RequiredConfiguration = observed.RequiredConfiguration.Where(Requirements.Contains)
                .Concat(exit == 1 ? Requirements : []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            NotAssessed = Unassessed.ToArray()
        };
    }

    public static CompatibilityDocument Failure(int exitCode, IEnumerable<Finding> findings, string configurationState,
        TemplateSnapshot? template = null)
    {
        State(configurationState);
        int exit = exitCode is >= 1 and <= 4 ? exitCode : 4;
        return new CompatibilityDocument
        {
            Status = Status(exit), ExitCode = exit, TemplateSha256 = template?.Sha256, TemplateBytes = template?.ByteCount,
            Counts = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(StringComparer.Ordinal)),
            ConfigurationState = configurationState, Issues = Ordered(findings.Select(Issue)),
            MissingEngineFeatures = [], RequiredConfiguration = exit == 1 ? Requirements.ToArray() : [], NotAssessed = Unassessed.ToArray()
        };
    }

    public static string CatalogState(ReadOnlySpan<byte> bytes)
    {
        var parsed = StrictJson.Parse<JsonObject>(bytes, "catalog");
        if (!parsed.IsValid) return "invalid";
        try
        {
            string? state = parsed.Value!["review"]?["state"]?.GetValue<string>();
            return state is "draft" or "reviewed" ? state : "invalid";
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or System.Text.Json.JsonException) { return "invalid"; }
    }

    private static ConfigurationIssue Issue(Finding finding)
    {
        string code = EngineCodes.Contains(finding.Code) && (finding.Severity == "warning"
                ? finding.Code.StartsWith("W_", StringComparison.Ordinal) : finding.Code.StartsWith("E_", StringComparison.Ordinal)) ? finding.Code
            : finding.Severity == "warning" ? "W_COMPATIBILITY_ASSESSMENT" : "E_COMPATIBILITY_ASSESSMENT";
        string capability = code switch
        {
            "E_TEMPLATE_PREFILLED" => "form.blankOriginal",
            "E_ACTIVE_CONTENT" or "W_CATALOG_JAVASCRIPT_REMOVAL" => "active.action",
            "E_APPROVAL_SIGNATURE" or "E_CERTIFICATION" or "E_PERMISSION_UNKNOWN" or "E_USAGE_RIGHTS_UNAPPROVED" => "signature.configuredAcroform",
            "E_XFA_UNSUPPORTED" or "E_XFA_XML" => "xfa.configuredAcroform",
            "E_PDF_ENCRYPTED" => "pdf.encryption",
            "E_CONFIGURATION_REVIEW_REQUIRED" or "E_CONFIGURATION_UNRESOLVED" or "E_CATALOG_REVIEW_REQUIRED" or "W_CONFIGURATION_DRAFT_REVIEW" => "configuration.review",
            "E_CATALOG_PROFILE_HASH" or "E_CATALOG_PROFILE_IDENTITY" => "configuration.profile",
            "E_CATALOG_TEMPLATE_HASH" => "template.identity",
            "E_CATALOG_TARGET_COVERAGE" or "E_CATALOG_TARGET_REQUIRED" or "E_CATALOG_TARGET_TYPE" or "E_CATALOG_TARGET_PAGE" => "field.structure",
            "E_CATALOG_ON_STATE" => "checkbox.mark",
            "E_CATALOG_DATE_LAYOUT_REQUIRED" or "E_CATALOG_DATE_LAYOUT_CONFLICT" or "E_CATALOG_DATE_GEOMETRY" => "text.layout",
            _ when code.StartsWith("E_CONFIG_FONT_", StringComparison.Ordinal) || code.StartsWith("E_FONT_", StringComparison.Ordinal) || code.StartsWith("W_FONT_", StringComparison.Ordinal) => "font.source",
            _ when code.StartsWith("E_CONFIG_CHECKBOX_", StringComparison.Ordinal) || code == "E_CHECKBOX_ON_STATE" => "checkbox.mark",
            _ when code.StartsWith("E_CONFIG_TEXT_", StringComparison.Ordinal) || code == "E_CONFIG_DATE_LAYOUT" => "text.layout",
            _ when code.StartsWith("E_TEMPLATE_", StringComparison.Ordinal) || code == "E_CONFIGURATION_TEMPLATE" => "template.identity",
            _ when code.StartsWith("E_PROFILE_", StringComparison.Ordinal) => "configuration.profile",
            _ when code.StartsWith("E_FIELD_", StringComparison.Ordinal) || code.StartsWith("E_WIDGET_", StringComparison.Ordinal) || code.StartsWith("E_TARGET_", StringComparison.Ordinal) => "field.structure",
            _ when code.StartsWith("E_PDF_", StringComparison.Ordinal) || code.StartsWith("E_JSON_", StringComparison.Ordinal) || code is "E_INPUT_JSON" or "E_INPUT_FILE" or "E_ARGUMENTS" => "input.syntax",
            _ => "configuration.assessment"
        };
        string? field = Field(finding.Key) ? finding.Key : Field(finding.Field) ? finding.Field : null;
        if (field is null && finding.Field is not null)
        { try { field = CliDraftKeys.FromCanonicalField(finding.Field); } catch (ArgumentException) { } }
        return new(code, capability, field);
    }
    internal static bool IsKnownEngineCode(string code) => EngineCodes.Contains(code);
    private static ConfigurationIssue[] Ordered(IEnumerable<ConfigurationIssue> issues)
    {
        var ordered = issues.Distinct().OrderByDescending(i => i.Code.StartsWith("E_", StringComparison.Ordinal))
            .ThenBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.Capability, StringComparer.Ordinal)
            .ThenBy(i => i.FieldId ?? "", StringComparer.Ordinal).ToArray();
        if (ordered.Length <= 1000) return ordered;
        bool blocking = ordered.Any(i => i.Code.StartsWith("E_", StringComparison.Ordinal));
        return ordered.Take(999).Append(new ConfigurationIssue(blocking ? "E_COMPATIBILITY_ISSUE_LIMIT" : "W_COMPATIBILITY_ISSUE_LIMIT",
            "configuration.assessment")).ToArray();
    }
    private static bool Field(string? value) => value is { Length: 70 } && value.StartsWith("field_", StringComparison.Ordinal) && Hash(value[6..]);
    private static bool Hash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Status(int exit) => exit switch { 0 => "supported", 1 => "configuration_needed", 2 => "invalid", 3 => "unsupported", _ => "internal_error" };
    private static void State(string state)
    { if (state is not ("absent" or "draft" or "reviewed" or "invalid")) throw new ArgumentException("E_CONFIGURATION_REVIEW_STATE"); }
}
