using System.Numerics;
using System.Text.Json;

namespace PdfForm.Core;

public static class DataValidator
{
    public static ParseResult<DataDocument> ParseData(ReadOnlySpan<byte> bytes)
    {
        bool generic = ProfileParser.HasSchema(bytes, "pdfform.data/3");
        ParseResult<DataDocument> parsed;
        if (generic)
        {
            var wire = StrictJson.Parse<GenericDataDocument>(bytes, "data");
            if (!wire.IsValid) parsed = new(null, wire.Findings);
            else
            {
                var value = wire.Value!;
                parsed = new(new DataDocument
                { Schema = value.Schema, Profile = value.Profile, Context = null, Values = value.Values, Extensions = value.Extensions }, []);
            }
        }
        else parsed = StrictJson.Parse<DataDocument>(bytes, "data");
        if (!parsed.IsValid) return parsed;
        var d = parsed.Value!;
        var errors = new List<Finding>();
        void Error(string code, string message) => errors.Add(new(code, "syntax", message));
        if (d.Schema != (generic ? "pdfform.data/3" : "pdfform.data/2")) Error("E_DATA_SCHEMA", "Data schema must be pdfform.data/2 or pdfform.data/3.");
        if (!ProfileParser.Match(d.Profile, "[A-Za-z0-9][A-Za-z0-9._-]{0,127}@[A-Za-z0-9][A-Za-z0-9._-]{0,127}")) Error("E_DATA_PROFILE", "Data profile identity is invalid.");
        if (!generic && (d.Context is null || !ProfileParser.ValidContextValue("taxYear", d.Context.TaxYear) || !ProfileParser.ValidContextValue("quarter", d.Context.Quarter) || !ProfileParser.ValidContextValue("liabilityReporting", d.Context.LiabilityReporting))) Error("E_CONTEXT", "Filing context is missing or invalid.");
        if (d.Values.Count > Limits.MaxKeysOrTargets) Error("E_DATA_LIMIT", "Data exceeds the value count limit.");
        if (d.Values.Values.Any(v => v.ValueKind is not (JsonValueKind.String or JsonValueKind.True or JsonValueKind.False))) Error("E_VALUE_TYPE", "Data values must use string or Boolean tokens.");
        if (d.Extensions.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) Error("E_EXTENSIONS", "Extensions must be an object.");
        return new(errors.Count == 0 ? d : null, errors.ToArray());
    }

    public static ValidationReport Validate(ProfileDefinition profile, DataDocument data)
    {
        var findings = new List<Finding>();
        var values = new Dictionary<string, NormalizedValue>(StringComparer.Ordinal);
        var evaluations = new List<RuleEvaluation>();
        void Error(string code, string message, string? key = null) => findings.Add(new(code, "data", message, key));
        if (data.Profile != profile.Identity) Error("E_PROFILE_IDENTITY", "Data and profile identities do not agree.");
        bool generic = profile.Schema == "pdfform.profile/3";
        if (data.Schema != (generic ? "pdfform.data/3" : "pdfform.data/2"))
            Error("E_DATA_SCHEMA_PAIR", "Profile and data envelope versions do not agree.");
        if (generic ? data.Context is not null : data.Context is null)
            Error("E_CONTEXT", "Context presence does not match the selected profile version.");
        if (findings.Any(f => f.Code is "E_DATA_SCHEMA_PAIR" or "E_CONTEXT"))
            return new(profile, data, values, findings.ToArray(), evaluations.ToArray());
        if (!generic && !profile.FilingYears.Contains(data.Context!.TaxYear, StringComparer.Ordinal)) Error("E_FILING_YEAR", "Filing year is outside the profile's approved scope.");
        var definitions = profile.Keys.ToDictionary(k => k.Key, StringComparer.Ordinal);
        foreach (string unknown in data.Values.Keys.Where(k => !definitions.ContainsKey(k)))
        { _ = unknown; Error("E_UNKNOWN_KEY", "Data contains an undeclared key."); }
        foreach (var key in profile.Keys)
        {
            bool supplied = data.Values.TryGetValue(key.Key, out var raw);
            var normalized = ValueNormalizer.Normalize(key, raw, supplied);
            values[key.Key] = normalized.Value;
            findings.AddRange(normalized.Findings);
            if (key.Required && normalized.Value.IsValid && (!normalized.Value.IsPopulated || (key.Kind == "checkbox" && normalized.Value.Boolean != true) || (key.Kind == "text" && string.IsNullOrWhiteSpace(normalized.Value.Text))))
                Error("E_REQUIRED", "Required value is missing, blank or unchecked.", key.Key);
        }
        // Required/presence failures are established before arithmetic, independent of rule order.
        var presenceResults = profile.Rules.Where(r => r.Kind is "requiredIf" or "allOrNone")
            .ToDictionary(r => r.Id, r => EvaluateRule(r, definitions, values, data.Context), StringComparer.Ordinal);
        var requiredFailures = new HashSet<string>(findings.Where(f => f.Code == "E_REQUIRED" && f.Key is not null).Select(f => f.Key!), StringComparer.Ordinal);
        foreach (var failure in presenceResults.Values.SelectMany(x => x.Findings))
            if (failure.Key is not null) requiredFailures.Add(failure.Key);
        foreach (var rule in profile.Rules)
        {
            var evaluated = presenceResults.TryGetValue(rule.Id, out var presence)
                ? presence : EvaluateRule(rule, definitions, values, data.Context, requiredFailures);
            findings.AddRange(evaluated.Findings);
            evaluations.Add(new(rule.Id, evaluated.State));
        }
        if (!generic) ValidateInitialContext(profile, data.Context!, values, findings);
        return new(profile, data, values, findings.ToArray(), evaluations.ToArray());
    }

