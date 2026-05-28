using System.Runtime.InteropServices;

namespace SmartAudioSwitcher.Core;

public static class AudioDeviceManager
{
    private const uint DEVICE_STATE_ACTIVE = 0x00000001;
    private const ushort VT_LPWSTR = 31;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    public static List<AudioDevice> GetActiveDevices()
    {
        var devices = new List<AudioDevice>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            ThrowIfFailed(
                enumerator.EnumAudioEndpoints(EDataFlow.Render, DEVICE_STATE_ACTIVE, out collection),
                "Unable to enumerate active render devices.");

            ThrowIfFailed(collection.GetCount(out var count), "Unable to read device count.");

            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                IPropertyStore? store = null;
                PropVariant friendlyName = default;

                try
                {
                    ThrowIfFailed(collection.Item(i, out device), "Unable to read device entry.");
                    ThrowIfFailed(device.GetId(out var id), "Unable to read device id.");
                    ThrowIfFailed(device.OpenPropertyStore(0, out store), "Unable to open device property store.");

                    var key = new PropertyKey { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
                    ThrowIfFailed(store.GetValue(ref key, out friendlyName), "Unable to read device friendly name.");

                    var name = friendlyName.vt == VT_LPWSTR
                        ? Marshal.PtrToStringUni(friendlyName.unionMember) ?? "Unknown Device"
                        : "Unknown Device";

                    devices.Add(new AudioDevice { Id = id, Name = name, IsActive = true });
                }
                finally
                {
                    PropVariantClear(ref friendlyName);

                    if (store != null)
                    {
                        Marshal.ReleaseComObject(store);
                    }

                    if (device != null)
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
        }
        finally
        {
            if (collection != null)
            {
                Marshal.ReleaseComObject(collection);
            }

            if (enumerator != null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        return devices;
    }

    public static void SetDefaultDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("Device ID cannot be empty.", nameof(deviceId));
        }

        IPolicyConfig? policyConfig = null;
        try
        {
            policyConfig = (IPolicyConfig)new PolicyConfigClient();
            ThrowIfFailed(policyConfig.SetDefaultEndpoint(deviceId, ERole.Console), "Unable to set Console audio endpoint.");
            ThrowIfFailed(policyConfig.SetDefaultEndpoint(deviceId, ERole.Multimedia), "Unable to set Multimedia audio endpoint.");
            ThrowIfFailed(policyConfig.SetDefaultEndpoint(deviceId, ERole.Communications), "Unable to set Communications audio endpoint.");
        }
        finally
        {
            if (policyConfig != null)
            {
                Marshal.ReleaseComObject(policyConfig);
            }
        }
    }

    public static bool ToggleMicrophoneMute()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? micDevice = null;
        IAudioEndpointVolume? volume = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            // Try default communications capture device (microphone)
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Communications, out micDevice);
            
            // Fallback to console (multimedia) if communications fails
            if (hr < 0 || micDevice == null)
            {
                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Console, out micDevice);
            }

            if (hr < 0 || micDevice == null) return false;

            var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            ThrowIfFailed(micDevice.Activate(ref iid, 0, IntPtr.Zero, out var volumeObj), "Unable to activate volume interface.");
            volume = (IAudioEndpointVolume)volumeObj;

            ThrowIfFailed(volume.GetMute(out var isMuted), "Unable to get mute state.");
            bool newMuted = !isMuted;
            var context = Guid.Empty;
            ThrowIfFailed(volume.SetMute(newMuted, ref context), "Unable to set mute state.");

            return newMuted;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (volume != null) Marshal.ReleaseComObject(volume);
            if (micDevice != null) Marshal.ReleaseComObject(micDevice);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }



    private static readonly AudioDeviceNotificationClient _notificationClient = new();
    private static IMMDeviceEnumerator? _enumerator;

    public static event Action? DevicesUpdated;

    public static void Initialize()
    {
        if (_enumerator != null) return;

        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public static void Cleanup()
    {
        if (_enumerator != null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    private class AudioDeviceNotificationClient : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState) => Notify();
        public void OnDeviceAdded(string pwstrDeviceId) => Notify();
        public void OnDeviceRemoved(string pwstrDeviceId) => Notify();
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        private void Notify()
        {
            // Debounce or marshal to UI thread might be needed, but we'll let consumers handle thread safety/throttling
            DevicesUpdated?.Invoke();
        }
    }

    private static void ThrowIfFailed(int hr, string message)
    {
        if (hr < 0)
        {
            throw new COMException(message, hr);
        }
    }
}

