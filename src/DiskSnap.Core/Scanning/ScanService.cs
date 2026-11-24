using DiskSnap.Core.Snapshots;
using DiskSnap.Core.Storage;

namespace DiskSnap.Core.Scanning;

public sealed record ScanServiceUpdate(
    string Drive,
    string? RequestedDrive = null,
    ScanProgress? Progress = null,
    bool Started = false,
    bool Completed = false,
    string? SnapshotPath = null,
    string? Error = null);

public sealed record ScanDriveResult(string RequestedDrive, string Drive, string? SnapshotPath, string? Error)
{
    public bool Succeeded => Error is null;
}

public sealed record ScanBatchResult(IReadOnlyList<ScanDriveResult> Drives)
{
    public int SavedCount => Drives.Count(drive => drive.Succeeded);
    public int FailedCount => Drives.Count(drive => !drive.Succeeded);
}

public sealed class ScanService
{
    private readonly SnapshotStore _store;
    private readonly Func<string, ScannerOptions, CancellationToken, IProgress<ScanProgress>?, Task<Snapshot>> _scan;

    public ScanService(
        SnapshotStore store,
        Func<string, ScannerOptions, CancellationToken, IProgress<ScanProgress>?, Task<Snapshot>>? scan = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scan = scan ?? ((drive, options, token, progress) => new DiskScanner().ScanAsync(drive, options, token, progress));
    }

    public async Task<ScanBatchResult> RunAsync(
        IReadOnlyList<string> drives,
        Action<ScanServiceUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default,
        TextWriter? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(drives);
        var settings = await _store.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var rules = await _store.LoadRulesAsync(cancellationToken).ConfigureAwait(false);
        var options = new ScannerOptions
        {
            DirectoryThreshold = settings.DirectoryThreshold,
            FileThreshold = settings.FileThreshold,
            Rules = rules
        };
        var results = new List<ScanDriveResult>(drives.Count);

        foreach (var requestedDrive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var drive = requestedDrive;
            try
            {
                drive = SnapshotStore.NormalizeDrive(requestedDrive);
                onUpdate?.Invoke(new ScanServiceUpdate(drive, requestedDrive, Started: true));
                var scanProgress = new CallbackProgress<ScanProgress>(value =>
                    onUpdate?.Invoke(new ScanServiceUpdate(drive, requestedDrive, Progress: value)));
                var snapshot = await _scan(drive, options, cancellationToken, scanProgress).ConfigureAwait(false);
                if (!string.Equals(SnapshotStore.NormalizeDrive(snapshot.Header.Drive), drive, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The scanned snapshot belongs to a different drive.");
                }

                var path = await _store.WriteAsync(snapshot, settings.MaxSnapshots, cancellationToken, warnings).ConfigureAwait(false);
                results.Add(new ScanDriveResult(requestedDrive, drive, path, null));
                onUpdate?.Invoke(new ScanServiceUpdate(drive, requestedDrive, Completed: true, SnapshotPath: path));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new ScanDriveResult(requestedDrive, drive, null, exception.Message));
                onUpdate?.Invoke(new ScanServiceUpdate(drive, requestedDrive, Completed: true, Error: exception.Message));
            }
        }

        return new ScanBatchResult(results);
    }

    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
