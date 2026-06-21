namespace GameOptimizer;

/// <summary>
/// The never-touch list. FocusBalance only ever LOWERS priority, but lowering the priority of a
/// system / shell / security process can cause stalls, input lag, or instability — exactly what we are
/// trying to avoid. These are excluded unconditionally, on top of the runtime rules (foreground app,
/// our own process). Names are matched case-insensitively WITHOUT the ".exe".
/// </summary>
internal static class ProtectedProcesses
{
    // Kernel / session / shell / security infrastructure. Demoting any of these is never worth it.
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- core OS / session ---
        "system", "registry", "idle", "memory compression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "fontdrvhost", "dwm", "sihost", "ctfmon", "taskhostw", "runtimebroker",
        // --- shell (demoting Explorer makes the desktop/taskbar feel broken) ---
        "explorer", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp",
        // --- Windows security / Defender (lowering AV priority is both unsafe and unhelpful) ---
        "msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray", "mpdefendercoreservice",
        // --- audio (priority drops here cause crackle/dropouts — the opposite of "smooth") ---
        "audiodg",
        // --- anti-cheat services (background, never foreground; NEVER poke these — they flag tampering) ---
        "vgc", "vgtray", "vgk",                                  // Riot Vanguard
        "easyanticheat", "easyanticheat_eos", "easyanticheat_x64", // EasyAntiCheat
        "beservice", "beservice_x64", "bedaisy",                 // BattlEye
        "ace-base", "ace-tray", "acewebaccel", "sguard", "sguardsvc", "sguard64", // HoYoverse / NetEase ACE
        "anticheatexpert", "mhyprot", "mhyprot2", "mhyprot3",    // miHoYo/HoYoverse protect
        "gamemon", "gamemon64", "npggnt", "gameguard",           // nProtect GameGuard
        "faceitclient", "faceitservice",                         // FACEIT
        // --- our own footprint ---
        "focusbalance",
    };

    public static bool IsProtected(string processName) => Names.Contains(processName);

    /// <summary>Read-only view for showing the user exactly what is excluded (honesty / no hidden behavior).</summary>
    public static IReadOnlyCollection<string> All => Names;
}
