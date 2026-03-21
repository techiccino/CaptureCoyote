using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureCoyote.App.Interop;
using CaptureCoyote.App.Views;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Services.Abstractions;
using CaptureMode = CaptureCoyote.Core.Enums.CaptureMode;

namespace CaptureCoyote.App.Services;

internal sealed class CaptureOverlayCoordinator(IWindowLocatorService windowLocatorService)
{
    private static readonly TimeSpan HoverCommitDelay = TimeSpan.FromMilliseconds(55);
    private static readonly TimeSpan HoverReleaseDelay = TimeSpan.FromMilliseconds(120);
    private readonly IWindowLocatorService _windowLocatorService = windowLocatorService;
    private readonly List<CaptureOverlayWindow> _windows = [];
    private readonly TaskCompletionSource<PixelRect?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DispatcherTimer? _timer;
    private HashSet<nint> _overlayHandles = [];
    private CaptureMode _mode;
    private PixelPoint _selectionStart;
    private PixelRect? _selection;
    private WindowDescriptor? _hoveredWindow;
    private WindowDescriptor? _pendingHoveredWindow;
    private WindowDescriptor? _selectedWindow;
    private DateTimeOffset _pendingHoveredSince;
    private DateTimeOffset _hoveredWindowSince;
    private bool _isSelecting;
    private CaptureOverlayWindow? _selectionCaptureWindow;
    private bool _wasLeftDown;

    public WindowDescriptor? SelectedWindow => _selectedWindow;

    public async Task<PixelRect?> ShowAsync(DesktopSnapshot snapshot, CaptureMode mode, CancellationToken cancellationToken = default)
    {
        _mode = mode;
        var decodedDesktop = DecodeBitmap(snapshot.ImagePngBytes);

        foreach (var monitor in snapshot.Monitors)
        {
            var cropRect = new Int32Rect(
                (int)Math.Round(monitor.Bounds.X - snapshot.VirtualBounds.X),
                (int)Math.Round(monitor.Bounds.Y - snapshot.VirtualBounds.Y),
                Math.Max(1, (int)Math.Round(monitor.Bounds.Width)),
                Math.Max(1, (int)Math.Round(monitor.Bounds.Height)));

            var monitorBitmap = new CroppedBitmap(decodedDesktop, cropRect);
            monitorBitmap.Freeze();

            var window = new CaptureOverlayWindow(monitor, monitorBitmap);
            window.PreviewKeyDown += OverlayWindowOnPreviewKeyDown;
            window.PreviewMouseLeftButtonDown += OverlayWindowOnPreviewMouseLeftButtonDown;
            window.PreviewMouseMove += OverlayWindowOnPreviewMouseMove;
            window.PreviewMouseLeftButtonUp += OverlayWindowOnPreviewMouseLeftButtonUp;
            _windows.Add(window);
        }

        using var registration = cancellationToken.Register(Cancel);

        foreach (var window in _windows)
        {
            window.Show();
        }

        await Task.Delay(35, cancellationToken).ConfigureAwait(true);
        _overlayHandles = _windows.Select(window => window.WindowHandle).Where(handle => handle != nint.Zero).ToHashSet();

        if (_windows.FirstOrDefault() is { } firstWindow)
        {
            firstWindow.Activate();
            firstWindow.Focus();
        }

        if (_mode == CaptureMode.Window)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += TimerOnTick;
            _timer.Start();
        }

        if (NativeMethods.GetCursorPos(out var initialCursorPoint))
        {
            UpdateOverlayState(new PixelPoint(initialCursorPoint.X, initialCursorPoint.Y), leftDown: false);
        }

