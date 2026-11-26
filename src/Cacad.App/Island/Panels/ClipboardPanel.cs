using SkiaSharp;

namespace Cacad;

public sealed class ClipboardPanel : IslandPanel
{
    public ClipboardPanel(IIslandHost host) : base(host)
    {
        host.Clipboard.Changed += () => { _scrollTarget = 0; Host.Invalidate(); };
    }

    public override string Id => "clipboard";
    public override string Icon => "clipboard";
    public override string Title => "Clipboard";
    public override float Height => 196;
    public override bool Animating => Math.Abs(_scroll - _scrollTarget) > 0.3f;

    private static readonly (string id, string label)[] Filters =
        { ("all", "All"), ("text", "Text"), ("links", "Links"), ("images", "Images"), ("files", "Files"), ("pinned", "Pinned") };

    private string _filter = "all";
    private float _scroll, _scrollTarget;
    private const float CardW = 118, Gap = 8, Top = 58, CardH = 124;

    private readonly List<(SKRect r, string id)> _chips = new();
    private readonly List<(SKRect card, SKRect pin, SKRect del, ClipItem item)> _cards = new();
    private SKRect _clear, _left, _right;
    private float _maxScroll;

    private IEnumerable<ClipItem> Visible => Host.Clipboard.Items.Where(i => _filter switch
    {
        "text" => i.Kind == ClipKind.Text,
        "links" => i.Kind == ClipKind.Link,
        "images" => i.Kind == ClipKind.Image,
        "files" => i.Kind == ClipKind.Files,
        "pinned" => i.Pinned,
        _ => true,
    }).OrderByDescending(i => i.Pinned);

    public override void Draw(Frame f)
    {
        var c = f.C;
        float a = f.A;
        _scroll = G.Ease(_scroll, _scrollTarget, 16, f.Dt);

        _chips.Clear();
        float x = 16;
        foreach (var (id, label) in Filters)
        {
            var r = Chip(f, "chip-" + id, label, x, 33, _filter == id);
            _chips.Add((r, id));
            x = r.Right + 5;
        }
        using (var sub = Fonts.Sub(11.5f))
        {
            float cw = G.Measure(sub, "Clear");
            _clear = SKRect.Create(f.W - 16 - cw - 8, 33, cw + 8, 22);
            float k = Hov(f, "clear", G.In(_clear, f.Pointer));
            G.Text(c, "Clear", f.W - 20, 48, sub, G.Paper((0.5f + 0.5f * k) * a), SKTextAlign.Right);
        }

        var items = Visible.ToList();
        _cards.Clear();
        float viewW = f.W - 32;
        _maxScroll = Math.Max(0, items.Count * (CardW + Gap) - Gap - viewW);
        _scrollTarget = Math.Clamp(_scrollTarget, 0, _maxScroll);

        if (items.Count == 0)
        {
            var box = SKRect.Create(16, Top, viewW, CardH);
            G.Stroke(c, box, 14, G.White(0.12f * a));
            Icons.Draw(c, "clipboard", box.MidX, box.MidY - 12, 22, G.Paper(0.35f * a));
            using var sub = Fonts.Sub(12.5f);
            G.Text(c, _filter == "all" ? "Copy something and it shows up here" : "Nothing here yet", box.MidX, box.MidY + 18, sub, G.Paper(0.5f * a), SKTextAlign.Center);
            return;
        }

        c.Save();
        c.ClipRect(SKRect.Create(16, Top - 4, viewW, CardH + 8));
        for (int i = 0; i < items.Count; i++)
        {
            float cx = 16 + i * (CardW + Gap) - _scroll;
            if (cx > f.W || cx + CardW < 0) continue;
            DrawCard(f, items[i], SKRect.Create(cx, Top, CardW, CardH), i);
        }
        c.Restore();

        // Edge arrows when there is more to see.
        _left = _right = SKRect.Empty;
        if (_scroll > 1) _left = Arrow(f, "left", 10, "chevron-left");
        if (_scroll < _maxScroll - 1) _right = Arrow(f, "right", f.W - 34, "chevron-right");
    }

    private SKRect Arrow(Frame f, string key, float x, string icon)
    {
        var r = SKRect.Create(x, Top + CardH / 2 - 12, 24, 24);
        float k = Hov(f, "arrow-" + key, G.In(r, f.Pointer));
        G.Circle(f.C, r.MidX, r.MidY, 12, new SKColor(40, 40, 44, G.A(f.A)));
        G.Circle(f.C, r.MidX, r.MidY, 12, G.White(0.08f * k * f.A));
        Icons.Draw(f.C, icon, r.MidX, r.MidY, 14, G.Paper(f.A));
        return r;
    }

