using System.Windows;
using CaptureCoyote.Editor.ViewModels;
using CaptureCoyote.Infrastructure.Branding;

namespace CaptureCoyote.Editor.Views;

public partial class DetectedTextWindow : Window
{
    public DetectedTextWindow(EditorViewModel viewModel)
    {
        InitializeComponent();
        BrandingAssets.ApplyWindowBrand(this, BrandLogoImage, BrandLogoContainer);
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            DetectedTextTextBox.Focus();
            if (viewModel.HasExtractedText)
            {
                DetectedTextTextBox.SelectAll();
            }
        };
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
