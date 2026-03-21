using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Core.Models;

public sealed class RecoveryDraftInfo
{
    public Guid ProjectId { get; set; }

    public string Name { get; set; } = "Untitled Capture";

    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;

    public CaptureMode Mode { get; set; }

    public string DraftPath { get; set; } = string.Empty;

    public string? ThumbnailPath { get; set; }

    public string? ExtractedText { get; set; }

    public string? SourceWindowTitle { get; set; }
}
