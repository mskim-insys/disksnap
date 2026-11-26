using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using DiskSnap.Core.Comparison;
using DiskSnap.Core.Snapshots;
using DiskSnap.Core.Storage;

namespace DiskSnap.App;

public sealed partial class MainWindowViewModel
{
    private static readonly ComparisonTreeRows EmptyComparisonRows = new();
    private ComparisonTreeRows _comparisonRows = EmptyComparisonRows;
    private bool _hasComparison;
    private ComparisonTreeModel? _comparisonTree;
    private SnapshotComparison? _comparison;
    private CancellationTokenSource? _comparisonCancellation;
    private ComparisonTreeRow? _selectedComparisonRow;
    private bool _isComparing;
    private bool _growthOnly;
    private long _comparisonGeneration;

    private void InitializeComparisonCommands()
    {
        CompareCommand = new AsyncRelayCommand(CompareSelectedAsync, () => CanCompare);
        ClearSelectionCommand = new RelayCommand(_ => ClearComparisonSelection());
        CompareLatestTwoCommand = new RelayCommand(parameter =>
        {
            if (parameter is DriveItemViewModel drive)
            {
                EnsureDefaultSelection(drive, force: true);
            }
        });
        ToggleComparisonRowCommand = new RelayCommand(parameter =>
        {
            if (parameter is ComparisonTreeRow row)
            {
                _comparisonTree?.ToggleExpanded(row);
            }
        });
        JumpToBiggestGrowerCommand = new RelayCommand(_ => JumpToBiggestGrower());
        OpenComparisonInExplorerCommand = new RelayCommand(OpenComparisonInExplorer);
        CopyComparisonPathCommand = new RelayCommand(CopyComparisonPath);
        ShowInTreeCommand = new RelayCommand(parameter =>
        {
            if (parameter is TopGrowerRow row)
                ShowInTree(row.Node.Path);
        });
    }

    public ICommand CompareCommand { get; private set; } = null!;
    public ICommand ClearSelectionCommand { get; private set; } = null!;
    public ICommand CompareLatestTwoCommand { get; private set; } = null!;
    public ICommand ToggleComparisonRowCommand { get; private set; } = null!;
    public ICommand JumpToBiggestGrowerCommand { get; private set; } = null!;
    public ICommand OpenComparisonInExplorerCommand { get; private set; } = null!;
    public ICommand CopyComparisonPathCommand { get; private set; } = null!;
    public event Action<ComparisonTreeRow>? JumpRequested;

    public ComparisonTreeRows ComparisonRows
    {
        get => _comparisonRows;
        private set => SetProperty(ref _comparisonRows, value);
    }

    public bool HasComparison
    {
        get => _hasComparison;
        private set => SetProperty(ref _hasComparison, value);
    }

