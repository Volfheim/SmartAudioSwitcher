namespace SmartAudioSwitcher.Core;

public class AudioDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public override string ToString()
    {
        return Name;
    }
}
