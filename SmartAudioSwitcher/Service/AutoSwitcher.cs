using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmartAudioSwitcher.Core;
using Timer = System.Timers.Timer;

namespace SmartAudioSwitcher.Service;

public sealed class AutoSwitcher : IDisposable
{
    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private readonly object _configLock = new();
    private readonly object _processLock = new();
    private readonly WinEventDelegate _foregroundChangedHandler;
    private readonly Timer _resyncTimer;

    private List<AudioScenario> _scenarioSnapshot = new();
    private bool _fallbackEnabled;
    private string _speakerDeviceId = string.Empty;

    private IntPtr _foregroundHook = IntPtr.Zero;
    private bool _isRunning;
    private bool _isDisposed;
    private IntPtr _lastWindow = IntPtr.Zero;
    private string _currentDeviceId = string.Empty;

    public event Action<string>? LogMessage;

    public AutoSwitcher(AppSettings settings)
    {
        _foregroundChangedHandler = OnForegroundChanged;
        _resyncTimer = new Timer(10000); // 10 seconds fallback interval
        _resyncTimer.Elapsed += (_, _) => ProcessForegroundWindow(GetForegroundWindow());
        UpdateSettings(settings);
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_configLock)
        {
            _fallbackEnabled = settings.AutoSwitchFallback;
            _speakerDeviceId = settings.SpeakerDeviceId;
            _scenarioSnapshot = settings.Scenarios
                .Where(s => !string.IsNullOrWhiteSpace(s.ProcessName) && !string.IsNullOrWhiteSpace(s.TargetDeviceId))
                .Select(s => new AudioScenario
                {
                    ProcessName = s.ProcessName.Trim(),
                    TargetDeviceId = s.TargetDeviceId,
                    TargetDeviceName = s.TargetDeviceName
                })
                .ToList();
        }

        if (_isRunning)
        {
            ProcessForegroundWindow(GetForegroundWindow(), force: true);
        }
    }

    public void Start()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(AutoSwitcher));
        }

        if (_isRunning)
        {
            return;
        }

        // Out-of-context foreground hook keeps overhead low and avoids injecting into target processes.
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _foregroundChangedHandler,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (_foregroundHook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize foreground window hook.");
        }

        _isRunning = true;
        _lastWindow = IntPtr.Zero;
        _currentDeviceId = string.Empty;
        _resyncTimer.Start();
        LogMessage?.Invoke("AutoSwitcher started.");
        ProcessForegroundWindow(GetForegroundWindow(), force: true);
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _resyncTimer.Stop();

        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        _lastWindow = IntPtr.Zero;
        LogMessage?.Invoke("AutoSwitcher stopped.");
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (!_isRunning || hWnd == IntPtr.Zero)
        {
            return;
        }

        ProcessForegroundWindow(hWnd);
    }

    private void ProcessForegroundWindow(IntPtr hWnd, bool force = false)
    {
        lock (_processLock)
        {
            if (!_isRunning || hWnd == IntPtr.Zero || (!force && hWnd == _lastWindow))
            {
                return;
            }

            try
            {
                GetWindowThreadProcessId(hWnd, out var processId);
                if (processId == 0)
                {
                    return;
                }

                string processName;
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch
                {
                    return;
                }

                AudioScenario? matchedScenario;
                bool fallbackEnabled;
                string speakerDeviceId;

                lock (_configLock)
                {
                    matchedScenario = _scenarioSnapshot.FirstOrDefault(s =>
                        s.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) ||
                        s.ProcessName.Equals($"{processName}.exe", StringComparison.OrdinalIgnoreCase));
                    fallbackEnabled = _fallbackEnabled;
                    speakerDeviceId = _speakerDeviceId;
                }

                if (matchedScenario != null)
                {
                    SwitchDevice(matchedScenario.TargetDeviceId, $"Active window: {processName} -> {matchedScenario.TargetDeviceName}");
                }
                else if (fallbackEnabled && !string.IsNullOrWhiteSpace(speakerDeviceId))
                {
                    SwitchDevice(speakerDeviceId, $"Fallback: {processName} -> Speakers");
                }

                _lastWindow = hWnd;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AutoSwitcher error: {ex.Message}");
            }
        }
    }

    private void SwitchDevice(string deviceId, string reason)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.Equals(_currentDeviceId, deviceId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            AudioDeviceManager.SetDefaultDevice(deviceId);
            _currentDeviceId = deviceId;
            LogMessage?.Invoke(reason);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Switch error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _resyncTimer.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
