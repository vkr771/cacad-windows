using SkiaSharp;

namespace Cacad;

/// <summary>C3E, the surface engine. It makes the island's surfaces (glass, materials, your custom image) cheaper to
/// draw without changing a single pixel of how they look. Everything it does is caching and scheduling:
///
///  1. Glass pacing. Frosted, Liquid and Prism glass film the screen behind the island and blur it. Instead of doing
///     that four times a second forever, C3E watches the island's shape and the screen: while both stay the same it
///     backs off (to every 1.5 s), and the moment either changes it is back to four times a second.
///  2. Glass buffers. The mask handed to the glass worker is reused instead of allocated every capture. Glass also
///     forgets what it remembered under the island the moment the scene behind changes (an app comes forward, a
///     window is minimised), so it never shows a window that has gone.
///  3. Materials ahead of time. Aurora, Nebula and Neon are shaders that are rendered at their own frame rate; C3E
///     renders the next frame on a background thread, so the island's own frame only draws a finished picture.
///
/// With C3E off, everything is drawn the original way. Toggle: Settings › Performance.</summary>
public static class C3E
{
    public static bool On = true;

    // ---------- 1. glass pacing ----------

    private static int _stillShape = -1, _stillCount;

    /// <summary>How long until the next look at the screen behind the glass (ms).</summary>
    /// <summary>Something changed behind the island (another app came forward): look again soon, whatever the pacing.</summary>
    public static void Kick() { _stillCount = 0; _stillShape = -1; }

    public static int GlassInterval(int shapeGen, int unchangedCaptures, bool fallbackStill)
    {
        if (!On) return fallbackStill ? 1000 : 250;
        if (shapeGen != _stillShape) { _stillShape = shapeGen; _stillCount = 0; return 250; }
        _stillCount++;
        // Settled shape: back off step by step while the screen behind keeps coming back unchanged.
        if (_stillCount < 4) return 250;
        return unchangedCaptures switch { < 2 => 400, < 6 => 800, _ => 1500 };
    }

    // ---------- 2. glass buffers ----------

    private static byte[]? _mask;

    public static byte[] MaskBuffer(int n)
    {
        if (!On || _mask == null || _mask.Length != n) return _mask = new byte[n];
        return _mask;
    }

    // ---------- 3. materials ahead of time ----------

    private static SKImage? _ahead;
    private static object? _aheadShape;
    private static int _aheadFrame = int.MinValue, _working;

    /// <summary>The newest material frame the worker finished for this size and place (null if none newer than
    /// <paramref name="shownFrame"/>), and asks the worker for <paramref name="wantFrame"/> if it is not on it yet.
    /// The caller keeps showing its last frame until one lands, a step behind at most.</summary>
    public static (SKImage? img, int frame) Ahead(object shape, int shownFrame, int wantFrame, Func<SKImage?> render)
    {
        SKImage? got = null;
        int gotFrame = 0;
        lock (typeof(C3E))
        {
            if (_ahead != null && Equals(_aheadShape, shape) && _aheadFrame != shownFrame)
            {
                got = _ahead;
                gotFrame = _aheadFrame;
                _ahead = null;
            }
        }
        if (gotFrame != wantFrame && Interlocked.CompareExchange(ref _working, 1, 0) == 0)
            Task.Run(() =>
            {
                try
                {
                    var img = render();
                    lock (typeof(C3E))
                    {
                        _ahead?.Dispose();
                        _ahead = img;
                        _aheadShape = shape;
                        _aheadFrame = wantFrame;
                    }
                }
                catch { }
                finally { Volatile.Write(ref _working, 0); }
            });
        return (got, gotFrame);
    }

}
