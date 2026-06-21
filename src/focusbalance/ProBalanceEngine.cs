using System.Diagnostics;

namespace GameOptimizer;

/// <summary>
/// ProBalance-style background restraint, reversible by design.
///
/// What it does, exactly: once per tick it looks at every process. A process that is NOT the foreground
/// app, NOT protected, and is sustaining high CPU gets its priority class LOWERED (its original value is
/// saved first). When that process calms down, comes to the foreground, exits, or the engine is turned
/// off, its ORIGINAL priority is restored. Nothing is ever killed and nothing is ever raised above where
/// it started. On a single-CCD 9600X this is the part that actually helps — keeping a background hog from
/// stealing time from the game — far more than pinning core affinity.
///
/// Honest limits (see README): this lowers contention, it does not "boost FPS". If nothing in the
/// background is misbehaving, it does nothing and you will see no change. That is the correct behavior.
/// </summary>
internal sealed class ProBalanceEngine : IDisposable
{
    // Mutable so the UI can adjust thresholds / target live. Already-restrained processes keep the
    // ORIGINAL priority we saved, so changing DemoteTo only affects future restraints — always reversible.
    public sealed class Settings
    {
        /// <summary>% of TOTAL CPU a background process must sustain before it is restrained.</summary>
        public double DemotePercent { get; set; } = 8.0;
        /// <summary>Restore once a restrained process drops below this % of total CPU.</summary>
        public double RestorePercent { get; set; } = 3.0;
        /// <summary>Consecutive ticks over the demote threshold before acting (anti-flap).</summary>
        public int SustainTicks { get; set; } = 2;
        /// <summary>Priority class restrained processes are lowered to.</summary>
        public ProcessPriorityClass DemoteTo { get; set; } = ProcessPriorityClass.BelowNormal;
        /// <summary>Tick period.</summary>
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
    }

    public sealed record DemotedView(int Pid, string Name, double CpuPercent,
                                     ProcessPriorityClass Original, ProcessPriorityClass Now);

    private sealed class Tracked
    {
        public required Process Proc;
        public required string Name;
        public TimeSpan LastCpu;
        public DateTime LastSample;
        public double CpuPercent;
        public int OverThresholdTicks;
        public int UnderThresholdTicks;
        public bool Demoted;
        public ProcessPriorityClass Original;
    }

    private readonly Settings _cfg;
    private readonly ActionLog _log;
    private readonly int _selfPid = Environment.ProcessId;
    private readonly int _cpuCount = Math.Max(1, Environment.ProcessorCount);

    private readonly object _gate = new();
    private readonly Dictionary<int, Tracked> _tracked = new();
    private readonly HashSet<int> _cannotTouch = new();   // pids we failed to read/set — stop retrying

    private System.Threading.Timer? _timer;
    private int _tickBusy;   // 0/1 re-entrancy guard: a slow tick must never overlap the next one
    private volatile bool _enabled;
    private volatile bool _disposed;
    private string _foregroundName = "(none)";
    private int _foregroundPid;
    // Any process that has EVER been the foreground app this session (the game, your browser, anything you
    // actively use). We never change its priority — so we never even open a modify handle to a game, which
    // is what an anti-cheat would flag. Games are foreground when you play them, so this protects them.
    // Touched only on the single-threaded tick, so no lock needed.
    private readonly HashSet<int> _everForeground = new();

    public ProBalanceEngine(Settings cfg, ActionLog log) { _cfg = cfg; _log = log; }

    public bool Enabled => _enabled;
    public string ForegroundName { get { lock (_gate) return _foregroundName; } }
    /// <summary>Live config — the UI may adjust thresholds / demote target at any time.</summary>
    public Settings Config => _cfg;

