using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CaptureCoyote.App.Services;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Mvvm;

namespace CaptureCoyote.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly Func<CaptureMode, Task> _captureAsync;
    private readonly Func<Task> _openProjectAsync;
    private readonly Action _openSettings;
    private readonly Action _openSearchWindow;
    private readonly Func<RecentWorkspaceItem, Task> _openRecentItemAsync;
    private readonly Func<RecoveryDraftInfo, Task> _restoreRecoveryDraftAsync;
    private readonly Func<Task<int>> _cleanTemporarySnipsAsync;
    private readonly Action<string> _copyText;
    private readonly ObservableCollection<LauncherRecentItemViewModel> _savedProjects = [];
    private BitmapSource? _lastPreview;
    private bool _isBusy;
    private string _statusText = "Ready to capture.";
    private CaptureMode _selectedCaptureMode;
    private int _selectedDelaySeconds;
    private string _searchText = string.Empty;
    private string _lastCaptureName = "No recent capture";
    private string _lastCaptureMeta = "Take a capture to preview it here.";
    private string _lastCaptureExtractedText = string.Empty;
    private LauncherRecoveryDraftViewModel? _latestRecoveryDraft;

    public MainViewModel(
        AppSettings settings,
        Func<CaptureMode, Task> captureAsync,
        Func<Task> openProjectAsync,
        Action openSettings,
        Action openSearchWindow,
        Func<RecentWorkspaceItem, Task> openRecentItemAsync,
        Func<RecoveryDraftInfo, Task> restoreRecoveryDraftAsync,
        Func<Task<int>> cleanTemporarySnipsAsync,
        Action<string> copyText)
    {
        Settings = settings;
        _captureAsync = captureAsync;
        _openProjectAsync = openProjectAsync;
        _openSettings = openSettings;
        _openSearchWindow = openSearchWindow;
        _openRecentItemAsync = openRecentItemAsync;
        _restoreRecoveryDraftAsync = restoreRecoveryDraftAsync;
        _cleanTemporarySnipsAsync = cleanTemporarySnipsAsync;
        _copyText = copyText;
        _selectedCaptureMode = settings.LastCaptureMode;
        _selectedDelaySeconds = 0;
        settings.LastDelaySeconds = 0;

        DelayOptions = new ObservableCollection<DelayOption>
        {
            new() { Label = "Off", Seconds = 0 },
            new() { Label = "3s", Seconds = 3 },
            new() { Label = "5s", Seconds = 5 },
            new() { Label = "10s", Seconds = 10 }
        };
        CaptureCommand = new RelayCommand<CaptureMode>(mode => _ = _captureAsync(mode), _ => !IsBusy);
        OpenProjectCommand = new AsyncRelayCommand(_openProjectAsync, () => !IsBusy);
        OpenSettingsCommand = new RelayCommand(_openSettings);
        OpenSearchWindowCommand = new RelayCommand(_openSearchWindow);
        OpenSupportLinkCommand = new RelayCommand(OpenSupportLink, () => HasSupportLink);
        OpenRecentItemCommand = new RelayCommand<RecentWorkspaceItem>(item => _ = OpenRecentItemAsync(item), item => !IsBusy && item is not null);
        RestoreRecoveryDraftCommand = new RelayCommand<RecoveryDraftInfo>(draft => _ = RestoreRecoveryDraftAsync(draft), draft => !IsBusy && draft is not null);
        CopyDetectedTextCommand = new RelayCommand(CopyDetectedText, () => HasLastCaptureExtractedText);
        CleanTemporarySnipsCommand = new AsyncRelayCommand(CleanTemporarySnipsAsync, () => !IsBusy && HasOldTemporarySnips);

        RecentItems = new ObservableCollection<LauncherRecentItemViewModel>();
        FilteredRecentItems = new ObservableCollection<LauncherRecentItemViewModel>();
        RefreshWorkspaceState();
    }

    public AppSettings Settings { get; }

    public ObservableCollection<DelayOption> DelayOptions { get; }

    public RelayCommand<CaptureMode> CaptureCommand { get; }

    public AsyncRelayCommand OpenProjectCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand OpenSearchWindowCommand { get; }

    public RelayCommand OpenSupportLinkCommand { get; }

    public RelayCommand<RecentWorkspaceItem> OpenRecentItemCommand { get; }

    public RelayCommand<RecoveryDraftInfo> RestoreRecoveryDraftCommand { get; }

    public RelayCommand CopyDetectedTextCommand { get; }

    public AsyncRelayCommand CleanTemporarySnipsCommand { get; }

    public ObservableCollection<LauncherRecentItemViewModel> RecentItems { get; }

    public ObservableCollection<LauncherRecentItemViewModel> FilteredRecentItems { get; }

    public LauncherRecoveryDraftViewModel? LatestRecoveryDraft
    {
        get => _latestRecoveryDraft;
        private set
        {
            if (SetProperty(ref _latestRecoveryDraft, value))
            {
                RaisePropertyChanged(nameof(HasRecoveryDraft));
            }
        }
    }

    public bool HasRecoveryDraft => LatestRecoveryDraft is not null;

    public int OldTemporarySnipCount => GetOldTemporarySnipCount();

    public bool HasOldTemporarySnips => OldTemporarySnipCount > 0;

    public string OldTemporarySnipHintText => OldTemporarySnipCount == 1
        ? "You have 1 old temporary snip. Clean now?"
        : $"You have {OldTemporarySnipCount} old temporary snips. Clean now?";

    public string OldTemporarySnipDetailText => Settings.TemporarySnipRetentionDays <= 0
        ? "Saved editable projects and exported files stay untouched."
        : $"This only affects unsaved temporary history older than {Settings.TemporarySnipRetentionDays} days. Saved editable projects and exported files stay untouched.";

    public CaptureMode SelectedCaptureMode
    {
        get => _selectedCaptureMode;
        set
        {
            if (SetProperty(ref _selectedCaptureMode, value))
            {
                Settings.LastCaptureMode = value;
            }
        }
    }

    public int SelectedDelaySeconds
    {
        get => _selectedDelaySeconds;
        set => SetProperty(ref _selectedDelaySeconds, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyRecentFilter();
            }
        }
    }

    public BitmapSource? LastPreview
    {
        get => _lastPreview;
        private set => SetProperty(ref _lastPreview, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CaptureCommand.RaiseCanExecuteChanged();
                OpenProjectCommand.RaiseCanExecuteChanged();
                OpenRecentItemCommand.RaiseCanExecuteChanged();
                RestoreRecoveryDraftCommand.RaiseCanExecuteChanged();
                CleanTemporarySnipsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string LastCaptureName
    {
        get => _lastCaptureName;
        private set => SetProperty(ref _lastCaptureName, value);
    }

    public string LastCaptureMeta
    {
        get => _lastCaptureMeta;
        private set => SetProperty(ref _lastCaptureMeta, value);
    }

    public string LastCaptureExtractedText
    {
        get => _lastCaptureExtractedText;
        private set
        {
            if (SetProperty(ref _lastCaptureExtractedText, value))
            {
                RaisePropertyChanged(nameof(HasLastCaptureExtractedText));
                RaisePropertyChanged(nameof(LastCaptureExtractedTextPreview));
                CopyDetectedTextCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasLastCaptureExtractedText => !string.IsNullOrWhiteSpace(LastCaptureExtractedText);

    public string LastCaptureExtractedTextPreview => string.IsNullOrWhiteSpace(LastCaptureExtractedText)
        ? "OCR text will appear here after a capture with readable text."
        : LastCaptureExtractedText.ReplaceLineEndings(" ").Trim();

    public bool HasWorkspaceResults => FilteredRecentItems.Count > 0;

    public bool ShowNoSearchResults => !HasWorkspaceResults && !string.IsNullOrWhiteSpace(SearchText);

    public bool HasSupportLink => AppLinks.HasSupportUrl;

    public int WorkspaceItemCount => GetCombinedWorkspaceItems().Count;

    public int WorkspaceProjectCount => GetCombinedWorkspaceItems().Count(item => item.IsEditableProject);

    public int WorkspaceCaptureCount => GetCombinedWorkspaceItems().Count(item => !item.IsEditableProject);

    public string WorkspaceSummaryText => WorkspaceItemCount == 0
        ? "No saved captures yet. Take a snip and they will show up here."
        : $"{WorkspaceItemCount} saved items ready to search: {WorkspaceProjectCount} editable projects and {WorkspaceCaptureCount} captures.";

    public void UpdatePreview(ScreenshotProject project)
    {
        using var stream = new MemoryStream(project.OriginalImagePngBytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        LastPreview = frame;
        LastCaptureName = project.Name;
        LastCaptureMeta = BuildCaptureMeta(project);
        LastCaptureExtractedText = string.IsNullOrWhiteSpace(project.ExtractedText)
            ? string.Empty
            : project.ExtractedText.Trim();
    }

    public void RefreshWorkspaceState()
    {
        RecentItems.Clear();
        foreach (var item in Settings.RecentItems.OrderByDescending(item => item.UpdatedAt))
        {
            RecentItems.Add(new LauncherRecentItemViewModel(item));
        }

        ApplyRecentFilter();
        TryHydrateLastCaptureFromWorkspace();
        RaisePropertyChanged(nameof(OldTemporarySnipCount));
        RaisePropertyChanged(nameof(HasOldTemporarySnips));
        RaisePropertyChanged(nameof(OldTemporarySnipHintText));
        RaisePropertyChanged(nameof(OldTemporarySnipDetailText));
        CleanTemporarySnipsCommand.RaiseCanExecuteChanged();

        LatestRecoveryDraft = Settings.RecoveryDrafts
            .OrderByDescending(draft => draft.ModifiedAt)
            .Select(draft => new LauncherRecoveryDraftViewModel(draft))
            .FirstOrDefault();
    }

    public void UpdateSavedProjects(IEnumerable<RecentWorkspaceItem> items)
    {
        _savedProjects.Clear();
        foreach (var item in items.OrderByDescending(item => item.UpdatedAt))
        {
            _savedProjects.Add(new LauncherRecentItemViewModel(item));
        }

        ApplyRecentFilter();
        TryHydrateLastCaptureFromWorkspace();
        RaisePropertyChanged(nameof(WorkspaceItemCount));
        RaisePropertyChanged(nameof(WorkspaceProjectCount));
        RaisePropertyChanged(nameof(WorkspaceCaptureCount));
        RaisePropertyChanged(nameof(WorkspaceSummaryText));
    }

    public IReadOnlyList<RecentWorkspaceItem> GetWorkspaceItemsSnapshot()
    {
        return GetCombinedWorkspaceItems()
            .Select(item => item.Item)
            .ToList();
    }

    private async Task OpenRecentItemAsync(RecentWorkspaceItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _openRecentItemAsync(item).ConfigureAwait(true);
    }

    private async Task RestoreRecoveryDraftAsync(RecoveryDraftInfo? draft)
    {
        if (draft is null)
        {
            return;
        }

        await _restoreRecoveryDraftAsync(draft).ConfigureAwait(true);
    }

    private void CopyDetectedText()
    {
        if (HasLastCaptureExtractedText)
        {
            _copyText(LastCaptureExtractedText);
            StatusText = "Copied detected text to the clipboard.";
        }
    }

    private void OpenSupportLink()
    {
        if (!ExternalLinkService.TryOpen(AppLinks.SupportUrl))
        {
            StatusText = "No support link is configured yet.";
            return;
        }

        StatusText = "Opened the CaptureCoyote support page.";
    }

    private async Task CleanTemporarySnipsAsync()
    {
        var removedCount = await _cleanTemporarySnipsAsync().ConfigureAwait(true);
        RefreshWorkspaceState();
        StatusText = removedCount == 0
            ? "No old temporary snips were ready to clean."
            : removedCount == 1
                ? "Removed 1 old temporary snip from launcher history."
                : $"Removed {removedCount} old temporary snips from launcher history.";
    }

    private void ApplyRecentFilter()
    {
        var query = SearchText.Trim();
        var workspaceItems = GetCombinedWorkspaceItems();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? workspaceItems
            : workspaceItems
                .Where(item => item.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        FilteredRecentItems.Clear();
        foreach (var item in filtered)
        {
            FilteredRecentItems.Add(item);
        }

        RaisePropertyChanged(nameof(HasWorkspaceResults));
        RaisePropertyChanged(nameof(ShowNoSearchResults));
        RaisePropertyChanged(nameof(WorkspaceItemCount));
        RaisePropertyChanged(nameof(WorkspaceProjectCount));
        RaisePropertyChanged(nameof(WorkspaceCaptureCount));
        RaisePropertyChanged(nameof(WorkspaceSummaryText));
    }

    private List<LauncherRecentItemViewModel> GetCombinedWorkspaceItems()
    {
        var recentProjectPaths = RecentItems
            .Select(item => item.Item.EditableProjectPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RecentItems
            .Concat(_savedProjects.Where(item =>
                string.IsNullOrWhiteSpace(item.Item.EditableProjectPath) ||
                !recentProjectPaths.Contains(item.Item.EditableProjectPath!)))
            .OrderByDescending(item => item.Item.UpdatedAt)
            .ToList();
    }

    private void TryHydrateLastCaptureFromWorkspace()
    {
        if (LastPreview is not null)
        {
            return;
        }

        var first = RecentItems.FirstOrDefault() ?? _savedProjects.FirstOrDefault();
        if (first is null)
        {
            return;
        }

        LastPreview = first.Thumbnail;
        LastCaptureName = first.Title;
        LastCaptureMeta = $"{first.Subtitle} - {first.TimestampLabel}";
        LastCaptureExtractedText = first.Item.ExtractedText ?? string.Empty;
    }

    private static string BuildCaptureMeta(ScreenshotProject project)
    {
        var source = string.IsNullOrWhiteSpace(project.CaptureContext.SourceWindowTitle)
            ? CaptureModeDisplay.ToDisplayText(project.CaptureContext.Mode)
            : project.CaptureContext.SourceWindowTitle;

        return $"{source} - {project.CaptureContext.CapturedAt.LocalDateTime:dd MMM yyyy, HH:mm}";
    }

    private int GetOldTemporarySnipCount()
    {
        if (Settings.TemporarySnipRetentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.Now.AddDays(-Settings.TemporarySnipRetentionDays);
        return Settings.RecentItems.Count(item =>
            string.IsNullOrWhiteSpace(item.EditableProjectPath) &&
            string.IsNullOrWhiteSpace(item.LastImagePath) &&
            item.UpdatedAt < cutoff);
    }
}
