using System.Globalization;
using SkiaSharp;

namespace Cacad;

/// <summary>The clock faces, each drawn small in the resting pill and large in the card's Clock tab (and as
/// previews in Settings). They are made only of type, thin lines and light, drawn straight onto the island's own
/// surface, with no cards, panels or tubes, so glass, gradients and colour show through and the time sits in the
/// surface rather than on it. One instance per place holds that place's animation state.
///
/// Pill coordinates: x = 0 at the centre, y from 0 to the pill's height. Card: draws inside the given box.</summary>
public sealed class ClockFaces
{
    public static readonly (string id, string name)[] Styles =
    {
        ("sky", "Sky"), ("roll", "Roll"), ("editorial", "Editorial"), ("poster", "Poster"),
        ("words", "Words"), ("dots", "Dots"), ("orbit", "Orbit"), ("dial", "Dial"), ("comic", "Comic"),
    };

    /// <summary>Faces from earlier versions map to the nearest one here.</summary>
    public static string Normalize(string id) => Styles.Any(s => s.id == id) ? id : id switch
    {
        "flip" or "nixie" => "roll", "board" => "dots", "swiss" => "poster", "terminal" => "editorial", _ => "sky",
    };

    public static string Next(string style, int dir)
    {
        int i = Math.Max(0, Array.FindIndex(Styles, s => s.id == Normalize(style)));
        return Styles[(i + dir + Styles.Length) % Styles.Length].id;
    }

    /// <summary>The weather, for the Sky face (sunrise, sunset, sky, temperature). Set by the island.</summary>
    public static Func<WeatherService.Now?> Weather = () => null;

    private double _t, _busyUntil;

    /// <summary>Seconds until the next frame is needed (0: every frame; MaxValue: only when the minute changes).</summary>
    public double Interval(string style, bool big) => _busyUntil > _t ? 0 : Normalize(style) switch
    {
        "sky" => 1.0,
        "roll" => big ? 1.0 : 1.0,
        "poster" => 1 / 15.0,
        "dots" => big ? 1 / 15.0 : double.MaxValue,
        "orbit" or "dial" => 1 / 20.0,
        "comic" => 1.0,
        _ => double.MaxValue,
    };

    public void Reset() { _rollInit = false; _phrase = ""; }

    // ---------- colours and type ----------

    private static SKColor Mix(SKColor a, SKColor b, float k) => new(
        (byte)(a.Red + (b.Red - a.Red) * k), (byte)(a.Green + (b.Green - a.Green) * k), (byte)(a.Blue + (b.Blue - a.Blue) * k));

    /// <summary>The accent ramp: a sunset for themes whose accent is plain white, else the theme's own colour.</summary>
    private static SKColor[] Ramp(Theme t) => t.Accent == SKColors.White
        ? new[] { new SKColor(255, 179, 107), new SKColor(255, 94, 138), new SKColor(139, 124, 255) }
        : new[] { t.Accent, Mix(t.Accent, G.PaperBase, 0.45f), G.PaperBase };

    private static string Hours(DateTime now, bool h24) => now.ToString(h24 ? "HH" : "%h", CultureInfo.InvariantCulture);
    private static string Ampm(DateTime now) => now.ToString("tt", CultureInfo.InvariantCulture).ToLowerInvariant();
    private static string Short(DateTime now, bool h24) => now.ToString(h24 ? "H:mm" : "h:mm", CultureInfo.InvariantCulture);

    private static float SpacedWidth(SKFont font, string s, float tracking)
    {
        float w = 0;
        for (int i = 0; i < s.Length; i++) w += font.MeasureText(s[i].ToString()) + (i < s.Length - 1 ? tracking : 0);
        return w;
    }

    private static void Spaced(SKCanvas c, string s, float x, float y, SKFont font, SKPaint p, float tracking)
    {
        foreach (char ch in s)
        {
            string g = ch.ToString();
            c.DrawText(g, x, y, SKTextAlign.Left, font, p);
            x += font.MeasureText(g) + tracking;
        }
    }

    private static void Spaced(SKCanvas c, string s, float x, float y, SKFont font, SKColor color, float tracking, bool center = false)
    {
        if (center) x -= SpacedWidth(font, s, tracking) / 2;
        using var p = new SKPaint { IsAntialias = true, Color = color };
        Spaced(c, s, x, y, font, p, tracking);
    }

    private static float CapHeight(SKFont f) { f.MeasureText("0", out var b); return b.Height; }

    // ---------- the pill ----------

    /// <summary>The width a face takes in a pill of height h (to lay the companion beside it).</summary>
    public static float Width(string style, float h, bool h24) => new ClockFaces().PillFace(null, Normalize(style), DateTime.Now, 0, h, h / 36f, 1, Themes.All[0], h24);

    public void DrawPill(SKCanvas c, string style, float w, float h, float a, Theme theme, bool h24, double t, float dt)
    {
        _t = t;
        _dt = dt;
        if (a > 0.003f) PillFace(c, Normalize(style), DateTime.Now, w, h, h / 36f, a, theme, h24);
    }

    private float _dt;

