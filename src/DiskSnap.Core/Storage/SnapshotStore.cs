using System.Globalization;
using System.Text.Json;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;

namespace DiskSnap.Core.Storage;

public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions SettingsJson = new() { WriteIndented = true };
    private readonly string _baseDirectory;

    public SnapshotStore(string? baseDirectory = null)
    {
        if (baseDirectory is null)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException("The local application data directory is unavailable.");
            }
            baseDirectory = Path.Combine(localAppData, "DiskSnap");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public async Task<DiskSnapSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_baseDirectory, "settings.json");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(_baseDirectory);
            var defaults = new DiskSnapSettings();
            var tempPath = Path.Combine(_baseDirectory, $".{Guid.NewGuid():N}.tmp");
            var created = false;
            try
            {
                await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    created = true;
                    await JsonSerializer.SerializeAsync(output, defaults, SettingsJson, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(tempPath, path);
                created = false;
            }
            finally
            {
                if (created)
                {
                    File.Delete(tempPath);
                }
            }

            return defaults;
        }

        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var settings = await JsonSerializer.DeserializeAsync<DiskSnapSettings>(input, SettingsJson, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The settings file is empty.");
        settings.Validate();
        return settings;
    }

    public async Task<IReadOnlyList<ScanRule>> LoadRulesAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_baseDirectory, "rules.json");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(_baseDirectory);
            var tempPath = Path.Combine(_baseDirectory, $".{Guid.NewGuid():N}.tmp");
            var created = false;
            try
            {
                await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    created = true;
                    await ScanRules.WriteDefaultsAsync(output, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(tempPath, path);
                created = false;
            }
            finally
            {
                if (created)
                {
                    File.Delete(tempPath);
                }
            }
        }

        return await ScanRules.LoadAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> WriteAsync(Snapshot snapshot, int maxSnapshots, CancellationToken cancellationToken = default, TextWriter? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        warnings ??= Console.Error;
        if (maxSnapshots < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSnapshots));
        }

        var drive = NormalizeDrive(snapshot.Header.Drive);
        if (!DateTimeOffset.TryParse(snapshot.Header.Utc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc))
        {
            throw new InvalidDataException("The snapshot header has an invalid timestamp.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshotsDirectory = Path.Combine(_baseDirectory, "snapshots");
        var driveDirectory = Path.Combine(snapshotsDirectory, drive[..1]);
        Directory.CreateDirectory(_baseDirectory);
        RejectReparsePoint(_baseDirectory);
        Directory.CreateDirectory(snapshotsDirectory);
        RejectReparsePoint(snapshotsDirectory);
        Directory.CreateDirectory(driveDirectory);
        RejectReparsePoint(driveDirectory);
        await CleanupStaleTempsAsync(driveDirectory, warnings, cancellationToken).ConfigureAwait(false);

        var finalPath = Path.Combine(driveDirectory, utc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl.gz");
        var tempPath = Path.Combine(driveDirectory, $".{Guid.NewGuid():N}.tmp");
        var tempCreated = false;
        try
        {
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                tempCreated = true;
                await SnapshotFile.WriteAsync(file, snapshot, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, finalPath);
            tempCreated = false;
        }
        finally
        {
            if (tempCreated)
            {
                File.Delete(tempPath);
            }
        }

        await ApplyRetentionAsync(driveDirectory, drive, maxSnapshots, warnings).ConfigureAwait(false);
        return finalPath;
    }

    public async Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(
        string requestedDrive,
        TextWriter? warnings = null,
        CancellationToken cancellationToken = default)
    {
        var drive = NormalizeDrive(requestedDrive);
        warnings ??= Console.Error;
        var snapshotsDirectory = Path.Combine(_baseDirectory, "snapshots");
        var driveDirectory = Path.Combine(snapshotsDirectory, drive[..1]);
        if (!Directory.Exists(_baseDirectory) || !Directory.Exists(snapshotsDirectory) || !Directory.Exists(driveDirectory))
        {
            return Array.Empty<SnapshotInfo>();
        }

        CheckStoreDirectories(driveDirectory);
        var snapshots = new List<SnapshotInfo>();
        foreach (var candidate in Directory.EnumerateFiles(driveDirectory, "*.jsonl.gz"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(candidate);
            if (!IsSnapshotName(Path.GetFileName(path)))
            {
                continue;
            }

            try
            {
                CheckStoreDirectories(driveDirectory);
                RejectReparsePoint(path);
                var header = await SnapshotFile.ReadHeaderAsync(path).ConfigureAwait(false);
                var utc = ValidateCandidateHeader(header, drive);
                snapshots.Add(new SnapshotInfo(
                    path,
                    utc,
                    new FileInfo(path).Length,
                    new SnapshotHeaderSummary(
                        header.Drive,
                        header.VolumeSerial,
                        header.FileSystem,
                        header.VolumeTotal,
                        header.VolumeFree,
                        header.ScanSeconds,
                        header.Skipped.Count)));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                await warnings.WriteLineAsync($"Warning: skipping snapshot '{path}': {exception.Message}").ConfigureAwait(false);
            }
        }

        return snapshots.OrderByDescending(snapshot => snapshot.Utc)
            .ThenByDescending(snapshot => snapshot.Path, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task DeleteSnapshotAsync(string snapshotPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(snapshotPath);
        var driveDirectory = Path.GetDirectoryName(path);
        var snapshotsDirectory = Path.Combine(_baseDirectory, "snapshots");
        if (driveDirectory is null ||
            !string.Equals(Path.GetDirectoryName(driveDirectory), snapshotsDirectory, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(driveDirectory).Length != 1 ||
            !IsSnapshotName(Path.GetFileName(path)))
        {
            throw new IOException($"Snapshot deletion target is outside the drive store: '{path}'.");
        }

        var drive = NormalizeDrive(Path.GetFileName(driveDirectory) + ":");
        var expectedDirectory = Path.Combine(snapshotsDirectory, drive[..1]);
        var prefix = Path.GetFullPath(expectedDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(path), expectedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Snapshot deletion target is outside the drive store: '{path}'.");
        }

        CheckStoreDirectories(expectedDirectory);
        RejectReparsePoint(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The snapshot to delete does not exist.", path);
        }

        var header = await SnapshotFile.ReadHeaderAsync(path).ConfigureAwait(false);
        ValidateCandidateHeader(header, drive);
        CheckStoreDirectories(expectedDirectory);
        RejectReparsePoint(path);
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
    }

    public static string NormalizeDrive(string drive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(drive);
        if ((drive.Length != 2 && !(drive.Length == 3 && drive[2] == '\\')) ||
            !char.IsAsciiLetter(drive[0]) || drive[1] != ':')
        {
            throw new ArgumentException("Drive must be a letter followed by a colon, optionally with a trailing backslash, for example C:.", nameof(drive));
        }

        return char.ToUpperInvariant(drive[0]) + ":";
    }

    private async Task CleanupStaleTempsAsync(string driveDirectory, TextWriter warnings, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        foreach (var path in Directory.EnumerateFiles(driveDirectory, "*.tmp"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name.Length != 37 || name[0] != '.' || !name.EndsWith(".tmp", StringComparison.Ordinal) ||
                !name.AsSpan(1, 32).ToString().All(char.IsAsciiHexDigit))
            {
                continue;
            }

            CheckStoreDirectories(driveDirectory);
            try
            {
                RejectReparsePoint(path);
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await warnings.WriteLineAsync($"Warning: could not clean temporary file '{path}': {exception.Message}").ConfigureAwait(false);
            }
        }
    }

    private async Task ApplyRetentionAsync(string driveDirectory, string drive, int maxSnapshots, TextWriter warnings)
    {
        CheckStoreDirectories(driveDirectory);
        var prefix = Path.GetFullPath(driveDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidates = new List<(string Path, DateTimeOffset Utc)>();
        foreach (var candidate in Directory.EnumerateFiles(driveDirectory, "*.jsonl.gz"))
        {
            var path = Path.GetFullPath(candidate);
            if (!IsSnapshotName(Path.GetFileName(path)))
            {
                continue;
            }
            try
            {
                RejectReparsePoint(path);
                var header = await SnapshotFile.ReadHeaderAsync(path).ConfigureAwait(false);
                candidates.Add((path, ValidateCandidateHeader(header, drive)));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                await warnings.WriteLineAsync($"Warning: skipping retention candidate '{path}': {exception.Message}").ConfigureAwait(false);
            }
        }

        foreach (var (path, _) in candidates.OrderByDescending(item => item.Utc)
                     .ThenByDescending(item => item.Path, StringComparer.Ordinal).Skip(maxSnapshots))
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(path), driveDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Retention target is outside the drive store: '{path}'.");
            }

            CheckStoreDirectories(driveDirectory);
            RejectReparsePoint(path);
            File.Delete(path);
        }
    }

    private void CheckStoreDirectories(string driveDirectory)
    {
        RejectReparsePoint(_baseDirectory);
        RejectReparsePoint(Path.Combine(_baseDirectory, "snapshots"));
        RejectReparsePoint(driveDirectory);
    }

    private static DateTimeOffset ValidateCandidateHeader(SnapshotHeader header, string drive)
    {
        if (header.Version != 1 || string.IsNullOrWhiteSpace(header.Drive) ||
            !string.Equals(NormalizeDrive(header.Drive), drive, StringComparison.Ordinal) ||
            !DateTimeOffset.TryParse(header.Utc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc) ||
            string.IsNullOrWhiteSpace(header.Tool) || string.IsNullOrWhiteSpace(header.VolumeSerial) ||
            string.IsNullOrWhiteSpace(header.FileSystem) || string.IsNullOrWhiteSpace(header.Host) ||
            header.Metric != "allocated" || header.Skipped is null || header.Skipped.Sample is null)
        {
            throw new InvalidDataException("Invalid or foreign-drive snapshot header.");
        }

        return utc.ToUniversalTime();
    }

    private static bool IsSnapshotName(string name) =>
        name.Length == 24 && name.EndsWith(".jsonl.gz", StringComparison.Ordinal) &&
        DateTime.TryParseExact(name[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Snapshot store path is a reparse point: '{path}'.");
        }
    }
}
