using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class DesktopSnapshot
{
    public required int PixelWidth { get; init; }

    public required int PixelHeight { get; init; }

    public required byte[] PixelBuffer { get; init; }

    public required int PixelStride { get; init; }

    public required PixelRect VirtualBounds { get; init; }

    public required IReadOnlyList<MonitorDescriptor> Monitors { get; init; }
}
