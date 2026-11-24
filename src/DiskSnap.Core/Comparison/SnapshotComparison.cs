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

    public IReadOnlyList<ComparisonNode> TopGrowers(long minDelta = 50L * 1024 * 1024, bool shrinkers = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minDelta);
        var result = new List<ComparisonNode>();
        foreach (var node in Nodes)
        {
            if (node.Delta is not long delta || (shrinkers ? delta >= 0 : delta <= 0))
                continue;

            var magnitude = shrinkers ? -delta : delta;
            if (magnitude < minDelta)
                continue;

            var attributed = false;
            foreach (var child in node.Children)
            {
                if (child.Delta is long childDelta && (shrinkers ? childDelta < 0 : childDelta > 0))
                {
                    var childMagnitude = shrinkers ? -childDelta : childDelta;
                    if ((decimal)childMagnitude * 5 >= (decimal)magnitude * 4)
                    {
                        attributed = true;
                        break;
                    }
                }
            }

            if (!attributed)
                result.Add(node);
        }

        result.Sort((a, b) =>
        {
            var order = shrinkers ? a.Delta!.Value.CompareTo(b.Delta!.Value) : b.Delta!.Value.CompareTo(a.Delta!.Value);
            return order != 0 ? order : ComparePaths(a.Path, b.Path);
        });
        return result.AsReadOnly();
    }

    internal static int ComparePaths(string left, string right)
    {
        var order = StringComparer.OrdinalIgnoreCase.Compare(left, right);
        return order != 0 ? order : StringComparer.Ordinal.Compare(left, right);
    }
}