    /// <summary>Draws a pill face (or, with no canvas, only measures it) and returns its width.</summary>
    private float PillFace(SKCanvas? c, string style, DateTime now, float w, float h, float k, float a, Theme theme, bool h24)
    {
        switch (style)
        {
            case "sky":
            {
                using var f = Fonts.Med(15 * k);
                string s = Short(now, h24);
                float aw = 34 * k, total = aw + 9 * k + f.MeasureText(s);
                if (c == null) return total;
                float x = -total / 2;
                SkyArc(c, now, SKRect.Create(x, h / 2 - 9 * k, aw, 17 * k), a, false);
                G.Text(c, s, x + aw + 9 * k, h / 2 + CapHeight(f) / 2, f, G.Paper(a));
                return total;
            }
            case "roll":
            {
                using var big = Fonts.Med(19 * k);
                using var small = Fonts.Med(10.5f * k);
                string hm = Short(now, h24), ss = now.ToString("ss");
                float dw = big.MeasureText("0"), colonW = big.MeasureText(":") + 1 * k;
                float hmW = hm.Sum(ch => ch == ':' ? colonW : dw), sw = small.MeasureText("0") * 2, total = hmW + 5 * k + sw;
                if (c == null) return total;
                float x = -total / 2, baseline = h / 2 + CapHeight(big) / 2;
                RollText(c, hm + ss, x, baseline, big, small, dw, colonW, a, h24, 5 * k, CapHeight(big));
                return total;
            }
            case "editorial":
            {
                using var big = Fonts.Title(19 * k);
                using var serif = Fonts.SerifItalic(13.5f * k);
                string time = Short(now, h24), day = now.ToString("ddd", CultureInfo.InvariantCulture), tail = h24 ? "" : Ampm(now);
                float tw = SpacedWidth(big, time, -0.6f * k), dw = G.Measure(serif, day), aw = G.Measure(serif, tail), gap = 7 * k;
                float total = dw + gap + tw + (tail.Length > 0 ? gap * 0.6f + aw : 0);
                if (c == null) return total;
                float x = -total / 2, baseline = h / 2 + CapHeight(big) / 2;
                G.Text(c, day, x, baseline, serif, G.Paper(0.55f * a));
                x += dw + gap;
                Spaced(c, time, x, baseline, big, G.Paper(a), -0.6f * k);
                if (tail.Length > 0) G.Text(c, tail, x + tw + gap * 0.6f, baseline, serif, Ramp(theme)[1].WithAlpha(G.A(a)));
                return total;
            }
            case "poster":
            {
                using var font = Fonts.Title(27 * k);
                string hh = Hours(now, h24), mm = now.ToString("mm");
                float tr = -1.4f * k, hw = SpacedWidth(font, hh, tr), mw = SpacedWidth(font, mm, tr), overlap = 2 * k, total = hw + mw - overlap;
                if (c == null) return total;
                PosterText(c, hh, mm, -total / 2, h / 2 + CapHeight(font) / 2, font, tr, overlap, a, theme, 1.1f * k);
                return total;
            }
            case "words":
            {
                var (lead, hour) = PhraseParts(now);
                using var caps = Fonts.Mono(8.6f * k);
                using var serif = Fonts.SerifItalic(19 * k);
                string L = lead.ToUpperInvariant();
                float lw = L.Length > 0 ? SpacedWidth(caps, L, 1.3f * k) + 6 * k : 0, total = lw + G.Measure(serif, hour);
                if (c == null) return total;
                float x = -total / 2, baseline = h / 2 + CapHeight(serif) * 0.5f + 1 * k;
                if (L.Length > 0) Spaced(c, L, x, baseline - 1 * k, caps, G.Paper(0.55f * a), 1.3f * k);
                G.Text(c, hour, x + lw, baseline, serif, G.Paper(a));
                return total;
            }
            case "dots":
            {
                float p = (h - 13 * k) / 7f;
                string s = Hours(now, h24).PadLeft(2, ' ') + ":" + now.ToString("mm");
                float total = Matrix.Width(s.Trim(), p);
                if (c == null) return total;
                Matrix.Draw(c, s.Trim(), -total / 2, (h - 7 * p) / 2, p, G.PaperBase, a, 0.06f);
                return total;
            }
            case "orbit":
            {
                using var f = Fonts.Din(17 * k, SKFontStyleWeight.Light, SKFontStyleWidth.Normal);
                string s = Short(now, h24);
                float r = 11 * k, total = 2 * r + 9 * k + f.MeasureText(s);
                if (c == null) return total;
                float x = -total / 2;
                Orbit(c, x + r, h / 2, r, now, a, theme, false);
                G.Text(c, s, x + 2 * r + 9 * k, h / 2 + CapHeight(f) / 2, f, G.Paper(a));
                return total;
            }
            case "comic":
            {
                using var f = Fonts.Comic(22 * k);
                using var sfxF = Fonts.Comic(10 * k);
                string s = Short(now, h24), sfx = now.Second % 2 == 0 ? "TIK!" : "TOK!";
                float tw = ComicWidth(f, s), gap = 7 * k, total = tw + gap + ComicWidth(sfxF, "TOK!");
                if (c == null) return total;
                float x = -total / 2, baseline = h / 2 + CapHeight(f) / 2;
                ComicText(c, s, x, baseline, f, a, Ramp(theme)[0], 1.0f * k, false, Pop(s));
                // The sound effect, small and tilted, ticking each second.
                c.Save();
                c.Translate(x + tw + gap, baseline - 3 * k);
                c.RotateDegrees(-10);
                ComicText(c, sfx, 0, 0, sfxF, 0.8f * a, Ramp(theme)[0], 0.6f * k, false, 0, plain: true);
                c.Restore();
                return total;
            }
            default: // dial
            {
                using var f = Fonts.Din(17 * k, SKFontStyleWeight.SemiBold);
                string s = Short(now, h24);
                float r = 12 * k, total = 2 * r + 8 * k + f.MeasureText(s);
                if (c == null) return total;
                float x = -total / 2;
                Dial(c, x + r, h / 2, r, now, a, theme, false);
                G.Text(c, s, x + 2 * r + 8 * k, h / 2 + CapHeight(f) / 2, f, G.Paper(a));
                return total;
            }
        }
    }

