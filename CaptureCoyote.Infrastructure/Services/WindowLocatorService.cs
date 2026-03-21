using System.Runtime.InteropServices;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Infrastructure.Interop;
using CaptureCoyote.Services.Abstractions;
using Forms = System.Windows.Forms;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class WindowLocatorService : IWindowLocatorService
{
    private readonly HashSet<string> _ignoredClasses =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd"
    ];

    public WindowDescriptor? GetWindowAt(PixelPoint point, IEnumerable<nint>? excludedHandles = null)
    {
        var excluded = excludedHandles?.ToHashSet() ?? [];
        var candidates = new Dictionary<nint, WindowDescriptor>();

        foreach (var candidate in EnumeratePointCandidates(point, excluded))
        {
            candidates[candidate.Handle] = candidate;
        }

        foreach (var candidate in EnumerateAllCandidates(point, excluded))
        {
            candidates[candidate.Handle] = candidate;
        }

        return candidates.Count == 0
            ? null
            : candidates.Values
                .OrderByDescending(candidate => ScoreWindow(candidate, point))
                .FirstOrDefault();
    }

    private IEnumerable<WindowDescriptor> EnumeratePointCandidates(PixelPoint point, HashSet<nint> excludedHandles)
    {
        var cursorPoint = new NativeMethods.POINT
        {
            X = (int)Math.Round(point.X),
            Y = (int)Math.Round(point.Y)
        };

        var current = NativeMethods.WindowFromPoint(cursorPoint);
        var visited = new HashSet<nint>();
        while (current != nint.Zero)
        {
            var root = NativeMethods.GetAncestor(current, NativeMethods.GA_ROOT);
            if (root == nint.Zero)
            {
                break;
            }

            if (visited.Add(root) && TryCreateWindowDescriptor(root, point, excludedHandles, out var descriptor))
            {
                yield return descriptor!;
            }

            current = NativeMethods.GetWindow(root, NativeMethods.GW_HWNDNEXT);
        }
    }

    private IEnumerable<WindowDescriptor> EnumerateAllCandidates(PixelPoint point, HashSet<nint> excludedHandles)
    {
        var visited = new HashSet<nint>();
        for (var hwnd = NativeMethods.GetTopWindow(nint.Zero); hwnd != nint.Zero; hwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT))
        {
            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root == nint.Zero || !visited.Add(root))
            {
                continue;
            }

            if (TryCreateWindowDescriptor(root, point, excludedHandles, out var descriptor))
            {
                yield return descriptor!;
            }
        }
    }

    private bool TryCreateWindowDescriptor(
        nint root,
        PixelPoint point,
        HashSet<nint> excludedHandles,
        out WindowDescriptor? descriptor)
    {
        descriptor = null;
        if (excludedHandles.Contains(root))
        {
            return false;
        }

        if (!NativeMethods.IsWindowVisible(root) || NativeMethods.IsIconic(root))
        {
            return false;
        }

        var className = NativeMethods.GetWindowClassName(root);
        if (_ignoredClasses.Contains(className))
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(root, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) == NativeMethods.WS_EX_TOOLWINDOW)
        {
            return false;
        }

        var cloaked = 0;
        _ = NativeMethods.DwmGetWindowAttribute(root, NativeMethods.DWMWA_CLOAKED, out cloaked, sizeof(int));
        if (cloaked != 0)
        {
            return false;
        }

        if (!TryGetWindowBounds(root, out var bounds) || !bounds.Contains(point))
        {
            return false;
        }

        if (!TryGetClientBounds(root, out var clientBounds))
        {
            clientBounds = bounds;
        }

        var title = NativeMethods.GetWindowTitle(root);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = className;
        }

        descriptor = new WindowDescriptor
        {
            Handle = root,
            Title = title,
            ClassName = className,
            Bounds = bounds,
            ClientBounds = clientBounds
        };
        return true;
    }

    private static double ScoreWindow(WindowDescriptor descriptor, PixelPoint point)
    {
        var score = 0d;

        if (!string.IsNullOrWhiteSpace(descriptor.Title) &&
            !string.Equals(descriptor.Title, descriptor.ClassName, StringComparison.Ordinal))
        {
            score += 18;
        }

        if (descriptor.ClientBounds.Contains(point))
        {
            score += 14;
        }

        var monitorBounds = Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(point.X), (int)Math.Round(point.Y))).Bounds;
        var monitorArea = Math.Max(1d, monitorBounds.Width * monitorBounds.Height);
        var windowArea = Math.Max(1d, descriptor.Bounds.Width * descriptor.Bounds.Height);
        var areaRatio = windowArea / monitorArea;

        if (areaRatio < 0.35)
        {
            score += 18;
        }
        else if (areaRatio < 0.6)
        {
            score += 12;
        }
        else if (areaRatio < 0.85)
        {
            score += 6;
        }
        else if (areaRatio > 0.97)
        {
            score -= 28;
        }
        else if (areaRatio > 0.9)
        {
            score -= 14;
        }

        var clientArea = Math.Max(1d, descriptor.ClientBounds.Width * descriptor.ClientBounds.Height);
        if (clientArea > 0)
        {
            score += Math.Min(10, clientArea / windowArea * 10);
        }

        var centerX = descriptor.ClientBounds.X + (descriptor.ClientBounds.Width / 2);
        var centerY = descriptor.ClientBounds.Y + (descriptor.ClientBounds.Height / 2);
        var distanceFromCenter = Math.Sqrt(Math.Pow(point.X - centerX, 2) + Math.Pow(point.Y - centerY, 2));
        var normalizedDistance = distanceFromCenter / Math.Max(1, Math.Max(descriptor.ClientBounds.Width, descriptor.ClientBounds.Height));
        score += Math.Max(0, 8 - (normalizedDistance * 12));

        if (descriptor.ClassName.Contains("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
        {
            score -= 6;
        }

        return score;
    }

    private static bool TryGetWindowBounds(nint hwnd, out PixelRect bounds)
    {
        bounds = PixelRect.Empty;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT rect, Marshal.SizeOf<NativeMethods.RECT>()) != 0)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out rect))
            {
                return false;
            }
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 6 || height <= 6)
        {
            return false;
        }

        bounds = new PixelRect(rect.Left, rect.Top, width, height);
        return true;
    }

    private static bool TryGetClientBounds(nint hwnd, out PixelRect bounds)
    {
        bounds = PixelRect.Empty;
        if (!NativeMethods.TryGetClientBounds(hwnd, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 6 || height <= 6)
        {
            return false;
        }

        bounds = new PixelRect(rect.Left, rect.Top, width, height);
        return true;
    }
}
