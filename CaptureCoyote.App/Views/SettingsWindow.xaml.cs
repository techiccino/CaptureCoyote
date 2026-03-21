using System.Windows;
using CaptureCoyote.App.ViewModels;
using CaptureCoyote.Infrastructure.Branding;

namespace CaptureCoyote.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
        DataContext = viewModel;
        viewModel.RequestClose += (_, saved) =>
        {
            DialogResult = saved;
            Close();
        };
    }
}
