using System.Text;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace GameOptimizer;

/// <summary>
/// LatencyMon-style DPC/ISR execution-time monitor. Opens an NT kernel ETW session and records, per driver,
/// how long its Deferred Procedure Calls and Interrupt Service Routines take to execute. A driver with very
/// long DPC/ISR times is the classic software cause of stutter and audio dropouts that FPS counters can't
/// explain. This MEASURES execution time — it does not change anything (read-only diagnostic).
/// </summary>
internal sealed class DpcLatencyMonitor : IDisposable
{
    public sealed class DriverStat
    {
        public string Name = "";
        public long DpcCount;
        public double DpcTotalMs;
        public double DpcMaxMs;
        public long DpcOver500us;   // how OFTEN a long DPC happens — the signal that actually matters
        public long DpcOver1ms;
        public long IsrCount;
        public double IsrTotalMs;
        public double IsrMaxMs;
    }

    private readonly ModuleMap _modules = new();
    private readonly Dictionary<string, DriverStat> _stats = new(StringComparer.OrdinalIgnoreCase);
    private TraceEventSession? _session;

    public double WorstDpcMs { get; private set; }
    public string WorstDpcDriver { get; private set; } = "(none)";
    public double WorstIsrMs { get; private set; }
    public string WorstIsrDriver { get; private set; } = "(none)";

    /// <summary>Blocks on the ETW processing thread until <see cref="Stop"/> is called.</summary>
    public void Run()
    {
        // NT Kernel Logger is a single, well-known session. Constructing it with this name reclaims/restarts
        // any leftover instance from a previous (crashed) run.
        _session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
        _session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.DeferedProcedureCalls | // (sic) the library spells it with one 'r'
            KernelTraceEventParser.Keywords.Interrupt |
            KernelTraceEventParser.Keywords.ImageLoad);

        var kernel = _session.Source.Kernel;

        // Image load + rundown: build the address→driver map. Both carry ImageLoadTraceData.
        kernel.ImageDCStart += OnImage;   // rundown of already-loaded modules at session start
        kernel.ImageLoad += OnImage;      // modules loaded while we run

        kernel.PerfInfoDPC += OnDpc;
        kernel.PerfInfoThreadedDPC += OnDpc;
        kernel.PerfInfoISR += OnIsr;

