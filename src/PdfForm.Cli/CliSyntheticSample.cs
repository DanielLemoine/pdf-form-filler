using System.Globalization;
using System.Numerics;
using System.Text.Json;
using PdfForm.Core;

namespace PdfForm.Cli;

public sealed class CliSyntheticSampleResult
{
    private readonly byte[]? bytes;
    internal CliSyntheticSampleResult(byte[]? bytes, IEnumerable<Finding> findings, int failureExitCode = 3)
    { this.bytes = bytes?.ToArray(); Findings = Array.AsReadOnly(findings.ToArray()); ExitCode = Ready ? 0 : failureExitCode; }
    public IReadOnlyList<Finding> Findings { get; }
    public bool Ready => bytes is not null && !Findings.Any(f => f.Severity == "error");
    public int ExitCode { get; }
    public string Status => Ready ? "supported" : ExitCode == 4 ? "internal_error" : "unsupported";
    public byte[]? CopyData() => bytes?.ToArray();
}

/// <summary>Finite deterministic synthetic candidate construction. Never reads PDF values/defaults,
/// publishes files, claims rendering occurred, or solves arbitrary patterns/business constraints.
/// The caller must supply the admitted PDF owner's fit and whole-sample compatibility checks.</summary>
public static class CliSyntheticSample
{
    private const int MaximumRulePasses = 8;
    private const int MaximumCandidateChecks = 20_000;

    /// <summary>Adapts the existing finding-returning fit seam without treating an internal failure
    /// as an ordinary rejected value. Build catches this private signal and preserves its exit 4.</summary>
    public static bool AcceptFit(IReadOnlyList<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var internalFailures = InternalFailures(findings);
        if (internalFailures.Length != 0) throw new SampleInternalFailure(internalFailures);
        return !findings.Any(f => f.Severity == "error");
    }

