using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Mvvm;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Editor.ViewModels;

public sealed class EditorViewModel : ObservableObject
{
    private readonly IAnnotationRenderService _annotationRenderService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IFileExportService _fileExportService;
    private readonly IProjectSerializationService _projectSerializationService;
    private readonly IRecentWorkspaceService _recentWorkspaceService;
    private readonly IRecoveryDraftService _recoveryDraftService;
    private readonly Func<Task> _persistWorkspaceStateAsync;
    private readonly Func<Task>? _workspaceChangedAsync;
    private readonly Stack<ScreenshotProject> _undoStack = [];
    private readonly Stack<ScreenshotProject> _redoStack = [];

    private ObservableCollection<AnnotationObject> _annotations = [];
    private ObservableCollection<AnnotationObject> _layerAnnotations = [];
    private BitmapSource _baseImage;
    private bool _isDirty;
    private string _statusText = "Ready";
    private ScreenshotProject _project;
    private AnnotationObject? _selectedAnnotation;
    private EditorTool _selectedTool;
    private string _strokeColorHex;
    private string _fillColorHex;
    private double _strokeThickness;
    private double _opacity;
    private double _fontSize;
    private double _blurStrength;
    private StylePresetKind _currentStylePreset;
    private bool _isSyncingStyleState;
    private string? _lastOutputPath;
    private CancellationTokenSource? _recoverySaveCts;

    public EditorViewModel(
        ScreenshotProject project,
        AppSettings settings,
        IAnnotationRenderService annotationRenderService,
        IClipboardService clipboardService,
        IFileDialogService fileDialogService,
        IFileExportService fileExportService,
        IProjectSerializationService projectSerializationService,
        IRecentWorkspaceService recentWorkspaceService,
        IRecoveryDraftService recoveryDraftService,
        Func<Task> persistWorkspaceStateAsync,
        bool startDirty = false,
        Func<Task>? workspaceChangedAsync = null)
    {
        _project = project.Clone();
        Settings = settings;
        _annotationRenderService = annotationRenderService;
        _clipboardService = clipboardService;
        _fileDialogService = fileDialogService;
        _fileExportService = fileExportService;
        _projectSerializationService = projectSerializationService;
        _recentWorkspaceService = recentWorkspaceService;
        _recoveryDraftService = recoveryDraftService;
        _persistWorkspaceStateAsync = persistWorkspaceStateAsync;
        _workspaceChangedAsync = workspaceChangedAsync;

        _baseImage = DecodeBitmap(_project.OriginalImagePngBytes);
        _selectedTool = settings.LastUsedTool;
        _strokeColorHex = settings.StyleDefaults.StrokeColor.ToHex();
        _fillColorHex = settings.StyleDefaults.FillColor.ToHex();
        _strokeThickness = settings.StyleDefaults.StrokeThickness;
        _opacity = settings.StyleDefaults.Opacity;
        _fontSize = settings.StyleDefaults.FontSize;
        _blurStrength = settings.StyleDefaults.BlurStrength;
        _currentStylePreset = settings.DefaultStylePreset;

        RecentColors = new ObservableCollection<string>(settings.RecentColors);
        StylePresets = new ObservableCollection<StylePresetDefinition>(StylePresetCatalog.All);
        Tools = Enum.GetValues<EditorTool>();
        Annotations = new ObservableCollection<AnnotationObject>(_project.Annotations.OrderBy(annotation => annotation.ZIndex));
        LayerAnnotations = new ObservableCollection<AnnotationObject>(Annotations.OrderByDescending(annotation => annotation.ZIndex));
        _project.AnnotationText = string.IsNullOrWhiteSpace(_project.AnnotationText)
            ? AnnotationSearchTextBuilder.Build(_project.Annotations)
            : _project.AnnotationText;

        SelectToolCommand = new RelayCommand<EditorTool>(tool => SelectedTool = tool);
        UndoCommand = new RelayCommand(Undo, () => _undoStack.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redoStack.Count > 0);
        DeleteSelectionCommand = new RelayCommand(DeleteSelection, () => SelectedAnnotation is not null);
        DuplicateSelectionCommand = new RelayCommand(DuplicateSelection, () => SelectedAnnotation is not null);
        BringForwardCommand = new RelayCommand(BringForward, () => SelectedAnnotation is not null);
        SendBackwardCommand = new RelayCommand(SendBackward, () => SelectedAnnotation is not null);
        ClearCropCommand = new RelayCommand(ClearCrop, () => _project.CropState.IsActive);
        ApplyStylePresetCommand = new RelayCommand<StylePresetKind>(kind => ApplyStylePreset(kind));
        ApplyStrokePresetCommand = new RelayCommand<string>(hex => StrokeColorHex = hex ?? StrokeColorHex);
        ApplyFillPresetCommand = new RelayCommand<string>(hex => FillColorHex = hex ?? FillColorHex);
        CopyCommand = new RelayCommand(CopyToClipboard);
        CopyDetectedTextCommand = new RelayCommand(CopyDetectedText, () => HasExtractedText);
        CopySearchableTextCommand = new RelayCommand(CopySearchableText, () => HasCombinedSearchText);
        ShowDetectedTextCommand = new RelayCommand(ShowDetectedText);
        QuickSaveCommand = new AsyncRelayCommand(() => ExportImageAsync(quickSave: true));
        SaveImageAsCommand = new AsyncRelayCommand(() => ExportImageAsync(quickSave: false));
        SaveProjectCommand = new AsyncRelayCommand(async () => await SaveProjectAsync(saveAs: false).ConfigureAwait(true));
        SaveProjectAsCommand = new AsyncRelayCommand(async () => await SaveProjectAsync(saveAs: true).ConfigureAwait(true));
        OpenContainingFolderCommand = new RelayCommand(OpenContainingFolder, () => !string.IsNullOrWhiteSpace(_lastOutputPath));
        PinToScreenCommand = new RelayCommand(PinToScreen);

        if (Settings.UseToolAwareDefaults)
        {
            ApplyRecommendedDefaultsForTool(_selectedTool, applyToSelection: false);
        }

        if (startDirty)
        {
            IsDirty = true;
            StatusText = "Recovered unsaved edits. Save when ready.";
        }
    }

