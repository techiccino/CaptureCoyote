using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public interface IProjectLibraryService
{
    Task<IReadOnlyList<RecentWorkspaceItem>> LoadAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
