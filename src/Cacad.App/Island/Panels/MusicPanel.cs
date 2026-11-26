using SkiaSharp;

namespace Cacad;

public sealed class MusicPanel : IslandPanel
{
    public MusicPanel(IIslandHost host) : base(host) { }

    public override string Id => "music";
    public override string Icon => "music";
    public override string Title => "Music";
    public override float Height => 150;
    public override double FrameInterval => 0;
    public override bool Animating => Host.Media.IsPlaying || _swapAt > _t - 0.6 || _barState.Busy || Settling;

    private MediaService M => Host.Media;
    private float _playK = 1, _morph, _bgK = 1;
    private double _t, _swapAt = -9, _marquee;
    private int _seenTrack = -1;
    private readonly Dictionary<string, double> _kicks = new();
    private string? _pressed;
    private double? _seekPreview;
    private float _vol = -1;
    private bool _muted;
    private DateTime _volAt;

    private SKRect _prev, _play, _next, _bar, _mute, _volRect, _corner;

    private bool Settling => Math.Abs(_playK - (M.IsPlaying ? 1 : 0)) > 0.004f || Math.Abs(_morph - (M.IsPlaying ? 0 : 1)) > 0.004f
                             || Math.Abs(_bgK - (Host.Settings.AlbumBackground ? 1 : 0)) > 0.004f;

    private string BarStyle => string.IsNullOrEmpty(Host.Settings.BarStyle) ? ProgressBars.ThemeDefault(Host.Settings.ThemeId) : Host.Settings.BarStyle;

    private void Advance(Frame f)
    {
        if (_t == f.T) return;
        float dt = f.Dt;
        _t = f.T;
        bool playing = M.IsPlaying;
        _playK = G.Ease(_playK, playing ? 1 : 0, 8, dt);
        _morph = G.Ease(_morph, playing ? 0 : 1, 14, dt);
        _bgK = G.Ease(_bgK, Host.Settings.AlbumBackground ? 1 : 0, 5, dt);
        if (playing) _marquee += dt;
        if (M.TrackVersion != _seenTrack)
        {
            if (_seenTrack >= 0) _swapAt = _t;
            _seenTrack = M.TrackVersion;
            _marquee = 0;
            _barState.Reset();
        }
        if ((DateTime.UtcNow - _volAt).TotalMilliseconds > 250 && _pressed != "vol")
        {
            _vol = Host.Audio.Volume;
            _muted = Host.Audio.Muted;
            _volAt = DateTime.UtcNow;
        }
    }

    private SKColor Strong(Frame f) => M.Art?.Strong ?? f.Theme.Accent;

    public override void DrawBackground(Frame f)
    {
        Advance(f);
        var art = M.Art;
        if (art == null || !M.HasSession) return;
        var c = f.C;
        float amb = f.A * _bgK, h = f.H;
        if (amb > 0.003f)
        {
            // The cover squeezed to six columns and stretched across the card paints its streaks; it drifts slowly.
            float col = (f.W + 40) / 5.4f, drift = MathF.Sin((float)f.T / 2.6f) * 0.8f, left = -20 - (0.2f + drift * 0.3f) * col;
            using (var p = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(G.A(amb)) })
                c.DrawImage(art.Strip, SKRect.Create(left, -20, 6 * col, h + 40), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), p);