    public event EventHandler? SurfaceInvalidated;

    public event EventHandler<PinnedCaptureRequestEventArgs>? PinToScreenRequested;

    public event EventHandler? DetectedTextRequested;

    public AppSettings Settings { get; }

    public IEnumerable<EditorTool> Tools { get; }

    public ObservableCollection<string> RecentColors { get; }

    public ObservableCollection<StylePresetDefinition> StylePresets { get; }

    public ObservableCollection<AnnotationObject> Annotations
    {
        get => _annotations;
        private set => SetProperty(ref _annotations, value);
    }

    public ObservableCollection<AnnotationObject> LayerAnnotations
    {
        get => _layerAnnotations;
        private set => SetProperty(ref _layerAnnotations, value);
    }

    public BitmapSource BaseImage
    {
        get => _baseImage;
        private set => SetProperty(ref _baseImage, value);
    }

    public string DisplayTitle => $"{_project.Name}{(IsDirty ? "*" : string.Empty)}";

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public AnnotationObject? SelectedAnnotation
    {
        get => _selectedAnnotation;
        set
        {
            if (SetProperty(ref _selectedAnnotation, value))
            {
                RaisePropertyChanged(nameof(HasSelection));
                SyncStylePropertiesFromSelection();
                RaiseCommandStates();
                InvalidateSurface();
            }
        }
    }

    public bool HasSelection => SelectedAnnotation is not null;

    public bool HasExtractedText => !string.IsNullOrWhiteSpace(_project.ExtractedText);

    public string ExtractedText => _project.ExtractedText ?? string.Empty;

    public bool HasAnnotationText => !string.IsNullOrWhiteSpace(_project.AnnotationText);

    public string AnnotationText => _project.AnnotationText ?? string.Empty;

    public bool HasCombinedSearchText => !string.IsNullOrWhiteSpace(CombinedSearchText);

    public string CombinedSearchText => BuildCombinedSearchText();

    public string OcrReviewSummary => BuildOcrReviewSummary();

    public string OcrGuidanceText => BuildOcrGuidanceText();

    public string SearchIndexHeading => HasExtractedText
        ? "Search Index (OCR + Annotations)"
        : "Search Index (Annotations Only)";

