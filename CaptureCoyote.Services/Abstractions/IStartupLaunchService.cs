namespace CaptureCoyote.Services.Abstractions;

public interface IStartupLaunchService
{
    void ApplySetting(bool enabled);

    bool IsEnabled();
}
