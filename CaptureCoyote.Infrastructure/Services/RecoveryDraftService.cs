using System.IO;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Infrastructure.Helpers;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class RecoveryDraftService(
    IProjectSerializationService projectSerializationService,
    IAnnotationRenderService annotationRenderService) : IRecoveryDraftService
{
    private const int MaxDrafts = 6;

    public async Task SaveDraftAsync(AppSettings settings, ScreenshotProject project, CancellationToken cancellationToken = default)
    {
        var info = new RecoveryDraftInfo
        {
            ProjectId = project.Id,
            Name = project.Name,
            ModifiedAt = DateTimeOffset.Now,
            Mode = project.CaptureContext.Mode,
            DraftPath = Path.Combine(AppStoragePaths.RecoveryDirectory, $"{project.Id:N}.coyote"),
            ThumbnailPath = Path.Combine(AppStoragePaths.RecoveryThumbnailsDirectory, $"{project.Id:N}.png"),
            ExtractedText = project.ExtractedText,
            SourceWindowTitle = project.CaptureContext.SourceWindowTitle
        };

        Directory.CreateDirectory(Path.GetDirectoryName(info.DraftPath)!);
        await projectSerializationService
            .SaveAsync(info.DraftPath, project, preserveEditableProjectPath: true, cancellationToken)
            .ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(info.ThumbnailPath!)!);
        File.WriteAllBytes(info.ThumbnailPath!, ImageHelper.CreateThumbnailPng(annotationRenderService.RenderToPng(project)));

        var previousDrafts = settings.RecoveryDrafts.ToList();
        settings.RecoveryDrafts = settings.RecoveryDrafts
            .Where(item => item.ProjectId != info.ProjectId)
            .Prepend(info)
            .OrderByDescending(item => item.ModifiedAt)
            .Take(MaxDrafts)
            .ToList();

        var retainedIds = settings.RecoveryDrafts.Select(item => item.ProjectId).ToHashSet();
        var staleDrafts = previousDrafts
            .Where(item => !retainedIds.Contains(item.ProjectId))
            .ToList();

        foreach (var staleDraft in staleDrafts)
        {
            DeleteManagedFile(staleDraft.DraftPath, AppStoragePaths.RecoveryDirectory);
            DeleteManagedFile(staleDraft.ThumbnailPath, AppStoragePaths.RecoveryThumbnailsDirectory);
        }
    }

    public Task ClearDraftAsync(AppSettings settings, Guid projectId, CancellationToken cancellationToken = default)
    {
        var removed = settings.RecoveryDrafts.FirstOrDefault(item => item.ProjectId == projectId);
        settings.RecoveryDrafts = settings.RecoveryDrafts
            .Where(item => item.ProjectId != projectId)
            .OrderByDescending(item => item.ModifiedAt)
            .ToList();

        if (removed is not null)
        {
            DeleteManagedFile(removed.DraftPath, AppStoragePaths.RecoveryDirectory);
            DeleteManagedFile(removed.ThumbnailPath, AppStoragePaths.RecoveryThumbnailsDirectory);
        }

        return Task.CompletedTask;
    }

    public void PruneUnavailableDrafts(AppSettings settings)
    {
        var survivors = new List<RecoveryDraftInfo>();
        foreach (var draft in settings.RecoveryDrafts)
        {
            if (!string.IsNullOrWhiteSpace(draft.DraftPath) && File.Exists(draft.DraftPath))
            {
                survivors.Add(draft);
            }
            else
            {
                DeleteManagedFile(draft.ThumbnailPath, AppStoragePaths.RecoveryThumbnailsDirectory);
            }
        }

        settings.RecoveryDrafts = survivors
            .OrderByDescending(item => item.ModifiedAt)
            .Take(MaxDrafts)
            .ToList();
    }

    public string? ResolveOpenPath(RecoveryDraftInfo draft)
    {
        if (!string.IsNullOrWhiteSpace(draft.DraftPath) && File.Exists(draft.DraftPath))
        {
            return draft.DraftPath;
        }

        return null;
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
}
