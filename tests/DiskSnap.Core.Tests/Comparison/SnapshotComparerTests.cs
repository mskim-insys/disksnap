using DiskSnap.Core.Comparison;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;

namespace DiskSnap.Core.Tests.Comparison;

public sealed class SnapshotComparerTests
{
    [Fact]
    public void DirectoriesAndDescendantsCanBeExactlyNewOrRemoved()
    {
        var older = Snapshot(10, Node("", "d", 50), Node("old", "d", 50), Node(@"old\file", "f", 50));
        var newer = Snapshot(10, Node("", "d", 80), Node("new", "d", 80), Node(@"new\file", "f", 80));
        var result = SnapshotComparer.Compare(older, newer);

        Assert.Equal(30, result.Root.Delta);
        AssertNode(result, "new", NodePresence.New, 0, 80, 80);
        AssertNode(result, @"new\file", NodePresence.New, 0, 80, 80);
        AssertNode(result, "old", NodePresence.Removed, 50, 0, -50);
        AssertNode(result, @"old\file", NodePresence.Removed, 50, 0, -50);
    }

    [Fact]
    public void CollapsedAndExcludedParentsRollChangesIntoComparableAncestor()
    {
        var older = Snapshot(10, Node("", "d", 100), Node("folder", "c", 100));
        var newer = Snapshot(10, Node("", "d", 180), Node("FOLDER", "d", 180), Node(@"FOLDER\child", "d", 180), Node(@"FOLDER\child\file", "f", 180));
        var result = SnapshotComparer.Compare(older, newer);

        AssertNode(result, "folder", NodePresence.Both, 100, 180, 80);
        AssertNode(result, @"folder\child", NodePresence.Unknown, null, 180, null);
        AssertNode(result, @"folder\child\file", NodePresence.Unknown, null, 180, null);
        Assert.Equal(80, result.Root.Delta);
        Assert.Single(result.TopGrowers(1), n => n.Path == "FOLDER");

        var excluded = Snapshot(10, Node("", "d", 0), Node("folder", "x", 0));
        result = SnapshotComparer.Compare(excluded, newer);
        Assert.Null(Find(result, "folder").Delta);
        Assert.Equal(NodePresence.Unknown, Find(result, @"folder\child").Presence);
    }

    [Fact]
    public void ChangedThresholdsDoNotInventFileDeltasAndAggregatesRemainSeparate()
    {
        var older = Snapshot(100, Node("", "d", 200), Node("*", "g", 80), Node("shrunk", "f", 120));
        var newer = Snapshot(60, Node("", "d", 150), Node("*", "g", 50), Node("grown", "f", 100));
        var result = SnapshotComparer.Compare(older, newer);

        var grown = Find(result, "grown");
        var shrunk = Find(result, "shrunk");
        AssertNode(result, "grown", NodePresence.Unknown, null, 100, null);
        Assert.Equal(100, grown.OlderFoldedFileThreshold);
        AssertNode(result, "shrunk", NodePresence.Unknown, 120, null, null);
        Assert.Equal(60, shrunk.NewerFoldedFileThreshold);
        AssertNode(result, "*", NodePresence.Aggregate, 80, 50, -30);
        Assert.Equal(-50, result.Root.Delta);
        Assert.DoesNotContain(result.TopGrowers(1), n => n.Path == "grown");
    }

    [Fact]
    public void ZeroThresholdAndAbsentAggregatesAreKnown()
    {
        var older = Snapshot(0, Node("", "d", 0));
        var newer = Snapshot(50, Node("", "d", 70), Node("file", "f", 60), Node("*", "g", 10));
        var result = SnapshotComparer.Compare(older, newer);
        AssertNode(result, "file", NodePresence.New, 0, 60, 60);
        AssertNode(result, "*", NodePresence.Aggregate, 0, 10, 10);
        Assert.Null(Find(result, "file").PercentChange);
        result = SnapshotComparer.Compare(newer, older);
        AssertNode(result, "file", NodePresence.Removed, 60, 0, -60);
        AssertNode(result, "*", NodePresence.Aggregate, 10, 0, -10);
    }

