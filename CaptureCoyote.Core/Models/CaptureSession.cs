using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Core.Models;

public sealed class CaptureSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public CaptureMode Mode { get; set; }

    public int DelaySeconds { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.Now;

    public bool TriggeredByHotkey { get; set; }
}
