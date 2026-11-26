using System.Diagnostics;
using SkiaSharp;

namespace Cacad;

/// <summary>The glass surfaces' backdrop, built off the UI thread. A screen capture costs ~20 ms through GDI, which on
/// the UI thread stalled every frame it landed in; here the window thread only hands over a mask of where the
/// island was drawn and later picks up a finished, blurred, saturated image.</summary>
public sealed class GlassBackdrop : IDisposable
{
    private readonly Action _onReady;
    private SKBitmap? _raw;            // the screen behind the window, island pixels kept from earlier captures
    private int _w, _h;
    private int[]? _seenAt;            // per pixel: when (s) it was last captured live rather than hidden by the island
    private ulong _hash;
    private int[]? _samples;   // a coarse grid of what was visible last capture (-1: covered by the island)
    private int _busy;
    private sealed record Pair(SKImage Heavy, SKImage Soft);
    private Pair? _pending;            // finished by the worker, not yet taken by the UI thread
    private bool _disposed;

    /// <summary>The heavily blurred backdrop the UI draws with (owned by the UI thread).</summary>
    public SKImage? Current { get; private set; }
    /// <summary>A lightly blurred backdrop, for Prism Glass's refracting rim where shapes stay readable.</summary>
    public SKImage? Soft { get; private set; }
    /// <summary>Bumps whenever a new backdrop is taken, so cached glass can tell it is stale.</summary>
    public int Version { get; private set; }
    /// <summary>Captures in a row that found the screen behind unchanged (C3E backs off on these).</summary>
    public int Unchanged { get; private set; }

    private int _forget;

    /// <summary>The scene behind the island changed in a way the visible edges may not show (a window behind it came
    /// forward, was minimised or restored): the next capture forgets everything remembered under the island and looks
    /// again, and it always rebuilds the blur.</summary>
    public void SceneChanged() { Volatile.Write(ref _forget, 1); _hash = 0; }

    public GlassBackdrop(Action onReady) => _onReady = onReady;

    private static readonly string? TestPath = Environment.GetEnvironmentVariable("CACAD_GLASS_TEST");
    private static Image? _testImage;
    private static DateTime _testStamp;

    /// <summary>The test picture, reloaded when the file changes (to test a backdrop changing under the island).</summary>
    private static Image? TestImage
    {
        get
        {
            if (TestPath == null || !File.Exists(TestPath)) return null;
            try
            {
                var stamp = File.GetLastWriteTimeUtc(TestPath);
                if (_testImage == null || stamp != _testStamp)
                {
                    using var fs = File.OpenRead(TestPath);
                    var img = Image.FromStream(fs);
                    var copy = new Bitmap(img); // detach from the stream so the file can be replaced
                    img.Dispose();
                    _testImage?.Dispose();
                    _testImage = copy;
                    _testStamp = stamp;
                }
            }
            catch { }
            return _testImage;
        }
    }

    /// <summary>Take a finished image if one is waiting. UI thread, start of a frame.</summary>
    public void Swap()
    {
        var next = Interlocked.Exchange(ref _pending, null);
        if (next == null) return;
        Current?.Dispose();
        Soft?.Dispose();
        Current = next.Heavy;
        Soft = next.Soft;
        Version++;
    }

    public bool Busy => Volatile.Read(ref _busy) == 1;

    /// <summary>Start a capture of <paramref name="screenRect"/> (the window's bounds in screen pixels).
    /// <paramref name="islandAlpha"/> is the window's alpha channel from the last frame: those pixels are the
    /// island itself, so the capture keeps what was behind them instead of filming its own glass.</summary>
    /// <param name="gen">the island's motion counter when <paramref name="islandAlpha"/> was taken; <paramref name="genNow"/>
    /// reads it again. If the island moved before the screen was grabbed, the grab shows the island's new outline where
    /// the mask says "background", and keeping it would leave a ghost of the card in the glass; such a capture is dropped.</param>
    public void Request(Rectangle screenRect, Rectangle monitor, byte[] islandAlpha, int w, int h, int gen, Func<int> genNow, Rectangle? focus = null)
    {
        if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        Task.Run(() =>
        {
            try { Build(screenRect, monitor, islandAlpha, w, h, gen, genNow, focus); }
            catch { /* locked screen, secure desktop: try again next time */ }
            finally { Volatile.Write(ref _busy, 0); }
        });
    }

