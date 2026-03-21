using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public interface IProjectSerializationService
{
    Task SaveAsync(string path, ScreenshotProject project, bool preserveEditableProjectPath = false, CancellationToken cancellationToken = default);

    Task<ScreenshotProject> LoadAsync(string path, bool useContainerPathAsEditableProjectPath = true, CancellationToken cancellationToken = default);
}
