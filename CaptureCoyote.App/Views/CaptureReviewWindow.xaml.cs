using System.Windows;
using CaptureCoyote.App.ViewModels;
using CaptureCoyote.Infrastructure.Branding;

namespace CaptureCoyote.App.Views;

public partial class CaptureReviewWindow : Window
{
    public CaptureReviewWindow(CaptureReviewViewModel viewModel)
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }
}
