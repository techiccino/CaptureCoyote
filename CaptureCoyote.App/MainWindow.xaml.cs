using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using CaptureCoyote.App.Interop;
using CaptureCoyote.App.Services;
using CaptureCoyote.App.ViewModels;
using CaptureCoyote.App.Views;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Editor.ViewModels;
using CaptureCoyote.Editor.Views;
using CaptureCoyote.Infrastructure.Branding;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.App;

public partial class MainWindow : Window
{
    private readonly CaptureCoyoteContext _context;
    private readonly MainViewModel _viewModel;
    private readonly TrayIconService _trayIconService;
    private CancellationTokenSource? _libraryRefreshCts;
    private WorkspaceSearchWindow? _workspaceSearchWindow;
    private bool _isExitRequested;
    private bool _trayHintShown;

    public MainWindow(CaptureCoyoteContext context)
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
        _context = context;
        _context.RecentWorkspaceService.PruneUnavailableEntries(_context.Settings);
        _context.RecoveryDraftService.PruneUnavailableDrafts(_context.Settings);
        _viewModel = new MainViewModel(
            context.Settings,
            CaptureAsync,
            OpenProjectAsync,
            OpenSettingsWindow,
            OpenWorkspaceSearchWindow,
            OpenRecentItemAsync,
            RestoreRecoveryDraftAsync,
            CleanTemporarySnipsAsync,
            text => _context.ClipboardService.SetText(text));
        DataContext = _viewModel;
        _viewModel.RefreshWorkspaceState();
        _trayIconService = new TrayIconService(
            () => Dispatcher.InvokeAsync(ShowFromTray).Task,
            mode => Dispatcher.InvokeAsync(() => CaptureAsync(mode)).Task.Unwrap(),
            () => Dispatcher.InvokeAsync(OpenProjectFromTrayAsync).Task.Unwrap(),
            () => Dispatcher.InvokeAsync(OpenSettingsFromTrayAsync).Task.Unwrap(),
            ExitApplication);

