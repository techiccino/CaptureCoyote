using System.IO;
using System.Windows.Media.Imaging;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetImage(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        image.Freeze();
        System.Windows.Clipboard.SetImage(image);
    }

    public void SetText(string text)
    {
        System.Windows.Clipboard.SetText(text ?? string.Empty);
    }
}
