using DiskSnap.Core.Formatting;

namespace DiskSnap.Core.Comparison;

public sealed class SpecialFoldersModel
{
    public SpecialFoldersModel(SnapshotComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var header = comparison.Newer.Header;
        SkippedCount = header.Skipped.Count;
        Unaccounted = header.VolumeTotal - header.VolumeFree - comparison.Root.NewerNode!.Size;
        Rows = comparison.Nodes
            .Where(node => node.NewerNode is { } newer &&
                (newer.Kind is "x" or "c" || newer.Flags?.Contains("cloud", StringComparer.OrdinalIgnoreCase) == true || newer.RuleId is not null))
            .Select(node => new SpecialFolderRow(node)).ToArray();
    }

    public IReadOnlyList<SpecialFolderRow> Rows { get; }
    public int SkippedCount { get; }
    public long Unaccounted { get; }
    public string UnaccountedDisplay => SizeFormatter.Format(Unaccounted);
    public string UnaccountedTooltip => SizeFormatter.FormatExactBytes(Unaccounted);
}

public sealed class SpecialFolderRow
{
    internal SpecialFolderRow(ComparisonNode node)
    {
        Node = node;
        var newer = node.NewerNode!;
        Flags = string.Join(", ", newer.Flags ?? Array.Empty<string>());
        IsCloud = newer.Flags?.Contains("cloud", StringComparer.OrdinalIgnoreCase) == true;
        Title = node.NewerGuidance.Count > 0 ? string.Join(", ", node.NewerGuidance.Select(rule => rule.Title)) : newer.RuleId ?? "-";
        Guidance = string.Join(Environment.NewLine + Environment.NewLine, node.NewerGuidance.Select(rule => $"{rule.Title}{Environment.NewLine}{rule.Guidance}"));
        if (IsCloud)
            Guidance = (Guidance.Length == 0 ? "" : Guidance + Environment.NewLine + Environment.NewLine) + "Cloud folder contents are not enumerated; local content may be omitted.";
    }

    public ComparisonNode Node { get; }
    public string Path => Node.Path.Length == 0 ? "(drive root)" : Node.Path;
    public string Kind => Node.NewerNode!.Kind;
    public string Title { get; }
    public string Guidance { get; }
    public string Flags { get; }
    public bool IsCloud { get; }
    public string SizeDisplay => SizeFormatter.Format(Node.NewerSize);
    public string SizeTooltip => SizeFormatter.FormatExactBytes(Node.NewerSize);
    public string TrendDisplay => Node.OlderNode is not null && Node.NewerNode is not null && Node.Delta is long delta
        ? (delta >= 0 ? "+" : "-") + SizeFormatter.Format(Math.Abs(delta)) : "-";
    public string TrendTooltip => SizeFormatter.FormatExactBytes(Node.OlderNode is not null ? Node.Delta : null);
}
