using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PdfForm.Publication.Tests")]
namespace PdfForm.Publication;

/// <summary>Finite per-attempt byte limits, including result and completion records.</summary>
public sealed record PublicationLimits(long MaxArtifactBytes = 64L * 1024 * 1024,
    long MaxTotalBytes = 256L * 1024 * 1024, int MaxArtifacts = 4,
    int MaxManifestBytes = 1024 * 1024)
{
    internal void Validate()
    {
        if (MaxArtifactBytes is < 1 or > 256L * 1024 * 1024 ||
            MaxTotalBytes < MaxArtifactBytes || MaxTotalBytes > 1024L * 1024 * 1024 ||
            MaxArtifacts is < 1 or > 16 || MaxManifestBytes is < 256 or > 10 * 1024 * 1024)
            throw new PublicationException("E_PUBLICATION_LIMIT", 2);
    }
}

/// <summary>Verifier must independently inspect the reopened candidate; no PDF-engine assumptions are made here.</summary>
public sealed record CandidateArtifact(string Id, string Path, ReadOnlyMemory<byte> Bytes,
    Func<Stream, bool> Verify);
public sealed record ArtifactDescriptor(string Id, string Path, long Bytes, string Sha256);
public sealed record ExpectedArtifact(string Id, string Path);
public sealed record SingleFileRequest(string Destination, IReadOnlyList<string> InputPaths,
    CandidateArtifact Artifact, bool Force = false, string? ResultPath = null,
    ReadOnlyMemory<byte>? ResultBytes = null);
public sealed record PackageRequest(string Destination, string RequestId, string ToolBuild,
    IReadOnlyList<string> InputPaths, IReadOnlyList<CandidateArtifact> Artifacts,
    Func<IReadOnlyList<ArtifactDescriptor>, byte[]> CreateResult);

/// <summary>No-write preflight diagnostics. Readiness is a point-in-time check, not a lease or rendering approval.</summary>
public sealed record PublicationPreflightOutcome(bool IsReady, int ExitCode, string? ErrorCode);

/// <summary>Error codes are value-free. Output descriptors are internal payload-location metadata, not automatically safe diagnostics.</summary>
public sealed record PublicationOutcome(bool Success, int ExitCode, string ArtifactState,
    string? ErrorCode, IReadOnlyList<ArtifactDescriptor> Outputs, bool CleanupIncomplete = false);
public sealed record ReconciliationOutcome(bool IsComplete, string? ErrorCode,
    IReadOnlyList<ArtifactDescriptor> Outputs);

internal sealed class PublicationException(string code, int exitCode = 4) : Exception(code)
{
    internal string Code { get; } = code;
    internal int ExitCode { get; } = exitCode;
}
internal enum PublicationPoint
{
    StageCreated, BeforeArtifactWrite, AfterArtifactWrite, BeforeVerify, AfterVerify,
    BeforeResultWrite, BeforeCompletionWrite, BeforeCommit, AfterCommit, BeforeReportCommit
}
// Internal, per-attempt fault seams. These run after successful creation has been journaled,
// but before identity acquisition; no path or callback exception text enters diagnostics.
internal enum StageCreationPoint { RootDirectory, ChildDirectory, File }
internal enum StageWriteKind { Artifact, Manifest }
