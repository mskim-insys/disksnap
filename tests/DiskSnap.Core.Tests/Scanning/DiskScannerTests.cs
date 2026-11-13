using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;
using Xunit.Sdk;

namespace DiskSnap.Core.Tests.Scanning;

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

        var snapshot = await ScanAsync(fixture.Path, directoryThreshold: 0, fileThreshold: 0);

        var root = Assert.Single(snapshot.Nodes, node => node.Path == "");
        var targetNode = Assert.Single(snapshot.Nodes, node => node.Path == "Target");
        var link = Assert.Single(snapshot.Nodes, node => node.Path == "TargetAlias");
        Assert.Equal("d", targetNode.Kind);
        Assert.Equal("l", link.Kind);
        Assert.Equal(0, link.Size);
        Assert.Equal(targetNode.Size, root.Size);
    }

    [Fact]
    public async Task Scan_DirectorySymlinkIsRecordedAsLinkWithoutDescending()
    {
        EnsureWindows();
        using var fixture = new TempDirectory();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Path, "Target")).FullName;
        File.WriteAllBytes(Path.Combine(target, "payload.bin"), new byte[16 * 1024]);
        var linkPath = Path.Combine(fixture.Path, "SymbolicAlias");
        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            throw SkipException.ForSkip("Directory symlink privilege is unavailable; enable Developer Mode or run elevated.");
        }

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
            Rule("measure", "Measured", "exclude", measure: true),
            Rule("manual", "TRAC*", "track", measure: false)
        };

        var snapshot = await new DiskScanner().ScanAsync(fixture.Path, new ScannerOptions
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

        var snapshot = await new DiskScanner().ScanAsync(fixture.Path, new ScannerOptions
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
        await new DiskScanner().ScanAsync(path, new ScannerOptions
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
            CreateNoWindow = true
        };
        command.ArgumentList.Add("/d");
        command.ArgumentList.Add("/c");
        command.ArgumentList.Add($"mklink /J \"{junctionPath}\" \"{targetPath}\"");
        using var process = Process.Start(command)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create the temp junction.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw SkipException.ForSkip("Could not create a junction in the temp fixture: " + output);
        }
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
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
