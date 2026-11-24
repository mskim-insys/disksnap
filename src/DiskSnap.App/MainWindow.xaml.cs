using System.Windows;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Storage;

namespace DiskSnap.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var store = new SnapshotStore();
        _viewModel = new MainWindowViewModel(
            new DriveCatalog(),
            new ScanService(store),
            store,
            ((App)Application.Current).StartupWarning);
        DataContext = _viewModel;
        _viewModel.DeleteConfirmationRequested += ConfirmSnapshotDeletion;
        Loaded += MainWindowLoaded;
    }

    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindowLoaded;
        await _viewModel.InitializeAsync();
    }

    private async void ConfirmSnapshotDeletion(SnapshotRowViewModel row)
    {
        var result = MessageBox.Show(
            this,
            $"Delete this snapshot?\n\n{row.Snapshot.Path}",
            "Delete snapshot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        await _viewModel.ConfirmDeleteAsync(row, result == MessageBoxResult.Yes);
    }
}
