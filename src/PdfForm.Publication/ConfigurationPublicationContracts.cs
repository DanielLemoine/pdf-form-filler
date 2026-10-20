namespace PdfForm.Publication;

/// <summary>
/// Exact configuration payloads, already validated by the caller. Candidate verifiers inspect
/// reopened staged bytes. CreateResult serializes Core's configuration result using the supplied
/// verified output descriptors and both full Core template/profile identities, even for configure
/// where the observed template is not a payload. It must not create a pdfform.result/2 run receipt.
/// </summary>
public sealed record ConfigurationPackageRequest(
    string Destination, string RequestId, string ToolBuild, string Operation,
    IReadOnlyList<string> InputPaths,
    ReadOnlyMemory<byte> CatalogJson, ReadOnlyMemory<byte> ProfileJson,
    Func<Stream, bool> VerifyCatalog, Func<Stream, bool> VerifyProfile,
    Func<IReadOnlyList<ArtifactDescriptor>, byte[]> CreateResult,
    ReadOnlyMemory<byte>? TemplatePdf = null, Func<Stream, bool>? VerifyTemplate = null);