    [Fact]
    public void TopGrowersUseEightyPercentAndShrinkersUseSymmetricRule()
    {
        const long m = 1024 * 1024;
        var older = Snapshot(0, Node("", "d", 300 * m), Node("a", "d", 100 * m), Node(@"a\child", "c", 50 * m), Node("b", "c", 100 * m), Node("s", "c", 100 * m));
        var newer = Snapshot(0, Node("", "d", 440 * m), Node("a", "d", 200 * m), Node(@"a\child", "c", 130 * m), Node("b", "c", 190 * m), Node("s", "c", 50 * m));
        var result = SnapshotComparer.Compare(older, newer);
        var growers = result.TopGrowers();
        Assert.Equal(new[] { "", "b", @"a\child" }, growers.Select(n => n.Path));
        Assert.DoesNotContain(growers, n => n.Path == "a");
        Assert.Equal(160, Find(result, @"a\child").PercentChange);
        Assert.Equal(new[] { "s" }, result.TopGrowers(shrinkers: true).Select(n => n.Path));
        Assert.Empty(result.TopGrowers(200 * m));
        Assert.Throws<ArgumentOutOfRangeException>(() => result.TopGrowers(-1));
    }

    [Fact]
    public void BelowEightyPercentKeepsParentAndTiesSortByPath()
    {
        var older = Snapshot(0, Node("", "d", 0), Node("p", "d", 0), Node(@"p\z", "c", 0), Node(@"p\A", "c", 0));
        var newer = Snapshot(0, Node("", "d", 100), Node("p", "d", 100), Node(@"p\z", "c", 79), Node(@"p\A", "c", 79));
        var result = SnapshotComparer.Compare(older, newer);
        Assert.Equal(new[] { "p", @"p\A", @"p\z" }, result.TopGrowers(1).Select(n => n.Path));
        Assert.Equal(new[] { @"p\A", @"p\z" }, Find(result, "p").Children.Select(n => n.Path));
    }

    [Fact]
    public void KindsRulesAndCaseInsensitivePathsArePreserved()
    {
        var rule = new ScanRule { Id = "hint", Pattern = "*", Action = "track", Title = "title", Guidance = "guidance" };
        var older = Snapshot(0, Node("", "d", 30), Node("Dir", "c", 30, "hint"), Node("link", "l", 0), Node("excluded", "x", 0));
        var newer = Snapshot(0, Node("", "d", 50), Node("dir", "d", 50, "HINT"), Node(@"dir\*", "g", 50), Node("LINK", "l", 0), Node("excluded", "x", 0));
        var result = SnapshotComparer.Compare(older, newer, [rule]);
        var directory = Find(result, "DIR");
        Assert.Equal(20, directory.Delta);
        Assert.Equal("guidance", Assert.Single(directory.OlderGuidance).Guidance);
        Assert.Same(rule, Assert.Single(directory.NewerGuidance));
        Assert.Equal(NodePresence.Both, Find(result, "link").Presence);
        Assert.Null(Find(result, "excluded").OlderSize);
        Assert.Null(Find(result, "excluded").Delta);
        Assert.Null(Find(result, @"dir\*").OlderSize);
        Assert.Same(result.Root, directory.Parent);
    }

    [Fact]
    public void ChangedDirectoryThresholdAndLinksUseRecordedStructure()
    {
        var older = new Snapshot
        {
            Header = Header(10, "1234", 200),
            Nodes = [Node("", "d", 100), Node("dir", "c", 100), Node("oldLink", "l", 0)]
        };
        var newer = new Snapshot
        {
            Header = Header(10, "1234", 50),
            Nodes = [Node("", "d", 100), Node("dir", "d", 100), Node(@"dir\child", "c", 100), Node("newLink", "l", 0)]
        };
        var result = SnapshotComparer.Compare(older, newer);
        AssertNode(result, "dir", NodePresence.Both, 100, 100, 0);
        AssertNode(result, @"dir\child", NodePresence.Unknown, null, 100, null);
        AssertNode(result, "oldLink", NodePresence.Removed, 0, 0, 0);
        AssertNode(result, "newLink", NodePresence.New, 0, 0, 0);
    }

    [Fact]
    public void ShrinkerAttributionUsesEightyPercentBoundary()
    {
        var older = Snapshot(0, Node("", "d", 200), Node("dir", "d", 200), Node(@"dir\child", "c", 100), Node(@"dir\other", "c", 100));
        var newer = Snapshot(0, Node("", "d", 100), Node("dir", "d", 100), Node(@"dir\child", "c", 20), Node(@"dir\other", "c", 80));
        var result = SnapshotComparer.Compare(older, newer);
        Assert.Equal(new[] { @"dir\child", @"dir\other" }, result.TopGrowers(1, shrinkers: true).Select(n => n.Path));
        Assert.Equal(-80, Find(result, @"dir\child").PercentChange);
    }

