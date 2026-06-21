using System.Diagnostics;
using GameOptimizer;

// Smoke test for ProBalanceEngine. Spawns a CPU-hog child WE own (so no admin needed), then verifies the
// real engine: detects it → restrains it (Idle) → and on Stop() RESTORES it to its original priority.
// This proves the one property that matters most: every change is reversible.

Console.WriteLine("== FocusBalance engine smoke test ==");

// 1) Start a CPU hog (a process we own; not in the protected list).
var hog = Process.Start(new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = "-NoProfile -Command \"while($true){}\"",
    UseShellExecute = false,
    CreateNoWindow = true,
})!;
Console.WriteLine($"hog started: powershell pid {hog.Id} (busy loop)");

int rc = 1;
try
{
    var original = SafePriority(hog.Id);
    Console.WriteLine($"hog original priority: {original}");

    var log = new ActionLog(logDir: null);
    var engine = new ProBalanceEngine(new ProBalanceEngine.Settings
    {
        DemotePercent = 3.0,    // a single pegged core is ~100/N% of total — well above this
        RestorePercent = 1.0,
        SustainTicks = 1,
        DemoteTo = ProcessPriorityClass.Idle,
        Interval = TimeSpan.FromMilliseconds(400),
        Grace = TimeSpan.Zero, // no startup grace in the test so the hog is evaluated immediately
    }, log);

    engine.Start();

    // 2) Wait for the engine to restrain the hog.
    bool restrained = WaitUntil(TimeSpan.FromSeconds(12), () =>
        engine.CurrentlyDemoted().Any(d => d.Pid == hog.Id));

    var afterDemote = SafePriority(hog.Id);
    Console.WriteLine($"restrained by engine: {restrained}; hog priority now: {afterDemote}");

    // 3) Turn the engine off — this must restore the hog to its ORIGINAL priority.
    engine.Stop();
    Thread.Sleep(300);
    var afterRestore = SafePriority(hog.Id);
    Console.WriteLine($"after Stop(): hog priority restored to: {afterRestore}");

    engine.Dispose();

    bool pass = restrained
                && afterDemote == ProcessPriorityClass.Idle
                && afterRestore == original;
    Console.WriteLine();
    Console.WriteLine(pass ? "RESULT: PASS  (detect → restrain → full restore)"
                          : "RESULT: FAIL");
    Console.WriteLine("--- action log ---");
    foreach (var line in log.Snapshot().Reverse()) Console.WriteLine("  " + line);
    rc = pass ? 0 : 1;
}
finally
{
    try { if (!hog.HasExited) hog.Kill(true); } catch { }
}
return rc;

static ProcessPriorityClass SafePriority(int pid)
{
    try { using var p = Process.GetProcessById(pid); return p.PriorityClass; }
    catch { return ProcessPriorityClass.Normal; }
}

static bool WaitUntil(TimeSpan timeout, Func<bool> cond)
{
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < timeout)
    {
        if (cond()) return true;
        Thread.Sleep(200);
    }
    return cond();
}
