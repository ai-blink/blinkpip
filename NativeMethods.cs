using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HybridPipPlayer;

public static class NativeMethods
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CHILD = 0x40000000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const uint LWA_ALPHA = 0x00000002;

    public const int WM_HOTKEY = 0x0312;
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_SIZING = 0x0214;
    public const int WM_ENTERSIZEMOVE = 0x0231;
    public const int WM_EXITSIZEMOVE = 0x0232;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    // 전역 단축키 ID 및 조합키
    public const int HOTKEY_ID_CLICKTHROUGH = 9001; // Ctrl + Alt + C
    public const int HOTKEY_ID_PIP = 9002;          // Ctrl + Alt + P
    public const int HOTKEY_ID_WALLPAPER = 9003;    // Ctrl + Alt + B
    public const int HOTKEY_ID_AMBIENT = 9004;      // Ctrl + Alt + G
    public const int HOTKEY_ID_LOCK = 9007;         // Ctrl + Alt + L (PIP 잠금)
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int HOTKEY_ID_SUB_UP = 9005;       // Ctrl + Alt + ↑ (자막 크게)
    public const int HOTKEY_ID_SUB_DOWN = 9006;     // Ctrl + Alt + ↓ (자막 작게)
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // 저수준 마우스 훅 — mpv/WebView2 자식 창이 마우스를 가져가도 PIP 를 영상 위에서 끌 수 있게 한다
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTCAPTION = 2;
    public const uint GA_ROOT = 2;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, IntPtr windowTitle);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc fn, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnableWindow(IntPtr hwnd, bool enable);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT pt, int count);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);

    // Windows 11 DWM 둥근 모서리 / 가려진(cloaked) 창 판별
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int attrValue, int attrSize);

    public static void ApplyWin11RoundedCorners(IntPtr hwnd)
    {
        int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        int DWMWCP_ROUND = 2;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, sizeof(int));
    }

    // 마우스 클릭 투과 및 창 전체 투명도 조절. 해제 시 LAYERED 도 걷어 합성 비용을 남기지 않는다.
    public static void SetClickThrough(IntPtr hwnd, bool enable, byte alpha = 180)
    {
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (enable)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~(WS_EX_TRANSPARENT | WS_EX_LAYERED));
        }
    }

    private static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // 배경화면 모드에서 붙일 부모와 z-순서 기준을 돌려준다.
    //  - 24H2 이전: SHELLDLL_DefView 를 가진 최상위 WorkerW 의 "다음" WorkerW (z-순서는 건드리지 않음)
    //  - 24H2 이후: DefView 와 WorkerW 가 모두 Progman 의 자식 → Progman 에 붙이고 DefView 바로 아래에 둔다
    // 실패하면 (0, 0).
    public static (IntPtr parent, IntPtr insertAfter) GetWallpaperParent()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0000, 1000, out _);

        IntPtr legacy = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            // 24H2 에서는 Progman 이 DefView 의 부모로 잡히므로 WorkerW 클래스일 때만 레거시 경로로 본다
            if (ClassOf(top) == "WorkerW" &&
                FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero) != IntPtr.Zero)
            {
                legacy = FindWindowEx(IntPtr.Zero, top, "WorkerW", IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        if (legacy != IntPtr.Zero) return (legacy, IntPtr.Zero);

        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);
        IntPtr childWorker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", IntPtr.Zero);
        if (defView != IntPtr.Zero && childWorker != IntPtr.Zero) return (progman, defView);
        return (IntPtr.Zero, IntPtr.Zero);
    }

    // 포그라운드 창이 (바탕화면·작업표시줄·가려진 창 제외) 모니터 전체를 덮는지
    public static bool IsOtherFullscreenAppForeground(IntPtr self, System.Drawing.Rectangle monitorBounds)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == self || !IsWindowVisible(fg)) return false;
        if (DwmGetWindowAttribute(fg, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        string cls = ClassOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "SHELLDLL_DefView") return false;
        if (!GetWindowRect(fg, out RECT r)) return false;
        return r.Left <= monitorBounds.Left && r.Top <= monitorBounds.Top &&
               r.Right >= monitorBounds.Right && r.Bottom >= monitorBounds.Bottom;
    }
}