    public static CliSyntheticSampleResult Build(ProfileDefinition profile, FilingContext? legacyContext,
        Func<KeyDefinition, JsonElement, bool> fits,
        Func<ReadOnlyMemory<byte>, IReadOnlyList<Finding>> checkWholeSample)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(fits); ArgumentNullException.ThrowIfNull(checkWholeSample);
        bool generic = profile.Schema == "pdfform.profile/3";
        if (!generic && profile.Schema != "pdfform.profile/2") return Fail("E_SAMPLE_PROFILE", "Use an admitted supported profile before requesting synthetic input.");
        if (!generic && legacyContext is null) return Fail("E_SAMPLE_CONTEXT_REQUIRED", "Supply an explicit synthetic filing context for a version 2 profile.");
        if (generic && legacyContext is not null) return Fail("E_SAMPLE_CONTEXT_UNEXPECTED", "Version 3 synthetic input must omit filing context.");
        if (profile.Keys.Length > Limits.MaxKeysOrTargets || profile.Rules.Length > Limits.MaxRules) return Fail("E_SAMPLE_LIMIT", "Profile exceeds the supported sample bounds.");
        if (profile.Keys.Select(k => k.Key).Distinct(StringComparer.Ordinal).Count() != profile.Keys.Length) return Fail("E_SAMPLE_PROFILE", "Profile contains conflicting key identities.");
        var definitions = profile.Keys.ToDictionary(k => k.Key, StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var findings = new List<Finding>(); int candidateChecks = 0;
        bool TrySet(KeyDefinition key, JsonElement raw)
        {
            // Invalid pattern/type candidates consume the same finite budget as successful
            // normalization. Otherwise a large profile could multiply rejected regex work.
            if (++candidateChecks > MaximumCandidateChecks) throw new SampleFailure("E_SAMPLE_BUDGET", "Synthetic candidate search reached its finite bound; supply values explicitly.");
            var normalized = ValueNormalizer.Normalize(key, raw, true);
            if (normalized.Findings.Any(f => f.Code == "E_PATTERN_TIMEOUT"))
                throw new SampleFailure("E_SAMPLE_BUDGET", "A candidate pattern exhausted its execution budget; supply input explicitly.");
            if (normalized.Findings.Any(f => f.Severity == "error") || !normalized.Value.IsValid ||
                key.Required && (!normalized.Value.IsPopulated || key.Kind == "checkbox" && normalized.Value.Boolean != true ||
                    key.Kind == "text" && string.IsNullOrWhiteSpace(normalized.Value.Text))) return false;
            if (!fits(key, raw)) return false;
            values[key.Key] = raw.Clone(); return true;
        }
        bool Supply(string keyName)
        {
            var key = definitions[keyName];
            if (profile.GetSampleHint(keyName) is { } hint && TrySet(key, hint)) return true;
            foreach (var candidate in Candidates(key, legacyContext)) if (TrySet(key, candidate)) return true;
            return false;
        }
        bool Populated(string key) => values.TryGetValue(key, out var raw) && ValueNormalizer.Normalize(definitions[key], raw, true).Value.IsPopulated;
        bool Active(RuleDefinition rule)
        {
            if (rule.When is not { } when) return true;
            if (when.Context is { } context)
            {
                if (legacyContext is null) throw new SampleFailure("E_SAMPLE_CONTEXT_RULE", "A context-dependent rule cannot generate context-free synthetic input.");
                string actual = context switch { "taxYear" => legacyContext.TaxYear, "quarter" => legacyContext.Quarter, _ => legacyContext.LiabilityReporting };
                return actual == when.EqualsValue.GetString();
            }
            if (!definitions.TryGetValue(when.Key!, out var definition)) throw new SampleFailure("E_SAMPLE_RULE", "Rule refers to an undeclared key.");
            var actualValue = ValueNormalizer.Normalize(definition, values.GetValueOrDefault(when.Key!), values.ContainsKey(when.Key!)).Value;
            return ValueNormalizer.Equivalent(actualValue, ValueNormalizer.Normalize(definition, when.EqualsValue, true).Value);
        }
        byte[] Encode()
        {
            var sorted = values.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            return generic
                ? JsonSerializer.SerializeToUtf8Bytes(new { schema = "pdfform.data/3", profile = profile.Identity, values = sorted }, StrictJson.Options)
                : JsonSerializer.SerializeToUtf8Bytes(new { schema = "pdfform.data/2", profile = profile.Identity, context = legacyContext, values = sorted }, StrictJson.Options);
        }
        try
        {
            foreach (var key in profile.Keys.OrderBy(k => k.Key, StringComparer.Ordinal))
                if (!Supply(key.Key))
                {
                    if (key.Required) return Fail("E_SAMPLE_NO_CANDIDATE", "No bounded synthetic value satisfies this key's type, pattern, glyph and fit constraints; supply input explicitly.", key.Key);
                    findings.Add(new("W_SAMPLE_OPTIONAL_OMITTED", "sample", "No bounded synthetic value was found for this optional key; it is omitted.", key.Key, Severity: "warning"));
                }
            bool stable = false;
            for (int pass = 0; pass < MaximumRulePasses; pass++)
            {
                byte[] before = Encode();
                foreach (var rule in profile.Rules)
                {
                    if (!Active(rule)) continue;
                    if (rule.Kind is "requiredIf" or "allOrNone")
                    {
                        if (rule.Kind == "allOrNone" && !rule.Keys!.Any(Populated)) continue;
                        foreach (string key in rule.Keys!)
                            if (!Populated(key) && !Supply(key)) return Fail("E_SAMPLE_RULE", "A presence rule needs a value that the bounded synthetic generator cannot supply.", key);
                    }
                    else if (rule.Kind == "equal")
                    {
                        string left = rule.Left!, right = rule.Right!;
                        var l = ValueNormalizer.Normalize(definitions[left], values.GetValueOrDefault(left), values.ContainsKey(left)).Value;
                        var r = ValueNormalizer.Normalize(definitions[right], values.GetValueOrDefault(right), values.ContainsKey(right)).Value;
                        if (ValueNormalizer.Equivalent(l, r)) continue;
                        if (values.TryGetValue(left, out var a) && TrySet(definitions[right], a)) continue;
                        if (values.TryGetValue(right, out var b) && TrySet(definitions[left], b)) continue;
                        return Fail("E_SAMPLE_RULE", "An equality rule has incompatible synthetic constraints; supply input explicitly.", left);
                    }
                    else if (rule.Kind == "sum")
                    {
                        BigInteger total = BigInteger.Zero;
                        foreach (var term in rule.Terms!)
                        {
                            var amount = ValueNormalizer.Normalize(definitions[term.Key], values.GetValueOrDefault(term.Key), values.ContainsKey(term.Key)).Value.Cents ?? 0;
                            total += (BigInteger)amount * term.Sign;
                        }
                        var absolute = BigInteger.Abs(total);
                        string money = (total.Sign < 0 ? "-" : "") + (absolute / 100).ToString(CultureInfo.InvariantCulture) + "." + (absolute % 100).ToString("D2", CultureInfo.InvariantCulture);
                        if (!TrySet(definitions[rule.Total!], Text(money))) return Fail("E_SAMPLE_RULE", "The exact synthetic sum cannot satisfy its total field constraints.", rule.Total);
                    }
                    else return Fail("E_SAMPLE_RULE_UNSUPPORTED", "A rule requires sample generation outside the supported bounded strategy.");
                }
                if (before.AsSpan().SequenceEqual(Encode())) { stable = true; break; }
            }
            if (!stable) return Fail("E_SAMPLE_RULE_BUDGET", "Synthetic rules did not settle within the bounded strategy; supply input explicitly.");
            byte[] encoded = Encode();
            if (encoded.Length > Limits.MaxJsonBytes) return Fail("E_SAMPLE_LIMIT", "Synthetic input exceeds the existing JSON byte bound.");
            var parsed = DataValidator.ParseData(encoded);
            if (!parsed.IsValid) return new(null, parsed.Findings.Prepend(new Finding("E_SAMPLE_UNSATISFIED", "sample", "Synthetic input could not satisfy the admitted data contract.")));
            var validation = DataValidator.Validate(profile, parsed.Value!);
            if (!validation.IsValid) return new(null, validation.Findings.Prepend(new Finding("E_SAMPLE_UNSATISFIED", "sample", "The bounded strategy cannot satisfy these profile rules; supply input explicitly.")));
            var compatibility = checkWholeSample(encoded);
            var internalFailures = InternalFailures(compatibility);
            if (internalFailures.Length != 0) return new(null, internalFailures, failureExitCode: 4);
            if (compatibility.Any(f => f.Severity == "error")) return new(null, compatibility.Prepend(new Finding("E_SAMPLE_FIT", "sample", "Synthetic input does not satisfy the admitted whole-form glyph and fit policy.")));
            return new(encoded, findings.Concat(validation.Findings).Concat(compatibility));
        }
        catch (SampleInternalFailure ex) { return new(null, ex.Findings, failureExitCode: 4); }
        catch (SampleFailure ex) { return Fail(ex.Code, ex.Message); }
        catch { return new(null, [new("E_SAMPLE_INTERNAL", "sample", "Synthetic generation or its compatibility check could not complete safely.")], failureExitCode: 4); }
    }

