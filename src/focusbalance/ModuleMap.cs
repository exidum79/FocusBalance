namespace GameOptimizer;

/// <summary>
/// Maps a kernel routine address (from a DPC/ISR event) back to the driver image that owns it, so we can
/// say "nvlddmkm.sys" instead of "0xfffff803...". Built from the kernel ETW ImageLoad / image-rundown
/// (ImageDCStart) events. All updates and lookups happen on the single ETW processing thread, so no
/// locking is needed here.
/// </summary>
internal sealed class ModuleMap
{
    private readonly struct Module
    {
        public readonly ulong Start;
        public readonly ulong End;     // exclusive
        public readonly string Name;
        public Module(ulong start, ulong end, string name) { Start = start; End = end; Name = name; }
    }

    private readonly List<Module> _mods = new();
    private bool _dirty;

    public void AddOrUpdate(ulong baseAddr, int size, string fileName)
    {
        if (baseAddr == 0 || size <= 0) return;
        string name = LeafName(fileName);
        // Drop any stale entry covering the same base (driver reloaded at a new size, etc.).
        _mods.RemoveAll(m => m.Start == baseAddr);
        _mods.Add(new Module(baseAddr, baseAddr + (ulong)size, name));
        _dirty = true;
    }

    /// <summary>Driver leaf name owning <paramref name="address"/>, or "Unknown" if unmapped.</summary>
    public string Resolve(ulong address)
    {
        if (address == 0) return "Unknown";
        if (_dirty) { _mods.Sort((a, b) => a.Start.CompareTo(b.Start)); _dirty = false; }

        int lo = 0, hi = _mods.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var m = _mods[mid];
            if (address < m.Start) hi = mid - 1;
            else if (address >= m.End) lo = mid + 1;
            else return m.Name;
        }
        return "Unknown";
    }

    public int Count => _mods.Count;

    private static string LeafName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "Unknown";
        int i = path.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 && i < path.Length - 1 ? path[(i + 1)..] : path;
    }
}
