using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfForm.Core;

public static class Limits
{
    public const int MaxJsonBytes = 10 * 1024 * 1024;
    public const int MaxDepth = 32;
    public const int MaxKeysOrTargets = 10_000;
    public const int MaxRules = 1_000;
    public const int MaxTextScalars = 256;
    public const int MaxTemplateBytes = 50 * 1024 * 1024;
    public const int MaxTemplatePages = 100;
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
}

public sealed record Finding(string Code, string Stage, string Message, string? Key = null,
    string? Field = null, string? RuleId = null, string Severity = "error");
public sealed record RuleEvaluation(string RuleId, string State);
public sealed record ParseResult<T>(T? Value, IReadOnlyList<Finding> Findings) where T : class
{
    public bool IsValid => Value is not null && !Findings.Any(x => x.Severity == "error");
}

public sealed record TemplateDefinition
{
    public required string[] Sha256Allowlist { get; init; }
    public required string Title { get; init; }
    public required int PageCount { get; init; }
    public required string StructureFingerprintV1 { get; init; }
}
public sealed record ProfileOptions
{
    public required string ZeroAmounts { get; init; }
    public DateCellLayout[]? DateCellLayouts { get; init; }
}
public sealed record DateCellLayout
{
    public required string Target { get; init; }
    public required string TemplateSha256 { get; init; }
    public required double[][] Cells { get; init; }
}
public sealed record CheckboxAssignment
{
    public required string Target { get; init; }
    public string? On { get; init; }
}
public sealed record KeyDefinition
{
    public required string Key { get; init; }
    public required string Kind { get; init; }
    public required bool Required { get; init; }
    public required bool Sensitive { get; init; }
    public string? Section { get; init; }
    public string? Label { get; init; }
    public string? Pattern { get; init; }
    public string? Strip { get; init; }
    public string? Transform { get; init; }
    public int? MaxLength { get; init; }
    public JsonElement Targets { get; init; }
    public int[]? Parts { get; init; }
    public string? Target { get; init; }
    public string? Dollars { get; init; }
    public string? Cents { get; init; }
    public bool? AllowNegative { get; init; }
    public bool? Thousands { get; init; }
    public string? Zero { get; init; }
    public string? Format { get; init; }
    public string? On { get; init; }
    public Dictionary<string, CheckboxAssignment[]>? Options { get; init; }

