using System.Diagnostics;

namespace Cacad;

/// <summary>Frame timing, written to a log every few seconds when CACAD_PROFILE names a file. Off (and free) otherwise.</summary>
public static class FrameStats
{
    public static readonly string? LogPath = Environment.GetEnvironmentVariable("CACAD_PROFILE");
    public static bool On => LogPath != null;

    private static readonly List<double> Gaps = new(), Render = new(), Present = new(), Capture = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double _lastFrame = -1, _windowStart;
    private static int _gc0 = GC.CollectionCount(0);
    public static string Label = "";

    public static double Now => Clock.Elapsed.TotalMilliseconds;

    public static void Frame(double renderMs, double presentMs, int pixels)
    {
        if (!On) return;
        double now = Now;
        if (_lastFrame >= 0) Gaps.Add(now - _lastFrame);
        _lastFrame = now;
        Render.Add(renderMs);
        Present.Add(presentMs);
        _pixels = pixels;
        if (now - _windowStart > 3000) Flush(now);
    }

    private static int _pixels;

    public static void Captured(double ms) { if (On) Capture.Add(ms); }

    /// <summary>Log a UI-thread step that took long enough to cost a frame.</summary>
    public static void Note(string what, double ms)
    {
        if (!On || ms < 8) return;
        try { File.AppendAllText(LogPath!, $"{DateTime.Now:HH:mm:ss.fff}   slow: {what} {ms:0.0} ms{Environment.NewLine}"); } catch { }
    }

    /// <summary>A frame-free stretch should not count as a long gap.</summary>
    public static void Idle() => _lastFrame = -1;

    private static void Flush(double now)
    {
        static string S(List<double> v) => v.Count == 0 ? "-" :
            $"avg {v.Average():0.0} p95 {v.OrderBy(x => x).ElementAt((int)(v.Count * 0.95)):0.0} max {v.Max():0.0}";
        double secs = (now - _windowStart) / 1000;
        int gc = GC.CollectionCount(0);
        string line = $"{DateTime.Now:HH:mm:ss} [{Label}] fps {Render.Count / secs:0} | gap {S(Gaps)} | render {S(Render)} | present {S(Present)} | capture {S(Capture)} n={Capture.Count} | px {_pixels} | gc0 +{gc - _gc0}";
        try { File.AppendAllText(LogPath!, line + Environment.NewLine); } catch { }
        Gaps.Clear(); Render.Clear(); Present.Clear(); Capture.Clear();
        _gc0 = gc;
        _windowStart = now;
    }
}
