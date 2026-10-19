namespace PdfForm.Publication;

/// <summary>
/// Bounded publication into an existing caller-controlled directory. No PDF semantics, network calls,
/// permission changes, external cleanup, or release-qualification claims. Candidate verifiers are mandatory.
/// </summary>
public sealed class SafePublisher
{
    private readonly PublicationLimits limits;
    private readonly Action<PublicationPoint>? fault;
    private readonly Action<StageCreationPoint>? afterCreate;
    private readonly Action? beforePathAccess;
    public SafePublisher(PublicationLimits? limits = null) : this(limits, null) { }
    internal SafePublisher(PublicationLimits? limits, Action<PublicationPoint>? fault,
        Action<StageCreationPoint>? afterCreate = null, Action? beforePathAccess = null)
    { this.limits = limits ?? new(); this.fault = fault; this.afterCreate = afterCreate; this.beforePathAccess = beforePathAccess; }

    /// <summary>
    /// Checks target/input identities and create-new policy without writing or invoking candidate verification.
    /// It reserves nothing. PublishSingle repeats the same checks and rechecks its own guard at commit;
    /// callers must still render from the immutable input snapshots they validated.
    /// </summary>
    public PublicationPreflightOutcome PreflightSingle(string destination, IReadOnlyList<string> inputPaths,
        string? resultPath = null, bool force = false)
    {
        try
        {
            limits.Validate();
            _ = PrepareSinglePaths(destination, inputPaths, resultPath, force);
            return new(true, 0, null);
        }
        catch (PublicationException e) { return new(false, e.ExitCode, e.Code); }
        catch { return new(false, 4, "E_PUBLICATION_PREFLIGHT"); }
    }

    public PublicationOutcome PublishSingle(SingleFileRequest request)
    {
        OwnedStage? stage = null, reportStage = null;
        bool committed = false;
        var outputs = new List<ArtifactDescriptor>();
        try
        {
            limits.Validate();
            if ((request.ResultPath is null) != (request.ResultBytes is null)) throw new PublicationException("E_RESULT_ARGUMENT", 2);
            if (request.Artifact.Verify is null || !PublicationPaths.IsOpaqueId(request.Artifact.Id) ||
                !PublicationPaths.IsSafeRelativeOutput(request.Artifact.Path)) throw new PublicationException("E_OUTPUT_SET", 2);
            var paths = PrepareSinglePaths(request.Destination, request.InputPaths, request.ResultPath, request.Force);
            var destination = paths.Destination;
            var reportPath = paths.ReportPath;
            var guard = paths.Guard;
            var reportGuard = paths.ReportGuard;
            var bytes = Snapshot(request.Artifact.Bytes, limits.MaxArtifactBytes);
            byte[]? reportBytes = request.ResultBytes is null ? null : Snapshot(request.ResultBytes.Value, limits.MaxManifestBytes);
            if ((long)bytes.Length + (reportBytes?.Length ?? 0) > limits.MaxTotalBytes) throw new PublicationException("E_OUTPUT_LIMIT", 2);
            stage = new(destination, limits, afterCreate);
            stage.Create(bytes.LongLength);
            Point(PublicationPoint.StageCreated);
            var descriptor = WriteArtifact(stage, request.Artifact with { Bytes = bytes }) with { Path = Path.GetFileName(destination) }; outputs.Add(descriptor);
            // Prepare standalone diagnostic bytes before publishing the PDF; it remains a separate transaction.
            string? reportCandidate = null;
            if (reportPath is not null)
            {
                Point(PublicationPoint.BeforeResultWrite);
                reportStage = new(reportPath, limits, afterCreate);
                reportStage.Create(reportBytes!.LongLength);
                reportCandidate = reportStage.Write("result.json", reportBytes, StageWriteKind.Manifest);
            }
            Point(PublicationPoint.BeforeCommit);
            guard.Recheck(); stage.AssertOwned(); reportStage?.AssertOwned();
            var candidatePath = Path.Combine(stage.Root, request.Artifact.Path.Replace('/', Path.DirectorySeparatorChar));
            OwnedStage.CheckHash(candidatePath, descriptor);
            NativeFileSystem.CommitCreateNew(candidatePath, destination);
            committed = true;
            stage.MarkTransferred(candidatePath);
            // Only this known successful transfer permits cleanup to skip the absent candidate.
            Point(PublicationPoint.AfterCommit);
            if (reportPath is not null)
            {
                Point(PublicationPoint.BeforeReportCommit);
                reportGuard!.Recheck(); reportStage!.AssertOwned();
                OwnedStage.CheckHash(reportCandidate!, CompletionProtocol.BytesDescriptor("result", "result.json", reportBytes!));
                if (PublicationPaths.Exists(reportPath)) throw new PublicationException("E_RESULT_PUBLICATION");
                NativeFileSystem.CommitCreateNew(reportCandidate!, reportPath);
                reportStage.MarkTransferred(reportCandidate!);
            }
            bool clean = stage.Cleanup() & (reportStage?.Cleanup() ?? true);
            return new(true, 0, "published", null, outputs.AsReadOnly(), !clean);
        }
        catch (Exception e)
        {
            bool clean = (stage?.Cleanup() ?? true) & (reportStage?.Cleanup() ?? true);
            return Failure(e, committed, clean, outputs);
        }
    }

