namespace DiskSnap.Core.Comparison;

public sealed class TopGrowerRow
{
    private readonly ComparisonTreeRow _text;

    private TopGrowerRow(ComparisonNode node) => _text = new ComparisonTreeRow(node, 0, false, false, 0);

    public ComparisonNode Node => _text.Node;
    public string Path => Node.Path.Length == 0 ? "(drive root)" : Node.Path;
    public string OlderDisplay => _text.OlderDisplay;
    public string NewerDisplay => _text.NewerDisplay;
    public string OlderTooltip => _text.OlderTooltip;
    public string NewerTooltip => _text.NewerTooltip;
    public string DeltaDisplay => _text.DeltaDisplay;
    public string DeltaTooltip => _text.DeltaTooltip;
    public string PercentDisplay => _text.PercentDisplay;
    public bool HasGuidance => _text.HasGuidance;
    public string GuidanceTooltip => _text.GuidanceTooltip;

    public static IReadOnlyList<TopGrowerRow> Create(SnapshotComparison comparison, long minDelta = 50L * 1024 * 1024, bool shrinkers = false, bool includeRoot = false)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return comparison.TopGrowers(minDelta, shrinkers, includeRoot).Select(node => new TopGrowerRow(node)).ToArray();
    }
}
