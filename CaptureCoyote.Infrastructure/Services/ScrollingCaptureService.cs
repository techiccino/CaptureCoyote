using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Infrastructure.Interop;
using CaptureCoyote.Services.Abstractions;
using Forms = System.Windows.Forms;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class ScrollingCaptureService : IScrollingCaptureService
{
    private const int MaxFrames = 18;
    private const int MinOverlap = 56;
    private const int MinNewContentHeight = 34;
    private const int MaxAcceptedDifference = 22;
    private const int InitialFocusDelayMs = 220;
    private const int CaptureSettleDelayMs = 360;
    private const int OverlapStep = 4;
    private const int MaxStickyTopTrim = 180;
    private const int MaxStickyBottomTrim = 160;
    private const double StickyTopDifferenceThreshold = 7.25;
    private const double StickyBottomDifferenceThreshold = 8.5;

    public async Task<CaptureResult?> CaptureScrollingWindowAsync(
        WindowDescriptor window,
        CaptureContext context,
        CancellationToken cancellationToken = default)
    {
        if (window.Handle == nint.Zero)
        {
            return null;
        }

        BringWindowToFront(window.Handle);
        var repositionState = CenterWindowForScrollCapture(window.Handle);
        BringWindowToFront(window.Handle);
        await Task.Delay(InitialFocusDelayMs, cancellationToken).ConfigureAwait(false);
        ScrollToTop(window);
        await Task.Delay(GetSettleDelay(window), cancellationToken).ConfigureAwait(false);

        BringWindowToFront(window.Handle);
        using var initialFrame = TryCaptureFrame(window.Handle, out var initialBounds);
        if (initialFrame is null)
        {
            return null;
        }

        var frames = new List<ScrollFrameCapture>
        {
            new((Bitmap)initialFrame.Clone(), stickyTopTrim: 0, stickyBottomTrim: 0, overlapWithPrevious: 0)
        };

        try
        {
            for (var step = 1; step < MaxFrames; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BringWindowToFront(window.Handle);
                AdvanceScroll(window);
                await Task.Delay(GetSettleDelay(window), cancellationToken).ConfigureAwait(false);

                BringWindowToFront(window.Handle);
                using var nextFrame = TryCaptureFrame(window.Handle, out _);
                if (nextFrame is null)
                {
                    break;
                }

                var previous = frames[^1].Bitmap;
                var stickyTopTrim = DetermineStickyTopTrim(previous, nextFrame);
                var stickyBottomTrim = DetermineStickyBottomTrim(previous, nextFrame);
                if (MeasureFrameDifference(previous, nextFrame, stickyTopTrim, stickyBottomTrim) < 4.5)
                {
                    break;
                }

                var previousBottomTrim = Math.Max(frames[^1].StickyBottomTrim, stickyBottomTrim);
                var overlap = FindBestOverlap(
                    previous,
                    nextFrame,
                    Math.Min(previous.Width, nextFrame.Width),
                    stickyTopTrim,
                    previousBottomTrim,
                    stickyBottomTrim);
                var newContentHeight = nextFrame.Height - stickyTopTrim - stickyBottomTrim - overlap;
                if (newContentHeight < MinNewContentHeight)
                {
                    break;
                }

                frames[^1].StickyBottomTrim = previousBottomTrim;
                frames.Add(new ScrollFrameCapture((Bitmap)nextFrame.Clone(), stickyTopTrim, stickyBottomTrim, overlap));
            }

            using var stitched = StitchFrames(frames);
            using var stream = new MemoryStream();
            stitched.Save(stream, ImageFormat.Png);

            context.SourceBounds = initialBounds;
            context.ScrollFrameCount = frames.Count;
            return new CaptureResult
            {
                ImagePngBytes = stream.ToArray(),
                PixelWidth = stitched.Width,
                PixelHeight = stitched.Height,
                Context = context
            };
        }
        finally
        {
            RestoreWindowPosition(repositionState);

            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private static void BringWindowToFront(nint handle)
    {
        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        NativeMethods.SetWindowPos(
            handle,
            nint.Zero,
            0,
            0,
            0,
            0,
            (uint)(NativeMethods.SetWindowPosFlags.NoMove |
                   NativeMethods.SetWindowPosFlags.NoSize |
                   NativeMethods.SetWindowPosFlags.ShowWindow));
        NativeMethods.SetForegroundWindow(handle);
    }

    private static WindowRepositionState CenterWindowForScrollCapture(nint handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            return WindowRepositionState.None;
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var screen = Forms.Screen.FromHandle(handle).WorkingArea;

        var targetLeft = screen.Left + Math.Max(0, (screen.Width - width) / 2);
        var targetTop = screen.Top + Math.Max(0, (screen.Height - height) / 2);

        if (width > screen.Width)
        {
            targetLeft = screen.Left;
        }
        else
        {
            targetLeft = Math.Clamp(targetLeft, screen.Left, screen.Right - width);
        }

        if (height > screen.Height)
        {
            targetTop = screen.Top;
        }
        else
        {
            targetTop = Math.Clamp(targetTop, screen.Top, screen.Bottom - height);
        }

        if (rect.Left == targetLeft && rect.Top == targetTop)
        {
            return WindowRepositionState.None;
        }

        NativeMethods.SetWindowPos(
            handle,
            nint.Zero,
            targetLeft,
            targetTop,
            width,
            height,
            (uint)NativeMethods.SetWindowPosFlags.ShowWindow);

        return new WindowRepositionState(handle, rect.Left, rect.Top, width, height, true);
    }

    private static void RestoreWindowPosition(WindowRepositionState state)
    {
        if (!state.Moved)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            state.Handle,
            nint.Zero,
            state.Left,
            state.Top,
            state.Width,
            state.Height,
            (uint)(NativeMethods.SetWindowPosFlags.NoZOrder | NativeMethods.SetWindowPosFlags.ShowWindow));
    }

    private static void SendPageDown()
    {
        NativeMethods.keybd_event(NativeMethods.VK_NEXT, 0, 0, 0);
        NativeMethods.keybd_event(NativeMethods.VK_NEXT, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
    }

    private static void ScrollToTop(WindowDescriptor window)
    {
        if (IsWordLikeWindow(window))
        {
            SendCtrlHome();
            return;
        }

        NativeMethods.keybd_event(NativeMethods.VK_HOME, 0, 0, 0);
        NativeMethods.keybd_event(NativeMethods.VK_HOME, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
    }

    private static void AdvanceScroll(WindowDescriptor window)
    {
        SendPageDown();
    }

    private static Bitmap? TryCaptureFrame(nint handle, out PixelRect bounds)
    {
        bounds = PixelRect.Empty;
        if (!NativeMethods.TryGetClientBounds(handle, out var clientRect))
        {
            return null;
        }

        var width = Math.Max(1, clientRect.Right - clientRect.Left);
        var height = Math.Max(1, clientRect.Bottom - clientRect.Top);
        bounds = new PixelRect(clientRect.Left, clientRect.Top, width, height);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(clientRect.Left, clientRect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static Bitmap StitchFrames(IReadOnlyList<ScrollFrameCapture> frames)
    {
        if (frames.Count == 1)
        {
            return (Bitmap)frames[0].Bitmap.Clone();
        }

        var width = frames.Min(frame => frame.Bitmap.Width);
        var totalHeight = GetRenderedHeight(frames[0], isFirstFrame: true);

        for (var index = 1; index < frames.Count; index++)
        {
            var frame = frames[index];
            totalHeight += GetRenderedHeight(frame, isFirstFrame: false);
        }

        var stitched = new Bitmap(width, totalHeight, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(stitched);
        graphics.Clear(Color.Transparent);

        var firstFrame = frames[0].Bitmap;
        var firstHeight = GetRenderedHeight(frames[0], isFirstFrame: true);
        graphics.DrawImage(
            firstFrame,
            new Rectangle(0, 0, width, firstHeight),
            new Rectangle(0, 0, width, firstHeight),
            GraphicsUnit.Pixel);

        var offsetY = firstHeight;
        for (var index = 1; index < frames.Count; index++)
        {
            var frame = frames[index];
            var sourceY = Math.Min(
                frame.Bitmap.Height - 1,
                frame.StickyTopTrim + frame.OverlapWithPrevious);
            var maxBottom = Math.Max(sourceY + 1, frame.Bitmap.Height - frame.StickyBottomTrim);
            var drawHeight = Math.Max(1, maxBottom - sourceY);
            graphics.DrawImage(
                frame.Bitmap,
                new Rectangle(0, offsetY, width, drawHeight),
                new Rectangle(0, sourceY, width, drawHeight),
                GraphicsUnit.Pixel);
            offsetY += drawHeight;
        }

        return stitched;
    }

    private static int DetermineStickyTopTrim(Bitmap previous, Bitmap current)
    {
        var maxTrim = Math.Min(MaxStickyTopTrim, Math.Min(previous.Height, current.Height) / 4);
        if (maxTrim < 24)
        {
            return 0;
        }

        var contiguousRows = 0;
        var bestTrim = 0;
        for (var y = 0; y < maxTrim; y += 3)
        {
            var difference = CompareHorizontalBand(previous, current, y, y);
            if (difference <= StickyTopDifferenceThreshold)
            {
                contiguousRows += 3;
                bestTrim = y + 3;
            }
            else if (contiguousRows >= 18)
            {
                break;
            }
            else
            {
                contiguousRows = 0;
                bestTrim = 0;
            }
        }

        return Math.Clamp(bestTrim, 0, maxTrim);
    }

    private static int DetermineStickyBottomTrim(Bitmap previous, Bitmap current)
    {
        var maxTrim = Math.Min(MaxStickyBottomTrim, Math.Min(previous.Height, current.Height) / 4);
        if (maxTrim < 20)
        {
            return 0;
        }

        var contiguousRows = 0;
        var bestTrim = 0;
        for (var offset = 0; offset < maxTrim; offset += 3)
        {
            var previousY = previous.Height - 1 - offset;
            var currentY = current.Height - 1 - offset;
            var difference = CompareHorizontalBand(previous, current, previousY, currentY);
            if (difference <= StickyBottomDifferenceThreshold)
            {
                contiguousRows += 3;
                bestTrim = offset + 3;
            }
            else if (contiguousRows >= 18)
            {
                break;
            }
            else
            {
                contiguousRows = 0;
                bestTrim = 0;
            }
        }

        return Math.Clamp(bestTrim, 0, maxTrim);
    }

    private static int FindBestOverlap(
        Bitmap previous,
        Bitmap current,
        int width,
        int currentTopTrim,
        int previousBottomTrim,
        int currentBottomTrim)
    {
        var previousContentHeight = previous.Height - previousBottomTrim;
        var currentContentHeight = current.Height - currentTopTrim - currentBottomTrim;
        var maxOverlap = Math.Min(previousContentHeight, currentContentHeight) - 24;
        if (maxOverlap <= MinOverlap)
        {
            return 0;
        }

        var bestOverlap = 0;
        var bestDifference = double.MaxValue;

        for (var overlap = MinOverlap; overlap <= maxOverlap; overlap += OverlapStep)
        {
            var difference = CompareOverlap(
                previous,
                current,
                width,
                overlap,
                currentTopTrim,
                previousBottomTrim);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                bestOverlap = overlap;
            }
        }

        return bestDifference <= MaxAcceptedDifference
            ? bestOverlap
            : 0;
    }

    private static double CompareOverlap(
        Bitmap previous,
        Bitmap current,
        int width,
        int overlap,
        int currentTopTrim,
        int previousBottomTrim)
    {
        var sampleLeft = Math.Max(18, width / 9);
        var sampleRight = Math.Max(sampleLeft + 1, width - sampleLeft);
        var stepX = Math.Max(8, width / 34);
        var stepY = Math.Max(6, overlap / 28);
        double totalDifference = 0;
        var sampleCount = 0;

        for (var y = 0; y < overlap; y += stepY)
        {
            var previousY = previous.Height - previousBottomTrim - overlap + y;
            var currentY = currentTopTrim + y;

            for (var x = sampleLeft; x < sampleRight; x += stepX)
            {
                var previousColor = previous.GetPixel(x, previousY);
                var currentColor = current.GetPixel(x, currentY);
                totalDifference += Math.Abs(previousColor.R - currentColor.R);
                totalDifference += Math.Abs(previousColor.G - currentColor.G);
                totalDifference += Math.Abs(previousColor.B - currentColor.B);
                sampleCount += 3;
            }
        }

        return sampleCount == 0 ? double.MaxValue : totalDifference / sampleCount;
    }

    private static double MeasureFrameDifference(Bitmap previous, Bitmap current, int currentTopTrim, int currentBottomTrim)
    {
        var width = Math.Min(previous.Width, current.Width);
        var height = Math.Min(previous.Height, current.Height);
        var stepX = Math.Max(14, width / 28);
        var stepY = Math.Max(14, height / 28);
        var sampleLeft = 12;
        var sampleRight = Math.Max(sampleLeft + 1, width - 12);
        var startY = Math.Max(currentTopTrim + 18, height / 9);
        var endY = Math.Max(startY + 1, height - Math.Max(currentBottomTrim + 18, height / 10));
        double totalDifference = 0;
        var sampleCount = 0;

        for (var y = startY; y < endY; y += stepY)
        {
            for (var x = sampleLeft; x < sampleRight; x += stepX)
            {
                var previousColor = previous.GetPixel(x, y);
                var currentColor = current.GetPixel(x, y);
                totalDifference += Math.Abs(previousColor.R - currentColor.R);
                totalDifference += Math.Abs(previousColor.G - currentColor.G);
                totalDifference += Math.Abs(previousColor.B - currentColor.B);
                sampleCount += 3;
            }
        }

        return sampleCount == 0 ? 0 : totalDifference / sampleCount;
    }

    private static double CompareHorizontalBand(Bitmap previous, Bitmap current, int previousY, int currentY)
    {
        var width = Math.Min(previous.Width, current.Width);
        var sampleLeft = Math.Max(18, width / 9);
        var sampleRight = Math.Max(sampleLeft + 1, width - sampleLeft);
        var stepX = Math.Max(8, width / 34);
        double totalDifference = 0;
        var sampleCount = 0;

        for (var x = sampleLeft; x < sampleRight; x += stepX)
        {
            var previousColor = previous.GetPixel(x, previousY);
            var currentColor = current.GetPixel(x, currentY);
            totalDifference += Math.Abs(previousColor.R - currentColor.R);
            totalDifference += Math.Abs(previousColor.G - currentColor.G);
            totalDifference += Math.Abs(previousColor.B - currentColor.B);
            sampleCount += 3;
        }

        return sampleCount == 0 ? double.MaxValue : totalDifference / sampleCount;
    }

    private static int GetRenderedHeight(ScrollFrameCapture frame, bool isFirstFrame)
    {
        var sourceY = isFirstFrame ? 0 : frame.StickyTopTrim + frame.OverlapWithPrevious;
        var bottomEdge = Math.Max(sourceY + 1, frame.Bitmap.Height - frame.StickyBottomTrim);
        return Math.Max(1, bottomEdge - sourceY);
    }

    private static void SendCtrlHome()
    {
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, 0);
        NativeMethods.keybd_event(NativeMethods.VK_HOME, 0, 0, 0);
        NativeMethods.keybd_event(NativeMethods.VK_HOME, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
    }

    private static bool IsWordLikeWindow(WindowDescriptor window)
    {
        return window.ClassName.Contains("OpusApp", StringComparison.OrdinalIgnoreCase) ||
               window.Title.Contains("Word", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetSettleDelay(WindowDescriptor window)
    {
        return IsWordLikeWindow(window)
            ? CaptureSettleDelayMs + 120
            : CaptureSettleDelayMs;
    }

    private sealed class ScrollFrameCapture(Bitmap bitmap, int stickyTopTrim, int stickyBottomTrim, int overlapWithPrevious) : IDisposable
    {
        public Bitmap Bitmap { get; } = bitmap;

        public int StickyTopTrim { get; } = stickyTopTrim;

        public int StickyBottomTrim { get; set; } = stickyBottomTrim;

        public int OverlapWithPrevious { get; } = overlapWithPrevious;

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }

    private readonly record struct WindowRepositionState(nint Handle, int Left, int Top, int Width, int Height, bool Moved)
    {
        public static WindowRepositionState None => new(nint.Zero, 0, 0, 0, 0, false);
    }
}
