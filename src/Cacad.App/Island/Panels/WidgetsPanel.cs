using SkiaSharp;

namespace Cacad;

public sealed class WidgetsPanel : IslandPanel
{
    public WidgetsPanel(IIslandHost host) : base(host) { }

    public override string Id => "widgets";
    public override string Icon => "layout-grid";
    public override string Title => "Widgets";
    public override float Height => 192;
    public override bool Animating => true; // clock seconds and live stats; throttled by the island

    private static readonly (string id, string city)[] Zones =
    {
        ("Tokyo Standard Time", "Tokyo"), ("GMT Standard Time", "London"), ("Eastern Standard Time", "New York"),
        ("Pacific Standard Time", "San Francisco"), ("India Standard Time", "Mumbai"), ("AUS Eastern Standard Time", "Sydney"),
        ("W. Europe Standard Time", "Berlin"), ("Singapore Standard Time", "Singapore"), ("Arabian Standard Time", "Dubai"),
    };

    private SKRect _zone, _add;
    private readonly List<(SKRect r, TodoItem item)> _todoRects = new();

    public override void Draw(Frame f)
    {
        Host.Stats.Sample();
        float colW = (f.W - 32 - 8) / 2, l = 16, r = 16 + colW + 8, top = 34, rowH = 72;
        DrawClock(f, SKRect.Create(l, top, colW, rowH));
        DrawTodo(f, SKRect.Create(r, top, colW, rowH));
        DrawStats(f, SKRect.Create(l, top + rowH + 8, colW, rowH));
        DrawBattery(f, SKRect.Create(r, top + rowH + 8, colW, rowH));
    }