    // ---------- the card ----------

    public void DrawCard(SKCanvas c, string style, SKRect box, float a, Theme theme, bool h24, double t, float dt)
    {
        _t = t;
        _dt = dt;
        if (a <= 0.003f) return;
        var now = DateTime.Now;
        switch (Normalize(style))
        {
            case "sky": SkyCard(c, now, h24, box, a); break;
            case "roll": RollCard(c, now, h24, box, a); break;
            case "editorial": EditorialCard(c, now, h24, box, a, theme); break;
            case "poster": PosterCard(c, now, h24, box, a, theme); break;
            case "words": WordsCard(c, now, box, a, theme, dt); break;
            case "dots": DotsCard(c, now, h24, box, a); break;
            case "orbit": OrbitCard(c, now, h24, box, a, theme); break;
            case "comic": ComicCard(c, now, h24, box, a, theme); break;
            default: DialCard(c, now, h24, box, a, theme); break;
        }
    }

    // =====================================================================================================
    // Sky: the sun (or the moon) gliding along its arc from sunrise to sunset, lighting the surface around it.
    // =====================================================================================================

    /// <summary>Where the sun or moon is on its arc (0 = rising, 1 = setting), and whether it is day.</summary>
    private static (float pos, bool day) SkyPosition(DateTime now)
    {
        var w = Weather();
        var rise = w is { Sunrise.Ticks: > 0 } ? now.Date + w.Sunrise.TimeOfDay : now.Date.AddHours(6);
        var set = w is { Sunset.Ticks: > 0 } ? now.Date + w.Sunset.TimeOfDay : now.Date.AddHours(18);
        if (now >= rise && now < set) return ((float)((now - rise) / (set - rise)), true);
        // The night runs from today's (or yesterday's) sunset to the next sunrise.
        var from = now < rise ? set.AddDays(-1) : set;
        var to = now < rise ? rise : rise.AddDays(1);
        return ((float)((now - from) / (to - from)), false);
    }

    /// <summary>The arc and its light in a box: a soft band fading at both horizons, the body on it, glowing.</summary>
    private static void SkyArc(SKCanvas c, DateTime now, SKRect box, float a, bool big)
    {
        var (pos, day) = SkyPosition(now);
        // A circle whose chord is the box's width, rising box.Height above the bottom edge.
        float chord = box.Width, sag = box.Height * (big ? 0.92f : 0.95f);
        float R = (chord * chord / 4 + sag * sag) / (2 * sag);
        float cx = box.MidX, cy = box.Bottom - sag + R;
        float half = MathF.Asin(chord / 2 / R);            // radians either side of the top
        float start = -MathF.PI / 2 - half, sweep = 2 * half;
        var oval = SKRect.Create(cx - R, cy - R, 2 * R, 2 * R);
        float band = big ? 7 : 2.2f;
        // The band: brightest overhead, gone at the horizons.
        using (var sh = SKShader.CreateLinearGradient(new SKPoint(box.Left, 0), new SKPoint(box.Right, 0),
                   new[] { G.White(0), G.White(0.22f * a), G.White(0.30f * a), G.White(0.22f * a), G.White(0) }, new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = band, StrokeCap = SKStrokeCap.Butt, Shader = sh })
        using (var path = new SKPath())
        {
            path.AddArc(oval, start * 180 / MathF.PI, sweep * 180 / MathF.PI);
            c.DrawPath(path, p);
        }
        float ang = start + sweep * Math.Clamp(pos, 0, 1);
        float bx = cx + R * MathF.Cos(ang), by = cy + R * MathF.Sin(ang);
        var light = day ? new SKColor(255, 240, 214) : new SKColor(226, 234, 255);
        float br = big ? 10 : 3.6f;
        // Light falling on the surface around it.
        using (var halo = SKShader.CreateRadialGradient(new SKPoint(bx, by), br * (big ? 9 : 4.5f),
                   new[] { light.WithAlpha(G.A(0.26f * a)), light.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
        using (var hp = new SKPaint { IsAntialias = true, Shader = halo })
            c.DrawCircle(bx, by, br * (big ? 9 : 4.5f), hp);
        using (var glow = new SKPaint { IsAntialias = true, Color = light.WithAlpha(G.A(0.8f * a)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, br * 0.7f) })
            c.DrawCircle(bx, by, br * 1.15f, glow);
        using (var body = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(G.A(a)) })
            c.DrawCircle(bx, by, br, body);
        if (!day && big)
        {
            // The moon's phase: its shadow drawn over the disc.
            float phase = MoonPhase(now);
            float lit = MathF.Cos(phase * 2 * MathF.PI);  // 1 new, -1 full
            if (lit > -0.95f)
            {
                c.Save();
                using var clip = new SKPath(); clip.AddCircle(bx, by, br);
                c.ClipPath(clip, SKClipOperation.Intersect, true);
                using var shade = new SKPaint { IsAntialias = true, Color = new SKColor(20, 22, 30, G.A(0.82f * a)) };
                float off = br * 2 * (1 - (1 - lit) / 2) * (phase < 0.5f ? -1 : 1);
                c.DrawCircle(bx + off, by, br * 1.02f, shade);
                c.Restore();
            }
        }
    }

    /// <summary>0 = new moon, 0.5 = full.</summary>
    private static float MoonPhase(DateTime now)
    {
        double days = (now.ToUniversalTime() - new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)).TotalDays;
        return (float)(days / 29.530588853 % 1 + 1) % 1;
    }

    private static string MoonName(DateTime now) => MoonPhase(now) switch
    {
        < 0.03f or >= 0.97f => "New moon", < 0.22f => "Waxing crescent", < 0.28f => "First quarter", < 0.47f => "Waxing gibbous",
        < 0.53f => "Full moon", < 0.72f => "Waning gibbous", < 0.78f => "Last quarter", _ => "Waning crescent",
    };

    private static void SkyCard(SKCanvas c, DateTime now, bool h24, SKRect box, float a)
    {
        var (_, day) = SkyPosition(now);
        string title = !day ? "Night" : now.Hour switch { < 11 => "Morning", < 13 => "Midday", < 17 => "Afternoon", _ => "Evening" };
        var w = Weather();
        string sub = !day ? MoonName(now) : w != null ? WeatherService.Describe(w.Code) : now.ToString("dddd d MMMM", CultureInfo.InvariantCulture);
        SkyArc(c, now, SKRect.Create(box.Left + 10, box.Top + 30, box.Width - 20, box.Height - 26), a, true);
        using var t = Fonts.Title(23);
        using var s = Fonts.Sub(12.5f);
        G.Text(c, title, box.Left + 8, box.Top + 16, t, G.Paper(a));
        G.Text(c, sub + (w != null && !day ? $"  ·  {Math.Round(w.Temp)}°" : ""), box.Left + 9, box.Top + 34, s, G.Paper(0.45f * a));
        using var time = Fonts.Med(19);
        string right = Short(now, h24) + (h24 ? "" : " " + Ampm(now));
        G.Text(c, right, box.Right - 8, box.Top + 14, time, G.Paper(a), SKTextAlign.Right);
        if (w != null && day)
            G.Text(c, $"{Math.Round(w.Temp)}°", box.Right - 8, box.Top + 34, s, G.Paper(0.45f * a), SKTextAlign.Right);
    }

    // =====================================================================================================
    // Roll: clean numerals; a digit that changes rolls in from above while the old one sinks away and fades.
    // =====================================================================================================

    private readonly char[] _cur = new char[8], _prev = new char[8];
    private readonly double[] _at = new double[8];
    private bool _rollInit;

    /// <summary>Draws "h:mmss" (pill: the seconds small after a gap) or "hh:mm:ss" columns with the roll.</summary>
    private void RollText(SKCanvas c, string s, float x, float baseline, SKFont big, SKFont small, float dw, float colonW, float a, bool h24, float gap, float capH)
    {
        // s = "h:mm" + "ss"
        int hmLen = s.Length - 2;
        Track(s.Replace(":", ""));
        int slot = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            bool sec = i >= hmLen;
            var font = sec ? small : big;
            float cw = sec ? small.MeasureText("0") : dw;
            if (i == hmLen) x += gap;
            if (ch == ':') { G.Text(c, ":", x, baseline - capH * 0.06f, big, G.Paper(0.55f * a)); x += colonW; continue; }
            float y = sec ? baseline - capH + CapHeight(small) : baseline;
            Digit(c, slot++, x, y, cw, font, sec ? 0.55f * a : a);
            x += cw;
        }
    }

