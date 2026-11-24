namespace DiskSnap.Core.Scanning;

public sealed record ScanProgress(string CurrentPath, long ItemsScanned, int SkippedCount);
