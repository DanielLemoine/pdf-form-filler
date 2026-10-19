using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PdfForm.Publication;

internal readonly record struct FileIdentity(ulong Volume, ulong Number, uint Links, bool Directory);
internal enum NativeErrorNamespace { Windows, Linux }

/// <summary>Metadata only. Unsupported platforms/links fail closed. No ACL changes.</summary>
internal static class NativeFileSystem
{
    // WinError.h: https://learn.microsoft.com/windows/win32/debug/system-error-codes--0-499-
    private const int WindowsErrorFileExists = 80;
    private const int WindowsErrorAlreadyExists = 183;
    // Linux UAPI asm-generic/errno-base.h, used by the supported Linux development targets.
    private const int LinuxErrorExists = 17;
    internal static FileIdentity Identity(string path)
    {
        try { return ReadIdentity(path); }
        catch (PublicationException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new PublicationException("E_PATH_ACCESS", 2); }
    }

    private static FileIdentity ReadIdentity(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new PublicationException("E_PATH_LINK_UNSUPPORTED", 2);
        if (OperatingSystem.IsWindows())
        {
            using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var data))
                throw new PublicationException("E_PATH_IDENTITY", 2);
            return new(data.VolumeSerialNumber, ((ulong)data.FileIndexHigh << 32) | data.FileIndexLow,
                data.NumberOfLinks, (attributes & FileAttributes.Directory) != 0);
        }
        if (OperatingSystem.IsLinux())
        {
            if (Statx(-100, path, 0x100, 0x7ff, out var data) != 0 || (data.Mask & 0x107) != 0x107)
                throw new PublicationException("E_PATH_IDENTITY", 2);
            var kind = data.Mode & 0xf000;
            if (kind is not (0x8000 or 0x4000)) throw new PublicationException("E_PATH_KIND", 2);
            return new(((ulong)data.DeviceMajor << 32) | data.DeviceMinor, data.Inode, data.Links, kind == 0x4000);
        }
        throw new PublicationException("E_FILESYSTEM_UNSUPPORTED", 2);
    }

    internal static void CreateDirectoryExclusive(string path)
    {
        bool created = OperatingSystem.IsWindows() ? CreateDirectoryW(path, IntPtr.Zero) :
            OperatingSystem.IsLinux() ? Mkdir(path, 0x1ff) == 0 :
            throw new PublicationException("E_FILESYSTEM_UNSUPPORTED", 2);
        if (!created) throw new PublicationException("E_STAGE_CREATE");
    }

    internal static void CommitCreateNew(string source, string destination)
    {
        bool ok = OperatingSystem.IsWindows() ? MoveFileExW(source, destination, 0) :
            OperatingSystem.IsLinux() ? RenameAt2(-100, source, -100, destination, 1) == 0 :
            throw new PublicationException("E_FILESYSTEM_UNSUPPORTED", 2);
        if (!ok)
        {
            int error = Marshal.GetLastPInvokeError();
            throw CommitError(OperatingSystem.IsWindows() ? NativeErrorNamespace.Windows : NativeErrorNamespace.Linux, error);
        }
    }
    internal static PublicationException CommitError(NativeErrorNamespace errorNamespace, int error)
    {
        bool collision = errorNamespace == NativeErrorNamespace.Windows
            ? error is WindowsErrorFileExists or WindowsErrorAlreadyExists
            : errorNamespace == NativeErrorNamespace.Linux && error == LinuxErrorExists;
        return new(collision ? "E_DESTINATION_EXISTS" : "E_COMMIT", collision ? 2 : 4);
    }

    internal static void RequireInputSource(string path)
    {
        if (OperatingSystem.IsLinux()) return; // Synthetic development scope, as for destinations.
        if (!OperatingSystem.IsWindows()) throw new PublicationException("E_FILESYSTEM_UNSUPPORTED", 2);
        // Inputs must be local fixed-drive snapshots; unlike destinations, their format need not be NTFS.
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            throw new PublicationException("E_FILESYSTEM_UNSUPPORTED", 2);
    }

    internal static void RequirePublicationTarget(string path)
    {
        if (OperatingSystem.IsLinux()) return; // Development/synthetic path only; no Windows qualification claim.
        if (!OperatingSystem.IsWindows()) throw new PublicationException("E_FILESYSTEM_UNSUPPORTED", 2);
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        if (drive.DriveType != DriveType.Fixed || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new PublicationException("E_FILESYSTEM_UNSUPPORTED", 2);
    }

    internal static void RequireQualifiedReplacement(string path)
    {
        if (!OperatingSystem.IsWindows() || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new PublicationException("E_ATOMIC_REPLACE_UNSUPPORTED", 2);
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        if (drive.DriveType != DriveType.Fixed || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new PublicationException("E_ATOMIC_REPLACE_UNSUPPORTED", 2);
        // ReplaceFileW has documented partial-failure states that violate S30.
        // No replacement backend is admitted until actual NTFS preservation tests pass.
        throw new PublicationException("E_ATOMIC_REPLACE_UNQUALIFIED", 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxInformation
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string existing, string destination, uint flags);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameAt2(int oldDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        int newDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);
    [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
    private static extern int Mkdir([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out StatxInformation data);
}
