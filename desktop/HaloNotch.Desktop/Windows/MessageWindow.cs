using System.Runtime.InteropServices;
using static HaloNotch.Win.Native;

namespace HaloNotch.Win;

/// Hidden message-only window: receives the global hotkey and clipboard-change notifications.
/// Created on the UI thread, whose message loop (Avalonia's) delivers the messages.
sealed class MessageWindow
{
    public static readonly MessageWindow Shared = new();

    public event Action? HotKey;
    public event Action? ClipboardChanged;
    public IntPtr Handle { get; }

    readonly WndProc proc;     // kept alive: Windows calls it through a native pointer

    MessageWindow()
    {
        proc = OnMessage;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = "HaloNotchMessages",
        };
        RegisterClassExW(ref wc);
        Handle = CreateWindowExW(0, wc.lpszClassName, "", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */,
                                 IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        AddClipboardFormatListener(Handle);
    }

    /// Ctrl+Alt+V (Ctrl+Alt is AltGr on many layouts, but V has no AltGr symbol on common ones).
    public bool RegisterHotKey() => Native.RegisterHotKey(Handle, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x56);

    IntPtr OnMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WM_HOTKEY) HotKey?.Invoke();
        else if (msg == WM_CLIPBOARDUPDATE) ClipboardChanged?.Invoke();
        return DefWindowProcW(hwnd, msg, w, l);
    }
}
