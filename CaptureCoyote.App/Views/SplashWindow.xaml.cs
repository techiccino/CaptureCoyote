using System.Windows;
using CaptureCoyote.Infrastructure.Branding;

namespace CaptureCoyote.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
    }
}
