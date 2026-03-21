using System.IO;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Infrastructure.Helpers;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class RecentWorkspaceService(
    IProjectSerializationService projectSerializationService,
    IAnnotationRenderService annotationRenderService) : IRecentWorkspaceService
{
    private const int MaxRecentItems = 12;

    public async Task TrackCaptureAsync(AppSettings settings, ScreenshotProject project, CancellationToken cancellationToken = default)
    {
        var item = CreateOrUpdateItem(settings, project);
        item.EditableProjectPath = project.EditableProjectPath;
        await SaveCachedProjectAsync(item.CachedProjectPath!, project, cancellationToken).ConfigureAwait(false);
        SaveThumbnail(item.ThumbnailPath!, project);
        Commit(settings, item);
    }

    public async Task TrackProjectAsync(AppSettings settings, ScreenshotProject project, string projectPath, string? lastImagePath = null, CancellationToken cancellationToken = default)
    {
        var item = CreateOrUpdateItem(settings, project);
        item.EditableProjectPath = projectPath;
        item.LastImagePath = string.IsNullOrWhiteSpace(lastImagePath) ? item.LastImagePath : lastImagePath;
        await SaveCachedProjectAsync(item.CachedProjectPath!, project, cancellationToken).ConfigureAwait(false);
        SaveThumbnail(item.ThumbnailPath!, project);
        Commit(settings, item);
    }

    public async Task TrackImageExportAsync(AppSettings settings, ScreenshotProject project, string imagePath, CancellationToken cancellationToken = default)
    {
        var item = CreateOrUpdateItem(settings, project);
        item.LastImagePath = imagePath;
        await SaveCachedProjectAsync(item.CachedProjectPath!, project, cancellationToken).ConfigureAwait(false);
        SaveThumbnail(item.ThumbnailPath!, project);
        Commit(settings, item);
    }

    public void PruneUnavailableEntries(AppSettings settings)
    {
        var survivors = new List<RecentWorkspaceItem>();
        foreach (var item in settings.RecentItems)
        {
            if (!string.IsNullOrWhiteSpace(item.EditableProjectPath) && File.Exists(item.EditableProjectPath))
            {
                survivors.Add(item);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.CachedProjectPath) && File.Exists(item.CachedProjectPath))
            {
                survivors.Add(item);
            }
            else
            {
                DeleteManagedFile(item.CachedProjectPath, AppStoragePaths.RecentProjectsDirectory);
                DeleteManagedFile(item.ThumbnailPath, AppStoragePaths.RecentThumbnailsDirectory);
            }
        }

        settings.RecentItems = survivors
            .OrderByDescending(item => item.UpdatedAt)
            .Take(MaxRecentItems)
            .ToList();
    }

    public int CleanupOldTemporaryItems(AppSettings settings, int olderThanDays)
    {
        if (olderThanDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.Now.AddDays(-olderThanDays);
        var staleItems = settings.RecentItems
            .Where(item => IsTemporaryItem(item) && item.UpdatedAt < cutoff)
            .ToList();

        if (staleItems.Count == 0)
        {
            return 0;
        }

        var staleIds = staleItems.Select(item => item.ProjectId).ToHashSet();
        settings.RecentItems = settings.RecentItems
            .Where(item => !staleIds.Contains(item.ProjectId))
            .OrderByDescending(item => item.UpdatedAt)
            .Take(MaxRecentItems)
            .ToList();

        foreach (var staleItem in staleItems)
        {
            DeleteManagedFile(staleItem.CachedProjectPath, AppStoragePaths.RecentProjectsDirectory);
            DeleteManagedFile(staleItem.ThumbnailPath, AppStoragePaths.RecentThumbnailsDirectory);
        }

        return staleItems.Count;
    }

    public string? ResolveOpenPath(RecentWorkspaceItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.EditableProjectPath) && File.Exists(item.EditableProjectPath))
        {
            return item.EditableProjectPath;
        }

        if (!string.IsNullOrWhiteSpace(item.CachedProjectPath) && File.Exists(item.CachedProjectPath))
        {
            return item.CachedProjectPath;
        }

        return null;
    }

    private static RecentWorkspaceItem CreateOrUpdateItem(AppSettings settings, ScreenshotProject project)
    {
        var existing = settings.RecentItems.FirstOrDefault(item => item.ProjectId == project.Id);
        return new RecentWorkspaceItem
        {
            ProjectId = project.Id,
            Name = project.Name,
            UpdatedAt = DateTimeOffset.Now,
            Mode = project.CaptureContext.Mode,
            EditableProjectPath = existing?.EditableProjectPath ?? project.EditableProjectPath,
            CachedProjectPath = existing?.CachedProjectPath ?? Path.Combine(AppStoragePaths.RecentProjectsDirectory, $"{project.Id:N}.coyote"),
            LastImagePath = existing?.LastImagePath,
            ThumbnailPath = existing?.ThumbnailPath ?? Path.Combine(AppStoragePaths.RecentThumbnailsDirectory, $"{project.Id:N}.png"),
            ExtractedText = project.ExtractedText,
            AnnotationText = string.IsNullOrWhiteSpace(project.AnnotationText)
                ? AnnotationSearchTextBuilder.Build(project.Annotations)
                : project.AnnotationText,
            SourceWindowTitle = project.CaptureContext.SourceWindowTitle
        };
    }

    private static void Commit(AppSettings settings, RecentWorkspaceItem updated)
    {
        var previousItems = settings.RecentItems.ToList();
        settings.RecentItems = settings.RecentItems
            .Where(item => item.ProjectId != updated.ProjectId)
            .Prepend(updated)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(MaxRecentItems)
            .ToList();

        var retainedIds = settings.RecentItems.Select(item => item.ProjectId).ToHashSet();
        var staleItems = previousItems
            .Where(item => !retainedIds.Contains(item.ProjectId))
            .ToList();

        foreach (var staleItem in staleItems)
        {
            DeleteManagedFile(staleItem.CachedProjectPath, AppStoragePaths.RecentProjectsDirectory);
            DeleteManagedFile(staleItem.ThumbnailPath, AppStoragePaths.RecentThumbnailsDirectory);
        }
    }

    private async Task SaveCachedProjectAsync(string cachePath, ScreenshotProject project, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await projectSerializationService
            .SaveAsync(cachePath, project, preserveEditableProjectPath: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private void SaveThumbnail(string thumbnailPath, ScreenshotProject project)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
        var thumbnailBytes = ImageHelper.CreateThumbnailPng(annotationRenderService.RenderToPng(project));
        File.WriteAllBytes(thumbnailPath, thumbnailBytes);
    }

    private static void DeleteManagedFile(string? path, string managedDirectory)
    {
        var safePath = path;
        if (string.IsNullOrWhiteSpace(safePath) || !File.Exists(safePath))
        {
            return;
        }

        if (!safePath.StartsWith(managedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Delete(safePath);
    }

    private static bool IsTemporaryItem(RecentWorkspaceItem item)
    {
        return string.IsNullOrWhiteSpace(item.EditableProjectPath) &&
               string.IsNullOrWhiteSpace(item.LastImagePath);
    }
}
