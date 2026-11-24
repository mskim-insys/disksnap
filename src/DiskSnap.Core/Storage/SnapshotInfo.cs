namespace DiskSnap.Core.Storage;

public sealed record SnapshotHeaderSummary(
    string Drive,
    string VolumeSerial,
    string FileSystem,
    long VolumeTotal,
    long VolumeFree,
    double ScanSeconds,
    int SkippedCount);

public sealed record SnapshotInfo(
    string Path,
    DateTimeOffset Utc,
    long FileSize,
    SnapshotHeaderSummary Header);
