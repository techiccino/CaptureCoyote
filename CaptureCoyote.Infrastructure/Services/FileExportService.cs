using System.Diagnostics;
using System.IO;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class FileExportService : IFileExportService
{
    public string BuildOutputPath(AppSettings settings, CaptureContext context, ImageFileFormat format)
    {
        Directory.CreateDirectory(settings.DefaultSaveFolder);

        var name = settings.FileNamingPattern
            .Replace("{date}", context.CapturedAt.ToString("yyyyMMdd"))
            .Replace("{time}", context.CapturedAt.ToString("HHmmss"))
            .Replace("{mode}", context.Mode.ToString().ToLowerInvariant());

        if (context.SourceWindowTitle is { Length: > 0 })
        {
            name = name.Replace("{title}", SanitizePath(context.SourceWindowTitle));
        }
        else
        {
            name = name.Replace("{title}", "capture");
        }

        var extension = format == ImageFileFormat.Jpg ? ".jpg" : ".png";
        return Path.Combine(settings.DefaultSaveFolder, $"{SanitizePath(name)}{extension}");
    }

    public void SaveBytes(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
    }

    public void OpenContainingFolder(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }

    private static string SanitizePath(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Trim().Trim('.');
    }
}
