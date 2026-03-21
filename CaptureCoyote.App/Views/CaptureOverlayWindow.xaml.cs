using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureCoyote.App.Interop;
using CaptureCoyote.App.Services;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using Point = System.Windows.Point;

namespace CaptureCoyote.App.Views;

public partial class CaptureOverlayWindow : Window
{
    private static readonly TimeSpan MagnifierRefreshInterval = TimeSpan.FromMilliseconds(32);
    private readonly MonitorDescriptor _monitor;
    private readonly BitmapSource _monitorImage;
    private PixelPoint? _lastMagnifierCursor;
    private DateTimeOffset _lastMagnifierRefreshAt;

    public CaptureOverlayWindow(MonitorDescriptor monitor, BitmapSource monitorImage)
    {
        InitializeComponent();
        _monitor = monitor;
        _monitorImage = monitorImage;
        BackgroundImage.Source = monitorImage;

        Left = monitor.Bounds.X / monitor.ScaleFactor;
        Top = monitor.Bounds.Y / monitor.ScaleFactor;
        Width = monitor.Bounds.Width / monitor.ScaleFactor;
        Height = monitor.Bounds.Height / monitor.ScaleFactor;

        Loaded += (_, _) => UpdateMask(null);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(
                handle,
                nint.Zero,
                (int)Math.Round(_monitor.Bounds.X),
                (int)Math.Round(_monitor.Bounds.Y),
                (int)Math.Round(_monitor.Bounds.Width),
                (int)Math.Round(_monitor.Bounds.Height),
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        };
    }

    public nint WindowHandle => new WindowInteropHelper(this).Handle;

    internal void UpdateState(CaptureOverlayRenderState state)
    {
        InstructionText.Text = state.Instructions;

        Rect? selection = state.Selection is { } rect && IntersectsMonitor(rect) ? ToLocalRect(rect.Normalize()) : (Rect?)null;
        UpdateMask(selection);
        UpdateSelection(state.Selection);
        UpdateHoveredWindow(state.Mode, state.HoveredWindow);
        UpdateMagnifier(state.Cursor, state.ShowMagnifier);
    }

    private void UpdateMask(Rect? selection)
    {
        var outer = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(outer);

        if (selection is { } visibleSelection && visibleSelection.Width > 0 && visibleSelection.Height > 0)
        {
            group.Children.Add(new RectangleGeometry(visibleSelection, 8, 8));
        }

        DimPath.Data = group;
    }

    private void UpdateSelection(PixelRect? selection)
    {
        if (selection is null || !IntersectsMonitor(selection.Value))
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            SelectionBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var normalized = selection.Value.Normalize();
        var rect = ToLocalRect(normalized);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            SelectionBadge.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionBorder.Visibility = Visibility.Visible;
        SelectionBorder.Width = rect.Width;
        SelectionBorder.Height = rect.Height;
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);

