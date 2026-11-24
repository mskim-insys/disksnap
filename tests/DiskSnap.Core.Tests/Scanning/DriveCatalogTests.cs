using System.IO;
using DiskSnap.Core.Scanning;

namespace DiskSnap.Core.Tests.Scanning;

public sealed class DriveCatalogTests
{
    [Fact]
    public void ListsOnlyReadyFixedDrivesAndNormalizesDriveLetters()
    {
        var catalog = new DriveCatalog(() =>
        [
            new DriveCatalogEntry("c:\\", DriveType.Fixed, true),
            new DriveCatalogEntry("D:", DriveType.Fixed, true),
            new DriveCatalogEntry("E:", DriveType.Fixed, false),
            new DriveCatalogEntry("F:", DriveType.Removable, true),
            new DriveCatalogEntry("G:", DriveType.Network, true)
        ]);

        Assert.Equal(new[] { "C:", "D:" }, catalog.GetReadyFixedDrives());
    }
}
