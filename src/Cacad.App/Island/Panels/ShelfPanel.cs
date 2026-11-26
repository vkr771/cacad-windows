using System.Collections.Specialized;
using SkiaSharp;

namespace Cacad;

/// <summary>A place to park files: drop them on the island, drag them back out into any app.</summary>
public sealed class ShelfPanel : IslandPanel
{
    public ShelfPanel(IIslandHost host) : base(host)
    {
        host.Settings.Shelf.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
    }

    public override string Id => "shelf";
    public override string Icon => "folder";
    public override string Title => "Shelf";
    public override float Height => 156;
    public override bool Animating => Math.Abs(_scroll - _scrollTarget) > 0.3f;

    private List<string> Files => Host.Settings.Shelf;
    private const float Pitch = 76, TileTop = 54;
    private float _scroll, _scrollTarget, _maxScroll;
    private readonly List<(SKRect tile, SKRect remove, string path)> _tiles = new();
    private SKRect _copy, _reveal, _clear;
    private string? _downOn;
    private SKPoint _downAt;
    private bool _dragged;

    public override void Draw(Frame f)
    {
        var c = f.C;
        float a = f.A;
        _scroll = G.Ease(_scroll, _scrollTarget, 16, f.Dt);

        using (var t = Fonts.Med(13))
        using (var s = Fonts.Sub(13))
        {
            G.Text(c, "Shelf", 18, 46, t, G.Paper(a));
            float tw = G.Measure(t, "Shelf");
            G.Text(c, Files.Count == 0 ? "" : $" · {Files.Count} {(Files.Count == 1 ? "file" : "files")}", 18 + tw, 46, s, G.Paper(0.55f * a));
        }

        _copy = _reveal = _clear = SKRect.Empty;
        if (Files.Count > 0)
        {
            _clear = IconButton(f, "clear", "trash-2", f.W - 16 - 26);
            _reveal = IconButton(f, "reveal", "folder-open", f.W - 16 - 56);
            _copy = IconButton(f, "copy", "copy", f.W - 16 - 86);
        }

        _tiles.Clear();
        if (Files.Count == 0)
        {
            var box = SKRect.Create(16, 56, f.W - 32, 84);
            using (var dash = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = G.White(0.2f * a), PathEffect = SKPathEffect.CreateDash(new[] { 5f, 4f }, 0) })
                c.DrawRoundRect(box, 14, 14, dash);
            Icons.Draw(c, "upload", box.MidX, box.MidY - 12, 20, G.Paper(0.45f * a));
            using var sub = Fonts.Sub(12.5f);
            G.Text(c, "Drop files on the island to keep them here", box.MidX, box.MidY + 18, sub, G.Paper(0.55f * a), SKTextAlign.Center);
            return;
        }

        _maxScroll = Math.Max(0, Files.Count * Pitch - (f.W - 32) + 4);
        _scrollTarget = Math.Clamp(_scrollTarget, 0, _maxScroll);
        c.Save();
        c.ClipRect(SKRect.Create(12, TileTop - 6, f.W - 24, 100));
        using var name = Fonts.Sub(11);
        for (int i = 0; i < Files.Count; i++)
        {
            float x = 16 + i * Pitch - _scroll;
            if (x > f.W || x + Pitch < 0) continue;
            string path = Files[i];
            var tile = SKRect.Create(x, TileTop, 68, 90);
            float k = Hov(f, "tile" + i, G.In(tile, f.Pointer));
            if (k > 0.003f) G.Fill(c, tile, 12, G.White(0.08f * k * a));
            var img = ShellImages.Get(path, 96);
            var ir = SKRect.Create(x + 10, TileTop + 6, 48, 48);
            if (img != null)
            {
                bool photo = img.Width != img.Height || IsImage(path);
                if (photo) G.ImageCover(c, img, ir, a, 6); else G.Image(c, img, ir, a);
            }
            else Icons.Draw(c, "file", ir.MidX, ir.MidY, 30, G.Paper(0.6f * a));
            G.Text(c, G.EllipsizeMiddle(Path.GetFileName(path.TrimEnd('\\')), name, 64), tile.MidX, TileTop + 72, name, G.Paper(0.8f * a), SKTextAlign.Center);

            var remove = SKRect.Create(tile.Right - 18, tile.Top + 2, 16, 16);
            if (k > 0.05f)
            {
                G.Circle(c, remove.MidX, remove.MidY, 8, new SKColor(60, 60, 66, G.A(a * k)));
                Icons.Draw(c, "x", remove.MidX, remove.MidY, 10, G.Paper(a * k), 2.2f);
            }
            _tiles.Add((tile, remove, path));
        }
        c.Restore();
    }

    private static bool IsImage(string p) => Path.GetExtension(p).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".heic";

    private SKRect IconButton(Frame f, string key, string icon, float x)
    {
        var r = SKRect.Create(x, 30, 26, 26);
        float k = Hov(f, key, G.In(r, f.Pointer));
        HoverSquare(f, r, 8, k);
        Icons.Draw(f.C, icon, r.MidX, r.MidY, 15, G.Paper((0.6f + 0.4f * k) * f.A));
        return r;
    }

    public override bool Drop(string[] files)
    {
        foreach (var file in files)
            if (!Files.Contains(file, StringComparer.OrdinalIgnoreCase)) Files.Add(file);
        Host.Settings.Save();
        _scrollTarget = _maxScroll + Pitch * files.Length;
        Host.Invalidate();
        return true;
    }

    public override bool Down(SKPoint p, MouseButtons b)
    {
        _dragged = false;
        _downOn = null;
        if (b != MouseButtons.Left) return false;
        foreach (var (tile, remove, path) in _tiles)
            if (tile.Contains(p) && !remove.Contains(p)) { _downOn = path; _downAt = p; return true; }
        return false;
    }

    public override void Drag(SKPoint p)
    {
        if (_downOn == null || _dragged) return;
        if (Math.Abs(p.X - _downAt.X) + Math.Abs(p.Y - _downAt.Y) < 5) return;
        _dragged = true;
        string file = _downOn;
        _downOn = null;
        Host.DragFilesOut(new[] { file });
    }

    public override void Up(SKPoint p)
    {
        if (_downOn != null && !_dragged && (File.Exists(_downOn) || Directory.Exists(_downOn))) Shell.Open(_downOn);
        _downOn = null;
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        if (_clear.Contains(p)) { Files.Clear(); Host.Settings.Save(); Host.Invalidate(); return; }
        if (_reveal.Contains(p) && Files.Count > 0) { Shell.Reveal(Files[^1]); return; }
        if (_copy.Contains(p) && Files.Count > 0)
        {
            var sc = new StringCollection();
            sc.AddRange(Files.ToArray());
            try { Clipboard.SetFileDropList(sc); Host.Toast("copy", $"{Files.Count} {(Files.Count == 1 ? "file" : "files")} copied"); } catch { }
            return;
        }
        foreach (var (tile, remove, path) in _tiles)
        {
            if (remove.Contains(p) || (b == MouseButtons.Right && tile.Contains(p)))
            {
                Files.Remove(path);
                Host.Settings.Save();
                Host.Invalidate();
                return;
            }
        }
    }

    public override bool Wheel(SKPoint p, int delta)
    {
        _scrollTarget = Math.Clamp(_scrollTarget - Math.Sign(delta) * Pitch, 0, _maxScroll);
        Host.Invalidate();
        return true;
    }
}
