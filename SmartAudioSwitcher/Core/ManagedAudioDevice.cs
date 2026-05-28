using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartAudioSwitcher.Core;

public class ManagedAudioDevice : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _deviceId = string.Empty;
    private string _customName = string.Empty;
    private int _hotkey = 0;
    private int _modifiers = 0;
    private bool _isDeletable = true;
    private bool _isCapturing = false;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCapturing
    {
        get => _isCapturing;
        set 
        { 
            if (_isCapturing != value) 
            { 
                _isCapturing = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(HotkeyDisplay)); 
            } 
        }
    }

    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public string DeviceId
    {
        get => _deviceId;
        set { if (_deviceId != value) { _deviceId = value; OnPropertyChanged(); } }
    }

    public string CustomName
    {
        get => _customName;
        set { if (_customName != value) { _customName = value; OnPropertyChanged(); } }
    }

    public int Hotkey
    {
        get => _hotkey;
        set { if (_hotkey != value) { _hotkey = value; OnPropertyChanged(); OnPropertyChanged(nameof(HotkeyDisplay)); } }
    }

    public int Modifiers
    {
        get => _modifiers;
        set { if (_modifiers != value) { _modifiers = value; OnPropertyChanged(); OnPropertyChanged(nameof(HotkeyDisplay)); } }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string HotkeyDisplay
    {
        get
        {
            if (IsCapturing) return "Нажмите клавиши...";
            if (Hotkey <= 0) return "Назначить";
            
            try
            {
                var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(Hotkey);
                var keyText = key.ToString();
                var parts = new System.Collections.Generic.List<string>();
                if ((Modifiers & 2) == 2) parts.Add("Ctrl");
                if ((Modifiers & 4) == 4) parts.Add("Shift");
                if ((Modifiers & 1) == 1) parts.Add("Alt");
                if ((Modifiers & 8) == 8) parts.Add("Win");
                return parts.Count > 0 ? $"{string.Join(" + ", parts)} + {keyText}" : keyText;
            }
            catch
            {
                return $"VK:{Hotkey}";
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDeletable
    {
        get => _isDeletable;
        set { if (_isDeletable != value) { _isDeletable = value; OnPropertyChanged(); } }
    }
}
