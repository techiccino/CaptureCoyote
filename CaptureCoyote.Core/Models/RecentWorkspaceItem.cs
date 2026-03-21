using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Core.Models;

public sealed class RecentWorkspaceItem
{
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = "Untitled Capture";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public CaptureMode Mode { get; set; }

    public string? EditableProjectPath { get; set; }

    public string? CachedProjectPath { get; set; }

    public string? LastImagePath { get; set; }

    public string? ThumbnailPath { get; set; }

    public string? ExtractedText { get; set; }

    public string? AnnotationText { get; set; }

    public string? SourceWindowTitle { get; set; }
}
