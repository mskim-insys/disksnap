using System.Text;
using DiskSnap.Core.Reporting;
using DiskSnap.Core.Snapshots;
using DiskSnap.Core.Storage;

namespace DiskSnap.Core.Tests.Reporting;

public sealed class ComparisonReportRunnerTests
{
    [Fact]
    public async Task UsesNewestTwoLoadsRulesAndWritesCompleteBomlessUtf8Report()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        await store.WriteAsync(Snapshot(1, "SERIAL", 1_000_000), 10);
        await store.WriteAsync(Snapshot(2, "SERIAL", 120_000_000), 10);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "rules.json"),
            "[{\"id\":\"test\",\"pattern\":\"growth\",\"action\":\"track\",\"measure\":false,\"title\":\"Test rule\",\"guidance\":\"Custom guidance.\"}]");
        var outputPath = Path.Combine(fixture.Path, "report.md");
        await File.WriteAllTextAsync(outputPath, "previous report");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await new ComparisonReportRunner(store).RunAsync("C:", null, null, outputPath, output, error);

        Assert.Equal(0, result);
        Assert.Equal("", error.ToString());
        Assert.Contains("Saved report to", output.ToString());
        var bytes = await File.ReadAllBytesAsync(outputPath);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        var report = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Older snapshot (UTC): 2026-11-28 00:00:01 UTC", report, StringComparison.Ordinal);
        Assert.Contains("Newer snapshot (UTC): 2026-11-28 00:00:02 UTC", report, StringComparison.Ordinal);
        Assert.Contains("Custom guidance.", report, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(fixture.Path, ".*.tmp"));
    }

    [Fact]
    public async Task ExplicitSnapshotsAreOrderedByHeaderUtcAndMustMatchRequestedDriveAndSerial()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var laterPath = Path.Combine(fixture.Path, "later.jsonl.gz");
        var earlierPath = Path.Combine(fixture.Path, "earlier.jsonl.gz");
        await SnapshotFile.WriteAsync(laterPath, Snapshot(2, "SERIAL", 120_000_000));
        await SnapshotFile.WriteAsync(earlierPath, Snapshot(1, "SERIAL", 1_000_000));
        var outputPath = Path.Combine(fixture.Path, "report.md");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await new ComparisonReportRunner(store).RunAsync("c:\\", laterPath, earlierPath, outputPath, output, error);

        Assert.Equal(0, result);
        Assert.Equal("", error.ToString());
        var report = await File.ReadAllTextAsync(outputPath);
        Assert.True(report.IndexOf("Older snapshot (UTC): 2026-11-28 00:00:01 UTC", StringComparison.Ordinal) <
                    report.IndexOf("Newer snapshot (UTC): 2026-11-28 00:00:02 UTC", StringComparison.Ordinal));

        var existing = await File.ReadAllBytesAsync(outputPath);
        Assert.Equal(1, await new ComparisonReportRunner(store).RunAsync("D:", laterPath, earlierPath, outputPath, output, error));
        Assert.Equal(existing, await File.ReadAllBytesAsync(outputPath));
        Assert.Contains("does not match requested drive", error.ToString(), StringComparison.Ordinal);

        var differentSerialPath = Path.Combine(fixture.Path, "other-serial.jsonl.gz");
        await SnapshotFile.WriteAsync(differentSerialPath, Snapshot(1, "OTHER", 1_000_000));
        Assert.Equal(1, await new ComparisonReportRunner(store).RunAsync("C:", earlierPath, differentSerialPath, outputPath, output, error));
        Assert.Equal(existing, await File.ReadAllBytesAsync(outputPath));
        Assert.Contains("same non-empty volume serial", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutomaticSelectionFailsForTooFewSnapshotsOrMismatchedNewestSerialsWithoutFallback()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var runner = new ComparisonReportRunner(store);
        var outputPath = Path.Combine(fixture.Path, "report.md");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(1, await runner.RunAsync("C:", null, null, outputPath, output, error));
        Assert.Contains("At least two valid snapshots", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));

        await store.WriteAsync(Snapshot(1, "SERIAL", 1_000_000), 10);
        await store.WriteAsync(Snapshot(2, "SERIAL", 2_000_000), 10);
        await store.WriteAsync(Snapshot(3, "OTHER", 3_000_000), 10);
        Assert.Equal(1, await runner.RunAsync("C:", null, null, outputPath, output, error));
        Assert.Contains("newest two snapshots have different volume serials", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task MissingOutputOrUnpairedExplicitPathFailsWithoutChangingExistingReport()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var firstPath = Path.Combine(fixture.Path, "first.jsonl.gz");
        var secondPath = Path.Combine(fixture.Path, "second.jsonl.gz");
        await SnapshotFile.WriteAsync(firstPath, Snapshot(1, "SERIAL", 1_000_000));
        await SnapshotFile.WriteAsync(secondPath, Snapshot(2, "SERIAL", 120_000_000));
        var existingPath = Path.Combine(fixture.Path, "report.md");
        var original = Encoding.UTF8.GetBytes("existing report");
        await File.WriteAllBytesAsync(existingPath, original);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new ComparisonReportRunner(store);

        Assert.Equal(1, await runner.RunAsync("C:", firstPath, null, existingPath, output, error));
        Assert.Contains("--a and --b must be supplied together", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllBytesAsync(existingPath));
        Assert.Equal(1, await runner.RunAsync("C:", firstPath, secondPath, null, output, error));
        Assert.Contains("outputPath", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await File.ReadAllBytesAsync(existingPath));
    }

    [Fact]
    public async Task FailedOutputReplacementPreservesDestinationAndRemovesTemporaryFile()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        await store.WriteAsync(Snapshot(1, "SERIAL", 1_000_000), 10);
        await store.WriteAsync(Snapshot(2, "SERIAL", 120_000_000), 10);
        var destination = Directory.CreateDirectory(Path.Combine(fixture.Path, "report.md")).FullName;
        var marker = Path.Combine(destination, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await new ComparisonReportRunner(store).RunAsync("C:", null, null, destination, output, error);

        Assert.Equal(1, result);
        Assert.Contains("Compare failed:", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("keep", await File.ReadAllTextAsync(marker));
        Assert.Empty(Directory.GetFiles(fixture.Path, ".*.tmp"));
        Assert.Equal("", output.ToString());
    }

    private static Snapshot Snapshot(int second, string serial, long size) => new()
    {
        Header = new SnapshotHeader
        {
            Version = 1,
            Tool = "DiskSnap test",
            Drive = "C:",
            VolumeSerial = serial,
            FileSystem = "NTFS",
            Utc = $"2026-11-28T00:00:{second:D2}Z",
            Host = "test",
            Elevated = false,
            VolumeTotal = 1_000_000_000,
            VolumeFree = 500_000_000,
            Metric = "allocated",
            DirectoryThreshold = 0,
            FileThreshold = 0,
            ScanSeconds = 0,
            Skipped = new SkippedSummary { Count = 0, Sample = [] }
        },
        Nodes =
        [
            new SnapshotNode { Path = "", Kind = "d", Size = size, FileCount = 1 },
            new SnapshotNode { Path = "growth", Kind = "c", Size = size, FileCount = 1, RuleId = "test" }
        ]
    };

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiskSnapCompareTests-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
