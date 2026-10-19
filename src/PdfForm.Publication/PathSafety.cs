namespace PdfForm.Publication;

/// <summary>Conservative Windows-compatible output paths, also used by Linux synthetic tests.</summary>
public static class PublicationPaths
{
    public static bool IsSafeRelativeOutput(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || Path.IsPathRooted(path)) return false;
        var parts = path.Split('/');
        return parts.Length <= 8 && parts.All(IsSafeSegment);
    }
    internal static bool IsStagingPath(string path) => path.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith(".pdfform-stage-", StringComparison.OrdinalIgnoreCase));
    internal static bool IsOpaqueId(string value) => value.Length is > 0 and <= 64 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    // Component admission bound; this is not a new maximum in the wider result/completion schemas.
    internal static bool IsBuildId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
    internal static bool IsSafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ')) return false;
        if (value.Any(c => c < 32 || ":*?\"<>|/\\".Contains(c))) return false;
        var stem = value.Split('.')[0].TrimEnd(' ');
        return !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³" }.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
    internal static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PublicationException("E_PATH_UNSUPPORTED", 2);
        string full;
        try
        {
            // Reject drive-relative and root-without-drive paths before environment-dependent resolution.
            // This also refuses raw UNC/device spellings, including alternate or mixed separators.
            if (OperatingSystem.IsWindows() && Path.IsPathRooted(path) && !IsLocalWindowsAbsolute(path))
                throw new PublicationException("E_PATH_UNSUPPORTED", 2);
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            // GetFullPath is lexical. Check the canonical root before any drive, metadata or native access.
            if (OperatingSystem.IsWindows() && !IsLocalWindowsAbsolute(full))
                throw new PublicationException("E_PATH_UNSUPPORTED", 2);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new PublicationException("E_PATH_UNSUPPORTED", 2); }
        var relative = full[Path.GetPathRoot(full)!.Length..];
        if (relative.Split(Path.DirectorySeparatorChar).Any(p => !IsSafeSegment(p)))
            throw new PublicationException("E_PATH_UNSUPPORTED", 2);
        return full;
    }
    private static bool IsLocalWindowsAbsolute(string path) => path.Length >= 3 &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
    internal static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new PublicationException("E_PATH_ACCESS", 2); }
    }
}

internal sealed class PathGuard
{
    private readonly List<(string Path, FileIdentity Identity)> snapshots = [];
    private readonly List<(string Path, FileIdentity Identity)> inputs = [];
    private readonly List<(string Path, FileIdentity? Identity, FileIdentity Parent)> outputs = [];

    internal PathGuard(IReadOnlyList<string> inputPaths, IEnumerable<string> outputPaths)
    {
        var normalizedInputs = NormalizeInputs(inputPaths);
        var normalizedOutputs = outputPaths.Select(PublicationPaths.Normalize).ToArray();
        foreach (var path in normalizedInputs)
        {
            NativeFileSystem.RequireInputSource(path);
            CaptureAncestors(Path.GetDirectoryName(path)!);
            var id = NativeFileSystem.Identity(path);
            if (id.Directory) throw new PublicationException("E_INPUT_KIND", 2);
            inputs.Add((path, id)); snapshots.Add((path, id));
        }
        foreach (var path in normalizedOutputs)
        {
            var parent = Path.GetDirectoryName(path)!;
            CaptureAncestors(parent);
            var parentId = NativeFileSystem.Identity(parent);
            FileIdentity? id = PublicationPaths.Exists(path) ? NativeFileSystem.Identity(path) : null;
            foreach (var input in inputs)
                if (SamePath(path, input.Path) || id is not null && SameIdentity(id.Value, input.Identity))
                    throw new PublicationException("E_PATH_COLLISION", 2);
            foreach (var previous in outputs)
                if (SamePath(path, previous.Path) || id is not null && previous.Identity is not null && SameIdentity(id.Value, previous.Identity.Value) ||
                    SameIdentity(parentId, previous.Parent) && string.Equals(Path.GetFileName(path), Path.GetFileName(previous.Path), StringComparison.OrdinalIgnoreCase) ||
                    IsAncestor(path, previous.Path) || IsAncestor(previous.Path, path))
                    throw new PublicationException("E_PATH_COLLISION", 2);
            foreach (var input in inputs)
                if (IsAncestor(path, input.Path)) throw new PublicationException("E_PATH_COLLISION", 2);
            outputs.Add((path, id, parentId));
        }
    }
    internal static string[] NormalizeInputs(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count > 64) throw new PublicationException("E_PATH_LIMIT", 2);
        return inputPaths.Select(PublicationPaths.Normalize).ToArray();
    }
    internal void Recheck()
    {
        foreach (var item in snapshots)
            if (!SameIdentity(NativeFileSystem.Identity(item.Path), item.Identity)) throw new PublicationException("E_PATH_CHANGED", 2);
        foreach (var item in outputs)
        {
            var exists = PublicationPaths.Exists(item.Path);
            if (item.Identity is null && exists) throw new PublicationException("E_DESTINATION_EXISTS", 2);
            if (item.Identity is not null && (!exists || NativeFileSystem.Identity(item.Path) != item.Identity.Value))
                throw new PublicationException("E_PATH_CHANGED", 2);
        }
    }
    private void CaptureAncestors(string path)
    {
        string? current = path;
        while (current is not null)
        {
            var id = NativeFileSystem.Identity(current);
            if (!id.Directory) throw new PublicationException("E_PATH_PARENT", 2);
            if (!snapshots.Any(p => SamePath(p.Path, current))) snapshots.Add((current, id));
            current = Path.GetDirectoryName(current);
        }
    }
    internal static bool SameIdentity(FileIdentity a, FileIdentity b) => a.Volume == b.Volume && a.Number == b.Number;
    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool IsAncestor(string a, string b) => b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
