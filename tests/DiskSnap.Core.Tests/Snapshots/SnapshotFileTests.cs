using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DiskSnap.Core.Snapshots;

namespace DiskSnap.Core.Tests.Snapshots;

public sealed class SnapshotFileTests
{
    [Fact]
    public async Task WriteAndRead_RoundTripsEveryHeaderAndNodeField()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl.gz");
        var snapshot = new Snapshot
        {
            Header = new SnapshotHeader
            {
                Version = 1,
                Tool = "DiskSnap 1.0.0",
                Drive = "C:",
                VolumeSerial = "1234ABCD",
                FileSystem = "NTFS",
                Utc = "2026-11-12T09:00:00Z",
                Host = "test-host",
                Elevated = true,
                VolumeTotal = 1_000_000,
                VolumeFree = 400_000,
                Metric = "allocated",
                DirectoryThreshold = 256 * 1024 * 1024,
                FileThreshold = 64 * 1024 * 1024,
                ScanSeconds = 12.5,
                Skipped = new SkippedSummary
                {
                    Count = 2,
                    Sample = ["C:\\locked", "C:\\vanished"]
                }
            },
            Nodes =
            [
                new SnapshotNode
                {
                    Path = "",
                    Kind = "d",
                    Size = 600_000,
                    FileCount = 42
                },
                new SnapshotNode
                {
                    Path = "Users\\me\\AppData",
                    Kind = "x",
                    Size = 0,
                    FileCount = 3,
                    RuleId = "temp",
                    Flags = ["sys", "cloud"]
                },
                new SnapshotNode
                {
                    Path = "Users\\me\\large.bin",
                    Kind = "f",
                    Size = 128,
                    Flags = ["hardlink"]
                }
            ]
        };

        try
        {
            await SnapshotFile.WriteAsync(path, snapshot);

            var loaded = await SnapshotFile.ReadAsync(path);

            Assert.Equal(snapshot.Header.Type, loaded.Header.Type);
            Assert.Equal(snapshot.Header.Version, loaded.Header.Version);
            Assert.Equal(snapshot.Header.Tool, loaded.Header.Tool);
            Assert.Equal(snapshot.Header.Drive, loaded.Header.Drive);
            Assert.Equal(snapshot.Header.VolumeSerial, loaded.Header.VolumeSerial);
            Assert.Equal(snapshot.Header.FileSystem, loaded.Header.FileSystem);
            Assert.Equal(snapshot.Header.Utc, loaded.Header.Utc);
            Assert.Equal(snapshot.Header.Host, loaded.Header.Host);
            Assert.Equal(snapshot.Header.Elevated, loaded.Header.Elevated);
            Assert.Equal(snapshot.Header.VolumeTotal, loaded.Header.VolumeTotal);
            Assert.Equal(snapshot.Header.VolumeFree, loaded.Header.VolumeFree);
            Assert.Equal(snapshot.Header.Metric, loaded.Header.Metric);
            Assert.Equal(snapshot.Header.DirectoryThreshold, loaded.Header.DirectoryThreshold);
            Assert.Equal(snapshot.Header.FileThreshold, loaded.Header.FileThreshold);
            Assert.Equal(snapshot.Header.ScanSeconds, loaded.Header.ScanSeconds);
            Assert.Equal(snapshot.Header.Skipped.Count, loaded.Header.Skipped.Count);
            Assert.Equal(snapshot.Header.Skipped.Sample, loaded.Header.Skipped.Sample);
            Assert.Equal(snapshot.Nodes.Count, loaded.Nodes.Count);
            for (var index = 0; index < snapshot.Nodes.Count; index++)
            {
                Assert.Equal(snapshot.Nodes[index].Path, loaded.Nodes[index].Path);
                Assert.Equal(snapshot.Nodes[index].Kind, loaded.Nodes[index].Kind);
                Assert.Equal(snapshot.Nodes[index].Size, loaded.Nodes[index].Size);
                Assert.Equal(snapshot.Nodes[index].FileCount, loaded.Nodes[index].FileCount);
                Assert.Equal(snapshot.Nodes[index].RuleId, loaded.Nodes[index].RuleId);
                Assert.Equal(snapshot.Nodes[index].Flags, loaded.Nodes[index].Flags);
            }

            await using var file = File.OpenRead(path);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var headerLine = await reader.ReadLineAsync()
                ?? throw new InvalidDataException("The snapshot header is missing.");
            using var headerJson = JsonDocument.Parse(headerLine);
            Assert.Equal(
                new[]
                {
                    "t", "v", "tool", "drive", "volSerial", "fs", "utc", "host", "elevated", "volTotal",
                    "volFree", "metric", "dirThreshold", "fileThreshold", "scanSeconds", "skipped"
                },
                headerJson.RootElement.EnumerateObject().Select(property => property.Name));

            var firstNodeLine = await reader.ReadLineAsync()
                ?? throw new InvalidDataException("The first snapshot node is missing.");
            using var firstNodeJson = JsonDocument.Parse(firstNodeLine);
            Assert.Equal(
                new[] { "p", "k", "s", "n" },
                firstNodeJson.RootElement.EnumerateObject().Select(property => property.Name));

            var secondNodeLine = await reader.ReadLineAsync()
                ?? throw new InvalidDataException("The second snapshot node is missing.");
            using var secondNodeJson = JsonDocument.Parse(secondNodeLine);
            Assert.Equal(
                new[] { "p", "k", "s", "n", "r", "fl" },
                secondNodeJson.RootElement.EnumerateObject().Select(property => property.Name));

            var thirdNodeLine = await reader.ReadLineAsync()
                ?? throw new InvalidDataException("The third snapshot node is missing.");
            using var thirdNodeJson = JsonDocument.Parse(thirdNodeLine);
            Assert.Equal(
                new[] { "p", "k", "s", "fl" },
                thirdNodeJson.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Null(await reader.ReadLineAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
