namespace CaptureCoyote.Core.Models;

public sealed class CaptureResult
{
    public required byte[] ImagePngBytes { get; init; }

    public required int PixelWidth { get; init; }

    public required int PixelHeight { get; init; }

    public required CaptureContext Context { get; init; }
}
