using System.IO;
using System.Windows.Media.Imaging;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Mvvm;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.App.ViewModels;

public sealed class CaptureReviewViewModel : ObservableObject
{
    private readonly ScreenshotProject _project;
    private readonly AppSettings _settings;
    private readonly IAnnotationRenderService _annotationRenderService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileExportService _fileExportService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IProjectSerializationService _projectSerializationService;
    private readonly Action<ScreenshotProject> _openEditor;
    private readonly Func<ScreenshotProject, string, Task> _trackProjectAsync;
    private readonly Func<ScreenshotProject, string, Task> _trackImageExportAsync;
    private readonly Action<string> _copyText;
    private string _statusText = "Review the capture, then finish in one click.";

    public CaptureReviewViewModel(
        ScreenshotProject project,
        AppSettings settings,
        IAnnotationRenderService annotationRenderService,
        IClipboardService clipboardService,
        IFileExportService fileExportService,
        IFileDialogService fileDialogService,
        IProjectSerializationService projectSerializationService,
        Action<ScreenshotProject> openEditor,
        Func<ScreenshotProject, string, Task> trackProjectAsync,
        Func<ScreenshotProject, string, Task> trackImageExportAsync,
        Action<string> copyText)
    {
        _project = project;
        _settings = settings;
        _annotationRenderService = annotationRenderService;
        _clipboardService = clipboardService;
        _fileExportService = fileExportService;
        _fileDialogService = fileDialogService;
        _projectSerializationService = projectSerializationService;
        _openEditor = openEditor;
        _trackProjectAsync = trackProjectAsync;
        _trackImageExportAsync = trackImageExportAsync;
        _copyText = copyText;

        PreviewImage = Decode(project.OriginalImagePngBytes);
        CopyCommand = new RelayCommand(Copy);
        QuickSaveCommand = new RelayCommand(QuickSave);
        SaveAsCommand = new RelayCommand(SaveAs);
        SaveEditableCommand = new AsyncRelayCommand(SaveEditableAsync);
        CopyDetectedTextCommand = new RelayCommand(CopyDetectedText, () => HasExtractedText);
        OpenInEditorCommand = new RelayCommand(() => _openEditor(_project));
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? RequestClose;

    public BitmapSource PreviewImage { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public RelayCommand CopyCommand { get; }

    public RelayCommand QuickSaveCommand { get; }

    public RelayCommand SaveAsCommand { get; }

    public AsyncRelayCommand SaveEditableCommand { get; }

    public RelayCommand OpenInEditorCommand { get; }

    public RelayCommand CopyDetectedTextCommand { get; }

    public RelayCommand CancelCommand { get; }

    public string CaptureSummary => BuildSummary();

    public bool HasExtractedText => !string.IsNullOrWhiteSpace(_project.ExtractedText);

    public string ExtractedTextPreview => string.IsNullOrWhiteSpace(_project.ExtractedText)
        ? "No machine-readable text was found in this capture."
        : _project.ExtractedText!;

    public void ApplyExtractedText(string? extractedText)
    {
        var normalized = extractedText?.Trim() ?? string.Empty;
        if (string.Equals(_project.ExtractedText ?? string.Empty, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _project.ExtractedText = normalized;
        RaisePropertyChanged(nameof(HasExtractedText));
        RaisePropertyChanged(nameof(ExtractedTextPreview));

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            StatusText = "OCR finished for this capture.";
        }
    }

    private void Copy()
    {
        var bytes = _annotationRenderService.RenderToPng(_project);
        _clipboardService.SetImage(bytes);
        StatusText = "Copied final image to the clipboard.";
    }

    private void QuickSave()
    {
        var path = _fileExportService.BuildOutputPath(_settings, _project.CaptureContext, _settings.PreferredImageFormat);
        var bytes = _annotationRenderService.Render(_project, new ExportOptions { Format = _settings.PreferredImageFormat, IncludeCrop = true });
        _fileExportService.SaveBytes(path, bytes);
        _ = _trackImageExportAsync(_project, path);
        StatusText = $"Quick saved to {path}";
    }

    private void SaveAs()
    {
        var defaultPath = _fileExportService.BuildOutputPath(_settings, _project.CaptureContext, _settings.PreferredImageFormat);
        var path = _fileDialogService.ShowSaveImageDialog(_settings.DefaultSaveFolder, Path.GetFileName(defaultPath), _settings.PreferredImageFormat);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var format = Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ? ImageFileFormat.Jpg : ImageFileFormat.Png;
        var bytes = _annotationRenderService.Render(_project, new ExportOptions { Format = format, IncludeCrop = true });
        _fileExportService.SaveBytes(path, bytes);
        _ = _trackImageExportAsync(_project, path);
        StatusText = $"Saved image to {path}";
    }

    private async Task SaveEditableAsync()
    {
        var suggestedName = Path.GetFileNameWithoutExtension(_fileExportService.BuildOutputPath(_settings, _project.CaptureContext, _settings.PreferredImageFormat));
        var path = _fileDialogService.ShowSaveProjectDialog(_settings.DefaultSaveFolder, suggestedName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await _projectSerializationService.SaveAsync(path, _project).ConfigureAwait(true);
        _project.EditableProjectPath = path;
        await _trackProjectAsync(_project, path).ConfigureAwait(true);
        StatusText = $"Saved editable project to {path}";
    }

    private void CopyDetectedText()
    {
        if (!HasExtractedText)
        {
            return;
        }

        _copyText(_project.ExtractedText!);
        StatusText = "Copied detected text to the clipboard.";
    }

    private string BuildSummary()
    {
        var source = string.IsNullOrWhiteSpace(_project.CaptureContext.SourceWindowTitle)
            ? CaptureModeDisplay.ToDisplayText(_project.CaptureContext.Mode)
            : _project.CaptureContext.SourceWindowTitle;

        return $"{source} - {_project.CaptureContext.CapturedAt.LocalDateTime:dd MMM yyyy, HH:mm}";
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
