using System.Windows;

namespace SpaceMonger.App.Views;

public partial class DonateDialog : Window
{
    public DonateDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        
        // 覆盖整个 Owner 窗口区域以实现遮罩效果
        if (Owner is not null)
        {
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            Left = Owner.Left;
            Top = Owner.Top;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
