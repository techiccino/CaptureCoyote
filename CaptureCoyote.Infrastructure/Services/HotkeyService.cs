using System.Windows.Interop;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Infrastructure.Interop;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class HotkeyService : IHotkeyService
{
    private readonly Dictionary<int, HotkeyBinding> _registered = [];
    private HwndSource? _source;
    private nint _windowHandle;
    private int _nextId = 9000;

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public void Attach(nint windowHandle)
    {
        if (_windowHandle == windowHandle && _source is not null)
        {
            return;
        }

        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WndProc);
    }

    public void RegisterBindings(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();

        foreach (var binding in bindings)
        {
            var id = _nextId++;
            var registered = NativeMethods.RegisterHotKey(_windowHandle, id, (uint)binding.Modifiers, (uint)binding.VirtualKey);
            if (registered)
            {
                _registered[id] = binding;
            }
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys.ToList())
        {
            _ = NativeMethods.UnregisterHotKey(_windowHandle, id);
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var binding))
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(binding));
            handled = true;
        }

        return nint.Zero;
    }
}
