using System.Runtime.InteropServices;

namespace GameOptimizer;

/// <summary>
/// Thin P/Invoke layer. We only need to know which process owns the foreground window
/// (= the app the user is actively using, e.g. the game) so we never restrain it.
/// </summary>
internal static class Native
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// PID of the process that owns the current foreground window, or 0 if none / unavailable.
    /// This is the app the user is looking at right now — it (and our own process) are never touched.
    /// </summary>
    public static int ForegroundProcessId()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        return GetWindowThreadProcessId(hwnd, out uint pid) == 0 && pid == 0 ? 0 : (int)pid;
    }
}
