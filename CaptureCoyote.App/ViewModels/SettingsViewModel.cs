using System.Collections.ObjectModel;
using CaptureCoyote.App.Services;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Mvvm;

namespace CaptureCoyote.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _target;
    private string _defaultSaveFolder;
    private string _fileNamingPattern;
    private ImageFileFormat _preferredImageFormat;
    private bool _autoCopyToClipboard;
    private bool _autoOpenEditor;
    private bool _showSplashScreenOnLaunch;
    private bool _launchOnWindowsStartup;
    private int _temporarySnipRetentionDays;
    private StylePresetKind _defaultStylePreset;
    private bool _useToolAwareDefaults;
    private bool _saved;

    public SettingsViewModel(AppSettings settings)
    {
        _target = settings;
        _defaultSaveFolder = settings.DefaultSaveFolder;
        _fileNamingPattern = settings.FileNamingPattern;
        _preferredImageFormat = settings.PreferredImageFormat;
        _autoCopyToClipboard = settings.AutoCopyToClipboard;
        _autoOpenEditor = settings.AutoOpenEditor;
        _showSplashScreenOnLaunch = settings.ShowSplashScreenOnLaunch;
        _launchOnWindowsStartup = settings.LaunchOnWindowsStartup;
        _temporarySnipRetentionDays = settings.TemporarySnipRetentionDays;
        _defaultStylePreset = settings.DefaultStylePreset;
        _useToolAwareDefaults = settings.UseToolAwareDefaults;

        Hotkeys = new ObservableCollection<HotkeyBinding>(settings.Hotkeys.Select(binding => new HotkeyBinding
        {
            Name = binding.Name,
            Mode = binding.Mode,
            VirtualKey = binding.VirtualKey,
            KeyLabel = binding.KeyLabel,
            Modifiers = binding.Modifiers
        }));

        AvailableKeys = CreateAvailableKeys();
        RetentionOptions = new ObservableCollection<RetentionOption>(
            [
                new() { Label = "14 days", Days = 14 },
                new() { Label = "30 days", Days = 30 },
                new() { Label = "60 days", Days = 60 },
                new() { Label = "90 days", Days = 90 },
                new() { Label = "Never hint me", Days = 0 }
            ]);
        StylePresetOptions = new ObservableCollection<StylePresetOption>(StylePresetCatalog.All.Select(preset => new StylePresetOption
        {
            Kind = preset.Kind,
            DisplayName = preset.DisplayName,
            Description = preset.Description
        }));
        OpenLogsFolderCommand = new RelayCommand(AppDiagnostics.OpenLogsFolder);
        OpenSupportLinkCommand = new RelayCommand(OpenSupportLink, () => HasSupportLink);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<HotkeyBinding> Hotkeys { get; }

    public ObservableCollection<HotkeyKeyOption> AvailableKeys { get; }

    public ObservableCollection<RetentionOption> RetentionOptions { get; }

    public ObservableCollection<StylePresetOption> StylePresetOptions { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand OpenLogsFolderCommand { get; }

    public RelayCommand OpenSupportLinkCommand { get; }

    public bool HasSupportLink => AppLinks.HasSupportUrl;

    public bool Saved
    {
        get => _saved;
        private set => SetProperty(ref _saved, value);
    }

    public string DefaultSaveFolder
    {
        get => _defaultSaveFolder;
        set => SetProperty(ref _defaultSaveFolder, value);
    }

    public string FileNamingPattern
    {
        get => _fileNamingPattern;
        set => SetProperty(ref _fileNamingPattern, value);
    }

    public ImageFileFormat PreferredImageFormat
    {
        get => _preferredImageFormat;
        set => SetProperty(ref _preferredImageFormat, value);
    }

    public bool AutoCopyToClipboard
    {
        get => _autoCopyToClipboard;
        set => SetProperty(ref _autoCopyToClipboard, value);
    }

    public bool AutoOpenEditor
    {
        get => _autoOpenEditor;
        set => SetProperty(ref _autoOpenEditor, value);
    }

    public bool ShowSplashScreenOnLaunch
    {
        get => _showSplashScreenOnLaunch;
        set => SetProperty(ref _showSplashScreenOnLaunch, value);
    }

    public bool LaunchOnWindowsStartup
    {
        get => _launchOnWindowsStartup;
        set => SetProperty(ref _launchOnWindowsStartup, value);
    }

    public int TemporarySnipRetentionDays
    {
        get => _temporarySnipRetentionDays;
        set => SetProperty(ref _temporarySnipRetentionDays, value);
    }

    public StylePresetKind DefaultStylePreset
    {
        get => _defaultStylePreset;
        set => SetProperty(ref _defaultStylePreset, value);
    }

    public bool UseToolAwareDefaults
    {
        get => _useToolAwareDefaults;
        set => SetProperty(ref _useToolAwareDefaults, value);
    }

    private void Save()
    {
        foreach (var hotkey in Hotkeys)
        {
            hotkey.KeyLabel = AvailableKeys.FirstOrDefault(option => option.VirtualKey == hotkey.VirtualKey)?.Label ?? hotkey.KeyLabel;
            hotkey.Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift;
        }

        _target.DefaultSaveFolder = DefaultSaveFolder;
        _target.FileNamingPattern = FileNamingPattern;
        _target.PreferredImageFormat = PreferredImageFormat;
        _target.AutoCopyToClipboard = AutoCopyToClipboard;
        _target.AutoOpenEditor = AutoOpenEditor;
        _target.ShowSplashScreenOnLaunch = ShowSplashScreenOnLaunch;
        _target.LaunchOnWindowsStartup = LaunchOnWindowsStartup;
        _target.HasConfiguredLaunchOnWindowsStartup = true;
        _target.TemporarySnipRetentionDays = TemporarySnipRetentionDays;
        _target.DefaultStylePreset = DefaultStylePreset;
        _target.UseToolAwareDefaults = UseToolAwareDefaults;
        _target.Hotkeys = Hotkeys.ToList();
        Saved = true;
        RequestClose?.Invoke(this, true);
    }

    private void Cancel()
    {
        Saved = false;
        RequestClose?.Invoke(this, false);
    }

    private void OpenSupportLink()
    {
        ExternalLinkService.TryOpen(AppLinks.SupportUrl);
    }

    private static ObservableCollection<HotkeyKeyOption> CreateAvailableKeys()
    {
        var keys = new List<HotkeyKeyOption>();
        for (var value = 0x30; value <= 0x39; value++)
        {
            keys.Add(new HotkeyKeyOption { Label = ((char)value).ToString(), VirtualKey = value });
        }

        for (var value = 0x41; value <= 0x5A; value++)
        {
            keys.Add(new HotkeyKeyOption { Label = ((char)value).ToString(), VirtualKey = value });
        }

        for (var value = 0; value < 12; value++)
        {
            keys.Add(new HotkeyKeyOption { Label = $"F{value + 1}", VirtualKey = 0x70 + value });
        }

        return new ObservableCollection<HotkeyKeyOption>(keys);
    }
}
