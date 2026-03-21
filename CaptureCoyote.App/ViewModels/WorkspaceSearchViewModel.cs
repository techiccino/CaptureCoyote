using System.Collections.ObjectModel;
using System.ComponentModel;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Mvvm;

namespace CaptureCoyote.App.ViewModels;

public sealed class WorkspaceSearchViewModel : ObservableObject
{
    private const string DefaultModeFilter = "All Modes";
    private const string DefaultTypeFilter = "All Items";
    private const string DefaultDateFilter = "Any Time";
    private const string DefaultSortOption = "Newest First";

    private readonly Func<RecentWorkspaceItem, Task> _openItemAsync;
    private readonly Action<RecentWorkspaceItem> _revealItemFolder;
    private readonly Func<IReadOnlyList<RecentWorkspaceItem>, Task<int>> _deleteItemsAsync;
    private readonly Action<IReadOnlyList<RecentWorkspaceItem>> _revealSelectedItemFolders;
    private readonly List<LauncherRecentItemViewModel> _allItems = [];
    private string _searchText = string.Empty;
    private string _selectedTypeFilter = DefaultTypeFilter;
    private string _selectedModeFilter = DefaultModeFilter;
    private string _selectedDateFilter = DefaultDateFilter;
    private string _selectedSortOption = DefaultSortOption;

    public WorkspaceSearchViewModel(
        IEnumerable<RecentWorkspaceItem> items,
        Func<RecentWorkspaceItem, Task> openItemAsync,
        Action<RecentWorkspaceItem> revealItemFolder,
        Func<IReadOnlyList<RecentWorkspaceItem>, Task<int>> deleteItemsAsync,
        Action<IReadOnlyList<RecentWorkspaceItem>> revealSelectedItemFolders)
    {
        _openItemAsync = openItemAsync;
        _revealItemFolder = revealItemFolder;
        _deleteItemsAsync = deleteItemsAsync;
        _revealSelectedItemFolders = revealSelectedItemFolders;

        ModeFilters = new ObservableCollection<string>
        {
            DefaultModeFilter,
            "Region",
            "Window",
            "Scrolling",
            "Full Screen"
        };

        TypeFilters = new ObservableCollection<string>
        {
            DefaultTypeFilter,
            "Projects",
            "Captures"
        };

        DateFilters = new ObservableCollection<string>
        {
            DefaultDateFilter,
            "Today",
            "Last 7 Days",
            "Last 30 Days"
        };

        SortOptions = new ObservableCollection<string>
        {
            DefaultSortOption,
            "Oldest First",
            "Name A-Z",
            "Name Z-A",
            "Projects First"
        };

        FilteredItems = new ObservableCollection<LauncherRecentItemViewModel>();
        OpenItemCommand = new RelayCommand<RecentWorkspaceItem>(item => _ = OpenItemAsync(item), item => item is not null);
        RevealItemFolderCommand = new RelayCommand<RecentWorkspaceItem>(RevealItemFolder, item => item is not null);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => HasSelection);
        RevealSelectedFoldersCommand = new RelayCommand(RevealSelectedFolders, () => HasSelection && CanRevealSelectedFolders);
        SelectVisibleItemsCommand = new RelayCommand(SelectVisibleItems, () => FilteredItems.Count > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => HasSelection);
        ClearFiltersCommand = new RelayCommand(ClearFilters, () => HasActiveFilters);