    public IEnumerable<string> EnumerateTargets()
    {
        if (Targets.ValueKind == JsonValueKind.Array)
            foreach (var t in Targets.EnumerateArray())
                if (t.ValueKind == JsonValueKind.String) yield return t.GetString()!;
                else if (t.ValueKind == JsonValueKind.Array)
                    foreach (var p in t.EnumerateArray())
                        if (p.ValueKind == JsonValueKind.String) yield return p.GetString()!;
        if (Target is not null) yield return Target;
        if (Dollars is not null) yield return Dollars;
        if (Cents is not null) yield return Cents;
        if (Options is not null)
            foreach (var assignment in Options.Values.SelectMany(x => x)) yield return assignment.Target;
    }
}
public sealed record RuleTerm
{
    public required string Key { get; init; }
    public required int Sign { get; init; }
}
public sealed record RuleCondition
{
    public string? Key { get; init; }
    public string? Context { get; init; }
    [JsonPropertyName("equals")] public required JsonElement EqualsValue { get; init; }
}
public sealed record RuleDefinition
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Severity { get; init; }
    public string? Total { get; init; }
    public RuleTerm[]? Terms { get; init; }
    public string? Left { get; init; }
    public string? Right { get; init; }
    public string[]? Keys { get; init; }
    public RuleCondition? When { get; init; }
}
public sealed record ProfileDefinition
{
    public required string Schema { get; init; }
    public required string Id { get; init; }
    public required string Revision { get; init; }
    public required int ProfileVersion { get; init; }
    public required string Title { get; init; }
    public required TemplateDefinition Template { get; init; }
    public required string[] Capabilities { get; init; }
    public required string[] FilingYears { get; init; }
    public required ProfileOptions Options { get; init; }
    public required KeyDefinition[] Keys { get; init; }
    public required RuleDefinition[] Rules { get; init; }
    public required JsonElement[] Unmapped { get; init; }
    public JsonElement Source { get; init; }
    public JsonElement Extensions { get; init; }
    [JsonIgnore] public string Identity => $"{Id}@{Revision}";
    public IEnumerable<string> EnumerateTargets() => Keys.SelectMany(x => x.EnumerateTargets());
}
public sealed record FilingContext
{
    public required string TaxYear { get; init; }
    public required string Quarter { get; init; }
    public required string LiabilityReporting { get; init; }
}
public sealed record DataDocument
{
    public required string Schema { get; init; }
    public required string Profile { get; init; }
    public required FilingContext Context { get; init; }
    public required Dictionary<string, JsonElement> Values { get; init; }
    public JsonElement Extensions { get; init; }
}
public enum ValuePresence { Missing, Blank, Supplied }
public sealed record NormalizedValue(string Kind, ValuePresence Presence, bool IsValid,
    string? Text = null, long? Cents = null, DateOnly? Date = null, bool? Boolean = null)
{
    public bool IsPopulated => Presence == ValuePresence.Supplied;
}
public sealed record ValidationReport(ProfileDefinition Profile, DataDocument Data,
    IReadOnlyDictionary<string, NormalizedValue> Values, IReadOnlyList<Finding> Findings,
    IReadOnlyList<RuleEvaluation> Rules)
{
    public bool IsValid => !Findings.Any(x => x.Severity == "error");
    public IReadOnlyDictionary<string, string> ValidationLevels => new Dictionary<string, string>
    { ["syntax"] = "passed", ["profile"] = "passed", ["data"] = IsValid ? "passed" : "failed", ["binding"] = "not_run", ["packet"] = "not_run" };
    public bool ReadyToRender => false;
    public string FilingReadiness => "not_assessed";
}
public sealed record JobDocument
{
    public required string Schema { get; init; }
    public required string RequestId { get; init; }
    public required string Policy { get; init; }
    public required JobMember[] Documents { get; init; }
    public required bool Flatten { get; init; }
    public PacketDefinition? Packet { get; init; }
}
public sealed record JobMember
{
    public required string Alias { get; init; }
    public required string Pdf { get; init; }
    public required string Profile { get; init; }
    public required string Data { get; init; }
    public required string Output { get; init; }
}
public sealed record PacketDefinition
{
    public required string Output { get; init; }
    public required PacketPageSelection[] Pages { get; init; }
}
public sealed record PacketPageSelection
{
    public required string Document { get; init; }
    public required int[] Pages { get; init; }
}
public sealed record ToolIdentity(string Name, string Version, string Build, string Engine, string Runtime, string SourceReference);
public sealed record ProfileIdentity(string Id, string Revision, int ProfileVersion, string Sha256);
public sealed record TemplateIdentity(string Id, string Sha256);
public sealed record OutputArtifact(string Id, string Path, long Bytes, string Sha256);
public sealed record ResultDocument
{
    public string Schema { get; init; } = "pdfform.result/2";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RequestId { get; init; }
    public required string Operation { get; init; }
    public required string Status { get; init; }
    public required int ExitCode { get; init; }
    public required string ArtifactState { get; init; }
    public required IReadOnlyDictionary<string, string> ValidationLevels { get; init; }
    public required ToolIdentity Tool { get; init; }
    public required ProfileIdentity[] Profiles { get; init; }
    public required TemplateIdentity[] Templates { get; init; }
    public required OutputArtifact[] Outputs { get; init; }
    public required Finding[] Errors { get; init; }
    public required Finding[] Warnings { get; init; }
    public required IReadOnlyDictionary<string, long> Stats { get; init; }
    public string FilingReadiness { get; init; } = "not_assessed";
}