    public void Start()
    {
        _enabled = true;
        _log.Write($"FocusBalance ON — restrain background > {_cfg.DemotePercent:0.#}% CPU → {_cfg.DemoteTo}; restore < {_cfg.RestorePercent:0.#}%.");
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, _cfg.Interval);
    }

    /// <summary>Turn restraint off and RESTORE every process we touched. Safe to call repeatedly.</summary>
    public void Stop()
    {
        if (!_enabled) return;
        _enabled = false;
        RestoreAll("engine disabled");
        _log.Write("FocusBalance OFF — all restrained processes restored.");
    }

    public IReadOnlyList<DemotedView> CurrentlyDemoted()
    {
        lock (_gate)
        {
            return _tracked.Values
                .Where(t => t.Demoted)
                .Select(t => new DemotedView(t.Proc.Id, t.Name, t.CpuPercent, t.Original, _cfg.DemoteTo))
                .OrderByDescending(d => d.CpuPercent)
                .ToList();
        }
    }

    private void Tick()
    {
        if (_disposed || !_enabled) return;
        if (Interlocked.Exchange(ref _tickBusy, 1) == 1) return; // previous tick still running — skip
        try { TickCore(); }
        catch (Exception ex) { _log.Write($"[tick error] {ex.GetType().Name}: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _tickBusy, 0); }
    }

    private void TickCore()
    {
        int fgPid = Native.ForegroundProcessId();
        var now = DateTime.UtcNow;
        var seen = new HashSet<int>();

        // Resolve the foreground process name once (best-effort) for the UI + so we never restrain it.
        string fgName = "(none)";
        if (fgPid != 0)
        {
            try { using var fp = Process.GetProcessById(fgPid); fgName = fp.ProcessName; } catch { }
        }
        lock (_gate) { _foregroundPid = fgPid; _foregroundName = fgName == "(none)" ? "(none)" : fgName; }
        if (fgPid > 0) _everForeground.Add(fgPid); // remember it so we never touch it again, even on alt-tab

        foreach (var proc in SafeGetProcesses())
        {
            int pid = proc.Id;
            seen.Add(pid);

            // Never touch: ourselves, the foreground app, anything that has EVER been foreground (the game /
            // your apps — so we never open a modify handle an anti-cheat could flag), the idle "process", or
            // anything we already know we cannot access. Protected NAMES are filtered just below.
            if (pid == _selfPid || pid == fgPid || pid == 0 || _everForeground.Contains(pid) || _cannotTouch.Contains(pid)) { proc.Dispose(); continue; }

            TimeSpan cpu;
            string name;
            try { cpu = proc.TotalProcessorTime; name = proc.ProcessName; }
            catch { _cannotTouch.Add(pid); proc.Dispose(); continue; } // protected / exited mid-read

            if (ProtectedProcesses.IsProtected(name)) { proc.Dispose(); continue; }

            Tracked t;
            lock (_gate)
            {
                if (!_tracked.TryGetValue(pid, out t!))
                {
                    t = new Tracked { Proc = proc, Name = name, LastCpu = cpu, LastSample = now };
                    _tracked[pid] = t;
                    continue; // need two samples to compute a rate; measure next tick
                }
            }

            // CPU% of the whole machine since last sample.
            double wallSec = (now - t.LastSample).TotalSeconds;
            if (wallSec > 0)
            {
                double busySec = (cpu - t.LastCpu).TotalSeconds;
                t.CpuPercent = Math.Clamp(busySec / (wallSec * _cpuCount) * 100.0, 0, 100);
            }
            t.LastCpu = cpu;
            t.LastSample = now;

            // This handle is replaced by the one we stored on first sight; dispose the duplicate.
            if (!ReferenceEquals(proc, t.Proc)) proc.Dispose();

            Evaluate(t, fgPid);
        }

        // Restore + forget anything that disappeared (exited) or came to the foreground this tick.
        ReapVanished(seen, fgPid);
    }

    private void Evaluate(Tracked t, int fgPid)
    {
        bool over = t.CpuPercent >= _cfg.DemotePercent;
        bool under = t.CpuPercent < _cfg.RestorePercent;
        t.OverThresholdTicks = over ? t.OverThresholdTicks + 1 : 0;
        t.UnderThresholdTicks = under ? t.UnderThresholdTicks + 1 : 0;

        if (!t.Demoted)
        {
            if (t.OverThresholdTicks >= _cfg.SustainTicks)
                Demote(t);
        }
        else
        {
            // Restore as soon as it calms down for a couple of ticks (it already left foreground checks).
            if (t.UnderThresholdTicks >= _cfg.SustainTicks)
                Restore(t, $"calmed to {t.CpuPercent:0.#}% CPU");
        }
    }

    private void Demote(Tracked t)
    {
        try
        {
            t.Original = t.Proc.PriorityClass;
            if (t.Original == _cfg.DemoteTo) { t.Demoted = true; return; } // already there; track for restore-on-exit only
            t.Proc.PriorityClass = _cfg.DemoteTo;
            t.Demoted = true;
            _log.Write($"restrain  {t.Name} (pid {t.Proc.Id})  {t.Original} → {_cfg.DemoteTo}  @ {t.CpuPercent:0.#}% CPU");
        }
        catch (Exception ex)
        {
            _cannotTouch.Add(t.Proc.Id);
            _log.Write($"[skip] cannot restrain {t.Name} (pid {t.Proc.Id}): {ex.GetType().Name}");
        }
    }

    private void Restore(Tracked t, string reason)
    {
        if (!t.Demoted) return;
        try
        {
            t.Proc.PriorityClass = t.Original;
            _log.Write($"restore   {t.Name} (pid {t.Proc.Id})  → {t.Original}  ({reason})");
        }
        catch (Exception ex)
        {
            _log.Write($"[warn] could not restore {t.Name} (pid {t.Proc.Id}): {ex.GetType().Name} — it may have exited.");
        }
        t.Demoted = false;
        t.OverThresholdTicks = 0;
        t.UnderThresholdTicks = 0;
    }

    private void ReapVanished(HashSet<int> seen, int fgPid)
    {
        List<Tracked>? toDrop = null;
        lock (_gate)
        {
            foreach (var kv in _tracked)
            {
                var t = kv.Value;
                bool gone = !seen.Contains(kv.Key);
                bool nowForeground = kv.Key == fgPid;
                if (gone || nowForeground)
                {
                    (toDrop ??= new()).Add(t);
                }
            }
            if (toDrop != null)
                foreach (var t in toDrop) _tracked.Remove(t.Proc.Id);
        }
        if (toDrop == null) return;
        foreach (var t in toDrop)
        {
            bool exited = t.Proc.HasExitedSafe();
            // If it merely came to the foreground we restore it; if it exited the handle is dead and
            // Restore() will log a benign warning — either way we let the original priority win.
            if (t.Demoted) Restore(t, exited ? "process exited" : "now foreground");
            // Only forget a "cannot touch" verdict when the pid actually EXITED (it may be recycled). For a
            // process that just went foreground (e.g. an anti-cheat-protected game on alt-tab), keep the
            // verdict so we don't re-attempt and re-log [skip] every time it toggles foreground/background.
            if (exited) _cannotTouch.Remove(t.Proc.Id);
            t.Proc.Dispose();
        }
    }

    private void RestoreAll(string reason)
    {
        List<Tracked> all;
        lock (_gate)
        {
            all = _tracked.Values.ToList();
            _tracked.Clear();
        }
        foreach (var t in all)
        {
            if (t.Demoted) Restore(t, reason);
            t.Proc.Dispose();
        }
    }

    private static IEnumerable<Process> SafeGetProcesses()
    {
        try { return Process.GetProcesses(); }
        catch { return Array.Empty<Process>(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        // Final safety net: whatever happens, leave the system exactly as we found it.
        RestoreAll("shutdown");
    }
}

internal static class ProcessExtensions
{
    /// <summary>HasExited without throwing if the handle is already invalid.</summary>
    public static bool HasExitedSafe(this Process p)
    {
        try { return p.HasExited; } catch { return true; }
    }
}
