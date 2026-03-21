using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public sealed class HotkeyPressedEventArgs(HotkeyBinding binding) : EventArgs
{
    public HotkeyBinding Binding { get; } = binding;
}
