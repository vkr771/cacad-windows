using SkiaSharp;

namespace Cacad;

/// <summary>A Live Activity: something happening right now that takes over the resting pill (a download, a timer,
/// a call, charging...). The highest-priority running activity is shown; a toast still briefly covers it.</summary>
public sealed class LiveActivity
{
    public required string Id { get; init; }
    public int Priority { get; init; }
    public string Icon { get; set; } = "clock";
    public SKColor Color { get; set; } = SKColors.White;
    public Func<string> Title { get; set; } = () => "";
    public Func<string> Value { get; set; } = () => "";
    /// <summary>0..1 draws a ring round the icon; NaN a spinner; null nothing.</summary>
    public Func<float?>? Progress { get; set; }
    /// <summary>Clicking the pill while this shows (null: open the card as usual).</summary>
    public Action? Click { get; set; }
    /// <summary>Offered on the right-click menu, e.g. "Stop timer".</summary>
    public (string label, Action act)? Stop { get; set; }
    public float Width { get; set; } = 300;
    /// <summary>Seconds between frames this needs (it animates even when nothing else does).</summary>
    public double Interval { get; set; } = 1 / 30.0;
    /// <summary>A custom look instead of the standard row: (canvas, width, height, alpha, time). Pill coordinates:
    /// x = 0 at the centre, y from 0 to the height.</summary>
    public Action<SKCanvas, float, float, float, double>? Draw { get; set; }
    public DateTime Started { get; } = DateTime.Now;
}

public static class ActivityView
{
    /// <summary>The standard look: an icon (ringed by progress, or a spinner), a title, a value on the right.</summary>
    public static void Draw(SKCanvas c, LiveActivity act, float w, float h, float a, double t)
    {
        if (act.Draw != null) { act.Draw(c, w, h, a, t); return; }
        float L = -w / 2, R = w / 2, cy = h / 2, ix = L + 20;
        var color = act.Color;
        float? prog = act.Progress?.Invoke();

        G.Circle(c, ix, cy, 11.5f, color.WithAlpha(G.A(0.18f * a)));
        Icons.Draw(c, act.Icon, ix, cy, 13, color.WithAlpha(G.A(a)));
        if (prog is { } p)
        {
            using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f, StrokeCap = SKStrokeCap.Round };
            var box = SKRect.Create(ix - 13.5f, cy - 13.5f, 27, 27);
            if (float.IsNaN(p))
            {
                // Indeterminate: a short arc chasing round.
                ring.Color = color.WithAlpha(G.A(a));
                using var arc = new SKPath();
                arc.AddArc(box, (float)(t * 360 % 360), 90);
                c.DrawPath(arc, ring);
            }
            else
            {
                ring.Color = G.White(0.14f * a);
                c.DrawOval(box, ring);
                ring.Color = color.WithAlpha(G.A(a));
                using var arc = new SKPath();
                arc.AddArc(box, -90, 360 * Math.Clamp(p, 0, 1));
                c.DrawPath(arc, ring);
            }
        }

        string value = act.Value();
        using var mono = Fonts.Mono(12);
        float vw = G.Measure(mono, value);
        G.Text(c, value, R - 14, cy + 4.3f, mono, G.Paper(0.85f * a), SKTextAlign.Right);
        using var font = Fonts.Med(12.5f);
        float room = R - 14 - vw - 10 - (ix + 20);
        G.Text(c, G.Ellipsize(act.Title(), font, room), ix + 20, cy + 4.5f, font, G.Paper(a));
    }
}
