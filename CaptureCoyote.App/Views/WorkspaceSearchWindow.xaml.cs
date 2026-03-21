using System.Windows;
using CaptureCoyote.App.ViewModels;
using CaptureCoyote.Infrastructure.Branding;

namespace CaptureCoyote.App.Views;

public partial class WorkspaceSearchWindow : Window
{
    public WorkspaceSearchWindow(WorkspaceSearchViewModel viewModel)
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
        DataContext = viewModel;
        Loaded += (_, _) => SearchTextBox.Focus();
    }
}
