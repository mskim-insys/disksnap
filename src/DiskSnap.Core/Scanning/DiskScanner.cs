using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DiskSnap.Core.Snapshots;
using Microsoft.Win32.SafeHandles;

namespace DiskSnap.Core.Scanning;

public sealed class DiskScanner
{
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int ErrorInvalidFunction = 1;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNotSupported = 50;
    private const int ErrorOperationAborted = 995;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly HashSet<FileIdentity> _countedHardLinks = [];
    private readonly List<string> _skippedSample = [];
    private ScannerOptions _options = null!;
    private uint _volumeSerial;
    private int _skippedCount;

    public async Task<Snapshot> ScanAsync(
        string rootPath,
        ScannerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DiskSnap scanning requires Windows.");
        }

        var scanOptions = options ?? new ScannerOptions();
        scanOptions.Validate();
        var fullRootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRootPath))
        {
            throw new DirectoryNotFoundException($"Scan root '{fullRootPath}' does not exist.");
        }

        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => Scan(fullRootPath, scanOptions, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private Snapshot Scan(string rootPath, ScannerOptions options, CancellationToken cancellationToken)
    {
        _options = options;
        _countedHardLinks.Clear();
        _skippedSample.Clear();
        _skippedCount = 0;
        cancellationToken.ThrowIfCancellationRequested();

        var volumeRoot = Path.GetPathRoot(rootPath)
            ?? throw new ArgumentException("The scan root is not on a local volume.", nameof(rootPath));
        var volume = GetVolumeInformation(volumeRoot);
        _volumeSerial = volume.SerialNumber;
        var stopwatch = Stopwatch.StartNew();
        var root = ScanDirectory(rootPath, string.Empty, applyRules: true, cancellationToken);
        var nodes = new List<SnapshotNode>();
        EmitDirectory(root, nodes, isRoot: true);
        stopwatch.Stop();
        return new Snapshot
        {
            Header = new SnapshotHeader
            {
                Version = 1,
                Tool = "DiskSnap 1.0.0",
                Drive = volumeRoot.TrimEnd(Path.DirectorySeparatorChar),
                VolumeSerial = volume.SerialNumber.ToString("X8", CultureInfo.InvariantCulture),
                FileSystem = volume.FileSystem,
                Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Host = Environment.MachineName,
                Elevated = IsElevated(),
                VolumeTotal = volume.TotalBytes,
                VolumeFree = volume.FreeBytes,
                Metric = "allocated",
                DirectoryThreshold = options.DirectoryThreshold,
                FileThreshold = options.FileThreshold,
                ScanSeconds = stopwatch.Elapsed.TotalSeconds,
                Skipped = new SkippedSummary
                {
                    Count = _skippedCount,
                    Sample = _skippedSample.ToArray()
                }
            },
            Nodes = nodes
        };
    }

    private DirectoryRecord ScanDirectory(
        string fullPath,
        string relativePath,
        bool applyRules,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryRecord(relativePath);
        IReadOnlyList<WindowsDirectoryEnumerator.Entry> entries;
        try
        {
            entries = WindowsDirectoryEnumerator.Enumerate(fullPath, cancellationToken);
        }
        catch (Exception exception) when (IsSkippable(exception))
        {
            RecordSkipped(fullPath);
            return directory;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Name is "." or "..")
            {
                continue;
            }

            var childRelativePath = string.IsNullOrEmpty(relativePath)
                ? entry.Name
                : relativePath + "\\" + entry.Name;
            var childFullPath = Path.Combine(fullPath, entry.Name);
            var rule = applyRules ? ScanRules.FindMatch(_options.Rules, childRelativePath) : null;
            var isDirectory = (entry.Attributes & FileAttributeDirectory) != 0;
            var isReparsePoint = (entry.Attributes & FileAttributeReparsePoint) != 0;
            var isLink = isReparsePoint && CloudPlaceholderClassifier.IsLinkTag(entry.ReparseTag);

            if (rule is not null && string.Equals(rule.Action, "exclude", StringComparison.OrdinalIgnoreCase))
            {
                directory.Children.Add(MeasureExcluded(
                    entry,
                    childFullPath,
                    childRelativePath,
                    isDirectory,
                    isLink,
                    rule,
                    cancellationToken));
                continue;
            }

            if (isDirectory)
            {
                if (isLink)
                {
                    directory.Children.Add(DirectoryRecord.Link(childRelativePath, rule?.Id));
                }
                else
                {
                    var child = ScanDirectory(childFullPath, childRelativePath, applyRules, cancellationToken);
                    child.RuleId = rule?.Id;
                    directory.Children.Add(child);
                }

                continue;
            }

            directory.Children.Add(ScanFile(entry, childFullPath, childRelativePath, rule));
        }

        directory.RecalculateTotals();
        return directory;
    }

    private DirectoryRecord MeasureExcluded(
        WindowsDirectoryEnumerator.Entry entry,
        string fullPath,
        string relativePath,
        bool isDirectory,
        bool isLink,
        ScanRule rule,
        CancellationToken cancellationToken)
    {
        if (!rule.Measure)
        {
            var excluded = DirectoryRecord.Excluded(relativePath, rule.Id, isDirectory, size: 0, fileCount: null);
            if (!isDirectory && CloudPlaceholderClassifier.IsCloudPlaceholder(entry.Attributes))
            {
                excluded.Flags.Add("cloud");
            }

            return excluded;
        }

        if (!isDirectory)
        {
            var classification = CloudPlaceholderClassifier.Classify(entry.Attributes, entry.AllocationSize);
            var size = isLink ? 0 : classification.CountedSize;
            var excluded = DirectoryRecord.Excluded(relativePath, rule.Id, isDirectory: false, size, fileCount: 1);
            excluded.Flags.AddRange(classification.Flags);
            return excluded;
        }

        if (isLink)
        {
            return DirectoryRecord.Excluded(relativePath, rule.Id, isDirectory: true, size: 0, fileCount: 1);
        }

        var measured = ScanDirectory(fullPath, relativePath, applyRules: false, cancellationToken);
        return DirectoryRecord.Excluded(relativePath, rule.Id, isDirectory: true, measured.Size, measured.FileCount);
    }

    private DirectoryRecord ScanFile(
        WindowsDirectoryEnumerator.Entry entry,
        string fullPath,
        string relativePath,
        ScanRule? rule)
    {
        var isLink = (entry.Attributes & FileAttributeReparsePoint) != 0 &&
            entry.ReparseTag == 0xA000000C;
        var classification = CloudPlaceholderClassifier.Classify(entry.Attributes, entry.AllocationSize);
        var countedSize = isLink ? 0 : classification.CountedSize;
        var thresholdSize = isLink ? 0 : classification.CountedSize;
        var flags = new List<string>(classification.Flags);

        if (!isLink && !CloudPlaceholderClassifier.IsCloudPlaceholder(entry.Attributes) &&
            entry.AllocationSize >= _options.HardLinkDeduplicationThreshold &&
            WindowsDirectoryEnumerator.TryGetHardLinkInfo(fullPath, out var linkCount) &&
            linkCount > 1)
        {
            flags.Add("hardlink");
            if (!_countedHardLinks.Add(new FileIdentity(_volumeSerial, entry.FileId)))
            {
                countedSize = 0;
            }
        }

        return new DirectoryRecord(relativePath)
        {
            IsDirectory = false,
            Size = countedSize,
            FileCount = 1,
            RuleId = rule?.Id,
            Kind = isLink ? "l" : "f",
            Flags = flags,
            FileThresholdSize = thresholdSize
        };
    }

    private void EmitDirectory(DirectoryRecord directory, List<SnapshotNode> nodes, bool isRoot = false)
    {
        if (directory.IsExcluded)
        {
            nodes.Add(new SnapshotNode
            {
                Path = directory.Path,
                Kind = "x",
                Size = directory.Size,
                FileCount = directory.FileCount,
                RuleId = directory.RuleId,
                Flags = directory.Flags.Count == 0 ? null : directory.Flags.ToArray()
            });
            return;
        }

        if (!isRoot && directory.Size < _options.DirectoryThreshold)
        {
            nodes.Add(new SnapshotNode
            {
                Path = directory.Path,
                Kind = "c",
                Size = directory.Size,
                FileCount = directory.FileCount,
                RuleId = directory.RuleId
            });
            return;
        }

        nodes.Add(new SnapshotNode
        {
            Path = directory.Path,
            Kind = "d",
            Size = directory.Size,
            FileCount = directory.FileCount,
            RuleId = directory.RuleId
        });

        var smallFiles = new List<DirectoryRecord>();
        foreach (var child in directory.Children.OrderBy(child => child.Path, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(child => child.Path, StringComparer.Ordinal))
        {
            if (child.IsDirectory)
            {
                EmitDirectory(child, nodes);
            }
            else if (child.Kind == "l")
            {
                nodes.Add(new SnapshotNode
                {
                    Path = child.Path,
                    Kind = child.Kind,
                    Size = child.Size,
                    RuleId = child.RuleId
                });
            }
            else if (child.FileThresholdSize < _options.FileThreshold)
            {
                smallFiles.Add(child);
            }
            else
            {
                nodes.Add(new SnapshotNode
                {
                    Path = child.Path,
                    Kind = child.Kind,
                    Size = child.Size,
                    RuleId = child.RuleId,
                    Flags = child.Flags.Count == 0 ? null : child.Flags.ToArray()
                });
            }
        }

        if (smallFiles.Count == 0)
        {
            return;
        }

        var aggregateRules = smallFiles.Select(file => file.RuleId).Where(id => id is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var aggregateFlags = smallFiles.SelectMany(file => file.Flags).Distinct(StringComparer.Ordinal).ToArray();
        nodes.Add(new SnapshotNode
        {
            Path = string.IsNullOrEmpty(directory.Path) ? "*" : directory.Path + "\\*",
            Kind = "g",
            Size = smallFiles.Sum(file => file.Size),
            FileCount = smallFiles.Sum(file => file.FileCount),
            RuleId = aggregateRules.Length == 1 ? aggregateRules[0] : null,
            Flags = aggregateFlags.Length == 0 ? null : aggregateFlags
        });
    }

    private static bool IsSkippable(Exception exception) => exception switch
    {
        UnauthorizedAccessException => true,
        IOException => true,
        Win32Exception nativeException => nativeException.NativeErrorCode is not
            (ErrorInvalidFunction or ErrorInvalidParameter or ErrorNotSupported or ErrorOperationAborted),
        _ => false
    };

    private void RecordSkipped(string path)
    {
        _skippedCount++;
        if (_skippedSample.Count < _options.SkippedPathSampleLimit)
        {
            _skippedSample.Add(path);
        }
    }

    private static VolumeMetadata GetVolumeInformation(string volumeRoot)
    {
        var fileSystemName = new System.Text.StringBuilder(261);
        if (!GetVolumeInformationW(
            volumeRoot,
            null,
            0,
            out var serialNumber,
            out _,
            out _,
            fileSystemName,
            fileSystemName.Capacity))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!GetDiskFreeSpaceExW(volumeRoot, out _, out var totalBytes, out var freeBytes))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new VolumeMetadata(serialNumber, fileSystemName.ToString(), checked((long)totalBytes), checked((long)freeBytes));
    }

    private static bool IsElevated()
    {
        const uint TokenQuery = 0x0008;
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using (token)
        {
            if (!GetTokenInformation(token, 20, out var elevation, sizeof(int), out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return elevation != 0;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string rootPathName,
        System.Text.StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        System.Text.StringBuilder fileSystemNameBuffer,
        int fileSystemNameSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetDiskFreeSpaceExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string directoryName,
        out ulong freeBytesAvailableToCaller,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeFileHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeFileHandle tokenHandle,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    private readonly record struct FileIdentity(uint VolumeSerial, Guid FileId);
    private sealed record VolumeMetadata(uint SerialNumber, string FileSystem, long TotalBytes, long FreeBytes);

    private sealed class DirectoryRecord(string path)
    {
        public string Path { get; } = path;
        public bool IsDirectory { get; init; } = true;
        public bool IsExcluded { get; init; }
        public long Size { get; set; }
        public long? FileCount { get; set; } = 0;
        public long FileThresholdSize { get; init; }
        public string? RuleId { get; set; }
        public string Kind { get; init; } = "d";
        public List<string> Flags { get; init; } = [];
        public List<DirectoryRecord> Children { get; } = [];

        public void RecalculateTotals()
        {
            Size = Children.Sum(child => child.Size);
            FileCount = Children.Sum(child => child.FileCount ?? 0);
        }

        public static DirectoryRecord Link(string path, string? ruleId) => new(path)
        {
            IsDirectory = false,
            Size = 0,
            FileCount = 1,
            Kind = "l",
            RuleId = ruleId
        };

        public static DirectoryRecord Excluded(string path, string ruleId, bool isDirectory, long size, long? fileCount) => new(path)
        {
            IsDirectory = isDirectory,
            IsExcluded = true,
            Size = size,
            FileCount = fileCount,
            RuleId = ruleId,
            Kind = "x"
        };
    }
}
