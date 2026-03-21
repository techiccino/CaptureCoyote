using CaptureCoyote.App.Views;

namespace CaptureCoyote.App.Services;

internal sealed class DelayCountdownCoordinator
{
    public async Task ShowAsync(
        int seconds,
        string operationTitle = "Capture",
        string? hintText = null,
        CancellationToken cancellationToken = default)
    {
        if (seconds <= 0)
        {
            return;
        }

        var window = new DelayCountdownWindow();
        try
        {
            window.PositionOnCursorScreen();
            window.UpdateCountdown(seconds, operationTitle, hintText);
            window.Show();

            for (var remaining = seconds; remaining > 0; remaining--)
            {
                window.UpdateCountdown(remaining, operationTitle, hintText);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
