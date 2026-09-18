using System.Windows;

namespace LabelStudio.Desktop;

public partial class PrintDialogWindow : Window
{
    public string PrinterQueueName => QueueNameBox.Text;

    public PrintDialogWindow()
    {
        InitializeComponent();
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}