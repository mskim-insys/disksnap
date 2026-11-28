using System.Globalization;
using System.Text;
using System.Text.Json;
using DiskSnap.Core.Comparison;
using DiskSnap.Core.Snapshots;
using DiskSnap.Core.Storage;

namespace DiskSnap.Core.Reporting;

public sealed class ComparisonReportRunner
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly SnapshotStore _store;

    public ComparisonReportRunner(SnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<int> RunAsync(
        string drive,
        string? aPath,
        string? bPath,
        string? outputPath,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            drive = SnapshotStore.NormalizeDrive(drive);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            if ((aPath is null) != (bPath is null))
                throw new ArgumentException("--a and --b must be supplied together.");

            Snapshot older;
            Snapshot newer;
            if (aPath is not null)
            {
                older = await SnapshotFile.ReadAsync(aPath, cancellationToken).ConfigureAwait(false);
                newer = await SnapshotFile.ReadAsync(bPath!, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var candidates = await _store.ListSnapshotsAsync(drive, error, cancellationToken).ConfigureAwait(false);
                if (candidates.Count < 2)
                    throw new InvalidOperationException($"At least two valid snapshots are required for {drive}.");
                if (!string.Equals(candidates[0].Header.VolumeSerial, candidates[1].Header.VolumeSerial, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The newest two snapshots have different volume serials.");

                older = await SnapshotFile.ReadAsync(candidates[1].Path, cancellationToken).ConfigureAwait(false);
                newer = await SnapshotFile.ReadAsync(candidates[0].Path, cancellationToken).ConfigureAwait(false);
            }

            ValidateDrive(older, drive);
            ValidateDrive(newer, drive);
            if (string.IsNullOrWhiteSpace(older.Header.VolumeSerial) ||
                !string.Equals(older.Header.VolumeSerial, newer.Header.VolumeSerial, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Snapshots must have the same non-empty volume serial.");

            var olderUtc = ParseUtc(older.Header.Utc);
            var newerUtc = ParseUtc(newer.Header.Utc);
            if (olderUtc > newerUtc)
                (older, newer) = (newer, older);

            var rules = await _store.LoadRulesAsync(cancellationToken).ConfigureAwait(false);
            var comparison = SnapshotComparer.Compare(older, newer, rules);
            var report = ComparisonReportGenerator.Generate(comparison);
            var fullOutputPath = Path.GetFullPath(outputPath);
            await WriteAtomicallyAsync(fullOutputPath, report, cancellationToken).ConfigureAwait(false);

            await output.WriteLineAsync($"Saved report to {fullOutputPath}").ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
                                          InvalidDataException or InvalidOperationException or NotSupportedException or JsonException)
        {
            await error.WriteLineAsync($"Compare failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static void ValidateDrive(Snapshot snapshot, string drive)
    {
        var snapshotDrive = SnapshotStore.NormalizeDrive(snapshot.Header.Drive);
        if (!string.Equals(snapshotDrive, drive, StringComparison.Ordinal))
            throw new InvalidOperationException($"Snapshot drive '{snapshotDrive}' does not match requested drive '{drive}'.");
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc))
            throw new InvalidDataException("A snapshot header has an invalid timestamp.");
        return utc.ToUniversalTime();
    }

    private static async Task WriteAtomicallyAsync(string outputPath, string report, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new ArgumentException("The report output path must have a parent directory.", nameof(outputPath));
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"The report output directory does not exist: '{directory}'.");

        var temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await using var writer = new StreamWriter(file, Utf8WithoutBom, 4096, leaveOpen: true);
                await writer.WriteAsync(report.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
