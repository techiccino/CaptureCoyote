using System.Windows;
using CaptureCoyote.App.ViewModels;
using CaptureCoyote.Infrastructure.Branding;

namespace CaptureCoyote.App.Views;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
        DataContext = viewModel;
    }

    private void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SkipButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
