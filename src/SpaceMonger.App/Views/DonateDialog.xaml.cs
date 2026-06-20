using System.Windows;

namespace SpaceMonger.App.Views;

public partial class DonateDialog : Window
{
    public DonateDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
