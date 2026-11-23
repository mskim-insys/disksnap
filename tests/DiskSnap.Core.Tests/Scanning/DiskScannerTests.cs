using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;
using Xunit.Sdk;

namespace DiskSnap.Core.Tests.Scanning;

public sealed class DirectorySymlinkFactAttribute : FactAttribute
{
    public DirectorySymlinkFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Scanner integration fixtures require Windows.";
            return;
        }

        var fixture = Path.Combine(Path.GetTempPath(), "DiskSnap.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var link = Path.Combine(fixture, "link");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, fixture);
            }
            catch (Exception exception) when (
                (exception is IOException or UnauthorizedAccessException) && (exception.HResult & 0xFFFF) == 1314)
            {
                Skip = "Directory symlink privilege is unavailable; enable Developer Mode or run elevated.";
                return;
            }

            Directory.Delete(link);
        }
        finally
        {
            Directory.Delete(fixture);
        }
    }
}

public sealed class DiskScannerTests
{
    [Fact]
    public async Task Scan_JunctionIsRecordedAsLinkWithoutCountingItsTargetTwice()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Path, "Target")).FullName;
        File.WriteAllBytes(Path.Combine(target, "payload.bin"), new byte[16 * 1024]);
        var junction = Path.Combine(fixture.Path, "TargetAlias");
        CreateJunction(junction, target);

        try
        {
            var snapshot = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);

            var root = Assert.Single(snapshot.Nodes, node => node.Path == "");
            var targetNode = Assert.Single(snapshot.Nodes, node => node.Path == "Target");
            var link = Assert.Single(snapshot.Nodes, node => node.Path == "TargetAlias");
            Assert.Equal("d", targetNode.Kind);
            Assert.Equal("l", link.Kind);
            Assert.Equal(0, link.Size);
            Assert.Equal(targetNode.Size, root.Size);
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [DirectorySymlinkFact]
    public async Task Scan_DirectorySymlinkIsRecordedAsLinkWithoutDescending()
    {
        using var fixture = new TempDirectory();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Path, "Target")).FullName;
        File.WriteAllBytes(Path.Combine(target, "payload.bin"), new byte[16 * 1024]);
        var linkPath = Path.Combine(fixture.Path, "SymbolicAlias");
        Directory.CreateSymbolicLink(linkPath, target);

        var snapshot = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);

        var root = Assert.Single(snapshot.Nodes, node => node.Path == "");
        var targetNode = Assert.Single(snapshot.Nodes, node => node.Path == "Target");
        var link = Assert.Single(snapshot.Nodes, node => node.Path == "SymbolicAlias");
        Assert.Equal("l", link.Kind);
        Assert.Equal(0, link.Size);
        Assert.Equal(targetNode.Size, root.Size);
    }

    [Fact]
    public async Task Scan_AccessDeniedDirectoryIsSkippedAndSampled()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var locked = Directory.CreateDirectory(Path.Combine(fixture.Path, "Locked")).FullName;
        File.WriteAllBytes(Path.Combine(locked, "payload.bin"), new byte[4096]);
        var denyResult = RunIcacls(locked, "/deny", "*S-1-1-0:(RD)");
        if (denyResult.ExitCode != 0)
        {
            RunIcacls(locked, "/remove:d", "*S-1-1-0");
            throw SkipException.ForSkip("Could not apply a deny-list ACE to the temp fixture: " + denyResult.Output);
        }

        try
        {
            var snapshot = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);

            Assert.Equal(1, snapshot.Header.Skipped.Count);
            Assert.Contains(locked, snapshot.Header.Skipped.Sample, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(snapshot.Nodes, node => node.Path.StartsWith("Locked\\", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            var removeResult = RunIcacls(locked, "/remove:d", "*S-1-1-0");
            if (removeResult.ExitCode != 0)
            {
                throw new InvalidOperationException("Could not remove the test deny ACE: " + removeResult.Output);
            }
        }
    }

    [Fact]
    public async Task Scan_ThresholdsExpandAndCollapseDirectoriesAndAggregateSmallFiles()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var expanded = Directory.CreateDirectory(Path.Combine(fixture.Path, "Expanded")).FullName;
        var collapsed = Directory.CreateDirectory(Path.Combine(fixture.Path, "Collapsed")).FullName;
        File.WriteAllBytes(Path.Combine(expanded, "large.bin"), new byte[1024 * 1024]);
        File.WriteAllBytes(Path.Combine(expanded, "small-a.bin"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(expanded, "small-b.bin"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(collapsed, "tiny.bin"), new byte[4096]);

        var baseline = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);
        var largeSize = Assert.Single(baseline.Nodes, node => node.Path == "Expanded\\large.bin").Size;
        var smallASize = Assert.Single(baseline.Nodes, node => node.Path == "Expanded\\small-a.bin").Size;
        var smallBSize = Assert.Single(baseline.Nodes, node => node.Path == "Expanded\\small-b.bin").Size;
        var collapsedSize = Assert.Single(baseline.Nodes, node => node.Path == "Collapsed").Size;
        var expandedSize = Assert.Single(baseline.Nodes, node => node.Path == "Expanded").Size;
        var fileThreshold = Math.Max(smallASize, smallBSize) + 1;
        var directoryThreshold = collapsedSize + 1;
        Assert.True(largeSize >= fileThreshold);
        Assert.True(expandedSize >= directoryThreshold);

        var snapshot = await ScanAsync(fixture.Path, directoryThreshold, fileThreshold);

        var root = Assert.Single(snapshot.Nodes, node => node.Path == "");
        var expandedNode = Assert.Single(snapshot.Nodes, node => node.Path == "Expanded");
        var collapsedNode = Assert.Single(snapshot.Nodes, node => node.Path == "Collapsed");
        var largeFile = Assert.Single(snapshot.Nodes, node => node.Path == "Expanded\\large.bin");
        var aggregate = Assert.Single(snapshot.Nodes, node => node.Path == "Expanded\\*");
        Assert.Equal("d", root.Kind);
        Assert.Equal("d", expandedNode.Kind);
        Assert.Equal("c", collapsedNode.Kind);
        Assert.Equal(1, collapsedNode.FileCount);
        Assert.Equal("f", largeFile.Kind);
        Assert.Equal("g", aggregate.Kind);
        Assert.Equal(2, aggregate.FileCount);
        Assert.Equal(smallASize + smallBSize, aggregate.Size);
        Assert.DoesNotContain(snapshot.Nodes, node => node.Path is "Expanded\\small-a.bin" or "Expanded\\small-b.bin");
    }

    [Fact]
    public async Task Scan_RulesExcludeMeasureAndAttachTrackRuleId()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var unmeasured = Directory.CreateDirectory(Path.Combine(fixture.Path, "Unmeasured")).FullName;
        var measured = Directory.CreateDirectory(Path.Combine(fixture.Path, "Measured")).FullName;
        var tracked = Directory.CreateDirectory(Path.Combine(fixture.Path, "Tracked")).FullName;
        File.WriteAllBytes(Path.Combine(unmeasured, "hidden.bin"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(measured, "measured.bin"), new byte[8192]);
        File.WriteAllBytes(Path.Combine(tracked, "tracked.bin"), new byte[4096]);
        var baseline = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);
        var expectedMeasuredSize = Assert.Single(baseline.Nodes, node => node.Path == "Measured\\measured.bin").Size;
        var rules = new[]
        {
            Rule("skip", "unmeasured", "exclude", measure: false),
            Rule("later", "UNMEASURED", "track", measure: false),
            Rule("measure", "Measured", "exclude", measure: true),
            Rule("manual", "TRAC*", "track", measure: false)
        };

        var snapshot = await new DiskScanner().ScanAsyncForTests(fixture.Path, new ScannerOptions
        {
            DirectoryThreshold = 0,
            FileThreshold = 0,
            HardLinkDeduplicationThreshold = long.MaxValue,
            Rules = rules
        });

        var unmeasuredNode = Assert.Single(snapshot.Nodes, node => node.Path == "Unmeasured");
        var measuredNode = Assert.Single(snapshot.Nodes, node => node.Path == "Measured");
        var trackedNode = Assert.Single(snapshot.Nodes, node => node.Path == "Tracked");
        Assert.Equal("x", unmeasuredNode.Kind);
        Assert.Equal(0, unmeasuredNode.Size);
        Assert.Null(unmeasuredNode.FileCount);
        Assert.Equal("skip", unmeasuredNode.RuleId);
        Assert.DoesNotContain(snapshot.Nodes, node => node.Path.StartsWith("Unmeasured\\", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("x", measuredNode.Kind);
        Assert.Equal("measure", measuredNode.RuleId);
        Assert.Equal(expectedMeasuredSize, measuredNode.Size);
        Assert.Equal(1, measuredNode.FileCount);
        Assert.DoesNotContain(snapshot.Nodes, node => node.Path.StartsWith("Measured\\", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("d", trackedNode.Kind);
        Assert.Equal("manual", trackedNode.RuleId);
        Assert.Contains(snapshot.Nodes, node => node.Path == "Tracked\\tracked.bin");
    }

    [Fact]
    public async Task Scan_EmbeddedDefaultRulesCanBeCopiedAndEditedInATempLocation()
    {
        using var fixture = new TempDirectory();
        var rulesPath = Path.Combine(fixture.Path, "settings", "rules.json");

        await ScanRules.CreateDefaultFileAsync(rulesPath);
        var loaded = await ScanRules.LoadAsync(rulesPath);

        Assert.Equal(ScanRules.Defaults.Select(rule => (rule.Id, rule.Pattern, rule.Action, rule.Title, rule.Guidance)),
            loaded.Select(rule => (rule.Id, rule.Pattern, rule.Action, rule.Title, rule.Guidance)));
        Assert.Contains(loaded, rule => rule.Id == "winsxs" && rule.Action == "exclude" && !rule.Measure);
        Assert.Contains(loaded, rule => rule.Id == "vhdx" && rule.Action == "track");
    }

    [Fact]
    public async Task Scan_TwoHardLinksToLargeFileCountOneAllocation()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var originalPath = Path.Combine(fixture.Path, "original.bin");
        var hardLinkPath = Path.Combine(fixture.Path, "hardlink.bin");
        File.WriteAllBytes(originalPath, new byte[2 * 1024 * 1024]);
        if (!CreateHardLinkW(hardLinkPath, originalPath, IntPtr.Zero))
        {
            throw SkipException.ForSkip("The temp volume does not support creating hard links.");
        }

        var snapshot = await new DiskScanner().ScanAsyncForTests(fixture.Path, new ScannerOptions
        {
            DirectoryThreshold = 0,
            FileThreshold = 0,
            HardLinkDeduplicationThreshold = 1024 * 1024,
            Rules = Array.Empty<ScanRule>()
        });

        var files = snapshot.Nodes.Where(node => node.Kind == "f").ToArray();
        Assert.Equal(2, files.Length);
        Assert.All(files, file => Assert.Contains("hardlink", file.Flags ?? Array.Empty<string>()));
        Assert.Single(files, file => file.Size > 0);
        Assert.Equal(files.Sum(file => file.Size), Assert.Single(snapshot.Nodes, node => node.Path == "").Size);

        var aggregateSnapshot = await new DiskScanner().ScanAsyncForTests(fixture.Path, new ScannerOptions
        {
            DirectoryThreshold = 0,
            FileThreshold = 4 * 1024 * 1024,
            HardLinkDeduplicationThreshold = 1024 * 1024,
            Rules = Array.Empty<ScanRule>()
        });
        var aggregate = Assert.Single(aggregateSnapshot.Nodes, node => node.Path == "*");
        Assert.Equal(files.Sum(file => file.Size), aggregate.Size);
        Assert.Equal(files.Length, aggregate.FileCount);
        Assert.Equal(new[] { "hardlink" }, aggregate.Flags);
    }

    [Fact]
    public async Task Scan_RequiresAVolumeRootAndNormalizesDriveDesignators()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();

        Assert.Equal(@"C:\", DiskScanner.NormalizeRootPath("C:"));
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => new DiskScanner().ScanAsync(fixture.Path));
        Assert.Contains("volume root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drive root", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(87)]
    public async Task Scan_UnsupportedEnumerationErrorInInnerDirectoryIsSkipped(int errorCode)
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var inner = Directory.CreateDirectory(Path.Combine(fixture.Path, "Inner")).FullName;
        var scanner = new DiskScanner((path, cancellationToken) =>
        {
            if (string.Equals(path, inner, StringComparison.OrdinalIgnoreCase))
            {
                throw new Win32Exception(errorCode);
            }

            return WindowsDirectoryEnumerator.Enumerate(path, cancellationToken);
        });

        var snapshot = await scanner.ScanAsyncForTests(fixture.Path, new ScannerOptions
        {
            DirectoryThreshold = 0,
            FileThreshold = 0,
            Rules = Array.Empty<ScanRule>()
        });

        Assert.Equal(1, snapshot.Header.Skipped.Count);
        Assert.Contains(inner, snapshot.Header.Skipped.Sample, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(snapshot.Nodes, node => node.Path.StartsWith("Inner\\", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_ErrorClassificationKeepsRootUnsupportedAndOperationAbortedFatal()
    {
        foreach (var errorCode in new[] { 1, 50, 87 })
        {
            Assert.False(DiskScanner.IsSkippable(new Win32Exception(errorCode), isScanRoot: true));
            Assert.True(DiskScanner.IsSkippable(new Win32Exception(errorCode), isScanRoot: false));
        }

        Assert.False(DiskScanner.IsSkippable(new Win32Exception(995), isScanRoot: false));
    }

    [Fact]
    public async Task Scan_UnsupportedRootEnumerationErrorNamesDriveAndFileSystem()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var scanner = new DiskScanner((_, _) => throw new Win32Exception(50));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => scanner.ScanAsyncForTests(fixture.Path));

        Assert.Contains("drive", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filesystem", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_SmallExcludedFileKeepsItsPreviousAggregateOutput()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var entry = new WindowsDirectoryEnumerator.Entry("cloud-excluded.bin", 0x00400000, 0, 8192, Guid.Empty);
        var scanner = new DiskScanner((_, _) => new[] { entry });
        var options = new ScannerOptions
        {
            DirectoryThreshold = 0,
            FileThreshold = 1024 * 1024,
            HardLinkDeduplicationThreshold = long.MaxValue,
            Rules = new[] { Rule("excluded", "cloud-excluded.bin", "exclude", measure: false) }
        };

        var snapshot = await scanner.ScanAsyncForTests(fixture.Path, options);

        var aggregate = Assert.Single(snapshot.Nodes, node => node.Path == "*");
        Assert.Equal("g", aggregate.Kind);
        Assert.Equal(0, aggregate.Size);
        Assert.Equal(0, aggregate.FileCount);
        Assert.Equal("excluded", aggregate.RuleId);
        Assert.Equal(new[] { "cloud" }, aggregate.Flags);
    }

    [Fact]
    public async Task Scan_SmallFileAggregatePreservesRuleAndCloudMetadata()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var entries = new[]
        {
            new WindowsDirectoryEnumerator.Entry("cloud.bin", 0x00400000, 0, 8192, Guid.Empty),
            new WindowsDirectoryEnumerator.Entry("normal.bin", 0, 0, 8192, Guid.NewGuid())
        };
        var scanner = new DiskScanner((_, _) => entries);
        var rules = new[]
        {
            Rule("cloud", "cloud.bin", "track", measure: false),
            Rule("normal", "normal.bin", "track", measure: false)
        };
        var individual = await scanner.ScanAsyncForTests(fixture.Path, new ScannerOptions
        {
            DirectoryThreshold = 0,
            FileThreshold = 0,
            HardLinkDeduplicationThreshold = long.MaxValue,
            Rules = rules
        });
        var aggregated = await scanner.ScanAsyncForTests(fixture.Path, new ScannerOptions
        {
            DirectoryThreshold = 0,
            FileThreshold = 1024 * 1024,
            HardLinkDeduplicationThreshold = long.MaxValue,
            Rules = rules
        });

        var oldFileNodes = individual.Nodes.Where(node => node.Kind == "f").ToArray();
        var aggregate = Assert.Single(aggregated.Nodes, node => node.Path == "*");
        Assert.Equal(oldFileNodes.Sum(node => node.Size), aggregate.Size);
        Assert.Equal(oldFileNodes.Length, aggregate.FileCount);
        Assert.Null(aggregate.RuleId);
        Assert.Equal(oldFileNodes.SelectMany(node => node.Flags ?? Array.Empty<string>()).Distinct(), aggregate.Flags);
    }

    [Fact]
    public async Task Scan_SparseFileRecordsAllocatedSizeBelowLogicalSize()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var filePath = Path.Combine(fixture.Path, "sparse.bin");
        const long logicalSize = 64L * 1024 * 1024;
        using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(logicalSize);
        }

        var sparseFlag = RunExecutable("fsutil.exe", "sparse", "setflag", filePath);
        if (sparseFlag.ExitCode != 0)
        {
            throw SkipException.ForSkip("The temp volume does not support sparse files: " + sparseFlag.Output);
        }

        var zeroRange = RunExecutable("fsutil.exe", "sparse", "setrange", filePath, "0", logicalSize.ToString(CultureInfo.InvariantCulture));
        if (zeroRange.ExitCode != 0)
        {
            throw SkipException.ForSkip("Could not mark zero data in a temp sparse file: " + zeroRange.Output);
        }

        var snapshot = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);
        var sparseFile = Assert.Single(snapshot.Nodes, node => node.Path == "sparse.bin");
        Assert.True(sparseFile.Size < new FileInfo(filePath).Length);
    }

    [Fact]
    public async Task Scan_CompressedFileRecordsAllocatedSizeBelowLogicalSize()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var filePath = Path.Combine(fixture.Path, "compressed.bin");
        File.WriteAllBytes(filePath, new byte[16 * 1024 * 1024]);
        var compression = RunExecutable("compact.exe", "/c", "/q", filePath);
        if (compression.ExitCode != 0)
        {
            throw SkipException.ForSkip("The temp volume does not support NTFS compression: " + compression.Output);
        }

        var snapshot = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);
        var compressedFile = Assert.Single(snapshot.Nodes, node => node.Path == "compressed.bin");
        Assert.True(compressedFile.Size < new FileInfo(filePath).Length);
    }

    [Fact]
    public async Task Scan_SucceedsForPathsLongerThan260Characters()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var nestedPath = fixture.Path;
        while (nestedPath.Length < 300)
        {
            nestedPath = Path.Combine(nestedPath, new string('d', 40));
            Directory.CreateDirectory(WindowsDirectoryEnumerator.ToExtendedPath(nestedPath));
        }

        var longFilePath = Path.Combine(nestedPath, "payload.bin");
        File.WriteAllBytes(WindowsDirectoryEnumerator.ToExtendedPath(longFilePath), new byte[4096]);
        var snapshot = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);
        var file = Assert.Single(snapshot.Nodes, node => node.Path.EndsWith("payload.bin", StringComparison.Ordinal));
        Assert.True(Path.Combine(fixture.Path, file.Path.Replace('\\', Path.DirectorySeparatorChar)).Length > 260);
    }

    [Fact]
    public async Task Scan_CancellationStopsPromptlyWithoutProducingSnapshot()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        for (var directoryIndex = 0; directoryIndex < 20; directoryIndex++)
        {
            var directory = Directory.CreateDirectory(Path.Combine(fixture.Path, $"nested-{directoryIndex:D2}"));
            for (var fileIndex = 0; fileIndex < 500; fileIndex++)
            {
                File.WriteAllBytes(Path.Combine(directory.FullName, $"file-{fileIndex:D3}.bin"), [1]);
            }
        }

        var enteredChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new DiskScanner((path, cancellationToken) =>
        {
            var entries = WindowsDirectoryEnumerator.Enumerate(path, cancellationToken);
            if (!string.Equals(path, fixture.Path, StringComparison.OrdinalIgnoreCase))
            {
                enteredChild.TrySetResult();
            }

            return entries;
        });
        using var cancellation = new CancellationTokenSource();
        var scan = scanner.ScanAsyncForTests(fixture.Path, new ScannerOptions
        {
            Rules = Array.Empty<ScanRule>()
        }, cancellation.Token);
        await enteredChild.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelledAt = Stopwatch.StartNew();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.True(cancelledAt.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Empty(Directory.GetFiles(fixture.Path, "*.jsonl.gz", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(0x00001000u)]
    [InlineData(0x00400000u)]
    public void CloudPlaceholderClassifier_ReturnsZeroSizeAndCloudFlag(uint attributes)
    {
        var classification = CloudPlaceholderClassifier.Classify(attributes, 123456);

        Assert.Equal(0, classification.CountedSize);
        Assert.Equal(new[] { "cloud" }, classification.Flags);
    }

    [Fact]
    public void CloudPlaceholderClassifier_PreservesAllocationWithoutPlaceholderAttributes()
    {
        var classification = CloudPlaceholderClassifier.Classify(0, 123456);

        Assert.Equal(123456, classification.CountedSize);
        Assert.Empty(classification.Flags);
    }

    private static async Task<Snapshot> ScanAsync(string path, long directoryThreshold, long fileThreshold) =>
        await new DiskScanner().ScanAsyncForTests(path, new ScannerOptions
        {
            DirectoryThreshold = directoryThreshold,
            FileThreshold = fileThreshold,
            HardLinkDeduplicationThreshold = long.MaxValue,
            Rules = Array.Empty<ScanRule>()
        });

    private static ScanRule Rule(string id, string pattern, string action, bool measure) => new()
    {
        Id = id,
        Pattern = pattern,
        Action = action,
        Measure = measure,
        Title = id,
        Guidance = "fixture guidance"
    };

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("Scanner integration fixtures require Windows.");
        }
    }

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        var command = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Arguments = $"/d /s /c \"mklink /J \"{junctionPath}\" \"{targetPath}\"\""
        };
        using var process = Process.Start(command)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create the temp junction.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not create a junction in the temp fixture: " + output);
        }
    }

    private static (int ExitCode, string Output) RunExecutable(string fileName, params string[] arguments)
    {
        var command = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            command.ArgumentList.Add(argument);
        }

        using var process = Process.Start(command)
            ?? throw new InvalidOperationException($"Could not start {fileName} for the temp fixture.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static (int ExitCode, string Output) RunIcacls(string path, params string[] arguments)
    {
        var command = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        command.ArgumentList.Add(path);
        foreach (var argument in arguments)
        {
            command.ArgumentList.Add(argument);
        }

        using var process = Process.Start(command)
            ?? throw new InvalidOperationException("Could not start icacls.exe for the temp fixture.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiskSnap.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                var deletePath = OperatingSystem.IsWindows()
                    ? WindowsDirectoryEnumerator.ToExtendedPath(Path)
                    : Path;
                Directory.Delete(deletePath, recursive: true);
            }
        }
    }
}
