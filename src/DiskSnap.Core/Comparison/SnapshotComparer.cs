using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;

namespace DiskSnap.Core.Comparison;

public static class SnapshotComparer
{
    public static SnapshotComparison Compare(Snapshot older, Snapshot newer, IReadOnlyList<ScanRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);
        ArgumentNullException.ThrowIfNull(older.Header);
        ArgumentNullException.ThrowIfNull(newer.Header);
        if (string.IsNullOrWhiteSpace(older.Header.VolumeSerial) ||
            string.IsNullOrWhiteSpace(newer.Header.VolumeSerial) ||
            !string.Equals(older.Header.VolumeSerial, newer.Header.VolumeSerial, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Snapshots must have the same non-empty volume serial.");
        if (older.Header.FileThreshold < 0 || newer.Header.FileThreshold < 0)
            throw new ArgumentException("Snapshot file thresholds cannot be negative.");

        var oldNodes = Index(older);
        var newNodes = Index(newer);
        if (!oldNodes.ContainsKey("") || !newNodes.ContainsKey(""))
            throw new ArgumentException("Each snapshot must contain a root node.");

        var guidance = (rules ?? ScanRules.Defaults)
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ScanRule>)Array.AsReadOnly(group.ToArray()), StringComparer.OrdinalIgnoreCase);
        var nodes = new Dictionary<string, ComparisonNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, oldNode) in oldNodes)
        {
            newNodes.TryGetValue(path, out var newNode);
            nodes.Add(path, new ComparisonNode(newNode is null ? path : Normalize(newNode.Path), oldNode, newNode));
        }
        foreach (var (path, newNode) in newNodes)
        {
            if (!nodes.ContainsKey(path))
                nodes.Add(path, new ComparisonNode(path, null, newNode));
        }

        foreach (var (path, node) in nodes)
        {
            if (path.Length == 0)
                continue;
            var separator = path.LastIndexOf('\\');
            var parentPath = separator < 0 ? "" : path[..separator];
            if (!nodes.TryGetValue(parentPath, out var parent))
                throw new ArgumentException($"Snapshot node '{path}' has no parent.");
            node.Parent = parent;
            parent.MutableChildren.Add(node);
        }

        var root = nodes[""];
        var ordered = new List<ComparisonNode>(nodes.Count);
        var stack = new Stack<ComparisonNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            node.OlderSize = RecordedSize(node.OlderNode);
            node.NewerSize = RecordedSize(node.NewerNode);
            node.OlderGuidance = GuidanceFor(node.OlderNode, guidance);
            node.NewerGuidance = GuidanceFor(node.NewerNode, guidance);
            if (node.OlderNode is null)
                SetMissing(node, missingOlder: true, older.Header.FileThreshold);
            else if (node.NewerNode is null)
                SetMissing(node, missingOlder: false, newer.Header.FileThreshold);
            else
                node.Presence = node.OlderNode.Kind == "g" || node.NewerNode.Kind == "g" ? NodePresence.Aggregate : NodePresence.Both;

            ordered.Add(node);
            foreach (var child in node.Children)
                stack.Push(child);
        }

        foreach (var node in ordered)
        {
            node.MutableChildren.Sort((a, b) =>
            {
                var left = a.Delta.HasValue ? Math.Abs(a.Delta.Value) : -1;
                var right = b.Delta.HasValue ? Math.Abs(b.Delta.Value) : -1;
                var order = right.CompareTo(left);
                return order != 0 ? order : SnapshotComparison.ComparePaths(a.Path, b.Path);
            });
        }
        return new SnapshotComparison(older, newer, root, ordered.AsReadOnly());
    }

    private static void SetMissing(ComparisonNode node, bool missingOlder, long threshold)
    {
        var present = missingOlder ? node.NewerNode! : node.OlderNode!;
        var parent = node.Parent;
        var otherParent = missingOlder ? parent?.OlderNode : parent?.NewerNode;
        var parentDefinitelyAbsent = parent is not null &&
            (missingOlder ? parent.Presence == NodePresence.New : parent.Presence == NodePresence.Removed);
        var definitelyAbsent = parentDefinitelyAbsent || otherParent?.Kind is "f" or "l";
        var expanded = otherParent?.Kind == "d";
        var aggregate = present.Kind == "g";
        var knownAbsent = definitelyAbsent || (expanded &&
            (present.Kind is "d" or "c" or "l" or "g" || (present.Kind == "f" && threshold == 0)));

        node.Presence = aggregate ? NodePresence.Aggregate : knownAbsent
            ? (missingOlder ? NodePresence.New : NodePresence.Removed)
            : NodePresence.Unknown;
        if (knownAbsent)
        {
            if (missingOlder) node.OlderSize = 0;
            else node.NewerSize = 0;
        }
        else if (expanded && present.Kind == "f" && threshold > 0)
        {
            if (missingOlder) node.OlderFoldedFileThreshold = threshold;
            else node.NewerFoldedFileThreshold = threshold;
        }
    }

    private static long? RecordedSize(SnapshotNode? node) =>
        node is null || (node.Kind == "x" && node.FileCount is null) ? null : node.Size;

    private static IReadOnlyList<ScanRule> GuidanceFor(SnapshotNode? node, Dictionary<string, IReadOnlyList<ScanRule>> guidance) =>
        node?.RuleId is not null && guidance.TryGetValue(node.RuleId, out var matches) ? matches : Array.Empty<ScanRule>();

    private static Dictionary<string, SnapshotNode> Index(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot.Nodes);
        var result = new Dictionary<string, SnapshotNode>(snapshot.Nodes.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var node in snapshot.Nodes)
        {
            ArgumentNullException.ThrowIfNull(node);
            var path = Normalize(node.Path);
            if (node.Size < 0 || node.Kind is not ("d" or "c" or "f" or "g" or "l" or "x"))
                throw new ArgumentException($"Snapshot node '{path}' has an invalid kind or size.");
            if (!result.TryAdd(path, node))
                throw new ArgumentException($"Snapshot contains duplicate path '{path}'.");
        }
        foreach (var (path, node) in result)
        {
            if (path.Length == 0)
            {
                if (node.Kind is not ("d" or "c"))
                    throw new ArgumentException("The snapshot root must be a directory.");
                continue;
            }
            var separator = path.LastIndexOf('\\');
            var parent = separator < 0 ? "" : path[..separator];
            if (!result.TryGetValue(parent, out var parentNode) || parentNode.Kind != "d")
                throw new ArgumentException($"Snapshot node '{path}' must have an expanded parent.");
            if ((node.Kind == "g") != (path[(separator + 1)..] == "*"))
                throw new ArgumentException($"Snapshot node '{path}' has an invalid aggregate path.");
        }
        return result;
    }

    private static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path = path.Replace('/', '\\');
        if (path.Length > 0 && (path.StartsWith('\\') || path.EndsWith('\\') ||
            path.Split('\\').Any(segment => segment is "" or "." or "..")))
            throw new ArgumentException($"Invalid snapshot path '{path}'.");
        return path;
    }
}
