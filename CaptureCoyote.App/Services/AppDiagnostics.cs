using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CaptureCoyote.App.Services;

public static class AppDiagnostics
{
    private static readonly object SyncRoot = new();
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];
    private static bool _sessionHeaderWritten;

    public static string LogDirectory
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaptureCoyote",
                "Logs");

            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"capturecoyote-{DateTime.Now:yyyy-MM-dd}.log");

    public static void LogSessionStart(string entryPoint)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var runtime = RuntimeInformation.FrameworkDescription;
        var os = RuntimeInformation.OSDescription;
        WriteEntry("INFO", $"Session start [{entryPoint}] | version={version} | runtime={runtime} | os={os}");
    }

    public static void LogInfo(string message) => WriteEntry("INFO", message);

    public static void LogWarning(string message) => WriteEntry("WARN", message);

    public static void LogException(string context, Exception exception)
    {
        var builder = new StringBuilder();
        builder.AppendLine(context);
        builder.AppendLine(exception.ToString());
        WriteEntry("ERROR", builder.ToString().TrimEnd());
    }

    public static void OpenLogsFolder()
    {
        Directory.CreateDirectory(LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{LogDirectory}\"",
            UseShellExecute = true
        });
    }

    private static void WriteEntry(string level, string message)
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(LogDirectory);
            using var writer = new StreamWriter(CurrentLogPath, append: true, Encoding.UTF8);
            if (!_sessionHeaderWritten)
            {
                writer.WriteLine($"----- Session {SessionId} started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} -----");
                _sessionHeaderWritten = true;
            }

            writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}");
        }
    }
}
