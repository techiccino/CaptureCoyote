using System.Drawing;
using System.IO;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Infrastructure.Branding;
using Forms = System.Windows.Forms;

namespace CaptureCoyote.App.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;

    public TrayIconService(
        Func<Task> showLauncherAsync,
        Func<CaptureMode, Task> captureAsync,
        Func<Task> openProjectAsync,
        Func<Task> openSettingsAsync,
        Action exitApplication)
    {
        _trayIcon = LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "CaptureCoyote",
            Icon = _trayIcon,
            Visible = true,
            ContextMenuStrip = BuildMenu(showLauncherAsync, captureAsync, openProjectAsync, openSettingsAsync, exitApplication)
        };

        _notifyIcon.DoubleClick += async (_, _) => await showLauncherAsync().ConfigureAwait(false);
    }

    public void ShowInfo(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
    }

    private static Forms.ContextMenuStrip BuildMenu(
        Func<Task> showLauncherAsync,
        Func<CaptureMode, Task> captureAsync,
        Func<Task> openProjectAsync,
        Func<Task> openSettingsAsync,
        Action exitApplication)
    {
        var menu = new Forms.ContextMenuStrip();

        var openLauncher = new Forms.ToolStripMenuItem("Open CaptureCoyote");
        openLauncher.Click += async (_, _) => await showLauncherAsync().ConfigureAwait(false);

        var regionCapture = new Forms.ToolStripMenuItem("Region Capture");
        regionCapture.Click += async (_, _) => await captureAsync(CaptureMode.Region).ConfigureAwait(false);

        var windowCapture = new Forms.ToolStripMenuItem("Window Capture");
        windowCapture.Click += async (_, _) => await captureAsync(CaptureMode.Window).ConfigureAwait(false);

        var scrollingCapture = new Forms.ToolStripMenuItem("Scrolling Capture");
        scrollingCapture.Click += async (_, _) => await captureAsync(CaptureMode.Scrolling).ConfigureAwait(false);

        var fullScreenCapture = new Forms.ToolStripMenuItem("Full-Screen Capture");
        fullScreenCapture.Click += async (_, _) => await captureAsync(CaptureMode.FullScreen).ConfigureAwait(false);

        var openProject = new Forms.ToolStripMenuItem("Open Editable Project");
        openProject.Click += async (_, _) => await openProjectAsync().ConfigureAwait(false);

        var settings = new Forms.ToolStripMenuItem("Settings");
        settings.Click += async (_, _) => await openSettingsAsync().ConfigureAwait(false);

        var exit = new Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => exitApplication();

        menu.Items.Add(openLauncher);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(regionCapture);
        menu.Items.Add(windowCapture);
        menu.Items.Add(scrollingCapture);
        menu.Items.Add(fullScreenCapture);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(openProject);
        menu.Items.Add(settings);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);

        return menu;
    }

    private static Icon LoadTrayIcon()
    {
        var path = Path.Combine(BrandingAssets.AssetsDirectory, BrandingAssets.IconFileName);
        return File.Exists(path)
            ? new Icon(path)
            : (Icon)SystemIcons.Application.Clone();
    }
}
