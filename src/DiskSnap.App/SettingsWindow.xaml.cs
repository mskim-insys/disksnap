using System.Globalization;
using System.Windows;
using DiskSnap.Core.Formatting;
using DiskSnap.Core.Storage;

namespace DiskSnap.App;

public partial class SettingsWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public SettingsWindow(MainWindowViewModel viewModel, DiskSnapSettings settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        MaxSnapshotsBox.Text = settings.MaxSnapshots.ToString(CultureInfo.InvariantCulture);
        DirectoryThresholdBox.Text = settings.DirectoryThreshold.ToString(CultureInfo.InvariantCulture) + "B";
        FileThresholdBox.Text = settings.FileThreshold.ToString(CultureInfo.InvariantCulture) + "B";
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxSnapshotsBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var max) || max < 1 ||
            !SizeTextParser.TryParse(DirectoryThresholdBox.Text, out var directory) ||
            !SizeTextParser.TryParse(FileThresholdBox.Text, out var file))
        {
            ErrorText.Text = "Enter a maximum of at least 1 and valid non-negative thresholds.";
            return;
        }
        try
        {
            IsEnabled = false;
            await _viewModel.SaveSettingsAsync(new DiskSnapSettings { MaxSnapshots = max, DirectoryThreshold = directory, FileThreshold = file });
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"Could not save settings: {exception.Message}";
            IsEnabled = true;
        }
    }
}
