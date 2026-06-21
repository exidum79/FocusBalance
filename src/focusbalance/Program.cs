using GameOptimizer;

// ========================================================================
//  FocusBalance (GameOptimizer)
//  ProBalance-style background restraint, reversible by design.
//
//  While you use a foreground app (e.g. a game), any OTHER process that hogs CPU has its priority
//  temporarily LOWERED, then restored when it calms down, comes to the foreground, exits, or you turn
//  the tool off. Nothing is killed; nothing is raised. System / shell / security / audio processes and
//  the foreground app itself are never touched. On a single-CCD CPU (Ryzen 9600X) this matters far more
//  than pinning core affinity. It reduces contention — it does NOT magically add FPS.
// ========================================================================

ApplicationConfiguration.Initialize();

// Admin is required to change other processes' priority. The manifest already forces an elevation
// prompt, so this is a belt-and-suspenders check rather than the primary gate.
if (!IsElevated())
{
    MessageBox.Show(
        "FocusBalance needs administrator rights to lower the priority of background processes.\n\n" +
        "Right-click the .exe and choose \"Run as administrator\".",
        "FocusBalance", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    return;
}

string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
var log = new ActionLog(logDir);
var engine = new ProBalanceEngine(new ProBalanceEngine.Settings(), log);
engine.Start(); // on by default; toggle from the window / tray

using var form = new MainForm(engine, log);
Application.Run(form);

static bool IsElevated()
{
    try
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    catch { return false; }
}