    public PublicationOutcome PublishPackage(PackageRequest request)
    {
        OwnedStage? stage = null;
        bool committed = false;
        var outputs = new List<ArtifactDescriptor>();
        try
        {
            limits.Validate();
            if (!PublicationPaths.IsOpaqueId(request.RequestId) || !PublicationPaths.IsBuildId(request.ToolBuild))
                throw new PublicationException("E_REQUEST_ID", 2);
            if (request.Artifacts.Count < 1 || request.Artifacts.Count > limits.MaxArtifacts) throw new PublicationException("E_OUTPUT_SET", 2);
            var artifacts = request.Artifacts.ToArray();
            var expected = artifacts.Select(a => new ExpectedArtifact(a.Id, a.Path)).ToArray();
            CompletionProtocol.ValidateExpected(expected, limits);
            if (artifacts.Any(a => a.Verify is null)) throw new PublicationException("E_CANDIDATE_VERIFY", 2);
            var destination = PublicationPaths.Normalize(request.Destination);
            var inputPaths = PathGuard.NormalizeInputs(request.InputPaths);
            beforePathAccess?.Invoke();
            NativeFileSystem.RequirePublicationTarget(destination);
            if (PublicationPaths.IsStagingPath(destination)) throw new PublicationException("E_OUTPUT_PATH", 2);
            var guard = new PathGuard(inputPaths, [destination]);
            if (PublicationPaths.Exists(destination)) throw new PublicationException("E_DESTINATION_EXISTS", 2);
            long planned = checked(artifacts.Sum(a => (long)a.Bytes.Length) + 2L * limits.MaxManifestBytes);
            if (planned > limits.MaxTotalBytes) throw new PublicationException("E_OUTPUT_LIMIT", 2);
            var candidates = artifacts.Select(a => a with { Bytes = Snapshot(a.Bytes, limits.MaxArtifactBytes) }).ToArray();
            stage = new(destination, limits, afterCreate);
            stage.Create(planned);
            Point(PublicationPoint.StageCreated);
            foreach (var artifact in candidates) outputs.Add(WriteArtifact(stage, artifact));
            Point(PublicationPoint.BeforeResultWrite);
            var result = Snapshot(request.CreateResult(outputs.AsReadOnly()), limits.MaxManifestBytes);
            CompletionProtocol.ValidateResult(result, request.RequestId, request.ToolBuild, outputs);
            var resultDescriptor = CompletionProtocol.BytesDescriptor("result", "result.json", result);
            stage.Write("result.json", result, StageWriteKind.Manifest);
            Point(PublicationPoint.BeforeCompletionWrite);
            var completion = CompletionProtocol.Create(request.RequestId, request.ToolBuild, resultDescriptor, outputs);
            if (completion.Length > limits.MaxManifestBytes) throw new PublicationException("E_MANIFEST_LIMIT");
            stage.Write("completion.json", completion, StageWriteKind.Manifest);
            var anchors = new ProducerAnchors(outputs.ToArray(), resultDescriptor,
                CompletionProtocol.BytesDescriptor("completion", "completion.json", completion));
            Point(PublicationPoint.BeforeCommit);
            guard.Recheck(); stage.AssertOwned();
            var verified = CompletionProtocol.ReconcileStaged(stage, request.RequestId, request.ToolBuild, expected, limits, anchors);
            if (!verified.IsComplete) throw new PublicationException(verified.ErrorCode ?? "E_COMPLETION_INVALID");
            // Exactly one same-filesystem create-new directory commit. Never merge into an existing destination.
            NativeFileSystem.CommitCreateNew(stage.Root, destination);
            committed = true; stage.Committed = true;
            Point(PublicationPoint.AfterCommit);
            return new(true, 0, "published", null, outputs.AsReadOnly());
        }
        catch (Exception e)
        {
            bool clean = stage?.Cleanup() ?? true;
            return Failure(e, committed, clean, outputs);
        }
    }

