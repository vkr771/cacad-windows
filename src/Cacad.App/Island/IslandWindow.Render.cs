using System.Runtime.InteropServices;
using SkiaSharp;

namespace Cacad;

/// <summary>The render engine. Skia draws straight into the layered window's DIB (no copy); each frame clears and
/// redraws only the island's bounds (this frame's and last frame's), and tells Windows that rectangle is all that
/// changed. The glass surfaces' backdrop is captured and blurred on a worker thread.</summary>
public sealed partial class IslandWindow
{
    private SKSurface? _surface;
    private IntPtr _memDC, _dib, _dibBits, _oldObj;
    private int _bmpW, _bmpH;
    private readonly Frame _frame = new();

    private RectangleF _lastDirty;     // device pixels drawn last frame
    private bool _fullRedraw = true;
    private byte _lastConstAlpha = 255;
    private float _reveal = 1;   // hide until hover: 0 hidden, 1 shown

    private GlassBackdrop? _glass;
    private readonly System.Windows.Forms.Timer _glassTimer = new() { Interval = 250 };
    private bool _glassWired;
    private int _glassStill, _glassLooks;

    // ---------- surface ----------

    private void EnsureBitmap(int w, int h)
    {
        if (_surface != null && _bmpW == w && _bmpH == h) return;
        FreeBitmap();
        _bmpW = w;
        _bmpH = h;
        var bmi = new Native.BITMAPINFO
        {
            BmiHeader = new Native.BITMAPINFOHEADER
            {
                BiSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                BiWidth = w,
                BiHeight = -h,
                BiPlanes = 1,
                BiBitCount = 32,
            }
        };
        _memDC = Native.CreateCompatibleDC(IntPtr.Zero);
        _dib = Native.CreateDIBSection(_memDC, ref bmi, Native.DIB_RGB_COLORS, out _dibBits, IntPtr.Zero, 0);
        _oldObj = Native.SelectObject(_memDC, _dib);
        // Skia renders into the DIB's own memory: what it draws is what UpdateLayeredWindow presents.
        _surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul), _dibBits, w * 4);
        _fullRedraw = true;
    }

    private void FreeBitmap()
    {
        _surface?.Dispose();
        _surface = null;
        if (_memDC != IntPtr.Zero)
        {
            if (_oldObj != IntPtr.Zero) Native.SelectObject(_memDC, _oldObj);
            Native.DeleteDC(_memDC);
            _memDC = _oldObj = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero) { Native.DeleteObject(_dib); _dib = _dibBits = IntPtr.Zero; }
    }

    // ---------- glass surfaces (frosted, liquid, prism) ----------

    private void UpdateGlassTimer()
    {
        if (!_glassWired)
        {
            _glassWired = true;
            _glass = new GlassBackdrop(() => BeginInvoke(new Action(Invalidate)));
            _glassTimer.Tick += (_, _) =>
            {
                if (!_theme.IsGlass || Hidden) { _glassTimer.Stop(); return; }
                RequestBackdrop();
                // While nothing on the island moves, the screen behind is checked less often.
                _glassStill = _vsync?.Running == true ? 0 : _glassStill + 1;
                // C3E paces this by the island's shape and by whether the screen behind keeps coming back the same.
                _glassTimer.Interval = C3E.GlassInterval(Volatile.Read(ref _shapeGen), _glass?.Unchanged ?? 0, _glassStill > 8);
            };
        }
        if (_theme.IsGlass && !Hidden) { _glassTimer.Start(); RequestBackdrop(); } else _glassTimer.Stop();
    }

    private void RequestBackdrop()
    {
        if (_glass == null || _glass.Busy || _dibBits == IntPtr.Zero) return;
        // Only while the island is still: a mask taken mid-animation would not match the screen a moment later.
        if (!_w.IsSettled || !_h.IsSettled || !_open.IsSettled || !_notch.IsSettled || !_fade.IsSettled) return;
        // Hand the worker this frame's alpha: where the island is, it must not film its own glass.
        int n = _bmpW * _bmpH;
        var alpha = C3E.MaskBuffer(n);
        unsafe
        {
            byte* px = (byte*)_dibBits;
            for (int i = 0; i < n; i++) alpha[i] = px[i * 4 + 3];
        }
        // The whole window every time: the card's area must stay as fresh as the pill's.
        Rectangle? focus = null;
        _glass.Request(new Rectangle(Left, Top, _bmpW, _bmpH), MonitorBounds, alpha, _bmpW, _bmpH, _shapeGen, () => Volatile.Read(ref _shapeGen), focus);
    }

    /// <summary>Prism Glass: the refracting lens shader over both backdrops. False until they exist.</summary>
    private bool DrawPrism(SKCanvas c, float w, float h, float r, float open)
    {
        var heavy = _glass?.Current;
        var soft = _glass?.Soft;
        if (heavy == null || soft == null) return false;
        var island = SKRect.Create((BoxW / 2 - w / 2) * _scale, IslandTop * _scale, w * _scale, h * _scale);
        bool notch = _notch.Value > 50;
        double t0 = FrameStats.Now;
        bool moving = !_w.IsSettled || !_h.IsSettled || !_open.IsSettled || !_notch.IsSettled;
        bool ok = PrismGlass.Draw(c, PrismGlass.For(_theme), heavy, soft, _glass!.Version, island, r * _scale, notch, open, _scale, _bmpW, _bmpH, moving);
        if (FrameStats.On) FrameStats.Note("prism shader", FrameStats.Now - t0);
        return ok;
    }

    private void DrawGlass(SKCanvas c, float h, float open)
    {
        var img = _glass?.Current;
        if (img != null)
        {
            // In device pixels: the backdrop is aligned with the window. The clip (the island's shape) still applies.
            c.Save();
            c.ResetMatrix();
            float iw = img.Width, ih = img.Height;
            // A slight magnification of what is behind: the lens "bend".
            c.DrawImage(img, SKRect.Create(iw * 0.06f, ih * 0.03f, iw * 0.88f, ih * 0.86f), SKRect.Create(0, 0, _bmpW, _bmpH),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            c.Restore();
        }
        using (var tint = new SKPaint { Color = new SKColor(14, 16, 22, (byte)(img == null ? 200 : 70 + 75 * open)) })
            c.DrawRect(SKRect.Create(-BoxW, -10, BoxW * 2, h + 20), tint);
        using var sheen = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h),
            new[] { G.White(0.18f), G.White(0.05f), G.White(0), G.White(0.07f) }, new[] { 0f, 0.35f, 0.72f, 1f }, SKShaderTileMode.Clamp);
        using var sp = new SKPaint { Shader = sheen };
        c.DrawRect(SKRect.Create(-BoxW, 0, BoxW * 2, h), sp);
    }

    // ---------- the frame ----------

    private readonly ClockFaces _pillClock = new();
    private float _frameDt;

    /// <summary>The island's bounds this frame, in device pixels (everything is drawn clipped to its shape).</summary>
    private RectangleF IslandBounds()
    {
        float halfW = (float)_w.Value / 2 + Flare + 3;
        float left = (BoxW / 2 - halfW) * _scale, right = (BoxW / 2 + halfW) * _scale;
        float bottom = (IslandTop + (float)_h.Value + 3) * _scale;
        return RectangleF.FromLTRB(Math.Max(0, MathF.Floor(left)), 0, Math.Min(_bmpW, MathF.Ceiling(right)), Math.Min(_bmpH, MathF.Ceiling(bottom)));
    }

    private void DrawAndPresent(float dt)
    {
        _frameDt = dt;
        if (_surface == null || _memDC == IntPtr.Zero) return;
        double t0 = FrameStats.Now;
        _glass?.Swap();

        var now = Hidden ? RectangleF.Empty : IslandBounds();
        RectangleF dirty;
        if (!_partial.IsEmpty && !_fullRedraw && !now.IsEmpty && now == _lastDirty)
        {
            // Z3R: only the companion changed; redraw its patch (the island is where it was last frame).
            dirty = RectangleF.Intersect(_partial, now);
        }
        else
        {
            dirty = _fullRedraw ? new RectangleF(0, 0, _bmpW, _bmpH) : RectangleF.Union(now.IsEmpty ? _lastDirty : now, _lastDirty.IsEmpty ? now : _lastDirty);
            _fullRedraw = false;
            _lastDirty = now;
        }

        var c = _surface.Canvas;
        c.Save();
        c.ClipRect(SKRect.Create(dirty.X, dirty.Y, dirty.Width, dirty.Height));
        c.Clear(SKColors.Transparent);
        if (!Hidden)
        {
            c.Scale(_scale);
            c.Translate(BoxW / 2, IslandTop);
            DrawIsland(c, dt);
        }
        c.Restore();
        c.Flush();
        double t1 = FrameStats.Now;

        byte constAlpha = Hidden ? (byte)0 : (byte)Math.Round(255 * Math.Clamp(_reveal, 0, 1));
        // A change of the window-wide alpha must reach every pixel, not just the dirty ones.
        bool whole = constAlpha != _lastConstAlpha;
        _lastConstAlpha = constAlpha;
        Present(whole ? new RectangleF(0, 0, _bmpW, _bmpH) : dirty, constAlpha);

        if (FrameStats.On)
        {
            FrameStats.Label = $"{_mode}/{_shown.Id}/{_theme.Id}";
            FrameStats.Frame(t1 - t0, FrameStats.Now - t1, (int)(dirty.Width * dirty.Height));
        }
    }

    private unsafe void Present(RectangleF dirty, byte constAlpha)
    {
        var dst = new Native.POINT { X = Left, Y = Top };
        var size = new Native.SIZE { Cx = _bmpW, Cy = _bmpH };
        var src = new Native.POINT();
        var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = constAlpha, AlphaFormat = Native.AC_SRC_ALPHA };
        var rc = new Native.RECT
        {
            Left = (int)MathF.Floor(dirty.Left), Top = (int)MathF.Floor(dirty.Top),
            Right = (int)MathF.Ceiling(dirty.Right), Bottom = (int)MathF.Ceiling(dirty.Bottom),
        };
        var info = new Native.UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)sizeof(Native.UPDATELAYEREDWINDOWINFO),
            pptDst = &dst,
            psize = &size,
            hdcSrc = _memDC,
            pptSrc = &src,
            pblend = &blend,
            dwFlags = Native.ULW_ALPHA,
            prcDirty = rc.Right > rc.Left && rc.Bottom > rc.Top ? &rc : null,
        };
        if (!Native.UpdateLayeredWindowIndirect(Handle, ref info))
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, _memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
    }

    private void DrawIsland(SKCanvas c, float dt)
    {
        float w = (float)_w.Value, h = (float)_h.Value;
        float k = G.Clamp01((float)_open.Value);
        float r = Math.Min(h / 2, 18 + 18 * k);
        float compactA = Math.Max(0, 1 - k / 0.3f) * (1 - _toastK);
        float openA = G.Clamp01((k - 0.3f) / 0.5f);
        float contentA = openA * G.Clamp01((float)_fade.Value);

        using var shape = ShapePath(w, h, r, G.Clamp01((float)_notch.Value / 100f));
        if (_theme.IsGlass)
        {
            c.Save();
            c.ClipPath(shape, SKClipOperation.Intersect, true);
            if (!(_theme.Refract && DrawPrism(c, w, h, r, k))) DrawGlass(c, h, k);
            c.Restore();
        }
        else
        {
            using var fill = new SKPaint { IsAntialias = true, Color = _theme.Fill };
            c.DrawPath(shape, fill);
        }

        c.Save();
        c.ClipPath(shape, SKClipOperation.Intersect, true);
        if (_theme.Surface != null)
        {
            // A drawn material (aurora, nebula, neon, titanium) under everything on the island.
            var island = SKRect.Create((BoxW / 2 - w / 2) * _scale, IslandTop * _scale, w * _scale, h * _scale);
            bool moving = !_w.IsSettled || !_h.IsSettled || !_open.IsSettled;
            double s0 = FrameStats.Now;
            Surfaces.DrawBase(c, _theme, island, w, h, _scale, _t, SurfacePointer(w, h), k, moving);
            if (FrameStats.On) FrameStats.Note($"surface {_theme.Surface}", FrameStats.Now - s0);
        }
        // The pill's content shares it with the cat: everything else moves right by the cat's slot.
        float slot = CatSlot, cw = w - slot;
        c.Save();
        c.Translate(slot / 2, 0);
        // A Live Activity takes the pill's place; the usual content fades out beneath it.
        if (compactA > 0.001f && _activityK < 0.999f) DrawCompact(c, compactA * (1 - _activityK), cw, h);
        if (compactA > 0.001f) DrawActivity(c, compactA * _activityK, cw, h);
        if (_toastK > 0.001f) DrawToast(c, _toastK, cw, h);
        c.Restore();
        float pillA = Math.Max(compactA, _toastK);
        if (openA <= 0.001f) Companion?.Hover(false);
        _catPatch = RectangleF.Empty;
        if (slot > 0 && pillA > 0.001f && Companion != null)
        {
            // With music, a toast or an activity, the cat sits at the pill's left end. With the clock, it sits right
            // beside the time: the clock is centred in the space right of the cat's slot, so placing the cat a small
            // gap before the clock's left edge centres the pair in the pill.
            float catW = slot - 6;
            float leftX = -w / 2 + 6;
            float clockW = Settings.ShowClock ? ClockFaces.Width(Settings.PillClockStyle, h, Settings.Clock24h) : 0;
            float besideX = slot / 2 - clockW / 2 - 7 - catW;
            float clockness = (1 - _musicK) * (1 - _activityK) * (1 - _toastK);
            float x = G.Lerp(leftX, Math.Max(leftX, besideX), clockness);
            // Z3R: where the companion is (device pixels), for frames that redraw only it. Generous: whiskers,
            // props, particles and a bubble all fit, top to bottom of the pill.
            float patchW = CatArt.Measure(Companion.Pose, CatPx) + 14;
            _catPatch = RectangleF.FromLTRB(MathF.Floor((BoxW / 2 + x - 6) * _scale), MathF.Floor(IslandTop * _scale),
                MathF.Ceiling((BoxW / 2 + x - 6 + patchW) * _scale), MathF.Ceiling((IslandTop + h) * _scale));
            CatArt.Draw(c, Companion.Pose, x + CatArt.Whisker * CatPx, (h - 11 * CatPx) / 2 + 2, CatPx, pillA,
                Companion.Time, Companion.Beat, Companion.Pop, Companion.Blink, Companion.EarTwitch, Settings.CompanionSpecies, Settings.CompanionSkin, Settings.CompanionStyle);
        }
        if (openA > 0.001f)
        {
            c.Save();
            c.Translate(-CardW / 2, 0);
            var f = _frame;
            f.C = c;
            f.A = contentA;
            f.W = CardW;
            f.H = h;
            f.Pointer = _pointer is { } p ? ToCard(p) : null;
            f.Dt = dt;
            f.T = _t;
            f.Theme = _theme;
            _shown.DrawBackground(f);
            Ambient.Draw(c, _theme, openA, CardW, h, _t);
            DrawTabs(c, openA, f.Pointer);
            if (Settings.Companion && Companion != null)
            {
                // In the card the cat sits in the top-left corner, where you can pet it (hover) and poke it (click).
                bool over = f.Pointer is { } pp && CatCardRect.Contains(pp);
                Companion.Hover(over);
                CatArt.Draw(c, Companion.Pose, CatCardRect.Left + 4, CatCardRect.Top + 6, CatPx, openA, Companion.Time, Companion.Beat, Companion.Pop, Companion.Blink, Companion.EarTwitch, Settings.CompanionSpecies, Settings.CompanionSkin, Settings.CompanionStyle);
            }
            if (contentA > 0.001f) _shown.Draw(f);
            c.Restore();
        }
        c.Restore();

        // The hairline, half a DIP inside the outline.
        c.Save();
        c.ClipPath(shape, SKClipOperation.Intersect, true);
        using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = _theme.Edge })
        {
            if (_theme.Refract && _glass?.Soft != null) PrismGlass.DrawRim(c, PrismGlass.For(_theme), shape, w, h, _notch.Value > 50);
            else if (Surfaces.DrawRim(c, _theme, shape, w, h, _t)) { /* the surface lit its own rim */ }
            else if (_theme.IsGlass)
            {
                using var sh = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h),
                    new[] { G.White(0.55f), G.White(0.16f), G.White(0.32f) }, new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
                edge.Shader = sh;
                c.DrawPath(shape, edge);
            }
            else c.DrawPath(shape, edge);
        }
        c.Restore();
    }

    /// <summary>The cat's corner in the open card (card DIPs), clear of the centred tab strip.</summary>
    private static readonly SKRect CatCardRect = SKRect.Create(8, 0, 50, 26);

    /// <summary>Where the pointer is across the island (0..1), easing toward it; a slow drift when it is away,
    /// so Titanium's sheen keeps moving a little on its own.</summary>
    private SKPoint SurfacePointer(float w, float h)
    {
        float t = (float)_t;
        var target = _pointer is { } p
            ? new SKPoint(G.Clamp01((p.X + w / 2) / Math.Max(1, w)), G.Clamp01(p.Y / Math.Max(1, h)))
            : new SKPoint(0.5f + 0.38f * MathF.Sin(t * 0.21f), 0.5f + 0.3f * MathF.Cos(t * 0.17f));
        _surfacePointer = new SKPoint(G.Ease(_surfacePointer.X, target.X, 6, _frameDt), G.Ease(_surfacePointer.Y, target.Y, 6, _frameDt));
        return _surfacePointer;
    }

    private SKPoint _surfacePointer = new(0.5f, 0.5f);

    /// <summary>The outline. As a notch (n = 1): square top corners curving out into the top edge, rounded bottom.
    /// As an island (n = 0): a rounded rectangle. Between the two the flares shrink away, then the top corners round off.</summary>
    private SKPath ShapePath(float w, float h, float r, float n)
    {
        float L = -w / 2, R = w / 2;
        var p = new SKPath();
        if (n > 0.5f)
        {
            float f = Math.Min(Flare * (2 * n - 1), h / 2);
            p.MoveTo(L - f, 0);
            if (f > 0.01f) p.ArcTo(new SKRect(L - 2 * f, 0, L, 2 * f), -90, 90, false);
            p.LineTo(L, h - r);
            p.ArcTo(L, h, L + r, h, r);
            p.LineTo(R - r, h);
            p.ArcTo(R, h, R, h - r, r);
            p.LineTo(R, f);
            if (f > 0.01f) p.ArcTo(new SKRect(R, 0, R + 2 * f, 2 * f), 180, 90, false);
            p.Close();
            return p;
        }
        float t = Math.Min(r * (1 - 2 * n), h / 2);
        p.MoveTo(L, t);
        p.LineTo(L, h - r);
        p.ArcTo(L, h, L + r, h, r);
        p.LineTo(R - r, h);
        p.ArcTo(R, h, R, h - r, r);
        p.LineTo(R, t);
        if (t > 0.01f)
        {
            p.ArcTo(R, 0, R - t, 0, t);
            p.LineTo(L + t, 0);
            p.ArcTo(L, 0, L, t, t);
        }
        else { p.LineTo(R, 0); p.LineTo(L, 0); }
        p.Close();
        return p;
    }

    // ---------- the resting pill ----------

    /// <summary>Playing music shows its cover and a visualizer; otherwise the clock. The two crossfade
    /// (<see cref="_musicK"/>), so a pause or a new track does not snap.</summary>
    private void DrawCompact(SKCanvas c, float a, float w, float h)
    {
        float L = -w / 2, cy = h / 2, right = w / 2 - 10;
        float mk = _musicK, ma = a * mk, ca = a * (1 - mk);
        if (ma > 0.003f)
        {
            var cover = SKRect.Create(L + 6, cy - 12, 24, 24);
            if (Media.Art != null) G.ImageCover(c, Media.Art.Image, cover, ma, 6);
            else
            {
                G.Fill(c, cover, 6, G.White(0.12f * ma));
                Icons.Draw(c, "music", cover.MidX, cover.MidY, 13, G.Paper(0.7f * ma));
            }
            var tint = (Media.Art?.Strong ?? _theme.Accent).WithAlpha(G.A(ma));
            Visualizers.Draw(c, Settings.Visualizer, right, cy, tint, (float)_t);
            if (LyricsShown) DrawLyric(c, ma, w, h);
        }
        if (ca > 0.003f && Settings.ShowClock)
            _pillClock.DrawPill(c, Settings.PillClockStyle, w, h, ca, _theme, Settings.Clock24h, _t, _frameDt);
        if (_micMuted)
        {
            float mx = mk > 0.5f ? right - 34 : w / 2 - 20;
            Icons.Draw(c, "mic-off", mx, cy, 14, new SKColor(255, 95, 87, G.A(a)));
        }
    }

    private void DrawToast(SKCanvas c, float a, float w, float h)
    {
        float L = -w / 2, cy = h / 2;
        var color = _toastColor ?? G.PaperBase;
        if (_toastIcon == "swatch")
        {
            // A copied colour shows itself.
            G.Circle(c, L + 20, cy, 10, color.WithAlpha(G.A(a)));
            using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = G.White(0.5f * a) };
            c.DrawCircle(L + 20, cy, 10, ring);
        }
        else
        {
            G.Circle(c, L + 20, cy, 11, color.WithAlpha(G.A(0.2f * a)));
            Icons.Draw(c, _toastIcon, L + 20, cy, 13, color.WithAlpha(G.A(a)));
        }
        using var font = Fonts.Med(13);
        float room = w - 52;
        if (_toastAction != null)
        {
            // A tappable toast carries a small chevron: tap to act.
            Icons.Draw(c, "chevron-right", w / 2 - 16, cy, 13, G.Paper(0.6f * a));
            room -= 18;
        }
        G.Text(c, G.Ellipsize(_toastText, font, room), L + 38, cy + 4.6f, font, G.Paper(a));
    }

    // ---------- the tab strip ----------

    /// <summary>The tabs shown: every panel, except Lyrics, which appears only while a song with lyrics plays.</summary>
    private List<IslandPanel> Tabs => _panels.Where(p => p.Visible).ToList();

    private float TabStart => CardW / 2 - (Tabs.Count * 22 + 10) / 2f;
    private float GearX => TabStart + Tabs.Count * 22 + 10;

    private string? HitTab(SKPoint p)
    {
        if (p.Y > 27) return null;
        float x = p.X + CardW / 2;
        for (int i = 0; i < Tabs.Count; i++)
            if (Math.Abs(x - (TabStart + i * 22)) <= 11) return Tabs[i].Id;
        if (Math.Abs(x - GearX) <= 11) return "gear";
        return null;
    }

    private void DrawTabs(SKCanvas c, float a, SKPoint? pointer)
    {
        const float cy = 13;
        string? over = pointer is { } pp ? HitTab(new SKPoint(pp.X - CardW / 2, pp.Y)) : null;
        for (int i = 0; i < Tabs.Count; i++)
        {
            float cx = TabStart + i * 22;
            bool on = Tabs[i] == _panel;
            float alpha = on ? 1 : over == Tabs[i].Id ? 0.75f : 0.4f;
            Icons.Draw(c, Tabs[i].Icon, cx, cy, 15, G.Paper(alpha * a));
            if (on)
            {
                using var dot = new SKPaint { Color = _theme.Accent.WithAlpha(G.A(a)) };
                c.DrawRect(cx - 1.32f, cy + 9.84f, 2.64f, 2.64f, dot);
            }
        }
        using (var div = new SKPaint { Color = G.Paper(0.18f * a) })
            c.DrawRect(GearX - 16.5f, cy - 5.4f, 1, 10.8f, div);
        Icons.Draw(c, "settings", GearX, cy, 13.8f, G.Paper((over == "gear" ? 0.75f : 0.4f) * a));
    }
}
