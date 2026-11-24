using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Storage;

namespace DiskSnap.App;

public sealed class MainWindowViewModel : BindableObject
{
    private readonly DriveCatalog _driveCatalog;
    private readonly ScanService _scanService;
    private readonly SnapshotStore _snapshotStore;
    private readonly List<string> _snapshotWarnings = [];
    private CancellationTokenSource? _scanCancellation;
    private bool _isScanning;
    private string _currentPath = string.Empty;
    private long _itemsScanned;
    private int _skippedCount;
    private string _statusMessage;
    private string _selectionA = "A: not selected";
    private string _selectionB = "B: not selected";
    private SnapshotInfo? _selectedSnapshotA;
    private SnapshotInfo? _selectedSnapshotB;

    public MainWindowViewModel(
        DriveCatalog driveCatalog,
        ScanService scanService,
        SnapshotStore snapshotStore,
        string? startupWarning = null)
    {
        _driveCatalog = driveCatalog ?? throw new ArgumentNullException(nameof(driveCatalog));
        _scanService = scanService ?? throw new ArgumentNullException(nameof(scanService));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _statusMessage = startupWarning ?? "Ready.";

        ScanCommand = new AsyncRelayCommand(ScanSelectedDrivesAsync, () => !IsScanning);
        CancelCommand = new RelayCommand(_ => _scanCancellation?.Cancel(), _ => IsScanning);
        SelectACommand = new RelayCommand(SelectSnapshotA);
        SelectBCommand = new RelayCommand(SelectSnapshotB);
        DeleteSnapshotCommand = new RelayCommand(RequestSnapshotDeletion, _ => !IsScanning);
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];
    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectACommand { get; }
    public ICommand SelectBCommand { get; }
    public ICommand DeleteSnapshotCommand { get; }
    public event Action<SnapshotRowViewModel>? DeleteConfirmationRequested;

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ((AsyncRelayCommand)ScanCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
                ((RelayCommand)DeleteSnapshotCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public long ItemsScanned
    {
        get => _itemsScanned;
        private set => SetProperty(ref _itemsScanned, value);
    }

    public int SkippedCount
    {
        get => _skippedCount;
        private set => SetProperty(ref _skippedCount, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SelectionA
    {
        get => _selectionA;
        private set => SetProperty(ref _selectionA, value);
    }

    public string SelectionB
    {
        get => _selectionB;
        private set => SetProperty(ref _selectionB, value);
    }

    public SnapshotInfo? SelectedSnapshotA
    {
        get => _selectedSnapshotA;
        private set => SetProperty(ref _selectedSnapshotA, value);
    }

    public SnapshotInfo? SelectedSnapshotB
    {
        get => _selectedSnapshotB;
        private set => SetProperty(ref _selectedSnapshotB, value);
    }

    public async Task InitializeAsync()
    {
        try
        {
            _snapshotWarnings.Clear();
            foreach (var drive in _driveCatalog.GetReadyFixedDrives())
            {
                var item = new DriveItemViewModel(drive);
                Drives.Add(item);
                var warning = await RefreshDriveAsync(item).ConfigureAwait(true);
                if (warning is not null)
                {
                    _snapshotWarnings.Add(warning);
                }
            }

            if (_snapshotWarnings.Count > 0)
            {
                StatusMessage = string.Join(Environment.NewLine, _snapshotWarnings);
            }
            else if (Drives.Count == 0)
            {
                StatusMessage = "No ready fixed drives were found.";
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not load drives or snapshots: {exception.Message}";
        }
    }

    public async Task ConfirmDeleteAsync(SnapshotRowViewModel row, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _snapshotStore.DeleteSnapshotAsync(row.Snapshot.Path).ConfigureAwait(true);
            if (string.Equals(SelectedSnapshotA?.Path, row.Snapshot.Path, StringComparison.OrdinalIgnoreCase))
            {
                SelectedSnapshotA = null;
                SelectionA = "A: not selected";
            }
            if (string.Equals(SelectedSnapshotB?.Path, row.Snapshot.Path, StringComparison.OrdinalIgnoreCase))
            {
                SelectedSnapshotB = null;
                SelectionB = "B: not selected";
            }
            var drive = Drives.FirstOrDefault(item => string.Equals(item.Name, row.Snapshot.Header.Drive, StringComparison.OrdinalIgnoreCase));
            var warning = drive is null ? null : await RefreshDriveAsync(drive).ConfigureAwait(true);
            StatusMessage = $"Deleted snapshot {row.DateLabel}." +
                (warning is null ? string.Empty : Environment.NewLine + warning);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not delete snapshot: {exception.Message}";
        }
    }

    private async Task ScanSelectedDrivesAsync()
    {
        var selectedDrives = Drives.Where(drive => drive.IsSelected).Select(drive => drive.Name).ToArray();
        if (selectedDrives.Length == 0)
        {
            StatusMessage = "Select at least one drive to scan.";
            return;
        }

        using var warnings = new StringWriter(CultureInfo.CurrentCulture);
        _snapshotWarnings.Clear();
        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        CurrentPath = "Preparing scan...";
        ItemsScanned = 0;
        SkippedCount = 0;
        StatusMessage = "Scanning selected drives...";
        var dispatcher = Dispatcher.CurrentDispatcher;
        var progress = new DispatcherProgress<ScanServiceUpdate>(dispatcher, HandleScanUpdate);

        try
        {
            var result = await _scanService.RunAsync(
                selectedDrives,
                progress.Report,
                _scanCancellation.Token,
                warnings).ConfigureAwait(true);
            var messages = new List<string> { $"Scan complete: {result.SavedCount} saved, {result.FailedCount} failed." };
            messages.AddRange(result.Drives.Where(drive => !drive.Succeeded)
                .Select(drive => $"{drive.RequestedDrive}: {drive.Error}"));
            messages.AddRange(_snapshotWarnings);
            if (warnings.GetStringBuilder().Length > 0)
            {
                messages.Add(warnings.ToString().TrimEnd());
            }
            StatusMessage = string.Join(Environment.NewLine, messages);
        }
        catch (OperationCanceledException) when (_scanCancellation.IsCancellationRequested)
        {
            var messages = new List<string> { "Scan cancelled." };
            messages.AddRange(_snapshotWarnings);
            if (warnings.GetStringBuilder().Length > 0)
            {
                messages.Add(warnings.ToString().TrimEnd());
            }
            StatusMessage = string.Join(Environment.NewLine, messages);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Scan failed: {exception.Message}";
        }
        finally
        {
            IsScanning = false;
            CurrentPath = string.Empty;
            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    private void HandleScanUpdate(ScanServiceUpdate update)
    {
        if (update.Started)
        {
            CurrentPath = $"Scanning {update.Drive}...";
            ItemsScanned = 0;
            SkippedCount = 0;
        }
        else if (update.Progress is { } progress)
        {
            CurrentPath = progress.CurrentPath;
            ItemsScanned = progress.ItemsScanned;
            SkippedCount = progress.SkippedCount;
        }
        else if (update.Completed)
        {
            if (update.SnapshotPath is not null)
            {
                StatusMessage = $"Saved snapshot for {update.Drive}.";
            }
            else if (update.Error is not null)
            {
                StatusMessage = $"Scan of {update.RequestedDrive} failed: {update.Error}";
            }

            var drive = Drives.FirstOrDefault(item => string.Equals(item.Name, update.Drive, StringComparison.OrdinalIgnoreCase));
            if (drive is not null)
            {
                try
                {
                    var warning = RefreshDriveOnDispatcher(drive);
                    if (warning is not null)
                    {
                        _snapshotWarnings.Add(warning);
                    }
                }
                catch (Exception exception)
                {
                    _snapshotWarnings.Add($"Could not refresh {drive.Name} snapshots: {exception.Message}");
                }
            }
        }
    }

    private async Task<string?> RefreshDriveAsync(DriveItemViewModel drive)
    {
        using var warnings = new StringWriter(CultureInfo.CurrentCulture);
        var snapshots = await _snapshotStore.ListSnapshotsAsync(drive.Name, warnings).ConfigureAwait(true);
        drive.ReplaceSnapshots(snapshots);
        return warnings.GetStringBuilder().Length > 0 ? warnings.ToString().TrimEnd() : null;
    }

    private string? RefreshDriveOnDispatcher(DriveItemViewModel drive)
    {
        using var warnings = new StringWriter(CultureInfo.CurrentCulture);
        var snapshots = _snapshotStore.ListSnapshotsAsync(drive.Name, warnings).GetAwaiter().GetResult();
        drive.ReplaceSnapshots(snapshots);
        return warnings.GetStringBuilder().Length > 0 ? warnings.ToString().TrimEnd() : null;
    }

    private void SelectSnapshotA(object? parameter)
    {
        if (parameter is SnapshotRowViewModel row)
        {
            SelectedSnapshotA = row.Snapshot;
            SelectionA = $"A: {row.Snapshot.Header.Drive} - {row.DateLabel}";
        }
    }

    private void SelectSnapshotB(object? parameter)
    {
        if (parameter is SnapshotRowViewModel row)
        {
            SelectedSnapshotB = row.Snapshot;
            SelectionB = $"B: {row.Snapshot.Header.Drive} - {row.DateLabel}";
        }
    }

    private void RequestSnapshotDeletion(object? parameter)
    {
        if (parameter is SnapshotRowViewModel row)
        {
            DeleteConfirmationRequested?.Invoke(row);
        }
    }

    private sealed class DispatcherProgress<T>(Dispatcher dispatcher, Action<T> callback) : IProgress<T>
    {
        public void Report(T value)
        {
            if (dispatcher.CheckAccess())
            {
                callback(value);
            }
            else
            {
                dispatcher.Invoke(() => callback(value));
            }
        }
    }
}

public sealed class DriveItemViewModel(string name) : BindableObject
{
    private bool _isSelected;

    public string Name { get; } = name;
    public ObservableCollection<SnapshotRowViewModel> Snapshots { get; } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void ReplaceSnapshots(IEnumerable<SnapshotInfo> snapshots)
    {
        Snapshots.Clear();
        foreach (var snapshot in snapshots)
        {
            Snapshots.Add(new SnapshotRowViewModel(snapshot));
        }
    }
}

public sealed class SnapshotRowViewModel(SnapshotInfo snapshot)
{
    public SnapshotInfo Snapshot { get; } = snapshot;
    public string DateLabel { get; } = snapshot.Utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string SizeLabel { get; } = $"{snapshot.FileSize:N0} bytes";
    public string Summary => $"{snapshot.Header.FileSystem}; volume {snapshot.Header.VolumeSerial}; " +
        $"total {snapshot.Header.VolumeTotal:N0}; free {snapshot.Header.VolumeFree:N0}; " +
        $"scan {snapshot.Header.ScanSeconds:F1}s; skipped {snapshot.Header.SkippedCount:N0}";
}

public abstract class BindableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

internal sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();

    public async void Execute(object? parameter)
    {
        await execute();
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
