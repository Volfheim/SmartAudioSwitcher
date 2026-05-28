using System.Windows;
using System.Windows.Input;

namespace SmartAudioSwitcher.UI;

public partial class SettingsWindow : Window
{
    private readonly Action<bool, bool, bool, bool, bool> _onChanged;
    private bool _isInitializing;

    public SettingsWindow(
        bool startMinimized,
        bool minimizeToTrayOnClose,
        bool autoStart,
        bool showNotifications,
        bool showMicOverlay,
        Action<bool, bool, bool, bool, bool> onChanged)
    {
        InitializeComponent();
        _onChanged = onChanged;

        _isInitializing = true;
        ChkStartMinimized.IsChecked = startMinimized;
        ChkMinimizeToTrayOnClose.IsChecked = minimizeToTrayOnClose;
        ChkAutoStart.IsChecked = autoStart;
        ChkShowNotifications.IsChecked = showNotifications;
        ChkShowMicOverlay.IsChecked = showMicOverlay;
        _isInitializing = false;
    }

    private void OptionChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _onChanged(
            ChkStartMinimized.IsChecked == true,
            ChkMinimizeToTrayOnClose.IsChecked == true,
            ChkAutoStart.IsChecked == true,
            ChkShowNotifications.IsChecked == true,
            ChkShowMicOverlay.IsChecked == true);
    }



    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnUpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        _ = Core.AutoUpdater.CheckForUpdatesAsync(true);
    }
}