    private void Track(string digits)
    {
        for (int i = 0; i < digits.Length && i < 8; i++)
        {
            if (!_rollInit) { _cur[i] = _prev[i] = digits[i]; _at[i] = -9; }
            else if (digits[i] != _cur[i]) { _prev[i] = _cur[i]; _cur[i] = digits[i]; _at[i] = _t; }
        }
        _rollInit = true;
    }

    private void Digit(SKCanvas c, int slot, float x, float baseline, float cw, SKFont font, float a)
    {
        float u = (float)Math.Clamp((_t - _at[slot]) / 0.5, 0, 1);
        float capH = CapHeight(font);
        if (u >= 1) { G.Text(c, _cur[slot].ToString(), x + cw / 2, baseline, font, G.Paper(a), SKTextAlign.Center); return; }
        _busyUntil = _t + 0.05;
        float e = 1 - MathF.Pow(1 - u, 3), travel = capH * 0.9f;
        // The new digit drops in from above, the old one sinks below, both fading through a soft mask.
        var bounds = SKRect.Create(x - 2, baseline - capH - travel, cw + 4, capH + travel * 2);
        c.SaveLayer(bounds, null);
        G.Text(c, _prev[slot].ToString(), x + cw / 2, baseline + e * travel, font, G.Paper(a * (1 - e)), SKTextAlign.Center);
        G.Text(c, _cur[slot].ToString(), x + cw / 2, baseline - (1 - e) * travel, font, G.Paper(a * (0.25f + 0.75f * e)), SKTextAlign.Center);
        using (var mask = SKShader.CreateLinearGradient(new SKPoint(0, bounds.Top), new SKPoint(0, bounds.Bottom),
                   new[] { SKColors.Transparent, SKColors.White, SKColors.White, SKColors.Transparent }, new[] { 0.05f, 0.4f, 0.62f, 0.95f }, SKShaderTileMode.Clamp))
        using (var mp = new SKPaint { Shader = mask, BlendMode = SKBlendMode.DstIn })
            c.DrawRect(bounds, mp);
        c.Restore();
    }