    [Fact]
    public void MeasuredExclusionsRetainRecordedSizesButDoNotExposeChildren()
    {
        var oldX = new SnapshotNode { Path = "x", Kind = "x", Size = 30, FileCount = 2, RuleId = "winsxs" };
        var newX = new SnapshotNode { Path = "x", Kind = "x", Size = 60, FileCount = 3, RuleId = "winsxs" };
        var result = SnapshotComparer.Compare(Snapshot(0, Node("", "d", 30), oldX), Snapshot(0, Node("", "d", 60), newX));
        AssertNode(result, "x", NodePresence.Both, 30, 60, 30);
        Assert.NotEmpty(Find(result, "x").NewerGuidance);
    }

    [Fact]
    public void RefusesDifferentVolumeSerialsAndMalformedTrees()
    {
        var snapshot = Snapshot(0, Node("", "d", 0));
        var other = new Snapshot { Header = Header(0, "different"), Nodes = snapshot.Nodes };
        Assert.Contains("volume serial", Assert.Throws<ArgumentException>(() => SnapshotComparer.Compare(snapshot, other)).Message);
        var duplicates = Snapshot(0, Node("", "d", 0), Node("foo", "c", 0), Node("FOO", "c", 0));
        Assert.Throws<ArgumentException>(() => SnapshotComparer.Compare(snapshot, duplicates));
        var orphan = Snapshot(0, Node("", "d", 0), Node(@"missing\child", "c", 0));
        Assert.Throws<ArgumentException>(() => SnapshotComparer.Compare(snapshot, orphan));
    }

    [Fact]
    public void LargePairBuildsTreeAndFlatList()
    {
        const int count = 200_000;
        var oldNodes = new List<SnapshotNode>(count + 1) { Node("", "d", count) };
        var newNodes = new List<SnapshotNode>(count + 1) { Node("", "d", count * 2L) };
        for (var i = 0; i < count; i++)
        {
            var path = $"file{i:D6}";
            oldNodes.Add(Node(path, "f", 1));
            newNodes.Add(Node(path, "f", 2));
        }
        var result = SnapshotComparer.Compare(Snapshot(0, oldNodes), Snapshot(0, newNodes));
        Assert.Equal(count + 1, result.Nodes.Count);
        Assert.Equal(count, result.Root.Children.Count);
        Assert.Equal(count, result.Root.Delta);
        Assert.Equal(count + 1, result.TopGrowers(1).Count);
    }

    private static ComparisonNode Find(SnapshotComparison comparison, string path) =>
        Assert.Single(comparison.Nodes, node => string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase));

    private static void AssertNode(SnapshotComparison comparison, string path, NodePresence presence, long? older, long? newer, long? delta)
    {
        var node = Find(comparison, path);
        Assert.Equal(presence, node.Presence);
        Assert.Equal(older, node.OlderSize);
        Assert.Equal(newer, node.NewerSize);
        Assert.Equal(delta, node.Delta);
    }

    private static SnapshotNode Node(string path, string kind, long size, string? rule = null) =>
        new() { Path = path, Kind = kind, Size = size, RuleId = rule };

    private static Snapshot Snapshot(long threshold, params SnapshotNode[] nodes) => Snapshot(threshold, (IReadOnlyList<SnapshotNode>)nodes);
    private static Snapshot Snapshot(long threshold, IReadOnlyList<SnapshotNode> nodes) => new() { Header = Header(threshold, "1234"), Nodes = nodes };
    private static SnapshotHeader Header(long threshold, string serial, long directoryThreshold = 10) => new()
    {
        Version = 1, Tool = "test", Drive = "X:", VolumeSerial = serial, FileSystem = "NTFS",
        Utc = "2026-11-24T00:00:00Z", Host = "test", Elevated = false, VolumeTotal = 1000,
        VolumeFree = 500, Metric = "allocated", DirectoryThreshold = directoryThreshold, FileThreshold = threshold,
        ScanSeconds = 0, Skipped = new SkippedSummary { Count = 0, Sample = [] }
    };
}
