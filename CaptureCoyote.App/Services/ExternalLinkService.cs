using System.Diagnostics;

namespace CaptureCoyote.App.Services;

public static class ExternalLinkService
{
    public static bool TryOpen(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });

        return true;
    }
}
