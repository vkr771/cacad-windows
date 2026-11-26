using SkiaSharp;

namespace Cacad;

/// <summary>The card's notification history: the last messages your apps showed, newest first, so a banner you
/// missed in the pill can be read later. Click one to open Windows' notification centre; scroll for older ones.</summary>
public sealed class NotificationsPanel : IslandPanel
{
    public NotificationsPanel(IIslandHost host) : base(host) { }

    public override string Id => "notifications";
    public override string Icon => "bell";
    public override string Title => "Notifications";
    public override float Height => 202;
    public override bool Visible => Host.Settings.Notifications;

    private const float RowH = 44;
    private float _scroll;
    private SKRect _clear, _list;
    private readonly List<SKRect> _rows = new();

    public override void Draw(Frame f)
    {
        var c = f.C;
        float a = f.A;
        var items = Host.Notifier?.Recent ?? new List<Notifications.Item>();
        using var head = Fonts.Title(13);
        using var small = Fonts.Sub(11);
        G.Text(c, "Notifications", 22, 48, head, G.Paper(a));
        _clear = SKRect.Empty;
        if (items.Count > 0)
        {
            float cw = G.Measure(small, "Clear") + 16;
            _clear = SKRect.Create(f.W - 18 - cw, 34, cw, 20);
            bool over = f.Pointer is { } pp && _clear.Contains(pp);
            G.Fill(c, _clear, 10, G.White((over ? 0.16f : 0.09f) * a));
            G.Text(c, "Clear", _clear.MidX, _clear.MidY + 4, small, G.Paper(0.8f * a), SKTextAlign.Center);
        }

        _list = SKRect.Create(14, 60, f.W - 28, Height - 64);
        _rows.Clear();
        if (items.Count == 0)
        {
            Icons.Draw(c, "bell", f.W / 2, 104, 24, G.Paper(0.3f * a));
            using var sub = Fonts.Sub(12.5f);
            G.Text(c, Host.Settings.NotificationsPrivate ? "Nothing new. Messages show here without their text." : "Nothing new. Messages from your apps show here.",
                f.W / 2, 140, sub, G.Paper(0.5f * a), SKTextAlign.Center);
            return;
        }
        float max = Math.Max(0, items.Count * RowH - _list.Height);
        _scroll = Math.Clamp(_scroll, 0, max);

        c.Save();
        c.ClipRect(_list);
        using var title = Fonts.Med(12.5f);
        using var body = Fonts.Sub(11.5f);
        using var time = Fonts.Sub(10.5f);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var r = SKRect.Create(_list.Left, _list.Top + i * RowH - _scroll, _list.Width, RowH - 4);
            _rows.Add(r);
            if (r.Bottom < _list.Top || r.Top > _list.Bottom) continue;
            bool over = f.Pointer is { } p && r.Contains(p) && _list.Contains(p);
            G.Fill(c, r, 10, G.White((over ? 0.10f : 0.05f) * a));
            var icon = SKRect.Create(r.Left + 8, r.MidY - 12, 24, 24);
            if (it.Icon != null) G.Image(c, it.Icon, icon, a, 6);
            else { G.Fill(c, icon, 6, G.White(0.12f * a)); Icons.Draw(c, "bell", icon.MidX, icon.MidY, 12, G.Paper(0.7f * a)); }
            float x = icon.Right + 10, room = r.Right - 10 - x;
            string when = Ago(it.At);
            float ww = G.Measure(time, when);
            string text = Host.Settings.NotificationsPrivate ? "New message" : it.Body;
            G.Text(c, G.Ellipsize(it.Title == it.App || text.Length == 0 ? it.Title : $"{it.Title}", title, room - ww - 8), x, r.MidY - (text.Length > 0 ? 2 : -4), title, G.Paper(a));
            G.Text(c, when, r.Right - 10, r.MidY - 2, time, G.Paper(0.45f * a), SKTextAlign.Right);
            if (text.Length > 0)
                G.Text(c, G.Ellipsize($"{it.App} · {text}", body, room), x, r.MidY + 12, body, G.Paper(0.58f * a));
        }
        c.Restore();
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h";
        return t.ToString("d MMM");
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        if (_clear.Contains(p)) { Host.Notifier?.Recent.Clear(); _scroll = 0; Host.Invalidate(); return; }
        if (!_list.Contains(p)) return;
        if (_rows.Any(r => r.Contains(p)))
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-actioncenter:") { UseShellExecute = true }); } catch { }
    }

    public override bool Wheel(SKPoint p, int delta)
    {
        if (!_list.Contains(p)) return false;
        _scroll -= delta / 120f * RowH;
        Host.Invalidate();
        return true;
    }
}
