namespace CaptureCoyote.Services.Abstractions;

public interface IClipboardService
{
    void SetImage(byte[] pngBytes);

    void SetText(string text);
}
