using System.Diagnostics;

namespace Cacad;

/// <summary>Dev hook: where the UI thread's time goes, by task, logged every 5 s when CACAD_UIPROF names a file.</summary>
public static class UiProfile
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("CACAD_UIPROF");
    public static bool On => Path != null;
    private static readonly Dictionary<string, (double ms, int n)> Totals = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double _flushAt = 5000;

    /// <summary>Record time spent off the UI thread too (summed with the rest).</summary>
    public static void Note(string what, double ms)
    {
        if (!On) return;
        lock (Totals) { Totals.TryGetValue(what, out var v); Totals[what] = (v.ms + ms, v.n + 1); }
    }

    public static void Time(string what, Action a)
    {
        if (!On) { a(); return; }
        double t0 = Clock.Elapsed.TotalMilliseconds;
        a();
        double ms = Clock.Elapsed.TotalMilliseconds - t0;
        Totals.TryGetValue(what, out var v);
        Totals[what] = (v.ms + ms, v.n + 1);
        if (Clock.Elapsed.TotalMilliseconds > _flushAt)
        {
            _flushAt = Clock.Elapsed.TotalMilliseconds + 5000;
            var line = string.Join(" | ", Totals.OrderByDescending(kv => kv.Value.ms).Select(kv => $"{kv.Key} {kv.Value.ms:0}ms/{kv.Value.n}"));
            try { File.AppendAllText(Path!, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); } catch { }
            Totals.Clear();
        }
    }
}

