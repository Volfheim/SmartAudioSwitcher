using System;
using System.ComponentModel;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using SmartAudioSwitcher.Input;
using SmartAudioSwitcher.Service;
using SmartAudioSwitcher.UI;
using System.Diagnostics;

namespace SmartAudioSwitcher.Core;

public class AppController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly AutoSwitcher _autoSwitcher;
    private HotKeyManager? _hotKeyManager;
    private MicOverlayWindow? _micOverlay;

    private const int ID_HEADSET = 1;
    private const int ID_SPEAKERS = 2;
    private const int ID_MIC_MUTE = 3;
    private const int ID_VOL_UP = 4;
    private const int ID_VOL_DOWN = 5;
    private const int ID_PREV_TRACK = 6;
    private const int ID_NEXT_TRACK = 7;
    private const int ID_PLAY_PAUSE = 8;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

    public event Action<string, string>? ShowNotification;

    public AppController(AppSettings settings, AutoSwitcher autoSwitcher)
    {
        _settings = settings;
        _autoSwitcher = autoSwitcher;
    }

    public void Initialize(IntPtr windowHandle)
    {
        _hotKeyManager = new HotKeyManager(windowHandle);
        _hotKeyManager.HotKeyPressed += OnHotKeyPressed;
        
        RegisterHotkeys();

        // Initial sync of mic state if we want to show overlay at start (optional)
        // For now, overlay only appears when muted via hotkey.
    }

    public void RegisterHotkeys()
    {
        if (_hotKeyManager == null) return;
        _hotKeyManager.UnregisterAll();

        if (_settings.HeadsetHotkey > 0)
            _hotKeyManager.Register(ID_HEADSET, _settings.HeadsetHotkey, (KeyModifiers)_settings.HeadsetModifiers, out _);

        if (_settings.SpeakerHotkey > 0)
            _hotKeyManager.Register(ID_SPEAKERS, _settings.SpeakerHotkey, (KeyModifiers)_settings.SpeakerModifiers, out _);

        if (_settings.MicMuteHotkey > 0)
            _hotKeyManager.Register(ID_MIC_MUTE, _settings.MicMuteHotkey, (KeyModifiers)_settings.MicMuteModifiers, out _);

        if (_settings.VolUpHotkey > 0)
            _hotKeyManager.Register(ID_VOL_UP, _settings.VolUpHotkey, (KeyModifiers)_settings.VolUpModifiers, out _);

        if (_settings.VolDownHotkey > 0)
            _hotKeyManager.Register(ID_VOL_DOWN, _settings.VolDownHotkey, (KeyModifiers)_settings.VolDownModifiers, out _);

        if (_settings.PrevTrackHotkey > 0)
            _hotKeyManager.Register(ID_PREV_TRACK, _settings.PrevTrackHotkey, (KeyModifiers)_settings.PrevTrackModifiers, out _);

        if (_settings.NextTrackHotkey > 0)
            _hotKeyManager.Register(ID_NEXT_TRACK, _settings.NextTrackHotkey, (KeyModifiers)_settings.NextTrackModifiers, out _);

        if (_settings.PlayPauseHotkey > 0)
            _hotKeyManager.Register(ID_PLAY_PAUSE, _settings.PlayPauseHotkey, (KeyModifiers)_settings.PlayPauseModifiers, out _);
    }

    private void OnHotKeyPressed(int id)
    {
        if (id == ID_VOL_UP)
        {
            keybd_event(0xAF, 0, 0, 0); // VK_VOLUME_UP
            keybd_event(0xAF, 0, 2, 0); // KEYEVENTF_KEYUP
            return;
        }

        if (id == ID_VOL_DOWN)
        {
            keybd_event(0xAE, 0, 0, 0); // VK_VOLUME_DOWN
            keybd_event(0xAE, 0, 2, 0); // KEYEVENTF_KEYUP
            return;
        }

        if (id == ID_PREV_TRACK)
        {
            keybd_event(0xB1, 0, 0, 0); // VK_MEDIA_PREV_TRACK
            keybd_event(0xB1, 0, 2, 0); // KEYEVENTF_KEYUP
            return;
        }

        if (id == ID_NEXT_TRACK)
        {
            keybd_event(0xB0, 0, 0, 0); // VK_MEDIA_NEXT_TRACK
            keybd_event(0xB0, 0, 2, 0); // KEYEVENTF_KEYUP
            return;
        }

        if (id == ID_PLAY_PAUSE)
        {
            keybd_event(0xB3, 0, 0, 0); // VK_MEDIA_PLAY_PAUSE
            keybd_event(0xB3, 0, 2, 0); // KEYEVENTF_KEYUP
            return;
        }

        if (id == ID_MIC_MUTE)
        {
            bool isMuted = AudioDeviceManager.ToggleMicrophoneMute();
            
            if (_settings.ShowNotifications)
            {
                ShowNotification?.Invoke(
                    isMuted ? "Микрофон выключен 🔇" : "Микрофон включен 🎤",
                    "Smart Audio Switcher");
            }

            UpdateMicOverlay(isMuted);
            return;
        }

        var targetId = id == ID_HEADSET ? _settings.HeadsetDeviceId : _settings.SpeakerDeviceId;
        if (string.IsNullOrWhiteSpace(targetId)) return;

        try
        {
            AudioDeviceManager.SetDefaultDevice(targetId);
            if (_settings.ShowNotifications)
            {
                ShowNotification?.Invoke(
                    id == ID_HEADSET ? "Переключено на наушники." : "Переключено на колонки.",
                    "Smart Audio Switcher");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error switching device: {ex.Message}");
        }
    }

    private void UpdateMicOverlay(bool isMuted)
    {
        if (!_settings.ShowMicOverlay)
        {
            if (_micOverlay != null)
            {
                _micOverlay.Close();
                _micOverlay = null;
            }
            return;
        }

        if (isMuted)
        {
            if (_micOverlay == null)
            {
                _micOverlay = new MicOverlayWindow();
                _micOverlay.Show();
            }
        }
        else
        {
            if (_micOverlay != null)
            {
                _micOverlay.Close();
                _micOverlay = null;
            }
        }
    }

    public void SuspendHotkeys()
    {
        _hotKeyManager?.UnregisterAll();
    }

    public void ResumeHotkeys()
    {
        RegisterHotkeys();
    }

    public void Dispose()
    {
        _hotKeyManager?.Dispose();
        if (_micOverlay != null)
        {
            _micOverlay.Close();
            _micOverlay = null;
        }
    }
}