    private void DrawClock(Frame f, SKRect r)
    {
        var c = f.C;
        float a = f.A;
        Tile(f, r);
        var now = DateTime.Now;
        bool h24 = Host.Settings.Clock24h;
        using var big = Fonts.Title(28);
        using var ampm = Fonts.Med(12);
        using var small = Fonts.Med(11);
        using var sub = Fonts.Sub(11.5f);
        string time = now.ToString(h24 ? "HH:mm" : "h:mm");
        G.Text(c, time, r.Left + 14, r.Top + 36, big, G.Paper(a));
        if (!h24) G.Text(c, now.ToString("tt"), r.Left + 18 + G.Measure(big, time), r.Top + 36, ampm, G.Paper(0.7f * a));
        G.Text(c, now.ToString("ddd").ToUpperInvariant(), r.Right - 14, r.Top + 22, small, G.Paper(0.7f * a), SKTextAlign.Right);

        // World clock; click to change the city.
        string id = Host.Settings.WorldClocks.FirstOrDefault() ?? Zones[0].id;
        string city = Zones.FirstOrDefault(z => z.id == id).city ?? id;
        DateTime there;
        TimeSpan offset;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            there = TimeZoneInfo.ConvertTime(now, zone);
            // From the zones' own UTC offsets, so it stays exact across daylight saving.
            var utc = DateTime.UtcNow;
            offset = zone.GetUtcOffset(utc) - TimeZoneInfo.Local.GetUtcOffset(utc);
        }
        catch { there = now.ToUniversalTime(); city = "UTC"; offset = -TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow); }
        _zone = SKRect.Create(r.Left + 8, r.Bottom - 26, r.Width - 16, 20);
        float k = Hov(f, "zone", G.In(_zone, f.Pointer));
        HoverSquare(f, _zone, 6, k);
        Icons.Draw(c, "globe", r.Left + 19, r.Bottom - 16, 12, G.Paper(0.6f * a));
        // "Tokyo +3:30": a world clock, labelled with how far it is from you, so it never reads as your own time.
        G.Text(c, city, r.Left + 30, r.Bottom - 12, sub, G.Paper(0.75f * a));
        using (var mono = Fonts.Mono(10))
            G.Text(c, Offset(offset), r.Left + 34 + G.Measure(sub, city), r.Bottom - 12, mono, G.Paper(0.42f * a));
        string thereTime = there.ToString(h24 ? "HH:mm" : "h:mm tt");
        if (there.Date != now.Date) thereTime += " " + there.ToString("ddd");
        G.Text(c, thereTime, r.Right - 14, r.Bottom - 12, sub, G.Paper(0.75f * a), SKTextAlign.Right);
    }

    /// <summary>"+3:30", "-5", or "same" for a zone's distance from local time.</summary>
    private static string Offset(TimeSpan d)
    {
        if (d == TimeSpan.Zero) return "same";
        string sign = d < TimeSpan.Zero ? "-" : "+";
        d = d.Duration();
        return d.Minutes == 0 ? $"{sign}{(int)d.TotalHours}" : $"{sign}{(int)d.TotalHours}:{d.Minutes:00}";
    }

    private void DrawTodo(Frame f, SKRect r)
    {
        var c = f.C;
        float a = f.A;
        Tile(f, r);
        var todos = Host.Settings.Todos;
        int open = todos.Count(t => !t.Done);
        using var head = Fonts.Med(12);
        using var mono = Fonts.Mono(11);
        using var sub = Fonts.Sub(11.5f);
        G.Text(c, "To do", r.Left + 14, r.Top + 20, head, G.Paper(a));
        G.Text(c, open.ToString(), r.Left + 18 + G.Measure(head, "To do"), r.Top + 20, mono, G.Paper(0.5f * a));

        _add = SKRect.Create(r.Right - 30, r.Top + 6, 22, 22);
        float ak = Hov(f, "add", G.In(_add, f.Pointer));
        G.Circle(c, _add.MidX, _add.MidY, 10, G.White((0.08f + 0.1f * ak) * a));
        Icons.Draw(c, "plus", _add.MidX, _add.MidY, 12, G.Paper(a));

        _todoRects.Clear();
        var shown = todos.OrderBy(t => t.Done).Take(3).ToList();
        if (shown.Count == 0)
            G.Text(c, "Nothing to do. Add one with +", r.Left + 14, r.Top + 46, sub, G.Paper(0.45f * a));
        for (int i = 0; i < shown.Count; i++)
        {
            var item = shown[i];
            var row = SKRect.Create(r.Left + 8, r.Top + 27 + i * 15, r.Width - 16, 15);
            float k = Hov(f, "todo" + i, G.In(row, f.Pointer));
            float ia = a * (item.Done ? 0.4f : 1);
            Icons.Draw(c, item.Done ? "circle-check" : "circle", row.Left + 10, row.MidY, 11, item.Done ? f.Theme.Accent.WithAlpha(G.A(ia)) : G.Paper((0.6f + 0.4f * k) * ia));
            string text = G.Ellipsize(item.Text, sub, row.Width - 26);
            G.Text(c, text, row.Left + 21, row.MidY + 4, sub, G.Paper(0.85f * ia));
            if (item.Done)
            {
                using var strike = new SKPaint { Color = G.Paper(0.5f * ia), StrokeWidth = 1 };
                c.DrawLine(row.Left + 21, row.MidY, row.Left + 21 + G.Measure(sub, text), row.MidY, strike);
            }
            _todoRects.Add((row, item));
        }
    }

    private void DrawStats(Frame f, SKRect r)
    {
        var c = f.C;
        float a = f.A;
        Tile(f, r);
        var s = Host.Stats;
        using var head = Fonts.Med(12);
        using var mono = Fonts.Mono(11);
        Icons.Draw(c, "cpu", r.Left + 21, r.Top + 16, 13, G.Paper(0.7f * a));
        G.Text(c, "This PC", r.Left + 32, r.Top + 20, head, G.Paper(a));
        Meter(f, r.Left + 14, r.Top + 38, r.Width - 28, "CPU", s.Cpu, $"{s.Cpu * 100:0}%");
        Meter(f, r.Left + 14, r.Top + 58, r.Width - 28, "RAM", s.Ram, $"{s.RamUsedGb:0.0}/{s.RamTotalGb:0} GB");
    }

    private void Meter(Frame f, float x, float y, float w, string label, float value, string text)
    {
        using var mono = Fonts.Mono(10);
        G.Text(f.C, label, x, y, mono, G.Paper(0.55f * f.A));
        G.Text(f.C, text, x + w, y, mono, G.Paper(0.8f * f.A), SKTextAlign.Right);
        G.Pill(f.C, x, y + 4, w, 4, G.White(0.12f * f.A));
        var col = value > 0.85f ? new SKColor(255, 110, 100) : f.Theme.Accent;
        G.Pill(f.C, x, y + 4, Math.Max(4, w * value), 4, col.WithAlpha(G.A(f.A)));
    }

    private void DrawBattery(Frame f, SKRect r)
    {
        var c = f.C;
        float a = f.A;
        Tile(f, r);
        var (has, level, charging) = SystemStats.Battery();
        using var head = Fonts.Med(12);
        using var big = Fonts.Title(22);
        using var sub = Fonts.Sub(11.5f);
        if (!has)
        {
            Icons.Draw(c, "zap", r.Left + 21, r.Top + 16, 13, G.Paper(0.7f * a));
            G.Text(c, "Power", r.Left + 32, r.Top + 20, head, G.Paper(a));
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            G.Text(c, up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m", r.Left + 14, r.Top + 50, big, G.Paper(a));
            G.Text(c, "uptime · plugged in", r.Left + 14, r.Top + 64, sub, G.Paper(0.5f * a));
            return;
        }
        Icons.Draw(c, charging ? "battery-charging" : "battery", r.Left + 21, r.Top + 16, 14, G.Paper(0.7f * a));
        G.Text(c, "Battery", r.Left + 33, r.Top + 20, head, G.Paper(a));
        G.Text(c, $"{level * 100:0}%", r.Right - 14, r.Top + 50, big, G.Paper(a), SKTextAlign.Right);
        G.Text(c, charging ? "Plugged in" : "On battery", r.Left + 14, r.Top + 50, sub, G.Paper(0.55f * a));
        var col = level < 0.2f && !charging ? new SKColor(255, 110, 100) : new SKColor(80, 220, 120);
        G.Pill(c, r.Left + 14, r.Bottom - 14, r.Width - 28, 5, G.White(0.12f * a));
        G.Pill(c, r.Left + 14, r.Bottom - 14, Math.Max(5, (r.Width - 28) * level), 5, col.WithAlpha(G.A(a)));
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        if (_zone.Contains(p))
        {
            string id = Host.Settings.WorldClocks.FirstOrDefault() ?? Zones[0].id;
            int i = Array.FindIndex(Zones, z => z.id == id);
            Host.Settings.WorldClocks = new List<string> { Zones[(i + 1) % Zones.Length].id };
            Host.Settings.Save();
        }
        else if (_add.Contains(p))
        {
            string? text = Host.Prompt("New to-do", "What needs doing?");
            if (!string.IsNullOrWhiteSpace(text))
            {
                Host.Settings.Todos.Insert(0, new TodoItem { Text = text.Trim() });
                Host.Settings.Save();
            }
        }
        else
        {
            foreach (var (r, item) in _todoRects)
            {
                if (!r.Contains(p)) continue;
                if (b == MouseButtons.Right) Host.Settings.Todos.Remove(item);
                else
                {
                    item.Done = !item.Done;
                    if (item.Done) { IslandWindow.Instance?.Companion?.Pulse("done"); IslandWindow.Instance?.Companion?.Pet.Feed("todo"); }
                }
                Host.Settings.Save();
                break;
            }
        }
        Host.Invalidate();
    }
}
