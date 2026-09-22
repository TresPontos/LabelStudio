using System.Windows;

namespace LabelStudio.Desktop;

public partial class UnsavedChangesDialog : Window
{
    public bool SaveChanges { get; private set; }

    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SaveChanges = true;
        DialogResult = true;
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        SaveChanges = false;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
