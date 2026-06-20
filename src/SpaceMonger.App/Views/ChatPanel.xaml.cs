using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Views;

public partial class ChatPanel : UserControl
{
    private ChatViewModel? _viewModel;

    public event Action? OpenSettingsRequested;

    public ChatPanel()
    {
        InitializeComponent();
    }

    public void SetViewModel(ChatViewModel vm)
    {
        // Unsubscribe from previous view model's collection if any
        if (_viewModel != null)
        {
            _viewModel.Messages.CollectionChanged -= Messages_CollectionChanged;
        }

        _viewModel = vm;
        DataContext = vm;

        // Subscribe to collection changes for auto-scroll
        vm.Messages.CollectionChanged += Messages_CollectionChanged;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        // Use Dispatcher to ensure layout has updated before scrolling
        Dispatcher.InvokeAsync(() =>
        {
            MessagesScrollViewer.ScrollToEnd();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (_viewModel?.SendCommand.CanExecute(null) == true)
            {
                _viewModel.SendCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsRequested?.Invoke();
    }

    private void ClearContextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.LinkedEntry = null;
            _viewModel.LinkedRecommendation = null;
            _viewModel.LinkedItemPath = null;
        }
    }

    private void ThinkingBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ChatMessage message)
        {
            message.IsThinkingExpanded = !message.IsThinkingExpanded;
            e.Handled = true;
        }
    }

    private void CopyMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ChatMessage message)
        {
            var textToCopy = message.Text;
            if (!string.IsNullOrEmpty(textToCopy))
            {
                try
                {
                    Clipboard.SetText(textToCopy);
                }
                catch
                {
                    // Clipboard might be unavailable
                }
            }
            e.Handled = true;
        }
    }

    private void MessageBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            // Find the copy button in the parent grid
            var parent = VisualTreeHelper.GetParent(element) as Grid;
            if (parent != null)
            {
                var copyButton = FindChild<Button>(parent);
                if (copyButton != null)
                {
                    copyButton.Opacity = 1;
                }
            }
        }
    }

    private void MessageBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            var parent = VisualTreeHelper.GetParent(element) as Grid;
            if (parent != null)
            {
                var copyButton = FindChild<Button>(parent);
                if (copyButton != null)
                {
                    copyButton.Opacity = 0;
                }
            }
        }
    }

    private void FlowDocumentScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Forward the mouse wheel event to the parent ScrollViewer
        if (sender is FlowDocumentScrollViewer viewer)
        {
            var parentScrollViewer = FindParent<ScrollViewer>(viewer);
            if (parentScrollViewer != null)
            {
                parentScrollViewer.ScrollToVerticalOffset(parentScrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typedParent)
                return typedParent;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }
}
