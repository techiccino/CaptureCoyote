using System.IO;
using System.Windows;
using CaptureCoyote.App.Services;
using CaptureCoyote.App.ViewModels;
using CaptureCoyote.App.Views;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Infrastructure.Services;
using CaptureCoyote.Services.Abstractions;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace CaptureCoyote.App;

public partial class App : System.Windows.Application
{
    private CaptureCoyoteContext? _context;
    private SingleInstanceService? _singleInstanceService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();
        AppDiagnostics.LogSessionStart("startup");

        try
        {
            _singleInstanceService = new SingleInstanceService("CaptureCoyote");
            if (!_singleInstanceService.IsPrimaryInstance)
            {
                var forwarded = await _singleInstanceService.SignalPrimaryInstanceAsync(e.Args).ConfigureAwait(true);
                if (!forwarded)
                {
                    AppDiagnostics.LogWarning("Secondary launch could not forward activation to the primary instance.");
                }

                Shutdown();
                return;
            }

            _singleInstanceService.ActivationRequested += HandleSingleInstanceActivationAsync;
            _singleInstanceService.StartListening();

            base.OnStartup(e);

            var packagedActivation = TryGetPackagedActivationInfo();
            var startupProjectPath = ResolveStartupProjectPath(e.Args, packagedActivation?.FilePath);
            var isStartupLaunch = packagedActivation?.IsStartupTask == true ||
                                  (string.IsNullOrWhiteSpace(startupProjectPath) && ContainsStartupLaunchFlag(e.Args));
            if (!string.IsNullOrWhiteSpace(startupProjectPath))
            {
                AppDiagnostics.LogInfo($"Startup project path detected: {startupProjectPath}");
            }
            else if (isStartupLaunch)
            {
                AppDiagnostics.LogInfo("Windows startup launch detected.");
            }

            var settingsService = new SettingsService();
            var settings = await LoadSettingsWithFallbackAsync(settingsService).ConfigureAwait(true);
            ApplyStartupPreferenceMigration(settings);
            var screenCaptureService = new ScreenCaptureService(new MonitorService());
            var scrollingCaptureService = new ScrollingCaptureService();
            var projectSerializationService = new ProjectSerializationService();
            var annotationRenderService = new AnnotationRenderService();
            var startupLaunchService = new StartupLaunchService();
            SplashWindow? splashWindow = null;

            if (settings.ShowSplashScreenOnLaunch && !isStartupLaunch)
            {
                splashWindow = new SplashWindow();
                MainWindow = splashWindow;
                splashWindow.Show();
            }

            _context = new CaptureCoyoteContext(
                settings,
                settingsService,
                screenCaptureService,
                scrollingCaptureService,
                new WindowLocatorService(),
                new HotkeyService(),
                new ClipboardService(),
                new FileExportService(),
                projectSerializationService,
                new RecentWorkspaceService(projectSerializationService, annotationRenderService),
                new ProjectLibraryService(projectSerializationService, annotationRenderService),
                new RecoveryDraftService(projectSerializationService, annotationRenderService),
                startupLaunchService,
                new WindowsOcrService(),
                annotationRenderService,
                new FileDialogService());

            TryApplyWindowsStartupPreference(startupLaunchService, settings);

            if (splashWindow is not null)
            {
                await Task.Delay(1100).ConfigureAwait(true);
            }

            var mainWindow = new MainWindow(_context);
            MainWindow = mainWindow;
            if (isStartupLaunch)
            {
                mainWindow.StartHiddenInTray();
            }
            else
            {
                mainWindow.Show();
            }

            splashWindow?.Close();

            if (!string.IsNullOrWhiteSpace(startupProjectPath))
            {
                await mainWindow.OpenProjectPathAsync(startupProjectPath).ConfigureAwait(true);
                return;
            }

            if (!settings.HasCompletedFirstRun && !isStartupLaunch)
            {
                var firstRunViewModel = new FirstRunViewModel(settings);
                var firstRunWindow = new FirstRunWindow(firstRunViewModel)
                {
                    Owner = mainWindow
                };

                _ = firstRunWindow.ShowDialog();
                firstRunViewModel.ApplyToSettings();
                TryApplyWindowsStartupPreference(startupLaunchService, settings);
                await _context.SaveSettingsAsync().ConfigureAwait(true);
            }

            AppDiagnostics.LogInfo("Startup completed successfully.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException("Fatal startup failure.", ex);
            ShowFatalError(
                "CaptureCoyote could not start.",
                "CaptureCoyote ran into a startup problem. A local log was written and can help troubleshoot the issue.");
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_context is not null)
        {
            await _context.SaveSettingsAsync().ConfigureAwait(true);
            _context.HotkeyService.Dispose();
        }

        if (_singleInstanceService is not null)
        {
            _singleInstanceService.ActivationRequested -= HandleSingleInstanceActivationAsync;
            _singleInstanceService.Dispose();
            _singleInstanceService = null;
        }

        AppDiagnostics.LogInfo($"Application exit with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private async Task<AppSettings> LoadSettingsWithFallbackAsync(SettingsService settingsService)
    {
        try
        {
            return await settingsService.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException("Settings load failed. Falling back to defaults.", ex);
            System.Windows.MessageBox.Show(
                $"CaptureCoyote could not read the saved settings file, so it reset to safe defaults.\n\nLog: {AppDiagnostics.CurrentLogPath}",
                "Settings Reset",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return AppSettings.CreateDefault();
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppDiagnostics.LogException("Unhandled UI exception.", args.Exception);
            args.Handled = true;
            ShowFatalError(
                "CaptureCoyote hit an unexpected error.",
                "The app will close so it can start cleanly next time.");
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppDiagnostics.LogException("Unhandled AppDomain exception.", exception);
            }
            else
            {
                AppDiagnostics.LogWarning($"Unhandled AppDomain exception object: {args.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnostics.LogException("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }

    private static void ShowFatalError(string title, string message)
    {
        System.Windows.MessageBox.Show(
            $"{message}\n\nLog: {AppDiagnostics.CurrentLogPath}",
            title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }

    private static string? ResolveStartupProjectPath(string[] args, string? packagedActivationPath = null)
    {
        if (!string.IsNullOrWhiteSpace(packagedActivationPath) && File.Exists(packagedActivationPath))
        {
            return Path.GetExtension(packagedActivationPath).Equals(".coyote", StringComparison.OrdinalIgnoreCase)
                ? packagedActivationPath
                : null;
        }

        if (args.Length == 0)
        {
            return null;
        }

        var candidate = args.FirstOrDefault(arg =>
            !string.IsNullOrWhiteSpace(arg) &&
            !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(arg));

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        return Path.GetExtension(candidate).Equals(".coyote", StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static bool ContainsStartupLaunchFlag(IEnumerable<string> args)
    {
        return args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
    }

    private static PackagedActivationInfo? TryGetPackagedActivationInfo()
    {
        if (!IsPackaged())
        {
            return null;
        }

        try
        {
            var args = AppInstance.GetActivatedEventArgs();
            return args.Kind switch
            {
                ActivationKind.File => new PackagedActivationInfo(ResolvePackagedFilePath(args as FileActivatedEventArgs), false),
                ActivationKind.StartupTask => new PackagedActivationInfo(null, true),
                _ => null
            };
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException("Could not inspect packaged activation info.", ex);
            return null;
        }
    }

    private static string? ResolvePackagedFilePath(FileActivatedEventArgs? args)
    {
        return args?.Files?
            .OfType<IStorageItem>()
            .Select(item => item.Path)
            .FirstOrDefault(path => Path.GetExtension(path).Equals(".coyote", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyStartupPreferenceMigration(AppSettings settings)
    {
        if (settings.HasConfiguredLaunchOnWindowsStartup || !settings.HasCompletedFirstRun)
        {
            return;
        }

        settings.LaunchOnWindowsStartup = false;
        settings.HasConfiguredLaunchOnWindowsStartup = true;
    }

    private static void TryApplyWindowsStartupPreference(IStartupLaunchService startupLaunchService, AppSettings settings)
    {
        try
        {
            startupLaunchService.ApplySetting(settings.LaunchOnWindowsStartup);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogException("Could not sync the Windows startup preference.", ex);
        }
    }

    private async Task HandleSingleInstanceActivationAsync(string[] args)
    {
        var startupProjectPath = ResolveStartupProjectPath(args);
        var isStartupLaunch = string.IsNullOrWhiteSpace(startupProjectPath) && ContainsStartupLaunchFlag(args);

        await Dispatcher.InvokeAsync(async () =>
        {
            if (MainWindow is not MainWindow mainWindow)
            {
                return;
            }

            if (!isStartupLaunch)
            {
                mainWindow.ShowAndActivateMainWindow();
            }

            if (!string.IsNullOrWhiteSpace(startupProjectPath))
            {
                await mainWindow.OpenProjectPathAsync(startupProjectPath).ConfigureAwait(true);
            }
        }).Task.Unwrap();
    }

    private sealed record PackagedActivationInfo(string? FilePath, bool IsStartupTask);
}
