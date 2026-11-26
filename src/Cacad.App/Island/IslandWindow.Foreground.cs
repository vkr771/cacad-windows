using System.Runtime.InteropServices;

namespace Cacad;

/// <summary>Glass and the app in front: when another window comes forward (or the desktop shows), the screen behind
/// the island has just changed, so glass looks again right away (twice, as windows animate in) instead of waiting for
/// its next scheduled look.</summary>
public sealed partial class IslandWindow
{
    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

    private WinEventProc? _fgProc;          // kept alive: Windows calls it
    private IntPtr _fgHook, _minHook;

    [StructLayout(LayoutKind.Sequential)] private struct WinRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WinRect rect);
    private readonly System.Windows.Forms.Timer _fgLook = new() { Interval = 140 };
    private int _fgLooks;

    private void WatchForeground()
    {
        _fgProc = (_, evt, hwnd, _, _, _, _) =>
        {
            if (hwnd == Handle || !_theme.IsGlass || Hidden) return;
            // A window coming forward over the island's area, or any window minimised or restored (the one leaving
            // may have been behind the island): what glass remembers under the island is out of date.
            bool overlaps = evt != 0x0003 || (GetWindowRect(hwnd, out var wr) && wr.Left < Left + _bmpW && wr.Right > Left && wr.Top < Top + _bmpH && wr.Bottom > Top);
            if (overlaps) _glass?.SceneChanged();
            C3E.Kick();
            _fgLooks = 0;
            _fgLook.Stop();
            _fgLook.Interval = 140;
            _fgLook.Start();
        };
        _fgLook.Tick += (_, _) =>
        {
            // Look at ~140 ms (the new window is up) and again at ~500 ms (its opening animation has finished).
            _fgLooks++;
            RequestBackdrop();
            if (_fgLooks == 1) _fgLook.Interval = 360;
            else _fgLook.Stop();
        };
        // EVENT_SYSTEM_FOREGROUND, out of context: delivered on this (the window's) thread.
        _fgHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, _fgProc, 0, 0, 0);
        // EVENT_SYSTEM_MINIMIZESTART and MINIMIZEEND.
        _minHook = SetWinEventHook(0x0016, 0x0017, IntPtr.Zero, _fgProc, 0, 0, 0);
    }

    private void UnwatchForeground()
    {
        if (_fgHook != IntPtr.Zero) UnhookWinEvent(_fgHook);
        if (_minHook != IntPtr.Zero) UnhookWinEvent(_minHook);
        _fgHook = IntPtr.Zero;
        _fgLook.Dispose();
    }
}
