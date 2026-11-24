using DiskSnap.Core.Storage;

namespace DiskSnap.Core.Scanning;

public sealed class ScanRunner
{
    private readonly ScanService _service;

    public ScanRunner(
        SnapshotStore store,
        Func<string, ScannerOptions, CancellationToken, Task<DiskSnap.Core.Snapshots.Snapshot>>? scan = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _service = new ScanService(store, scan is null
            ? null
            : (drive, options, token, _) => scan(drive, options, token));
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> drives,
        bool quiet,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (drives.Count == 0)
        {
            await error.WriteLineAsync("At least one drive is required.").ConfigureAwait(false);
            return 1;
        }

        var result = await _service.RunAsync(
            drives,
            update =>
            {
                if (update.Started && !quiet)
                {
                    output.WriteLine($"Scanning {update.Drive}...");
                }
                else if (update.Completed && update.SnapshotPath is not null && !quiet)
                {
                    output.WriteLine($"Saved {update.SnapshotPath}");
                }
                else if (update.Completed && update.Error is not null)
                {
                    error.WriteLine($"Scan of '{update.RequestedDrive}' failed: {update.Error}");
                }
            },
            cancellationToken,
            error).ConfigureAwait(false);

        if (!quiet)
        {
            await output.WriteLineAsync($"Done: {result.SavedCount} saved, {result.FailedCount} failed").ConfigureAwait(false);
        }
        return result.FailedCount > 0 ? 1 : 0;
    }
}
