using System.Windows;

namespace CaptureCoyote.Editor.Views;

public partial class LayerRenameWindow : Window
{
    public LayerRenameWindow(string currentName)
    {
        InitializeComponent();
        LayerName = currentName;
        DataContext = this;
        Loaded += (_, _) =>
        {
            LayerNameTextBox.Focus();
            LayerNameTextBox.SelectAll();
        };
    }

    public string LayerName { get; set; }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
