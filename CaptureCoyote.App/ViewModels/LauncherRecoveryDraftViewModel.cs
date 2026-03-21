using System.IO;
using System.Windows.Media.Imaging;
using CaptureCoyote.Core.Models;

namespace CaptureCoyote.App.ViewModels;

public sealed class LauncherRecoveryDraftViewModel
{
    public LauncherRecoveryDraftViewModel(RecoveryDraftInfo draft)
    {
        Draft = draft;
        Thumbnail = LoadThumbnail(draft.ThumbnailPath);
    }

    public RecoveryDraftInfo Draft { get; }

    public BitmapSource? Thumbnail { get; }

    public string Title => Draft.Name;

    public string Subtitle => !string.IsNullOrWhiteSpace(Draft.SourceWindowTitle)
        ? Draft.SourceWindowTitle!
        : Draft.Mode.ToString();

    public string TimestampLabel => Draft.ModifiedAt.LocalDateTime.ToString("dd MMM, HH:mm");

    public string PreviewText => string.IsNullOrWhiteSpace(Draft.ExtractedText)
        ? "Unsaved edits are ready to restore."
        : Draft.ExtractedText!.ReplaceLineEndings(" ").Trim();

    private static BitmapSource? LoadThumbnail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