    private sealed record SingleFilePaths(string Destination, string? ReportPath, PathGuard Guard, PathGuard? ReportGuard);

    private SingleFilePaths PrepareSinglePaths(string destination, IReadOnlyList<string> inputPaths,
        string? reportPath, bool force)
    {
        destination = PublicationPaths.Normalize(destination);
        reportPath = reportPath is null ? null : PublicationPaths.Normalize(reportPath);
        var normalizedInputs = PathGuard.NormalizeInputs(inputPaths);
        // Entire request's lexical admission precedes the first filesystem/drive observation.
        beforePathAccess?.Invoke();
        NativeFileSystem.RequirePublicationTarget(destination);
        if (reportPath is not null) NativeFileSystem.RequirePublicationTarget(reportPath);
        var guard = new PathGuard(normalizedInputs, reportPath is null ? [destination] : [destination, reportPath]);
        var reportGuard = reportPath is null ? null : new PathGuard(normalizedInputs, [reportPath]);
        bool existing = PublicationPaths.Exists(destination);
        if (existing && !force) throw new PublicationException("E_DESTINATION_EXISTS", 2);
        if (existing && NativeFileSystem.Identity(destination).Directory) throw new PublicationException("E_OUTPUT_KIND", 2);
        if (reportPath is not null && PublicationPaths.Exists(reportPath)) throw new PublicationException("E_DESTINATION_EXISTS", 2);
        if (existing && force) NativeFileSystem.RequireQualifiedReplacement(destination);
        guard.Recheck();
        return new(destination, reportPath, guard, reportGuard);
    }

    private ArtifactDescriptor WriteArtifact(OwnedStage stage, CandidateArtifact artifact)
    {
        Point(PublicationPoint.BeforeArtifactWrite);
        var path = stage.Write(artifact.Path, artifact.Bytes, StageWriteKind.Artifact);
        Point(PublicationPoint.AfterArtifactWrite); Point(PublicationPoint.BeforeVerify);
        var descriptor = OwnedStage.Inspect(path, artifact.Id, artifact.Path, artifact.Bytes.Length, artifact.Verify);
        Point(PublicationPoint.AfterVerify);
        return descriptor;
    }
    private static byte[] Snapshot(ReadOnlyMemory<byte> bytes, long cap)
    {
        if (bytes.Length > cap) throw new PublicationException("E_OUTPUT_LIMIT", 2);
        return bytes.ToArray();
    }
    private void Point(PublicationPoint point) => fault?.Invoke(point);
    private static PublicationOutcome Failure(Exception error, bool committed, bool clean, IReadOnlyList<ArtifactDescriptor> outputs)
    {
        var known = error as PublicationException;
        return new(false, committed ? 4 : known?.ExitCode ?? 4, committed ? "published" : clean ? "none" : "staged",
            committed ? "E_POSTCOMMIT_REPORTING" : known?.Code ?? "E_PUBLICATION", committed ? Array.AsReadOnly(outputs.ToArray()) : [], !clean);
    }
}

