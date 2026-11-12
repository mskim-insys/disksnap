namespace DiskSnap.Core.Snapshots;

public sealed class SkippedSummary
{
    public required int Count { get; init; }
    public required IReadOnlyList<string> Sample { get; init; }
}
