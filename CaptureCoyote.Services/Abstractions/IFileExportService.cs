using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public interface IFileExportService
{
    string BuildOutputPath(AppSettings settings, CaptureContext context, ImageFileFormat format);

    void SaveBytes(string path, byte[] bytes);

    void OpenContainingFolder(string path);
}
