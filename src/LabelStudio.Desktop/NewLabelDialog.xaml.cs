using System.Windows;
using System.Windows.Controls;

namespace LabelStudio.Desktop;

public partial class NewLabelDialog : Window
{
    private bool _initializing = true;

    public string SelectedMediaProfileId { get; private set; } = "brother.dk-22251";
    public int SelectedViewRotationDegrees { get; private set; }

    public NewLabelDialog(
        string selectedMediaProfileId = "brother.dk-22251",
        bool changeExisting = false,
        int? selectedViewRotationDegrees = null)
    {
        InitializeComponent();

        SelectedMediaProfileId = selectedMediaProfileId;
        foreach (ComboBoxItem item in MediaCombo.Items.OfType<ComboBoxItem>())
        {
            item.IsSelected = item.Tag is string profileId &&
                profileId.Equals(selectedMediaProfileId, StringComparison.OrdinalIgnoreCase);
        }
        SelectOrientation(selectedViewRotationDegrees ?? DefaultRotation(selectedMediaProfileId));
        _initializing = false;

        if (changeExisting)
        {
            Title = "Change Roll";
            DialogEyebrow.Text = "LABEL STOCK";
            DialogHeading.Text = "Change the current roll";
            DialogDescription.Text = "Artwork stays in place. The canvas and printer output will use the selected roll geometry.";
            ConfirmButton.Content = "Change roll";
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (MediaCombo.SelectedItem is ComboBoxItem item && item.Tag is string profileId)
        {
            SelectedMediaProfileId = profileId;
        }
        if (OrientationCombo.SelectedItem is ComboBoxItem orientation &&
            orientation.Tag is string rotation &&
            int.TryParse(rotation, out int degrees))
        {
            SelectedViewRotationDegrees = degrees;
        }
        DialogResult = true;
    }

    private void OnMediaSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || OrientationCombo is null) return;
        if (MediaCombo.SelectedItem is ComboBoxItem item && item.Tag is string profileId)
        {
            SelectOrientation(DefaultRotation(profileId));
        }
    }

    private void SelectOrientation(int rotationDegrees)
    {
        int normalized = rotationDegrees is 90 or 270 ? 90 : 0;
        foreach (ComboBoxItem item in OrientationCombo.Items.OfType<ComboBoxItem>())
        {
            item.IsSelected = item.Tag is string value && value == normalized.ToString();
        }
        SelectedViewRotationDegrees = normalized;
    }

    private static int DefaultRotation(string profileId) =>
        profileId.Equals("brother.dk-11204", StringComparison.OrdinalIgnoreCase) ? 90 : 0;
}
