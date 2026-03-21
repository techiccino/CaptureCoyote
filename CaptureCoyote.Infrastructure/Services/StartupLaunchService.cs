using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class StartupLaunchService : IStartupLaunchService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CaptureCoyote";
    private const string StartupArgument = "--startup";

    public void ApplySetting(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("CaptureCoyote could not open the current user's Windows startup registry key.");

        if (enabled)
        {
            runKey.SetValue(ValueName, BuildLaunchCommand(), RegistryValueKind.String);
            return;
        }

        runKey.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public bool IsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var launchCommand = runKey?.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(launchCommand))
        {
            return false;
        }

        return launchCommand.Contains(StartupArgument, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildLaunchCommand()
    {
        var executablePath = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("CaptureCoyote could not resolve its executable path for Windows startup registration.");
        }

        if (!Path.GetFileName(executablePath).Equals("CaptureCoyote.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"CaptureCoyote only registers startup for CaptureCoyote.exe. Current process path: {executablePath}");
        }

        return $"\"{executablePath}\" {StartupArgument}";
    }

    private static string? ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return entryAssemblyPath;
        }

        return Process.GetCurrentProcess().MainModule?.FileName;
    }
}
