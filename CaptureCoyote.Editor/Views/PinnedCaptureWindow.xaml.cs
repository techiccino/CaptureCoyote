using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CaptureCoyote.Infrastructure.Branding;

namespace CaptureCoyote.Editor.Views;

public partial class PinnedCaptureWindow : Window
{
    public PinnedCaptureWindow()
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
    }

    public void UpdateCapture(byte[] imagePngBytes, string title)
    {
        PinnedImage.Source = DecodeBitmap(imagePngBytes);
        PinnedTitleText.Text = title;
        Title = $"{title} - Pinned";
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
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
