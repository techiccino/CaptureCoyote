using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Core.Models;

public sealed class ExportOptions
{
    public ImageFileFormat Format { get; set; } = ImageFileFormat.Png;

    public int JpegQuality { get; set; } = 92;

    public bool IncludeCrop { get; set; } = true;
}
