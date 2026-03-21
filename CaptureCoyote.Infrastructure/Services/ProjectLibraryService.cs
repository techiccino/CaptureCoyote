using System.Security.Cryptography;
using System.Text;
using System.IO;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Infrastructure.Helpers;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class ProjectLibraryService(
    IProjectSerializationService projectSerializationService,
    IAnnotationRenderService annotationRenderService) : IProjectLibraryService
{
    private const int MaxLibraryItems = 18;

    public async Task<IReadOnlyList<RecentWorkspaceItem>> LoadAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.DefaultSaveFolder) || !Directory.Exists(settings.DefaultSaveFolder))
        {
            return [];
        }

        var projectPaths = Directory.EnumerateFiles(settings.DefaultSaveFolder, "*.coyote", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .Take(MaxLibraryItems)
            .ToList();

        var items = new List<RecentWorkspaceItem>(projectPaths.Count);
        foreach (var path in projectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var project = await projectSerializationService
                    .LoadAsync(path, useContainerPathAsEditableProjectPath: true, cancellationToken)
                    .ConfigureAwait(false);

                var thumbnailPath = BuildThumbnailPath(path);
                SaveThumbnailIfNeeded(thumbnailPath, path, project);

                items.Add(new RecentWorkspaceItem
                {
                    ProjectId = project.Id,
                    Name = project.Name,
                    UpdatedAt = project.ModifiedAt == default ? File.GetLastWriteTime(path) : project.ModifiedAt,
                    Mode = project.CaptureContext.Mode,
                    EditableProjectPath = path,
                    ThumbnailPath = thumbnailPath,
                    ExtractedText = project.ExtractedText,
                    AnnotationText = string.IsNullOrWhiteSpace(project.AnnotationText)
                        ? AnnotationSearchTextBuilder.Build(project.Annotations)
                        : project.AnnotationText,
                    SourceWindowTitle = project.CaptureContext.SourceWindowTitle
                });
            }
            catch
            {
                // Skip malformed or unavailable project files so one bad file does not block the library.
            }
        }

        return items;
    }

    private void SaveThumbnailIfNeeded(string thumbnailPath, string projectPath, ScreenshotProject project)
    {
        var projectUpdatedAt = File.GetLastWriteTimeUtc(projectPath);
        if (File.Exists(thumbnailPath) && File.GetLastWriteTimeUtc(thumbnailPath) >= projectUpdatedAt)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
        var thumbnailBytes = ImageHelper.CreateThumbnailPng(annotationRenderService.RenderToPng(project));
        File.WriteAllBytes(thumbnailPath, thumbnailBytes);
    }

    private static string BuildThumbnailPath(string projectPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectPath)));
        return Path.Combine(AppStoragePaths.LibraryThumbnailsDirectory, $"{hash}.png");
    }
}
