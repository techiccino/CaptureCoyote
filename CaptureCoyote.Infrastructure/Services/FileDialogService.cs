using System.IO;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Services.Abstractions;
using Win32OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Win32SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? ShowOpenProjectDialog()
    {
        var dialog = new Win32OpenFileDialog
        {
            Title = "Open CaptureCoyote Project",
            Filter = "CaptureCoyote Project (*.coyote)|*.coyote|All Files (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveImageDialog(string initialDirectory, string suggestedFileName, ImageFileFormat format)
    {
        var extension = format == ImageFileFormat.Jpg ? "jpg" : "png";
        var dialog = new Win32SaveFileDialog
        {
            Title = "Save Image",
            InitialDirectory = initialDirectory,
            FileName = Path.GetFileNameWithoutExtension(suggestedFileName),
            DefaultExt = extension,
            AddExtension = true,
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveProjectDialog(string initialDirectory, string suggestedFileName)
    {
        var dialog = new Win32SaveFileDialog
        {
            Title = "Save Editable Project",
            InitialDirectory = initialDirectory,
            FileName = Path.GetFileNameWithoutExtension(suggestedFileName),
            DefaultExt = "coyote",
            AddExtension = true,
            Filter = "CaptureCoyote Project (*.coyote)|*.coyote"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