            using (var across = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(f.W, 0),
                       new[] { G.Black(0.69f * amb), G.Black(0.38f * amb), G.Black(0.52f * amb) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp))
            using (var p = new SKPaint { Shader = across })
                c.DrawRect(SKRect.Create(-12, -6, f.W + 24, h + 6), p);
            using (var up = SKShader.CreateLinearGradient(new SKPoint(0, h), new SKPoint(0, h * 0.55f),
                       new[] { G.Black(0.63f * amb), G.Black(0) }, null, SKShaderTileMode.Clamp))
            using (var p = new SKPaint { Shader = up })
                c.DrawRect(SKRect.Create(-12, -6, f.W + 24, h + 6), p);
        }
        float glow = f.A * 0.55f * _playK * _bgK;
        if (glow > 0.003f)
        {
            // Pre-blurred once per track (AlbumArt.Glow); drawn here as a plain image.
            using var p = new SKPaint { Color = SKColors.White.WithAlpha(G.A(glow)) };
            float pad = AlbumArt.GlowPad;
            c.DrawImage(art.Glow, SKRect.Create(22 - 30 - pad, 32 - 30 - pad, 134 + pad * 2, 134 + pad * 2), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), p);
        }
    }

    public override void Draw(Frame f)
    {
        Advance(f);
        if (!M.HasSession) { DrawEmpty(f); return; }
        var c = f.C;
        float a = f.A, W = f.W;
        var strong = Strong(f);

        // The cover: smaller and dimmed while paused, turned like a card on a song change.
        float flip = FlipAngle(), scale = 1 - 0.14f * (1 - _playK);
        float cxA = 59, cyA = 69;
        c.Save();
        c.Translate(cxA, cyA);
        c.Scale(scale * Math.Max(0.001f, MathF.Cos(flip * MathF.PI / 180f)), scale);
        c.Translate(-cxA, -cyA);
        var cover = SKRect.Create(22, 32, 74, 74);
        if (M.Art != null) G.ImageCover(c, M.Art.Image, cover, a, 14);
        else
        {
            G.Fill(c, cover, 14, G.White(0.1f * a));
            Icons.Draw(c, "music", cover.MidX, cover.MidY, 28, G.Paper(0.5f * a));
        }
        float dim = 0.45f * (1 - _playK);
        if (dim > 0) G.Fill(c, cover, 14, G.Black(dim * a));
        c.Restore();

        // The source badge, ringed in the surface colour.
        float bs = 1 - 0.15f * (1 - _playK), bw = 24 * bs, bx = 96 + 7 * bs - bw, by = 106 + 7 * bs - bw;
        G.Fill(c, SKRect.Create(bx - 2.5f, by - 2.5f, bw + 5, bw + 5), 8 * bs + 2.5f, f.Theme.Fill.WithAlpha(G.A(a)));
        G.Fill(c, SKRect.Create(bx, by, bw, bw), 8 * bs, strong.WithAlpha(G.A(a)));
        Icons.Draw(c, "music", bx + bw / 2, by + bw / 2, 14 * bs, G.Ink.WithAlpha(G.A(a)));

        // Title (gliding when long) and artist, lifting out and rising in on a song change.
        var (shift, fade) = SwapShift();
        float x0 = W - 18 - 128, width = x0 - 6 - 112;
        c.Save();
        float ta = a * G.Clamp01(fade);
        DrawTitle(c, string.IsNullOrEmpty(M.Title) ? "Unknown title" : M.Title, 112, 60 + shift, width, ta);
        using (var sub = Fonts.Sub(13))
            G.Text(c, G.Ellipsize(M.Artist, sub, width), 112, 80 + shift, sub,
                M.Art?.TextColor is { } tc ? tc.WithAlpha(G.A(ta)) : G.Paper(0.6f * ta));
        c.Restore();

        // Previous, play/pause, next.
        _prev = SKRect.Create(x0, 52, 36, 36);
        _play = SKRect.Create(x0 + 40, 46, 48, 48);
        _next = SKRect.Create(x0 + 92, 52, 36, 36);
        HoverSquare(f, _prev, 12, Hov(f, "prev", G.In(_prev, f.Pointer)) * (M.CanPrev ? 1 : 0.3f));
        HoverSquare(f, _next, 12, Hov(f, "next", G.In(_next, f.Pointer)) * (M.CanNext ? 1 : 0.3f));
        HoverSquare(f, _play, 16, Hov(f, "play", G.In(_play, f.Pointer)));
        Icons.Solid(c, "M11 12 20 5.5v13zM2.5 12 11 5.5v13z", x0 + 18 - Nudge("prev") * 7, 70, 20, G.Paper((M.CanPrev ? 1 : 0.4f) * a));
        Icons.Solid(c, "M13 12 4 5.5v13zM21.5 12 13 5.5v13z", x0 + 110 + Nudge("next") * 7, 70, 20, G.Paper((M.CanNext ? 1 : 0.4f) * a));
        float press = _pressed == "play" || Nudge("play") > 0.3f ? 0.12f : 0;
        c.Save();
        c.Translate(x0 + 64, 70);
        c.Scale(1 - press);
        PlayMorph(c, 30, G.Paper(a));
        c.Restore();

        // Progress bar and times.
        _bar = SKRect.Create(22, 114, W - 44, 15);
        double pos = _seekPreview ?? M.Position, len = Math.Max(M.Duration, 0.001);
        DrawBar(f, 22, W - 44, 122, strong, (float)Math.Clamp(pos / len, 0, 1));
        using (var mono = Fonts.Mono(11))
        {
            G.Text(c, G.FormatTime(pos), 22, 139, mono, G.Paper(0.627f * a));
            G.Text(c, M.Duration > 0 ? "-" + G.FormatTime(M.Duration - pos) : "--:--", W - 22, 139, mono, G.Paper(0.627f * a), SKTextAlign.Right);
        }

        // Volume, centred under the bar.
        float vx = W / 2 - 54, level = _muted ? 0 : Math.Max(0, _vol);
        _mute = SKRect.Create(vx - 3, 123, 24, 22);
        _volRect = SKRect.Create(vx + 18, 125, 96, 18);
        float ih = Hov(f, "mute", G.In(_mute, f.Pointer));
        Icons.Draw(c, level <= 0.001f ? "volume-x" : level > 0.5f ? "volume-2" : "volume1", vx + 9, 134, 15, G.Paper((0.69f + 0.31f * ih) * a));
        float vk = Hov(f, "vol", G.In(_volRect, f.Pointer) || _pressed == "vol"), vh = 3 + 3 * vk;
        G.Pill(c, vx + 24, 134 - vh / 2, 84, vh, G.White(0.15f * a));
        if (level > 0) G.Pill(c, vx + 24, 134 - vh / 2, Math.Max(vh, 84 * level), vh, G.Paper(a));

        // The palette button: the album's colours behind the card, on or off.
        float px = W - 14 - 24;
        _corner = SKRect.Create(px, 7, 24, 24);
        float ch = Hov(f, "corner", G.In(_corner, f.Pointer));
        bool bg = Host.Settings.AlbumBackground;
        if (bg) G.Fill(c, _corner, 8, G.White((0.12f + 0.1f * ch) * a));
        else
        {
            G.Fill(c, _corner, 8, G.White(0.12f * ch * a));
            G.Stroke(c, SKRect.Create(px + 0.5f, 7.5f, 23, 23), 7.5f, G.White(0.165f * a));
        }
        Icons.Draw(c, "palette", px + 12, 19, 15, G.Paper((bg ? 1 : 0.5f) * a));
    }

    private void DrawEmpty(Frame f)
    {
        var c = f.C;
        float a = f.A;
        var box = SKRect.Create(22, 36, 74, 74);
        G.Fill(c, box, 14, G.White(0.07f * a));
        Icons.Draw(c, "music", box.MidX, box.MidY, 28, G.Paper(0.35f * a));
        using var title = Fonts.Title(16);
        using var sub = Fonts.Sub(13);
        G.Text(c, "Nothing playing", 112, 66, title, G.Paper(a));
        G.Text(c, "Play something in Spotify, a browser", 112, 86, sub, G.Paper(0.55f * a));
        G.Text(c, "or any app that shows in Windows' media flyout.", 112, 103, sub, G.Paper(0.55f * a));
    }

    // ---------- title marquee ----------

    private void DrawTitle(SKCanvas c, string s, float x, float baseline, float width, float a)
    {
        using var font = Fonts.Title(16);
        float w = G.Measure(font, s);
        if (w <= width)
        {
            G.Text(c, s, x, baseline, font, G.Paper(a));
            return;
        }
        // Rests three seconds, then glides at 30 DIP/s with a copy following; the edges fade.
        double run = (w + 22) / 30.0, k = _marquee % (3 + run);
        float dx = k < 3 ? 0 : (float)((k - 3) * 30), lf = Math.Min(1, dx / 10);
        var rect = SKRect.Create(x, baseline - 20, width, 28);
        c.SaveLayer(rect, null);
        G.Text(c, s, x - dx, baseline, font, G.Paper(a));
        G.Text(c, s, x - dx + w + 22, baseline, font, G.Paper(a));
        using (var mask = SKShader.CreateLinearGradient(new SKPoint(x, 0), new SKPoint(x + width, 0),
                   new[] { G.Black(1 - lf), SKColors.Black, SKColors.Black, G.Black(0) },
                   new[] { 0f, 10 / width, 1 - 12 / width, 1f }, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { Shader = mask, BlendMode = SKBlendMode.DstIn })
            c.DrawRect(rect, p);
        c.Restore();
    }

    // ---------- swap animations ----------

    private (float shift, float fade) SwapShift()
    {
        double t = _t - _swapAt;
        if (t >= 0 && t < 0.2) { float u = (float)(t / 0.2); return (-10 * u * u, 1 - u); }
        if (t >= 0.2 && t < 0.52) { float u = (float)((t - 0.2) / 0.32); return (12 * (1 - BackOut(u)), u); }
        return (0, 1);
    }

    private static float BackOut(float u) { float x = u - 1; return 1 + 2.1f * x * x * x + 1.1f * x * x; }

    private float FlipAngle()
    {
        double t = _t - _swapAt;
        if (t < 0 || t >= 0.44) return 0;
        if (t < 0.18) { float u = (float)(t / 0.18); return 90 * u * u; }
        return -90 * (1 - BackOut((float)((t - 0.18) / 0.26)));
    }

    private float Nudge(string key)
    {
        if (!_kicks.TryGetValue(key, out double at)) return 0;
        double t = _t - at;
        if (t < 0 || t > 0.45) return 0;
        Host.Invalidate();
        return t < 0.06 ? (float)(t / 0.06) : (float)(Math.Exp(-(t - 0.06) / 0.09) * Math.Cos((t - 0.06) * 18));
    }

    private static readonly float[][][] PauseShape =
    {
        new[] { new[] { 6f, 4.5f }, new[] { 10.5f, 4.5f }, new[] { 10.5f, 19.5f }, new[] { 6f, 19.5f } },
        new[] { new[] { 13.5f, 4.5f }, new[] { 18f, 4.5f }, new[] { 18f, 19.5f }, new[] { 13.5f, 19.5f } },
    };
    private static readonly float[][][] PlayShape =
    {
        new[] { new[] { 6.5f, 3.8f }, new[] { 13f, 7.9f }, new[] { 13f, 16.1f }, new[] { 6.5f, 20.2f } },
        new[] { new[] { 13f, 7.9f }, new[] { 20f, 12f }, new[] { 20f, 12f }, new[] { 13f, 16.1f } },
    };

    /// <summary>Pause bars that fold into a play triangle (morph 0 = pause, 1 = play).</summary>
    private void PlayMorph(SKCanvas c, float size, SKColor color)
    {
        float e = _morph * _morph * (3 - 2 * _morph), k = size / 24f;
        using var path = new SKPath();
        for (int s = 0; s < 2; s++)
        {
            for (int i = 0; i < 4; i++)
            {
                var p = PauseShape[s][i];
                var q = PlayShape[s][i];
                float x = (p[0] + (q[0] - p[0]) * e - 12) * k, y = (p[1] + (q[1] - p[1]) * e - 12) * k;
                if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            path.Close();
        }
        using var paint = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 1.2f * k, StrokeJoin = SKStrokeJoin.Round };
        c.DrawPath(path, paint);
    }

    // ---------- progress bar (drawn by ProgressBars, shared with the Settings previews) ----------

    private readonly BarState _barState = new();

    private void DrawBar(Frame f, float left, float width, float cy, SKColor strong, float prog)
    {
        float hover = Hov(f, "bar", _pressed == "bar" || G.In(_bar, f.Pointer));
        float hx = (G.In(_bar, f.Pointer) || _pressed == "bar") && f.Pointer is { } pp ? pp.X - left : -1;
        ProgressBars.Draw(f.C, _barState, BarStyle, left, width, cy, strong, prog, f.A, hover, hx, (float)f.T, _playK, M.IsPlaying, f.Dt, M.Title + M.Artist);
    }

    // ---------- input ----------

    public override bool Down(SKPoint p, MouseButtons b)
    {
        if (b != MouseButtons.Left || !M.HasSession) return false;
        if (_bar.Contains(p) && M.CanSeek && M.Duration > 0) { _pressed = "bar"; Drag(p); return true; }
        if (_volRect.Contains(p)) { _pressed = "vol"; Drag(p); return true; }
        if (_play.Contains(p)) _pressed = "play";
        return false;
    }

    public override void Drag(SKPoint p)
    {
        if (_pressed == "bar")
            _seekPreview = Math.Clamp((p.X - _bar.Left) / _bar.Width, 0, 1) * M.Duration;
        else if (_pressed == "vol")
        {
            float vx = _volRect.Left + 6;
            _vol = Math.Clamp((p.X - vx) / 84f, 0, 1);
            _muted = false;
            Host.Audio.Volume = _vol;
        }
        Host.Invalidate();
    }

    public override void Up(SKPoint p)
    {
        if (_pressed == "bar" && _seekPreview is { } s) M.Seek(s);
        _seekPreview = null;
        _pressed = null;
        Host.Invalidate();
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        _pressed = null;
        if (b != MouseButtons.Left) return;
        if (_corner.Contains(p))
        {
            Host.Settings.AlbumBackground = !Host.Settings.AlbumBackground;
            Host.Settings.Save();
        }
        if (!M.HasSession) return;
        if (_play.Contains(p)) { M.TogglePlay(); _kicks["play"] = _t; }
        else if (_prev.Contains(p) && M.CanPrev) { M.Previous(); _kicks["prev"] = _t; }
        else if (_next.Contains(p) && M.CanNext) { M.Next(); _kicks["next"] = _t; }
        else if (_mute.Contains(p)) { _muted = !_muted; Host.Audio.Muted = _muted; }
        Host.Invalidate();
    }

    public override bool Wheel(SKPoint p, int delta)
    {
        float v = MathF.Round((Host.Audio.Volume + Math.Sign(delta) * 0.05f) * 20) / 20f;
        Host.Audio.Volume = Math.Clamp(v, 0, 1);
        _vol = Host.Audio.Volume;
        _muted = false;
        _volAt = DateTime.UtcNow;
        Host.Invalidate();
        return true;
    }
}
