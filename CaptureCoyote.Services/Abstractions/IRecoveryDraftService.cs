using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public interface IRecoveryDraftService
{
    Task SaveDraftAsync(AppSettings settings, ScreenshotProject project, CancellationToken cancellationToken = default);

    Task ClearDraftAsync(AppSettings settings, Guid projectId, CancellationToken cancellationToken = default);

    void PruneUnavailableDrafts(AppSettings settings);

    string? ResolveOpenPath(RecoveryDraftInfo draft);
}
