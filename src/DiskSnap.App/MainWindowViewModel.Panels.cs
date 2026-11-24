using System.Windows.Input;
using DiskSnap.Core.Comparison;
using DiskSnap.Core.Formatting;
using DiskSnap.Core.Storage;

namespace DiskSnap.App;

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<TopGrowerRow> _topGrowers = Array.Empty<TopGrowerRow>();
    private SpecialFoldersModel? _specialFolders;
    private bool _shrinkers;
    private bool _includeRoot;
    private string _minimumDeltaText = "50";
    private string _topGrowersMessage = "Choose two snapshots to compare.";

    public ICommand ShowInTreeCommand { get; private set; } = null!;
    public IReadOnlyList<TopGrowerRow> TopGrowers { get => _topGrowers; private set => SetProperty(ref _topGrowers, value); }
    public SpecialFoldersModel? SpecialFolders { get => _specialFolders; private set => SetProperty(ref _specialFolders, value); }
    public string TopGrowersMessage { get => _topGrowersMessage; private set => SetProperty(ref _topGrowersMessage, value); }
    public bool Shrinkers { get => _shrinkers; set { if (SetProperty(ref _shrinkers, value)) RefreshTopGrowers(); } }
    public bool IncludeRoot { get => _includeRoot; set { if (SetProperty(ref _includeRoot, value)) RefreshTopGrowers(); } }
    public string MinimumDeltaText { get => _minimumDeltaText; set { if (SetProperty(ref _minimumDeltaText, value)) RefreshTopGrowers(); } }

    private void RefreshComparisonPanels()
    {
        SpecialFolders = _comparison is null ? null : new SpecialFoldersModel(_comparison);
        RefreshTopGrowers();
    }

    private void RefreshTopGrowers()
    {
        if (_comparison is null)
        {
            TopGrowers = Array.Empty<TopGrowerRow>();
            TopGrowersMessage = "Choose two snapshots to compare.";
        }
        else if (!SizeTextParser.TryParse(MinimumDeltaText, out var minimum))
        {
            TopGrowers = Array.Empty<TopGrowerRow>();
            TopGrowersMessage = "Enter a valid non-negative size (for example 50, 50MB or 1GiB).";
        }
        else
        {
            TopGrowers = TopGrowerRow.Create(_comparison, minimum, Shrinkers, IncludeRoot);
            TopGrowersMessage = TopGrowers.Count == 0 ? "No qualifying rows for these filters." : string.Empty;
        }
    }

    public void ShowInTree(string path)
    {
        if (_comparisonTree is null) return;
        var row = _comparisonTree.ExpandToPath(path);
        if (row is null && GrowthOnly)
        {
            GrowthOnly = false;
            row = _comparisonTree.ExpandToPath(path);
        }
        if (row is null)
        {
            StatusMessage = "The selected path is not available in the comparison tree.";
            return;
        }
        SelectedComparisonRow = row;
        JumpRequested?.Invoke(row);
    }

    public async Task<DiskSnapSettings> LoadSettingsAsync() => await _snapshotStore.LoadSettingsAsync();
    public async Task SaveSettingsAsync(DiskSnapSettings settings)
    {
        await _snapshotStore.SaveSettingsAsync(settings);
        StatusMessage = "Settings saved for future scans.";
    }
    public void ReportSettingsError(Exception exception) => StatusMessage = $"Settings error: {exception.Message}";
}