        try
        {
            return await _completion.Task.ConfigureAwait(true);
        }
        finally
        {
            Cleanup();
        }
    }

    private void TimerOnTick(object? sender, EventArgs e)
    {
        if (_mode != CaptureMode.Window)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursorPoint))
        {
            return;
        }

        var cursor = new PixelPoint(cursorPoint.X, cursorPoint.Y);
        var leftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;

        switch (_mode)
        {
            case CaptureMode.Region:
                UpdateRegionSelection(cursor, leftDown);
                break;
            case CaptureMode.Window:
                UpdateWindowSelection(cursor, leftDown);
                break;
        }

        UpdateOverlayState(cursor, leftDown);
        _wasLeftDown = leftDown;
    }

    private void UpdateOverlayState(PixelPoint cursor, bool leftDown)
    {
        var state = new CaptureOverlayRenderState
        {
            Mode = _mode,
            Cursor = cursor,
            Selection = _selection,
            HoveredWindow = _hoveredWindow,
            ShowMagnifier = _mode == CaptureMode.Window && !_isSelecting && !leftDown,
            Instructions = BuildInstructions()
        };

        foreach (var window in _windows)
        {
            window.UpdateState(state);
        }
    }

    private void UpdateRegionSelection(PixelPoint cursor, bool leftDown)
    {
        if (leftDown && !_wasLeftDown)
        {
            _selectionStart = cursor;
            _selection = new PixelRect(cursor.X, cursor.Y, 0, 0);
            _isSelecting = true;
        }
        else if (leftDown && _isSelecting)
        {
            _selection = PixelRect.FromPoints(_selectionStart, cursor);
        }
        else if (!leftDown && _wasLeftDown && _isSelecting)
        {
            _isSelecting = false;
            if (_selection is { } selection && selection.Width > 6 && selection.Height > 6)
            {
                Complete(selection.Normalize());
            }
            else
            {
                _selection = null;
            }
        }
    }

    private void UpdateWindowSelection(PixelPoint cursor, bool leftDown)
    {
        if (!leftDown)
        {
            UpdateHoveredWindow(cursor);
            return;
        }

        if (_wasLeftDown)
        {
            return;
        }

        var lockedWindow = _hoveredWindow ?? _pendingHoveredWindow ?? _windowLocatorService.GetWindowAt(cursor, _overlayHandles);
        if (lockedWindow is not null)
        {
            _selectedWindow = lockedWindow;
            _hoveredWindow = lockedWindow;
            Complete(lockedWindow.Bounds.Normalize());
        }
    }

    private void UpdateHoveredWindow(PixelPoint cursor)
    {
        var now = DateTimeOffset.UtcNow;
        var candidate = _windowLocatorService.GetWindowAt(cursor, _overlayHandles);

        if (candidate is not null)
        {
            if (_hoveredWindow?.Handle == candidate.Handle)
            {
                _hoveredWindow = candidate;
                _hoveredWindowSince = now;
                _pendingHoveredWindow = null;
                return;
            }

            if (_pendingHoveredWindow?.Handle != candidate.Handle)
            {
                _pendingHoveredWindow = candidate;
                _pendingHoveredSince = now;
                return;
            }

            if (now - _pendingHoveredSince >= HoverCommitDelay)
            {
                _hoveredWindow = candidate;
                _hoveredWindowSince = now;
                _pendingHoveredWindow = null;
            }

            return;
        }

        _pendingHoveredWindow = null;

        if (_hoveredWindow is null)
        {
            return;
        }

        var keepHovered = _hoveredWindow.Bounds.Contains(cursor) || now - _hoveredWindowSince <= HoverReleaseDelay;
        if (!keepHovered)
        {
            _hoveredWindow = null;
        }
    }

    private void OverlayWindowOnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        if (_mode == CaptureMode.Region && _selection is { } selection && selection.Width > 6 && selection.Height > 6)
        {
            Complete(selection.Normalize());
        }

        if (_mode == CaptureMode.Window && (_hoveredWindow ?? _pendingHoveredWindow) is { } window)
        {
            _selectedWindow = window;
            Complete(window.Bounds.Normalize());
        }

        e.Handled = true;
    }

    private void OverlayWindowOnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_mode != CaptureMode.Region)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursorPoint))
        {
            return;
        }

        _selectionStart = new PixelPoint(cursorPoint.X, cursorPoint.Y);
        _selection = new PixelRect(_selectionStart.X, _selectionStart.Y, 0, 0);
        _isSelecting = true;
        _selectionCaptureWindow = sender as CaptureOverlayWindow;
        _selectionCaptureWindow?.CaptureMouse();
        UpdateOverlayState(_selectionStart, leftDown: true);
        e.Handled = true;
    }

    private void OverlayWindowOnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_mode != CaptureMode.Region || !_isSelecting)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursorPoint))
        {
            return;
        }

        var cursor = new PixelPoint(cursorPoint.X, cursorPoint.Y);
        _selection = PixelRect.FromPoints(_selectionStart, cursor);
        UpdateOverlayState(cursor, leftDown: true);
        e.Handled = true;
    }

    private void OverlayWindowOnPreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_mode != CaptureMode.Region || !_isSelecting)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursorPoint))
        {
            return;
        }

        var cursor = new PixelPoint(cursorPoint.X, cursorPoint.Y);
        _selection = PixelRect.FromPoints(_selectionStart, cursor);
        _isSelecting = false;
        _selectionCaptureWindow?.ReleaseMouseCapture();
        _selectionCaptureWindow = null;

        if (_selection is { } selection && selection.Width > 6 && selection.Height > 6)
        {
            Complete(selection.Normalize());
        }
        else
        {
            _selection = null;
            UpdateOverlayState(cursor, leftDown: false);
        }

        e.Handled = true;
    }

    private void Complete(PixelRect rect)
    {
        _completion.TrySetResult(rect);
    }

    private void Cancel()
    {
        _completion.TrySetResult(null);
    }

    private void Cleanup()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= TimerOnTick;
            _timer = null;
        }

        foreach (var window in _windows)
        {
            window.PreviewKeyDown -= OverlayWindowOnPreviewKeyDown;
            window.PreviewMouseLeftButtonDown -= OverlayWindowOnPreviewMouseLeftButtonDown;
            window.PreviewMouseMove -= OverlayWindowOnPreviewMouseMove;
            window.PreviewMouseLeftButtonUp -= OverlayWindowOnPreviewMouseLeftButtonUp;
            window.ReleaseMouseCapture();
            window.Close();
        }

        _windows.Clear();
    }

    private string BuildInstructions()
    {
        var targetWindow = _hoveredWindow ?? _pendingHoveredWindow;
        var targetName = targetWindow is null
            ? string.Empty
            : $" Target: {TrimTitle(targetWindow.Title)}.";

        return _mode switch
        {
            CaptureMode.Window when targetWindow is not null => $"Click to capture the highlighted window.{targetName} Esc cancels.",
            CaptureMode.Window => "Hover a window and click to capture. Esc cancels.",
            _ when _isSelecting => "Release to capture the selected region. Enter confirms, Esc cancels.",
            _ => "Drag to select a region. Enter confirms, Esc cancels."
        };
    }

    private static string TrimTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Untitled";
        }

        var trimmed = title.Trim();
        return trimmed.Length <= 72
            ? trimmed
            : $"{trimmed[..69]}...";
    }

    private static BitmapSource DecodeBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
