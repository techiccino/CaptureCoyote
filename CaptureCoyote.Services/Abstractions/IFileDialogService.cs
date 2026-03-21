using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Services.Abstractions;

public interface IFileDialogService
{
    string? ShowOpenProjectDialog();

    string? ShowSaveImageDialog(string initialDirectory, string suggestedFileName, ImageFileFormat format);

    string? ShowSaveProjectDialog(string initialDirectory, string suggestedFileName);
}
