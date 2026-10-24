using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace DeepseekDesktopAgent;

internal static class Native
{
    public const int GWL_STYLE = -16;

    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_THICKFRAME = 0x00040000L;
    public const long WS_SYSMENU = 0x00080000L;
    public const long WS_MINIMIZEBOX = 0x00020000L;
    public const long WS_MAXIMIZEBOX = 0x00010000L;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    public const uint GA_ROOT = 2;

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public const int BITSPIXEL = 12;
    public const int VREFRESH = 116;

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYUP = 0x0105;

    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_F4 = 0x73;
    public const int VK_W = 0x57;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool IsEmpty => Width <= 0 || Height <= 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT lpPoints, uint cPoints);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static string GetWindowTitle(IntPtr hWnd)
    {
        var len = GetWindowTextLength(hWnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static List<IntPtr> EnumTopLevelWindows()
    {
        var list = new List<IntPtr>();
        EnumWindows((h, _) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    public static double GetScaleForWindow(IntPtr hWnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hWnd);
            if (dpi > 0) return dpi / 96.0;
        }
        catch { }
        return 1.0;
    }

    public static double GetPrimaryScale()
    {
        try
        {
            var dpi = GetDpiForSystem();
            if (dpi > 0) return dpi / 96.0;
        }
        catch { }
        return 1.0;
    }

    public static double GetScaleAtPoint(int x, int y)
    {
        try
        {
            var pt = new POINT { X = x, Y = y };
            var monitor = MonitorFromPoint(pt, 2);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dx, out _) == 0 && dx > 0)
                return dx / 96.0;
        }
        catch { }
        return GetPrimaryScale();
    }

    public static void LogicalToPhysical(int logicalX, int logicalY, out int physicalX, out int physicalY)
    {
        var scale = GetPrimaryScale();
        physicalX = (int)Math.Round(logicalX * scale);
        physicalY = (int)Math.Round(logicalY * scale);
    }

    public static (int X, int Y) PhysicalToLogical(int physicalX, int physicalY)
    {
        var scale = GetPrimaryScale();
        return ((int)Math.Round(physicalX / scale), (int)Math.Round(physicalY / scale));
    }

    public static (int X, int Y, int W, int H) GetPrimaryScreenRect()
    {
        return (0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    public static (int Width, int Height, int Bits, int Refresh) GetDisplayInfo()
    {
        var (_, _, w, h) = GetPrimaryScreenRect();
        var hdc = GetDC(IntPtr.Zero);
        int bits = 32, refresh = 60;
        if (hdc != IntPtr.Zero)
        {
            bits = GetDeviceCaps(hdc, BITSPIXEL);
            refresh = GetDeviceCaps(hdc, VREFRESH);
            ReleaseDC(IntPtr.Zero, hdc);
        }
        if (refresh <= 1) refresh = 60;
        return (w, h, bits, refresh);
    }

    public static Bitmap CaptureScreenRegion(int x, int y, int width, int height)
    {
        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Bitmap CaptureWindow(IntPtr hWnd)
    {
        GetWindowRect(hWnd, out var rect);
        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);
        Bitmap? bmp = null;
        try
        {
            bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            var hdc = g.GetHdc();
            try { PrintWindow(hWnd, hdc, 2); }
            finally { g.ReleaseHdc(hdc); }
            if (IsMostlyBlank(bmp))
            {
                bmp.Dispose();
                bmp = CaptureScreenRegion(rect.Left, rect.Top, width, height);
            }
        }
        catch
        {
            bmp?.Dispose();
            bmp = CaptureScreenRegion(rect.Left, rect.Top, width, height);
        }
        return bmp;
    }

    private static bool IsMostlyBlank(Bitmap bmp)
    {
        int nonBlack = 0, total = 0;
        var stepX = Math.Max(1, bmp.Width / 20);
        var stepY = Math.Max(1, bmp.Height / 20);
        for (var y = 0; y < bmp.Height; y += stepY)
        {
            for (var x = 0; x < bmp.Width; x += stepX)
            {
                var c = bmp.GetPixel(x, y);
                total++;
                if (c.R > 8 || c.G > 8 || c.B > 8) nonBlack++;
            }
        }
        return total == 0 || nonBlack * 100 / total < 1;
    }

    public static void ForceFocus(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;
        var root = GetAncestor(hWnd, GA_ROOT);
        if (root == IntPtr.Zero) root = hWnd;
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(hWnd, out _);
        var attached = false;
        try
        {
            if (currentThread != targetThread)
                attached = AttachThreadInput(currentThread, targetThread, true);
            ShowWindow(root, SW_RESTORE);
            SetForegroundWindow(root);
            SetFocus(hWnd);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, targetThread, false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    public static bool IsBrowserFocused(IntPtr browser)
    {
        if (browser == IntPtr.Zero || !IsWindow(browser)) return false;
        var fg = GetForegroundWindow();
        if (fg == browser) return true;
        if (fg == IntPtr.Zero) return false;

        var focus = IntPtr.Zero;
        var tid = GetWindowThreadProcessId(fg, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(tid, ref info)) focus = info.hwndFocus;
        if (focus == IntPtr.Zero) focus = fg;

        for (var p = focus; p != IntPtr.Zero; p = GetParent(p))
        {
            if (p == browser) return true;
        }
        return false;
    }

    public static (int X, int Y) GetCursorPhysical()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public static (int X, int Y) GetCursorLogical()
    {
        var (x, y) = GetCursorPhysical();
        return PhysicalToLogical(x, y);
    }

    public static void ClickMouse(int physicalX, int physicalY, int mode)
    {
        SetCursorPos(physicalX, physicalY);
        Thread.Sleep(30);
        switch (mode)
        {
            case 0:
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                break;
            case 1:
                for (var i = 0; i < 2; i++)
                {
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(40);
                }
                break;
            case 2:
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                break;
            case 3:
                for (var i = 0; i < 2; i++)
                {
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(40);
                }
                break;
        }
    }

    public static void PressKeys(params int[] vkCodes)
    {
        var keys = vkCodes.Where(v => v > 0).Distinct().ToArray();
        foreach (var vk in keys)
            keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        for (var i = keys.Length - 1; i >= 0; i--)
            keybd_event((byte)keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}

internal sealed class KeyboardCloseGuard : IDisposable
{
    private readonly Native.HookProc _proc;
    private IntPtr _hook;
    public Func<IntPtr> GetBrowserWindow { get; set; } = () => IntPtr.Zero;
    public bool Enabled { get; set; } = true;

    public KeyboardCloseGuard()
    {
        _proc = Callback;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (Enabled && nCode >= 0 &&
                (wParam == (IntPtr)Native.WM_KEYDOWN || wParam == (IntPtr)Native.WM_SYSKEYDOWN))
            {
                var browser = GetBrowserWindow();
                if (browser != IntPtr.Zero)
                {
                    if (Native.IsBrowserFocused(browser))
                    {
                        var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                        var alt = (data.flags & 0x20) != 0;
                        var ctrl = (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0;
                        if ((data.vkCode == Native.VK_F4 && alt) || (data.vkCode == Native.VK_W && ctrl))
                            return (IntPtr)1;
                    }
                }
            }
        }
        catch { }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}
