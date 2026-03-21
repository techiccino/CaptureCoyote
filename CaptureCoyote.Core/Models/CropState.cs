using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class CropState
{
    public bool IsActive { get; set; }

    public PixelRect Bounds { get; set; }

    public CropState Clone() => new()
    {
        IsActive = IsActive,
        Bounds = Bounds
    };
}