    private void RollCard(SKCanvas c, DateTime now, bool h24, SKRect box, float a)
    {
        string hh = now.ToString(h24 ? "HH" : "hh", CultureInfo.InvariantCulture), mm = now.ToString("mm"), ss = now.ToString("ss");
        Track(hh + mm + ss);
        using var font = Fonts.Med(Math.Min(62, box.Height * 0.6f));
        using var label = Fonts.Sub(10);
        float dw = font.MeasureText("0"), capH = CapHeight(font), colon = dw * 0.55f;
        float total = 6 * dw + 2 * colon;
        float x = box.MidX - total / 2, baseline = box.Top + capH + 14;
        string[] names = { "HOURS", "MINUTES", "SECONDS" };
        string[] groups = { hh, mm, ss };
        int slot = 0;
        for (int gi = 0; gi < 3; gi++)
        {
            float gx = x;
            foreach (char _ in groups[gi]) { Digit(c, slot, x, baseline, dw, font, a); slot++; x += dw; }
            Spaced(c, names[gi], (gx + x) / 2, baseline + 22, label, G.Paper(0.42f * a), 1.4f, true);
            if (gi < 2)
            {
                // Square colon dots, set high like the reference.
                float d = Math.Max(2.5f, capH * 0.075f);
                G.Fill(c, SKRect.Create(x + colon / 2 - d / 2, baseline - capH * 0.78f, d, d), 0.5f, G.Paper(0.6f * a));
                G.Fill(c, SKRect.Create(x + colon / 2 - d / 2, baseline - capH * 0.50f, d, d), 0.5f, G.Paper(0.6f * a));
                x += colon;
            }
        }
        if (!h24) G.Text(c, Ampm(now), x + 6, baseline - capH + 10, label, G.Paper(0.42f * a));
    }

    // =====================================================================================================
    // Editorial: an italic weekday, a bold time, an italic am/pm in the accent.
    // =====================================================================================================

    private static void EditorialCard(SKCanvas c, DateTime now, bool h24, SKRect box, float a, Theme theme)
    {
        using var big = Fonts.Title(Math.Min(68, box.Height * 0.66f));
        using var serif = Fonts.SerifItalic(20);
        using var tail = Fonts.SerifItalic(26);
        string time = Short(now, h24);
        float tr = -big.Size * 0.035f, tw = SpacedWidth(big, time, tr), capH = CapHeight(big);
        float aw = h24 ? 0 : G.Measure(tail, Ampm(now)) + 10;
        float x = box.MidX - (tw + aw) / 2, baseline = box.Top + 26 + capH;
        G.Text(c, now.ToString("dddd, d MMMM", CultureInfo.InvariantCulture), x + 2, box.Top + 16, serif, G.Paper(0.5f * a));
        Spaced(c, time, x, baseline, big, G.Paper(a), tr);
        if (!h24) G.Text(c, Ampm(now), x + tw + 10, baseline, tail, Ramp(theme)[1].WithAlpha(G.A(a)));
    }

    // =====================================================================================================
    // Poster: a gradient hour overlapping outlined minutes, the gradient drifting slowly.
    // =====================================================================================================

    private void PosterText(SKCanvas c, string hh, string mm, float x, float baseline, SKFont font, float tr, float overlap, float a, Theme theme, float stroke)
    {
        float hw = SpacedWidth(font, hh, tr), capH = CapHeight(font);
        float drift = (float)(Math.Sin(_t * 0.5) * 0.5 + 0.5);
        var ramp = Ramp(theme).Select(col => col.WithAlpha(G.A(a))).ToArray();
        using (var sh = SKShader.CreateLinearGradient(new SKPoint(x - capH * 0.4f * drift, baseline - capH), new SKPoint(x + hw + capH * 0.4f * (1 - drift), baseline), ramp, null, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { IsAntialias = true, Shader = sh })
            Spaced(c, hh, x, baseline, font, p, tr);
        using (var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke, Color = G.Paper(0.92f * a) })
            Spaced(c, mm, x + hw - overlap, baseline, font, outline, tr);
    }

    private void PosterCard(SKCanvas c, DateTime now, bool h24, SKRect box, float a, Theme theme)
    {
        using var font = Fonts.Title(Math.Min(96, box.Height * 0.9f));
        string hh = Hours(now, h24), mm = now.ToString("mm");
        float tr = -font.Size * 0.05f, overlap = font.Size * 0.07f;
        float total = SpacedWidth(font, hh, tr) + SpacedWidth(font, mm, tr) - overlap, capH = CapHeight(font);
        float x = box.MidX - total / 2, baseline = box.MidY + capH / 2 - 2;
        PosterText(c, hh, mm, x, baseline, font, tr, overlap, a, theme, 1.6f);
        using var mono = Fonts.Mono(10);
        string side = (now.ToString("ddd dd", CultureInfo.InvariantCulture) + (h24 ? "" : " · " + Ampm(now))).ToUpperInvariant();
        Spaced(c, side, x + total + 10, baseline - capH + 8, mono, G.Paper(0.45f * a), 1.6f);
    }

    // =====================================================================================================
    // Comic: the time lettered like a manga sound effect: heavy capitals, each glyph tilted and nudged by hand, a
    // white fill inside a thick ink outline, dropped onto an accent shadow (screentone dots in the card). A tilted
    // "TIK!" / "TOK!" ticks beside it, and on each new minute the digits pop and burst lines flash out.
    // =====================================================================================================

    private string _comicLast = "";
    private double _popAt = -10;

    /// <summary>The minute pop, 1 at the change falling to 0 (0 when not popping).</summary>
    private float Pop(string time)
    {
        if (time != _comicLast) { if (_comicLast.Length > 0) _popAt = _t; _comicLast = time; }
        float k = (float)((_t - _popAt) / 0.6);
        if (k >= 1 || k < 0) return 0;
        _busyUntil = _t + 0.05;
        return 1 - k;
    }

    private static float ComicWidth(SKFont f, string s) => s.Sum(ch => f.MeasureText(ch.ToString())) + (s.Length - 1) * f.Size * 0.05f;

