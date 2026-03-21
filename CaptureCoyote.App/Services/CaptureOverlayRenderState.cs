using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.App.Services;

internal sealed class CaptureOverlayRenderState
{
    public required CaptureMode Mode { get; init; }

    public required PixelPoint Cursor { get; init; }

    public PixelRect? Selection { get; init; }

    public WindowDescriptor? HoveredWindow { get; init; }

    public bool ShowMagnifier { get; init; }

    public required string Instructions { get; init; }
}