    private void DrawCard(Frame f, ClipItem item, SKRect r, int index)
    {
        var c = f.C;
        float a = f.A;
        bool over = G.In(r, f.Pointer);
        float k = Hov(f, "card" + index, over);
        Tile(f, r, k);

        var (icon, label, color) = item.Kind switch
        {
            ClipKind.Files => ("folder", item.Files.Count > 1 ? "Files" : "File", new SKColor(255, 196, 92)),
            ClipKind.Image => ("image", "Image", new SKColor(120, 220, 140)),
            ClipKind.Link => ("link", "Link", new SKColor(110, 170, 255)),
            _ => ("type", "Text", G.PaperBase),
        };
        using var small = Fonts.Med(11);
        using var body = Fonts.Sub(12);
        using var mono = Fonts.Mono(10);

        // Header: kind, and either the age or (on hover) pin / delete.
        Icons.Draw(c, icon, r.Left + 16, r.Top + 15, 12, color.WithAlpha(G.A(a)));
        G.Text(c, label, r.Left + 26, r.Top + 19, small, color.WithAlpha(G.A(a)));
        var pin = SKRect.Create(r.Right - 44, r.Top + 5, 20, 20);
        var del = SKRect.Create(r.Right - 23, r.Top + 5, 20, 20);
        if (k > 0.05f)
        {
            float pk = Hov(f, "pin" + index, G.In(pin, f.Pointer)), dk = Hov(f, "del" + index, G.In(del, f.Pointer));
            HoverSquare(f, pin, 6, pk);
            HoverSquare(f, del, 6, dk);
            Icons.Draw(c, "pin", pin.MidX, pin.MidY, 12, item.Pinned ? f.Theme.Accent.WithAlpha(G.A(a * k)) : G.Paper(0.7f * a * k));
            Icons.Draw(c, "x", del.MidX, del.MidY, 12, G.Paper(0.7f * a * k));
        }
        if (k < 0.95f)
        {
            float ta = a * (1 - k);
            if (item.Pinned) Icons.Draw(c, "pin", r.Right - 14, r.Top + 15, 11, f.Theme.Accent.WithAlpha(G.A(ta)));
            else G.Text(c, G.Ago(item.At), r.Right - 10, r.Top + 19, mono, G.Paper(0.45f * ta), SKTextAlign.Right);
        }

        // Body.
        var content = SKRect.Create(r.Left + 10, r.Top + 28, r.Width - 20, r.Height - 50);
        switch (item.Kind)
        {
            case ClipKind.Image when item.Image != null:
                G.ImageCover(c, item.Image, content, a, 8);
                break;
            case ClipKind.Files:
            {
                int shown = Math.Min(3, item.Files.Count);
                for (int i = shown - 1; i >= 0; i--)
                {
                    var img = ShellImages.Get(item.Files[i], 64);
                    var ir = SKRect.Create(content.Left + i * 10, content.Top + 2 + i * 2, 36, 36);
                    if (img != null) G.Image(c, img, ir, a);
                    else Icons.Draw(c, "file", ir.MidX, ir.MidY, 24, G.Paper(0.6f * a));
                }
                using var t = Fonts.Med(12);
                G.Text(c, item.Files.Count > 1 ? $"{item.Files.Count} files" : "1 file", content.Left, content.Top + 56, t, G.Paper(a));
                G.Text(c, G.EllipsizeMiddle(Path.GetFileName(item.Files[0]), body, content.Width), content.Left, content.Top + 71, body, G.Paper(0.55f * a));
                break;
            }
            case ClipKind.Link:
            {
                string host = Uri.TryCreate(item.Text.Trim(), UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : item.Text;
                using var t = Fonts.Med(12.5f);
                G.Text(c, G.Ellipsize(host, t, content.Width), content.Left, content.Top + 14, t, G.Paper(a));
                Wrap(c, item.Text.Trim(), content.Left, content.Top + 32, content.Width, 3, body, G.Paper(0.5f * a));
                break;
            }
            default:
                Wrap(c, item.Text.Trim(), content.Left, content.Top + 13, content.Width, 5, body, G.Paper(0.88f * a));
                break;
        }

        // Footer: where it was copied from.
        using var foot = Fonts.Sub(10.5f);
        string src = item.Kind == ClipKind.Image ? $"{item.ImageW} × {item.ImageH}" : item.Source;
        G.Text(c, G.Ellipsize(src, foot, r.Width - 20), r.Left + 10, r.Bottom - 10, foot, G.Paper(0.45f * a));

        _cards.Add((r, pin, del, item));
    }

    /// <summary>Word-wrap into at most <paramref name="maxLines"/> lines; the last one is ellipsized when text remains.</summary>
    private static void Wrap(SKCanvas c, string text, float x, float y, float width, int maxLines, SKFont font, SKColor color)
    {
        var lines = new List<string>();
        foreach (var para in text.Replace("\r", "").Replace("\t", " ").Split('\n'))
        {
            string line = "";
            foreach (var w in para.Split(' '))
            {
                string attempt = line.Length == 0 ? w : line + " " + w;
                if (line.Length > 0 && G.Measure(font, attempt) > width) { lines.Add(line); line = w; }
                else line = attempt;
            }
            lines.Add(line);
            if (lines.Count > maxLines) break;
        }
        for (int i = 0; i < Math.Min(maxLines, lines.Count); i++)
        {
            string l = lines[i];
            if (i == maxLines - 1 && lines.Count > maxLines) l += " …";
            G.Text(c, G.Ellipsize(l, font, width), x, y + i * 15, font, color);
        }
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        foreach (var (r, id) in _chips)
            if (r.Contains(p)) { _filter = id; _scrollTarget = _scroll = 0; Host.Invalidate(); return; }
        if (_clear.Contains(p)) { Host.Clipboard.Clear(); return; }
        if (_left.Contains(p)) { _scrollTarget -= (CardW + Gap) * 3; Host.Invalidate(); return; }
        if (_right.Contains(p)) { _scrollTarget += (CardW + Gap) * 3; Host.Invalidate(); return; }
        foreach (var (card, pin, del, item) in _cards)
        {
            if (!card.Contains(p)) continue;
            if (pin.Contains(p)) Host.Clipboard.TogglePin(item);
            else if (del.Contains(p)) Host.Clipboard.Remove(item);
            else if (b == MouseButtons.Right) ShowActions(item);
            else
            {
                Host.Clipboard.Copy(item);
                Host.Toast("check", "Copied to clipboard");
            }
            return;
        }
    }

    /// <summary>Right-click a card: pin, delete, or copy the text transformed.</summary>
    private void ShowActions(ClipItem item)
    {
        Host.Hold(true);
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, BackColor = Color.FromArgb(28, 28, 31), ForeColor = Color.FromArgb(239, 237, 232) };
        if (Fonts.UiFont(9.5f) is { } font) menu.Font = font;
        menu.Items.Add(item.Pinned ? "Unpin" : "Pin", null, (_, _) => Host.Clipboard.TogglePin(item));

        if (item.Kind is ClipKind.Text or ClipKind.Link)
        {
            menu.Items.Add(new ToolStripSeparator());
            foreach (var (label, transform) in Transforms(item.Text))
                menu.Items.Add(label, null, (_, _) =>
                {
                    try
                    {
                        Clipboard.SetText(transform());
                        Host.Toast("check", label.Replace("Copy ", "Copied ").Replace("Copy", "Copied"));
                    }
                    catch { }
                });
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete", null, (_, _) => Host.Clipboard.Remove(item));
        menu.Closed += (_, _) => { Host.Hold(false); menu.BeginInvoke(menu.Dispose); };
        menu.Show(Cursor.Position);
    }

    /// <summary>The ways a text can be copied again, offered only when they would change it.</summary>
    private static IEnumerable<(string label, Func<string> transform)> Transforms(string text)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        string upper = text.ToUpper(culture), lower = text.ToLower(culture), title = culture.TextInfo.ToTitleCase(lower);
        if (upper != text) yield return ("Copy as UPPERCASE", () => upper);
        if (lower != text) yield return ("Copy as lowercase", () => lower);
        if (title != text) yield return ("Copy as Title Case", () => title);
        string tidy = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"[ \t]+", " ");
        if (tidy != text) yield return ("Copy trimmed", () => tidy);
        if (text.Contains('\n'))
        {
            string one = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\r?\n\s*", " ").Trim();
            yield return ("Copy as one line", () => one);
        }
        string? json = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            json = System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { }
        if (json != null && json != text) yield return ("Copy as formatted JSON", () => json);
        if (text.Contains('%'))
        {
            string decoded;
            try { decoded = Uri.UnescapeDataString(text); } catch { decoded = text; }
            if (decoded != text) yield return ("Copy URL-decoded", () => decoded);
        }
        if (text.IndexOfAny(new[] { ' ', '&', '?', '#', '/' }) >= 0 && !text.Contains('%'))
            yield return ("Copy URL-encoded", () => Uri.EscapeDataString(text));
    }

    public override bool Wheel(SKPoint p, int delta)
    {
        _scrollTarget = Math.Clamp(_scrollTarget - Math.Sign(delta) * (CardW + Gap), 0, _maxScroll);
        Host.Invalidate();
        return true;
    }
}
