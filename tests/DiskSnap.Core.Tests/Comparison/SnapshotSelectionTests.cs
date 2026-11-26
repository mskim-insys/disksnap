using DiskSnap.Core.Comparison;
using DiskSnap.Core.Storage;

namespace DiskSnap.Core.Tests.Comparison;

public sealed class SnapshotSelectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PickOnDifferentDriveReplacesChosenSlotAndClearsOther(bool selectAsOlder)
    {
        var oldA = Snapshot("X:");
        var oldB = Snapshot("X:");
        var picked = Snapshot("E:");
        var (older, newer, switched) = SnapshotSelection.Pick(picked, oldA, oldB, selectAsOlder);

        Assert.True(switched);
        Assert.Same(selectAsOlder ? picked : null, older);
        Assert.Same(selectAsOlder ? null : picked, newer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PickOnSameDriveOrWithEmptyOtherPreservesOtherSlot(bool selectAsOlder)
    {
        var picked = Snapshot("E:");
        var sameDrive = Snapshot("e:\\");
        var (older, newer, switched) = SnapshotSelection.Pick(picked, sameDrive, sameDrive, selectAsOlder);
        Assert.False(switched);
        Assert.Same(selectAsOlder ? picked : sameDrive, older);
        Assert.Same(selectAsOlder ? sameDrive : picked, newer);

        (older, newer, switched) = SnapshotSelection.Pick(picked, null, null, selectAsOlder);
        Assert.False(switched);
        Assert.Same(selectAsOlder ? picked : null, older);
        Assert.Same(selectAsOlder ? null : picked, newer);
    }

    [Theory]
    [InlineData("C:\\store\\one", "c:\\STORE\\ONE", null, "A")]
    [InlineData("C:\\store\\one", null, "c:\\STORE\\ONE", "B")]
    [InlineData("C:\\store\\one", "c:\\store\\one", "C:\\store\\one", "A/B")]
    [InlineData("C:\\store\\one", "C:\\store\\other", null, "")]
    [InlineData(null, null, null, "")]
    public void BadgeMatchesSnapshotPaths(string? path, string? older, string? newer, string expected) =>
        Assert.Equal(expected, SnapshotSelection.GetBadge(path, older, newer));

    private static SnapshotInfo Snapshot(string drive) =>
        new("snapshot", DateTimeOffset.UnixEpoch, 0, new SnapshotHeaderSummary(drive, "serial", "NTFS", 0, 0, 0, 0));
}
