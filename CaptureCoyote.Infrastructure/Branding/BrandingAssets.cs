using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaptureCoyote.Infrastructure.Branding;

public static class BrandingAssets
{
    public const string LogoFileName = "CaptureCoyoteLogo.png";
    public const string IconFileName = "CaptureCoyote.ico";

    private static readonly Lazy<ImageSource?> LogoSource = new(CreateLogoSource);
    private static readonly Lazy<ImageSource?> IconSource = new(CreateIconSource);

    public static string AssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets");

    public static ImageSource? LogoImageSource => LogoSource.Value;

    public static ImageSource? WindowIconSource => IconSource.Value;

    public static void ApplyWindowBrand(Window window, System.Windows.Controls.Image? logoImage = null, FrameworkElement? logoContainer = null)
    {
        if (WindowIconSource is not null)
        {
            window.Icon = WindowIconSource;
        }

        if (logoImage is null)
        {
            return;
        }

        if (LogoImageSource is not null)
        {
            logoImage.Source = LogoImageSource;
            logoImage.Visibility = Visibility.Visible;

            if (logoContainer is not null)
            {
                logoContainer.Visibility = Visibility.Visible;
            }
        }
        else
        {
            logoImage.Source = null;
            logoImage.Visibility = Visibility.Collapsed;

            if (logoContainer is not null)
            {
                logoContainer.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static ImageSource? CreateLogoSource()
    {
        return LoadImage(Path.Combine(AssetsDirectory, LogoFileName));
    }

    private static ImageSource? CreateIconSource()
    {
        return LoadImage(Path.Combine(AssetsDirectory, IconFileName));
    }

    private static ImageSource? LoadImage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var frame = decoder.Frames.FirstOrDefault();
        frame?.Freeze();
        return frame;
    }
}
