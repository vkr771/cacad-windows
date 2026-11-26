using System.Runtime.InteropServices;

namespace Cacad;

/// <summary>Frame pacing from the display itself. A background thread waits on DwmFlush (one return per
/// compositor frame, i.e. vsync) and posts one frame message to the window; it never posts another while
/// that frame is still being drawn, so a slow frame drops the next tick instead of queueing a backlog.
/// The WinForms timer this replaces fires on a 15.6 ms grid at the lowest message priority, which is what
/// made motion uneven.</summary>
public sealed class VsyncPump : IDisposable
{
    public const int WM_FRAME = 0x0400 + 77;

    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled(out bool enabled);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    private readonly IntPtr _hwnd;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _running = true, _wanted;
    private int _inFlight;

    public VsyncPump(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _thread = new Thread(Loop) { IsBackground = true, Name = "cacad vsync", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Frames are wanted: start (or keep) ticking.</summary>
    public void Run()
    {
        if (_wanted) return;
        _wanted = true;
        _wake.Set();
    }

    /// <summary>Nothing moves: stop ticking until <see cref="Run"/>.</summary>
    public void Pause() => _wanted = false;

    public bool Running => _wanted;

    /// <summary>Called by the window when it has finished the frame it was sent.</summary>
    public void Done() => Interlocked.Exchange(ref _inFlight, 0);

    private void Loop()
    {
        bool dwm = DwmIsCompositionEnabled(out bool on) == 0 && on;
        if (!dwm) timeBeginPeriod(1);
        try
        {
            while (_running)
            {
                if (!_wanted) { _wake.WaitOne(); continue; }
                // Wait for the next composition; without DWM, a 1 ms-resolution sleep to ~60 Hz.
                if (!dwm || DwmFlush() != 0) Thread.Sleep(16);
                if (_wanted && Interlocked.CompareExchange(ref _inFlight, 1, 0) == 0)
                    PostMessage(_hwnd, WM_FRAME, IntPtr.Zero, IntPtr.Zero);
            }
        }
        finally { if (!dwm) timeEndPeriod(1); }
    }

    public void Dispose()
    {
        _running = false;
        _wanted = false;
        _wake.Set();
    }
}
