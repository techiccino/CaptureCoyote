using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Core.Models;

public sealed class AppSettings
{
    public string DefaultSaveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "CaptureCoyote");

    public string FileNamingPattern { get; set; } = "CaptureCoyote_{date}_{time}_{mode}";

    public ImageFileFormat PreferredImageFormat { get; set; } = ImageFileFormat.Png;

    public bool AutoCopyToClipboard { get; set; } = true;

    public bool AutoOpenEditor { get; set; } = true;

    public bool ShowSplashScreenOnLaunch { get; set; } = true;

    public bool LaunchOnWindowsStartup { get; set; } = true;

    public bool HasConfiguredLaunchOnWindowsStartup { get; set; }

    public int TemporarySnipRetentionDays { get; set; } = 30;

    public bool HasCompletedFirstRun { get; set; }

    public string? PreferredOcrLanguageTag { get; set; }

    public StylePresetKind DefaultStylePreset { get; set; } = StylePresetKind.Documentation;

    public bool UseToolAwareDefaults { get; set; } = true;

    public CaptureMode LastCaptureMode { get; set; } = CaptureMode.Region;

    public EditorTool LastUsedTool { get; set; } = EditorTool.Select;

    public int LastDelaySeconds { get; set; }

    public StyleDefaults StyleDefaults { get; set; } = StyleDefaults.CreateDefault();

    public List<string> RecentColors { get; set; } =
    [
        "#FF1884C4",
        "#FFE05050",
        "#FFF5A623",
        "#FF2CB67D",
        "#FFFFFFFF",
        "#FF11151C"
    ];

    public List<HotkeyBinding> Hotkeys { get; set; } =
    [
        new() { Name = "Region capture", Mode = CaptureMode.Region, VirtualKey = 0x34, KeyLabel = "4", Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift },
        new() { Name = "Window capture", Mode = CaptureMode.Window, VirtualKey = 0x35, KeyLabel = "5", Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift },
        new() { Name = "Full-screen capture", Mode = CaptureMode.FullScreen, VirtualKey = 0x36, KeyLabel = "6", Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift }
    ];

    public List<RecentWorkspaceItem> RecentItems { get; set; } = [];

    public List<RecoveryDraftInfo> RecoveryDrafts { get; set; } = [];

    public static AppSettings CreateDefault() => new();
}