    public bool IsComparing
    {
        get => _isComparing;
        private set
        {
            if (SetProperty(ref _isComparing, value))
            {
                ((AsyncRelayCommand)CompareCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool GrowthOnly
    {
        get => _growthOnly;
        set
        {
            if (SetProperty(ref _growthOnly, value) && _comparisonTree is not null)
            {
                _comparisonTree.GrowthOnly = value;
            }
        }
    }

    public ComparisonTreeRow? SelectedComparisonRow
    {
        get => _selectedComparisonRow;
        set => SetProperty(ref _selectedComparisonRow, value);
    }

    private bool CanCompare => !IsScanning && !IsComparing && SelectedSnapshotA is not null && SelectedSnapshotB is not null;

    private void SelectComparisonSnapshot(SnapshotInfo snapshot, bool selectAsOlder)
    {
        var (older, newer, switchedDrive) = SnapshotSelection.Pick(
            snapshot, SelectedSnapshotA, SelectedSnapshotB, selectAsOlder);
        var swapped = false;

        if (older is not null && newer is not null)
        {
            if (!string.Equals(older.Header.VolumeSerial, newer.Header.VolumeSerial, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Snapshots have different volume serials and cannot be compared.";
                return;
            }

            if (older.Utc > newer.Utc)
            {
                (older, newer) = (newer, older);
                swapped = true;
            }
        }

        SelectedSnapshotA = older;
        SelectedSnapshotB = newer;
        SelectionA = FormatSelection("A", older);
        SelectionB = FormatSelection("B", newer);
        NotifyComparisonCommandState();

        if (older is not null && newer is not null)
        {
            var message = swapped ? "Snapshots were selected in reverse order; A and B were swapped." : null;
            _ = RunComparisonAsync(message);
        }
        else
        {
            CancelComparison();
            SetComparison(null);
            if (switchedDrive)
            {
                StatusMessage = $"Switched to {SnapshotStore.NormalizeDrive(snapshot.Header.Drive)}. Choose the other snapshot, or use 'Compare latest two'.";
            }
        }
    }

    private void ClearComparisonSelection()
    {
        SelectedSnapshotA = null;
        SelectedSnapshotB = null;
        SelectionA = FormatSelection("A", null);
        SelectionB = FormatSelection("B", null);
        CancelComparison();
        SetComparison(null);
        NotifyComparisonCommandState();
        StatusMessage = "Selection cleared.";
    }

    private void EnsureDefaultSelection(DriveItemViewModel drive, bool force = false)
    {
        if (!force && SelectedSnapshotA is not null && SelectedSnapshotB is not null)
        {
            return;
        }

        var snapshots = drive.Snapshots.Select(row => row.Snapshot)
            .OrderByDescending(snapshot => snapshot.Utc)
            .ThenByDescending(snapshot => snapshot.Path, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (snapshots.Length < 2)
        {
            return;
        }

        var newer = snapshots[0];
        var older = snapshots[1];
        if (!SameDrive(older, newer))
        {
            StatusMessage = "Snapshots must be from the same drive.";
            return;
        }

        if (!string.Equals(older.Header.VolumeSerial, newer.Header.VolumeSerial, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "The newest snapshots have different volume serials and cannot be compared.";
            return;
        }

        SelectedSnapshotA = older;
        SelectedSnapshotB = newer;
        SelectionA = FormatSelection("A", older);
        SelectionB = FormatSelection("B", newer);
        NotifyComparisonCommandState();
        _ = RunComparisonAsync(null);
    }

    private async Task CompareSelectedAsync()
    {
        if (SelectedSnapshotA is null || SelectedSnapshotB is null)
        {
            StatusMessage = "Choose two snapshots to compare.";
            return;
        }

        await RunComparisonAsync(null);
    }

    private async Task RunComparisonAsync(string? selectionNote)
    {
        var olderInfo = SelectedSnapshotA;
        var newerInfo = SelectedSnapshotB;
        if (olderInfo is null || newerInfo is null)
        {
            return;
        }

        if (!SameDrive(olderInfo, newerInfo))
        {
            StatusMessage = "Snapshots must be from the same drive.";
            return;
        }

        if (!string.Equals(olderInfo.Header.VolumeSerial, newerInfo.Header.VolumeSerial, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Snapshots have different volume serials and cannot be compared.";
            return;
        }

        if (olderInfo.Utc > newerInfo.Utc)
        {
            (olderInfo, newerInfo) = (newerInfo, olderInfo);
            SelectedSnapshotA = olderInfo;
            SelectedSnapshotB = newerInfo;
            SelectionA = FormatSelection("A", olderInfo);
            SelectionB = FormatSelection("B", newerInfo);
            selectionNote = "Snapshots were selected in reverse order; A and B were swapped.";
        }

        var generation = Interlocked.Increment(ref _comparisonGeneration);
        var cancellation = new CancellationTokenSource();
        var previousCancellation = _comparisonCancellation;
        _comparisonCancellation = cancellation;
        previousCancellation?.Cancel();
        SetComparison(null);
        IsComparing = true;
        StatusMessage = selectionNote is null ? "Comparing snapshots..." : selectionNote + " Comparing snapshots...";

        try
        {
            var rules = await _snapshotStore.LoadRulesAsync(cancellation.Token).ConfigureAwait(true);
            var snapshots = await Task.Run(async () =>
            {
                var older = await SnapshotFile.ReadAsync(olderInfo.Path, cancellation.Token).ConfigureAwait(false);
                var newer = await SnapshotFile.ReadAsync(newerInfo.Path, cancellation.Token).ConfigureAwait(false);
                return (Older: older, Newer: newer);
            }, cancellation.Token).ConfigureAwait(true);
            var comparison = await Task.Run(
                () => SnapshotComparer.Compare(snapshots.Older, snapshots.Newer, rules),
                cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();

            if (generation == _comparisonGeneration)
            {
                SetComparison(comparison);
                StatusMessage = selectionNote is null ? "Comparison complete." : selectionNote + " Comparison complete.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation == _comparisonGeneration)
            {
                SetComparison(null);
                StatusMessage = $"Comparison failed: {exception.GetType().Name}: {exception.Message}";
            }
        }
        finally
        {
            if (generation == _comparisonGeneration)
            {
                IsComparing = false;
                _comparisonCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void SetComparison(SnapshotComparison? comparison)
    {
        _comparison = comparison;
        _comparisonTree = comparison is null ? null : new ComparisonTreeModel(comparison);
        if (_comparisonTree is not null)
        {
            _comparisonTree.GrowthOnly = GrowthOnly;
        }

        SelectedComparisonRow = null;
        ComparisonRows = _comparisonTree?.VisibleRows ?? EmptyComparisonRows;
        HasComparison = comparison is not null;
        RefreshComparisonPanels();
    }

    private void CancelComparison()
    {
        Interlocked.Increment(ref _comparisonGeneration);
        _comparisonCancellation?.Cancel();
        _comparisonCancellation = null;
        IsComparing = false;
    }

    private void JumpToBiggestGrower()
    {
        var row = _comparisonTree?.JumpToBiggestGrower();
        if (row is null)
        {
            StatusMessage = "No qualifying grower was found in this comparison.";
            return;
        }

        SelectedComparisonRow = row;
        JumpRequested?.Invoke(row);
    }

    private void OpenComparisonInExplorer(object? parameter)
    {
        if (!TryGetComparisonPath(parameter, out var path))
        {
            return;
        }

        while (!File.Exists(path) && !Directory.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (parent is null || string.Equals(parent, path, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "No existing path on this drive could be opened in Explorer.";
                return;
            }

            path = parent;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            StatusMessage = $"Opened {path} in Explorer.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not open Explorer: {exception.Message}";
        }
    }

    private void CopyComparisonPath(object? parameter)
    {
        if (!TryGetComparisonPath(parameter, out var path))
        {
            return;
        }

        try
        {
            Clipboard.SetText(path);
            StatusMessage = "Copied path to the clipboard.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not copy the path: {exception.Message}";
        }
    }

    private bool TryGetComparisonPath(object? parameter, out string path)
    {
        path = string.Empty;
        var node = parameter switch
        {
            ComparisonTreeRow row => row.Node,
            TopGrowerRow row => row.Node,
            _ => null
        };
        if (_comparison is null || node is null)
        {
            StatusMessage = "Select a comparison row first.";
            return false;
        }

        try
        {
            var root = SnapshotStore.NormalizeDrive(_comparison.Newer.Header.Drive) + Path.DirectorySeparatorChar;
            var relative = node.Path;
            if (node.Presence == NodePresence.Aggregate)
            {
                relative = relative == "*" ? string.Empty : relative.EndsWith("\\*", StringComparison.Ordinal) ? relative[..^2] : relative;
            }

            path = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar)));
            var rootPath = Path.GetFullPath(root);
            if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(path, rootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The selected path is outside the comparison drive.");
            }

            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not resolve the selected path: {exception.Message}";
            return false;
        }
    }

    private void NotifyComparisonCommandState() =>
        ((AsyncRelayCommand)CompareCommand).NotifyCanExecuteChanged();

    private static bool SameDrive(SnapshotInfo first, SnapshotInfo second) =>
        string.Equals(SnapshotStore.NormalizeDrive(first.Header.Drive), SnapshotStore.NormalizeDrive(second.Header.Drive), StringComparison.OrdinalIgnoreCase);

    private static string FormatSelection(string label, SnapshotInfo? snapshot) => snapshot is null
        ? $"{label}: not selected"
        : $"{label}: {SnapshotStore.NormalizeDrive(snapshot.Header.Drive)} - {snapshot.Utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
}

public sealed class SnapshotSelectionBadgeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        SnapshotSelection.GetBadge(values[0] as string, values[1] as string, values[2] as string);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
