using System.Text;

namespace GameOptimizer;

/// <summary>
/// Append-only record of every priority change FocusBalance makes (demote / restore), kept both in
/// memory (for the UI) and on disk (so you can audit what it did across a whole gaming session).
/// Reversibility is the whole point of this tool, so every action is logged with its before/after value.
/// </summary>
internal sealed class ActionLog
{
    private readonly object _gate = new();
    private readonly LinkedList<string> _recent = new();   // newest first
    private readonly int _maxRecent;
    private readonly string? _file;

    public ActionLog(string? logDir, int maxRecent = 300)
    {
        _maxRecent = maxRecent;
        if (logDir != null)
        {
            try
            {
                Directory.CreateDirectory(logDir);
                _file = Path.Combine(logDir, $"focusbalance-{DateTime.Now:yyyyMMdd}.log");
            }
            catch { _file = null; } // logging to disk is best-effort; never crash the tool over it
        }
    }

    public void Write(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {message}";
        lock (_gate)
        {
            _recent.AddFirst(line);
            while (_recent.Count > _maxRecent) _recent.RemoveLast();
            if (_file != null)
            {
                try { File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8); } catch { }
            }
        }
    }

    /// <summary>Snapshot of recent lines, newest first, for the UI.</summary>
    public string[] Snapshot()
    {
        lock (_gate) return _recent.ToArray();
    }
}
