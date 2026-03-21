using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Editor.ViewModels;
using CaptureCoyote.Infrastructure.Branding;

namespace CaptureCoyote.Editor.Views;

public partial class EditorWindow : Window
{
    private DetectedTextWindow? _detectedTextWindow;
    private PinnedCaptureWindow? _pinnedCaptureWindow;
    private bool _closeConfirmed;
    private Point? _layerDragStart;
    private AnnotationObject? _draggedLayer;
    private readonly List<InputBinding> _shortcutBindings = [];
    private bool _shortcutsSuspended;

    public EditorWindow(EditorViewModel viewModel)
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
        _shortcutBindings.AddRange(InputBindings.Cast<InputBinding>());
        DataContext = viewModel;
        viewModel.DetectedTextRequested += ViewModelOnDetectedTextRequested;
        viewModel.PinToScreenRequested += ViewModelOnPinToScreenRequested;
        Closing += EditorWindowOnClosing;
        PreviewGotKeyboardFocus += EditorWindowOnPreviewGotKeyboardFocus;
        PreviewLostKeyboardFocus += EditorWindowOnPreviewLostKeyboardFocus;
        Closed += (_, _) =>
        {
            viewModel.DetectedTextRequested -= ViewModelOnDetectedTextRequested;
            viewModel.PinToScreenRequested -= ViewModelOnPinToScreenRequested;
            _detectedTextWindow?.Close();
            _pinnedCaptureWindow?.Close();
            PreviewGotKeyboardFocus -= EditorWindowOnPreviewGotKeyboardFocus;
            PreviewLostKeyboardFocus -= EditorWindowOnPreviewLostKeyboardFocus;
        };
    }

    private void ViewModelOnDetectedTextRequested(object? sender, EventArgs e)
    {
        if (DataContext is not EditorViewModel viewModel)
        {
            return;
        }

        if (_detectedTextWindow is null || !_detectedTextWindow.IsLoaded)
        {
            _detectedTextWindow = new DetectedTextWindow(viewModel)
            {
                Owner = this
            };
            _detectedTextWindow.Closed += (_, _) => _detectedTextWindow = null;
        }

        if (!_detectedTextWindow.IsVisible)
        {
            _detectedTextWindow.Show();
        }

        if (_detectedTextWindow.WindowState == WindowState.Minimized)
        {
            _detectedTextWindow.WindowState = WindowState.Normal;
        }

        _detectedTextWindow.Activate();
    }

    private void ViewModelOnPinToScreenRequested(object? sender, PinnedCaptureRequestEventArgs e)
    {
        if (_pinnedCaptureWindow is null || !_pinnedCaptureWindow.IsLoaded)
        {
            _pinnedCaptureWindow = new PinnedCaptureWindow();
            _pinnedCaptureWindow.Closed += (_, _) => _pinnedCaptureWindow = null;
            PositionPinnedWindow();
        }

        _pinnedCaptureWindow.UpdateCapture(e.ImagePngBytes, e.Title);

        if (!_pinnedCaptureWindow.IsVisible)
        {
            _pinnedCaptureWindow.Show();
        }

        _pinnedCaptureWindow.Activate();
    }

    private void PositionPinnedWindow()
    {
        if (_pinnedCaptureWindow is null)
        {
            return;
        }

        var width = _pinnedCaptureWindow.Width;
        _pinnedCaptureWindow.Left = Left + Math.Max(36, ActualWidth - width - 54);
        _pinnedCaptureWindow.Top = Top + 72;
    }

    private async void EditorWindowOnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || DataContext is not EditorViewModel viewModel)
        {
            return;
        }

        e.Cancel = true;

        if (viewModel.IsDirty)
        {
            var result = MessageBox.Show(
                "Save changes before closing?\n\nChoose No to close and keep a recovery draft for later.",
                "CaptureCoyote",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                var saved = await viewModel.SaveBeforeCloseAsync().ConfigureAwait(true);
                if (!saved)
                {
                    return;
                }

                await viewModel.ClearRecoveryDraftAsync().ConfigureAwait(true);
            }
            else
            {
                await viewModel.PersistRecoveryDraftNowAsync().ConfigureAwait(true);
            }
        }
        else
        {
            await viewModel.ClearRecoveryDraftAsync().ConfigureAwait(true);
        }

        _closeConfirmed = true;
        Close();
    }

    private void RenameLayerMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel viewModel ||
            sender is not MenuItem { DataContext: AnnotationObject annotation })
        {
            return;
        }

        viewModel.SelectedAnnotation = annotation;

        var renameWindow = new LayerRenameWindow(viewModel.GetLayerDisplayName(annotation))
        {
            Owner = this,
            ShowActivated = true,
            Topmost = Topmost
        };

        if (renameWindow.ShowDialog() == true)
        {
            viewModel.RenameLayer(annotation, renameWindow.LayerName);
        }
    }

    private void LayersListBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _layerDragStart = e.GetPosition(this);
        _draggedLayer = FindDataContext<AnnotationObject>(e.OriginalSource as DependencyObject);
    }

    private void LayersListBox_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedLayer is null || _layerDragStart is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        var delta = position - _layerDragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(AnnotationObject), _draggedLayer), DragDropEffects.Move);
        _draggedLayer = null;
        _layerDragStart = null;
    }

    private void LayersListBox_OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(AnnotationObject)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void LayersListBox_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox ||
            DataContext is not EditorViewModel viewModel ||
            e.Data.GetData(typeof(AnnotationObject)) is not AnnotationObject draggedLayer)
        {
            return;
        }

        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetIndex = listBox.Items.Count;
        if (targetItem?.DataContext is AnnotationObject targetAnnotation)
        {
            targetIndex = listBox.Items.IndexOf(targetAnnotation);
            var targetPosition = e.GetPosition(targetItem);
            if (targetPosition.Y > targetItem.ActualHeight / 2)
            {
                targetIndex++;
            }
        }

        viewModel.MoveLayerToDisplayIndex(draggedLayer, targetIndex);
        listBox.SelectedItem = draggedLayer;
        listBox.ScrollIntoView(draggedLayer);
        _draggedLayer = null;
        _layerDragStart = null;
        e.Handled = true;
    }

    private static T? FindDataContext<T>(DependencyObject? source)
        where T : class
    {
        return FindAncestor<FrameworkElement>(source)?.DataContext as T;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T typed)
            {
                return typed;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void EditorWindowOnPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateShortcutState(e.NewFocus);
    }

    private void EditorWindowOnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => UpdateShortcutState(Keyboard.FocusedElement)), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void UpdateShortcutState(IInputElement? focusedElement)
    {
        var shouldSuspend = IsTextInputFocused(focusedElement);
        if (shouldSuspend == _shortcutsSuspended)
        {
            return;
        }

        _shortcutsSuspended = shouldSuspend;
        InputBindings.Clear();

        if (!_shortcutsSuspended)
        {
            foreach (var binding in _shortcutBindings)
            {
                InputBindings.Add(binding);
            }
        }
    }

    private static bool IsTextInputFocused(IInputElement? focusedElement)
    {
        return focusedElement switch
        {
            TextBox or RichTextBox or PasswordBox => true,
            ComboBox comboBox when comboBox.IsEditable => true,
            DependencyObject dependencyObject when FindAncestor<TextBox>(dependencyObject) is not null => true,
            DependencyObject dependencyObject when FindAncestor<RichTextBox>(dependencyObject) is not null => true,
            DependencyObject dependencyObject when FindAncestor<PasswordBox>(dependencyObject) is not null => true,
            DependencyObject dependencyObject when FindAncestor<ComboBox>(dependencyObject) is { IsEditable: true } => true,
            _ => false
        };
    }
}
