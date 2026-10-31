using System.Runtime.InteropServices;

namespace ClaudeVpnGuard.Core;

internal static class NativeMethods
{
    [DllImport("ntdll.dll")]
    public static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    public static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public const uint PROCESS_SUSPEND_RESUME = 0x0800;
    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int WM_SYSCOMMAND = 0x112;
    public const int HTCAPTION = 0x2;
    public const int SC_MINIMIZE = 0xF020;
    public const int SC_RESTORE = 0xF120;
    public const int GWL_STYLE = -16;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_SYSMENU = 0x00080000;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    public const int DWMWCP_ROUND = 2;

    public static void StartWindowDrag(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    public static void EnableRoundedCorners(IntPtr hwnd)
    {
        try
        {
            var pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // older Windows builds
        }
    }

    /// <summary>
    /// Keep custom chrome but restore DWM minimize/restore transitions
    /// (same family of animation Cursor / normal Win11 apps use).
    /// </summary>
    public static void EnableNativeWindowTransitions(IntPtr hwnd)
    {
        try
        {
            var style = GetWindowLong(hwnd, GWL_STYLE);
            style |= WS_THICKFRAME | WS_MINIMIZEBOX | WS_SYSMENU;
            SetWindowLong(hwnd, GWL_STYLE, style);

            // 0 = allow DWM transitions (do not force-disable)
            var disable = 0;
            DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disable, sizeof(int));
        }
        catch
        {
            // ignore on older hosts
        }
    }

    public static void MinimizeNative(IntPtr hwnd)
    {
        // SC_MINIMIZE goes through the same syscommand path as OS caption buttons
        SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_MINIMIZE, IntPtr.Zero);
    }

    public static void RestoreNative(IntPtr hwnd)
    {
        ShowWindow(hwnd, SW_RESTORE);
    }
}