        SourceInitialized += MainWindowOnSourceInitialized;
        Loaded += MainWindowOnLoaded;
        StateChanged += MainWindowOnStateChanged;
        Closing += MainWindowOnClosing;
    }

    private async void MainWindowOnLoaded(object? sender, RoutedEventArgs e)
    {
        await RefreshProjectLibraryAsync().ConfigureAwait(true);
    }

    private void MainWindowOnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _context.HotkeyService.Attach(handle);
        _context.HotkeyService.RegisterBindings(_context.Settings.Hotkeys);
        _context.HotkeyService.HotkeyPressed += HotkeyServiceOnHotkeyPressed;
    }

    private async void HotkeyServiceOnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        await Dispatcher.InvokeAsync(() => CaptureAsync(e.Binding.Mode));
    }

    private async Task CaptureAsync(CaptureMode mode)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.IsBusy = true;
        _viewModel.SelectedCaptureMode = mode;
        _viewModel.StatusText = $"Preparing {CaptureModeDisplay.ToDisplayText(mode)} capture...";
        var captureUiState = CaptureUiState.Default;
        Window? preferredForegroundWindow = null;

        try
        {
            captureUiState = await HideForCaptureAsync().ConfigureAwait(true);

            if (_viewModel.SelectedDelaySeconds > 0)
            {
                _viewModel.StatusText = $"Waiting {_viewModel.SelectedDelaySeconds}s before capture...";
                var countdown = new DelayCountdownCoordinator();
                await countdown.ShowAsync(
                    _viewModel.SelectedDelaySeconds,
                    $"{CaptureModeDisplay.ToDisplayText(mode)} capture",
                    mode == CaptureMode.Scrolling
                        ? "When the countdown ends, CaptureCoyote will auto-scroll the selected window."
                        : "Move into position before capture starts.")
                    .ConfigureAwait(true);
                await Task.Delay(120).ConfigureAwait(true);
            }

            var captureContext = new CaptureContext
            {
                Mode = mode,
                DelaySeconds = _viewModel.SelectedDelaySeconds,
                CapturedAt = DateTimeOffset.Now
            };

            CaptureResult? captureResult = null;

            if (mode == CaptureMode.FullScreen)
            {
                captureResult = _context.ScreenCaptureService.CaptureFullScreen(captureContext);
            }
            else
            {
                var snapshot = _context.ScreenCaptureService.CaptureDesktopSnapshot();
                var overlay = new CaptureOverlayCoordinator(_context.WindowLocatorService);
                var overlayMode = mode == CaptureMode.Region ? CaptureMode.Region : CaptureMode.Window;
                var selection = await overlay.ShowAsync(snapshot, overlayMode).ConfigureAwait(true);
                if (selection is null)
                {
                    _viewModel.StatusText = "Capture cancelled.";
                    return;
                }

                WindowDescriptor? window = null;
                if (mode is CaptureMode.Window or CaptureMode.Scrolling)
                {
                    var center = new CaptureCoyote.Core.Primitives.PixelPoint(selection.Value.X + (selection.Value.Width / 2), selection.Value.Y + (selection.Value.Height / 2));
                    window = overlay.SelectedWindow ?? _context.WindowLocatorService.GetWindowAt(center);
                    if (window is not null)
                    {
                        captureContext.SourceWindowHandle = window.Handle.ToInt64();
                        captureContext.SourceWindowTitle = window.Title;
                        captureContext.SourceWindowClass = window.ClassName;
                    }
                }

                if (mode == CaptureMode.Scrolling)
                {
                    if (window is null)
                    {
                        _viewModel.StatusText = "Scrolling capture needs a valid window target.";
                        return;
                    }

                    _viewModel.StatusText = "Auto-scrolling the selected window...";
                    captureResult = await _context.ScrollingCaptureService
                        .CaptureScrollingWindowAsync(window, captureContext)
                        .ConfigureAwait(true);
                }
                else if (mode == CaptureMode.Window && window is not null)
                {
                    captureResult = _context.ScreenCaptureService.CaptureWindow(window, snapshot, captureContext);
                }
                else
                {
                    captureResult = _context.ScreenCaptureService.CreateCaptureFromSnapshot(snapshot, selection.Value, captureContext);
                }
            }

            if (captureResult is null)
            {
                _viewModel.StatusText = "No capture was created.";
                return;
            }

            var project = CreateProject(captureResult);
            await _context.RecentWorkspaceService.TrackCaptureAsync(_context.Settings, project).ConfigureAwait(true);
            _viewModel.UpdatePreview(project);
            _viewModel.RefreshWorkspaceState();

            if (_context.Settings.AutoCopyToClipboard)
            {
                _context.ClipboardService.SetImage(_context.AnnotationRenderService.RenderToPng(project));
            }

            EditorViewModel? editorViewModel = null;
            CaptureReviewViewModel? reviewViewModel = null;
            if (_context.Settings.AutoOpenEditor)
            {
                var editorResult = OpenEditor(project);
                editorViewModel = editorResult.ViewModel;
                preferredForegroundWindow = editorResult.Window;
            }
            else
            {
                var reviewResult = ShowReview(project);
                reviewViewModel = reviewResult.ViewModel;
                preferredForegroundWindow = reviewResult.Window;
            }

            _ = PopulateOcrAsync(project, editorViewModel, reviewViewModel);

            _viewModel.StatusText = mode == CaptureMode.Scrolling && captureResult.Context.ScrollFrameCount is > 1
                ? $"Scrolling capture completed using {captureResult.Context.ScrollFrameCount} frames."
                : "Capture completed.";
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException($"Capture failed for mode {mode}.", ex);
            _viewModel.StatusText = $"Capture failed: {ex.Message}";
        }
        finally
        {
            RestoreAfterCapture(captureUiState, preferredForegroundWindow);

            _viewModel.IsBusy = false;
            await _context.SaveSettingsAsync().ConfigureAwait(true);
        }
    }

    private ScreenshotProject CreateProject(CaptureResult captureResult)
    {
        var project = new ScreenshotProject
        {
            Name = $"Capture {captureResult.Context.CapturedAt:yyyy-MM-dd HH-mm-ss}",
            CreatedAt = captureResult.Context.CapturedAt,
            ModifiedAt = captureResult.Context.CapturedAt,
            CaptureContext = captureResult.Context,
            OriginalImagePngBytes = captureResult.ImagePngBytes.ToArray(),
            OriginalPixelWidth = captureResult.PixelWidth,
            OriginalPixelHeight = captureResult.PixelHeight,
            CropState = new CropState
            {
                IsActive = false,
                Bounds = CaptureCoyote.Core.Primitives.PixelRect.Empty
            }
        };

        project.ExtractedText = string.Empty;
        project.AnnotationText = AnnotationSearchTextBuilder.Build(project.Annotations);

        return project;
    }

    private (EditorViewModel ViewModel, EditorWindow Window) OpenEditor(ScreenshotProject project, bool startDirty = false)
    {
        var editorViewModel = new EditorViewModel(
            project,
            _context.Settings,
            _context.AnnotationRenderService,
            _context.ClipboardService,
            _context.FileDialogService,
            _context.FileExportService,
            _context.ProjectSerializationService,
            _context.RecentWorkspaceService,
            _context.RecoveryDraftService,
            () => _context.SaveSettingsAsync(),
            startDirty,
            RefreshWorkspaceAfterEditorSaveAsync);

        var editorWindow = new EditorWindow(editorViewModel)
        {
            Owner = CanOwnChildWindows() ? this : null
        };

        editorWindow.Show();
        return (editorViewModel, editorWindow);
    }

    private (CaptureReviewViewModel ViewModel, CaptureReviewWindow Window) ShowReview(ScreenshotProject project)
    {
        CaptureReviewWindow? reviewWindow = null;
        var reviewViewModel = new CaptureReviewViewModel(
            project,
            _context.Settings,
            _context.AnnotationRenderService,
            _context.ClipboardService,
            _context.FileExportService,
            _context.FileDialogService,
            _context.ProjectSerializationService,
            currentProject =>
            {
                reviewWindow?.Close();
                OpenEditor(currentProject);
            },
            TrackProjectAsync,
            TrackImageExportAsync,
            text => _context.ClipboardService.SetText(text));

        reviewWindow = new CaptureReviewWindow(reviewViewModel)
        {
            Owner = CanOwnChildWindows() ? this : null
        };

        reviewWindow.Show();
        return (reviewViewModel, reviewWindow);
    }

    private async Task PopulateOcrAsync(
        ScreenshotProject project,
        EditorViewModel? editorViewModel = null,
        CaptureReviewViewModel? reviewViewModel = null)
    {
        try
        {
            var extractedText = await _context.OcrService
                .ExtractTextAsync(project.OriginalImagePngBytes, _context.Settings.PreferredOcrLanguageTag)
                .ConfigureAwait(true);

            project.ExtractedText = extractedText;
            editorViewModel?.ApplyExtractedText(extractedText);
            reviewViewModel?.ApplyExtractedText(extractedText);

            await _context.RecentWorkspaceService.TrackCaptureAsync(_context.Settings, project).ConfigureAwait(true);
            _viewModel.UpdatePreview(project);
            _viewModel.RefreshWorkspaceState();
            await RefreshProjectLibraryAsync().ConfigureAwait(true);
            await _context.SaveSettingsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException("OCR extraction failed for new capture.", ex);
            editorViewModel?.ApplyExtractedText(string.Empty);
            reviewViewModel?.ApplyExtractedText(string.Empty);
        }
    }

    private async Task OpenProjectAsync()
    {
        var path = _context.FileDialogService.ShowOpenProjectDialog();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await OpenProjectPathAsync(path).ConfigureAwait(true);
    }

    public async Task<bool> OpenProjectPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            _viewModel.StatusText = $"Project file was not found: {path}";
            return false;
        }

        try
        {
            var project = await LoadProjectAsync(path, useContainerPathAsEditableProjectPath: true).ConfigureAwait(true);
            _viewModel.UpdatePreview(project);
            await _context.RecentWorkspaceService.TrackProjectAsync(_context.Settings, project, path).ConfigureAwait(true);
            _viewModel.RefreshWorkspaceState();
            await RefreshProjectLibraryAsync().ConfigureAwait(true);
            await _context.SaveSettingsAsync().ConfigureAwait(true);
            _ = OpenEditor(project);
            _viewModel.StatusText = $"Opened editable project: {path}";
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException($"Could not open editable project '{path}'.", ex);
            _viewModel.StatusText = $"Could not open project: {ex.Message}";
            ShowOperationError("Could not open project", ex.Message);
            return false;
        }
    }

    private void OpenSettingsWindow()
    {
        var viewModel = new SettingsViewModel(_context.Settings);
        var window = new SettingsWindow(viewModel)
        {
            Owner = CanOwnChildWindows() ? this : null
        };

        if (window.ShowDialog() == true)
        {
            ApplyWindowsStartupPreference();
            _context.HotkeyService.RegisterBindings(_context.Settings.Hotkeys);
            _ = _context.SaveSettingsAsync();
            _ = RefreshProjectLibraryAsync();
            _viewModel.RefreshWorkspaceState();
            _viewModel.StatusText = "Settings saved.";
        }
    }

    private void OpenWorkspaceSearchWindow()
    {
        if (_workspaceSearchWindow is null || !_workspaceSearchWindow.IsLoaded)
        {
            var viewModel = new WorkspaceSearchViewModel(
                _viewModel.GetWorkspaceItemsSnapshot(),
                OpenRecentItemAsync,
                RevealRecentItemFolder,
                DeleteWorkspaceItemsAsync,
                RevealSelectedItemFolders);
            _workspaceSearchWindow = new WorkspaceSearchWindow(viewModel)
            {
                Owner = CanOwnChildWindows() ? this : null
            };
            _workspaceSearchWindow.Closed += (_, _) => _workspaceSearchWindow = null;
            _workspaceSearchWindow.Show();
            return;
        }

        if (_workspaceSearchWindow.DataContext is WorkspaceSearchViewModel searchViewModel)
        {
            searchViewModel.UpdateItems(_viewModel.GetWorkspaceItemsSnapshot());
        }

        if (_workspaceSearchWindow.WindowState == WindowState.Minimized)
        {
            _workspaceSearchWindow.WindowState = WindowState.Normal;
        }

        _workspaceSearchWindow.Activate();
        _workspaceSearchWindow.Focus();
    }

    private async Task OpenProjectFromTrayAsync()
    {
        ShowFromTray();
        await OpenProjectAsync().ConfigureAwait(true);
    }

    private async Task OpenRecentItemAsync(RecentWorkspaceItem item)
    {
        var path = _context.RecentWorkspaceService.ResolveOpenPath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            _context.RecentWorkspaceService.PruneUnavailableEntries(_context.Settings);
            _viewModel.RefreshWorkspaceState();
            _viewModel.StatusText = "That recent item is no longer available.";
            await _context.SaveSettingsAsync().ConfigureAwait(true);
            return;
        }

        try
        {
            var useContainerPathAsEditableProjectPath = !string.IsNullOrWhiteSpace(item.EditableProjectPath) &&
                                                        string.Equals(path, item.EditableProjectPath, StringComparison.OrdinalIgnoreCase);
            var project = await LoadProjectAsync(path, useContainerPathAsEditableProjectPath).ConfigureAwait(true);
            _viewModel.UpdatePreview(project);

            if (useContainerPathAsEditableProjectPath)
            {
                await _context.RecentWorkspaceService.TrackProjectAsync(_context.Settings, project, path, item.LastImagePath).ConfigureAwait(true);
            }
            else
            {
                await _context.RecentWorkspaceService.TrackCaptureAsync(_context.Settings, project).ConfigureAwait(true);
            }

            _viewModel.RefreshWorkspaceState();
            await RefreshProjectLibraryAsync().ConfigureAwait(true);
            await _context.SaveSettingsAsync().ConfigureAwait(true);
            _ = OpenEditor(project);
            _viewModel.StatusText = $"Opened recent item: {project.Name}";
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException($"Could not open recent item '{item.Name}'.", ex);
            _viewModel.StatusText = $"Could not open recent item: {ex.Message}";
            ShowOperationError("Could not open recent item", ex.Message);
        }
    }

    private void RevealRecentItemFolder(RecentWorkspaceItem item)
    {
        var path = ResolveRevealPath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            _viewModel.StatusText = "No local file is available yet for that search item.";
            return;
        }

        _context.FileExportService.OpenContainingFolder(path);
        _viewModel.StatusText = $"Opened the containing folder for {item.Name}.";
    }

    private void RevealSelectedItemFolders(IReadOnlyList<RecentWorkspaceItem> items)
    {
        var paths = items
            .Select(ResolveRevealPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            _viewModel.StatusText = "No local files were available for the selected snips.";
            return;
        }

        foreach (var path in paths)
        {
            _context.FileExportService.OpenContainingFolder(path);
        }

        _viewModel.StatusText = paths.Count == 1
            ? "Opened the containing folder for the selected snip."
            : $"Opened {paths.Count} containing folders for the selected snips.";
    }

    private async Task RestoreRecoveryDraftAsync(RecoveryDraftInfo draft)
    {
        var path = _context.RecoveryDraftService.ResolveOpenPath(draft);
        if (string.IsNullOrWhiteSpace(path))
        {
            _context.RecoveryDraftService.PruneUnavailableDrafts(_context.Settings);
            _viewModel.RefreshWorkspaceState();
            _viewModel.StatusText = "That recovery draft is no longer available.";
            await _context.SaveSettingsAsync().ConfigureAwait(true);
            return;
        }

        try
        {
            var project = await LoadProjectAsync(path, useContainerPathAsEditableProjectPath: false).ConfigureAwait(true);
            _viewModel.UpdatePreview(project);
            _ = OpenEditor(project, startDirty: true);
            _viewModel.StatusText = $"Restored unsaved draft: {draft.Name}";
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException($"Could not restore recovery draft '{draft.Name}'.", ex);
            _viewModel.StatusText = $"Could not restore draft: {ex.Message}";
            ShowOperationError("Could not restore draft", ex.Message);
        }
    }

    private async Task<int> CleanTemporarySnipsAsync()
    {
        var removedCount = _context.RecentWorkspaceService.CleanupOldTemporaryItems(
            _context.Settings,
            _context.Settings.TemporarySnipRetentionDays);

        if (removedCount > 0)
        {
            await _context.SaveSettingsAsync().ConfigureAwait(true);
            await RefreshProjectLibraryAsync().ConfigureAwait(true);
        }

        return removedCount;
    }

    private async Task<int> DeleteWorkspaceItemsAsync(IReadOnlyList<RecentWorkspaceItem> items)
    {
        var selectedItems = items
            .GroupBy(item => $"{item.ProjectId:N}|{item.EditableProjectPath}|{item.CachedProjectPath}|{item.LastImagePath}")
            .Select(group => group.First())
            .ToList();

        if (selectedItems.Count == 0)
        {
            return 0;
        }

        var projectCount = selectedItems.Count(item => !string.IsNullOrWhiteSpace(item.EditableProjectPath));
        var captureCount = selectedItems.Count - projectCount;
        var confirmationMessage = BuildDeleteSelectionMessage(selectedItems.Count, projectCount, captureCount);

        var confirmation = System.Windows.MessageBox.Show(
            confirmationMessage,
            "Delete Selected Snips",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return 0;
        }

        var removedCount = 0;
        var failedItems = new List<string>();

        foreach (var item in selectedItems)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(item.EditableProjectPath) && File.Exists(item.EditableProjectPath))
                {
                    DeleteFileToRecycleBin(item.EditableProjectPath);
                }

                RemoveRecentItem(item);
                DeleteManagedSupportFile(item.CachedProjectPath);
                DeleteManagedSupportFile(item.ThumbnailPath);
                removedCount++;
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogException($"Could not delete workspace item '{item.Name}'.", ex);
                failedItems.Add(item.Name);
            }
        }

        _context.RecentWorkspaceService.PruneUnavailableEntries(_context.Settings);
        _viewModel.RefreshWorkspaceState();
        await RefreshProjectLibraryAsync().ConfigureAwait(true);
        await _context.SaveSettingsAsync().ConfigureAwait(true);

        if (failedItems.Count > 0)
        {
            ShowOperationError(
                "Some snips could not be deleted",
                $"CaptureCoyote removed {removedCount} items, but could not delete: {string.Join(", ", failedItems)}");
        }

        _viewModel.StatusText = removedCount switch
        {
            0 => "No selected snips were deleted.",
            1 => "Deleted 1 selected snip.",
            _ => $"Deleted {removedCount} selected snips."
        };

        return removedCount;
    }

    private Task OpenSettingsFromTrayAsync()
    {
        ShowFromTray();
        OpenSettingsWindow();
        return Task.CompletedTask;
    }

    private async Task<ScreenshotProject> LoadProjectAsync(string path, bool useContainerPathAsEditableProjectPath)
    {
        var project = await _context.ProjectSerializationService
            .LoadAsync(path, useContainerPathAsEditableProjectPath)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(project.ExtractedText))
        {
            try
            {
                project.ExtractedText = await _context.OcrService
                    .ExtractTextAsync(project.OriginalImagePngBytes, _context.Settings.PreferredOcrLanguageTag)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogException($"OCR extraction failed while loading project '{path}'.", ex);
                project.ExtractedText = string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(project.AnnotationText))
        {
            project.AnnotationText = AnnotationSearchTextBuilder.Build(project.Annotations);
        }

        return project;
    }

    private async Task TrackProjectAsync(ScreenshotProject project, string path)
    {
        await _context.RecentWorkspaceService.TrackProjectAsync(_context.Settings, project, path).ConfigureAwait(true);
        _viewModel.RefreshWorkspaceState();
        await RefreshProjectLibraryAsync().ConfigureAwait(true);
        await _context.SaveSettingsAsync().ConfigureAwait(true);
        _viewModel.UpdatePreview(project);
    }

    private async Task TrackImageExportAsync(ScreenshotProject project, string path)
    {
        await _context.RecentWorkspaceService.TrackImageExportAsync(_context.Settings, project, path).ConfigureAwait(true);
        _viewModel.RefreshWorkspaceState();
        await _context.SaveSettingsAsync().ConfigureAwait(true);
    }

    private async Task RefreshWorkspaceAfterEditorSaveAsync()
    {
        _viewModel.RefreshWorkspaceState();
        await RefreshProjectLibraryAsync().ConfigureAwait(true);
    }

    private static string? ResolveRevealPath(RecentWorkspaceItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.EditableProjectPath) && File.Exists(item.EditableProjectPath))
        {
            return item.EditableProjectPath;
        }

        if (!string.IsNullOrWhiteSpace(item.LastImagePath) && File.Exists(item.LastImagePath))
        {
            return item.LastImagePath;
        }

        if (!string.IsNullOrWhiteSpace(item.CachedProjectPath) && File.Exists(item.CachedProjectPath))
        {
            return item.CachedProjectPath;
        }

        return null;
    }

    private void RemoveRecentItem(RecentWorkspaceItem item)
    {
        _context.Settings.RecentItems = _context.Settings.RecentItems
            .Where(existing => existing.ProjectId != item.ProjectId)
            .ToList();
    }

    private async Task RefreshProjectLibraryAsync()
    {
        _libraryRefreshCts?.Cancel();
        _libraryRefreshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _libraryRefreshCts = cts;

        try
        {
            var items = await _context.ProjectLibraryService.LoadAsync(_context.Settings, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _viewModel.UpdateSavedProjects(items);
            if (_workspaceSearchWindow?.DataContext is WorkspaceSearchViewModel searchViewModel)
            {
                searchViewModel.UpdateItems(_viewModel.GetWorkspaceItemsSnapshot());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException("Saved-project library refresh failed.", ex);
            _viewModel.StatusText = $"Could not refresh saved projects: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_libraryRefreshCts, cts))
            {
                _libraryRefreshCts = null;
            }

            cts.Dispose();
        }
    }

    private void MainWindowOnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray(showHint: true);
        }
    }

    private void HideToTray(bool showHint)
    {
        ShowInTaskbar = false;
        Hide();

        if (showHint && !_trayHintShown)
        {
            _trayIconService.ShowInfo("CaptureCoyote", "CaptureCoyote is still running in the system tray.");
            _trayHintShown = true;
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Focus();
        _ = RefreshProjectLibraryAsync();
    }

    public void ShowAndActivateMainWindow()
    {
        ShowFromTray();
    }

    public void StartHiddenInTray()
    {
        var previousOpacity = Opacity;
        Opacity = 0;
        Show();
        HideToTray(showHint: false);
        Opacity = previousOpacity;
    }

    private async Task<CaptureUiState> HideForCaptureAsync()
    {
        var windowsToRestore = System.Windows.Application.Current.Windows
            .OfType<Window>()
            .Where(window => window.IsVisible && window is not CaptureOverlayWindow && window is not DelayCountdownWindow)
            .Select(window => new CaptureWindowState(
                window,
                window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState,
                window.Opacity,
                window.ShowInTaskbar,
                GetOwnerDepth(window)))
            .OrderBy(state => state.OwnerDepth)
            .ToList();

        var activeWindow = windowsToRestore
            .Select(state => state.Window)
            .FirstOrDefault(window => window.IsActive);

        foreach (var state in windowsToRestore)
        {
            state.Window.Opacity = 0;
            state.Window.UpdateLayout();
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        NativeMethods.FlushComposition();
        await Task.Delay(50).ConfigureAwait(true);

        foreach (var state in windowsToRestore.OrderByDescending(state => state.OwnerDepth))
        {
            state.Window.Hide();
            var handle = new WindowInteropHelper(state.Window).Handle;
            if (handle != nint.Zero)
            {
                _ = NativeMethods.ShowWindow(handle, NativeMethods.SW_HIDE);
            }
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        NativeMethods.FlushComposition();
        await Task.Delay(140).ConfigureAwait(true);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        NativeMethods.FlushComposition();
        await Task.Delay(40).ConfigureAwait(true);

        return new CaptureUiState(windowsToRestore, activeWindow);
    }

    private void RestoreAfterCapture(CaptureUiState state, Window? preferredForegroundWindow = null)
    {
        foreach (var windowState in state.WindowsToRestore)
        {
            var window = windowState.Window;
            window.ShowInTaskbar = windowState.ShowInTaskbar;
            window.Opacity = windowState.Opacity;
            if (!window.IsVisible)
            {
                window.Show();
            }

            window.WindowState = windowState.WindowState;
        }

        if (preferredForegroundWindow is { IsVisible: true } foregroundWindow)
        {
            foregroundWindow.Activate();
            foregroundWindow.Focus();
        }
        else if (state.ActiveWindow is { IsVisible: true } activeWindow)
        {
            activeWindow.Activate();
            activeWindow.Focus();
        }

        _ = RefreshProjectLibraryAsync();
    }

    private static int GetOwnerDepth(Window window)
    {
        var depth = 0;
        var current = window.Owner;
        while (current is not null)
        {
            depth++;
            current = current.Owner;
        }

        return depth;
    }

    private bool CanOwnChildWindows() => IsVisible && ShowInTaskbar && WindowState != WindowState.Minimized;

    private static string BuildDeleteSelectionMessage(int itemCount, int projectCount, int captureCount)
    {
        var lines = new List<string>
        {
            itemCount == 1
                ? "Delete the selected snip?"
                : $"Delete {itemCount} selected snips?"
        };

        if (projectCount > 0)
        {
            lines.Add(projectCount == 1
                ? "Saved editable .coyote projects will be moved to the Recycle Bin."
                : $"{projectCount} saved editable .coyote projects will be moved to the Recycle Bin.");
        }

        if (captureCount > 0)
        {
            lines.Add(captureCount == 1
                ? "Capture-only history items will be removed from CaptureCoyote."
                : $"{captureCount} capture-only history items will be removed from CaptureCoyote.");
        }

        lines.Add("Exported images are left on disk unless you remove them yourself.");

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static void DeleteFileToRecycleBin(string path)
    {
        FileSystem.DeleteFile(
            path,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin,
            UICancelOption.DoNothing);
    }

    private static void DeleteManagedSupportFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var localAppDataRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaptureCoyote"));
        var fullPath = Path.GetFullPath(path);

        if (!fullPath.StartsWith(localAppDataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Delete(fullPath);
    }

    private void ApplyWindowsStartupPreference()
    {
        try
        {
            _context.StartupLaunchService.ApplySetting(_context.Settings.LaunchOnWindowsStartup);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException("Could not update the Windows startup registration.", ex);
            ShowOperationError("Could not update Windows startup", ex.Message);
        }
    }

    private static void ShowOperationError(string title, string message)
    {
        System.Windows.MessageBox.Show(
            $"{message}\n\nLog: {AppDiagnostics.CurrentLogPath}",
            title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        _trayIconService.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindowOnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExitRequested)
        {
            e.Cancel = true;
            HideToTray(showHint: true);
            return;
        }

        _libraryRefreshCts?.Cancel();
        _libraryRefreshCts?.Dispose();
        _libraryRefreshCts = null;
        _workspaceSearchWindow?.Close();
        _workspaceSearchWindow = null;
        _context.HotkeyService.HotkeyPressed -= HotkeyServiceOnHotkeyPressed;
        StateChanged -= MainWindowOnStateChanged;
    }

    private sealed record CaptureWindowState(
        Window Window,
        WindowState WindowState,
        double Opacity,
        bool ShowInTaskbar,
        int OwnerDepth);

    private sealed record CaptureUiState(
        IReadOnlyList<CaptureWindowState> WindowsToRestore,
        Window? ActiveWindow)
    {
        public static CaptureUiState Default => new(Array.Empty<CaptureWindowState>(), null);
    }
}
