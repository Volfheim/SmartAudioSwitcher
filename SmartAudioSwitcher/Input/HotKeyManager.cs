using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SmartAudioSwitcher.Input;

public class HotKeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _handle;
    private HwndSource? _source;
    private readonly HashSet<int> _registeredIds = new();

    public event Action<int>? HotKeyPressed;

    public HotKeyManager(IntPtr handle)
    {
        _handle = handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(HwndHook);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            HotKeyPressed?.Invoke(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public bool Register(int id, int vk, KeyModifiers modifiers, out int errorCode)
    {
        if (RegisterHotKey(_handle, id, (uint)modifiers, (uint)vk))
        {
            _registeredIds.Add(id);
            errorCode = 0;
            return true;
        }

        errorCode = Marshal.GetLastWin32Error();
        return false;
    }

    public void Unregister(int id)
    {
        if (_registeredIds.Contains(id))
        {
            UnregisterHotKey(_handle, id);
            _registeredIds.Remove(id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredIds.ToList())
        {
            UnregisterHotKey(_handle, id);
        }

        _registeredIds.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(HwndHook);
        _source?.Dispose();
        GC.SuppressFinalize(this);
    }
}

[Flags]
public enum KeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}