    private static void ComicText(SKCanvas c, string s, float x, float baseline, SKFont font, float a, SKColor accent, float k, bool tone, float pop, bool plain = false)
    {
        float track = font.Size * 0.05f, drop = font.Size * 0.075f;
        // The pop: a quick swell and settle.
        float bounce = pop > 0 ? 1 + 0.22f * MathF.Sin(pop * MathF.PI) * pop : 1;
        var ink = new SKColor(12, 10, 16);
        for (int i = 0; i < s.Length; i++)
        {
            string g = s[i].ToString();
            float gw = font.MeasureText(g);
            // Hand lettering: each glyph tilted and lifted a little, the same way every time.
            float tilt = plain ? 0 : ((i * 37 + 3) % 7 - 3) * 1.3f;
            float lift = plain ? 0 : ((i * 53 + 1) % 5 - 2) * 0.04f * font.Size;
            c.Save();
            c.Translate(x + gw / 2, baseline + lift);
            c.RotateDegrees(tilt);
            c.Scale(bounce);
            var lean = SKMatrix.CreateSkew(-0.14f, 0);
            c.Concat(ref lean);
            using var path = font.GetTextPath(g, new SKPoint(-gw / 2, 0));
            if (!plain)
            {
                // The drop shadow in the accent: screentone dots in the card, solid in the pill.
                c.Save();
                c.Translate(drop, drop * 0.8f);
                if (tone)
                {
                    float pitch = Math.Max(2.6f, font.Size * 0.05f);
                    using var dots = SKShader.CreateImage(Manga.DotsTile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                        SKMatrix.CreateScale(pitch / 16f, pitch / 16f).PostConcat(SKMatrix.CreateRotationDegrees(45)));
                    using var tint = SKColorFilter.CreateBlendMode(accent, SKBlendMode.SrcIn);
                    using var p = new SKPaint { IsAntialias = true, Shader = dots, ColorFilter = tint, Color = SKColors.White.WithAlpha(G.A(a)) };
                    c.DrawPath(path, p);
                    using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = font.Size * 0.02f, Color = accent.WithAlpha(G.A(0.8f * a)) };
                    c.DrawPath(path, edge);
                }
                else using (var p = new SKPaint { IsAntialias = true, Color = accent.WithAlpha(G.A(a)) }) c.DrawPath(path, p);
                c.Restore();
            }
            using (var stroke = new SKPaint
                   {
                       IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round,
                       StrokeWidth = font.Size * (plain ? 0.1f : 0.13f), Color = ink.WithAlpha(G.A(a)),
                   })
                c.DrawPath(path, stroke);
            using (var fill = new SKPaint { IsAntialias = true, Color = plain ? accent.WithAlpha(G.A(a)) : G.Paper(a) }) c.DrawPath(path, fill);
            c.Restore();
            x += gw + track;
        }
    }

    /// <summary>Burst lines flashing out round the time: faint at rest, bright as it pops.</summary>
    private static void Burst(SKCanvas c, float cx, float cy, float rx, float ry, float a, float pop, int seed)
    {
        using var path = new SKPath();
        const int n = 30;
        for (int i = 0; i < n; i++)
        {
            float h1 = Rand(seed * 97 + i), h2 = Rand(seed * 89 + i * 3 + 1);
            float ang = (i + h1 * 0.7f) / n * MathF.PI * 2, half = 0.012f + 0.02f * h2;
            float r0 = 1.0f + 0.12f * h2, r1 = r0 + 0.25f + 0.35f * h1 + 0.25f * pop;
            path.MoveTo(cx + MathF.Cos(ang) * rx * r0, cy + MathF.Sin(ang) * ry * r0);
            path.LineTo(cx + MathF.Cos(ang - half) * rx * r1, cy + MathF.Sin(ang - half) * ry * r1);
            path.LineTo(cx + MathF.Cos(ang + half) * rx * r1, cy + MathF.Sin(ang + half) * ry * r1);
            path.Close();
        }
        using var p = new SKPaint { IsAntialias = true, Color = G.Paper((0.1f + 0.45f * pop) * a) };
        c.DrawPath(path, p);
    }

    private static float Rand(int n)
    {
        uint x = unchecked((uint)n * 2654435761u);
        x ^= x >> 15; x = unchecked(x * 2246822519u); x ^= x >> 13;
        return (x & 0xFFFFFF) / (float)0x1000000;
    }

    private void ComicCard(SKCanvas c, DateTime now, bool h24, SKRect box, float a, Theme theme)
    {
        using var font = Fonts.Comic(Math.Min(88, box.Height * 0.74f));
        using var label = Fonts.Comic(13);
        using var sfxF = Fonts.Comic(20);
        string s = Short(now, h24);
        float pop = Pop(s);
        var accent = Ramp(theme)[0];
        float tw = ComicWidth(font, s), capH = CapHeight(font), sfxW = ComicWidth(sfxF, "TOK!");
        float x = box.MidX - (tw + 18 + sfxW) / 2, baseline = box.MidY + capH / 2 + 4;
        Burst(c, x + tw / 2, baseline - capH / 2, tw * 0.62f, capH * 0.82f, a, pop, now.Minute);
        // The chapter (day of the year) and the day, like a page header.
        string header = $"CH. {now.DayOfYear}  ·  {now.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant()}";
        Spaced(c, header, x + 2, baseline - capH - 12, label, G.Paper(0.5f * a), 1.4f);
        ComicText(c, s, x, baseline, font, a, accent, font.Size / 22f, true, pop);
        // The sound effect, tilted, ticking each second; am/pm under it.
        c.Save();
        c.Translate(x + tw + 18, baseline - capH * 0.5f);
        c.RotateDegrees(-12);
        ComicText(c, now.Second % 2 == 0 ? "TIK!" : "TOK!", 0, 0, sfxF, 0.9f * a, accent, 1, false, 0, plain: true);
        c.Restore();
        if (!h24) Spaced(c, Ampm(now).ToUpperInvariant(), x + tw + 22, baseline, label, G.Paper(0.55f * a), 1.4f);
    }

    // =====================================================================================================
    // Words: small tracked capitals lead into a big italic hour.
    // =====================================================================================================

    private static (string lead, string hour) PhraseParts(DateTime now)
    {
        string[] names = { "twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven" };
        int m5 = (int)Math.Round(now.Minute / 5.0) * 5, hour = now.Hour;
        if (m5 == 60) { m5 = 0; hour++; }
        if (m5 > 30) hour++;
        string hn = names[hour % 12];
        static string n(int m) => m switch { 5 => "five", 10 => "ten", 20 => "twenty", _ => "twenty-five" };
        return m5 switch
        {
            0 => ("", hn + " o'clock"),
            15 => ("quarter past", hn),
            30 => ("half past", hn),
            45 => ("quarter to", hn),
            < 30 => (n(m5) + " past", hn),
            _ => (n(60 - m5) + " to", hn),
        };
    }

    private string _phrase = "";
    private DateTime _phraseAt;
    private float _phraseK = 1;

    private void WordsCard(SKCanvas c, DateTime now, SKRect box, float a, Theme theme, float dt)
    {
        var (lead0, hour0) = PhraseParts(now);
        string p = lead0 + hour0;
        if (_phrase == "") { _phrase = p; _phraseAt = now; }
        DateTime prevAt = _phraseAt;
        if (p != _phrase) { _phrase = p; prevAt = _phraseAt; _phraseAt = now; _phraseK = 0; }
        _phraseK = G.Ease(_phraseK, 1, 4, dt);
        if (_phraseK > 0.995f) _phraseK = 1; else _busyUntil = _t + 0.05;
        string partOfDay = now.Hour switch { >= 5 and < 12 => "in the morning", >= 12 and < 17 => "in the afternoon", >= 17 and < 21 => "in the evening", _ => "at night" };

        void Draw(DateTime at, float alpha, float dy)
        {
            if (alpha < 0.01f) return;
            var (lead, hour) = PhraseParts(at);
            using var caps = Fonts.Mono(11.5f);
            using var serif = Fonts.SerifItalic(Math.Min(58, box.Height * 0.52f));
            using var small = Fonts.SerifItalic(15);
            float cy = box.Top + box.Height * 0.62f + dy;
            Spaced(c, ("it's " + lead).ToUpperInvariant().Trim(), box.MidX, cy - serif.Size * 0.78f, caps, G.Paper(0.5f * alpha), 2.6f, true);
            G.Text(c, hour, box.MidX, cy, serif, G.Paper(alpha), SKTextAlign.Center);
            G.Text(c, partOfDay + ", " + at.ToString("dddd", CultureInfo.InvariantCulture).ToLowerInvariant(), box.MidX, cy + 24, small, Ramp(theme)[1].WithAlpha(G.A(0.85f * alpha)), SKTextAlign.Center);
        }
        if (_phraseK < 1) Draw(prevAt, a * (1 - _phraseK), -10 * _phraseK);
        Draw(now, a * _phraseK, 10 * (1 - _phraseK));
    }

    // =====================================================================================================
    // Dots: white dot-matrix digits; in the card a line of sixty dots fills with the seconds.
    // =====================================================================================================

    private static void DotsCard(SKCanvas c, DateTime now, bool h24, SKRect box, float a)
    {
        string s = Hours(now, h24) + ":" + now.ToString("mm");
        float p = Math.Min(8.5f, (box.Height - 34) / 7f), w = Matrix.Width(s, p);
        Matrix.Draw(c, s, box.MidX - w / 2, box.Top + 6, p, G.PaperBase, a, 0.05f);
        // Sixty dots: the seconds gone are lit, the current one breathes.
        float gap = Math.Min(5.4f, (box.Width - 40) / 60f), y = box.Top + 6 + 7 * p + 18, x0 = box.MidX - gap * 59 / 2;
        float pulse = 0.55f + 0.45f * MathF.Sin(now.Millisecond / 1000f * MathF.PI);
        for (int i = 0; i < 60; i++)
        {
            float alpha = i < now.Second ? 0.75f : i == now.Second ? pulse : 0.1f;
            G.Circle(c, x0 + i * gap, y, i % 15 == 0 ? 1.4f : 1.1f, G.Paper(alpha * a));
        }
        if (!h24)
        {
            using var mono = Fonts.Mono(9.5f);
            Spaced(c, Ampm(now).ToUpperInvariant(), box.MidX + w / 2 + 8, box.Top + 6 + p * 2, mono, G.Paper(0.45f * a), 1.4f);
        }
    }

    // =====================================================================================================
    // Orbit: thin numerals within a faint ring; the minute and the second circle it as two small moons.
    // =====================================================================================================

    private static void Orbit(SKCanvas c, float cx, float cy, float r, DateTime now, float a, Theme theme, bool big)
    {
        using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = big ? 1 : 0.9f, Color = G.White(0.16f * a) })
            c.DrawCircle(cx, cy, r, ring);
        double sec = now.Second + now.Millisecond / 1000.0, min = now.Minute + sec / 60;
        void Moon(double turns, float size, SKColor col)
        {
            float ang = (float)(turns * Math.PI * 2 - Math.PI / 2);
            G.Circle(c, cx + MathF.Cos(ang) * r, cy + MathF.Sin(ang) * r, size, col);
        }
        Moon(min / 60, big ? 4.2f : 2.4f, Ramp(theme)[0].WithAlpha(G.A(a)));
        Moon(sec / 60, big ? 2.2f : 1.3f, G.Paper(0.85f * a));
    }

    private static void OrbitCard(SKCanvas c, DateTime now, bool h24, SKRect box, float a, Theme theme)
    {
        float r = Math.Min(box.Height / 2 - 2, 52);
        Orbit(c, box.MidX, box.MidY, r, now, a, theme, true);
        using var f = Fonts.Din(Math.Min(40, r * 0.78f), SKFontStyleWeight.Light, SKFontStyleWidth.Normal);
        string s = Short(now, h24);
        G.Text(c, s, box.MidX, box.MidY + CapHeight(f) / 2, f, G.Paper(a), SKTextAlign.Center);
        using var mono = Fonts.Mono(9.5f);
        string under = (now.ToString("ddd d MMM", CultureInfo.InvariantCulture) + (h24 ? "" : " · " + Ampm(now))).ToUpperInvariant();
        Spaced(c, under, box.MidX, box.MidY + CapHeight(f) / 2 + 16, mono, G.Paper(0.4f * a), 1.4f, true);
    }

    // =====================================================================================================
    // Dial: only the ticks and the hands, floating on the surface; the card adds the day in italics.
    // =====================================================================================================

    private static void Dial(SKCanvas c, float cx, float cy, float r, DateTime now, float a, Theme theme, bool big)
    {
        using (var tick = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round })
            for (int i = 0; i < 60; i++)
            {
                bool major = i % 5 == 0, quarter = i % 15 == 0;
                if (!big && !major) continue;
                float ang = i * MathF.PI / 30, len = quarter ? r * 0.16f : major ? r * 0.09f : r * 0.03f;
                tick.StrokeWidth = quarter ? Math.Max(1.1f, r * 0.04f) : Math.Max(0.6f, r * 0.022f);
                tick.Color = G.Paper((quarter ? 0.8f : major ? 0.45f : 0.2f) * a);
                float s = MathF.Sin(ang), co = -MathF.Cos(ang);
                c.DrawLine(cx + s * (r - len), cy + co * (r - len), cx + s * r, cy + co * r, tick);
            }
        double sec = now.Second + now.Millisecond / 1000.0, min = now.Minute + sec / 60, hr = now.Hour % 12 + min / 60;
        void Hand(double turns, float len, float width, SKColor col, float tail)
        {
            float ang = (float)(turns * Math.PI * 2), s = MathF.Sin(ang), co = -MathF.Cos(ang);
            using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = width, Color = col };
            c.DrawLine(cx - s * tail, cy - co * tail, cx + s * len, cy + co * len, p);
        }
        var acc = Ramp(theme)[0];
        Hand(hr / 12, r * 0.52f, Math.Max(1.6f, r * 0.07f), G.Paper(a), 0);
        Hand(min / 60, r * 0.8f, Math.Max(1.1f, r * 0.045f), G.Paper(a), 0);
        if (big) Hand(sec / 60, r * 0.88f, Math.Max(0.7f, r * 0.015f), acc.WithAlpha(G.A(a)), r * 0.16f);
        G.Circle(c, cx, cy, Math.Max(1.3f, r * 0.045f), (big ? acc : G.PaperBase).WithAlpha(G.A(a)));
    }

    private static void DialCard(SKCanvas c, DateTime now, bool h24, SKRect box, float a, Theme theme)
    {
        float r = Math.Min(box.Height / 2 - 2, 52), cx = box.Left + 24 + r, cy = box.MidY;
        Dial(c, cx, cy, r, now, a, theme, true);
        float x = cx + r + 32;
        using var day = Fonts.SerifItalic(34);
        using var date = Fonts.Sub(14);
        using var mono = Fonts.Mono(10.5f);
        G.Text(c, now.ToString("dddd", CultureInfo.InvariantCulture), x, cy - 4, day, G.Paper(a));
        G.Text(c, now.ToString("d MMMM", CultureInfo.InvariantCulture), x, cy + 18, date, G.Paper(0.55f * a));
        Spaced(c, now.ToString(h24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture).ToUpperInvariant(), x, cy + 36, mono, G.Paper(0.38f * a), 1.4f);
    }

    // ---------- a 5x7 dot font (digits) ----------

    private static class Matrix
    {
        private static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
            [':'] = new[] { ".", ".", "#", ".", "#", ".", "." },
        };

        private static string[] Of(char ch) => Glyphs.TryGetValue(ch, out var g) ? g : Glyphs['0'];

        public static float Width(string s, float p) => s.Sum(ch => (Of(ch)[0].Length + 1) * p) - p;

        /// <summary>The text as round dots of pitch p; the unlit dots of each glyph show at <paramref name="ghost"/>.</summary>
        public static void Draw(SKCanvas c, string s, float x, float y, float p, SKColor col, float a, float ghost)
        {
            float r = p * 0.34f;
            using var off = new SKPaint { IsAntialias = true, Color = col.WithAlpha(G.A(ghost * a)) };
            using var on = new SKPaint { IsAntialias = true, Color = col.WithAlpha(G.A(a)) };
            foreach (char ch in s)
            {
                var g = Of(ch);
                for (int row = 0; row < 7; row++)
                    for (int cc = 0; cc < g[row].Length; cc++)
                        c.DrawCircle(x + cc * p + p / 2, y + row * p + p / 2, r, g[row][cc] == '#' ? on : off);
                x += (g[0].Length + 1) * p;
            }
        }
    }
}
