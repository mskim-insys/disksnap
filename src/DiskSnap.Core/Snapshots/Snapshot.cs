namespace DiskSnap.Core.Snapshots;

public sealed class Snapshot
{
    public required SnapshotHeader Header { get; init; }
    public required IReadOnlyList<SnapshotNode> Nodes { get; init; }
}
