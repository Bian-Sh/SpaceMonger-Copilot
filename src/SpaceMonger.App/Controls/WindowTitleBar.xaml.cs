using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SpaceMonger.App.Controls;

public partial class WindowTitleBar : UserControl
{
    public WindowTitleBar()
    {
        InitializeComponent();
    }

    private Window ParentWindow => Window.GetWindow(this);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var window = ParentWindow;
        if (window is null) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize(window);
        }
        else
        {
            // If window is maximized, restore first so drag works naturally
            if (window.WindowState == WindowState.Maximized)
            {
                var point = PointToScreen(e.GetPosition(this));
                window.WindowState = WindowState.Normal;
                window.Left = point.X - window.Width / 2;
                window.Top = point.Y - 18;
            }
            window.DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ParentWindow is { } w)
            w.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ParentWindow is { } w)
            ToggleMaximize(w);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ParentWindow?.Close();
    }

    private static void ToggleMaximize(Window window)
    {
        if (window.WindowState == WindowState.Maximized)
        {
            window.WindowState = WindowState.Normal;
        }
        else
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Updates the maximize/restore icon based on current window state.
    /// Call this when the parent window's state changes.
    /// </summary>
    public void UpdateMaximizeIcon(bool isMaximized)
    {
        // E922 = Maximize, E923 = Restore
        MaximizeButton.Content = isMaximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }
}