    public EditorTool SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (SetProperty(ref _selectedTool, value))
            {
                Settings.LastUsedTool = value;
                if (SelectedAnnotation is null && Settings.UseToolAwareDefaults)
                {
                    ApplyRecommendedDefaultsForTool(value, applyToSelection: false);
                }

                InvalidateSurface();
            }
        }
    }

    public StylePresetKind CurrentStylePreset
    {
        get => _currentStylePreset;
        private set
        {
            if (SetProperty(ref _currentStylePreset, value))
            {
                RaisePropertyChanged(nameof(CurrentPresetDescription));
                RaisePropertyChanged(nameof(SelectedPresetKind));
            }
        }
    }

    public StylePresetKind SelectedPresetKind
    {
        get => CurrentStylePreset;
        set
        {
            if (value != CurrentStylePreset)
            {
                ApplyStylePreset(value);
            }
        }
    }

    public string CurrentPresetDescription => StylePresetCatalog.Get(CurrentStylePreset).Description;

    public string StrokeColorHex
    {
        get => _strokeColorHex;
        set
        {
            var normalized = NormalizeHex(value, Settings.StyleDefaults.StrokeColor);
            if (SetProperty(ref _strokeColorHex, normalized))
            {
                ApplyStyleStateChanges(isFill: false);
            }
        }
    }

    public string FillColorHex
    {
        get => _fillColorHex;
        set
        {
            var normalized = NormalizeHex(value, Settings.StyleDefaults.FillColor);
            if (SetProperty(ref _fillColorHex, normalized))
            {
                ApplyStyleStateChanges(isFill: true);
            }
        }
    }

    public double StrokeThickness
    {
        get => _strokeThickness;
        set
        {
            if (SetProperty(ref _strokeThickness, Math.Max(0, value)))
            {
                ApplyStyleStateChanges();
            }
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetProperty(ref _opacity, Math.Clamp(value, 0.05, 1)))
            {
                ApplyStyleStateChanges();
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (SetProperty(ref _fontSize, Math.Max(10, value)))
            {
                ApplyStyleStateChanges();
            }
        }
    }

    public double BlurStrength
    {
        get => _blurStrength;
        set
        {
            if (SetProperty(ref _blurStrength, Math.Max(2, value)))
            {
                ApplyStyleStateChanges();
            }
        }
    }

    public RelayCommand<EditorTool> SelectToolCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand RedoCommand { get; }

    public RelayCommand DeleteSelectionCommand { get; }

    public RelayCommand DuplicateSelectionCommand { get; }

    public RelayCommand BringForwardCommand { get; }

    public RelayCommand SendBackwardCommand { get; }

    public RelayCommand ClearCropCommand { get; }

    public RelayCommand<StylePresetKind> ApplyStylePresetCommand { get; }

    public RelayCommand<string> ApplyStrokePresetCommand { get; }

    public RelayCommand<string> ApplyFillPresetCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand CopyDetectedTextCommand { get; }

    public RelayCommand CopySearchableTextCommand { get; }

    public RelayCommand ShowDetectedTextCommand { get; }

    public AsyncRelayCommand QuickSaveCommand { get; }

    public AsyncRelayCommand SaveImageAsCommand { get; }

    public AsyncRelayCommand SaveProjectCommand { get; }

    public AsyncRelayCommand SaveProjectAsCommand { get; }

    public RelayCommand OpenContainingFolderCommand { get; }

    public RelayCommand PinToScreenCommand { get; }

    public PixelRect GetVisibleBounds()
    {
        if (_project.CropState.IsActive && !_project.CropState.Bounds.IsEmpty)
        {
            return _project.CropState.Bounds.Normalize().ClampWithin(new PixelRect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight));
        }

        return new PixelRect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight);
    }

    public void CaptureUndoState()
    {
        _undoStack.Push(BuildProjectSnapshot());
        if (_undoStack.Count > 50)
        {
            var trimmed = _undoStack.Reverse().Take(50).Reverse().ToList();
            _undoStack.Clear();
            foreach (var item in trimmed)
            {
                _undoStack.Push(item);
            }
        }

        _redoStack.Clear();
        RaiseCommandStates();
    }

    public void AddAnnotation(AnnotationObject annotation, bool select = true)
    {
        annotation.ZIndex = Annotations.Count == 0 ? 0 : Annotations.Max(item => item.ZIndex) + 1;
        ApplyCurrentStyle(annotation);
        Annotations.Add(annotation);
        SortAnnotations();
        if (select)
        {
            SelectedAnnotation = annotation;
        }

        MarkDirty("Annotation added.");
    }

    public void RemoveAnnotation(AnnotationObject annotation)
    {
        if (Annotations.Remove(annotation))
        {
            if (ReferenceEquals(SelectedAnnotation, annotation))
            {
                SelectedAnnotation = null;
            }

            ReindexAnnotations();
            RefreshLayerAnnotations();
            MarkDirty("Annotation removed.");
        }
    }

    public void SetCrop(PixelRect bounds)
    {
        _project.CropState.IsActive = true;
        _project.CropState.Bounds = bounds.Normalize().ClampWithin(new PixelRect(0, 0, BaseImage.PixelWidth, BaseImage.PixelHeight));
        MarkDirty("Crop updated.");
    }

    public void ClearCrop()
    {
        _project.CropState.IsActive = false;
        _project.CropState.Bounds = PixelRect.Empty;
        MarkDirty("Crop cleared.");
    }

    public ScreenshotProject BuildProjectSnapshot()
    {
        var snapshot = _project.Clone();
        snapshot.Annotations = Annotations.Select(annotation => annotation.Clone()).OrderBy(annotation => annotation.ZIndex).ToList();
        snapshot.AnnotationText = AnnotationSearchTextBuilder.Build(snapshot.Annotations);
        snapshot.OriginalImagePngBytes = _project.OriginalImagePngBytes.ToArray();
        snapshot.ModifiedAt = DateTimeOffset.Now;
        snapshot.LastExportFormat = _project.LastExportFormat;
        return snapshot;
    }

    public int GetNextStepNumber() => Annotations.OfType<StepAnnotation>().Select(annotation => annotation.Number).DefaultIfEmpty().Max() + 1;

    public void NotifyAnnotationChanged(string status) => MarkDirty(status);

    public void NudgeSelectedAnnotation(double deltaX, double deltaY)
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        CaptureUndoState();
        SelectedAnnotation.Move(deltaX, deltaY);
        MarkDirty("Annotation nudged.");
    }

    public void RenameLayer(AnnotationObject annotation, string? layerName)
    {
        if (!Annotations.Contains(annotation))
        {
            return;
        }

        var normalized = string.IsNullOrWhiteSpace(layerName) ? string.Empty : layerName.Trim();
        if (string.Equals(annotation.LayerName, normalized, StringComparison.Ordinal))
        {
            return;
        }

        CaptureUndoState();
        annotation.LayerName = normalized;
        RefreshLayerAnnotations();
        MarkDirty(string.IsNullOrWhiteSpace(normalized) ? "Layer name cleared." : "Layer renamed.");
    }

    public void MoveLayerToDisplayIndex(AnnotationObject annotation, int targetDisplayIndex)
    {
        if (!Annotations.Contains(annotation))
        {
            return;
        }

        var displayOrdered = Annotations
            .OrderByDescending(item => item.ZIndex)
            .ToList();

        var currentIndex = displayOrdered.FindIndex(item => item.Id == annotation.Id);
        if (currentIndex < 0)
        {
            return;
        }

        targetDisplayIndex = Math.Clamp(targetDisplayIndex, 0, displayOrdered.Count);
        if (targetDisplayIndex > currentIndex)
        {
            targetDisplayIndex--;
        }

        if (targetDisplayIndex == currentIndex)
        {
            return;
        }

        CaptureUndoState();
        displayOrdered.RemoveAt(currentIndex);
        displayOrdered.Insert(targetDisplayIndex, annotation);

        Annotations = new ObservableCollection<AnnotationObject>(displayOrdered.Reverse<AnnotationObject>());
        ReindexAnnotations();
        RefreshLayerAnnotations();
        SelectedAnnotation = annotation;
        MarkDirty("Layer order updated.");
    }

    public string GetLayerDisplayName(AnnotationObject annotation)
    {
        if (annotation.HasCustomLayerName)
        {
            return annotation.LayerName;
        }

        var baseName = GetDefaultLayerName(annotation);
        var unnamedMatches = Annotations
            .OrderBy(item => item.ZIndex)
            .Where(item => !item.HasCustomLayerName && string.Equals(GetDefaultLayerName(item), baseName, StringComparison.Ordinal))
            .ToList();

        if (unnamedMatches.Count <= 1)
        {
            return baseName;
        }

        var index = unnamedMatches.FindIndex(item => item.Id == annotation.Id);
        return index < 0 ? baseName : $"{baseName} {index + 1}";
    }

    public void ApplyExtractedText(string? extractedText)
    {
        var normalized = extractedText?.Trim() ?? string.Empty;
        if (string.Equals(_project.ExtractedText ?? string.Empty, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _project.ExtractedText = normalized;
        RaisePropertyChanged(nameof(ExtractedText));
        RaisePropertyChanged(nameof(HasExtractedText));
        RaisePropertyChanged(nameof(HasCombinedSearchText));
        RaisePropertyChanged(nameof(CombinedSearchText));
        RaisePropertyChanged(nameof(OcrReviewSummary));
        RaisePropertyChanged(nameof(OcrGuidanceText));
        RaisePropertyChanged(nameof(SearchIndexHeading));
        RaiseCommandStates();

        if (!IsDirty)
        {
            StatusText = string.IsNullOrWhiteSpace(normalized)
                ? "No OCR text was found. OCR uses the original captured pixels, so recapturing at a higher source zoom may help."
                : "OCR finished for this capture.";
        }
    }

    public async Task<bool> SaveBeforeCloseAsync()
    {
        return await SaveProjectAsync(saveAs: string.IsNullOrWhiteSpace(_project.EditableProjectPath)).ConfigureAwait(true);
    }

    public async Task PersistRecoveryDraftNowAsync()
    {
        CancelPendingRecoverySave();
        if (!IsDirty)
        {
            return;
        }

        await _recoveryDraftService.SaveDraftAsync(Settings, BuildProjectSnapshot()).ConfigureAwait(true);
        await _persistWorkspaceStateAsync().ConfigureAwait(true);
    }

    public async Task ClearRecoveryDraftAsync()
    {
        CancelPendingRecoverySave();
        await _recoveryDraftService.ClearDraftAsync(Settings, _project.Id).ConfigureAwait(true);
        await _persistWorkspaceStateAsync().ConfigureAwait(true);
    }

    private async Task<bool> SaveProjectAsync(bool saveAs)
    {
        var current = BuildProjectSnapshot();
        var suggestedName = Path.GetFileNameWithoutExtension(_fileExportService.BuildOutputPath(Settings, current.CaptureContext, current.LastExportFormat));

        var path = saveAs || string.IsNullOrWhiteSpace(current.EditableProjectPath)
            ? _fileDialogService.ShowSaveProjectDialog(Settings.DefaultSaveFolder, suggestedName)
            : current.EditableProjectPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        await _projectSerializationService.SaveAsync(path, current).ConfigureAwait(true);
        current.EditableProjectPath = path;
        current.Name = Path.GetFileNameWithoutExtension(path);
        await _recentWorkspaceService.TrackProjectAsync(Settings, current, path).ConfigureAwait(true);
        await _recoveryDraftService.ClearDraftAsync(Settings, current.Id).ConfigureAwait(true);
        await _persistWorkspaceStateAsync().ConfigureAwait(true);
        if (_workspaceChangedAsync is not null)
        {
            await _workspaceChangedAsync().ConfigureAwait(true);
        }

        _project.EditableProjectPath = path;
        _project.ModifiedAt = DateTimeOffset.Now;
        _project.Name = Path.GetFileNameWithoutExtension(path);
        IsDirty = false;
        StatusText = $"Saved editable project to {path}";
        RaisePropertyChanged(nameof(DisplayTitle));
        InvalidateSurface();
        return true;
    }

    private async Task ExportImageAsync(bool quickSave)
    {
        var current = BuildProjectSnapshot();
        var format = Settings.PreferredImageFormat;
        var defaultPath = _fileExportService.BuildOutputPath(Settings, current.CaptureContext, format);
        var path = quickSave
            ? defaultPath
            : _fileDialogService.ShowSaveImageDialog(Settings.DefaultSaveFolder, Path.GetFileName(defaultPath), format);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        format = Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            ? ImageFileFormat.Jpg
            : ImageFileFormat.Png;

        current.LastExportFormat = format;
        var bytes = _annotationRenderService.Render(current, new ExportOptions { Format = format, IncludeCrop = true });
        _fileExportService.SaveBytes(path, bytes);
        await _recentWorkspaceService.TrackImageExportAsync(Settings, current, path).ConfigureAwait(true);
        await _persistWorkspaceStateAsync().ConfigureAwait(true);
        _project.LastExportFormat = format;
        _lastOutputPath = path;
        StatusText = $"Saved image to {path}";
        OpenContainingFolderCommand.RaiseCanExecuteChanged();

        if (quickSave && Settings.AutoCopyToClipboard)
        {
            _clipboardService.SetImage(_annotationRenderService.RenderToPng(current));
            StatusText = $"Quick saved and copied image: {path}";
        }
    }

    private void CopyToClipboard()
    {
        var bytes = _annotationRenderService.RenderToPng(BuildProjectSnapshot());
        _clipboardService.SetImage(bytes);
        StatusText = "Final image copied to clipboard.";
    }

    private void CopyDetectedText()
    {
        if (!HasExtractedText)
        {
            return;
        }

        _clipboardService.SetText(ExtractedText);
        StatusText = "Detected text copied to clipboard.";
    }

    private void CopySearchableText()
    {
        if (!HasCombinedSearchText)
        {
            return;
        }

        _clipboardService.SetText(CombinedSearchText);
        StatusText = "Combined searchable text copied to the clipboard.";
    }

    private void ShowDetectedText()
    {
        StatusText = HasExtractedText
            ? "Opened detected text."
            : "No text was detected. OCR uses the original captured pixels, so source-app zoom matters more than editor zoom.";
        DetectedTextRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenContainingFolder()
    {
        if (!string.IsNullOrWhiteSpace(_lastOutputPath))
        {
            _fileExportService.OpenContainingFolder(_lastOutputPath);
        }
    }

    private void PinToScreen()
    {
        var snapshot = BuildProjectSnapshot();
        var bytes = _annotationRenderService.RenderToPng(snapshot);
        PinToScreenRequested?.Invoke(this, new PinnedCaptureRequestEventArgs(bytes, snapshot.Name));
        StatusText = "Pinned the current capture to the screen.";
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(BuildProjectSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        StatusText = "Undo complete.";
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(BuildProjectSnapshot());
        RestoreSnapshot(_redoStack.Pop());
        StatusText = "Redo complete.";
    }

    private void RestoreSnapshot(ScreenshotProject snapshot)
    {
        var selectedId = SelectedAnnotation?.Id;
        _project = snapshot.Clone();
        BaseImage = DecodeBitmap(_project.OriginalImagePngBytes);
        Annotations = new ObservableCollection<AnnotationObject>(_project.Annotations.OrderBy(annotation => annotation.ZIndex));
        RefreshLayerAnnotations();
        _project.AnnotationText = string.IsNullOrWhiteSpace(_project.AnnotationText)
            ? AnnotationSearchTextBuilder.Build(_project.Annotations)
            : _project.AnnotationText;
        SelectedAnnotation = selectedId is null
            ? null
            : Annotations.FirstOrDefault(annotation => annotation.Id == selectedId);

        IsDirty = true;
        RaisePropertyChanged(nameof(ExtractedText));
        RaisePropertyChanged(nameof(HasExtractedText));
        RaisePropertyChanged(nameof(AnnotationText));
        RaisePropertyChanged(nameof(HasAnnotationText));
        RaisePropertyChanged(nameof(HasCombinedSearchText));
        RaisePropertyChanged(nameof(CombinedSearchText));
        RaisePropertyChanged(nameof(OcrReviewSummary));
        RaisePropertyChanged(nameof(OcrGuidanceText));
        RaisePropertyChanged(nameof(SearchIndexHeading));
        RaiseCommandStates();
        RaisePropertyChanged(nameof(DisplayTitle));
        InvalidateSurface();
    }

    private void DeleteSelection()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        CaptureUndoState();
        RemoveAnnotation(SelectedAnnotation);
    }

    private void DuplicateSelection()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        CaptureUndoState();
        var duplicate = SelectedAnnotation.Clone();
        duplicate.Id = Guid.NewGuid();
        duplicate.Move(18, 18);

        if (duplicate is StepAnnotation step)
        {
            step.Number = GetNextStepNumber();
        }

        duplicate.ZIndex = Annotations.Count == 0 ? 0 : Annotations.Max(annotation => annotation.ZIndex) + 1;
        Annotations.Add(duplicate);
        SortAnnotations();
        SelectedAnnotation = duplicate;
        SelectedTool = EditorTool.Select;
        MarkDirty("Annotation duplicated.");
    }

    private void BringForward()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        var ordered = Annotations.OrderBy(annotation => annotation.ZIndex).ToList();
        var index = ordered.IndexOf(SelectedAnnotation);
        if (index < 0 || index == ordered.Count - 1)
        {
            return;
        }

        CaptureUndoState();
        (ordered[index], ordered[index + 1]) = (ordered[index + 1], ordered[index]);
        Annotations = new ObservableCollection<AnnotationObject>(ordered);
        ReindexAnnotations();
        RefreshLayerAnnotations();
        MarkDirty("Annotation moved forward.");
        SelectedAnnotation = ordered[index + 1];
    }

    private void SendBackward()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        var ordered = Annotations.OrderBy(annotation => annotation.ZIndex).ToList();
        var index = ordered.IndexOf(SelectedAnnotation);
        if (index <= 0)
        {
            return;
        }

        CaptureUndoState();
        (ordered[index], ordered[index - 1]) = (ordered[index - 1], ordered[index]);
        Annotations = new ObservableCollection<AnnotationObject>(ordered);
        ReindexAnnotations();
        RefreshLayerAnnotations();
        MarkDirty("Annotation moved backward.");
        SelectedAnnotation = ordered[index - 1];
    }

    private void ApplyStyleStateChanges(bool isFill = false)
    {
        if (_isSyncingStyleState)
        {
            return;
        }

        var stroke = ArgbColor.FromHex(StrokeColorHex, Settings.StyleDefaults.StrokeColor);
        var fill = ArgbColor.FromHex(FillColorHex, Settings.StyleDefaults.FillColor);
        Settings.StyleDefaults.StrokeColor = stroke;
        Settings.StyleDefaults.FillColor = fill;
        Settings.StyleDefaults.StrokeThickness = StrokeThickness;
        Settings.StyleDefaults.Opacity = Opacity;
        Settings.StyleDefaults.FontSize = FontSize;
        Settings.StyleDefaults.BlurStrength = BlurStrength;
        UpsertRecentColor(isFill ? fill.ToHex() : stroke.ToHex());

        if (SelectedAnnotation is null)
        {
            InvalidateSurface();
            return;
        }

        CaptureUndoState();
        SelectedAnnotation.StrokeColor = stroke;
        SelectedAnnotation.FillColor = fill;
        SelectedAnnotation.StrokeThickness = StrokeThickness;
        SelectedAnnotation.Opacity = Opacity;

        if (SelectedAnnotation is TextAnnotation text)
        {
            text.FontSize = FontSize;
        }

        if (SelectedAnnotation is CalloutAnnotation callout)
        {
            callout.FontSize = FontSize;
        }

        if (SelectedAnnotation is StepAnnotation step)
        {
            step.FontSize = FontSize;
        }

        if (SelectedAnnotation is BlurAnnotation blur)
        {
            blur.Strength = BlurStrength;
        }

        MarkDirty("Style updated.");
    }

    private void ApplyStylePreset(StylePresetKind kind)
    {
        CurrentStylePreset = kind;
        Settings.DefaultStylePreset = kind;

        if (Settings.UseToolAwareDefaults)
        {
            ApplyRecommendedDefaultsForTool(SelectedTool, applyToSelection: SelectedAnnotation is not null);
            StatusText = $"Applied {StylePresetCatalog.Get(kind).DisplayName} preset.";
            return;
        }

        ApplyStyleState(BuildBaseDefaults(StylePresetCatalog.Get(kind)), applyToSelection: SelectedAnnotation is not null);
        StatusText = $"Applied {StylePresetCatalog.Get(kind).DisplayName} preset.";
    }

    private void ApplyRecommendedDefaultsForTool(EditorTool tool, bool applyToSelection)
    {
        var preset = StylePresetCatalog.Get(CurrentStylePreset);
        ApplyStyleState(BuildDefaultsForTool(tool, preset), applyToSelection);
    }

    private void ApplyStyleState(StyleDefaults defaults, bool applyToSelection)
    {
        _isSyncingStyleState = true;
        try
        {
            StrokeColorHex = defaults.StrokeColor.ToHex();
            FillColorHex = defaults.FillColor.ToHex();
            StrokeThickness = defaults.StrokeThickness;
            Opacity = defaults.Opacity;
            FontSize = defaults.FontSize;
            BlurStrength = defaults.BlurStrength;
        }
        finally
        {
            _isSyncingStyleState = false;
        }

        Settings.StyleDefaults = defaults.Clone();
        UpsertRecentColor(defaults.StrokeColor.ToHex());
        if (defaults.FillColor.A > 0)
        {
            UpsertRecentColor(defaults.FillColor.ToHex());
        }

        if (applyToSelection && SelectedAnnotation is not null)
        {
            ApplyStyleStateChanges(isFill: true);
        }
    }

    private static StyleDefaults BuildBaseDefaults(StylePresetDefinition preset) => new()
    {
        StrokeColor = preset.StrokeColor,
        FillColor = preset.FillColor,
        StrokeThickness = preset.StrokeThickness,
        Opacity = preset.Opacity,
        FontSize = preset.FontSize,
        BlurStrength = preset.BlurStrength
    };

    private static StyleDefaults BuildDefaultsForTool(EditorTool tool, StylePresetDefinition preset)
    {
        var defaults = BuildBaseDefaults(preset);

        switch (tool)
        {
            case EditorTool.Arrow:
                defaults.StrokeColor = preset.AccentColor;
                defaults.FillColor = ArgbColor.Transparent;
                defaults.StrokeThickness = Math.Max(4, preset.StrokeThickness + 0.5);
                break;
            case EditorTool.Rectangle:
            case EditorTool.Ellipse:
                defaults.StrokeColor = preset.AccentColor;
                defaults.FillColor = WithAlpha(preset.AccentColor, 18);
                defaults.StrokeThickness = Math.Max(2.2, preset.StrokeThickness);
                break;
            case EditorTool.Line:
                defaults.StrokeColor = preset.AccentColor;
                defaults.FillColor = ArgbColor.Transparent;
                defaults.StrokeThickness = Math.Max(3, preset.StrokeThickness);
                break;
            case EditorTool.Text:
                defaults.StrokeColor = ArgbColor.White;
                defaults.FillColor = new ArgbColor(196, 10, 15, 21);
                defaults.StrokeThickness = 0;
                defaults.FontSize = Math.Max(24, preset.FontSize);
                break;
            case EditorTool.Callout:
                defaults.StrokeColor = preset.AccentColor;
                defaults.FillColor = new ArgbColor(228, 12, 17, 24);
                defaults.StrokeThickness = Math.Max(2.5, preset.StrokeThickness - 0.5);
                defaults.FontSize = Math.Max(22, preset.FontSize);
                break;
            case EditorTool.Step:
                defaults.StrokeColor = ArgbColor.White;
                defaults.FillColor = preset.AccentColor;
                defaults.StrokeThickness = 3;
                defaults.FontSize = Math.Max(22, preset.FontSize - 2);
                break;
            case EditorTool.Highlight:
                defaults.StrokeColor = preset.AccentColor;
                defaults.FillColor = WithAlpha(preset.AccentColor, 104);
                defaults.StrokeThickness = 0;
                break;
            case EditorTool.Blur:
                defaults.StrokeColor = preset.AccentColor;
                defaults.FillColor = new ArgbColor(48, 255, 255, 255);
                defaults.BlurStrength = Math.Max(12, preset.BlurStrength);
                break;
            case EditorTool.Select:
            case EditorTool.Crop:
            default:
                break;
        }

        return defaults;
    }

    private void ApplyCurrentStyle(AnnotationObject annotation)
    {
        annotation.StrokeColor = ArgbColor.FromHex(StrokeColorHex, Settings.StyleDefaults.StrokeColor);
        annotation.FillColor = ArgbColor.FromHex(FillColorHex, Settings.StyleDefaults.FillColor);
        annotation.StrokeThickness = StrokeThickness;
        annotation.Opacity = Opacity;

        switch (annotation)
        {
            case TextAnnotation text:
                text.FontSize = FontSize;
                break;
            case CalloutAnnotation callout:
                callout.FontSize = FontSize;
                break;
            case StepAnnotation step:
                step.FontSize = FontSize;
                break;
            case BlurAnnotation blur:
                blur.Strength = BlurStrength;
                break;
            case ShapeAnnotation { ShapeKind: ShapeKind.Highlight } shape when shape.FillColor == ArgbColor.Transparent:
                shape.FillColor = ArgbColor.Highlight;
                break;
        }
    }

    private void SyncStylePropertiesFromSelection()
    {
        _isSyncingStyleState = true;
        try
        {
            if (SelectedAnnotation is null)
            {
                StrokeColorHex = Settings.StyleDefaults.StrokeColor.ToHex();
                FillColorHex = Settings.StyleDefaults.FillColor.ToHex();
                StrokeThickness = Settings.StyleDefaults.StrokeThickness;
                Opacity = Settings.StyleDefaults.Opacity;
                FontSize = Settings.StyleDefaults.FontSize;
                BlurStrength = Settings.StyleDefaults.BlurStrength;
                return;
            }

            StrokeColorHex = SelectedAnnotation.StrokeColor.ToHex();
            FillColorHex = SelectedAnnotation.FillColor.ToHex();
            StrokeThickness = SelectedAnnotation.StrokeThickness;
            Opacity = SelectedAnnotation.Opacity;
            FontSize = SelectedAnnotation switch
            {
                TextAnnotation text => text.FontSize,
                CalloutAnnotation callout => callout.FontSize,
                StepAnnotation step => step.FontSize,
                _ => Settings.StyleDefaults.FontSize
            };
            BlurStrength = SelectedAnnotation is BlurAnnotation blur ? blur.Strength : Settings.StyleDefaults.BlurStrength;
        }
        finally
        {
            _isSyncingStyleState = false;
        }
    }

    private void MarkDirty(string status)
    {
        _project.ModifiedAt = DateTimeOffset.Now;
        UpdateAnnotationSearchText();
        IsDirty = true;
        StatusText = status;
        RaisePropertyChanged(nameof(DisplayTitle));
        RaiseCommandStates();
        InvalidateSurface();
        ScheduleRecoverySave();
    }

    private void InvalidateSurface() => SurfaceInvalidated?.Invoke(this, EventArgs.Empty);

    private void ReindexAnnotations()
    {
        for (var i = 0; i < Annotations.Count; i++)
        {
            Annotations[i].ZIndex = i;
        }
    }

    private void SortAnnotations()
    {
        Annotations = new ObservableCollection<AnnotationObject>(Annotations.OrderBy(annotation => annotation.ZIndex));
        RefreshLayerAnnotations();
        ReindexAnnotations();
        InvalidateSurface();
    }

    private void RefreshLayerAnnotations()
    {
        LayerAnnotations = new ObservableCollection<AnnotationObject>(Annotations.OrderByDescending(annotation => annotation.ZIndex));
    }

    private static string GetDefaultLayerName(AnnotationObject annotation)
    {
        return annotation switch
        {
            ShapeAnnotation { ShapeKind: ShapeKind.Highlight } => "Highlight",
            ShapeAnnotation { ShapeKind: ShapeKind.Ellipse } => "Ellipse",
            ShapeAnnotation => "Rectangle",
            BlurAnnotation => "Pixelate",
            ArrowAnnotation => "Arrow",
            LineAnnotation => "Line",
            CalloutAnnotation => "Callout",
            TextAnnotation => "Text",
            StepAnnotation => "Step",
            _ => annotation.Kind.ToString()
        };
    }

    private void UpsertRecentColor(string hex)
    {
        hex = NormalizeHex(hex, ArgbColor.Accent);
        var existing = RecentColors.FirstOrDefault(color => color.Equals(hex, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecentColors.Remove(existing);
        }

        RecentColors.Insert(0, hex);
        while (RecentColors.Count > 8)
        {
            RecentColors.RemoveAt(RecentColors.Count - 1);
        }

        Settings.RecentColors = RecentColors.ToList();
    }

    private void RaiseCommandStates()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        DeleteSelectionCommand.RaiseCanExecuteChanged();
        DuplicateSelectionCommand.RaiseCanExecuteChanged();
        BringForwardCommand.RaiseCanExecuteChanged();
        SendBackwardCommand.RaiseCanExecuteChanged();
        ClearCropCommand.RaiseCanExecuteChanged();
        OpenContainingFolderCommand.RaiseCanExecuteChanged();
        CopyDetectedTextCommand.RaiseCanExecuteChanged();
        CopySearchableTextCommand.RaiseCanExecuteChanged();
    }

    private void ScheduleRecoverySave()
    {
        CancelPendingRecoverySave();
        var snapshot = BuildProjectSnapshot();
        var cts = new CancellationTokenSource();
        _recoverySaveCts = cts;
        _ = PersistRecoveryDraftAsync(snapshot, cts.Token);
    }

    private async Task PersistRecoveryDraftAsync(ScreenshotProject snapshot, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.4), cancellationToken).ConfigureAwait(true);
            await _recoveryDraftService.SaveDraftAsync(Settings, snapshot, cancellationToken).ConfigureAwait(true);
            await _persistWorkspaceStateAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void CancelPendingRecoverySave()
    {
        _recoverySaveCts?.Cancel();
        _recoverySaveCts?.Dispose();
        _recoverySaveCts = null;
    }

    private static string NormalizeHex(string? value, ArgbColor fallback)
    {
        return ArgbColor.FromHex(value, fallback).ToHex();
    }

    private void UpdateAnnotationSearchText()
    {
        _project.AnnotationText = AnnotationSearchTextBuilder.Build(Annotations);
        RaisePropertyChanged(nameof(AnnotationText));
        RaisePropertyChanged(nameof(HasAnnotationText));
        RaisePropertyChanged(nameof(HasCombinedSearchText));
        RaisePropertyChanged(nameof(CombinedSearchText));
        RaisePropertyChanged(nameof(OcrReviewSummary));
    }

    private string BuildCombinedSearchText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_project.ExtractedText))
        {
            parts.Add(_project.ExtractedText!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_project.AnnotationText))
        {
            parts.Add(_project.AnnotationText!.Trim());
        }

        return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private string BuildOcrReviewSummary()
    {
        var ocrLines = string.IsNullOrWhiteSpace(_project.ExtractedText)
            ? 0
            : _project.ExtractedText!.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length;
        var annotationEntries = string.IsNullOrWhiteSpace(_project.AnnotationText)
            ? 0
            : _project.AnnotationText!.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length;
        return $"{ocrLines} OCR line{(ocrLines == 1 ? string.Empty : "s")} | {annotationEntries} annotation entr{(annotationEntries == 1 ? "y" : "ies")}";
    }

    private string BuildOcrGuidanceText()
    {
        return HasExtractedText
            ? "Searchable text combines OCR output with annotation text so saved .coyote projects are easier to find later."
            : HasAnnotationText
                ? "OCR did not detect text from the captured image. The search index below is currently coming from your annotation text only. CaptureCoyote runs OCR on the original captured pixels, not the zoom level in the editor, so recapturing at a larger source zoom such as 125% usually works better."
                : "No OCR text was found. CaptureCoyote runs OCR on the original captured pixels, not the zoom level in the editor. If the source app was fit-to-page or heavily zoomed out, recapturing at a larger source zoom such as 125% usually works better.";
    }

    private static ArgbColor WithAlpha(ArgbColor color, byte alpha) => new(alpha, color.R, color.G, color.B);

    private static BitmapSource DecodeBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
