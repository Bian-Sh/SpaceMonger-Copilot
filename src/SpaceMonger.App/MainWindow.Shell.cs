using System.Windows;
using System.Windows.Controls;
using SpaceMonger.App.Localization;

namespace SpaceMonger.App;

public partial class MainWindow
{
    private bool _isChatCollapsed;

    private void OpenSettingsDialog() => ShowSettingsPage();

    private void ToggleChatPanel()
    {
        _isChatCollapsed = !_isChatCollapsed;

        if (_isChatCollapsed)
        {
            ChatPanelColumn.Width = new GridLength(0);
            ChatPanelColumn.MinWidth = 0;
            ChatPanel.Visibility = Visibility.Collapsed;
            ChatSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            ChatPanelColumn.Width = new GridLength(360);
            ChatPanelColumn.MinWidth = 260;
            ChatPanel.Visibility = Visibility.Visible;
            ChatSplitter.Visibility = Visibility.Visible;
        }

        TitleBar.UpdateCollapseIcon(!_isChatCollapsed);
    }

    private void ShowSettingsPage()
    {
        if (_settingsViewModel is null)
            return;

        SettingsPage.ReloadSettingsForOpen();
        SettingsPage.Visibility = Visibility.Visible;
    }

    private void HideSettingsPage()
    {
        SettingsPage.Visibility = Visibility.Collapsed;
    }

    private void SwitchToAboutTab()
    {
        AboutTabBtn.IsChecked = true;
    }

    private void VersionDisplay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SwitchToAboutTab();
    }

    private void OnSettingsChanged()
    {
        L.SetLanguage(_settingsViewModel?.Language);
        _chatViewModel?.RefreshApiKeyStatus();
    }
}
