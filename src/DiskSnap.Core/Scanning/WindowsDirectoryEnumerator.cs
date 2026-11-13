using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskSnap.Core.Scanning;

internal static class WindowsDirectoryEnumerator
{
    private const uint FileListDirectory = 0x0001;
    private const uint FileShareAll = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileIdExtdDirectoryInfo = 19;
    private const int FileIdExtdDirectoryRestartInfo = 20;
    private const int ErrorNoMoreFiles = 18;
    private const int BufferSize = 64 * 1024;

    internal sealed record Entry(string Name, uint Attributes, uint ReparseTag, long AllocationSize, Guid FileId);

    internal static IReadOnlyList<Entry> Enumerate(string directoryPath, CancellationToken cancellationToken)
    {
        using var handle = CreateFileW(
            ToExtendedPath(directoryPath),
            FileListDirectory,
            FileShareAll,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var entries = new List<Entry>();
        var buffer = Marshal.AllocHGlobal(BufferSize);
        try
        {
            var restart = true;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!GetFileInformationByHandleEx(
                    handle,
                    restart ? FileIdExtdDirectoryRestartInfo : FileIdExtdDirectoryInfo,
                    buffer,
                    BufferSize))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreFiles)
                    {
                        break;
                    }

                    throw new Win32Exception(error);
                }

                restart = false;
                var offset = 0;
                while (true)
                {
                    var nextOffset = Marshal.ReadInt32(buffer, offset);
                    var attributes = unchecked((uint)Marshal.ReadInt32(buffer, offset + 56));
                    var nameLength = Marshal.ReadInt32(buffer, offset + 60);
                    var reparseTag = unchecked((uint)Marshal.ReadInt32(buffer, offset + 68));
                    var name = Marshal.PtrToStringUni(buffer + offset + 88, nameLength / sizeof(char))
                        ?? throw new InvalidDataException("A directory entry did not contain a file name.");
                    var fileIdBytes = new byte[16];
                    Marshal.Copy(buffer + offset + 72, fileIdBytes, 0, fileIdBytes.Length);
                    entries.Add(new Entry(
                        name,
                        attributes,
                        reparseTag,
                        Marshal.ReadInt64(buffer, offset + 48),
                        new Guid(fileIdBytes)));

                    if (nextOffset == 0)
                    {
                        break;
                    }

                    offset += nextOffset;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
    }

    internal static bool TryGetHardLinkInfo(string filePath, out uint linkCount)
    {
        linkCount = 0;
        using var handle = CreateFileW(
            ToExtendedPath(filePath),
            0x00000080,
            FileShareAll,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var information))
        {
            return false;
        }

        linkCount = information.NumberOfLinks;
        return true;
    }

    internal static string ToExtendedPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path[2..];
        }

        return @"\\?\" + path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        IntPtr information,
        int bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
