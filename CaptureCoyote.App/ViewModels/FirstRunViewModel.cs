using System.Collections.ObjectModel;
using System.Linq;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Mvvm;

namespace CaptureCoyote.App.ViewModels;

public sealed class FirstRunViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private bool _autoCopyToClipboard;
    private bool _autoOpenEditor;
    private bool _showSplashScreenOnLaunch;
    private bool _launchOnWindowsStartup;
    private bool _useToolAwareDefaults;
    private StylePresetKind _defaultStylePreset;

    public FirstRunViewModel(AppSettings settings)
    {
        _settings = settings;
        _autoCopyToClipboard = settings.AutoCopyToClipboard;
        _autoOpenEditor = settings.AutoOpenEditor;
        _showSplashScreenOnLaunch = settings.ShowSplashScreenOnLaunch;
        _launchOnWindowsStartup = settings.LaunchOnWindowsStartup;
        _useToolAwareDefaults = settings.UseToolAwareDefaults;
        _defaultStylePreset = settings.DefaultStylePreset;

        StylePresetOptions = new ObservableCollection<StylePresetOption>(StylePresetCatalog.All.Select(preset => new StylePresetOption
        {
            Kind = preset.Kind,
            DisplayName = preset.DisplayName,
            Description = preset.Description
        }));
    }

    public ObservableCollection<StylePresetOption> StylePresetOptions { get; }

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

    public bool UseToolAwareDefaults
    {
        get => _useToolAwareDefaults;
        set => SetProperty(ref _useToolAwareDefaults, value);
    }

    public StylePresetKind DefaultStylePreset
    {
        get => _defaultStylePreset;
        set => SetProperty(ref _defaultStylePreset, value);
    }

    public void ApplyToSettings()
    {
        _settings.AutoCopyToClipboard = AutoCopyToClipboard;
        _settings.AutoOpenEditor = AutoOpenEditor;
        _settings.ShowSplashScreenOnLaunch = ShowSplashScreenOnLaunch;
        _settings.LaunchOnWindowsStartup = LaunchOnWindowsStartup;
        _settings.HasConfiguredLaunchOnWindowsStartup = true;
        _settings.UseToolAwareDefaults = UseToolAwareDefaults;
        _settings.DefaultStylePreset = DefaultStylePreset;
        _settings.HasCompletedFirstRun = true;
    }
}
