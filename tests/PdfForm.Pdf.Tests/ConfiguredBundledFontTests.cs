using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using PdfForm.Core;
using PdfForm.Pdf;

// Source-only integration controls. Root owns runner wiring and separately
// admitted execution against the complete frozen Core/inspector successors.
internal static class ConfiguredBundledFontTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(ScheduleBEmbeddedFont.Supports(32) && ScheduleBEmbeddedFont.Supports(126)
            && ScheduleBEmbeddedFont.Supports(0x00c9) && !ScheduleBEmbeddedFont.Supports(0x00e9)
            && !ScheduleBEmbeddedFont.Supports(31) && !ScheduleBEmbeddedFont.Supports(127),
            "configured strategy shares the accepted engine-fixed ASCII plus U+00C9 repertoire");
        // Diagnostic-only batching: retain the same eight independent positive
        // preparations before the first fail-fast assertion. No oracle is relaxed.
        var controls = new List<(float Size, TemplateSnapshot Template, FormConfigurationSnapshot Configuration,
            Dictionary<string, string> Values, VerifiedFillResult Populated, VerifiedFillResult Blank)>();
        foreach (float size in new[] { 1f, 10, 12.5f, 72 })
        {
            var template = SyntheticTemplate(size, size);
            var configuration = Configuration(template);
            var keys = configuration.ParseProfile().Keys;
            var values = keys.ToDictionary(key => key.Key, _ => "AÉ", StringComparer.Ordinal);
            var populated = VerifiedFillPipeline.Prepare(template, configuration, Data(configuration, values), flatten: false);
            EmitPreparationDiagnostic(size, "populated", populated);
            var blank = VerifiedFillPipeline.Prepare(template, configuration,
                Data(configuration, keys.ToDictionary(key => key.Key, _ => "", StringComparer.Ordinal)), flatten: false);
            EmitPreparationDiagnostic(size, "blank", blank);
            controls.Add((size, template, configuration, values, populated, blank));
        }
        // One additional in-memory renderer probe diagnoses the shared guard;
        // it does not replace any of the eight public Prepare results or checks.
        if (controls[0].Populated.Findings.Any(f => f.Code == "E_FILL_ASSIGNMENTS"))
            EmitAssignmentGuardDiagnostic(controls[0].Template, controls[0].Configuration, controls[0].Values);
        CheckCanonicalFieldControls(check, controls[0].Template, controls[0].Configuration, controls[0].Values);
        foreach (var control in controls)
        {
            float size = control.Size;
            var template = control.Template;
            var configuration = control.Configuration;
            var keys = configuration.ParseProfile().Keys;
            var values = control.Values;
            var prepared = control.Populated;
            check(prepared.ReadyToPublish && prepared.ExitCode == 0
                && prepared.Findings.Any(f => f.Code == "W_FONT_EXPLICIT_REPLACEMENT" && f.Severity == "warning"),
                "configured pinned-font rendering and explicit warning: " + size);
            if (prepared.Candidate is not { } candidate) continue;
            check(candidate.ManifestForFixture.Targets.Values.All(target => target.Font is
                { Policy: ScheduleBEmbeddedFont.ConfiguredPolicy, PrivateUnembeddedException: false } font
                && font.Size == size && font.EmbeddedDigest == ScheduleBEmbeddedFont.ProgramSha256),
                "every witness retains the exact pinned identity and observed source size: " + size);
            VerifySharedProgram(check, candidate, "populated " + size);

            var blank = control.Blank;
            check(blank.ReadyToPublish, "blank configured targets retain independently verified editable defaults: " + size);
            if (blank.Candidate is { } blankCandidate)
            {
                VerifySharedProgram(check, blankCandidate, "blank " + size);
                if (size == 12.5f) RejectDefaultMutations(check, blankCandidate);
            }
            var fit = VerifiedFillPipeline.CheckValueFit(template, configuration, keys[0].Key, JsonSerializer.SerializeToElement("AÉ"));
            check(fit.All(f => f.Severity != "error"), "per-value fit paints using the same pinned implementation: " + size);
            if (size == 10) RejectSourceIdentityClaims(check, template, configuration, values);
            var unsupported = VerifiedFillPipeline.CheckValueFit(template, configuration, keys[0].Key, JsonSerializer.SerializeToElement("é"));
            check(unsupported.Any(f => f.Code == "E_FONT_GLYPH_UNQUALIFIED"),
                "bundled glyph inventory cannot broaden the accepted repertoire: " + size);
            if (size == 72)
            {
                var overflow = VerifiedFillPipeline.CheckValueFit(template, configuration, keys[0].Key,
                    JsonSerializer.SerializeToElement(new string('W', 64)));
                check(overflow.Any(f => f.Code == "E_TEXT_OVERFLOW"), "configured fit rejects overflow without shrinking or fallback");
            }
            var policy = configuration.ParsePolicy();
            double originalLeft = policy.TextFields[0].Rectangle[0];
            policy.TextFields[0].Rectangle[0] += 100;
            check(configuration.ParsePolicy().TextFields[0].Rectangle[0] == originalLeft,
                "configured placement trees cannot mutate the sealed source identity");
        }

        var mixed = SyntheticTemplate(10, 12.5f);
        var mixedConfiguration = Configuration(mixed);
        var mixedValues = mixedConfiguration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "AÉ", StringComparer.Ordinal);
        var mixedResult = VerifiedFillPipeline.Prepare(mixed, mixedConfiguration, Data(mixedConfiguration, mixedValues), flatten: false);
        check(mixedResult.ReadyToPublish && mixedResult.Candidate!.ManifestForFixture.Targets.Values
            .Select(target => target.Font!.Size).Order().SequenceEqual(new[] { 10f, 12.5f }),
            "one document may share one font program while retaining distinct field-local fixed sizes");
        if (mixedResult.Candidate is { } mixedCandidate) VerifySharedProgram(check, mixedCandidate, "mixed sizes");
        var flattened = VerifiedFillPipeline.Prepare(mixed, mixedConfiguration, Data(mixedConfiguration, mixedValues), flatten: true);
        check(flattened.ReadyToPublish && flattened.Candidate!.Flattened,
            "configured pinned font preserves editable verification before flattening");

        using var first = new PdfDocument(new PdfWriter(new MemoryStream()));
        using var second = new PdfDocument(new PdfWriter(new MemoryStream()));
        var shared = new ScheduleBEmbeddedFont.DocumentFont(first);
        string? crossDocumentCode = null;
        try { shared.Get(second, out _); } catch (VerifiedFillException error) { crossDocumentCode = error.Code; }
        check(crossDocumentCode == "E_FONT_DOCUMENT", "the shared font cannot leak into another authoring document");
        first.AddNewPage(); second.AddNewPage();
    }

    // Independent identities keep this fixture discriminating: a typed parent
    // contains two named leaf fields, one inheriting /FT and one declaring it.
    private static void CheckCanonicalFieldControls(Action<bool, string> check, TemplateSnapshot template,
        FormConfigurationSnapshot configuration, Dictionary<string, string> values)
    {
        var binding = new TemplateBinder().Bind(template, configuration);
        var parsed = DataValidator.ParseData(Data(configuration, values));
        if (!binding.Bound || !parsed.IsValid || parsed.Value is not { } data)
            throw new InvalidOperationException("canonical-control-admission");
        var validation = DataValidator.Validate(configuration.ParseProfile(), data);
        if (!validation.IsValid) throw new InvalidOperationException("canonical-control-data");
        var assignments = AssignmentPlanner.Build(validation);
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(template))))
        {
            var form = PdfAcroForm.GetAcroForm(pdf, false)!;
            var all = form.GetAllFormFields();
            var parent = all["group"];
            var first = all["group.amountA"];
            var second = all["group.amountB"];
            check(all.Count == 3 && PdfName.Tx.Equals(parent.GetPdfObject().GetAsName(PdfName.FT))
                && parent.GetChildFormFields().Count == 2
                && parent.GetChildFormFields().All(child => child.GetPdfObject().GetAsString(PdfName.T) is not null)
                && !first.GetPdfObject().ContainsKey(PdfName.FT)
                && PdfName.Tx.Equals(VerifiedFormRenderer.Inherited(first.GetPdfObject(), PdfName.FT, null))
                && PdfName.Tx.Equals(second.GetPdfObject().GetAsName(PdfName.FT)),
                "canonical fixture retains typed parent and inherited leaf type");
            var expected = new HashSet<string>(["group.amountA", "group.amountB"], StringComparer.Ordinal);
            var localFt = all.Where(pair => pair.Value.IsTerminalFormField()).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
            check(expected.SetEquals(Fields(form).Keys) && expected.SetEquals(binding.Inspection.NamedFields.Keys)
                && expected.SetEquals(assignments.Select(assignment => assignment.Field))
                && localFt.Count == 2 && localFt.Contains("group") && !localFt.Contains("group.amountA")
                && !expected.SetEquals(localFt),
                "canonical childless set agrees with independent inspection and excludes local-type parent");
        }
        bool Refused(IReadOnlyList<FieldAssignment> altered, string expectedMessage)
        {
            try { _ = VerifiedFormRenderer.Render(template, configuration, binding, altered, flatten: false); return false; }
            catch (VerifiedFillException error)
            {
                return error.Code == "E_FILL_ASSIGNMENTS" && error.ExitCode == 4 && error.SafeMessage == expectedMessage;
            }
        }
        const string coverage = "The assignment manifest must cover every terminal field exactly once.";
        check(Refused(assignments.Where(assignment => assignment.Field != "group.amountA").ToArray(), coverage),
            "canonical coverage refuses missing inherited leaf assignment");
        check(Refused([assignments[0], assignments[0]], coverage),
            "canonical coverage refuses duplicate leaf assignment");
        check(Refused([assignments[0] with { Field = "group" }, assignments[1]],
                "The assignment manifest and canonical field inventory disagree."),
            "canonical inventory refuses equal-count distinct parent substitution");
    }

    // This temporary test diagnostic emits only bounded enums/numbers and a
    // closed set of fixed product finding codes. No finding text, keys, fields,
    // values, paths, PDF bytes or arbitrary exception messages are emitted.
    private static readonly HashSet<string> DiagnosticCodes = new("""
            E_ACTIVE_CONTENT E_ALL_OR_NONE E_APPROVAL_SIGNATURE E_ASSIGNMENT_COVERAGE E_ASSIGNMENT_VALIDATION E_CALENDAR_DAY
            E_CATALOG_BUILTIN_IDENTITY E_CATALOG_DATE_GEOMETRY E_CATALOG_DATE_LAYOUT_CONFLICT E_CATALOG_DATE_LAYOUT_REQUIRED E_CATALOG_FONT E_CATALOG_ISSUES
            E_CATALOG_MARK_GEOMETRY E_CATALOG_ON_STATE E_CATALOG_POLICY_UNSUPPORTED E_CATALOG_PROFILE E_CATALOG_PROFILE_HASH E_CATALOG_PROFILE_IDENTITY
            E_CATALOG_REVIEW E_CATALOG_REVIEW_REQUIRED E_CATALOG_SCHEMA E_CATALOG_SNAPSHOT E_CATALOG_TARGETS E_CATALOG_TARGET_COVERAGE
            E_CATALOG_TARGET_PAGE E_CATALOG_TARGET_REQUIRED E_CATALOG_TARGET_TYPE E_CATALOG_TEMPLATE E_CATALOG_TEMPLATE_HASH E_CERTIFICATION
            E_CHECKBOX E_CHECKBOX_ON_STATE E_CHECKBOX_VECTOR_GEOMETRY E_CHECKBOX_VECTOR_SCOPE E_CHECKBOX_VECTOR_STRATEGY E_CONFIGURATION_FIELD_ID
            E_CONFIGURATION_POLICY E_CONFIGURATION_TEMPLATE E_CONFIG_CHECKBOX_GEOMETRY E_CONFIG_CHECKBOX_TARGET E_CONFIG_DATE_LAYOUT E_CONFIG_FONT_IDENTITY
            E_CONFIG_FONT_PROGRAM E_CONFIG_FONT_SIZE E_CONFIG_FONT_STRATEGY E_CONFIG_FONT_STRUCTURE E_CONFIG_TEXT_LAYOUT E_CONFIG_TEXT_TARGET
            E_CONFIG_WIDGET_ATTRIBUTES E_CONFIG_WIDGET_BOUNDS E_CONFIG_WIDGET_ROTATION E_CONTEXT E_CONTEXT_QUARTER E_CONTEXT_SCHEDULE
            E_CONTEXT_SCHEDULE_B E_CONTEXT_YEAR E_DATA_LIMIT E_DATA_PROFILE E_DATA_SCHEMA E_DATA_SCHEMA_PAIR
            E_DATE E_DATE_FONT_PROGRAM E_DATE_GLYPH_BOUNDS E_DATE_LAYOUT_BOUNDS E_DATE_LAYOUT_GEOMETRY E_DATE_LAYOUT_HASH
            E_DATE_LAYOUT_REQUIRED E_DATE_LAYOUT_TARGET E_DATE_LAYOUT_UNREVIEWED E_DATE_LAYOUT_UNSUPPORTED E_DATE_LAYOUT_VALUE E_EMBEDDED_CONTENT
            E_EQUAL E_EXTENSIONS E_FIELD_ATTRIBUTE E_FIELD_CAPABILITY E_FIELD_CYCLE E_FIELD_NAME
            E_FIELD_NO_WIDGET E_FIELD_PARENT E_FIELD_POLICY E_FIELD_TREE E_FIELD_TYPE E_FILING_YEAR
            E_FILL_ASSIGNMENTS E_FILL_BINDING E_FILL_CHECKBOX_POLICY E_FILL_FIELD_TREE E_FILL_FIELD_TYPE E_FILL_FORM
            E_FILL_GEOMETRY E_FILL_INTERNAL E_FILL_NOT_VERIFIED E_FILL_ON_AP E_FILL_ON_STATE E_FILL_OUTPUT_LIMIT
            E_FILL_PAGE_TRANSFORM E_FILL_POLICY_COVERAGE E_FILL_POLICY_TARGET E_FILL_TEMPLATE E_FILL_WIDGETS E_FILL_WIDGET_ATTRIBUTES
            E_FIT_INTERNAL E_FIT_KEY E_FLATTEN_FORM E_FONT_ASSET E_FONT_DOCUMENT E_FONT_EMBEDDED_POLICY
            E_FONT_FIXED_SIZE E_FONT_GLYPH E_FONT_GLYPH_UNQUALIFIED E_FONT_IMPORT E_FONT_MAPPING E_FONT_METRICS
            E_FONT_NOT_EMBEDDED E_FONT_OUTLINE_GLYPH E_FONT_PROGRAM E_FONT_PROGRAM_LIMIT E_FONT_RESOURCE E_FONT_RESOURCE_IDENTITY
            E_FONT_SEED E_FONT_SEED_LIMIT E_FONT_STRATEGY E_INACTIVE_SECTION E_INPUT_JSON E_JOB_DOCUMENTS
            E_JOB_OUTPUT E_JOB_POLICY E_JOB_SCHEMA E_JOB_SOURCE E_JSON_DUPLICATE E_JSON_MEMBERS
            E_JSON_SIZE E_JSON_UNICODE E_JSON_UTF8 E_MONEY E_MONEY_NEGATIVE E_NORMALIZATION_FORM
            E_NORMALIZATION_UNAPPROVED E_NORMALIZATION_VERIFY E_NO_ACROFORM E_NO_FIELDS E_OPTION E_ORPHAN_WIDGET
            E_PACKET_CONTEXT E_PACKET_FLATTEN E_PACKET_IDENTITY E_PACKET_INPUT E_PACKET_PAGES E_PACKET_PROFILE
            E_PACKET_SCHEDULE_B E_PACKET_TOTAL E_PATTERN E_PATTERN_TIMEOUT E_PDF_BOOLEAN E_PDF_ENCRYPTED
            E_PDF_INPUT E_PDF_LIMIT E_PDF_PARSE E_PDF_SIZE E_PERMISSION_UNKNOWN E_PROFILE_CAPABILITY
            E_PROFILE_CHECKBOX E_PROFILE_CONDITION E_PROFILE_DATE E_PROFILE_DATE_LAYOUT E_PROFILE_IDENTITY E_PROFILE_JSON
            E_PROFILE_KEY E_PROFILE_KIND E_PROFILE_KIND_MEMBER E_PROFILE_LIMIT E_PROFILE_MONEY E_PROFILE_NORMALIZATION
            E_PROFILE_NOT_RELEASED E_PROFILE_ONEOF E_PROFILE_PATTERN E_PROFILE_REVIEW_PENDING E_PROFILE_RULE_ID E_PROFILE_RULE_KIND
            E_PROFILE_RULE_OPERANDS E_PROFILE_RULE_SEVERITY E_PROFILE_SAMPLE_HINT E_PROFILE_SCHEMA E_PROFILE_SIZE E_PROFILE_SNAPSHOT
            E_PROFILE_SOURCE E_PROFILE_SPLIT E_PROFILE_TARGETS E_PROFILE_TARGET_OWNER E_PROFILE_UNMAPPED E_PROFILE_YEARS
            E_PROFILE_ZERO E_RELEASE_PROFILE_INVALID E_REQUEST_ID E_REQUIRED E_REQUIRED_IF E_REVIEWED_STRUCTURE
            E_SPLIT_CAPACITY E_SPLIT_LENGTH E_SUM E_TARGET_ALIGNMENT E_TARGET_FLAGS E_TARGET_FONT
            E_TARGET_LENGTH E_TARGET_MISSING E_TARGET_OVERLAP E_TARGET_REQUIRED E_TARGET_TYPE E_TARGET_UNMAPPED
            E_TARGET_WIDGETS E_TEMPLATE_ALLOWLIST E_TEMPLATE_CONTRACT E_TEMPLATE_HASH E_TEMPLATE_NOT_REVIEWED E_TEMPLATE_PAGES
            E_TEMPLATE_PREFILLED E_TEMPLATE_PROFILE E_TEMPLATE_STRUCTURE E_TEMPLATE_TITLE E_TEXT_COMB_CAPACITY E_TEXT_COMB_OVERFLOW
            E_TEXT_CONTROL E_TEXT_LAYOUT E_TEXT_LENGTH E_TEXT_LIMIT E_TEXT_MAX_LENGTH E_TEXT_OVERFLOW
            E_TEXT_VERTICAL_OVERFLOW E_UNKNOWN_KEY E_UNMAPPED_UNSUPPORTED E_USAGE_RIGHTS_UNAPPROVED E_VALUE_TYPE E_VERIFY_ACTIVE_CONTENT
            E_VERIFY_ALTERNATE_AP E_VERIFY_ANCESTOR_VALUE E_VERIFY_AP_BBOX E_VERIFY_AP_CLIP E_VERIFY_AP_CLIPPED_INK E_VERIFY_AP_COLOR
            E_VERIFY_AP_ENCODING E_VERIFY_AP_FONT E_VERIFY_AP_FONT_MAPPING E_VERIFY_AP_FONT_PROGRAM E_VERIFY_AP_GLYPHS E_VERIFY_AP_IMAGE
            E_VERIFY_AP_INK_BOUNDS E_VERIFY_AP_OPERATOR E_VERIFY_AP_ORIENTATION E_VERIFY_AP_OUTLINE E_VERIFY_AP_RESOURCE_CYCLE E_VERIFY_AP_RESOURCE_LIMIT
            E_VERIFY_AP_SCALE E_VERIFY_AP_STATE E_VERIFY_AP_TEXT E_VERIFY_AP_TEXT_STATE E_VERIFY_AP_TRANSFORM E_VERIFY_AP_VISIBILITY
            E_VERIFY_CHECKBOX_AP E_VERIFY_CHECKBOX_BBOX E_VERIFY_CHECKBOX_DICTIONARY E_VERIFY_CHECKBOX_POLICY E_VERIFY_CHECKBOX_RESOURCES E_VERIFY_CHECKBOX_STATE
            E_VERIFY_CHECKBOX_VECTOR E_VERIFY_COMB_CAPACITY E_VERIFY_COMB_CELL E_VERIFY_CONTENT_LIMIT E_VERIFY_DATE_CELL E_VERIFY_DATE_GLYPHS
            E_VERIFY_DATE_POSITION E_VERIFY_FIELD_ATTRIBUTES E_VERIFY_FIELD_FONT_DEFAULT E_VERIFY_FIELD_FONT_RESOURCE E_VERIFY_FIELD_SET E_VERIFY_FIELD_TREE
            E_VERIFY_FIELD_TYPE E_VERIFY_FINAL_ACTIVE_CONTENT E_VERIFY_FINAL_FIELDS E_VERIFY_FINAL_WIDGETS E_VERIFY_FLATTEN_AP E_VERIFY_FLATTEN_CHECKBOX_RESOURCE
            E_VERIFY_FLATTEN_MARKED_CONTENT E_VERIFY_FLATTEN_MATRIX E_VERIFY_FLATTEN_OPERATOR E_VERIFY_FLATTEN_PAINT E_VERIFY_FLATTEN_PAINT_MISSING E_VERIFY_FLATTEN_PLACEMENT
            E_VERIFY_FLATTEN_STATE E_VERIFY_FLATTEN_WITNESS E_VERIFY_FONT_LIMIT E_VERIFY_FONT_POLICY E_VERIFY_FORM E_VERIFY_METADATA
            E_VERIFY_NEED_APPEARANCES E_VERIFY_OFF_AP E_VERIFY_ON_AP_CHANGED E_VERIFY_ORPHAN_WIDGET E_VERIFY_PAGE_CONTENT E_VERIFY_PAGE_COUNT
            E_VERIFY_PAGE_GEOMETRY E_VERIFY_RESET_DEFAULT E_VERIFY_RICH_VALUE E_VERIFY_TEXT_AP E_VERIFY_TEXT_STATE E_VERIFY_TEXT_VALUE
            E_VERIFY_WIDGETS E_VERIFY_WIDGET_ALIAS E_VERIFY_WIDGET_ATTRIBUTES E_VERIFY_WIDGET_GEOMETRY E_VERIFY_WIDGET_LINKAGE E_VERIFY_WIDGET_PAGE
            E_VERIFY_WIDGET_VALUE E_VERIFY_WIDGET_VISIBILITY E_WIDGET_DUPLICATE E_WIDGET_GEOMETRY E_WIDGET_NOT_ON_PAGE E_WIDGET_PARENT
            E_XFA_UNSUPPORTED E_XFA_XML W_ACCEPTANCE_NOT_COMPLETE W_CATALOG_JAVASCRIPT_REMOVAL W_CHECKBOX_FONT_PORTABILITY_UNQUALIFIED W_FONT_EXPLICIT_REPLACEMENT
            W_FONT_NOT_EMBEDDED W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED W_NORMAL_OFF_MISSING
            """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    private static void EmitPreparationDiagnostic(float size, string scenario, VerifiedFillResult result)
    {
        static string Stage(string stage) => stage is "syntax" or "profile" or "binding" or "bindingStructure"
            or "data" or "packet" or "render" or "internal" ? stage : "unknown_stage";
        var findings = result.Findings.Take(32).Select(f => new
        {
            code = DiagnosticCodes.Contains(f.Code) ? f.Code : "unknown_code",
            stage = Stage(f.Stage), severity = f.Severity is "warning" or "error" ? f.Severity : "unknown_severity"
        }).ToArray();
        Console.Error.WriteLine("DIAG_CONFIGURED_FONT " + JsonSerializer.Serialize(new
        {
            schema = "pdfform.configured-font-diagnostic/1", size,
            scenario = scenario is "populated" or "blank" ? scenario : "unknown_scenario",
            exitCode = result.ExitCode is >= 0 and <= 4 ? result.ExitCode : -1,
            ready = result.ReadyToPublish, findingCount = result.Findings.Count,
            findings, truncated = result.Findings.Count > 32
        }));
    }

    // This probe is limited to the first existing synthetic populated control.
    // It adds one direct Render call solely to classify the constant internal
    // guard message that the public configured API deliberately suppresses.
    private static void EmitAssignmentGuardDiagnostic(TemplateSnapshot template,
        FormConfigurationSnapshot configuration, Dictionary<string, string> values)
    {
        string stage = "parse", outcome = "not_run", guard = "not_reached", errorCode = "none";
        int errorExit = -1, assignmentCount = -1, assignmentDistinctCount = -1, inspectedCount = -1,
            inspectedNamedCount = -1, allWrapperCount = -1, localFtCount = -1, canonicalLeafCount = -1,
            localFtNonLeafCount = -1, inheritedFtLeafCount = -1;
        bool bound = false, dataValid = false, assignmentInspectionEqual = false,
            assignmentLocalFtEqual = false, assignmentCanonicalLeafEqual = false,
            inspectionCanonicalLeafEqual = false;
        try
        {
            var parsed = DataValidator.ParseData(Data(configuration, values));
            if (!parsed.IsValid || parsed.Value is not { } data)
                throw new InvalidOperationException("PROBE_PARSE_REFUSED");
            stage = "validate";
            var profile = configuration.ParseProfile();
            var validation = DataValidator.Validate(profile, data);
            dataValid = validation.IsValid;
            if (!dataValid) throw new InvalidOperationException("PROBE_DATA_REFUSED");
            stage = "bind";
            var binding = new TemplateBinder().Bind(template, configuration);
            bound = binding.Bound;
            if (!bound) throw new InvalidOperationException("PROBE_BINDING_REFUSED");
            stage = "assignments";
            var assignments = AssignmentPlanner.Build(validation);
            var assignedNames = assignments.Select(a => a.Field).ToHashSet(StringComparer.Ordinal);
            assignmentCount = assignments.Count;
            assignmentDistinctCount = assignedNames.Count;
            inspectedCount = binding.Inspection.TerminalFieldCount;
            inspectedNamedCount = binding.Inspection.NamedFields.Count;
            assignmentInspectionEqual = assignedNames.SetEquals(binding.Inspection.NamedFields.Keys);
            stage = "normalized_inventory";
            using (var output = new BoundedPdfMemoryStream())
            using (var pdf = ApprovedTemplateNormalizer.OpenForNormalization(template, output, configuration))
            {
                var form = PdfAcroForm.GetAcroForm(pdf, false)
                    ?? throw new InvalidOperationException("PROBE_FORM_MISSING");
                var all = form.GetAllFormFields();
                if (all.Count > Limits.MaxKeysOrTargets)
                    throw new InvalidOperationException("PROBE_INVENTORY_LIMIT");
                var localFt = all.Where(pair => pair.Value.IsTerminalFormField()).ToArray();
                var leaves = all.Where(pair => pair.Value.GetChildFormFields().Count == 0).ToArray();
                allWrapperCount = all.Count;
                localFtCount = localFt.Length;
                canonicalLeafCount = leaves.Length;
                localFtNonLeafCount = localFt.Count(pair => pair.Value.GetChildFormFields().Count != 0);
                inheritedFtLeafCount = leaves.Count(pair => !pair.Value.GetPdfObject().ContainsKey(PdfName.FT)
                    && VerifiedFormRenderer.Inherited(pair.Value.GetPdfObject(), PdfName.FT, null) is PdfName);
                assignmentLocalFtEqual = assignedNames.SetEquals(localFt.Select(pair => pair.Key));
                assignmentCanonicalLeafEqual = assignedNames.SetEquals(leaves.Select(pair => pair.Key));
                inspectionCanonicalLeafEqual = binding.Inspection.NamedFields.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(leaves.Select(pair => pair.Key));
            }
            stage = "render";
            _ = VerifiedFormRenderer.Render(template, configuration, binding, assignments, flatten: false);
            outcome = "render_returned";
            guard = "no_assignment_guard";
        }
        catch (VerifiedFillException error)
        {
            outcome = "verified_exception";
            errorCode = DiagnosticCodes.Contains(error.Code) ? error.Code : "unknown_code";
            errorExit = error.ExitCode is >= 0 and <= 4 ? error.ExitCode : -1;
            guard = stage == "render" && error.Code == "E_FILL_ASSIGNMENTS" && error.ExitCode == 4
                ? error.SafeMessage switch
                {
                    "The assignment manifest must cover every terminal field exactly once." => "initial_coverage",
                    "The assignment manifest and canonical field inventory disagree." => "normalized_inventory",
                    "Replacement requires every canonical field exactly once." => "replacement_inventory",
                    _ => "unmapped_assignment_guard"
                } : "other_verified_guard";
        }
        catch
        {
            outcome = "probe_exception"; // Never serialize arbitrary exception text/type or input identifiers.
        }
        Console.Error.WriteLine("DIAG_ASSIGNMENT_GUARD " + JsonSerializer.Serialize(new
        {
            schema = "pdfform.assignment-guard-diagnostic/1", size = 1, scenario = "populated",
            stage, outcome, guard, errorCode, errorExit, bound, dataValid,
            assignmentCount, assignmentDistinctCount, inspectedCount, inspectedNamedCount,
            allWrapperCount, localFtCount, canonicalLeafCount, localFtNonLeafCount, inheritedFtLeafCount,
            assignmentInspectionEqual, assignmentLocalFtEqual, assignmentCanonicalLeafEqual,
            inspectionCanonicalLeafEqual
        }));
    }

    // Additive v6 registration: RunExtensions(check, root). Exactly56 healthy-
    // path checks:24 font/AP controls +12 overflow controls +18 date controls +2 blank-default controls.
    internal static void RunExtensions(Action<bool, string> check, string root)
    {
        foreach (string strategy in new[] { "preserve-embedded-simple-v1", "bundled-liberation-serif-ascii-v1" })
        foreach (float size in new[] { 1f, 10, 72 })
        {
            var template = ExtensionTemplate(root, strategy, size);
            var configuration = ExtensionConfiguration(template, strategy);
            var values = configuration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "11", StringComparer.Ordinal);
            foreach (bool flatten in new[] { false, true })
            {
                var result = VerifiedFillPipeline.Prepare(template, configuration, Data(configuration, values), flatten);
                check(result.ReadyToPublish && result.Candidate is { } candidate
                    && ExtensionExactFonts(candidate, configuration, size, flatten),
                    "v6/exact-appearance-font/" + strategy + "/" + size + "/" + flatten);
                var ready = result.Candidate ?? throw new InvalidOperationException("Positive font control failed.");
                check(ExtensionRejectFontSwap(ready, root, strategy, flatten),
                    "v6/appearance-only-font-swap/" + strategy + "/" + size + "/" + flatten);
            }

            var narrow = ExtensionTemplate(root, strategy, size, narrow: true);
            var narrowConfiguration = ExtensionConfiguration(narrow, strategy);
            var one = narrowConfiguration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "W", StringComparer.Ordinal);
            var fitting = VerifiedFillPipeline.Prepare(narrow, narrowConfiguration, Data(narrowConfiguration, one), flatten: false);
            check(fitting.ReadyToPublish && fitting.Candidate is { } fittingCandidate
                && ExtensionExactFonts(fittingCandidate, narrowConfiguration, size, flatten: false),
                "v6/narrow-positive/" + strategy + "/" + size);
            var overlong = one.ToDictionary(pair => pair.Key, _ => new string('W', 64), StringComparer.Ordinal);
            var overflow = VerifiedFillPipeline.Prepare(narrow, narrowConfiguration, Data(narrowConfiguration, overlong), flatten: false);
            check(!overflow.ReadyToPublish && overflow.Findings.Any(f => f.Code == "E_TEXT_OVERFLOW"),
                "v6/overflow-without-shrink/" + strategy + "/" + size);

            foreach (bool eightCells in new[] { false, true })
            {
                var dateTemplate = ExtensionTemplate(root, strategy, size, dates: true);
                var dateConfiguration = ExtensionConfiguration(dateTemplate, strategy, dates: true, eightCells: eightCells);
                var dates = dateConfiguration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "2026-10-20", StringComparer.Ordinal);
                var dateResult = VerifiedFillPipeline.Prepare(dateTemplate, dateConfiguration, Data(dateConfiguration, dates), flatten: false);
                check(dateResult.ReadyToPublish && dateResult.Candidate is { } dateCandidate
                    && ExtensionExactFonts(dateCandidate, dateConfiguration, size, flatten: false)
                    && ExtensionDatePaint(dateCandidate, eightCells),
                    "v6/date-paint/" + strategy + "/" + size + "/" + eightCells);
                if (eightCells)
                    check(ExtensionRejectCellShift(dateResult.Candidate ?? throw new InvalidOperationException("Positive date control failed.")),
                        "v6/date-cell-position/" + strategy + "/" + size);
            }
        }
        foreach (float size in new[] { 1f, 72 })
        {
            var template = ExtensionTemplate(root, ScheduleBEmbeddedFont.ConfiguredPolicy, size);
            var configuration = ExtensionConfiguration(template, ScheduleBEmbeddedFont.ConfiguredPolicy);
            var values = configuration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "", StringComparer.Ordinal);
            var blank = VerifiedFillPipeline.Prepare(template, configuration, Data(configuration, values), flatten: false);
            check(blank.Candidate is { } candidate && ExtensionRejectBlankDefault(candidate, size),
                "v6/blank-replacement-local-default/" + size);
        }
    }

    private static TemplateSnapshot ExtensionTemplate(string root, string strategy, float size, bool narrow = false, bool dates = false)
    {
        using var output = new BoundedPdfMemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(SyntheticTemplate(size, size))), new PdfWriter(output)))
        {
            var form = PdfAcroForm.GetAcroForm(pdf, false)!;
            if (strategy == "preserve-embedded-simple-v1")
            {
                var approved = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", "f941.pdf"));
                if (approved.Sha256 != ApprovedTemplates.Form941Sha256) throw new InvalidOperationException("Exact font fixture required.");
                using var source = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(approved)));
                var original = PdfAcroForm.GetAcroForm(source, false)!.GetPdfObject().GetAsDictionary(PdfName.DR)!
                    .GetAsDictionary(PdfName.Font)!.GetAsDictionary(new PdfName("TimesNewRomanPSMT"))!;
                form.GetPdfObject().GetAsDictionary(PdfName.DR)!.GetAsDictionary(PdfName.Font)!
                    .Put(new PdfName("AuthoredEmbedded"), original.CopyTo(pdf));
            }
            var fields = Fields(form).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < fields.Length; i++)
            {
                string resource = strategy == "preserve-embedded-simple-v1" ? "AuthoredEmbedded" : "Helvetica";
                fields[i].Value.GetPdfObject().Put(PdfName.DA,
                    new PdfString("/" + resource + " " + size.ToString(CultureInfo.InvariantCulture) + " Tf 0 g"));
                double left = dates ? 10 : 10 + 290 * i;
                double bottom = dates ? 10 + 190 * i : 10;
                double width = narrow ? Math.Min(270, 20 * size + 8) : dates ? 590 : 270;
                fields[i].Value.GetWidgets()[0].GetPdfObject().Put(PdfName.Rect,
                    new PdfArray(new double[] { left, bottom, left + width, bottom + 140 }));
            }
        }
        return TemplateSnapshot.FromBytes(output.ToArray());
    }

    private static FormConfigurationSnapshot ExtensionConfiguration(TemplateSnapshot template, string strategy,
        bool dates = false, bool eightCells = false)
    {
        var draft = new PdfTemplateInspector().DiscoverConfiguration(template);
        var profile = new ProfileDefinition
        {
            Schema = "pdfform.profile/3", Id = "synthetic-font-extensions", Revision = "one", ProfileVersion = 1,
            Title = "Synthetic rendering extensions", Template = new()
            {
                Sha256Allowlist = [template.Sha256], Title = draft.TemplateTitle, PageCount = draft.Pages,
                StructureFingerprintV1 = draft.StructureFingerprintV1 ?? throw new InvalidOperationException("Synthetic fingerprint missing.")
            }, Capabilities = ["text.singleWidget"], FilingYears = [], Options = new()
            {
                ZeroAmounts = "blank", DateCellLayouts = eightCells ? draft.Targets.Select(target => new DateCellLayout
                {
                    Target = target.Target, TemplateSha256 = template.Sha256,
                    Cells = Enumerable.Range(0, 8).Select(i => new double[]
                    {
                        target.Rectangle[0] + i * (target.Rectangle[2] - target.Rectangle[0]) / 8,
                        target.Rectangle[1], target.Rectangle[0] + (i + 1) * (target.Rectangle[2] - target.Rectangle[0]) / 8,
                        target.Rectangle[3]
                    }).ToArray()
                }).ToArray() : null
            }, Keys = draft.Targets.Select(target => new KeyDefinition
            {
                Key = target.Key, Kind = dates ? "date" : "text", Required = false, Sensitive = true,
                Format = dates ? eightCells ? "MMddyyyy" : "MM/dd/yyyy" : null, MaxLength = dates ? null : 64,
                Targets = JsonSerializer.SerializeToElement(new[] { target.Target })
            }).ToArray(), Rules = [], Unmapped = [], Source = JsonSerializer.SerializeToElement(new { }), Extensions = JsonSerializer.SerializeToElement(new { })
        };
        byte[] profileBytes = ProfileParser.SerializeProfile(profile);
        var catalog = new FormCatalogDefinition
        {
            Schema = "pdfform.catalog/1", Template = new("template.pdf", template.ByteCount, template.Sha256),
            Profile = new("profile.json", profileBytes.Length, Convert.ToHexStringLower(SHA256.HashData(profileBytes)), profile.Id, profile.Revision, profile.ProfileVersion),
            Review = new("reviewed", []), Policy = new()
            {
                Id = "configured-acroform-v1", Normalization = "none-v1", CheckboxFields = [],
                TextFields = draft.Targets.Select(target => new TextFieldPolicy
                {
                    Target = target.Target, Page = target.Page ?? throw new InvalidOperationException("Page required."), Rectangle = target.Rectangle.ToArray(),
                    Strategy = strategy, Layout = eightCells ? "eight-date-cells-v1" : "single-line-v1",
                    ResourceName = target.Font!.ResourceName, BaseFont = target.Font.BaseFont, Subtype = target.Font.Subtype,
                    Encoding = target.Font.Encoding, FontSize = target.Font.FontSize ?? throw new InvalidOperationException("Fixed size required."),
                    ProgramSha256 = target.Font.ProgramSha256.Length == 0 ? null : target.Font.ProgramSha256,
                    MetricsSha256 = target.Font.MetricsSha256, Repertoire = strategy == "preserve-embedded-simple-v1" ? "encoded-font-v1" : "ascii-plus-eacute-v1"
                }).ToArray()
            }
        };
        return FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options), profileBytes, template.Sha256, template.ByteCount).Value
            ?? throw new InvalidOperationException("Synthetic extension configuration refused.");
    }

    private static List<PdfStream> ExtensionAppearances(PdfDocument pdf, RenderManifest manifest, bool flatten)
    {
        if (!flatten)
        {
            var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!);
            return manifest.Targets.Keys.Select(key => fields[key].GetWidgets()[0].GetPdfObject()
                .GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!).ToList();
        }
        var result = new List<PdfStream>();
        for (int page = 1; page <= pdf.GetNumberOfPages(); page++)
        {
            var objects = pdf.GetPage(page).GetResources().GetPdfObject().GetAsDictionary(PdfName.XObject);
            if (objects is null) continue;
            foreach (var name in objects.KeySet())
                if (objects.GetAsStream(name) is { } stream && PdfName.Form.Equals(stream.GetAsName(PdfName.Subtype))) result.Add(stream);
        }
        return result;
    }

    private static bool ExtensionExactFonts(VerifiedFillCandidate candidate, FormConfigurationSnapshot configuration, float size, bool flatten)
    {
        var policies = configuration.ParsePolicy().TextFields;
        string expectedProgram = policies[0].Strategy == "preserve-embedded-simple-v1" ? policies[0].ProgramSha256! : ScheduleBEmbeddedFont.ProgramSha256;
        string expectedName = policies[0].Strategy == "preserve-embedded-simple-v1" ? policies[0].BaseFont : ScheduleBEmbeddedFont.Name;
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()));
        var appearances = ExtensionAppearances(pdf, candidate.ManifestForFixture, flatten);
        if (appearances.Count != policies.Length || candidate.Flattened != flatten) return false;
        foreach (var appearance in appearances)
        {
            var sizes = GeneratedContentPolicy.Commands(appearance.GetBytes()).Where(command => command[^1].ToString() == "Tf").ToArray();
            if (sizes.Length == 0) return false;
            foreach (var command in sizes)
            {
                if (command.Count != 3 || command[0] is not PdfName resource || command[1] is not PdfNumber number || number.DoubleValue() != size) return false;
                var font = appearance.GetAsDictionary(PdfName.Resources)?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(resource);
                var descriptor = font?.GetAsDictionary(PdfName.FontDescriptor);
                var program = descriptor?.GetAsStream(PdfName.FontFile2);
                if (font?.GetAsName(PdfName.BaseFont)?.GetValue() != expectedName || !PdfName.TrueType.Equals(font?.GetAsName(PdfName.Subtype))
                    || !PdfName.WinAnsiEncoding.Equals(font?.GetAsName(PdfName.Encoding)) || font!.ContainsKey(PdfName.ToUnicode)
                    || program is null || descriptor!.ContainsKey(PdfName.FontFile) || descriptor.ContainsKey(PdfName.FontFile3)
                    || VerifiedFormRenderer.Digest(program.GetBytes()) != expectedProgram) return false;
            }
        }
        return true;
    }

    private static bool ExtensionRejectFontSwap(VerifiedFillCandidate candidate, string root, string strategy, bool flatten)
    {
        bool isolated = false;
        byte[] changed = ExtensionMutate(candidate, pdf =>
        {
            var appearance = ExtensionAppearances(pdf, candidate.ManifestForFixture, flatten).First();
            string before = VerifiedFormRenderer.Digest(appearance.GetBytes());
            var resource = (PdfName)GeneratedContentPolicy.Commands(appearance.GetBytes()).First(command => command[^1].ToString() == "Tf")[0];
            PdfDictionary replacement;
            if (strategy == "preserve-embedded-simple-v1") replacement = new ScheduleBEmbeddedFont.DocumentFont(pdf).Get(pdf, out _).GetPdfObject();
            else
            {
                var approved = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", "f941.pdf"));
                if (approved.Sha256 != ApprovedTemplates.Form941Sha256) throw new InvalidOperationException("Exact font fixture required.");
                using var source = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(approved)));
                replacement = (PdfDictionary)PdfAcroForm.GetAcroForm(source, false)!.GetPdfObject().GetAsDictionary(PdfName.DR)!
                    .GetAsDictionary(PdfName.Font)!.GetAsDictionary(new PdfName("TimesNewRomanPSMT"))!.CopyTo(pdf);
            }
            appearance.GetAsDictionary(PdfName.Resources)!.GetAsDictionary(PdfName.Font)!.Put(resource, replacement);
            isolated = before == VerifiedFormRenderer.Digest(appearance.GetBytes());
        });
        return isolated && ExtensionVerificationCode(changed, candidate.ManifestForFixture, flatten) == "E_VERIFY_AP_FONT";
    }

    private static bool ExtensionDatePaint(VerifiedFillCandidate candidate, bool eightCells)
    {
        string expectedText = eightCells ? "10202026" : "10/20/2026";
        if (!candidate.ManifestForFixture.Targets.Values.All(target => target.Assignment.Text == expectedText
            && (eightCells ? target.DateCells is { Length: 8 } : target.DateCells is null))) return false;
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()));
        return ExtensionAppearances(pdf, candidate.ManifestForFixture, flatten: false).All(appearance =>
        {
            var commands = GeneratedContentPolicy.Commands(appearance.GetBytes()).ToArray();
            int shows = commands.Count(command => command[^1].ToString() is "Tj" or "TJ");
            int matrices = commands.Count(command => command[^1].ToString() == "Tm");
            return shows == (eightCells ? 8 : 1) && (!eightCells || matrices == 8);
        });
    }

    private static bool ExtensionRejectCellShift(VerifiedFillCandidate candidate)
    {
        bool moved = false;
        byte[] changed = ExtensionMutate(candidate, pdf =>
        {
            var appearance = ExtensionAppearances(pdf, candidate.ManifestForFixture, flatten: false).First();
            string content = Encoding.ASCII.GetString(appearance.GetBytes());
            var matrix = new Regex(@"\b1\s+0\s+0\s+1\s+(?<x>[-+]?[0-9]+(?:\.[0-9]+)?)\s+(?<y>[-+]?[0-9]+(?:\.[0-9]+)?)\s+Tm\b");
            string altered = matrix.Replace(content, match =>
            {
                moved = true;
                double x = double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture) + 1;
                return "1 0 0 1 " + x.ToString("0.#########", CultureInfo.InvariantCulture) + " " + match.Groups["y"].Value + " Tm";
            }, 1);
            appearance.SetData(Encoding.ASCII.GetBytes(altered));
        });
        return moved && ExtensionVerificationCode(changed, candidate.ManifestForFixture, flatten: false) == "E_VERIFY_DATE_POSITION";
    }

    private static bool ExtensionRejectBlankDefault(VerifiedFillCandidate candidate, float size)
    {
        bool blankUnchanged = false;
        byte[] changed = ExtensionMutate(candidate, pdf =>
        {
            var field = Fields(PdfAcroForm.GetAcroForm(pdf, false)!).OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
            var appearance = field.GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!;
            string before = VerifiedFormRenderer.Digest(appearance.GetBytes());
            field.GetPdfObject().Put(PdfName.DA, new PdfString("/PdfFormScheduleB " + (size == 1 ? "10" : "1") + " Tf 0 g"));
            blankUnchanged = field.GetPdfObject().GetAsString(PdfName.V)?.ToUnicodeString() == ""
                && before == VerifiedFormRenderer.Digest(appearance.GetBytes())
                && !GeneratedContentPolicy.Commands(appearance.GetBytes()).Any(command => command[^1].ToString() is "Tj" or "TJ");
        });
        return blankUnchanged && ExtensionVerificationCode(changed, candidate.ManifestForFixture, flatten: false) == "E_VERIFY_FIELD_FONT_DEFAULT";
    }

    private static byte[] ExtensionBytes(TemplateSnapshot snapshot)
    {
        using var input = snapshot.OpenRead();
        using var output = new MemoryStream();
        input.CopyTo(output); return output.ToArray();
    }

    private static byte[] ExtensionMutate(VerifiedFillCandidate candidate, Action<PdfDocument> mutation)
    {
        using var output = new BoundedPdfMemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()), new PdfWriter(output))) mutation(pdf);
        return output.ToArray();
    }

    private static string? ExtensionVerificationCode(byte[] bytes, RenderManifest manifest, bool flatten)
    {
        try
        {
            if (flatten) VerifiedFormRenderer.VerifyFlattened(bytes, manifest);
            else VerifiedFormRenderer.VerifyEditable(bytes, manifest);
            return null;
        }
        catch (VerifiedFillException error) { return error.Code; }
    }

    // Additive correction registration: RunPreservedDefaultCorrection(check, root).
    // Exactly42 checks:3 sizes *2 blank/populated states *(1 no-op +2 nodes *3 faults).
    internal static void RunPreservedDefaultCorrection(Action<bool, string> check, string root)
    {
        // Preserve all six public preparations before the unchanged fail-fast controls.
        // Diagnostics contain only closed codes, fixed case labels and structural facts.
        var controls = new List<(float Size, bool Populated, TemplateSnapshot Template,
            FormConfigurationSnapshot Configuration, Dictionary<string, string> Values, VerifiedFillResult Result)>();
        foreach (float size in new[] { 1f, 10, 72 })
        foreach (bool populated in new[] { false, true })
        {
            var template = CorrectionSplitWidgets(ExtensionTemplate(root, "preserve-embedded-simple-v1", size));
            var configuration = ExtensionConfiguration(template, "preserve-embedded-simple-v1");
            var values = configuration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => populated ? "11" : "", StringComparer.Ordinal);
            var result = VerifiedFillPipeline.Prepare(template, configuration, Data(configuration, values), flatten: false);
            EmitPreservedPreparationDiagnostic(size, populated, result);
            controls.Add((size, populated, template, configuration, values, result));
        }
        CheckSplitWidgetCanonicalControls(check, controls[0].Template, controls[0].Configuration, controls[0].Values);
        if (controls[0].Result.Candidate is null)
            EmitPreservedRenderDiagnostic(controls[0].Template, controls[0].Configuration, controls[0].Values);
        foreach (var control in controls)
        {
            float size = control.Size;
            bool populated = control.Populated;
            var template = control.Template;
            var configuration = control.Configuration;
            var result = control.Result;
            var candidate = result.Candidate ?? throw new InvalidOperationException("Preserved-default positive setup failed.");
            string fieldKey = candidate.ManifestForFixture.Targets.Keys.Order(StringComparer.Ordinal).First();
            byte[] roundtrip = CorrectionMutate(candidate.CopyForFixture(), pdf => { _ = Fields(PdfAcroForm.GetAcroForm(pdf, false)!); });
            string expectedState = CorrectionInvariant(roundtrip, candidate.ManifestForFixture);
            var fit = VerifiedFillPipeline.CheckValueFit(template, configuration, configuration.ParseProfile().Keys[0].Key,
                JsonSerializer.SerializeToElement(populated ? "11" : ""));
            check(result.ReadyToPublish && CorrectionDistinctWidgets(roundtrip)
                && ExtensionVerificationCode(roundtrip, candidate.ManifestForFixture, flatten: false) is null
                && CorrectionDefaultsCode(roundtrip, candidate.ManifestForFixture, fieldKey) is null
                && CorrectionApPass(roundtrip, candidate.ManifestForFixture)
                && CorrectionInvariant(candidate.CopyForFixture(), candidate.ManifestForFixture) == expectedState
                && fit.All(finding => finding.Severity != "error"),
                "v7/preserved-default-no-op/" + size + "/" + populated);

            for (int nodeIndex = 0; nodeIndex < 2; nodeIndex++)
            for (int fault = 0; fault < 3; fault++)
            {
                byte[] changed = CorrectionMutate(roundtrip, pdf =>
                {
                    var field = Fields(PdfAcroForm.GetAcroForm(pdf, false)!)[fieldKey];
                    var node = nodeIndex == 0 ? field.GetPdfObject() : field.GetWidgets()[0].GetPdfObject();
                    if (fault == 0)
                        node.Put(PdfName.DA, new PdfString("/PdfFormPreserved " + (size == 1 ? "10" : "1") + " Tf 0 g"));
                    else if (fault == 1)
                        node.Put(PdfName.DA, new PdfString("/PdfFormPreserved " + size.ToString(CultureInfo.InvariantCulture) + " Tf .5 g"));
                    else
                    {
                        // A valid alternate program isolates default identity from parser failure.
                        var other = new ScheduleBEmbeddedFont.DocumentFont(pdf).Get(pdf, out _);
                        var fonts = new PdfDictionary(); fonts.Put(NativeTextAppearance.PreservedResourceName, other.GetPdfObject());
                        var resources = new PdfDictionary(); resources.Put(PdfName.Font, fonts);
                        node.Put(PdfName.DR, resources);
                    }
                });
                string expectedCode = fault == 2 ? "E_VERIFY_AP_FONT" : "E_VERIFY_FIELD_FONT_DEFAULT";
                check(CorrectionInvariant(changed, candidate.ManifestForFixture) == expectedState
                    && CorrectionApPass(changed, candidate.ManifestForFixture)
                    && CorrectionDefaultsCode(changed, candidate.ManifestForFixture, fieldKey) == expectedCode
                    && ExtensionVerificationCode(changed, candidate.ManifestForFixture, flatten: false) == expectedCode,
                    "v7/preserved-default-tamper/" + size + "/" + populated + "/" + nodeIndex + "/" + fault);
            }
        }
    }

    private static void CheckSplitWidgetCanonicalControls(Action<bool, string> check, TemplateSnapshot template,
        FormConfigurationSnapshot configuration, Dictionary<string, string> values)
    {
        var binding = new TemplateBinder().Bind(template, configuration);
        var parsed = DataValidator.ParseData(Data(configuration, values));
        if (!binding.Bound || !parsed.IsValid || parsed.Value is not { } data)
            throw new InvalidOperationException("split-widget-control-admission");
        var validation = DataValidator.Validate(configuration.ParseProfile(), data);
        if (!validation.IsValid) throw new InvalidOperationException("split-widget-control-data");
        var assignments = AssignmentPlanner.Build(validation);
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(template)));
        var form = PdfAcroForm.GetAcroForm(pdf, false)!;
        var all = form.GetAllFormFields();
        var selected = Fields(form);
        var expected = new HashSet<string>(["group.amountA", "group.amountB"], StringComparer.Ordinal);
        check(all.Count == 3 && expected.SetEquals(selected.Keys)
            && expected.SetEquals(binding.Inspection.NamedFields.Keys)
            && expected.SetEquals(assignments.Select(assignment => assignment.Field))
            && all.Values.All(field => field.GetChildFormFields().Count > 0)
            && PdfName.Tx.Equals(all["group"].GetPdfObject().GetAsName(PdfName.FT))
            && !selected["group.amountA"].GetPdfObject().ContainsKey(PdfName.FT)
            && PdfName.Tx.Equals(VerifiedFormRenderer.Inherited(selected["group.amountA"].GetPdfObject(), PdfName.FT, null)),
            "split widgets retain canonical names while old childless wrappers select none");
        check(selected.Values.All(field =>
        {
            var canonical = field.GetPdfObject();
            var widget = field.GetWidgets().Single().GetPdfObject();
            var child = field.GetChildFormFields().Single().GetPdfObject();
            return canonical.GetAsString(PdfName.T) is not null && !PdfName.Widget.Equals(canonical.GetAsName(PdfName.Subtype))
                && PdfName.Widget.Equals(widget.GetAsName(PdfName.Subtype)) && !widget.ContainsKey(PdfName.T)
                && !widget.ContainsKey(PdfName.FT) && widget.GetAsString(PdfName.DA) is not null
                && widget.GetAsString(PdfName.DA)!.ToUnicodeString() == canonical.GetAsString(PdfName.DA)?.ToUnicodeString()
                && widget.ContainsKey(PdfName.DR) == canonical.ContainsKey(PdfName.DR)
                && !Equals(canonical.GetIndirectReference(), widget.GetIndirectReference())
                && Equals(child.GetIndirectReference(), widget.GetIndirectReference())
                && Equals(widget.GetAsDictionary(PdfName.Parent)?.GetIndirectReference(), canonical.GetIndirectReference())
                && Equals(canonical.GetAsArray(PdfName.Kids)?.GetAsDictionary(0)?.GetIndirectReference(), widget.GetIndirectReference());
        }), "split widgets preserve distinct dictionary ownership and local default appearance");

        // A local /FT on an unnamed widget must not introduce the flattened
        // wrapper's synthetic trailing-dot name as a new canonical field.
        byte[] localTypeBytes = CorrectionMutate(ExtensionBytes(template), changed =>
            Fields(PdfAcroForm.GetAcroForm(changed, false)!)["group.amountA"].GetWidgets()[0].GetPdfObject()
                .Put(PdfName.FT, PdfName.Tx));
        var localTypeTemplate = TemplateSnapshot.FromBytes(localTypeBytes);
        var localTypeConfiguration = ExtensionConfiguration(localTypeTemplate, "preserve-embedded-simple-v1");
        var localTypeBinding = new TemplateBinder().Bind(localTypeTemplate, localTypeConfiguration);
        var localTypeValues = localTypeConfiguration.ParseProfile().Keys.ToDictionary(key => key.Key, _ => "11", StringComparer.Ordinal);
        var localTypeResult = VerifiedFillPipeline.Prepare(localTypeTemplate, localTypeConfiguration,
            Data(localTypeConfiguration, localTypeValues), flatten: false);
        using var localTypePdf = new PdfDocument(VerifiedFormRenderer.Reader(localTypeBytes));
        var localTypeForm = PdfAcroForm.GetAcroForm(localTypePdf, false)!;
        check(localTypeForm.GetAllFormFields().ContainsKey("group.amountA.")
            && expected.SetEquals(Fields(localTypeForm).Keys)
            && localTypeBinding.Bound && expected.SetEquals(localTypeBinding.Inspection.NamedFields.Keys)
            && localTypeResult.ReadyToPublish && localTypeResult.Candidate is { } localTypeCandidate
            && CorrectionDistinctWidgets(localTypeCandidate.CopyForFixture()),
            "unnamed widget local type cannot overwrite or add a canonical field name");
    }

    private static void EmitPreservedPreparationDiagnostic(float size, bool populated, VerifiedFillResult result)
    {
        var findings = result.Findings.Take(32).Select(finding => new
        {
            code = DiagnosticCodes.Contains(finding.Code) ? finding.Code : "unknown_code",
            stage = finding.Stage is "syntax" or "profile" or "binding" or "bindingStructure" or "data" or "packet"
                or "render" or "internal" ? finding.Stage : "unknown_stage",
            severity = finding.Severity is "warning" or "error" ? finding.Severity : "unknown_severity"
        }).ToArray();
        Console.Error.WriteLine("DIAG_PRESERVED_DEFAULT " + JsonSerializer.Serialize(new
        {
            schema = "pdfform.preserved-default-diagnostic/1", size, scenario = populated ? "populated" : "blank",
            exitCode = result.ExitCode is >= 0 and <= 4 ? result.ExitCode : -1,
            ready = result.ReadyToPublish, findingCount = result.Findings.Count, findings,
            truncated = result.Findings.Count > 32
        }));
    }

    // One bounded first-blank synthetic probe preserves the public Prepare result.
    // It reports a closed method label, never exception messages, values or field names.
    private static void EmitPreservedRenderDiagnostic(TemplateSnapshot template,
        FormConfigurationSnapshot configuration, Dictionary<string, string> values)
    {
        string stage = "parse", outcome = "not_run", errorCode = "none";
        int errorExit = -1;
        bool dataValid = false, bindingReady = false;
        string[] frames = [];
        var allowedFrames = new HashSet<string>(StringComparer.Ordinal)
        {
            "VerifiedFormRenderer.Render", "VerifiedFormRenderer.RenderCore", "VerifiedFormRenderer.PrepareTarget",
            "VerifiedFormRenderer.ReplaceAll", "VerifiedFormRenderer.ReplaceText", "VerifiedFormRenderer.VerifyEditable",
            "VerifiedFormRenderer.CheckTargetForReplacement", "VerifiedFormRenderer.CheckPolicyCoverage",
            "NativeTextAppearance.ResolveConfiguredFont", "NativeTextAppearance.ResolvePreservedFont",
            "NativeTextAppearance.SetPreservedDefaults", "NativeTextAppearance.VerifyPreservedDefaults",
            "NativeTextAppearance.VerifyFont", "NativeTextAppearance.Verify", "NativeTextAppearance.CheckFit",
            "TemplateBinder.Bind", "ApprovedTemplateNormalizer.OpenForNormalization"
        };
        try
        {
            var parsed = DataValidator.ParseData(Data(configuration, values));
            if (!parsed.IsValid || parsed.Value is not { } data) throw new InvalidOperationException("PROBE_PARSE_REFUSED");
            stage = "validate";
            var validation = DataValidator.Validate(configuration.ParseProfile(), data);
            dataValid = validation.IsValid;
            if (!dataValid) throw new InvalidOperationException("PROBE_DATA_REFUSED");
            stage = "bind";
            var bound = new TemplateBinder().Bind(template, configuration);
            bindingReady = bound.Bound;
            if (!bindingReady) throw new InvalidOperationException("PROBE_BINDING_REFUSED");
            stage = "render";
            _ = VerifiedFormRenderer.Render(template, configuration, bound, AssignmentPlanner.Build(validation), flatten: false);
            outcome = "render_returned";
        }
        catch (Exception error)
        {
            outcome = error is VerifiedFillException ? "verified_exception" : "other_exception";
            if (error is VerifiedFillException verified)
            {
                errorCode = DiagnosticCodes.Contains(verified.Code) ? verified.Code : "unknown_code";
                errorExit = verified.ExitCode is >= 0 and <= 4 ? verified.ExitCode : -1;
            }
            frames = (new System.Diagnostics.StackTrace(error, false).GetFrames() ?? [])
                .Select(frame => frame.GetMethod()).Where(method => method is not null)
                .Select(method => method!.DeclaringType?.Name + "." + method.Name)
                .Where(allowedFrames.Contains).Take(8).ToArray();
        }
        Console.Error.WriteLine("DIAG_PRESERVED_RENDER " + JsonSerializer.Serialize(new
        {
            schema = "pdfform.preserved-render-diagnostic/1", size = 1, scenario = "blank",
            stage, dataValid, bindingReady, outcome, errorCode, errorExit, frames
        }));
    }

    private static TemplateSnapshot CorrectionSplitWidgets(TemplateSnapshot snapshot)
    {
        using var output = new BoundedPdfMemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(ExtensionBytes(snapshot)), new PdfWriter(output)))
        {
            var form = PdfAcroForm.GetAcroForm(pdf, false)!;
            foreach (var field in Fields(form).Values)
            {
                var canonical = field.GetPdfObject();
                if (!PdfName.Widget.Equals(canonical.GetAsName(PdfName.Subtype))) continue;
                var widget = new PdfDictionary(); widget.Put(PdfName.Type, PdfName.Annot); widget.Put(PdfName.Subtype, PdfName.Widget);
                foreach (var key in new[] { PdfName.Rect, PdfName.AP, PdfName.AS, PdfName.F, PdfName.BS, PdfName.Border, PdfName.MK, PdfName.P, PdfName.DA, PdfName.DR })
                    if (canonical.Get(key, true) is { } value) widget.Put(key, value);
                widget.Put(PdfName.Parent, canonical); widget.MakeIndirect(pdf);
                var kids = new PdfArray(); kids.Add(widget); canonical.Put(PdfName.Kids, kids);
                bool replaced = false;
                for (int page = 1; page <= pdf.GetNumberOfPages(); page++)
                {
                    var annotations = pdf.GetPage(page).GetPdfObject().GetAsArray(PdfName.Annots);
                    if (annotations is null) continue;
                    for (int i = 0; i < annotations.Size(); i++)
                        if (annotations.GetAsDictionary(i)?.GetIndirectReference()?.Equals(canonical.GetIndirectReference()) == true)
                        {
                            annotations.Set(i, widget); widget.Put(PdfName.P, pdf.GetPage(page).GetPdfObject()); replaced = true;
                        }
                }
                if (!replaced) throw new InvalidOperationException("Synthetic merged widget page reference missing.");
                foreach (var key in new[] { PdfName.Type, PdfName.Subtype, PdfName.Rect, PdfName.AP, PdfName.AS, PdfName.F, PdfName.BS, PdfName.Border, PdfName.MK, PdfName.P }) canonical.Remove(key);
            }
        }
        return TemplateSnapshot.FromBytes(output.ToArray());
    }

    private static bool CorrectionDistinctWidgets(byte[] bytes)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        return Fields(PdfAcroForm.GetAcroForm(pdf, false)!).Values.All(field =>
            !ReferenceEquals(field.GetPdfObject(), field.GetWidgets()[0].GetPdfObject())
            && !Equals(field.GetPdfObject().GetIndirectReference(), field.GetWidgets()[0].GetPdfObject().GetIndirectReference()));
    }

    private static byte[] CorrectionMutate(byte[] bytes, Action<PdfDocument> mutation)
    {
        using var output = new BoundedPdfMemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes), new PdfWriter(output))) mutation(pdf);
        return output.ToArray();
    }

    private static string CorrectionInvariant(byte[] bytes, RenderManifest manifest)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!);
        var rows = new List<string>();
        foreach (string key in manifest.Targets.Keys.Order(StringComparer.Ordinal))
        {
            var field = fields[key]; var widget = field.GetWidgets()[0].GetPdfObject();
            var appearance = widget.GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!;
            var fonts = appearance.GetAsDictionary(PdfName.Resources)?.GetAsDictionary(PdfName.Font);
            var graph = new List<string>();
            if (fonts is not null)
                foreach (var name in fonts.KeySet().OrderBy(name => name.GetValue(), StringComparer.Ordinal))
                {
                    var font = fonts.GetAsDictionary(name)!;
                    var program = font.GetAsDictionary(PdfName.FontDescriptor)?.GetAsStream(PdfName.FontFile2);
                    graph.Add(name.GetValue() + ":" + NativeTextAppearance.SourceMetricsSha256(font) + ":"
                        + (program is null ? "<none>" : VerifiedFormRenderer.Digest(program.GetBytes())));
                }
            rows.Add(key + "|" + field.GetPdfObject().Get(PdfName.V)?.ToString() + "|" + field.GetPdfObject().Get(PdfName.DV)?.ToString()
                + "|" + widget.GetAsArray(PdfName.Rect) + "|" + VerifiedFormRenderer.Digest(appearance.GetBytes()) + "|" + string.Join(",", graph));
        }
        return string.Join("\n", rows);
    }

    private static string? CorrectionDefaultsCode(byte[] bytes, RenderManifest manifest, string key)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        var field = Fields(PdfAcroForm.GetAcroForm(pdf, false)!)[key];
        try { NativeTextAppearance.VerifyPreservedDefaults(field, manifest.Targets[key].Font!, new()); return null; }
        catch (VerifiedFillException error) { return error.Code; }
    }

    private static bool CorrectionApPass(byte[] bytes, RenderManifest manifest)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!);
        var context = new NativeTextAppearance.VerificationContext();
        foreach (var pair in manifest.Targets)
            NativeTextAppearance.Verify(fields[pair.Key].GetWidgets()[0].GetPdfObject()
                .GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!, pair.Value, context);
        return true;
    }

    private static void VerifySharedProgram(Action<bool, string> check, VerifiedFillCandidate candidate, string label)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()));
        var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!);
        var context = new NativeTextAppearance.VerificationContext();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in candidate.ManifestForFixture.Targets)
        {
            var dictionary = ScheduleBEmbeddedFont.VerifyDefaults(pdf, fields[pair.Key], pair.Value.Font!, context);
            identities.Add(dictionary.GetIndirectReference()?.ToString()
                ?? throw new InvalidOperationException("The shared imported font must have a document identity."));
            var stream = dictionary.GetAsDictionary(PdfName.FontDescriptor)!.GetAsStream(PdfName.FontFile2)!;
            check(stream.GetBytes().Length == ScheduleBEmbeddedFont.ProgramBytes
                && VerifiedFormRenderer.Digest(stream.GetBytes()) == ScheduleBEmbeddedFont.ProgramSha256,
                "full unchanged pinned font bytes are embedded: " + label);
        }
        check(identities.Count == 1, "all configured defaults share one document font graph: " + label);
    }

    private static void RejectDefaultMutations(Action<bool, string> check, VerifiedFillCandidate candidate)
    {
        foreach (int nodeIndex in new[] { 0, 1, 2 })
        foreach (bool removeResource in new[] { false, true })
        {
            byte[] changed;
            string beforeAppearance, afterAppearance;
            using (var output = new MemoryStream())
            {
                using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(candidate.CopyForFixture()), new PdfWriter(output)))
                {
                    var form = PdfAcroForm.GetAcroForm(pdf, false)!;
                    var field = Fields(form).OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
                    var widget = field.GetWidgets()[0].GetPdfObject();
                    beforeAppearance = VerifiedFormRenderer.Digest(widget.GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!.GetBytes());
                    var node = new[] { form.GetPdfObject(), field.GetPdfObject(), widget }[nodeIndex];
                    if (removeResource) node.Remove(PdfName.DR);
                    else node.Put(PdfName.DA, new PdfString("/PdfFormScheduleB 12.5 Tf .5 g"));
                    afterAppearance = VerifiedFormRenderer.Digest(widget.GetAsDictionary(PdfName.AP)!.GetAsStream(PdfName.N)!.GetBytes());
                }
                changed = output.ToArray();
            }
            string? code = null;
            try { VerifiedFormRenderer.VerifyEditable(changed, candidate.ManifestForFixture); }
            catch (VerifiedFillException error) { code = error.Code; }
            check(beforeAppearance == afterAppearance && code == (removeResource ? "E_VERIFY_FIELD_FONT_RESOURCE" : "E_VERIFY_FIELD_FONT_DEFAULT"),
                "blank q/Q appearance cannot mask damaged font defaults/resources: " + nodeIndex + "/" + removeResource);
        }
    }

    private static void RejectSourceIdentityClaims(Action<bool, string> check, TemplateSnapshot template,
        FormConfigurationSnapshot configuration, Dictionary<string, string> values)
    {
        byte[] profile;
        using (var input = configuration.OpenProfileRead())
        using (var output = new MemoryStream()) { input.CopyTo(output); profile = output.ToArray(); }
        foreach (string claim in new[] { "metricsSha256", "programSha256" })
        {
            using var input = configuration.OpenCatalogRead();
            var catalog = JsonNode.Parse(input)!;
            catalog["policy"]!["textFields"]![0]![claim] = claim == "programSha256"
                ? ScheduleBEmbeddedFont.ProgramSha256 : new string('0', 64);
            var changed = FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options), profile,
                template.Sha256, template.ByteCount).Value ?? throw new InvalidOperationException("Synthetic identity claim setup failed.");
            var refused = VerifiedFillPipeline.Prepare(template, changed, Data(changed, values), flatten: false);
            check(refused.ExitCode == 3 && !refused.ReadyToPublish
                && refused.Findings.Any(finding => finding.Code == "E_CONFIG_FONT_IDENTITY"),
                "pinned output font cannot override a false source identity claim: " + claim);
        }
    }

    private static TemplateSnapshot SyntheticTemplate(float firstSize, float secondSize)
    {
        using var output = new MemoryStream();
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(Fixture.Make()), new PdfWriter(output)))
        {
            var fields = Fields(PdfAcroForm.GetAcroForm(pdf, false)!).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < fields.Length; i++)
            {
                float size = i == 0 ? firstSize : secondSize;
                var field = fields[i].Value;
                field.GetPdfObject().Put(PdfName.DA, new PdfString("/Helvetica " + size.ToString(CultureInfo.InvariantCulture) + " Tf 0 g"));
                field.GetPdfObject().Put(PdfName.Ff, new PdfNumber(0));
                field.GetPdfObject().Put(PdfName.Q, new PdfNumber(0));
                field.GetPdfObject().Put(PdfName.MaxLen, new PdfNumber(64));
                field.GetWidgets()[0].GetPdfObject().Put(PdfName.Rect, new PdfArray(new double[] { 10 + 290 * i, 10, 280 + 290 * i, 150 }));
            }
        }
        return TemplateSnapshot.FromBytes(output.ToArray());
    }

    private static FormConfigurationSnapshot Configuration(TemplateSnapshot template)
    {
        var draft = new PdfTemplateInspector().DiscoverConfiguration(template);
        var profile = new ProfileDefinition
        {
            Schema = "pdfform.profile/3", Id = "synthetic-render-font", Revision = "one", ProfileVersion = 1,
            Title = "Synthetic font rendering", Template = new()
            {
                Sha256Allowlist = [template.Sha256], Title = draft.TemplateTitle, PageCount = draft.Pages,
                StructureFingerprintV1 = draft.StructureFingerprintV1 ?? throw new InvalidOperationException("Synthetic fingerprint missing.")
            }, Capabilities = ["text.singleWidget"], FilingYears = [], Options = new() { ZeroAmounts = "blank" },
            Keys = draft.Targets.Select(target => new KeyDefinition
            {
                Key = target.Key, Kind = "text", Required = false, Sensitive = true, MaxLength = 64,
                Targets = JsonSerializer.SerializeToElement(new[] { target.Target })
            }).ToArray(), Rules = [], Unmapped = [], Source = JsonSerializer.SerializeToElement(new { }), Extensions = JsonSerializer.SerializeToElement(new { })
        };
        byte[] profileBytes = ProfileParser.SerializeProfile(profile);
        var catalog = new
        {
            schema = "pdfform.catalog/1", template = new { path = "template.pdf", bytes = template.ByteCount, sha256 = template.Sha256 },
            profile = new { path = "profile.json", bytes = profileBytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(profileBytes)), id = profile.Id, revision = profile.Revision, profileVersion = profile.ProfileVersion },
            review = new { state = "reviewed", unresolved = Array.Empty<object>() }, policy = new
            {
                id = "configured-acroform-v1", normalization = "none-v1",
                textFields = draft.Targets.Select(target => new
                {
                    target = target.Target, page = target.Page, rectangle = target.Rectangle, strategy = ScheduleBEmbeddedFont.ConfiguredPolicy,
                    layout = "single-line-v1", resourceName = target.Font!.ResourceName, baseFont = target.Font.BaseFont,
                    subtype = target.Font.Subtype, encoding = target.Font.Encoding, fontSize = target.Font.FontSize,
                    metricsSha256 = target.Font.MetricsSha256, repertoire = "ascii-plus-eacute-v1"
                }).ToArray(), checkboxFields = Array.Empty<object>()
            }
        };
        return FormCatalog.Bind(JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options), profileBytes, template.Sha256, template.ByteCount).Value
            ?? throw new InvalidOperationException("Synthetic rendering configuration was refused.");
    }

    private static Dictionary<string, PdfFormField> Fields(PdfAcroForm form)
    {
        static IEnumerable<KeyValuePair<string, PdfFormField>> Walk(PdfFormField field, string prefix)
        {
            var part = field.GetPdfObject().GetAsString(PdfName.T)?.ToUnicodeString();
            string name = part is null ? prefix : prefix.Length == 0 ? part : prefix + "." + part;
            var children = field.GetChildFormFields().Where(child =>
                !PdfName.Widget.Equals(child.GetPdfObject().GetAsName(PdfName.Subtype))
                || child.GetPdfObject().ContainsKey(PdfName.T)).ToArray();
            if (children.Length == 0) yield return new(name, field);
            else foreach (var child in children)
                foreach (var leaf in Walk(child, name)) yield return leaf;
        }
        return form.GetRootFormFields().Values.SelectMany(field => Walk(field, ""))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static byte[] Data(FormConfigurationSnapshot configuration, Dictionary<string, string> values) =>
        JsonSerializer.SerializeToUtf8Bytes(new { schema = "pdfform.data/3", profile = configuration.ParseProfile().Identity, values }, StrictJson.Options);
}
