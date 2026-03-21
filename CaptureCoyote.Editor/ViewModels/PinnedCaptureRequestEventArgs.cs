namespace CaptureCoyote.Editor.ViewModels;

public sealed class PinnedCaptureRequestEventArgs : EventArgs
{
    public PinnedCaptureRequestEventArgs(byte[] imagePngBytes, string title)
    {
        ImagePngBytes = imagePngBytes.ToArray();
        Title = title;
    }

    public byte[] ImagePngBytes { get; }

    public string Title { get; }
}
