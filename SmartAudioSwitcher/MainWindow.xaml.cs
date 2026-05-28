using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using SmartAudioSwitcher.Core;
using SmartAudioSwitcher.Input;
using SmartAudioSwitcher.Service;
using SmartAudioSwitcher.UI;
using DrawingIcon = System.Drawing.Icon;

namespace SmartAudioSwitcher;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AutoSwitcher _autoSwitcher;
    private readonly AppController _appController;
    private SettingsWindow? _settingsWindow;

    public System.Collections.ObjectModel.ObservableCollection<AudioDevice> AvailableDevices { get; } = new();
    private string? _capturingDeviceId = null;

    private bool _isCapturingMicKey;
    private bool _isCapturingVolUpKey;
    private bool _isCapturingVolDownKey;
    private bool _isCapturingPrevTrackKey;
    private bool _isCapturingNextTrackKey;
    private bool _isCapturingPlayPauseKey;
    private bool _isRefreshingProcessList;
    private bool _isInitializing = true;
    private bool _exitRequested;
    private TextBox? _processPickerEditor;

    private List<RunningApp> _runningAppsCache = new();
    private DateTime _runningAppsCacheUpdatedUtc = DateTime.MinValue;
    private static readonly TimeSpan RunningAppsCacheTtl = TimeSpan.FromSeconds(8);
    private const double BaseAdaptiveWidth = 1080;
    private const double MaxAdaptiveWidth = 2000;
    private const double MinAdaptiveProcessWidth = 220;
    private const double MaxAdaptiveProcessWidth = 980;
    private const double MinAdaptiveTargetWidth = 180;
    private const double MaxAdaptiveTargetWidth = 560;
    private const double ScenarioControlsSpacing = 12;
    private const double LayoutOuterMargin = 40;
    private const double RightColumnUsableRatio = 0.60;

    private const double RightColumnChromeLoss = 40;

    public MainWindow()
    {
        _settings = AppSettings.Load();
        _autoSwitcher = new AutoSwitcher(_settings);
        _autoSwitcher.LogMessage += msg => Debug.WriteLine(msg);
        _appController = new AppController(_settings, _autoSwitcher);
        _appController.ShowNotification += OnShowNotification;

        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnShowNotification(string message, string title)
    {
        Dispatcher.Invoke(() =>
        {
            TrayIcon.ShowBalloonTip(title, message, BalloonIcon.Info);
        });
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _appController.Initialize(helper.Handle);

        AudioDeviceManager.Initialize();
        AudioDeviceManager.DevicesUpdated += OnDevicesUpdated;

        LoadData();
        
        // Check for updates in background
        _ = AutoUpdater.CheckForUpdatesAsync(false);
    }

    private void OnDevicesUpdated()
    {
        Dispatcher.Invoke(() =>
        {
            LoadDevices();
            // Refresh scenarios active checking if needed, or just let auto-switcher handle it on next tick
        });
    }

    private void LoadData()
    {
        _isInitializing = true;

        LoadDevices();

        if (CmbTargetDevice.SelectedItem == null && CmbTargetDevice.Items.Count > 0)
        {
            CmbTargetDevice.SelectedIndex = 0;
        }

        AttachProcessPickerTextHook();

        ChkFallback.IsChecked = _settings.AutoSwitchFallback;
        ChkAutoSwitch.IsChecked = _settings.AutoSwitchEnabled;

        RefreshScenarios();
        _autoSwitcher.UpdateSettings(_settings);
        UpdateAdaptiveLayout();

        UpdateAdaptiveLayout();

        UpdateHotkeyButton(BtnHotkeyMic, _settings.MicMuteHotkey, _settings.MicMuteModifiers);
        UpdateHotkeyButton(BtnHotkeyVolUp, _settings.VolUpHotkey, _settings.VolUpModifiers);
        UpdateHotkeyButton(BtnHotkeyVolDown, _settings.VolDownHotkey, _settings.VolDownModifiers);
        UpdateHotkeyButton(BtnHotkeyPrevTrack, _settings.PrevTrackHotkey, _settings.PrevTrackModifiers);
        UpdateHotkeyButton(BtnHotkeyNextTrack, _settings.NextTrackHotkey, _settings.NextTrackModifiers);
        UpdateHotkeyButton(BtnHotkeyPlayPause, _settings.PlayPauseHotkey, _settings.PlayPauseModifiers);
        _appController.RegisterHotkeys();

        _isInitializing = false;

        ApplyAutoSwitchState(showErrors: false);

        var args = Environment.GetCommandLineArgs();
        if (_settings.StartMinimized || args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            HideToTray();
        }
    }


    private void LoadDevices()
    {
        var devices = AudioDeviceManager.GetActiveDevices();
        
        AvailableDevices.Clear();
        foreach (var d in devices)
        {
            AvailableDevices.Add(d);
        }

        foreach (var md in _settings.Devices)
        {
            if (!string.IsNullOrWhiteSpace(md.DeviceId) && !devices.Any(d => d.Id == md.DeviceId))
            {
                var name = string.IsNullOrWhiteSpace(md.CustomName) ? "Неизвестно" : md.CustomName;
                AvailableDevices.Insert(0, new AudioDevice { Id = md.DeviceId, Name = $"[Отключено] {name}", IsActive = false });
            }
        }

        var targetId = (CmbTargetDevice.SelectedItem as AudioDevice)?.Id;
        
        CmbTargetDevice.ItemsSource = AvailableDevices;
        DevicesList.ItemsSource = null;
        DevicesList.ItemsSource = _settings.Devices;
        
        if (targetId != null)
        {
            SelectDevice(CmbTargetDevice, targetId);
        }
    }

    private string? ResolveDeviceId(IEnumerable<AudioDevice> devices, string id, string name)
    {
        if (devices.Any(d => string.Equals(d.Id, id, StringComparison.Ordinal)))
            return id; 
        
        if (!string.IsNullOrWhiteSpace(name))
        {
            var match = devices.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Id;
        }
        return id; 
    }

    private void PersistSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError(this, "Ошибка сохранения", $"Не удалось сохранить настройки:\n{ex.Message}");
        }
    }

    private void SelectDevice(ComboBox comboBox, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || comboBox.ItemsSource is not IEnumerable<AudioDevice> devices)
        {
            return;
        }

        comboBox.SelectedItem = devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));
    }

    private void RefreshScenarios()
    {
        ListScenarios.ItemsSource = null;
        ListScenarios.ItemsSource = _settings.Scenarios
            .OrderBy(s => s.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void CmbDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (sender is ComboBox comboBox && comboBox.DataContext is ManagedAudioDevice md)
        {
            if (comboBox.SelectedValue is string newId)
            {
                md.DeviceId = newId;
                PersistSettings();
            }
        }
    }

    private void BtnAddDevice_Click(object sender, RoutedEventArgs e)
    {
        _settings.Devices.Add(new ManagedAudioDevice { CustomName = "Новое устройство" });
        LoadDevices();
        PersistSettings();
    }

    private void BtnRemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            var device = _settings.Devices.FirstOrDefault(d => d.Id == id);
            if (device != null)
            {
                _settings.Devices.Remove(device);
                _appController.RegisterHotkeys();
                LoadDevices();
                PersistSettings();
            }
        }
    }

    private void BtnHotkeyDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } btn)
        {
            _capturingDeviceId = id;
            KeyDown += MainWindow_KeyDown;
        }
    }

    private void CmbTargetDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        UpdateAdaptiveLayout();
    }

    private void ChkAutoSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.AutoSwitchEnabled = ChkAutoSwitch.IsChecked == true;
        ApplyAutoSwitchState(showErrors: true);
        PersistSettings();
    }

    private void ChkFallback_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.AutoSwitchFallback = ChkFallback.IsChecked == true;
        _autoSwitcher.UpdateSettings(_settings);
        PersistSettings();
    }

    private void ApplyAutoSwitchState(bool showErrors)
    {
        if (!_settings.AutoSwitchEnabled)
        {
            _autoSwitcher.Stop();
            return;
        }

        try
        {
            _autoSwitcher.Start();
        }
        catch (Exception ex)
        {
            _settings.AutoSwitchEnabled = false;
            ChkAutoSwitch.IsChecked = false;
            _autoSwitcher.Stop();
            PersistSettings();

            if (showErrors)
            {
                AppDialog.ShowError(this, "Ошибка автосмены", $"Не удалось запустить автопереключение:\n{ex.Message}");
            }
        }
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



    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsWindow();
    }

    private void MenuItemOpen_Click(object sender, RoutedEventArgs e)
    {
        RestoreFromTray();
    }

    private void MenuItemSettings_Click(object sender, RoutedEventArgs e)
    {
        RestoreFromTray();
        OpenSettingsWindow();
    }

    private void MenuItemUpdate_Click(object sender, RoutedEventArgs e)
    {
        _ = AutoUpdater.CheckForUpdatesAsync(true);
    }

    private void MenuItemExit_Click(object sender, RoutedEventArgs e)
    {
        _exitRequested = true;
        Close();
    }

    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        RestoreFromTray();
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.Show();
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settings.StartMinimized,
            _settings.MinimizeToTrayOnClose,
            _settings.AutoStart,
            _settings.ShowNotifications,
            _settings.ShowMicOverlay,
            OnGeneralSettingsChanged)

        {
            Owner = this
        };

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnGeneralSettingsChanged(bool startMinimized, bool minimizeToTrayOnClose, bool autoStart, bool showNotifications, bool showMicOverlay)
    {

        if (_settings.AutoStart != autoStart)
        {
            SetAutoStart(autoStart);
        }

        _settings.StartMinimized = startMinimized;
        _settings.MinimizeToTrayOnClose = minimizeToTrayOnClose;
        _settings.AutoStart = autoStart;
        _settings.ShowNotifications = showNotifications;
        _settings.ShowMicOverlay = showMicOverlay;
        PersistSettings();
    }


    private void SetAutoStart(bool enable)
    {
        const string keyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "SmartAudioSwitcher";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyName, true);
            if (key == null) return;

            if (enable)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path)) return;
                key.SetValue(valueName, $"\"{path}\" --minimized");
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Autostart error: {ex.Message}");
        }
    }


    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void BtnAddScenario_Click(object sender, RoutedEventArgs e)
    {
        var processName = NormalizeProcessName(
            CmbProcessPicker.SelectedItem is RunningApp selected ? selected.ProcessName : CmbProcessPicker.Text);

        if (string.IsNullOrWhiteSpace(processName))
        {
            AppDialog.ShowWarning(this, "Сценарии", "Выберите процесс.");
            return;
        }

        if (CmbTargetDevice.SelectedItem is not AudioDevice device)
        {
            AppDialog.ShowWarning(this, "Сценарии", "Выберите целевое устройство.");
            return;
        }

        var existing = _settings.Scenarios.FirstOrDefault(s =>
            s.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.TargetDeviceId = device.Id;
            existing.TargetDeviceName = device.Name;
        }
        else
        {
            _settings.Scenarios.Add(new AudioScenario
            {
                ProcessName = processName,
                TargetDeviceId = device.Id,
                TargetDeviceName = device.Name
            });
        }

        CmbProcessPicker.SelectedItem = null;
        CmbProcessPicker.Text = string.Empty;
        CmbTargetDevice.SelectedItem = null;
        UpdateAdaptiveLayout();
        RefreshScenarios();
        _autoSwitcher.UpdateSettings(_settings);
        PersistSettings();
    }

    private void BtnRemoveScenario_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AudioScenario scenario })
        {
            _settings.Scenarios.Remove(scenario);
            UpdateAdaptiveLayout();
            RefreshScenarios();
            _autoSwitcher.UpdateSettings(_settings);
            PersistSettings();
        }
    }

    private void AttachProcessPickerTextHook()
    {
        CmbProcessPicker.ApplyTemplate();
        if (CmbProcessPicker.Template.FindName("PART_EditableTextBox", CmbProcessPicker) is not TextBox editor)
        {
            return;
        }

        if (ReferenceEquals(_processPickerEditor, editor))
        {
            return;
        }

        if (_processPickerEditor != null)
        {
            _processPickerEditor.TextChanged -= ProcessPickerEditor_TextChanged;
        }

        _processPickerEditor = editor;
        _processPickerEditor.TextChanged += ProcessPickerEditor_TextChanged;
    }

    private void ProcessPickerEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        UpdateAdaptiveLayout();
    }

    private void ScenarioInputGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInitializing || Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5)
        {
            return;
        }

        UpdateAdaptiveLayout();
    }

    private void UpdateAdaptiveLayout()
    {
        var targetNames = new List<string>();

        if (CmbTargetDevice.ItemsSource is IEnumerable<AudioDevice> devices)
        {
            targetNames.AddRange(devices.Select(d => d.Name));
        }

        targetNames.AddRange(_settings.Scenarios.Select(s => s.TargetDeviceName));

        if (CmbTargetDevice.SelectedItem is AudioDevice selectedDevice)
        {
            targetNames.Add(selectedDevice.Name);
        }

        var longestTarget = targetNames
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .OrderByDescending(v => v.Length)
            .FirstOrDefault() ?? "Аудиовыход";

        var desiredTargetWidth = Math.Clamp(EstimateWidth(longestTarget), MinAdaptiveTargetWidth, MaxAdaptiveTargetWidth);

        var processNames = _settings.Scenarios
            .Select(s => s.ProcessName)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (!string.IsNullOrWhiteSpace(CmbProcessPicker.Text))
        {
            processNames.Add(CmbProcessPicker.Text);
        }

        var longestProcess = processNames
            .OrderByDescending(v => v.Length)
            .FirstOrDefault() ?? "process.exe";

        var desiredProcessWidth = Math.Clamp(EstimateWidth(longestProcess), MinAdaptiveProcessWidth, MaxAdaptiveProcessWidth);
        var addButtonWidth = BtnAddScenario.ActualWidth > 0
            ? BtnAddScenario.ActualWidth
            : (BtnAddScenario.Width > 0 ? BtnAddScenario.Width : 30);
        var requiredScenarioInputs = desiredProcessWidth + desiredTargetWidth + addButtonWidth + ScenarioControlsSpacing;
        var desiredWidth = ((requiredScenarioInputs + RightColumnChromeLoss) / RightColumnUsableRatio) + LayoutOuterMargin;

        if (WindowState == WindowState.Normal)
        {
            Width = Math.Clamp(Math.Max(BaseAdaptiveWidth, desiredWidth), BaseAdaptiveWidth, MaxAdaptiveWidth);
        }

        var targetWidth = desiredTargetWidth;
        var processWidth = desiredProcessWidth;

        if (ScenarioInputGrid.ActualWidth > 0 && addButtonWidth > 0)
        {
            var availableInputs = ScenarioInputGrid.ActualWidth - addButtonWidth - ScenarioControlsSpacing;
            if (availableInputs > 0)
            {
                targetWidth = Math.Min(desiredTargetWidth, Math.Max(140, availableInputs - MinAdaptiveProcessWidth));
                processWidth = Math.Min(desiredProcessWidth, Math.Max(80, availableInputs - targetWidth));

                if (processWidth < MinAdaptiveProcessWidth && targetWidth > 140)
                {
                    var rebalancedTarget = Math.Max(140, targetWidth - (MinAdaptiveProcessWidth - processWidth));
                    processWidth = Math.Min(desiredProcessWidth, Math.Max(80, availableInputs - rebalancedTarget));
                    targetWidth = rebalancedTarget;
                }
            }
        }

        CmbTargetDevice.Width = targetWidth;
        CmbTargetDevice.MinWidth = Math.Min(140, targetWidth);
        CmbTargetDevice.MaxWidth = MaxAdaptiveTargetWidth;

        CmbProcessPicker.Width = processWidth;
        CmbProcessPicker.MinWidth = Math.Min(160, processWidth);
        CmbProcessPicker.MaxWidth = MaxAdaptiveProcessWidth;
    }

    private static double EstimateWidth(string text)
    {
        return 94 + (text.Length * 8.8);
    }

    private static string NormalizeProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Trim('"');
        normalized = Path.GetFileName(normalized);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (!normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".exe";
        }

        return normalized;
    }



    private void BtnHotkeyMic_Click(object sender, RoutedEventArgs e)
    {
        BtnHotkeyMic.Content = "Нажмите клавиши...";
        _isCapturingMicKey = true;
        KeyDown += MainWindow_KeyDown;
    }

    private void BtnHotkeyVolUp_Click(object sender, RoutedEventArgs e)
    {
        BtnHotkeyVolUp.Content = "Нажмите клавиши...";
        _isCapturingVolUpKey = true;
        KeyDown += MainWindow_KeyDown;
    }

    private void BtnHotkeyVolDown_Click(object sender, RoutedEventArgs e)
    {
        BtnHotkeyVolDown.Content = "Нажмите клавиши...";
        _isCapturingVolDownKey = true;
        KeyDown += MainWindow_KeyDown;
    }

    private void BtnHotkeyPrevTrack_Click(object sender, RoutedEventArgs e)
    {
        BtnHotkeyPrevTrack.Content = "Нажмите клавиши...";
        _isCapturingPrevTrackKey = true;
        KeyDown += MainWindow_KeyDown;
    }

    private void BtnHotkeyNextTrack_Click(object sender, RoutedEventArgs e)
    {
        BtnHotkeyNextTrack.Content = "Нажмите клавиши...";
        _isCapturingNextTrackKey = true;
        KeyDown += MainWindow_KeyDown;
    }

    private void BtnHotkeyPlayPause_Click(object sender, RoutedEventArgs e)
    {
        BtnHotkeyPlayPause.Content = "Нажмите клавиши...";
        _isCapturingPlayPauseKey = true;
        KeyDown += MainWindow_KeyDown;
    }


    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingDeviceId == null && !_isCapturingMicKey && !_isCapturingVolUpKey && !_isCapturingVolDownKey && !_isCapturingPrevTrackKey && !_isCapturingNextTrackKey && !_isCapturingPlayPauseKey)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _capturingDeviceId = null;
            _isCapturingMicKey = false;
            _isCapturingVolUpKey = false;
            _isCapturingVolDownKey = false;
            _isCapturingPrevTrackKey = false;
            _isCapturingNextTrackKey = false;
            _isCapturingPlayPauseKey = false;
            UpdateHotkeyButton(BtnHotkeyMic, _settings.MicMuteHotkey, _settings.MicMuteModifiers);
            UpdateHotkeyButton(BtnHotkeyVolUp, _settings.VolUpHotkey, _settings.VolUpModifiers);
            UpdateHotkeyButton(BtnHotkeyVolDown, _settings.VolDownHotkey, _settings.VolDownModifiers);
            UpdateHotkeyButton(BtnHotkeyPrevTrack, _settings.PrevTrackHotkey, _settings.PrevTrackModifiers);
            UpdateHotkeyButton(BtnHotkeyNextTrack, _settings.NextTrackHotkey, _settings.NextTrackModifiers);
            UpdateHotkeyButton(BtnHotkeyPlayPause, _settings.PlayPauseHotkey, _settings.PlayPauseModifiers);
            KeyDown -= MainWindow_KeyDown;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var mods = 0;
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) mods += 1;
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) mods += 2;
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) mods += 4;
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) mods += 8;

        var vk = KeyInterop.VirtualKeyFromKey(key);

        if (_capturingDeviceId != null)
        {
            var device = _settings.Devices.FirstOrDefault(d => d.Id == _capturingDeviceId);
            if (device != null)
            {
                device.Hotkey = vk;
                device.Modifiers = mods;
            }
            _capturingDeviceId = null;
        }
        else if (_isCapturingMicKey)
        {
            _settings.MicMuteHotkey = vk;
            _settings.MicMuteModifiers = mods;
            UpdateHotkeyButton(BtnHotkeyMic, vk, mods);
            _isCapturingMicKey = false;
        }
        else if (_isCapturingVolUpKey)
        {
            _settings.VolUpHotkey = vk;
            _settings.VolUpModifiers = mods;
            UpdateHotkeyButton(BtnHotkeyVolUp, vk, mods);
            _isCapturingVolUpKey = false;
        }
        else if (_isCapturingVolDownKey)
        {
            _settings.VolDownHotkey = vk;
            _settings.VolDownModifiers = mods;
            UpdateHotkeyButton(BtnHotkeyVolDown, vk, mods);
            _isCapturingVolDownKey = false;
        }
        else if (_isCapturingPrevTrackKey)
        {
            _settings.PrevTrackHotkey = vk;
            _settings.PrevTrackModifiers = mods;
            UpdateHotkeyButton(BtnHotkeyPrevTrack, vk, mods);
            _isCapturingPrevTrackKey = false;
        }
        else if (_isCapturingNextTrackKey)
        {
            _settings.NextTrackHotkey = vk;
            _settings.NextTrackModifiers = mods;
            UpdateHotkeyButton(BtnHotkeyNextTrack, vk, mods);
            _isCapturingNextTrackKey = false;
        }
        else if (_isCapturingPlayPauseKey)
        {
            _settings.PlayPauseHotkey = vk;
            _settings.PlayPauseModifiers = mods;
            UpdateHotkeyButton(BtnHotkeyPlayPause, vk, mods);
            _isCapturingPlayPauseKey = false;
        }

        _appController.RegisterHotkeys();
        PersistSettings();
        KeyDown -= MainWindow_KeyDown;
    }


    private void UpdateHotkeyButton(Button button, int vk, int modifiers)
    {
        if (vk <= 0)
        {
            button.Content = "Назначить";
            return;
        }

        try
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            var keyText = key.ToString();

            var parts = new List<string>();
            if ((modifiers & 2) == 2) parts.Add("Ctrl");
            if ((modifiers & 4) == 4) parts.Add("Shift");
            if ((modifiers & 1) == 1) parts.Add("Alt");
            if ((modifiers & 8) == 8) parts.Add("Win");

            button.Content = parts.Count > 0
                ? $"{string.Join(" + ", parts)} + {keyText}"
                : keyText;
        }
        catch
        {
            button.Content = $"VK:{vk}";
        }
    }

    private async void CmbProcessPicker_DropDownOpened(object sender, EventArgs e)
    {
        if (_isRefreshingProcessList)
        {
            return;
        }

        if (_runningAppsCache.Count > 0 && DateTime.UtcNow - _runningAppsCacheUpdatedUtc < RunningAppsCacheTtl)
        {
            return;
        }

        _isRefreshingProcessList = true;
        try
        {
            var apps = await Task.Run(GetRunningApplications);
            _runningAppsCache = apps;
            _runningAppsCacheUpdatedUtc = DateTime.UtcNow;
            CmbProcessPicker.ItemsSource = _runningAppsCache;
        }
        finally
        {
            _isRefreshingProcessList = false;
        }
    }

    private void CmbProcessPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbProcessPicker.SelectedItem is RunningApp app)
        {
            CmbProcessPicker.Text = app.ProcessName;
            UpdateAdaptiveLayout();
        }
    }

    private static List<RunningApp> GetRunningApplications()
    {
        var apps = new List<RunningApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentPid = Process.GetCurrentProcess().Id;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == currentPid || process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    var processName = $"{process.ProcessName}.exe";
                    if (!seen.Add(processName))
                    {
                        continue;
                    }

                    apps.Add(new RunningApp
                    {
                        Name = string.IsNullOrWhiteSpace(process.MainWindowTitle) ? process.ProcessName : process.MainWindowTitle,
                        ProcessName = processName,
                        Icon = TryLoadProcessIcon(process)
                    });
                }
                catch
                {
                }
            }
        }

        return apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static ImageSource? TryLoadProcessIcon(Process process)
    {
        try
        {
            string? processPath;
            try
            {
                processPath = process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                return null;
            }

            using DrawingIcon? icon = DrawingIcon.ExtractAssociatedIcon(processPath);
            if (icon == null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));

            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        AudioDeviceManager.DevicesUpdated -= OnDevicesUpdated;
        AudioDeviceManager.Cleanup();

        if (_settingsWindow != null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        _appController?.Dispose();

        _autoSwitcher.Dispose();
        TrayIcon.Dispose();
        base.OnClosed(e);
    }
}

public class RunningApp
{
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(ProcessName) ? Name : ProcessName;
    }
}


