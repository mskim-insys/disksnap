using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskSnap.Core.Comparison;
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
        _viewModel.JumpRequested += row =>
        {
            MainTabs.SelectedIndex = 0;
            ComparisonList.ScrollIntoView(row);
            ComparisonList.Focus();
        };
        Loaded += MainWindowLoaded;
    }

    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindowLoaded;
        await _viewModel.InitializeAsync();
    }

    private void ComparisonListPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView list &&
            ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void ComparisonListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.SelectedComparisonRow is not { } row || e.Key is not (Key.Left or Key.Right))
            return;
        if (e.Key == Key.Right && row.HasChildren && !row.IsExpanded)
            _viewModel.ToggleComparisonRowCommand.Execute(row);
        else if (e.Key == Key.Left && row.IsExpanded)
            _viewModel.ToggleComparisonRowCommand.Execute(row);
        else if (e.Key == Key.Left && row.Node.Parent is { } parent)
        {
            var parentRow = _viewModel.ComparisonRows.FirstOrDefault(candidate => ReferenceEquals(candidate.Node, parent));
            if (parentRow is not null)
            {
                _viewModel.SelectedComparisonRow = parentRow;
                ComparisonList.ScrollIntoView(parentRow);
            }
        }
        e.Handled = true;
    }

    private void TopGrowersDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView list && ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListViewItem { DataContext: TopGrowerRow row })
            _viewModel.ShowInTree(row.Node.Path);
    }

    private async void SettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _viewModel.LoadSettingsAsync();
            new SettingsWindow(_viewModel, settings) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            _viewModel.ReportSettingsError(exception);
        }
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
