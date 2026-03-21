using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class WindowDescriptor
{
    public nint Handle { get; init; }

    public required string Title { get; init; }

    public required string ClassName { get; init; }

    public required PixelRect Bounds { get; init; }

    public required PixelRect ClientBounds { get; init; }
}
