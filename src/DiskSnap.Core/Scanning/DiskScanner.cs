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
    private readonly Func<string, CancellationToken, IReadOnlyList<WindowsDirectoryEnumerator.Entry>> _enumerateDirectory;
    private ScannerOptions _options = null!;
    private IReadOnlyList<ScanRules.CompiledRule> _compiledRules = Array.Empty<ScanRules.CompiledRule>();
    private uint _volumeSerial;
    private string _volumeRoot = string.Empty;
    private string _fileSystem = string.Empty;
    private int _skippedCount;

    public DiskScanner() : this(WindowsDirectoryEnumerator.Enumerate)
    {
    }

    internal DiskScanner(Func<string, CancellationToken, IReadOnlyList<WindowsDirectoryEnumerator.Entry>> enumerateDirectory)
    {
        _enumerateDirectory = enumerateDirectory;
    }

    public Task<Snapshot> ScanAsync(
        string rootPath,
        ScannerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ScanCoreAsync(rootPath, options, cancellationToken, allowSubdirectory: false);

    internal Task<Snapshot> ScanAsyncForTests(
        string rootPath,
        ScannerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ScanCoreAsync(rootPath, options, cancellationToken, allowSubdirectory: true);

    internal static string NormalizeRootPath(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (rootPath.Length == 2 && char.IsAsciiLetter(rootPath[0]) && rootPath[1] == ':')
        {
            rootPath += Path.DirectorySeparatorChar;
        }

        return Path.GetFullPath(rootPath);
    }

    private async Task<Snapshot> ScanCoreAsync(
        string rootPath,
        ScannerOptions? options,
        CancellationToken cancellationToken,
        bool allowSubdirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DiskSnap scanning requires Windows.");
        }

        var scanOptions = options ?? new ScannerOptions();
        scanOptions.Validate();
        var fullRootPath = NormalizeRootPath(rootPath);
        var volumeRoot = Path.GetPathRoot(fullRootPath);
        if (!allowSubdirectory && !string.Equals(fullRootPath, volumeRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Scan root must be a volume root (for example 'C:\\'); scan rules are relative to the drive root.",
                nameof(rootPath));
        }

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
        _compiledRules = ScanRules.Compile(options.Rules);
        _countedHardLinks.Clear();
        _skippedSample.Clear();
        _skippedCount = 0;
        cancellationToken.ThrowIfCancellationRequested();

        var volumeRoot = Path.GetPathRoot(rootPath)
            ?? throw new ArgumentException("The scan root is not on a local volume.", nameof(rootPath));
        var volume = GetVolumeInformation(volumeRoot);
        _volumeSerial = volume.SerialNumber;
        _volumeRoot = volumeRoot.TrimEnd(Path.DirectorySeparatorChar);
        _fileSystem = volume.FileSystem;
        var stopwatch = Stopwatch.StartNew();
        var root = ScanDirectory(rootPath, string.Empty, applyRules: true, isScanRoot: true, cancellationToken);
        var nodes = new List<SnapshotNode>();
        EmitDirectory(root, nodes, isRoot: true);
        stopwatch.Stop();
        return new Snapshot
        {
            Header = new SnapshotHeader
            {
                Version = 1,
                Tool = "DiskSnap 1.0.0",
                Drive = _volumeRoot,
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
        bool isScanRoot,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryRecord(relativePath);
        IReadOnlyList<WindowsDirectoryEnumerator.Entry> entries;
        try
        {
            entries = _enumerateDirectory(fullPath, cancellationToken);
        }
        catch (Win32Exception exception) when (isScanRoot && IsUnsupportedEnumerationError(exception))
        {
            throw new NotSupportedException(
                $"Directory enumeration is not supported on drive '{_volumeRoot}' using filesystem '{_fileSystem}'.",
                exception);
        }
        catch (Exception exception) when (IsSkippable(exception, isScanRoot))
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
            var rule = applyRules ? ScanRules.FindMatch(_compiledRules, childRelativePath) : null;
            var isDirectory = (entry.Attributes & FileAttributeDirectory) != 0;
            var isReparsePoint = (entry.Attributes & FileAttributeReparsePoint) != 0;
            var isLink = isReparsePoint && CloudPlaceholderClassifier.IsLinkTag(entry.ReparseTag);

            if (isDirectory && CloudPlaceholderClassifier.IsCloudPlaceholderDirectory(entry.Attributes))
            {
                directory.Children.Add(new DirectoryRecord(childRelativePath)
                {
                    Kind = "c",
                    RuleId = rule?.Id,
                    Flags = ["cloud"]
                });
                continue;
            }

            if (rule is not null && string.Equals(rule.Action, "exclude", StringComparison.OrdinalIgnoreCase))
            {
                var excluded = MeasureExcluded(
                    entry,
                    childFullPath,
                    childRelativePath,
                    isDirectory,
                    isLink,
                    rule,
                    cancellationToken);
                if (!isDirectory && _options.FileThreshold > 0)
                {
                    directory.AddSmallFile(
                        new ScannedFile(excluded.Size, 0, excluded.RuleId, excluded.Kind, excluded.Flags),
                        childRelativePath,
                        excluded.FileCount ?? 0);
                }
                else
                {
                    directory.Children.Add(excluded);
                }

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
                    var child = ScanDirectory(childFullPath, childRelativePath, applyRules, isScanRoot: false, cancellationToken);
                    child.RuleId = rule?.Id;
                    directory.Children.Add(child);
                }

                continue;
            }

            var file = ScanFile(entry, childFullPath, rule);
            if (file.Kind == "l" || file.ThresholdSize >= _options.FileThreshold)
            {
                directory.Children.Add(DirectoryRecord.File(childRelativePath, file));
            }
            else
            {
                directory.AddSmallFile(file, childRelativePath);
            }
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
                excluded.Flags = ["cloud"];
            }

            return excluded;
        }

        if (!isDirectory)
        {
            var classification = CloudPlaceholderClassifier.Classify(entry.Attributes, entry.AllocationSize);
            var size = isLink ? 0 : classification.CountedSize;
            var excluded = DirectoryRecord.Excluded(relativePath, rule.Id, isDirectory: false, size, fileCount: 1);
            excluded.Flags = classification.Flags.ToArray();
            return excluded;
        }

        if (isLink)
        {
            return DirectoryRecord.Excluded(relativePath, rule.Id, isDirectory: true, size: 0, fileCount: 1);
        }

        var measured = ScanDirectory(fullPath, relativePath, applyRules: false, isScanRoot: false, cancellationToken);
        return DirectoryRecord.Excluded(relativePath, rule.Id, isDirectory: true, measured.Size, measured.FileCount);
    }

    private ScannedFile ScanFile(
        WindowsDirectoryEnumerator.Entry entry,
        string fullPath,
        ScanRule? rule)
    {
        var isLink = (entry.Attributes & FileAttributeReparsePoint) != 0 &&
            entry.ReparseTag == 0xA000000C;
        var classification = CloudPlaceholderClassifier.Classify(entry.Attributes, entry.AllocationSize);
        var countedSize = isLink ? 0 : classification.CountedSize;
        var thresholdSize = isLink ? 0 : classification.CountedSize;
        var flags = classification.Flags.ToArray();

        if (!isLink && !CloudPlaceholderClassifier.IsCloudPlaceholder(entry.Attributes) &&
            entry.AllocationSize >= _options.HardLinkDeduplicationThreshold &&
            WindowsDirectoryEnumerator.TryGetHardLinkInfo(fullPath, out var linkCount) &&
            linkCount > 1)
        {
            flags = [.. flags, "hardlink"];
            if (!_countedHardLinks.Add(new FileIdentity(_volumeSerial, entry.FileId)))
            {
                countedSize = 0;
            }
        }

        return new ScannedFile(countedSize, thresholdSize, rule?.Id, isLink ? "l" : "f", flags);
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
                Flags = directory.Flags.Length == 0 ? null : directory.Flags
            });
            return;
        }

        if (!isRoot && (directory.Kind == "c" || directory.Size < _options.DirectoryThreshold))
        {
            nodes.Add(new SnapshotNode
            {
                Path = directory.Path,
                Kind = "c",
                Size = directory.Size,
                FileCount = directory.FileCount,
                RuleId = directory.RuleId,
                Flags = directory.Flags.Length == 0 ? null : directory.Flags
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
            else
            {
                nodes.Add(new SnapshotNode
                {
                    Path = child.Path,
                    Kind = child.Kind,
                    Size = child.Size,
                    RuleId = child.RuleId,
                    Flags = child.Flags.Length == 0 ? null : child.Flags
                });
            }
        }

        if (!directory.HasSmallFileAggregate)
        {
            return;
        }

        nodes.Add(new SnapshotNode
        {
            Path = string.IsNullOrEmpty(directory.Path) ? "*" : directory.Path + "\\*",
            Kind = "g",
            Size = directory.SmallFileSize,
            FileCount = directory.SmallFileCount,
            RuleId = directory.GetSmallFileRuleId(),
            Flags = directory.GetSmallFileFlags()
        });
    }

    internal static bool IsSkippable(Exception exception, bool isScanRoot) => !isScanRoot && exception switch
    {
        UnauthorizedAccessException => true,
        IOException => true,
        Win32Exception nativeException when nativeException.NativeErrorCode == ErrorOperationAborted => false,
        Win32Exception => true,
        _ => false
    };

    private static bool IsUnsupportedEnumerationError(Win32Exception exception) =>
        exception.NativeErrorCode is ErrorInvalidFunction or ErrorInvalidParameter or ErrorNotSupported;

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
    private readonly record struct ScannedFile(long Size, long ThresholdSize, string? RuleId, string Kind, string[] Flags);
    private readonly record struct OrderedValue(string Path, string Value, int Index);
    private sealed record VolumeMetadata(uint SerialNumber, string FileSystem, long TotalBytes, long FreeBytes);

    private sealed class DirectoryRecord(string path)
    {
        private Dictionary<string, OrderedValue>? _smallFileRuleIds;
        private Dictionary<string, OrderedValue>? _smallFileFlags;

        public string Path { get; } = path;
        public bool IsDirectory { get; init; } = true;
        public bool IsExcluded { get; init; }
        public long Size { get; set; }
        public long? FileCount { get; set; } = 0;
        public long SmallFileSize { get; private set; }
        public long SmallFileCount { get; private set; }
        public bool HasSmallFileAggregate { get; private set; }
        public string? RuleId { get; set; }
        public string Kind { get; init; } = "d";
        public string[] Flags { get; set; } = Array.Empty<string>();
        public List<DirectoryRecord> Children { get; } = [];

        public void AddSmallFile(ScannedFile file, string path, long fileCount = 1)
        {
            SmallFileSize += file.Size;
            SmallFileCount += fileCount;
            HasSmallFileAggregate = true;
            if (file.RuleId is not null)
            {
                AddOrderedValue(_smallFileRuleIds ??= new Dictionary<string, OrderedValue>(StringComparer.OrdinalIgnoreCase),
                    path, file.RuleId, 0);
            }

            for (var index = 0; index < file.Flags.Length; index++)
            {
                AddOrderedValue(_smallFileFlags ??= new Dictionary<string, OrderedValue>(StringComparer.Ordinal),
                    path, file.Flags[index], index);
            }
        }

        public void RecalculateTotals()
        {
            var size = SmallFileSize;
            var fileCount = SmallFileCount;
            foreach (var child in Children)
            {
                size += child.Size;
                fileCount += child.FileCount ?? 0;
            }

            Size = size;
            FileCount = fileCount;
        }

        public string? GetSmallFileRuleId() => _smallFileRuleIds?.Count == 1
            ? _smallFileRuleIds.Values.Single().Value
            : null;

        public string[]? GetSmallFileFlags() => _smallFileFlags is null
            ? null
            : _smallFileFlags.Values
                .OrderBy(value => value.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Path, StringComparer.Ordinal)
                .ThenBy(value => value.Index)
                .Select(value => value.Value)
                .ToArray();

        public static DirectoryRecord File(string path, ScannedFile file) => new(path)
        {
            IsDirectory = false,
            Size = file.Size,
            FileCount = 1,
            RuleId = file.RuleId,
            Kind = file.Kind,
            Flags = file.Flags
        };

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

        private void AddOrderedValue(Dictionary<string, OrderedValue> values, string path, string value, int index)
        {
            if (!values.TryGetValue(value, out var existing) || ComparePaths(path, existing.Path) < 0)
            {
                values[value] = new OrderedValue(path, value, index);
            }
        }

        private static int ComparePaths(string left, string right)
        {
            var comparison = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left, right);
        }
    }
}