    private static IEnumerable<JsonElement> Candidates(KeyDefinition key, FilingContext? context)
    {
        if (key.Kind == "checkbox") { yield return JsonSerializer.SerializeToElement(key.Required); yield break; }
        if (key.Kind == "oneOf")
        {
            var preferred = context is null ? Array.Empty<string>() : new[] { context.Quarter, context.LiabilityReporting, context.TaxYear };
            foreach (string option in preferred.Concat(key.Options is null ? Enumerable.Empty<string>() : key.Options.Keys.Order(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal))
                if (key.Options?.ContainsKey(option) == true) yield return Text(option);
            yield break;
        }
        if (key.Kind == "money") { yield return Text("0.00"); yield return Text("1.00"); yield break; }
        if (key.Kind == "date")
        { yield return Text((context?.TaxYear ?? "2000") + "-01-02"); yield return Text("2000-01-02"); yield break; }
        if (key.Kind == "split")
        {
            int count = key.Parts?.Sum() ?? 0;
            if (count is > 0 and <= Limits.MaxTextScalars) { yield return Text(new string('0', count)); yield return Text(new string('1', count)); yield return Text(new string('A', count)); }
            yield break;
        }
        if (key.Kind != "text") yield break;
        foreach (string value in new[] { "SYNTHETIC", "Sample", "A", "0", "1", "X" }) yield return Text(value);
        if (context is not null) foreach (string value in new[] { context.TaxYear, context.Quarter, context.LiabilityReporting }) yield return Text(value);
        int maximum = Math.Min(key.MaxLength ?? Limits.MaxTextScalars, Limits.MaxTextScalars);
        foreach (int count in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 16, 20, 24, 32, 64, 128, 256, maximum }.Where(n => n > 0 && n <= maximum).Distinct())
            foreach (char letter in new[] { '0', '1', 'A' }) yield return Text(new string(letter, count));
    }
    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);
    private static Finding[] InternalFailures(IReadOnlyList<Finding> findings) => findings
        .Where(f => f.Severity == "error" && (f.Code is "E_FIT_INTERNAL" or "E_FILL_INTERNAL" or "E_SAMPLE_INTERNAL" || f.Stage == "internal"))
        .Select(f => new Finding(f.Code is "E_FIT_INTERNAL" or "E_FILL_INTERNAL" or "E_SAMPLE_INTERNAL" ? f.Code : "E_SAMPLE_INTERNAL",
            "internal", f.Code is "E_FIT_INTERNAL" or "E_FILL_INTERNAL" or "E_SAMPLE_INTERNAL" ? f.Code : "E_SAMPLE_INTERNAL"))
        .Distinct().ToArray();
    private static CliSyntheticSampleResult Fail(string code, string message, string? key = null) => new(null, [new(code, "sample", message, key)]);
    private sealed class SampleFailure(string code, string message) : Exception(message) { internal string Code { get; } = code; }
    private sealed class SampleInternalFailure(IReadOnlyList<Finding> findings) : Exception("E_SAMPLE_INTERNAL")
    { internal IReadOnlyList<Finding> Findings { get; } = findings; }
}
