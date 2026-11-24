using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;

namespace DiskSnap.Core.Comparison;

public enum NodePresence
{
    Both,
    New,
    Removed,
    Unknown,
    Aggregate
}

public enum FileObservation
{
    None,
    NewlyObserved,
    NoLongerObserved
}

public sealed class ComparisonNode
{
    private readonly List<ComparisonNode> _children = [];

    internal ComparisonNode(string path, SnapshotNode? older, SnapshotNode? newer)
    {
        Path = path;
        OlderNode = older;
        NewerNode = newer;
    }

    public string Path { get; }
    public SnapshotNode? OlderNode { get; }
    public SnapshotNode? NewerNode { get; }
    public ComparisonNode? Parent { get; internal set; }
    public IReadOnlyList<ComparisonNode> Children => _children;
    public NodePresence Presence { get; internal set; }
    public long? OlderSize { get; internal set; }
    public long? NewerSize { get; internal set; }
    public long? Delta => OlderSize.HasValue && NewerSize.HasValue ? NewerSize.Value - OlderSize.Value : null;
    public double? PercentChange => Delta.HasValue && OlderSize > 0 ? (double)Delta.Value / OlderSize.Value * 100 : null;
    // These are bounds only if an ordinary file was folded; exclusions or skips may also hide it.
    public long? OlderFoldedFileThreshold { get; internal set; }
    public long? NewerFoldedFileThreshold { get; internal set; }
    public FileObservation Observation => Presence != NodePresence.Unknown ? FileObservation.None :
        NewerNode?.Kind == "f" && OlderNode is null ? FileObservation.NewlyObserved :
        OlderNode?.Kind == "f" && NewerNode is null ? FileObservation.NoLongerObserved : FileObservation.None;
    // Conditional on the missing file having been folded rather than excluded or skipped.
    public long? FoldedFileChangeLowerBound => Observation switch
    {
        FileObservation.NewlyObserved when OlderFoldedFileThreshold.HasValue => NewerSize - OlderFoldedFileThreshold,
        FileObservation.NoLongerObserved when NewerFoldedFileThreshold.HasValue => OlderSize - NewerFoldedFileThreshold,
        _ => null
    };
    public IReadOnlyList<ScanRule> OlderGuidance { get; internal set; } = Array.Empty<ScanRule>();
    public IReadOnlyList<ScanRule> NewerGuidance { get; internal set; } = Array.Empty<ScanRule>();

    internal List<ComparisonNode> MutableChildren => _children;
}

public sealed class SnapshotComparison
{
    internal SnapshotComparison(Snapshot older, Snapshot newer, ComparisonNode root, IReadOnlyList<ComparisonNode> nodes)
    {
        Older = older;
        Newer = newer;
        Root = root;
        Nodes = nodes;
    }

    public Snapshot Older { get; }
    public Snapshot Newer { get; }
    public ComparisonNode Root { get; }
    public IReadOnlyList<ComparisonNode> Nodes { get; }

    public IReadOnlyList<ComparisonNode> TopGrowers(long minDelta = 50L * 1024 * 1024, bool shrinkers = false, bool includeRoot = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minDelta);
        var result = new List<ComparisonNode>();
        foreach (var node in Nodes)
        {
            if (!includeRoot && node == Root)
                continue;

            var magnitude = CandidateMagnitude(node, minDelta, shrinkers);
            if (!magnitude.HasValue)
                continue;

            var attributed = node.Children.Any(child =>
                CandidateMagnitude(child, minDelta, shrinkers) is long childMagnitude &&
                (decimal)childMagnitude * 5 >= (decimal)magnitude.Value * 4);
            if (!attributed)
                result.Add(node);
        }

        result.Sort((a, b) =>
        {
            var order = CandidateMagnitude(b, minDelta, shrinkers)!.Value.CompareTo(CandidateMagnitude(a, minDelta, shrinkers)!.Value);
            return order != 0 ? order : ComparePaths(a.Path, b.Path);
        });
        return result.AsReadOnly();
    }

    private static long? CandidateMagnitude(ComparisonNode node, long minDelta, bool shrinkers)
    {
        if (node.Delta is long delta && (shrinkers ? delta < 0 : delta > 0))
        {
            var magnitude = shrinkers ? -delta : delta;
            return magnitude >= minDelta ? magnitude : null;
        }

        if (!shrinkers && node.Observation == FileObservation.NewlyObserved &&
            node.NewerSize is long size && size >= minDelta &&
            node.Parent?.Delta is long parentDelta && parentDelta > 0 &&
            (decimal)size * 5 >= (decimal)parentDelta * 4)
            return size;

        return null;
    }

    internal static int ComparePaths(string left, string right)
    {
        var order = StringComparer.OrdinalIgnoreCase.Compare(left, right);
        return order != 0 ? order : StringComparer.Ordinal.Compare(left, right);
    }
}
