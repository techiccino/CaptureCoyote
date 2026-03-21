using System.IO;
using System.Windows.Media.Imaging;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Mvvm;

namespace CaptureCoyote.App.ViewModels;

public sealed class LauncherRecentItemViewModel : ObservableObject
{
    private bool _isSelected;

    public LauncherRecentItemViewModel(RecentWorkspaceItem item)
    {
        Item = item;
        Thumbnail = LoadThumbnail(item.ThumbnailPath);
    }

    public RecentWorkspaceItem Item { get; }

    public BitmapSource? Thumbnail { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsEditableProject => !string.IsNullOrWhiteSpace(Item.EditableProjectPath);

    public string Title => Item.Name;

    public string BadgeText => IsEditableProject ? "Project" : "Capture";

    public string BadgeToolTip => IsEditableProject
        ? "Editable .coyote project saved to disk."
        : "Recent capture kept in CaptureCoyote history.";

    public bool CanRevealInFolder =>
        !string.IsNullOrWhiteSpace(Item.EditableProjectPath) ||
        !string.IsNullOrWhiteSpace(Item.LastImagePath) ||
        !string.IsNullOrWhiteSpace(Item.CachedProjectPath);

    public string Subtitle => !string.IsNullOrWhiteSpace(Item.SourceWindowTitle)
        ? Item.SourceWindowTitle!
        : CaptureModeDisplay.ToDisplayText(Item.Mode);

    public string TimestampLabel => Item.UpdatedAt.LocalDateTime.ToString("dd MMM, HH:mm");

    public string PreviewText => BuildPreviewText(160);

    public string DetailedPreviewText => BuildPreviewText(280);

    public string SearchText => $"{Title} {Subtitle} {CollapseWhitespace(Item.ExtractedText)} {CollapseWhitespace(Item.AnnotationText)}";

    private string BuildPreviewText(int maxLength)
    {
        var parts = new List<string>();
        var ocrText = CollapseWhitespace(Item.ExtractedText);
        var annotationText = CollapseWhitespace(Item.AnnotationText);

        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            parts.Add($"OCR: {ocrText}");
        }

        if (!string.IsNullOrWhiteSpace(annotationText))
        {
            parts.Add($"Notes: {annotationText}");
        }

        var text = string.Join(" | ", parts);

        if (string.IsNullOrWhiteSpace(text))
        {
            return "No OCR or annotation text yet.";
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        return $"{text[..maxLength].TrimEnd()}...";
    }

    private static string CollapseWhitespace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

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
