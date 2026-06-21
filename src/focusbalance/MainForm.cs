using System.Diagnostics;

namespace GameOptimizer;

/// <summary>
/// FocusBalance window. The engine is ACTIVE the whole time the app is open — running it IS turning it on,
/// closing it IS turning it off (and restores every process it touched). No on/off toggle, no tray: this
/// tool is launched for a gaming session and closed afterwards. The window shows what it is doing
/// (foreground app, currently restrained processes, the action log), lets you tune the threshold /
/// strength, and runs a read-only DPC-latency check on demand.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly ProBalanceEngine _engine;
    private readonly ActionLog _log;

    private readonly Label _status = new();
    private readonly Label _foreground = new();
    private readonly ComboBox _demoteTo = new();
    private readonly NumericUpDown _threshold = new();
    private readonly ListView _demoted = new();
    private readonly ListBox _logView = new();
    private readonly System.Windows.Forms.Timer _ui = new();
    private readonly Button _measure = new();
    private readonly ComboBox _measureDur = new();

    public MainForm(ProBalanceEngine engine, ActionLog log)
    {
        _engine = engine;
        _log = log;

        Text = "FocusBalance — active while open (close to stop & restore)";
        MinimumSize = new Size(760, 560);
        Size = new Size(840, 660);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildLayout();

        _ui.Interval = 1000;
        _ui.Tick += (_, _) => RefreshUi();
        _ui.Start();

        RefreshUi();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // status row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // controls row
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); // demoted list
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); // log
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // disclaimer

        // --- status ---
        var statusPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _status.AutoSize = true;
        _status.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _status.ForeColor = Color.ForestGreen;
        _status.Margin = new Padding(0, 4, 24, 4);
        _foreground.AutoSize = true;
        _foreground.Margin = new Padding(0, 8, 0, 4);
        statusPanel.Controls.Add(_status);
        statusPanel.Controls.Add(_foreground);
        root.Controls.Add(statusPanel);

        // --- controls (tuning + DPC check; no on/off — running is on, closing is off) ---
        // WrapContents = true so controls wrap to the next line on a narrow / high-DPI window instead of clipping.
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };

        controls.Controls.Add(new Label { Text = "Lower to:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _demoteTo.DropDownStyle = ComboBoxStyle.DropDownList;
        _demoteTo.Items.AddRange(new object[] { "BelowNormal (gentle)", "Idle (strongest)" });
        _demoteTo.SelectedIndex = _engine.Config.DemoteTo == ProcessPriorityClass.Idle ? 1 : 0;
        _demoteTo.Width = 150;
        _demoteTo.SelectedIndexChanged += (_, _) =>
            _engine.Config.DemoteTo = _demoteTo.SelectedIndex == 1 ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal;
        controls.Controls.Add(_demoteTo);

        controls.Controls.Add(new Label { Text = "Restrain above (% CPU):", AutoSize = true, Margin = new Padding(20, 8, 4, 0) });
        _threshold.Minimum = 1;
        _threshold.Maximum = 90;
        _threshold.Value = (decimal)_engine.Config.DemotePercent;
        _threshold.Width = 60;
        _threshold.ValueChanged += (_, _) =>
        {
            _engine.Config.DemotePercent = (double)_threshold.Value;
            _engine.Config.RestorePercent = Math.Max(1, (double)_threshold.Value * 0.4);
        };
        controls.Controls.Add(_threshold);

        // --- DPC latency check (read-only diagnostic, shares the DpcLatencyMonitor engine) ---
        controls.Controls.Add(new Label { Text = "    |    DPC check:", AutoSize = true, Margin = new Padding(16, 8, 4, 0) });
        _measureDur.DropDownStyle = ComboBoxStyle.DropDownList;
        _measureDur.Items.AddRange(new object[] { "15s", "30s", "60s" });
        _measureDur.SelectedIndex = 1;
        _measureDur.Width = 100;          // wide enough that "30s"/"60s" + arrow aren't clipped (high-DPI safe)
        _measureDur.DropDownWidth = 100;
        controls.Controls.Add(_measureDur);
        _measure.Text = "Measure DPC latency";
        _measure.AutoSize = true;
        _measure.Margin = new Padding(4, 5, 4, 4);
        _measure.Click += async (_, _) => await MeasureDpcAsync();
        controls.Controls.Add(_measure);

        root.Controls.Add(controls);

        // --- currently restrained list ---
        _demoted.Dock = DockStyle.Fill;
        _demoted.View = View.Details;
        _demoted.FullRowSelect = true;
        _demoted.GridLines = true;
        _demoted.Columns.Add("Process", 220);
        _demoted.Columns.Add("PID", 70, HorizontalAlignment.Right);
        _demoted.Columns.Add("CPU %", 80, HorizontalAlignment.Right);
        _demoted.Columns.Add("Original", 120);
        _demoted.Columns.Add("Now", 120);
        var demotedGroup = new GroupBox { Text = "Currently restrained (restored automatically)", Dock = DockStyle.Fill, Padding = new Padding(8) };
        demotedGroup.Controls.Add(_demoted);
        root.Controls.Add(demotedGroup);

        // --- action log ---
        _logView.Dock = DockStyle.Fill;
        _logView.Font = new Font("Consolas", 9f);
        var logGroup = new GroupBox { Text = "Action log (every change is reversible)", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logGroup.Controls.Add(_logView);
        root.Controls.Add(logGroup);

        // --- honest disclaimer ---
        var disclaimer = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 50,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 6, 0, 0),
            Text = "Active while this window is open. Close it to stop and restore everything. It only LOWERS the "
                 + "priority of CPU-hungry BACKGROUND apps — it never touches the game or anything you've used (those "
                 + "stay foreground-protected), and never anti-cheat services. It reduces contention; it does not add "
                 + "FPS by magic. If nothing in the background misbehaves, it does nothing (correct).",
        };
        root.Controls.Add(disclaimer);

        Controls.Add(root);
    }

    private void RefreshUi()
    {
        int restrained = _engine.CurrentlyDemoted().Count;
        _status.Text = $"● Active — protecting foreground, restraining {restrained} background app(s)";
        _foreground.Text = $"Foreground (never touched): {_engine.ForegroundName}";

        var rows = _engine.CurrentlyDemoted();
        _demoted.BeginUpdate();
        _demoted.Items.Clear();
        foreach (var d in rows)
        {
            var item = new ListViewItem(d.Name);
            item.SubItems.Add(d.Pid.ToString());
            item.SubItems.Add($"{d.CpuPercent:0.#}");
            item.SubItems.Add(d.Original.ToString());
            item.SubItems.Add(d.Now.ToString());
            _demoted.Items.Add(item);
        }
        _demoted.EndUpdate();

        var snap = _log.Snapshot();
        _logView.BeginUpdate();
        _logView.Items.Clear();
        _logView.Items.AddRange(snap);
        _logView.EndUpdate();
    }

    // ---- DPC latency check (reuses the DpcLatencyMonitor engine; read-only) -------------------------
    private async Task MeasureDpcAsync()
    {
        int sec = (_measureDur.SelectedItem as string) switch { "15s" => 15, "60s" => 60, _ => 30 };
        _measure.Enabled = false;
        string prev = _measure.Text;
        _measure.Text = $"Measuring… {sec}s";
        try
        {
            string report = await Task.Run(() => RunDpc(sec));
            ShowReport(report);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "DPC measurement failed:\n" + ex.Message +
                "\n\n(Another kernel ETW logger may be running, or security software blocked it.)",
                "FocusBalance", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _measure.Text = prev; _measure.Enabled = true; }
    }

    private static string RunDpc(int seconds)
    {
        var mon = new DpcLatencyMonitor();
        try
        {
            using var stop = new System.Threading.Timer(_ => { try { mon.Stop(); } catch { } },
                                                        null, seconds * 1000, System.Threading.Timeout.Infinite);
            var sw = Stopwatch.StartNew();
            mon.Run(); // blocks on the ETW thread until the timer stops the session
            sw.Stop();
            return mon.BuildReportText(sw.Elapsed, 15);
        }
        finally { mon.Dispose(); }
    }

    private void ShowReport(string text)
    {
        var f = new Form
        {
            Text = "DPC / ISR latency report",
            Size = new Size(840, 560),
            StartPosition = FormStartPosition.CenterParent,
            Font = new Font("Segoe UI", 9f),
        };
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), Text = text,
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => f.Close();
        var copy = new Button { Text = "Copy", AutoSize = true };
        copy.Click += (_, _) => { try { Clipboard.SetText(text); } catch { } };
        bottom.Controls.Add(close);
        bottom.Controls.Add(copy);
        f.Controls.Add(box);
        f.Controls.Add(bottom);
        f.Show(this); // non-modal: FocusBalance keeps restraining while you read
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window IS the off switch: stop the engine, restore every touched process, exit.
        _ui.Stop();
        _engine.Dispose();
        base.OnFormClosing(e);
    }
}