        UpdateItems(items);
    }

    public ObservableCollection<string> ModeFilters { get; }

    public ObservableCollection<string> TypeFilters { get; }

    public ObservableCollection<string> DateFilters { get; }

    public ObservableCollection<string> SortOptions { get; }

    public ObservableCollection<LauncherRecentItemViewModel> FilteredItems { get; }

    public RelayCommand<RecentWorkspaceItem> OpenItemCommand { get; }

    public RelayCommand<RecentWorkspaceItem> RevealItemFolderCommand { get; }

    public AsyncRelayCommand DeleteSelectedCommand { get; }

    public RelayCommand RevealSelectedFoldersCommand { get; }

    public RelayCommand SelectVisibleItemsCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand ClearFiltersCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedModeFilter
    {
        get => _selectedModeFilter;
        set
        {
            if (SetProperty(ref _selectedModeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetProperty(ref _selectedTypeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedDateFilter
    {
        get => _selectedDateFilter;
        set
        {
            if (SetProperty(ref _selectedDateFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetProperty(ref _selectedSortOption, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool HasResults => FilteredItems.Count > 0;

    public int SelectedItemCount => _allItems.Count(item => item.IsSelected);

    public bool HasSelection => SelectedItemCount > 0;

    public bool CanRevealSelectedFolders => _allItems.Any(item => item.IsSelected && item.CanRevealInFolder);

    public string SelectionSummary => SelectedItemCount == 1
        ? "1 item selected"
        : $"{SelectedItemCount} items selected";

    public string SelectVisibleButtonText => FilteredItems.Count > 0 && FilteredItems.All(item => item.IsSelected)
        ? "Clear Visible"
        : "Select Visible";

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText) ||
        !string.Equals(SelectedTypeFilter, DefaultTypeFilter, StringComparison.Ordinal) ||
        !string.Equals(SelectedModeFilter, DefaultModeFilter, StringComparison.Ordinal) ||
        !string.Equals(SelectedDateFilter, DefaultDateFilter, StringComparison.Ordinal) ||
        !string.Equals(SelectedSortOption, DefaultSortOption, StringComparison.Ordinal);

    public string ResultSummary => FilteredItems.Count == 1
        ? "1 matching snip"
        : $"{FilteredItems.Count} matching snips";

    public string ActiveFilterSummary
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                parts.Add($"Text: \"{SearchText.Trim()}\"");
            }

            if (!string.Equals(SelectedModeFilter, DefaultModeFilter, StringComparison.Ordinal))
            {
                parts.Add(SelectedModeFilter);
            }

            if (!string.Equals(SelectedTypeFilter, DefaultTypeFilter, StringComparison.Ordinal))
            {
                parts.Add(SelectedTypeFilter);
            }

            if (!string.Equals(SelectedDateFilter, DefaultDateFilter, StringComparison.Ordinal))
            {
                parts.Add(SelectedDateFilter);
            }

            if (!string.Equals(SelectedSortOption, DefaultSortOption, StringComparison.Ordinal))
            {
                parts.Add($"Sort: {SelectedSortOption}");
            }

            return parts.Count == 0
                ? "Showing everything in your saved snip workspace."
                : string.Join(" | ", parts);
        }
    }

    public void UpdateItems(IEnumerable<RecentWorkspaceItem> items)
    {
        foreach (var item in _allItems)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _allItems.Clear();
        foreach (var item in items.OrderByDescending(item => item.UpdatedAt))
        {
            var viewModel = new LauncherRecentItemViewModel(item);
            viewModel.PropertyChanged += OnItemPropertyChanged;
            _allItems.Add(viewModel);
        }

        ApplyFilters();
    }

    private async Task OpenItemAsync(RecentWorkspaceItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _openItemAsync(item).ConfigureAwait(true);
    }

    private void RevealItemFolder(RecentWorkspaceItem? item)
    {
        if (item is null)
        {
            return;
        }

        _revealItemFolder(item);
    }

    private void ClearFilters()
    {
        _searchText = string.Empty;
        _selectedTypeFilter = DefaultTypeFilter;
        _selectedModeFilter = DefaultModeFilter;
        _selectedDateFilter = DefaultDateFilter;
        _selectedSortOption = DefaultSortOption;

        RaisePropertyChanged(nameof(SearchText));
        RaisePropertyChanged(nameof(SelectedTypeFilter));
        RaisePropertyChanged(nameof(SelectedModeFilter));
        RaisePropertyChanged(nameof(SelectedDateFilter));
        RaisePropertyChanged(nameof(SelectedSortOption));

        ApplyFilters();
    }

    private async Task DeleteSelectedAsync()
    {
        var selectedItems = GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        var removedCount = await _deleteItemsAsync(selectedItems).ConfigureAwait(true);
        if (removedCount > 0)
        {
            ClearSelection();
        }
    }

    private void RevealSelectedFolders()
    {
        var selectedItems = GetSelectedItems()
            .Where(item => !string.IsNullOrWhiteSpace(item.EditableProjectPath) ||
                           !string.IsNullOrWhiteSpace(item.LastImagePath) ||
                           !string.IsNullOrWhiteSpace(item.CachedProjectPath))
            .ToList();

        if (selectedItems.Count == 0)
        {
            return;
        }

        _revealSelectedItemFolders(selectedItems);
    }

    private void SelectVisibleItems()
    {
        if (FilteredItems.Count == 0)
        {
            return;
        }

        var shouldSelectVisible = FilteredItems.Any(item => !item.IsSelected);
        foreach (var item in FilteredItems)
        {
            item.IsSelected = shouldSelectVisible;
        }

        RefreshSelectionState();
    }

    private void ClearSelection()
    {
        foreach (var item in _allItems.Where(item => item.IsSelected))
        {
            item.IsSelected = false;
        }

        RefreshSelectionState();
    }

    private void ApplyFilters()
    {
        var query = SearchText.Trim();
        var filtered = SortItems(_allItems
            .Where(item => MatchesText(item, query))
            .Where(MatchesType)
            .Where(MatchesMode)
            .Where(MatchesDate))
            .ToList();

        var visibleItems = filtered.ToHashSet();
        foreach (var item in _allItems.Where(item => item.IsSelected && !visibleItems.Contains(item)))
        {
            item.IsSelected = false;
        }

        FilteredItems.Clear();
        foreach (var item in filtered)
        {
            FilteredItems.Add(item);
        }

        RaisePropertyChanged(nameof(HasResults));
        RaisePropertyChanged(nameof(HasActiveFilters));
        RaisePropertyChanged(nameof(ResultSummary));
        RaisePropertyChanged(nameof(ActiveFilterSummary));
        RaisePropertyChanged(nameof(SelectVisibleButtonText));
        SelectVisibleItemsCommand.RaiseCanExecuteChanged();
        RefreshSelectionState();
        ClearFiltersCommand.RaiseCanExecuteChanged();
    }

    private IReadOnlyList<RecentWorkspaceItem> GetSelectedItems()
    {
        return _allItems
            .Where(item => item.IsSelected)
            .Select(item => item.Item)
            .GroupBy(item => $"{item.ProjectId:N}|{item.EditableProjectPath}|{item.CachedProjectPath}|{item.LastImagePath}")
            .Select(group => group.First())
            .ToList();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(LauncherRecentItemViewModel.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        RaisePropertyChanged(nameof(SelectedItemCount));
        RaisePropertyChanged(nameof(HasSelection));
        RaisePropertyChanged(nameof(CanRevealSelectedFolders));
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(SelectVisibleButtonText));
        DeleteSelectedCommand.RaiseCanExecuteChanged();
        RevealSelectedFoldersCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
        SelectVisibleItemsCommand.RaiseCanExecuteChanged();
    }

    private bool MatchesType(LauncherRecentItemViewModel item)
    {
        return SelectedTypeFilter switch
        {
            "Projects" => item.IsEditableProject,
            "Captures" => !item.IsEditableProject,
            _ => true
        };
    }

    private IEnumerable<LauncherRecentItemViewModel> SortItems(IEnumerable<LauncherRecentItemViewModel> items)
    {
        return SelectedSortOption switch
        {
            "Oldest First" => items.OrderBy(item => item.Item.UpdatedAt),
            "Name A-Z" => items.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            "Name Z-A" => items.OrderByDescending(item => item.Title, StringComparer.OrdinalIgnoreCase),
            "Projects First" => items
                .OrderByDescending(item => item.IsEditableProject)
                .ThenByDescending(item => item.Item.UpdatedAt),
            _ => items.OrderByDescending(item => item.Item.UpdatedAt)
        };
    }

    private bool MatchesText(LauncherRecentItemViewModel item, string query)
    {
        return string.IsNullOrWhiteSpace(query) || item.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesMode(LauncherRecentItemViewModel item)
    {
        return SelectedModeFilter switch
        {
            "Region" => item.Item.Mode == CaptureMode.Region,
            "Window" => item.Item.Mode == CaptureMode.Window,
            "Scrolling" => item.Item.Mode == CaptureMode.Scrolling,
            "Full Screen" => item.Item.Mode == CaptureMode.FullScreen,
            _ => true
        };
    }

    private bool MatchesDate(LauncherRecentItemViewModel item)
    {
        var now = DateTimeOffset.Now;
        var updatedAt = item.Item.UpdatedAt;

        return SelectedDateFilter switch
        {
            "Today" => updatedAt.LocalDateTime.Date == now.LocalDateTime.Date,
            "Last 7 Days" => updatedAt >= now.AddDays(-7),
            "Last 30 Days" => updatedAt >= now.AddDays(-30),
            _ => true
        };
    }
}
