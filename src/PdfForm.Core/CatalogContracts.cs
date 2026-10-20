using System.Text.Json.Serialization;

namespace PdfForm.Core;

public sealed record CatalogFileIdentity([property: JsonRequired] string Path, [property: JsonRequired] long Bytes, [property: JsonRequired] string Sha256);
public sealed record CatalogProfileIdentity([property: JsonRequired] string Path, [property: JsonRequired] long Bytes, [property: JsonRequired] string Sha256,
    [property: JsonRequired] string Id, [property: JsonRequired] string Revision, [property: JsonRequired] int ProfileVersion);
public sealed record ConfigurationIssue([property: JsonRequired] string Code, [property: JsonRequired] string Capability, string? FieldId = null);
public sealed record ConfigurationReview([property: JsonRequired] string State, [property: JsonRequired] ConfigurationIssue[] Unresolved);

/// <summary>Private configuration facts, never permission to override independently observed PDF features.</summary>
public sealed record TextFieldPolicy
{
    public required string Target { get; init; }
    public required int Page { get; init; }
    public required double[] Rectangle { get; init; }
    public required string Strategy { get; init; }
    public required string Layout { get; init; }
    public required string ResourceName { get; init; }
    public required string BaseFont { get; init; }
    public required string Subtype { get; init; }
    public required string Encoding { get; init; }
    public required double FontSize { get; init; }
    public string? ProgramSha256 { get; init; }
    public required string MetricsSha256 { get; init; }
    public required string Repertoire { get; init; }
}
public sealed record CheckboxFieldPolicy
{
    public required string Target { get; init; }
    public required int Page { get; init; }
    public required double[] Rectangle { get; init; }
    public required string OnState { get; init; }
    public required string Mark { get; init; }
}
public sealed record FormPolicyDefinition
{
    public required string Id { get; init; }
    public required string Normalization { get; init; }
    public required TextFieldPolicy[] TextFields { get; init; }
    public required CheckboxFieldPolicy[] CheckboxFields { get; init; }
}
public sealed record FormCatalogDefinition
{
    public required string Schema { get; init; }
    public required CatalogFileIdentity Template { get; init; }
    public required CatalogProfileIdentity Profile { get; init; }
    public required ConfigurationReview Review { get; init; }
    public required FormPolicyDefinition Policy { get; init; }
}

/// <summary>Value-free assessment; actual inspection belongs to the PDF owner, not catalog parsing.</summary>
public sealed record CompatibilityDocument
{
    public string Schema { get; init; } = "pdfform.compatibility/1";
    public required string Status { get; init; }
    public required int ExitCode { get; init; }
    public string? TemplateSha256 { get; init; }
    public long? TemplateBytes { get; init; }
    public required IReadOnlyDictionary<string, long> Counts { get; init; }
    public required string ConfigurationState { get; init; }
    public required ConfigurationIssue[] Issues { get; init; }
    public required string[] MissingEngineFeatures { get; init; }
    public required string[] RequiredConfiguration { get; init; }
    public required string[] NotAssessed { get; init; }
}

/// <summary>Configuration publication receipt; never a substitute for a successful run/render receipt.</summary>
public sealed record ConfigurationResultDocument
{
    public string Schema { get; init; } = "pdfform.configuration-result/1";
    public required string Operation { get; init; }
    public required string Status { get; init; }
    public required int ExitCode { get; init; }
    public required string ArtifactState { get; init; }
    public required IReadOnlyDictionary<string, string> ValidationLevels { get; init; }
    public CatalogFileIdentity? Template { get; init; }
    public CatalogProfileIdentity? Profile { get; init; }
    public required OutputArtifact[] Outputs { get; init; }
    public required Finding[] Errors { get; init; }
    public required Finding[] Warnings { get; init; }
    public string FilingReadiness { get; init; } = "not_assessed";
}
