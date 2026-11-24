using System.Diagnostics;
using DiskSnap.Core.Comparison;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;
using DiskSnap.Core.Tests.Scanning;
using Xunit.Abstractions;

namespace DiskSnap.Core.Tests.Comparison;

public sealed class ComparisonTreeModelTests(ITestOutputHelper output)
{
    [Fact]
    public void ExpandingAndCollapsingChangesWholeVisibleRanges()
    {
        var model = new ComparisonTreeModel(CreateNestedComparison());
        var root = Assert.Single(model.VisibleRows);
        var notifications = 0;
        model.VisibleRows.CollectionChanged += (_, args) =>
        {
            Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, args.Action);
            notifications++;
        };

        Assert.True(model.ToggleExpanded(root));
        Assert.Equal(3, model.VisibleRows.Count);
        Assert.Equal(new[] { "folder", "other" }, model.VisibleRows.Skip(1).Select(row => row.Node.Path));
        Assert.Equal(1, notifications);

        var folder = model.VisibleRows[1];
        Assert.True(model.ToggleExpanded(folder));
        Assert.Equal(5, model.VisibleRows.Count);
        Assert.Equal(2, notifications);

        Assert.True(model.ToggleExpanded(root));
        Assert.Single(model.VisibleRows);
        Assert.Equal(3, notifications);
        Assert.True(model.ToggleExpanded(root));
        Assert.Equal(new[] { "", "folder", @"folder\large.bin", @"folder\small.bin", "other" },
            model.VisibleRows.Select(row => row.Node.Path));
    }

    [Fact]
    public void GrowthOnlyKeepsChangedRowsAndTheirAncestors()
    {
        var model = new ComparisonTreeModel(CreateNestedComparison());
        model.GrowthOnly = true;

        Assert.Equal(new[] { "", "folder", @"folder\large.bin" }, model.VisibleRows.Select(row => row.Node.Path));
        Assert.All(model.VisibleRows, row => Assert.True(row.Node.Delta > 0 || row.Node.Observation == FileObservation.NewlyObserved));

        model.GrowthOnly = false;
        Assert.Single(model.VisibleRows);
    }

    [Fact]
    public void ExpandToPathExpandsAncestorsAndNormalizesSeparators()
    {
        var model = new ComparisonTreeModel(CreateNestedComparison());

        var selected = model.ExpandToPath("folder/large.bin");

        Assert.NotNull(selected);
        Assert.Equal(@"folder\large.bin", selected.Node.Path);
        Assert.True(model.VisibleRows[0].IsExpanded);
        Assert.True(model.VisibleRows[1].IsExpanded);
        Assert.Contains(selected, model.VisibleRows);
        Assert.Null(model.ExpandToPath("missing"));
    }

    [Fact]
    public void JumpToBiggestGrowerUsesTopGrowersAndExpandsItsPath()
    {
        var model = new ComparisonTreeModel(CreateNestedComparison());

        var selected = model.JumpToBiggestGrower();

        Assert.NotNull(selected);
        Assert.Equal(@"folder\large.bin", selected.Node.Path);
        Assert.Contains(selected, model.VisibleRows);
    }

    [Fact]
    public void GrowthOnlyHidesRowsWithoutGrowthEvenWhenTheirParentIsVisible()
    {
        var older = Snapshot(
            Node("", "d", 200), Node("grow", "d", 100), Node(@"grow\larger", "f", 100),
            Node("shrink", "c", 100), Node("removed", "c", 10));
        var newer = Snapshot(
            Node("", "d", 250), Node("grow", "d", 150), Node(@"grow\larger", "f", 150),
            Node("shrink", "c", 90));
        var model = new ComparisonTreeModel(SnapshotComparer.Compare(older, newer));
        model.GrowthOnly = true;

        Assert.Equal(new[] { "", "grow", @"grow\larger" }, model.VisibleRows.Select(row => row.Node.Path));
    }

    [Fact]
    public void GrowthOnlyIncludesNewlyObservedFilesAndDisplaysConditionalBoundsAndGuidance()
    {
        var rule = new ScanRule { Id = "hint", Pattern = "*", Action = "track", Title = "Rule title", Guidance = "Display-only guidance." };
        var older = SnapshotWithFileThreshold(20, Node("", "d", 0), Node("folder", "d", 0));
        var newer = SnapshotWithFileThreshold(20, Node("", "d", 30), Node("folder", "d", 30), Node(@"folder\new.bin", "f", 30, "hint"));
        var model = new ComparisonTreeModel(SnapshotComparer.Compare(older, newer, [rule]));
        model.GrowthOnly = true;

        var observed = Assert.Single(model.VisibleRows, row => row.Node.Path == @"folder\new.bin");
        Assert.Equal("new.bin (newly observed)", observed.DisplayName);
        Assert.Equal("-", observed.OlderDisplay);
        Assert.Equal("30 B", observed.NewerDisplay);
        Assert.StartsWith("at least +10 B", observed.DeltaDisplay);
        Assert.Contains("assuming ordinary-file folding", observed.DeltaDisplay);
        Assert.Contains("Rule title", observed.GuidanceTooltip);
        Assert.Contains("Display-only guidance.", observed.GuidanceTooltip);

        var reversed = new ComparisonTreeModel(SnapshotComparer.Compare(newer, older, [rule]));
        reversed.GrowthOnly = true;
        var noLongerObserved = Assert.Single(reversed.VisibleRows, row => row.Node.Path == @"folder\new.bin");
        Assert.Equal("new.bin (no longer observed)", noLongerObserved.DisplayName);
        Assert.StartsWith("at least -10 B", noLongerObserved.DeltaDisplay);
    }

    [OptInBenchmarkFact]
    public void FlattenAndExpandSyntheticFiveHundredThousandNodeComparison()
    {
        const int nodeCount = 500_000;
        var oldNodes = new List<SnapshotNode>(nodeCount + 1) { Node("", "d", nodeCount) };
        var newNodes = new List<SnapshotNode>(nodeCount + 1) { Node("", "d", nodeCount * 2L) };
        for (var index = 0; index < nodeCount; index++)
        {
            var path = $"file{index:D6}";
            oldNodes.Add(Node(path, "f", 1));
            newNodes.Add(Node(path, "f", 2));
        }

        var comparison = SnapshotComparer.Compare(Snapshot(oldNodes), Snapshot(newNodes));
        var flattenTimer = Stopwatch.StartNew();
        var model = new ComparisonTreeModel(comparison);
        flattenTimer.Stop();
        Assert.Equal(nodeCount + 1, model.VisibleRows.Count);

        var root = model.VisibleRows[0];
        Assert.True(model.ToggleExpanded(root));
        var expandTimer = Stopwatch.StartNew();
        Assert.True(model.ToggleExpanded(root));
        expandTimer.Stop();
        Assert.Equal(nodeCount + 1, model.VisibleRows.Count);
        output.WriteLine($"Flattened {nodeCount:N0} nodes in {flattenTimer.Elapsed}.");
        output.WriteLine($"Expanded {nodeCount:N0} direct children in {expandTimer.Elapsed}.");
    }

    private static SnapshotComparison CreateNestedComparison()
    {
        const long mebibyte = 1024 * 1024;
        var older = Snapshot(
            Node("", "d", 30 * mebibyte), Node("folder", "d", 20 * mebibyte), Node(@"folder\large.bin", "f", 20 * mebibyte),
            Node(@"folder\small.bin", "f", 0), Node("other", "c", 10 * mebibyte));
        var newer = Snapshot(
            Node("", "d", 80 * mebibyte), Node("folder", "d", 70 * mebibyte), Node(@"folder\large.bin", "f", 70 * mebibyte),
            Node(@"folder\small.bin", "f", 0), Node("other", "c", 10 * mebibyte));
        return SnapshotComparer.Compare(older, newer);
    }

    private static Snapshot Snapshot(params SnapshotNode[] nodes) => Snapshot((IReadOnlyList<SnapshotNode>)nodes, 0);

    private static Snapshot SnapshotWithFileThreshold(long fileThreshold, params SnapshotNode[] nodes) =>
        Snapshot((IReadOnlyList<SnapshotNode>)nodes, fileThreshold);

    private static Snapshot Snapshot(IReadOnlyList<SnapshotNode> nodes, long fileThreshold = 0) => new()
    {
        Header = new SnapshotHeader
        {
            Version = 1,
            Tool = "DiskSnap test",
            Drive = "C:",
            VolumeSerial = "1234",
            FileSystem = "NTFS",
            Utc = DateTimeOffset.UtcNow.ToString("O"),
            Host = "test",
            Elevated = false,
            VolumeTotal = 1000,
            VolumeFree = 500,
            Metric = "allocated",
            DirectoryThreshold = 0,
            FileThreshold = fileThreshold,
            ScanSeconds = 0,
            Skipped = new SkippedSummary { Count = 0, Sample = Array.Empty<string>() }
        },
        Nodes = nodes
    };

    private static SnapshotNode Node(string path, string kind, long size, string? ruleId = null) => new()
    {
        Path = path,
        Kind = kind,
        Size = size,
        RuleId = ruleId
    };
}
