using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public interface IHotkeyService : IDisposable
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    void Attach(nint windowHandle);

    void RegisterBindings(IEnumerable<HotkeyBinding> bindings);

    void UnregisterAll();
}
