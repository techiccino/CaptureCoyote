using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class MonitorDescriptor
{
    public required string DeviceName { get; init; }

    public required string FriendlyName { get; init; }

    public required bool IsPrimary { get; init; }

    public required double ScaleFactor { get; init; }

    public required PixelRect Bounds { get; init; }

    public required PixelRect WorkingArea { get; init; }
}
