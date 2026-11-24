using DiskSnap.Core.Comparison;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;

namespace DiskSnap.Core.Tests.Comparison;

public sealed class ComparisonPanelsTests
{
    [Fact]
    public void TopRowsReuseObservedFileTextAndRespectFilters()
    {
        var older = Snapshot(20, Node("", "d", 100), Node("p", "d", 100), Node("shrink", "c", 30));
        var newer = Snapshot(20, Node("", "d", 250), Node("p", "d", 200), Node(@"p\file", "f", 80, "hint"), Node("shrink", "c", 10));
        var rule = new ScanRule { Id = "hint", Pattern = "*", Action = "track", Title = "Hint", Guidance = "Guidance" };
        var comparison = SnapshotComparer.Compare(older, newer, [rule]);
        var row = Assert.Single(TopGrowerRow.Create(comparison, 50));
        Assert.Equal(@"p\file", row.Path);
        Assert.Null(row.Node.Delta);
        Assert.Equal("80 B", row.NewerDisplay);
        Assert.Equal("-", row.OlderDisplay);
        Assert.Contains("at least +60 B", row.DeltaDisplay);
        Assert.Contains("assuming ordinary-file folding", row.DeltaDisplay);
        Assert.Contains("exclusions or skipped", row.DeltaTooltip);
        Assert.True(row.HasGuidance);
        Assert.Contains("Guidance", row.GuidanceTooltip);
        Assert.Contains(TopGrowerRow.Create(comparison, 50, includeRoot: true), item => item.Node == comparison.Root);
        Assert.Equal("shrink", Assert.Single(TopGrowerRow.Create(comparison, 1, shrinkers: true)).Path);
        Assert.Empty(TopGrowerRow.Create(comparison, 1000));
    }

    [Fact]
    public void SpecialFoldersShowMeasuredUnknownCloudGuidanceAndUnaccounted()
    {
        var older = Snapshot(0, Node("", "d", 100), Node("measured", "x", 20, "hint", 1));
        var newer = Snapshot(0, Node("", "d", 200), Node("measured", "x", 30, "hint", 2), Node("unmeasured", "x", 0), Node("cloud", "c", 0, "hint", 0, ["cloud"]), Node("tracked", "d", 5, "hint"));
        var rule = new ScanRule { Id = "hint", Pattern = "*", Action = "track", Title = "Hint", Guidance = "Display only" };
        var model = new SpecialFoldersModel(SnapshotComparer.Compare(older, newer, [rule]));
        Assert.Equal(300, model.Unaccounted);
        Assert.Contains("300", model.UnaccountedTooltip);
        Assert.Equal(2, model.SkippedCount);
        var measured = Assert.Single(model.Rows, row => row.Path == "measured");
        Assert.Equal("30 B", measured.SizeDisplay);
        Assert.Equal("+10 B", measured.TrendDisplay);
        Assert.Equal("-", Assert.Single(model.Rows, row => row.Path == "unmeasured").SizeDisplay);
        var cloud = Assert.Single(model.Rows, row => row.Path == "cloud");
        Assert.True(cloud.IsCloud);
        Assert.Contains("not enumerated", cloud.Guidance);
        Assert.Contains("Display only", cloud.Guidance);
        Assert.Contains("cloud", cloud.Flags);
        Assert.Contains(model.Rows, row => row.Path == "tracked");
    }

    private static Snapshot Snapshot(long threshold, params SnapshotNode[] nodes) => new()
    {
        Header = new SnapshotHeader { Version = 1, Tool = "test", Drive = "C:", VolumeSerial = "1", FileSystem = "NTFS", Utc = "2026-01-01T00:00:00Z", Host = "test", Elevated = false, VolumeTotal = 1000, VolumeFree = 500, Metric = "allocated", DirectoryThreshold = 0, FileThreshold = threshold, ScanSeconds = 0, Skipped = new SkippedSummary { Count = 2, Sample = Array.Empty<string>() } },
        Nodes = nodes
    };

    private static SnapshotNode Node(string path, string kind, long size, string? rule = null, long? count = null, IReadOnlyList<string>? flags = null) => new()
    {
        Path = path, Kind = kind, Size = size, RuleId = rule, FileCount = count, Flags = flags
    };
}
