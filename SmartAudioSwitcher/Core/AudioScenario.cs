namespace SmartAudioSwitcher.Core;

public class AudioScenario
{
    public string ProcessName { get; set; } = string.Empty;
    public string TargetDeviceId { get; set; } = string.Empty;
    public string TargetDeviceName { get; set; } = string.Empty; // For UI display

    public override string ToString()
    {
        return $"{ProcessName} -> {TargetDeviceName}";
    }
}