    public static (IReadOnlyList<Finding> Findings, string State) EvaluateRule(RuleDefinition rule,
        IReadOnlyDictionary<string, KeyDefinition> definitions, IReadOnlyDictionary<string, NormalizedValue> values, FilingContext? context,
        IReadOnlySet<string>? failedRequiredKeys = null)
    {
        var local = new List<Finding>();
        void Error(string code, string message, string? key = null) => local.Add(new(code, "data", message, key, RuleId: rule.Id, Severity: rule.Severity));
        if (rule.When is { } when)
        {
            if (when.Context is { } contextKey)
            {
                if (context is null) { Error("E_CONTEXT", "A context condition cannot run for context-free data."); return (local.AsReadOnly(), "skipped_invalid_operand"); }
                string actual = contextKey switch { "taxYear" => context.TaxYear, "quarter" => context.Quarter, _ => context.LiabilityReporting };
                if (actual != when.EqualsValue.GetString()) return (Array.Empty<Finding>(), "inactive");
            }
            else
            {
                var actual = values[when.Key!];
                if (!actual.IsValid) return (Array.Empty<Finding>(), "skipped_invalid_operand");
                var expected = ValueNormalizer.Normalize(definitions[when.Key!], when.EqualsValue, true).Value;
                if (!ValueNormalizer.Equivalent(actual, expected)) return (Array.Empty<Finding>(), "inactive");
            }
        }
        IEnumerable<string> operands = rule.Kind switch
        {
            "sum" => new[] { rule.Total! }.Concat(rule.Terms!.Select(x => x.Key)),
            "equal" => [rule.Left!, rule.Right!], _ => rule.Keys!
        };
        if (operands.Any(x => !values[x].IsValid)) return (Array.Empty<Finding>(), "skipped_invalid_operand");
        if (operands.Any(x => failedRequiredKeys?.Contains(x) == true)) return (Array.Empty<Finding>(), "skipped_required_operand");
        if (operands.Any(x => definitions[x].Required && (!values[x].IsPopulated || (definitions[x].Kind == "checkbox" && values[x].Boolean != true))))
            return (Array.Empty<Finding>(), "skipped_required_operand");
        switch (rule.Kind)
        {
            case "sum":
                BigInteger total = BigInteger.Zero;
                foreach (var term in rule.Terms!) total += (BigInteger)(values[term.Key].Cents ?? 0) * term.Sign;
                if (total != (values[rule.Total!].Cents ?? 0)) Error("E_SUM", "Supplied total does not equal the exact sum of its terms.", rule.Total);
                break;
            case "equal": if (!ValueNormalizer.Equivalent(values[rule.Left!], values[rule.Right!])) Error("E_EQUAL", "Supplied values do not agree.", rule.Left); break;
            case "requiredIf":
                foreach (var key in rule.Keys!)
                    if (!values[key].IsPopulated || (definitions[key].Kind == "text" && string.IsNullOrWhiteSpace(values[key].Text))) Error("E_REQUIRED_IF", "Conditionally required value is missing or blank.", key);
                break;
            case "allOrNone":
                if (rule.Keys!.Any(k => values[k].IsPopulated))
                    foreach (var key in rule.Keys!.Where(k => !values[k].IsPopulated)) Error("E_ALL_OR_NONE", "Related values must be supplied together.", key);
                break;
        }
        return (local.AsReadOnly(), local.Count == 0 ? "passed" : "failed");
    }

    private static void ValidateInitialContext(ProfileDefinition profile, FilingContext context,
        IReadOnlyDictionary<string, NormalizedValue> values, List<Finding> findings)
    {
        void Error(string code, string message, string? key = null) => findings.Add(new(code, "data", message, key));
        if (values.TryGetValue("quarter", out var quarter) && quarter.IsValid && quarter.IsPopulated && quarter.Text != context.Quarter)
            Error("E_CONTEXT_QUARTER", "Printed quarter does not agree with filing context.", "quarter");
        if (values.TryGetValue("calendarYear", out var year) && year.IsValid && year.IsPopulated && year.Text != context.TaxYear)
            Error("E_CONTEXT_YEAR", "Printed calendar year does not agree with filing context.", "calendarYear");
        if (profile.Id == "irs-941sb" && context.LiabilityReporting != "semiweekly") Error("E_CONTEXT_SCHEDULE_B", "Schedule B requires semiweekly filing context.");
        if (profile.Id != "irs-941") return;
        if (values.TryGetValue("line16.depositSchedule", out var schedule) && schedule.IsValid && schedule.Text is "monthly" or "semiweekly" && schedule.Text != context.LiabilityReporting)
            Error("E_CONTEXT_SCHEDULE", "Printed deposit schedule does not agree with filing context.", "line16.depositSchedule");
        string[] monthly = ["line16.month1", "line16.month2", "line16.month3", "line16.total"];
        if (context.LiabilityReporting != "monthly")
            foreach (string key in monthly)
                if (values.TryGetValue(key, out var value) && value.IsPopulated) Error("E_INACTIVE_SECTION", "Inactive monthly section must be blank.", key);
    }
}
