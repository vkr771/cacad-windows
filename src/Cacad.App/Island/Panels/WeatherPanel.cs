using SkiaSharp;

namespace Cacad;

/// <summary>The card's weather: now (temperature, sky, feels-like, today's high and low) and the next hours, each
/// with its chance of rain. From <see cref="WeatherService"/>.</summary>
public sealed class WeatherPanel : IslandPanel
{
    public WeatherPanel(IIslandHost host) : base(host) { }

    public override string Id => "weather";
    public override string Icon => "cloud-sun";
    public override string Title => "Weather";
    public override float Height => 192;
    public override bool Visible => Host.Settings.Weather;

    public override void DrawBackground(Frame f)
    {
        var w = Host.Weather.Current;
        if (w == null) return;
        // A wash of the sky's colour from the top-left: warm and clear, grey and cloudy, deep blue at night.
        var sky = !w.Day ? new SKColor(60, 70, 150) : WeatherService.Rainy(w.Code) ? new SKColor(80, 120, 170)
            : w.Code <= 1 ? new SKColor(255, 170, 70) : new SKColor(130, 150, 180);
        using var sh = SKShader.CreateRadialGradient(new SKPoint(60, 40), f.W * 0.75f,
            new[] { sky.WithAlpha(G.A(0.38f * f.A)), sky.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = sh };
        f.C.DrawRect(SKRect.Create(-12, -6, f.W + 24, f.H + 12), p);
    }

    public override void Draw(Frame f)
    {
        var c = f.C;
        float a = f.A;
        var w = Host.Weather.Current;
        if (w == null)
        {
            using var t = Fonts.Title(15);
            using var s = Fonts.Sub(12.5f);
            Icons.Draw(c, "cloud-sun", f.W / 2, 84, 26, G.Paper(0.35f * a));
            string status = Host.Weather.Status;
            G.Text(c, status.Length == 0 ? "Looking at the sky…" : status, f.W / 2, 124, t, G.Paper(a), SKTextAlign.Center);
            G.Text(c, "Uses Windows' location, or the city you type in Settings › Live.", f.W / 2, 144, s, G.Paper(0.5f * a), SKTextAlign.Center);
            return;
        }
        string deg = "°";
        // Now.
        Icons.Draw(c, WeatherService.Icon(w.Code, w.Day), 46, 66, 34, G.Paper(a), 1.6f);
        using (var big = Fonts.Din(46, SKFontStyleWeight.Medium, SKFontStyleWidth.Normal))
            G.Text(c, $"{Math.Round(w.Temp)}{deg}", 78, 84, big, G.Paper(a));
        using (var t = Fonts.Title(14))
            G.Text(c, WeatherService.Describe(w.Code), 200, 58, t, G.Paper(a));
        using (var s = Fonts.Sub(12))
        {
            G.Text(c, $"Feels {Math.Round(w.Feels)}{deg}  ·  H {Math.Round(w.High)}{deg}  L {Math.Round(w.Low)}{deg}", 200, 77, s, G.Paper(0.65f * a));
            string rain = w.RainChance >= 20 ? $"{w.RainChance}% chance of rain today" : "No rain expected";
            G.Text(c, G.Ellipsize($"{w.Place}  ·  {rain}", s, f.W - 220), 200, 94, s, G.Paper(0.45f * a));
        }

        // The next hours.
        var hours = w.Hours.Where(h => h.At > DateTime.Now.AddMinutes(-30)).Take(6).ToList();
        if (hours.Count == 0) return;
        float left = 18, right = f.W - 18, top = 112, cw = (right - left) / hours.Count;
        G.Fill(c, SKRect.Create(left, top, right - left, 70), 14, G.White(0.06f * a));
        using var time = Fonts.Sub(10.5f);
        using var temp = Fonts.Med(13);
        using var pct = Fonts.Mono(9.5f);
        for (int i = 0; i < hours.Count; i++)
        {
            var h = hours[i];
            float cx = left + cw * (i + 0.5f);
            bool now = i == 0;
            G.Text(c, now ? "Now" : h.At.ToString(Host.Settings.Clock24h ? "HH:00" : "h tt").ToLowerInvariant(), cx, top + 16, time, G.Paper((now ? 0.9f : 0.55f) * a), SKTextAlign.Center);
            bool day = h.At.Hour is >= 6 and < 19;
            Icons.Draw(c, WeatherService.Icon(h.Code, day), cx, top + 33, 15, G.Paper(0.85f * a));
            G.Text(c, $"{Math.Round(h.Temp)}{deg}", cx, top + 56, temp, G.Paper(a), SKTextAlign.Center);
            if (h.Rain >= 30)
                G.Text(c, $"{h.Rain}%", cx, top + 66, pct, new SKColor(120, 190, 255, G.A(a)), SKTextAlign.Center);
        }
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        // Open a full forecast for the same place.
        string q = Host.Settings.WeatherCity.Length > 0 ? Host.Settings.WeatherCity : "weather";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://www.bing.com/search?q={Uri.EscapeDataString(q + " weather")}") { UseShellExecute = true }); } catch { }
    }
}
