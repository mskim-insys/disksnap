using DiskSnap.Core.Storage;

namespace DiskSnap.Core.Comparison;

public static class SnapshotSelection
{
    public static (SnapshotInfo? Older, SnapshotInfo? Newer, bool SwitchedDrive) Pick(
        SnapshotInfo picked, SnapshotInfo? older, SnapshotInfo? newer, bool selectAsOlder)
    {
        ArgumentNullException.ThrowIfNull(picked);
        var other = selectAsOlder ? newer : older;
        var switchedDrive = other is not null && !string.Equals(
            SnapshotStore.NormalizeDrive(picked.Header.Drive),
            SnapshotStore.NormalizeDrive(other.Header.Drive),
            StringComparison.OrdinalIgnoreCase);
        if (switchedDrive)
        {
            other = null;
        }

        return selectAsOlder ? (picked, other, switchedDrive) : (other, picked, switchedDrive);
    }

    public static string GetBadge(string? path, string? olderPath, string? newerPath)
    {
        if (path is null)
        {
            return string.Empty;
        }

        var isOlder = string.Equals(path, olderPath, StringComparison.OrdinalIgnoreCase);
        var isNewer = string.Equals(path, newerPath, StringComparison.OrdinalIgnoreCase);
        return isOlder && isNewer ? "A/B" : isOlder ? "A" : isNewer ? "B" : string.Empty;
    }
}
