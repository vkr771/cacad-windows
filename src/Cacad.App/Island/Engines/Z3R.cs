using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Cacad;

/// <summary>Z3R, the app engine. It makes the whole app lighter on your PC without changing what you see:
///
///  1. Partial frames. Most of the time the only thing moving on the resting pill is the companion (breathing,
///     blinking). Z3R redraws just the companion's patch for those frames instead of the whole island, glass and
///     all, and still redraws everything the moment anything else changes.
///  2. Rest mode. After 20 seconds with nothing happening (no pointer on the island, card closed, no toast, no Live
///     Activity, no music), cacad asks Windows to run it in efficiency mode (EcoQoS: slower, cooler, less battery),
///     at a lower priority. Anything happening ends rest mode instantly.
///  3. Memory trim. Entering rest mode, and every 10 minutes in it, unused memory is handed back to Windows (a full
///     garbage collection, then the working set is trimmed), so cacad sits small in Task Manager.
///  4. Sleeping between frames. The island normally ticks with the display, 60 times a second, whenever anything
///     animates. When nothing is in motion and the next frame is a while off (the companion breathing, a material's
///     next step), Z3R stops the ticking and wakes just before that frame is due, so it still lands on a vsync.
///
/// With Z3R off, every frame is a full one, and cacad never rests or trims. Toggle: Settings › Performance.</summary>
public static class Z3R
{
    public static bool On = true;

    // ---------- 2 + 3. rest mode and memory ----------

    private static DateTime _busyAt = DateTime.Now, _trimmedAt = DateTime.MinValue;
    public static bool Resting { get; private set; }

    /// <summary>Called from the island's poll (4 times a second).</summary>
    public static void Update(bool busy)
    {
        if (busy || !On) { _busyAt = DateTime.Now; if (Resting) Wake(); return; }
        if (!Resting && (DateTime.Now - _busyAt).TotalSeconds > 20) Rest();
        if (Resting && (DateTime.Now - _trimmedAt).TotalMinutes > 10) Trim();
    }

    private static void Rest()
    {
        Resting = true;
        Efficiency(true);
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        Trim();
    }

    private static void Wake()
    {
        Resting = false;
        Efficiency(false);
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { }
    }

    /// <summary>Turning the engine off returns the process to normal at once.</summary>
    public static void Apply()
    {
        if (!On && Resting) Wake();
    }

    private static void Trim()
    {
        _trimmedAt = DateTime.Now;
        // Off the UI thread: a compacting collection then hand the pages back.
        Task.Run(() =>
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch { }
        });
    }

    // EcoQoS: PROCESS_POWER_THROTTLING_EXECUTION_SPEED (Windows 10 1709+, efficiency mode on Windows 11).
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottling { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, uint size);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    private static void Efficiency(bool on)
    {
        try
        {
            var t = new PowerThrottling { Version = 1, ControlMask = 1, StateMask = on ? 1u : 0u };
            SetProcessInformation(Process.GetCurrentProcess().Handle, 4 /* ProcessPowerThrottling */, ref t, (uint)Marshal.SizeOf<PowerThrottling>());
        }
        catch { }
    }

    // ---------- 4. sleeping between frames ----------

    private static System.Threading.Timer? _wake;
    private static Action? _wakeAction;
    private static Control? _wakeOn;

    /// <summary>Calls <paramref name="action"/> on the window's thread after <paramref name="seconds"/>.
    /// A newer request replaces an older one.</summary>
    public static void WakeIn(double seconds, Action action, Control on)
    {
        _wakeAction = action;
        _wakeOn = on;
        _wake ??= new System.Threading.Timer(_ =>
        {
            var a = _wakeAction;
            var w = _wakeOn;
            if (a == null || w == null || w.IsDisposed) return;
            try { w.BeginInvoke(a); } catch { }
        });
        _wake.Change(TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
    }

    // ---------- stats for Settings ----------

    private static TimeSpan _cpu0;
    private static DateTime _cpuAt;
    private static int _frames, _partial;
    private static DateTime _fpsAt = DateTime.Now;
    private static double _fps, _partialShare;

    public static void CountFrame(bool partial)
    {
        _frames++;
        if (partial) _partial++;
        double secs = (DateTime.Now - _fpsAt).TotalSeconds;
        if (secs >= 2)
        {
            _fps = _frames / secs;
            _partialShare = _frames == 0 ? 0 : (double)_partial / _frames;
            _frames = _partial = 0;
            _fpsAt = DateTime.Now;
        }
    }

    /// <summary>"CPU 0.4% · 96 MB · 21 fps (80% partial) · resting".</summary>
    public static string Stats()
    {
        using var p = Process.GetCurrentProcess();
        var cpu = p.TotalProcessorTime;
        double pct = 0;
        if (_cpuAt != default)
        {
            double wall = (DateTime.Now - _cpuAt).TotalMilliseconds;
            if (wall > 0) pct = (cpu - _cpu0).TotalMilliseconds / wall * 100 / Environment.ProcessorCount;
        }
        _cpu0 = cpu;
        _cpuAt = DateTime.Now;
        string mem = $"{p.WorkingSet64 / 1048576.0:0} MB";
        string frames = (DateTime.Now - _fpsAt).TotalSeconds > 6 ? "0 fps" : $"{_fps:0} fps" + (_partialShare > 0.05 ? $" ({_partialShare * 100:0}% partial)" : "");
        return $"CPU {pct:0.0}%  ·  {mem}  ·  {frames}" + (Resting ? "  ·  resting" : "");
    }
}
