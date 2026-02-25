using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpaceMonger.App.ViewModels;

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
}