    private void Build(Rectangle win, Rectangle mon, byte[] alpha, int w, int h, int gen, Func<int> genNow, Rectangle? focus)
    {
        var sw = Stopwatch.StartNew();
        if (_raw == null || _w != w || _h != h)
        {
            _raw?.Dispose();
            _raw = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            _w = w; _h = h; _hash = 0;
            _seenAt = new int[w * h];
            Array.Fill(_seenAt, int.MinValue / 2); // never seen
        }
        int now = (int)(Environment.TickCount64 / 1000);
        int cx = Math.Max(mon.Left, win.Left), cy = Math.Max(mon.Top, win.Top);
        int cx2 = Math.Min(mon.Right, win.Left + w), cy2 = Math.Min(mon.Bottom, win.Top + h);
        // C3E: only the part that matters right now (window pixels); the rest keeps what it last saw.
        if (focus is { } f && _seenAt != null)
        {
            cx = Math.Max(cx, win.Left + f.Left); cy = Math.Max(cy, win.Top + f.Top);
            cx2 = Math.Min(cx2, win.Left + f.Right); cy2 = Math.Min(cy2, win.Top + f.Bottom);
        }
        if (cx2 <= cx || cy2 <= cy) return;

        using var shot = new Bitmap(cx2 - cx, cy2 - cy, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(shot))
        {
            // Test hook: a fixed picture instead of the screen, to judge the glass against a known background.
            if (TestImage is { } test) g.DrawImage(test, new Rectangle(0, 0, shot.Width, shot.Height));
            else g.CopyFromScreen(cx, cy, 0, 0, shot.Size);
        }
        // The island moved while the screen was grabbed: the mask no longer matches it. Drop this one.
        if (genNow() != gen) return;

        int offX = cx - win.Left, offY = cy - win.Top;
        var data = shot.LockBits(new Rectangle(0, 0, shot.Width, shot.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        ulong hash = 14695981039346656037UL;
        unsafe
        {
            byte* src = (byte*)data.Scan0, raw = (byte*)_raw.GetPixels();
            int gw = (w + 7) >> 3, gh = (h + 3) >> 2, compared = 0, changed = 0;
            if (_samples == null || _samples.Length != gw * gh) { _samples = new int[gw * gh]; Array.Fill(_samples, -1); }
            var samples = new int[gw * gh];
            Array.Fill(samples, -1);
            for (int row = 0; row < shot.Height; row++)
            {
                byte* s = src + row * data.Stride;
                int y = row + offY;
                byte* r = raw + (y * w + offX) * 4;
                int ai = y * w + offX;
                for (int col = 0; col < shot.Width; col++)
                {
                    if (alpha[ai + col] > 8) continue; // the island itself: see below
                    int o = col * 4;
                    r[o] = s[o]; r[o + 1] = s[o + 1]; r[o + 2] = s[o + 2]; r[o + 3] = 255;
                    _seenAt![ai + col] = now;
                    // Sample a grid of spots; compare with the same spots last time (only those visible both times).
                    int gx = col + offX;
                    if ((y & 3) == 0 && (gx & 7) == 0)
                    {
                        int gi = (y >> 2) * gw + (gx >> 3), q = s[o] >> 5 | (s[o + 1] >> 5) << 3 | (s[o + 2] >> 5) << 6;
                        if (_samples![gi] >= 0) { compared++; if (_samples[gi] != q) changed++; }
                        samples[gi] = q;
                    }
                }
            }
            // The scene around the island changed (another app came forward, the desktop showed): whatever is
            // remembered under the island belongs to the old scene. Forget it, so it is filled in from the new one
            // below instead of showing the previous app's colours for up to 45 seconds.
            bool forget = Interlocked.Exchange(ref _forget, 0) == 1;
            // Changed: more than a few percent of the spots seen both times look different now.
            bool sceneMoved = compared >= 4 && changed > Math.Max(2, compared * 0.03);
            if (forget || sceneMoved)
                for (int i = 0; i < _seenAt!.Length; i++)
                    if (alpha[i] > 8) _seenAt[i] = int.MinValue / 2;
            _samples = samples;

            // Under the island the screen can't be captured (it would film the glass itself). Two cases:
            //  - seen recently (the card's area, visible until a moment ago while only the pill showed): keep what it
            //    last looked like. That is what lets Liquid Glass stay clear through the middle of the open card.
            //  - never seen, or seen too long ago (under the pill, or after a new wallpaper): an old picture there goes
            //    stale (the pill's middle once showed the previous wallpaper, black). Carry the nearest valid pixel
            //    below up into it, column by column; the blur turns that into a smooth, current guess.
            const int freshSeconds = 45;
            for (int col = 0; col < shot.Width; col++)
            {
                int x = col + offX;
                uint carry = 0;
                bool have = false;
                for (int row = shot.Height - 1; row >= 0; row--)
                {
                    int y = row + offY, i = y * w + x;
                    uint* px = (uint*)(raw + i * 4);
                    bool valid = alpha[i] <= 8 || now - _seenAt![i] <= freshSeconds;
                    if (valid) { carry = *px; have = true; }
                    else if (have) *px = carry;
                }
            }

            // A cheap fingerprint, sampled, to skip rebuilding an unchanged backdrop.
            for (int row = 0; row < shot.Height; row += 3)
            {
                byte* r = raw + ((row + offY) * w + offX) * 4;
                for (int col = 0; col < shot.Width; col += 13) { hash ^= r[col * 4 + 1]; hash *= 1099511628211UL; }
            }
        }
        shot.UnlockBits(data);
        if (hash == _hash) { Unchanged++; FrameStats.Captured(sw.Elapsed.TotalMilliseconds); return; }
        Unchanged = 0;
        _hash = hash;

        // Blur at half size, with the colour boost baked in so the UI draws it with no per-pixel filter.
        int bw = Math.Max(1, w / 2), bh = Math.Max(1, h / 2);
        using var backdrop = SKImage.FromBitmap(_raw);
        SKImage Blurred(float sigma, float saturation)
        {
            using var surface = SKSurface.Create(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp), ColorFilter = G.Saturation(saturation) };
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(backdrop, SKRect.Create(0, 0, w, h), SKRect.Create(0, 0, bw, bh), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
            return surface.Snapshot().ToRasterImage(true);
        }
        var pair = new Pair(Blurred(15, 1.35f), Blurred(1.2f, 1.45f));
        var old = Interlocked.Exchange(ref _pending, pair);
        old?.Heavy.Dispose();
        old?.Soft.Dispose();
        FrameStats.Captured(sw.Elapsed.TotalMilliseconds);
        _onReady();
    }

    public void Dispose()
    {
        _disposed = true;
        Current?.Dispose();
        Soft?.Dispose();
        Current = Soft = null;
        var old = Interlocked.Exchange(ref _pending, null);
        old?.Heavy.Dispose();
        old?.Soft.Dispose();
    }
}
