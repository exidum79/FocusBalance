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

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    private const uint GW_OWNER = 4;
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    /// <summary>
    /// PIDs that own at least one VISIBLE, real top-level window — i.e. interactive apps (games, browsers,
    /// anything you'd click into), even while they sit in the background loading. We never restrain these;
    /// only windowless background work (update/index/AV/telemetry workers) is eligible. This keeps a game
    /// from being demoted while it loads before you've focused it. We deliberately do NOT require a window
    /// title (loading/fullscreen game windows can have an empty title) — visible + top-level + non-tiny is
    /// enough; erring toward never-touching an app is the safe direction.
    /// </summary>
    public static HashSet<int> PidsWithVisibleWindows()
    {
        var set = new HashSet<int>();
        EnumWindows((hwnd, _) =>
        {
            // top-level (no owner), visible, and at least a real size (skip 0x0 phantom windows).
            if (IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero &&
                GetWindowRect(hwnd, out RECT r) && (r.R - r.L) >= 64 && (r.B - r.T) >= 64 &&
                GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid != 0)
                set.Add((int)pid);
            return true; // keep enumerating
        }, IntPtr.Zero);
        return set;
    }
}
