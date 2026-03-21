using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class CaptureContext
{
    public CaptureMode Mode { get; set; }

    public int DelaySeconds { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;

    public string? SourceWindowTitle { get; set; }

    public string? SourceWindowClass { get; set; }

    public long? SourceWindowHandle { get; set; }

    public PixelRect SourceBounds { get; set; }

    public int? ScrollFrameCount { get; set; }
}
