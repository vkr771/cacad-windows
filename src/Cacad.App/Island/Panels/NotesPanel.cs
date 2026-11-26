using SkiaSharp;

namespace Cacad;

/// <summary>A scratchpad in the island. Plain lines, "# headings" and "- [ ] checkboxes" (click to tick).
/// Saved as Markdown in the data folder; the pencil opens an editor for free typing.</summary>
public sealed class NotesPanel : IslandPanel
{
    public NotesPanel(IIslandHost host) : base(host) => Load();

    public override string Id => "notes";
    public override string Icon => "square-pen";
    public override string Title => "Notes";
    public override float Height => 196;
    public override bool Animating => Math.Abs(_scroll - _scrollTarget) > 0.3f;

    private static string FilePath => Path.Combine(SettingsStore.DataDir, "notes.md");
    private List<string> _lines = new();
    private float _scroll, _scrollTarget, _maxScroll;
    private SKRect _edit, _add;
    private readonly List<(SKRect r, int line)> _checks = new();
    private const float LineH = 21, Top = 40;

    private void Load()
    {
        try { _lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath).ToList() : new(); } catch { _lines = new(); }
        if (_lines.Count == 0)
            _lines = new() { "# Notes", "- [ ] Click a box to tick it", "- [ ] The pencil opens the editor", "Anything else is just a line of text." };
    }

    private void Save()
    {
        try { File.WriteAllLines(FilePath, _lines); } catch { }
    }

    public override void Shown() => Load();

    public override void Draw(Frame f)
    {
        var c = f.C;
        float a = f.A;
        _scroll = G.Ease(_scroll, _scrollTarget, 16, f.Dt);

        _add = IconButton(f, "add", "plus", f.W - 16 - 26);
        _edit = IconButton(f, "edit", "square-pen", f.W - 16 - 56);
        int open = _lines.Count(l => l.TrimStart().StartsWith("- [ ]"));
        using (var mono = Fonts.Mono(10.5f))
            G.Text(c, open == 0 ? "" : $"{open} to do", f.W - 16 - 66, 47, mono, G.Paper(0.45f * a), SKTextAlign.Right);

        var view = SKRect.Create(14, Top - 6, f.W - 28, f.H - Top - 4);
        _maxScroll = Math.Max(0, _lines.Count * LineH - view.Height + 8);
        _scrollTarget = Math.Clamp(_scrollTarget, 0, _maxScroll);
        _checks.Clear();

        c.Save();
        c.ClipRect(view);
        using var body = Fonts.Sub(13);
        using var head = Fonts.Title(15);
        for (int i = 0; i < _lines.Count; i++)
        {
            float y = Top + 14 + i * LineH - _scroll;
            if (y < view.Top - LineH || y > view.Bottom + LineH) continue;
            string line = _lines[i];
            string t = line.TrimStart();
            float x = 18;
            if (t.StartsWith("# "))
            {
                G.Text(c, G.Ellipsize(t[2..], head, view.Width - 80), x, y + 1, head, G.Paper(a));
                continue;
            }
            bool box = t.StartsWith("- [ ]") || t.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase);
            if (box)
            {
                bool done = !t.StartsWith("- [ ]");
                string text = t.Length > 5 ? t[5..].TrimStart() : "";
                var row = SKRect.Create(view.Left, y - 14, view.Width, LineH);
                float k = Hov(f, "line" + i, G.In(row, f.Pointer));
                if (k > 0.003f) G.Fill(c, row, 8, G.White(0.05f * k * a));
                float ia = done ? 0.45f : 1;
                Icons.Draw(c, done ? "circle-check" : "circle", x + 7, y - 4, 14, done ? f.Theme.Accent.WithAlpha(G.A(a)) : G.Paper((0.6f + 0.4f * k) * a));
                string shown = G.Ellipsize(text, body, view.Width - 40);
                G.Text(c, shown, x + 22, y, body, G.Paper(0.9f * ia * a));
                if (done)
                {
                    using var strike = new SKPaint { Color = G.Paper(0.45f * a), StrokeWidth = 1 };
                    c.DrawLine(x + 22, y - 4.5f, x + 22 + G.Measure(body, shown), y - 4.5f, strike);
                }
                _checks.Add((row, i));
            }
            else if (t.StartsWith("- "))
            {
                G.Circle(c, x + 7, y - 4.5f, 2.2f, G.Paper(0.6f * a));
                G.Text(c, G.Ellipsize(t[2..], body, view.Width - 40), x + 22, y, body, G.Paper(0.85f * a));
            }
            else G.Text(c, G.Ellipsize(line, body, view.Width - 10), x, y, body, G.Paper(0.8f * a));
        }
        c.Restore();
    }

    private SKRect IconButton(Frame f, string key, string icon, float x)
    {
        var r = SKRect.Create(x, 30, 26, 26);
        float k = Hov(f, key, G.In(r, f.Pointer));
        HoverSquare(f, r, 8, k);
        Icons.Draw(f.C, icon, r.MidX, r.MidY, 15, G.Paper((0.6f + 0.4f * k) * f.A));
        return r;
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        if (_edit.Contains(p))
        {
            Host.Hold(true);
            try
            {
                var text = NoteEditor.Edit(string.Join(Environment.NewLine, _lines));
                if (text != null) { _lines = text.Replace("\r", "").Split('\n').ToList(); Save(); }
            }
            finally { Host.Hold(false); }
        }
        else if (_add.Contains(p))
        {
            string? item = Host.Prompt("New item", "Something to remember");
            if (!string.IsNullOrWhiteSpace(item))
            {
                _lines.Add("- [ ] " + item.Trim());
                Save();
                _scrollTarget = float.MaxValue;
            }
        }
        else
        {
            foreach (var (r, line) in _checks)
            {
                if (!r.Contains(p)) continue;
                string s = _lines[line];
                int at = s.IndexOf("- [", StringComparison.Ordinal);
                bool done = s.Length > at + 3 && s[at + 3] != ' ';
                _lines[line] = s[..(at + 3)] + (done ? " " : "x") + s[(at + 4)..];
                if (!done) { IslandWindow.Instance?.Companion?.Pulse("done"); IslandWindow.Instance?.Companion?.Pet.Feed("todo"); }
                Save();
                break;
            }
        }
        Host.Invalidate();
    }

    public override bool Wheel(SKPoint p, int delta)
    {
        _scrollTarget = Math.Clamp(_scrollTarget - Math.Sign(delta) * LineH * 2, 0, _maxScroll);
        Host.Invalidate();
        return true;
    }
}