        SelectionBadge.Visibility = Visibility.Visible;
        SelectionBadgeTitle.Text = $"{Math.Round(normalized.Width)} x {Math.Round(normalized.Height)} px";
        SelectionBadgeMeta.Text = $"X {Math.Round(normalized.X)}  Y {Math.Round(normalized.Y)}";
        PositionFloatingElement(SelectionBadge, rect, preferAbove: true, xOffset: 0);
    }

    private void UpdateHoveredWindow(CaptureMode mode, WindowDescriptor? hoveredWindow)
    {
        if (mode != CaptureMode.Window || hoveredWindow is null || !IntersectsMonitor(hoveredWindow.Bounds))
        {
            WindowHighlight.Visibility = Visibility.Collapsed;
            WindowBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = ToLocalRect(hoveredWindow.Bounds);
        WindowHighlight.Visibility = Visibility.Visible;
        WindowHighlight.Width = rect.Width;
        WindowHighlight.Height = rect.Height;
        Canvas.SetLeft(WindowHighlight, rect.X);
        Canvas.SetTop(WindowHighlight, rect.Y);

        WindowBadge.Visibility = Visibility.Visible;
        WindowBadgeTitle.Text = hoveredWindow.Title;
        WindowBadgeMeta.Text = $"{Math.Round(hoveredWindow.Bounds.Width)} x {Math.Round(hoveredWindow.Bounds.Height)} px";
        PositionFloatingElement(WindowBadge, rect, preferAbove: true, xOffset: 12);
    }

    private void UpdateMagnifier(PixelPoint cursor, bool showMagnifier)
    {
        if (!showMagnifier || !Contains(cursor))
        {
            MagnifierHost.Visibility = Visibility.Collapsed;
            _lastMagnifierCursor = null;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastMagnifierCursor is { } lastCursor &&
            now - _lastMagnifierRefreshAt < MagnifierRefreshInterval &&
            Math.Abs(lastCursor.X - cursor.X) < 6 &&
            Math.Abs(lastCursor.Y - cursor.Y) < 6)
        {
            MagnifierHost.Visibility = Visibility.Visible;
            return;
        }

        var localX = (int)Math.Round(cursor.X - _monitor.Bounds.X);
        var localY = (int)Math.Round(cursor.Y - _monitor.Bounds.Y);
        var half = 16;
        var sourceX = Math.Clamp(localX - half, 0, Math.Max(0, _monitorImage.PixelWidth - (half * 2)));
        var sourceY = Math.Clamp(localY - half, 0, Math.Max(0, _monitorImage.PixelHeight - (half * 2)));
        var width = Math.Min(half * 2, _monitorImage.PixelWidth - sourceX);
        var height = Math.Min(half * 2, _monitorImage.PixelHeight - sourceY);

        if (width <= 0 || height <= 0)
        {
            MagnifierHost.Visibility = Visibility.Collapsed;
            return;
        }

        var cropped = new CroppedBitmap(_monitorImage, new Int32Rect(sourceX, sourceY, width, height));
        MagnifierImage.Source = cropped;
        MagnifierText.Text = $"X {Math.Round(cursor.X)}  Y {Math.Round(cursor.Y)}";
        MagnifierHost.Visibility = Visibility.Visible;
        _lastMagnifierCursor = cursor;
        _lastMagnifierRefreshAt = now;

        var localPoint = ToLocalPoint(cursor);
        var offsetX = localPoint.X > ActualWidth * 0.62 ? -MagnifierHost.Width - 24 : 24;
        var offsetY = localPoint.Y > ActualHeight * 0.62 ? -MagnifierHost.Height - 24 : 24;
        var left = Math.Clamp(localPoint.X + offsetX, 12, Math.Max(12, ActualWidth - MagnifierHost.Width - 12));
        var top = Math.Clamp(localPoint.Y + offsetY, 12, Math.Max(12, ActualHeight - MagnifierHost.Height - 12));
        Canvas.SetLeft(MagnifierHost, left);
        Canvas.SetTop(MagnifierHost, top);
    }

    private void PositionFloatingElement(FrameworkElement element, Rect anchor, bool preferAbove, double xOffset)
    {
        element.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredSize = element.DesiredSize;
        var width = desiredSize.Width > 0 ? desiredSize.Width : element.Width;
        var height = desiredSize.Height > 0 ? desiredSize.Height : element.Height;

        var left = Math.Clamp(anchor.X + xOffset, 12, Math.Max(12, ActualWidth - width - 12));
        var top = preferAbove
            ? anchor.Y - height - 12
            : anchor.Bottom + 12;

        if (top < 12)
        {
            top = Math.Min(ActualHeight - height - 12, anchor.Bottom + 12);
        }
        else if (top + height > ActualHeight - 12)
        {
            top = Math.Max(12, anchor.Y - height - 12);
        }

        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, Math.Max(12, top));
    }

    private bool Contains(PixelPoint point)
    {
        return point.X >= _monitor.Bounds.X &&
               point.X <= _monitor.Bounds.Right &&
               point.Y >= _monitor.Bounds.Y &&
               point.Y <= _monitor.Bounds.Bottom;
    }

    private bool IntersectsMonitor(PixelRect rect)
    {
        var local = rect.Normalize();
        return !(local.Right < _monitor.Bounds.X ||
                 local.X > _monitor.Bounds.Right ||
                 local.Bottom < _monitor.Bounds.Y ||
                 local.Y > _monitor.Bounds.Bottom);
    }

    private Rect ToLocalRect(PixelRect rect)
    {
        var normalized = rect.Normalize();
        return new Rect(
            (normalized.X - _monitor.Bounds.X) / _monitor.ScaleFactor,
            (normalized.Y - _monitor.Bounds.Y) / _monitor.ScaleFactor,
            normalized.Width / _monitor.ScaleFactor,
            normalized.Height / _monitor.ScaleFactor);
    }

    private Point ToLocalPoint(PixelPoint point)
    {
        return new Point(
            (point.X - _monitor.Bounds.X) / _monitor.ScaleFactor,
            (point.Y - _monitor.Bounds.Y) / _monitor.ScaleFactor);
    }
}
