using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SmartAudioSwitcher.Core;

public class AppSettings
{
    private static readonly string SettingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartAudioSwitcher", "settings.json");

    public List<ManagedAudioDevice> Devices { get; set; } = new();

    // Старые поля оставлены для обратной совместимости (миграции)
    public string HeadsetDeviceId { get; set; } = string.Empty;
    public string HeadsetDeviceName { get; set; } = string.Empty;
    public string SpeakerDeviceId { get; set; } = string.Empty;
    public string SpeakerDeviceName { get; set; } = string.Empty;
    public int HeadsetHotkey { get; set; } = 72; // H
    public int HeadsetModifiers { get; set; } = 1; // Alt
    public int SpeakerHotkey { get; set; } = 83; // S
    public int SpeakerModifiers { get; set; } = 1; // Alt

    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = false;
    public bool AutoStart { get; set; } = false;
    public bool ShowNotifications { get; set; } = true;
    public bool ShowMicOverlay { get; set; } = true;
    public bool UseDarkMode { get; set; } = true;

    public bool AutoSwitchEnabled { get; set; } = true;
    public bool AutoSwitchFallback { get; set; } = true;

    public int MicMuteHotkey { get; set; } = 77; // M
    public int MicMuteModifiers { get; set; } = 1; // Alt

    public int VolUpHotkey { get; set; } = 38; // Up Arrow
    public int VolUpModifiers { get; set; } = 2; // Ctrl

    public int VolDownHotkey { get; set; } = 40; // Down Arrow
    public int VolDownModifiers { get; set; } = 2; // Ctrl

    public int PrevTrackHotkey { get; set; } = 37; // Left Arrow
    public int PrevTrackModifiers { get; set; } = 2; // Ctrl

    public int NextTrackHotkey { get; set; } = 39; // Right Arrow
    public int NextTrackModifiers { get; set; } = 2; // Ctrl

    public int PlayPauseHotkey { get; set; } = 80; // P
    public int PlayPauseModifiers { get; set; } = 1; // Alt

    public List<AudioScenario> Scenarios { get; set; } = new();

    public static AppSettings Load()
    {
        if (!System.IO.File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = System.IO.File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            
            // Миграция
            if (settings.Devices.Count == 0)
            {
                if (!string.IsNullOrEmpty(settings.HeadsetDeviceId) || !string.IsNullOrEmpty(settings.HeadsetDeviceName))
                {
                    settings.Devices.Add(new ManagedAudioDevice 
                    { 
                        Id = Guid.NewGuid().ToString(), 
                        DeviceId = settings.HeadsetDeviceId, 
                        CustomName = string.IsNullOrEmpty(settings.HeadsetDeviceName) ? "Наушники" : settings.HeadsetDeviceName, 
                        Hotkey = settings.HeadsetHotkey, 
                        Modifiers = settings.HeadsetModifiers 
                    });
                }
                if (!string.IsNullOrEmpty(settings.SpeakerDeviceId) || !string.IsNullOrEmpty(settings.SpeakerDeviceName))
                {
                    settings.Devices.Add(new ManagedAudioDevice 
                    { 
                        Id = Guid.NewGuid().ToString(), 
                        DeviceId = settings.SpeakerDeviceId, 
                        CustomName = string.IsNullOrEmpty(settings.SpeakerDeviceName) ? "Колонки" : settings.SpeakerDeviceName, 
                        Hotkey = settings.SpeakerHotkey, 
                        Modifiers = settings.SpeakerModifiers 
                    });
                }
                // Очищаем, чтобы миграция не дублировалась
                settings.HeadsetDeviceId = string.Empty;
                settings.SpeakerDeviceId = string.Empty;
                settings.HeadsetDeviceName = string.Empty;
                settings.SpeakerDeviceName = string.Empty;
                
                if (settings.Devices.Count > 0)
                {
                    settings.Save(); // сохраняем сразу
                }
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(SettingsPath);
        if (dir != null && !System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(SettingsPath, json);
    }
}
