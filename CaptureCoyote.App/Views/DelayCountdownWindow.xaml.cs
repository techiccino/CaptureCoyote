using System.Windows;
using Forms = System.Windows.Forms;

namespace CaptureCoyote.App.Views;

public partial class DelayCountdownWindow : Window
{
    public DelayCountdownWindow()
    {
        InitializeComponent();
    }

    public void UpdateCountdown(int remainingSeconds, string operationTitle, string? hintText = null)
    {
        CountdownValueText.Text = remainingSeconds.ToString();
        var safeTitle = string.IsNullOrWhiteSpace(operationTitle) ? "Capture" : operationTitle.Trim();
        CountdownCaptionText.Text = remainingSeconds == 1
            ? $"{safeTitle} starts in 1 second"
            : $"{safeTitle} starts in {remainingSeconds} seconds";
        CountdownHintText.Text = string.IsNullOrWhiteSpace(hintText)
            ? "Move into position before capture starts."
            : hintText;
    }

    public void PositionOnCursorScreen()
    {
        var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        Left = area.Left + ((area.Width - Width) / 2d);
        Top = area.Top + ((area.Height - Height) / 2d);
    }
}
