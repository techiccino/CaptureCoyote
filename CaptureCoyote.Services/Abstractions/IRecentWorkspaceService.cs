using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public interface IRecentWorkspaceService
{
    Task TrackCaptureAsync(AppSettings settings, ScreenshotProject project, CancellationToken cancellationToken = default);

    Task TrackProjectAsync(AppSettings settings, ScreenshotProject project, string projectPath, string? lastImagePath = null, CancellationToken cancellationToken = default);

    Task TrackImageExportAsync(AppSettings settings, ScreenshotProject project, string imagePath, CancellationToken cancellationToken = default);

    void PruneUnavailableEntries(AppSettings settings);

    int CleanupOldTemporaryItems(AppSettings settings, int olderThanDays);

    string? ResolveOpenPath(RecentWorkspaceItem item);
}
