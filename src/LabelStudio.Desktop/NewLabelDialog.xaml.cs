using System.Windows;
using System.Windows.Controls;

namespace LabelStudio.Desktop;

public partial class NewLabelDialog : Window
{
    public string SelectedMediaProfileId { get; private set; } = "brother.dk-22251";

    public NewLabelDialog()
    {
        InitializeComponent();
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (MediaCombo.SelectedItem is ComboBoxItem item && item.Tag is string profileId)
        {
            SelectedMediaProfileId = profileId;
        }
        DialogResult = true;
    }
}