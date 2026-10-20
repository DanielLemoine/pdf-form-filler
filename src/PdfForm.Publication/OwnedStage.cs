using System.Security.Cryptography;

namespace PdfForm.Publication;

/// <summary>Only this attempt's exact recorded identities are eligible for cleanup. Never recursive delete.</summary>
internal sealed class OwnedStage
{
    private sealed class CreatedEntry(string path)
    {
        internal string Path { get; } = path;
        internal FileIdentity? Identity { get; set; }
        internal bool Transferred { get; set; }
    }
    private readonly List<CreatedEntry> files = [];
    private readonly List<CreatedEntry> directories = [];
    private readonly PublicationLimits limits;
    private readonly Action<StageCreationPoint>? afterCreate;
    private long written;
    internal string Root { get; }
    internal bool Committed { get; set; }

    // Allocation has no filesystem side effects, so the caller retains this journal even when Create fails.
    internal OwnedStage(string destination, PublicationLimits limits, Action<StageCreationPoint>? afterCreate = null)
    {
        this.limits = limits;
        this.afterCreate = afterCreate;
        Root = Path.Combine(Path.GetDirectoryName(destination)!, ".pdfform-stage-" + Guid.NewGuid().ToString("N"));
    }
    internal void Create(long reserveBytes)
    {
        if (reserveBytes > limits.MaxTotalBytes) throw new PublicationException("E_OUTPUT_LIMIT", 2);
        // Space is a preflight bound, not a reservation or a durability guarantee.
        if (new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace < checked(reserveBytes + 8L * 1024 * 1024))
            throw new PublicationException("E_OUTPUT_SPACE");
        NativeFileSystem.CreateDirectoryExclusive(Root);
        RecordCreation(directories, Root, StageCreationPoint.RootDirectory);
    }
    private void RecordCreation(List<CreatedEntry> journal, string path, StageCreationPoint point)
    {
        var entry = new CreatedEntry(path);
        journal.Add(entry);
        afterCreate?.Invoke(point);
        entry.Identity = NativeFileSystem.Identity(path);
    }
    internal string Write(string relativePath, ReadOnlyMemory<byte> bytes, StageWriteKind kind)
    {
        if (!PublicationPaths.IsSafeRelativeOutput(relativePath)) throw new PublicationException("E_OUTPUT_PATH", 2);
        long cap = kind == StageWriteKind.Artifact ? limits.MaxArtifactBytes : limits.MaxManifestBytes;
        if (bytes.Length > cap || written + bytes.Length > limits.MaxTotalBytes)
            throw new PublicationException("E_OUTPUT_LIMIT", 2);
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(path)!;
        var current = Root;
        foreach (var segment in Path.GetRelativePath(Root, parent).Split(Path.DirectorySeparatorChar))
        {
            if (segment == ".") continue;
            current = Path.Combine(current, segment);
            if (!directories.Any(x => x.Path == current))
            {
                NativeFileSystem.CreateDirectoryExclusive(current);
                RecordCreation(directories, current, StageCreationPoint.ChildDirectory);
            }
        }
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            RecordCreation(files, path, StageCreationPoint.File);
            stream.Write(bytes.Span);
            stream.Flush(flushToDisk: true);
        }
        written += bytes.Length;
        return path;
    }
    internal void AssertOwned()
    {
        foreach (var item in directories.Concat(files))
            if (item.Identity is null || !PathGuard.SameIdentity(NativeFileSystem.Identity(item.Path), item.Identity.Value))
                throw new PublicationException("E_STAGE_CHANGED");
        foreach (var item in files)
            if (NativeFileSystem.Identity(item.Path).Links != 1) throw new PublicationException("E_STAGE_CHANGED");
        var expected = files.Select(x => x.Path).Concat(directories.Skip(1).Select(x => x.Path)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in directories)
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
                if (!expected.Contains(entry) || !seen.Add(entry)) throw new PublicationException("E_STAGE_CONTENTS");
        if (seen.Count != expected.Count) throw new PublicationException("E_STAGE_CONTENTS");
    }
    internal bool Cleanup()
    {
        if (Committed) return true;
        // Creation is evidence of possible residue, not authority to delete an unverified pathname.
        // Even if its original name is absent, it may have been moved elsewhere.
        if (directories.Concat(files).Any(item => item.Identity is null)) return false;
        bool clean = true;
        // If any ancestor was replaced, even an owned child's textual pathname is no longer safe.
        try
        {
            foreach (var item in directories)
                if (!PathGuard.SameIdentity(NativeFileSystem.Identity(item.Path), item.Identity!.Value)) return false;
        }
        catch { return false; }
        foreach (var item in files.AsEnumerable().Reverse())
        {
            try
            {
                if (item.Transferred) continue;
                if (!PublicationPaths.Exists(item.Path)) { clean = false; continue; }
                if (!PathGuard.SameIdentity(NativeFileSystem.Identity(item.Path), item.Identity!.Value)) { clean = false; continue; }
                File.Delete(item.Path);
            }
            catch { clean = false; }
        }
        foreach (var item in directories.AsEnumerable().Reverse())
        {
            try { Directory.Delete(item.Path, recursive: false); }
            catch { clean = false; }
        }
        return clean;
    }
    internal void MarkTransferred(string path) => files.Single(item => item.Path == path).Transferred = true;
    internal static ArtifactDescriptor Inspect(string path, string id, string relativePath, long expectedBytes, Func<Stream, bool> verify)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != expectedBytes || !verify(stream)) throw new PublicationException("E_CANDIDATE_VERIFY");
        stream.Position = 0;
        return new(id, relativePath, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
    }
    internal static void CheckHash(string path, ArtifactDescriptor descriptor)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != descriptor.Bytes || Convert.ToHexStringLower(SHA256.HashData(stream)) != descriptor.Sha256)
            throw new PublicationException("E_ARTIFACT_INTEGRITY");
    }
}
