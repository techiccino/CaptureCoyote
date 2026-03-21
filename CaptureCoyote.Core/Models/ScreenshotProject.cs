using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Core.Models;

public sealed class ScreenshotProject
{
    public const string CurrentFormatVersion = "1.0";

    public string FormatVersion { get; set; } = CurrentFormatVersion;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Untitled Capture";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;

    public string? EditableProjectPath { get; set; }

    public string? ExtractedText { get; set; }

    public string? AnnotationText { get; set; }

    public CaptureContext CaptureContext { get; set; } = new();

    public CropState CropState { get; set; } = new();

    public List<AnnotationObject> Annotations { get; set; } = [];

    public byte[] OriginalImagePngBytes { get; set; } = [];

    public int OriginalPixelWidth { get; set; }

    public int OriginalPixelHeight { get; set; }

    public ImageFileFormat LastExportFormat { get; set; } = ImageFileFormat.Png;

    public ScreenshotProject Clone()
    {
        return new ScreenshotProject
        {
            FormatVersion = FormatVersion,
            Id = Id,
            Name = Name,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
            EditableProjectPath = EditableProjectPath,
            ExtractedText = ExtractedText,
            AnnotationText = AnnotationText,
            CaptureContext = new CaptureContext
            {
                Mode = CaptureContext.Mode,
                DelaySeconds = CaptureContext.DelaySeconds,
                CapturedAt = CaptureContext.CapturedAt,
                SourceWindowTitle = CaptureContext.SourceWindowTitle,
                SourceWindowClass = CaptureContext.SourceWindowClass,
                SourceWindowHandle = CaptureContext.SourceWindowHandle,
                SourceBounds = CaptureContext.SourceBounds,
                ScrollFrameCount = CaptureContext.ScrollFrameCount
            },
            CropState = CropState.Clone(),
            Annotations = Annotations.Select(annotation => annotation.Clone()).OrderBy(annotation => annotation.ZIndex).ToList(),
            OriginalImagePngBytes = OriginalImagePngBytes.ToArray(),
            OriginalPixelWidth = OriginalPixelWidth,
            OriginalPixelHeight = OriginalPixelHeight,
            LastExportFormat = LastExportFormat
        };
    }
}
