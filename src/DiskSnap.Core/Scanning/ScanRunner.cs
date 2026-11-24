using DiskSnap.Core.Snapshots;
using DiskSnap.Core.Storage;

namespace DiskSnap.Core.Scanning;

public sealed class ScanRunner
{
    private readonly SnapshotStore _store;
    private readonly Func<string, ScannerOptions, CancellationToken, Task<Snapshot>> _scan;

    public ScanRunner(SnapshotStore store, Func<string, ScannerOptions, CancellationToken, Task<Snapshot>>? scan = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scan = scan ?? ((drive, options, token) => new DiskScanner().ScanAsync(drive, options, token));
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

        var settings = await _store.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var rules = await _store.LoadRulesAsync(cancellationToken).ConfigureAwait(false);
        var options = new ScannerOptions
        {
            DirectoryThreshold = settings.DirectoryThreshold,
            FileThreshold = settings.FileThreshold,
            Rules = rules
        };
        var saved = 0;
        var failed = 0;
        foreach (var requestedDrive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var drive = SnapshotStore.NormalizeDrive(requestedDrive);
                if (!quiet)
                {
                    await output.WriteLineAsync($"Scanning {drive}...").ConfigureAwait(false);
                }

                var snapshot = await _scan(drive, options, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(SnapshotStore.NormalizeDrive(snapshot.Header.Drive), drive, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The scanned snapshot belongs to a different drive.");
                }

                var path = await _store.WriteAsync(snapshot, settings.MaxSnapshots, cancellationToken, error).ConfigureAwait(false);
                saved++;
                if (!quiet)
                {
                    await output.WriteLineAsync($"Saved {path}").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                await error.WriteLineAsync($"Scan of '{requestedDrive}' failed: {exception.Message}").ConfigureAwait(false);
            }
        }

        if (!quiet)
        {
            await output.WriteLineAsync($"Done: {saved} saved, {failed} failed").ConfigureAwait(false);
        }
        return failed > 0 ? 1 : 0;
    }
}