        _session.Source.Process(); // returns when Stop()/Dispose() tears the session down
    }

    public void Stop() => _session?.Stop();

    private void OnImage(ImageLoadTraceData d) => _modules.AddOrUpdate(d.ImageBase, d.ImageSize, d.FileName);

    private void OnDpc(DPCTraceData d)
    {
        double ms = d.ElapsedTimeMSec;
        var s = Stat(_modules.Resolve(d.Routine));
        s.DpcCount++;
        s.DpcTotalMs += ms;
        if (ms > s.DpcMaxMs) s.DpcMaxMs = ms;
        if (ms >= 0.5) s.DpcOver500us++;
        if (ms >= 1.0) s.DpcOver1ms++;
        if (ms > WorstDpcMs) { WorstDpcMs = ms; WorstDpcDriver = s.Name; }
    }

    private void OnIsr(ISRTraceData d)
    {
        double ms = d.ElapsedTimeMSec;
        var s = Stat(_modules.Resolve(d.Routine));
        s.IsrCount++;
        s.IsrTotalMs += ms;
        if (ms > s.IsrMaxMs) s.IsrMaxMs = ms;
        if (ms > WorstIsrMs) { WorstIsrMs = ms; WorstIsrDriver = s.Name; }
    }

    private DriverStat Stat(string name)
    {
        if (!_stats.TryGetValue(name, out var s)) { s = new DriverStat { Name = name }; _stats[name] = s; }
        return s;
    }

    /// <summary>Per-driver stats (call after Stop()/Process() has returned — the processing thread is done).</summary>
    public IReadOnlyCollection<DriverStat> Stats => _stats.Values;
    public int ModuleCount => _modules.Count;

    /// <summary>
    /// The full LatencyMon-style report as text (shared by the console tool and the FocusBalance button).
    /// Judged by RECURRING long DPCs, not the single worst spike (a lone giant spike is almost always a
    /// measurement/startup artifact — flagging it would be a false positive).
    /// </summary>
    public string BuildReportText(TimeSpan elapsed, int top)
    {
        var drivers = Stats
            .Where(s => s.DpcCount > 0 || s.IsrCount > 0)
            .OrderByDescending(s => s.DpcMaxMs)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("==================== DPC / ISR REPORT ====================");
        sb.AppendLine($"Duration: {elapsed.TotalSeconds:F0}s   drivers mapped: {ModuleCount}   drivers with activity: {drivers.Count}");
        sb.AppendLine();
        sb.AppendLine($"Highest single DPC: {WorstDpcMs * 1000:F1} us  ({WorstDpcDriver})");
        sb.AppendLine($"Highest single ISR: {WorstIsrMs * 1000:F1} us  ({WorstIsrDriver})");
        sb.AppendLine();
        sb.AppendLine($"Top {top} drivers by max DPC execution time:");
        sb.AppendLine($"{"DRIVER",-26} {"DPC cnt",8} {"DPC max us",11} {"DPC avg us",11} {">0.5ms",7} {"ISR cnt",8} {"ISR max us",11}");
        sb.AppendLine(new string('-', 86));
        foreach (var s in drivers.Take(top))
        {
            double dpcAvgUs = s.DpcCount > 0 ? s.DpcTotalMs / s.DpcCount * 1000 : 0;
            sb.AppendLine($"{Trunc(s.Name, 26),-26} {s.DpcCount,8} {s.DpcMaxMs * 1000,11:F1} {dpcAvgUs,11:F1} {s.DpcOver500us,7} {s.IsrCount,8} {s.IsrMaxMs * 1000,11:F1}");
        }
        sb.AppendLine();

        const int SustainedMinCount = 10;
        var sustained = drivers.Where(d => d.DpcCount >= SustainedMinCount).ToList();
        long recurringOver1ms = sustained.Sum(d => d.DpcOver1ms);
        long recurringOver500 = sustained.Sum(d => d.DpcOver500us);
        var worstSustained = sustained.OrderByDescending(d => d.DpcOver1ms).ThenByDescending(d => d.DpcOver500us)
                                      .FirstOrDefault(d => d.DpcOver500us > 0);
        var spikeDrv = drivers.FirstOrDefault(d => d.Name == WorstDpcDriver);
        double worstUs = WorstDpcMs * 1000;
        bool spikeIsOneOff = spikeDrv == null || spikeDrv.DpcCount < SustainedMinCount || WorstDpcDriver == "Unknown";

        sb.AppendLine("Interpretation (judged by RECURRING long DPCs, not the single worst spike):");
        if (recurringOver1ms >= 3)
            sb.AppendLine($"  [RED]   {recurringOver1ms} DPCs over 1 ms from active drivers (worst: '{worstSustained?.Name}'). Repeated long DPCs are a likely cause of stutter/audio dropouts — update or roll back that driver, or disable the device to confirm.");
        else if (recurringOver500 >= 10)
            sb.AppendLine($"  [WATCH] {recurringOver500} DPCs over 0.5 ms from active drivers (worst: '{worstSustained?.Name}'). Elevated under load.");
        else
            sb.AppendLine($"  [OK]    No recurring long DPCs from active drivers ({recurringOver500} over 0.5 ms, {recurringOver1ms} over 1 ms). Healthy.");
        sb.AppendLine();
        sb.AppendLine($"  Single worst spike: {worstUs:F0} us on '{WorstDpcDriver}'"
                    + (spikeIsOneOff ? $"  (count {spikeDrv?.DpcCount ?? 0} — a one-off/unmapped event; treated as noise, NOT counted in the verdict)."
                                     : "  (this driver is active — see the table above)."));
        sb.AppendLine();
        sb.AppendLine("  Notes: MS guideline is DPC < 100 us; real desktops peak higher, so the verdict counts how OFTEN a");
        sb.AppendLine("  driver exceeds 0.5/1 ms and ignores lone spikes. Measures DPC/ISR only — not GPU frame latency / FPS.");
        return sb.ToString();
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "~";

    public void Dispose()
    {
        try { _session?.Dispose(); } catch { }
        _session = null;
    }
}
