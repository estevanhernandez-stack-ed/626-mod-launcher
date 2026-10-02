using System.IO;
using System.Runtime.InteropServices;

namespace ModManager.App.Services;

/// <summary>
/// The notification-area icon behind close-to-tray (B1). Plain Shell_NotifyIcon over a hidden
/// top-level window: WinUI 3 has no tray API, and this is small enough not to justify a package.
///
/// <para>Left-click opens the window; right-click shows Open / Quit. Both are raised as events on
/// the UI thread (the hidden window is created on it, so its window procedure runs there); the
/// shell marshals onward through its DispatcherQueue so nothing re-enters WinUI from inside the
/// popup menu's modal loop.</para>
///
/// <para>The hidden window is top-level rather than message-only on purpose: only top-level windows
/// receive the "TaskbarCreated" broadcast, which is how the icon comes back after Explorer
/// restarts. It is never shown, so it never appears in Alt+Tab.</para>
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public event Action? OpenRequested;
    public event Action? QuitRequested;

    /// <summary>Raised when Explorer restarted and the icon could not be put back. A hidden window
    /// with no icon has no way back, so the shell shows it.</summary>
    public event Action? Lost;

    /// <summary>Whether the icon is in the notification area right now. Hiding the window is only
    /// safe while this is true.</summary>
    public bool IsAdded => _added;

    private const string ClassName = "ModManager.App.TrayIcon";
    private const uint WM_NULL = 0x0000;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_TRAY = 0x8000 + 1;   // WM_APP + 1: the icon's callback message
    private const uint IdOpen = 1, IdQuit = 2;

    // ONE window procedure for the process, held in a static: the class is registered once and the
    // native side keeps calling this pointer for as long as any window of the class exists. A
    // per-instance delegate would be collected under a class that outlives it.
    private static readonly WndProc Proc = WindowProc;
    private static TrayIcon? _current;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly string _tip;
    private readonly uint _taskbarCreated;
    private bool _added;
    private bool _disposed;

    public TrayIcon(string? iconPath, string tooltip)
    {
        _tip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        var hInstance = GetModuleHandleW(null);

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        // A second registration (the setting toggled off then on) fails with "class already exists",
        // which is fine: the class and its static procedure are the same.
        RegisterClassExW(ref wc);

        _hwnd = CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Couldn't create the tray icon's window (error {Marshal.GetLastWin32Error()}).");
        _current = this;

        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        if (iconPath is not null && File.Exists(iconPath))
        {
            _icon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON,
                GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
            _ownsIcon = _icon != IntPtr.Zero;
        }
        if (_icon == IntPtr.Zero) _icon = LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);

        // Refuse rather than exist without an icon: that would let the window hide with no way back.
        if (!Add())
        {
            Dispose();
            throw new InvalidOperationException("Couldn't add the launcher's icon to the notification area.");
        }
    }

    private bool Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        // Shell_NotifyIcon can report failure while Explorer is busy or starting, sometimes after the
        // icon went in anyway. MODIFY succeeds exactly when it did (and a second ADD would then fail
        // and leave a ghost icon nothing deletes); otherwise one more ADD.
        return _added = Shell_NotifyIconW(NIM_ADD, ref data)
                        || Shell_NotifyIconW(NIM_MODIFY, ref data)
                        || Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private NOTIFYICONDATAW Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_TRAY,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var self = _current;
        if (self is not null && hwnd == self._hwnd)
        {
            if (msg == WM_TRAY)
            {
                var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouse == WM_LBUTTONUP) self.OpenRequested?.Invoke();
                else if (mouse == WM_RBUTTONUP || mouse == WM_CONTEXTMENU) self.ShowMenu();
                return IntPtr.Zero;
            }
            if (msg == self._taskbarCreated && self._taskbarCreated != 0)
            {
                // Explorer restarted and took every icon with it. Put ours back, or say it is gone.
                if (!self.Add()) self.Lost?.Invoke();
                return IntPtr.Zero;
            }
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenuW(menu, MF_STRING, (UIntPtr)IdOpen, "Open 626 Mod Launcher");
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)IdQuit, "Quit");
            SetMenuDefaultItem(menu, IdOpen, 0);

            GetCursorPos(out var pt);
            // The documented dance: without the foreground call the menu never dismisses on an
            // outside click, and without the trailing WM_NULL it can need two clicks next time.
            SetForegroundWindow(_hwnd);
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd == IdOpen) OpenRequested?.Invoke();
            else if (cmd == IdQuit) QuitRequested?.Invoke();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            // Without NIM_DELETE the icon lingers until the user's pointer passes over it.
            var data = Data(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        if (_ownsIcon) DestroyIcon(_icon);
        DestroyWindow(_hwnd);
        if (ReferenceEquals(_current, this)) _current = null;
    }

    // ---- Win32 ----

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;
    private const int IDI_APPLICATION = 32512;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr iconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
