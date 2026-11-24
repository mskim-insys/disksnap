using System.IO;
using DiskSnap.Core.Storage;

namespace DiskSnap.Core.Scanning;

public sealed record DriveCatalogEntry(string Name, DriveType DriveType, bool IsReady);

public sealed class DriveCatalog
{
    private readonly Func<IEnumerable<DriveCatalogEntry>> _provider;

    public DriveCatalog(Func<IEnumerable<DriveCatalogEntry>>? provider = null)
    {
        _provider = provider ?? GetSystemDrives;
    }

    public IReadOnlyList<string> GetReadyFixedDrives() => _provider()
        .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
        .Select(drive => SnapshotStore.NormalizeDrive(drive.Name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(drive => drive, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IEnumerable<DriveCatalogEntry> GetSystemDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            bool isReady;
            try
            {
                isReady = drive.IsReady;
            }
            catch (IOException)
            {
                isReady = false;
            }

            yield return new DriveCatalogEntry(drive.Name, drive.DriveType, isReady);
        }
    }
}
