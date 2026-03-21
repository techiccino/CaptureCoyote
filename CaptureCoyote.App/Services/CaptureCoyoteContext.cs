using CaptureCoyote.Core.Models;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.App.Services;

public sealed class CaptureCoyoteContext(
    AppSettings settings,
    ISettingsService settingsService,
    IScreenCaptureService screenCaptureService,
    IScrollingCaptureService scrollingCaptureService,
    IWindowLocatorService windowLocatorService,
    IHotkeyService hotkeyService,
    IClipboardService clipboardService,
    IFileExportService fileExportService,
    IProjectSerializationService projectSerializationService,
    IRecentWorkspaceService recentWorkspaceService,
    IProjectLibraryService projectLibraryService,
    IRecoveryDraftService recoveryDraftService,
    IStartupLaunchService startupLaunchService,
    IOcrService ocrService,
    IAnnotationRenderService annotationRenderService,
    IFileDialogService fileDialogService)
{
    public AppSettings Settings { get; } = settings;

    public ISettingsService SettingsService { get; } = settingsService;

    public IScreenCaptureService ScreenCaptureService { get; } = screenCaptureService;

    public IScrollingCaptureService ScrollingCaptureService { get; } = scrollingCaptureService;

    public IWindowLocatorService WindowLocatorService { get; } = windowLocatorService;

    public IHotkeyService HotkeyService { get; } = hotkeyService;

    public IClipboardService ClipboardService { get; } = clipboardService;

    public IFileExportService FileExportService { get; } = fileExportService;

    public IProjectSerializationService ProjectSerializationService { get; } = projectSerializationService;

    public IRecentWorkspaceService RecentWorkspaceService { get; } = recentWorkspaceService;

    public IProjectLibraryService ProjectLibraryService { get; } = projectLibraryService;

    public IRecoveryDraftService RecoveryDraftService { get; } = recoveryDraftService;

    public IStartupLaunchService StartupLaunchService { get; } = startupLaunchService;

    public IOcrService OcrService { get; } = ocrService;

    public IAnnotationRenderService AnnotationRenderService { get; } = annotationRenderService;

    public IFileDialogService FileDialogService { get; } = fileDialogService;

    public Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        return SettingsService.SaveAsync(Settings, cancellationToken);
    }
}
