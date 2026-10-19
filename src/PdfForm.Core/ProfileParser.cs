using System.Text.Json;
using System.Text.RegularExpressions;

namespace PdfForm.Core;

public static class ProfileParser
{
    private static readonly string[] Common = ["key", "kind", "section", "label", "required", "pattern", "strip", "transform", "maxLength", "sensitive"];
    private static readonly Dictionary<string, string[]> KindProperties = new(StringComparer.Ordinal)
    {
        ["text"] = ["targets"], ["split"] = ["targets", "parts"],
        ["money"] = ["dollars", "cents", "allowNegative", "thousands", "zero"],
        ["date"] = ["targets", "format"], ["checkbox"] = ["target", "on"], ["oneOf"] = ["options"]
    };

    public static ParseResult<ProfileDefinition> ParseProfile(ReadOnlySpan<byte> bytes)
    {
        var parsed = StrictJson.Parse<ProfileDefinition>(bytes, "profile");
        if (!parsed.IsValid) return parsed;
        var p = parsed.Value!;
        var findings = new List<Finding>();
        void Error(string code, string message, string? key = null, string? rule = null) => findings.Add(new(code, "profile", message, key, RuleId: rule));
        bool Nonempty(string? value) => !string.IsNullOrWhiteSpace(value);
        bool Unique(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).Count() == values.Count();
        if (p.Schema != "pdfform.profile/2") Error("E_PROFILE_SCHEMA", "Profile schema must be pdfform.profile/2.");
        if (!SafeName(p.Id) || !SafeName(p.Revision) || p.ProfileVersion <= 0 || !Nonempty(p.Title))
            Error("E_PROFILE_IDENTITY", "Profile identity and version are invalid.");
        if (p.Template.Sha256Allowlist.Length == 0 || !Unique(p.Template.Sha256Allowlist) || p.Template.Sha256Allowlist.Any(x => !Match(x, "[0-9a-f]{64}")))
            Error("E_TEMPLATE_ALLOWLIST", "Template allowlist must contain unique lowercase SHA-256 hashes.");
        if (!Nonempty(p.Template.Title) || p.Template.PageCount is < 1 or > Limits.MaxTemplatePages || !Match(p.Template.StructureFingerprintV1, "sha256:[0-9a-f]{64}"))
            Error("E_TEMPLATE_CONTRACT", "Template title, page count or fingerprint is invalid.");
        if (!Unique(p.Capabilities) || p.Capabilities.Any(x => x is not ("text.singleWidget" or "checkbox.singleWidget" or "xfa.staticHybrid")))
            Error("E_PROFILE_CAPABILITY", "Profile declares an unsupported or duplicate capability.");
        if (p.FilingYears.Length == 0 || !Unique(p.FilingYears) || p.FilingYears.Any(x => !Match(x, "[0-9]{4}") || x == "0000"))
            Error("E_PROFILE_YEARS", "Supported filing years must be distinct four-digit Gregorian years.");
        if (p.Options.ZeroAmounts is not ("blank" or "show")) Error("E_PROFILE_ZERO", "Zero display policy is invalid.");
        if (p.Unmapped.Length != 0) Error("E_PROFILE_UNMAPPED", "The initial profile contract requires complete target ownership.");
        if (p.Extensions.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) Error("E_EXTENSIONS", "Extensions must be an object.");
        if (p.Source.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) Error("E_PROFILE_SOURCE", "Source metadata must be an object.");
        if (p.Keys.Length is < 1 or > Limits.MaxKeysOrTargets || p.Rules.Length > Limits.MaxRules) Error("E_PROFILE_LIMIT", "Profile exceeds a key or rule count limit.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var keyMap = new Dictionary<string, KeyDefinition>(StringComparer.Ordinal);
        foreach (var k in p.Keys)
        {
            if (!SafeName(k.Key) || !names.Add(k.Key)) { Error("E_PROFILE_KEY", "Profile key name is invalid or duplicated."); continue; }
            keyMap[k.Key] = k;
            if (!KindProperties.ContainsKey(k.Kind)) { Error("E_PROFILE_KIND", "Profile kind is unsupported.", k.Key); continue; }
            if (k.MaxLength is <= 0 || k.Transform is not (null or "upper") || (k.Strip?.Length ?? 0) > 256) Error("E_PROFILE_NORMALIZATION", "Text normalization or length limit is invalid.", k.Key);
            if (k.Pattern is not null)
            {
                if (k.Pattern.Length > 1024) { Error("E_PROFILE_PATTERN", "Profile pattern exceeds its length limit.", k.Key); continue; }
                try { _ = new Regex("\\A(?:" + k.Pattern + ")\\z", RegexOptions.CultureInvariant, Limits.RegexTimeout); }
                catch (ArgumentException) { Error("E_PROFILE_PATTERN", "Profile pattern is invalid.", k.Key); }
            }
            if (k.Kind is "text" or "date")
            {
                if (k.Targets.ValueKind != JsonValueKind.Array || k.Targets.GetArrayLength() == 0 || k.Targets.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || !Nonempty(x.GetString())))
                    Error("E_PROFILE_TARGETS", "Text and date require nonempty flat target arrays.", k.Key);
            }
            if (k.Kind == "split")
            {
                if (k.Parts is null || k.Parts.Length == 0 || k.Parts.Any(x => x <= 0) || k.Parts.Sum(x => (long)x) > Limits.MaxTextScalars ||
                    k.Targets.ValueKind != JsonValueKind.Array || k.Targets.GetArrayLength() == 0 ||
                    k.Targets.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Array || x.GetArrayLength() != k.Parts.Length || x.EnumerateArray().Any(t => t.ValueKind != JsonValueKind.String || !Nonempty(t.GetString()))))
                    Error("E_PROFILE_SPLIT", "Split copies must match positive component lengths.", k.Key);
            }
            if (k.Kind is "money" or "date" or "checkbox" && (k.Strip is not null || k.Transform is not null)) Error("E_PROFILE_NORMALIZATION", "Typed values do not allow text strip or case transformations.", k.Key);
            if (k.Kind == "money" && (!Nonempty(k.Dollars) || !Nonempty(k.Cents) || k.AllowNegative is null || k.Thousands is null || k.Zero is not ("blank" or "show")))
                Error("E_PROFILE_MONEY", "Money requires targets and explicit formatting flags.", k.Key);
            if (k.Kind == "date" && k.Format is not ("MMddyyyy" or "yyyy-MM-dd" or "MM/dd/yyyy")) Error("E_PROFILE_DATE", "Date format is unsupported.", k.Key);
            if (k.Kind == "checkbox" && (!Nonempty(k.Target) || k.On is "" or "Off")) Error("E_PROFILE_CHECKBOX", "Checkbox target or on-state is invalid.", k.Key);
            if (k.Kind == "oneOf" && (k.Options is null || k.Options.Count == 0 || k.Options.Any(x => string.IsNullOrEmpty(x.Key) || x.Value.Length == 0 || x.Value.Any(a => !Nonempty(a.Target) || a.On is "" or "Off"))))
                Error("E_PROFILE_ONEOF", "Choice options require distinct checkbox assignments.", k.Key);
            foreach (string target in k.EnumerateTargets())
                if (!Nonempty(target) || target.Length > 1024 || !targets.Add(target)) Error("E_PROFILE_TARGET_OWNER", "Target is empty or has conflicting owners.", k.Key);
        }
        if (targets.Count > Limits.MaxKeysOrTargets) Error("E_PROFILE_LIMIT", "Profile exceeds the target count limit.");
        // Kind-specific unknowns must fail even though the common DTO has those properties for other kinds.
        ReadOnlySpan<byte> json = bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? bytes[3..] : bytes;
        using var raw = JsonDocument.Parse(json.ToArray());
        foreach (var rawKey in raw.RootElement.GetProperty("keys").EnumerateArray())
        {
            string kind = rawKey.GetProperty("kind").GetString()!;
            if (!KindProperties.TryGetValue(kind, out var specific)) continue;
            if (rawKey.EnumerateObject().Any(x => !Common.Contains(x.Name) && !specific.Contains(x.Name)))
                Error("E_PROFILE_KIND_MEMBER", "Property is not allowed for this key kind.");
        }
        foreach (var layout in p.Options.DateCellLayouts ?? [])
        {
            if (!p.Template.Sha256Allowlist.Contains(layout.TemplateSha256, StringComparer.Ordinal) ||
                !p.Keys.Any(k => k.Kind == "date" && k.Format == "MMddyyyy" && k.EnumerateTargets().Contains(layout.Target, StringComparer.Ordinal)) ||
                layout.Cells.Length != 8 || layout.Cells.Any(r => r.Length != 4 || r.Any(x => !double.IsFinite(x)) || r[0] >= r[2] || r[1] >= r[3]))
                Error("E_PROFILE_DATE_LAYOUT", "Date cell layout must bind eight valid rectangles to an eight-digit date target and approved template.");
        }
        if (p.Options.DateCellLayouts is { } layouts && layouts.Select(x => (x.Target, x.TemplateSha256)).Distinct().Count() != layouts.Length)
            Error("E_PROFILE_DATE_LAYOUT", "Date cell layout is duplicated.");
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in p.Rules)
        {
            if (!SafeName(r.Id) || !ruleIds.Add(r.Id)) Error("E_PROFILE_RULE_ID", "Rule ID is invalid or duplicated.");
            if (r.Severity is not ("error" or "warning")) Error("E_PROFILE_RULE_SEVERITY", "Rule severity is unsupported.", rule: SafeName(r.Id) ? r.Id : null);
            string? rid = SafeName(r.Id) ? r.Id : null;
            bool Ref(string? key) => key is not null && keyMap.ContainsKey(key);
            switch (r.Kind)
            {
                case "sum":
                    if (!Ref(r.Total) || keyMap[r.Total!].Kind != "money" || r.Terms is null || r.Terms.Length is 0 or > Limits.MaxKeysOrTargets || r.Terms.Any(t => !Ref(t.Key) || keyMap[t.Key].Kind != "money" || t.Sign is not (1 or -1)) || r.Left is not null || r.Right is not null || r.Keys is not null)
                        Error("E_PROFILE_RULE_OPERANDS", "Sum requires only money operands and signed terms.", rule: rid);
                    break;
                case "equal":
                    if (!Ref(r.Left) || !Ref(r.Right) || keyMap[r.Left!].Kind != keyMap[r.Right!].Kind || r.Total is not null || r.Terms is not null || r.Keys is not null)
                        Error("E_PROFILE_RULE_OPERANDS", "Equality requires two declared keys of the same semantic kind.", rule: rid);
                    break;
                case "requiredIf": case "allOrNone":
                    if (r.Keys is null || r.Keys.Length == 0 || !Unique(r.Keys) || r.Keys.Any(x => !Ref(x)) || (r.Kind == "requiredIf" && r.When is null) || r.Total is not null || r.Terms is not null || r.Left is not null || r.Right is not null)
                        Error("E_PROFILE_RULE_OPERANDS", "Presence rule requires distinct declared keys and an applicable condition.", rule: rid);
                    break;
                default: Error("E_PROFILE_RULE_KIND", "Rule kind is unsupported.", rule: rid); break;
            }
            if (r.When is { } w)
            {
                if ((w.Key is null) == (w.Context is null)) Error("E_PROFILE_CONDITION", "Condition must select exactly one key or context member.", rule: rid);
                else if (w.Context is { } context)
                {
                    if (w.EqualsValue.ValueKind != JsonValueKind.String || !ValidContextValue(context, w.EqualsValue.GetString()!))
                        Error("E_PROFILE_CONDITION", "Context condition literal is invalid.", rule: rid);
                }
                else if (!Ref(w.Key)) Error("E_PROFILE_CONDITION", "Condition references an undeclared key.", rule: rid);
                else
                {
                    var key = keyMap[w.Key!];
                    bool tokenValid = key.Kind == "checkbox" ? w.EqualsValue.ValueKind is JsonValueKind.True or JsonValueKind.False : w.EqualsValue.ValueKind == JsonValueKind.String;
                    if (!tokenValid || !ValueNormalizer.Normalize(key, w.EqualsValue, true).Value.IsValid)
                        Error("E_PROFILE_CONDITION", "Condition literal must match its key's normalized type.", rule: rid);
                }
            }
        }
        return new(findings.Count == 0 ? p : null, findings.AsReadOnly());
    }
    internal static bool Match(string value, string pattern) => Regex.IsMatch(value, "\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant, Limits.RegexTimeout);
    internal static bool SafeName(string value) => Match(value, "[A-Za-z0-9][A-Za-z0-9._-]{0,127}");
    internal static bool ValidContextValue(string key, string value) => key switch
    {
        "taxYear" => Match(value, "[0-9]{4}") && value != "0000",
        "quarter" => value is "1" or "2" or "3" or "4",
        "liabilityReporting" => value is "none" or "monthly" or "semiweekly",
        _ => false
    };
}
