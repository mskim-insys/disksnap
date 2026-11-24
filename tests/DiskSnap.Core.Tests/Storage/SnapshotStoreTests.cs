using System.Collections;
using System.IO.Compression;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;
using DiskSnap.Core.Storage;
using DiskSnap.Core.Tests.Scanning;

namespace DiskSnap.Core.Tests.Storage;

public sealed class SnapshotStoreTests
{
    [Fact]
    public async Task WritesSnapshotsAndRetainsOnlyNewestForEachDrive()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var first = await store.WriteAsync(Snapshot("C:", 1), 2);
        var second = await store.WriteAsync(Snapshot("C:", 2), 2);
        var otherDrive = await store.WriteAsync(Snapshot("D:", 1), 2);
        var unrelated = Path.Combine(Path.GetDirectoryName(first)!, "notes.jsonl.gz");
        await File.WriteAllTextAsync(unrelated, "not a snapshot");
        var third = await store.WriteAsync(Snapshot("C:", 3), 2);

        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.True(File.Exists(third));
        Assert.True(File.Exists(otherDrive));
        Assert.True(File.Exists(unrelated));
        Assert.Equal(3, Directory.GetFiles(Path.GetDirectoryName(third)!).Length);
        Assert.Equal("C:", (await SnapshotFile.ReadAsync(third)).Header.Drive);
    }

    [Fact]
    public async Task RetentionUsesHeaderTimeRatherThanLocalFilenameOrder()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var older = await store.WriteAsync(Snapshot("C:", 1), 10);
        var newer = await store.WriteAsync(Snapshot("C:", 2), 10);
        var renamedOlder = Path.Combine(Path.GetDirectoryName(older)!, "20990101-000000.jsonl.gz");
        File.Move(older, renamedOlder);

        var latest = await store.WriteAsync(Snapshot("C:", 3), 2);

        Assert.False(File.Exists(renamedOlder));
        Assert.True(File.Exists(newer));
        Assert.True(File.Exists(latest));
    }

    [Fact]
    public async Task RetentionSkipsUnreadableForeignAndInvalidCandidatesAndStillSucceeds()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var old = await store.WriteAsync(Snapshot("C:", 1), 10);
        var directory = Path.GetDirectoryName(old)!;
        var foreign = Path.Combine(directory, "20200101-000001.jsonl.gz");
        var invalid = Path.Combine(directory, "20200101-000002.jsonl.gz");
        var missingType = Path.Combine(directory, "20200101-000003.jsonl.gz");
        var unreadable = Path.Combine(directory, "20200101-000004.jsonl.gz");
        await SnapshotFile.WriteAsync(foreign, Snapshot("D:", 1));
        await File.WriteAllTextAsync(invalid, "not gzip");
        await using (var file = File.Create(missingType))
        await using (var gzip = new GZipStream(file, CompressionMode.Compress))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteLineAsync("{\"v\":1}");
        }
        await SnapshotFile.WriteAsync(unreadable, Snapshot("C:", 2));
        using var locked = new FileStream(unreadable, FileMode.Open, FileAccess.Read, FileShare.None);
        using var warnings = new StringWriter();

        var saved = await store.WriteAsync(Snapshot("C:", 3), 1, warnings: warnings);

        Assert.True(File.Exists(saved));
        Assert.False(File.Exists(old));
        foreach (var path in new[] { foreign, invalid, missingType, unreadable })
        {
            Assert.True(File.Exists(path));
            Assert.Contains(path, warnings.ToString());
        }
        Assert.Contains("Warning", warnings.ToString());
    }

    [Fact]
    public async Task WriteDeletesOnlyStaleMatchingTempsInItsDriveFolder()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var directory = Directory.CreateDirectory(Path.Combine(fixture.Path, "snapshots", "C")).FullName;
        var otherDirectory = Directory.CreateDirectory(Path.Combine(fixture.Path, "snapshots", "D")).FullName;
        var nestedDirectory = Directory.CreateDirectory(Path.Combine(directory, "nested")).FullName;
        var stale = Path.Combine(directory, ".ABCDEF0123456789abcdef0123456789.tmp");
        var recent = Path.Combine(directory, ".11111111111111111111111111111111.tmp");
        var wrongHex = Path.Combine(directory, ".g1111111111111111111111111111111.tmp");
        var wrongLength = Path.Combine(directory, ".1111111111111111111111111111111.tmp");
        var wrongExtension = Path.Combine(directory, ".22222222222222222222222222222222.TMP");
        var other = Path.Combine(otherDirectory, ".33333333333333333333333333333333.tmp");
        var nested = Path.Combine(nestedDirectory, ".44444444444444444444444444444444.tmp");
        foreach (var path in new[] { stale, recent, wrongHex, wrongLength, wrongExtension, other, nested })
        {
            await File.WriteAllTextAsync(path, "keep");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(path == recent ? -23 : -25));
        }

        var saved = await store.WriteAsync(Snapshot("C:", 1), 1);

        Assert.True(File.Exists(saved));
        Assert.False(File.Exists(stale));
        foreach (var path in new[] { recent, wrongHex, wrongLength, wrongExtension, other, nested })
        {
            Assert.True(File.Exists(path));
        }
    }

    [Theory]
    [InlineData("C:", "C:")]
    [InlineData("c:\\", "C:")]
    public void NormalizesDriveAndRoot(string input, string expected) =>
        Assert.Equal(expected, SnapshotStore.NormalizeDrive(input));

    [Theory]
    [InlineData("C:/")]
    [InlineData("C:\\folder")]
    [InlineData("1:")]
    [InlineData("C:\\\\")]
    public void RejectsNonDrivePaths(string input) =>
        Assert.Throws<ArgumentException>(() => SnapshotStore.NormalizeDrive(input));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledWriteRemovesTempAndDoesNotRunRetention(bool cancel)
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var first = await store.WriteAsync(Snapshot("C:", 1), 10);
        var second = await store.WriteAsync(Snapshot("C:", 2), 10);
        using var cancellation = new CancellationTokenSource();
        var broken = Snapshot("C:", 3, new ThrowingNodes(cancel ? cancellation : null));

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync(broken, 1, cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(broken, 1));
        }

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(first)!).Length);
    }

    [Fact]
    public async Task FilenameCollisionDoesNotOverwriteOrRunRetention()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var first = await store.WriteAsync(Snapshot("C:", 1), 10);
        var second = await store.WriteAsync(Snapshot("C:", 2), 10);
        var before = await File.ReadAllBytesAsync(second);

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(Snapshot("C:", 2), 1));

        Assert.True(File.Exists(first));
        Assert.Equal(before, await File.ReadAllBytesAsync(second));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(first)!).Length);
    }

    [DirectorySymlinkFact]
    public async Task StoreRejectsLinkedDriveFolderWithoutTouchingTarget()
    {
        using var fixture = new TempDirectory();
        using var outside = new TempDirectory();
        var sentinel = Path.Combine(outside.Path, "20260101-000000.jsonl.gz");
        await File.WriteAllTextAsync(sentinel, "keep");
        var snapshots = Directory.CreateDirectory(Path.Combine(fixture.Path, "snapshots"));
        var link = Path.Combine(snapshots.FullName, "C");
        Directory.CreateSymbolicLink(link, outside.Path);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => new SnapshotStore(fixture.Path).WriteAsync(Snapshot("C:", 1), 1));
            Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));
            Assert.Single(Directory.GetFiles(outside.Path));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task SettingsUseDefaultsAndReadConfiguredValues()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var defaults = await store.LoadSettingsAsync();
        Assert.Equal(30, defaults.MaxSnapshots);
        Assert.Equal(256L * 1024 * 1024, defaults.DirectoryThreshold);
        Assert.Equal(64L * 1024 * 1024, defaults.FileThreshold);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "settings.json"), "{\"maxSnapshots\":2,\"dirThreshold\":10,\"fileThreshold\":20}");
        var settings = await store.LoadSettingsAsync();
        Assert.Equal(2, settings.MaxSnapshots);
        Assert.Equal(10, settings.DirectoryThreshold);
        Assert.Equal(20, settings.FileThreshold);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "settings.json"), "{\"maxSnapshots\":0}");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadSettingsAsync());
    }

    [Fact]
    public async Task RunnerAppliesConfiguredRetentionAfterEachSuccessfulScan()
    {
        using var fixture = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "settings.json"), "{\"maxSnapshots\":2}");
        var sequence = 0;
        var runner = new ScanRunner(new SnapshotStore(fixture.Path), (drive, _, _) =>
            Task.FromResult(Snapshot(drive, ++sequence)));
        var directory = Path.Combine(fixture.Path, "snapshots", "C");

        Assert.Equal(0, await runner.RunAsync(["C:"], true, TextWriter.Null, TextWriter.Null));
        var oldest = Assert.Single(Directory.GetFiles(directory));
        Assert.Equal(0, await runner.RunAsync(["C:"], true, TextWriter.Null, TextWriter.Null));
        Assert.Equal(0, await runner.RunAsync(["C:"], true, TextWriter.Null, TextWriter.Null));

        Assert.False(File.Exists(oldest));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public async Task RunnerContinuesAfterFailureAndPassesConfiguredThresholds()
    {
        using var fixture = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "settings.json"), "{\"maxSnapshots\":1,\"dirThreshold\":10,\"fileThreshold\":20}");
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "rules.json"), "[{\"id\":\"test\",\"pattern\":\"test\",\"action\":\"track\",\"measure\":false,\"title\":\"test\",\"guidance\":\"test\"}]");
        var store = new SnapshotStore(fixture.Path);
        var existing = await store.WriteAsync(Snapshot("C:", 1), 10);
        var seen = new List<string>();
        var runner = new ScanRunner(store, (drive, options, _) =>
        {
            seen.Add(drive);
            Assert.Equal(10, options.DirectoryThreshold);
            Assert.Equal(20, options.FileThreshold);
            Assert.Equal("test", Assert.Single(options.Rules).Id);
            if (drive == "C:") throw new IOException("drive unavailable");
            return Task.FromResult(Snapshot(drive, 1));
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var result = await runner.RunAsync(["C:", "D:"], true, output, error);

        Assert.Equal(1, result);
        Assert.Equal(new[] { "C:", "D:" }, seen);
        Assert.Equal("", output.ToString());
        Assert.Contains("drive unavailable", error.ToString());
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Path, "snapshots", "D")));
        Assert.True(File.Exists(existing));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Path, "snapshots", "C")));
    }

    [Fact]
    public async Task RunnerReportsFailureSummaryAndRetentionWarningsEvenWhenQuiet()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var first = await store.WriteAsync(Snapshot("C:", 1), 10);
        var invalid = Path.Combine(Path.GetDirectoryName(first)!, "20200101-000001.jsonl.gz");
        await File.WriteAllTextAsync(invalid, "invalid");
        var runner = new ScanRunner(store, (drive, _, _) => drive == "D:"
            ? throw new IOException("drive unavailable")
            : Task.FromResult(Snapshot(drive, 2)));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(1, await runner.RunAsync(["C:", "D:"], false, output, error));
        Assert.EndsWith($"Done: 1 saved, 1 failed{Environment.NewLine}", output.ToString());
        Assert.Contains(invalid, error.ToString());
        Assert.True(File.Exists(invalid));

        using var quietOutput = new StringWriter();
        using var quietError = new StringWriter();
        var quietRunner = new ScanRunner(store, (drive, _, _) => Task.FromResult(Snapshot(drive, 3)));
        Assert.Equal(0, await quietRunner.RunAsync(["C:"], true, quietOutput, quietError));
        Assert.Equal("", quietOutput.ToString());
        Assert.Contains(invalid, quietError.ToString());
        Assert.True(File.Exists(invalid));
    }

    [Fact]
    public async Task CancelledScanDoesNotWriteOrRetain()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var first = await store.WriteAsync(Snapshot("C:", 1), 10);
        var second = await store.WriteAsync(Snapshot("C:", 2), 10);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "settings.json"), "{\"maxSnapshots\":1}");
        using var cancellation = new CancellationTokenSource();
        var runner = new ScanRunner(store, (_, _, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot("C:", 3));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(["C:"], true, TextWriter.Null, TextWriter.Null, cancellation.Token));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(first)!).Length);
    }

    [Fact]
    public async Task RunnerReturnsSuccessOnlyForSuccessfulDrivesAndRejectsMismatchedSnapshot()
    {
        using var fixture = new TempDirectory();
        var store = new SnapshotStore(fixture.Path);
        var runner = new ScanRunner(store, (drive, _, _) => Task.FromResult(Snapshot(drive, 1)));
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await runner.RunAsync(["c:\\", "d:"], false, output, error));
        Assert.Contains("Saved", output.ToString());
        Assert.EndsWith($"Done: 2 saved, 0 failed{Environment.NewLine}", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.True(File.Exists(Path.Combine(fixture.Path, "rules.json")));
        Assert.NotEmpty(await store.LoadRulesAsync());

        var mismatch = new ScanRunner(store, (_, _, _) => Task.FromResult(Snapshot("D:", 2)));
        Assert.Equal(1, await mismatch.RunAsync(["C:"], true, output, error));
        Assert.Contains("different drive", error.ToString());
        Assert.Equal(1, await runner.RunAsync([], true, output, error));
        Assert.Equal(1, await runner.RunAsync(["..\\outside"], true, output, error));
    }

    private static Snapshot Snapshot(string drive, int second, IReadOnlyList<SnapshotNode>? nodes = null) => new()
    {
        Header = new SnapshotHeader
        {
            Version = 1, Tool = "DiskSnap test", Drive = drive, VolumeSerial = "1", FileSystem = "NTFS",
            Utc = $"2026-11-24T00:00:{second:D2}Z", Host = "test", Elevated = false, VolumeTotal = 1,
            VolumeFree = 0, Metric = "allocated", DirectoryThreshold = 0, FileThreshold = 0,
            ScanSeconds = 0, Skipped = new SkippedSummary { Count = 0, Sample = [] }
        },
        Nodes = nodes ?? [new SnapshotNode { Path = "", Kind = "d", Size = 0, FileCount = 0 }]
    };

    private sealed class ThrowingNodes(CancellationTokenSource? cancellation) : IReadOnlyList<SnapshotNode>
    {
        public int Count => 1;
        public SnapshotNode this[int index] => throw new NotSupportedException();
        public IEnumerator<SnapshotNode> GetEnumerator()
        {
            if (cancellation is not null)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            throw new IOException("Injected write failure");
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiskSnapTests-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
