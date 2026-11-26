using System.Drawing.Drawing2D;

namespace Cacad;

/// <summary>Pick any pixel on any screen: a frozen screenshot fills the desktop, a loupe follows the cursor showing
/// the pixels around it (the centre one boxed) and its colour. Click to pick; Esc or right-click to cancel.</summary>
internal sealed class ColorPicker : Form
{
    private readonly Bitmap _shot;
    private readonly Rectangle _virtual;
    private Point _cursor;
    private string? _picked;

    private const int Grid = 11, Cell = 12, Loupe = Grid * Cell;

    private ColorPicker()
    {
        _virtual = SystemInformation.VirtualScreen;
        _shot = new Bitmap(_virtual.Width, _virtual.Height);
        using (var g = Graphics.FromImage(_shot)) g.CopyFromScreen(_virtual.Location, Point.Empty, _virtual.Size);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtual;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    /// <summary>Show the picker; returns "#RRGGBB", or null when cancelled.</summary>
    public static string? Pick()
    {
        using var f = new ColorPicker();
        f.ShowDialog();
        return f._picked;
    }

    private Color At(Point screen)
    {
        int x = Math.Clamp(screen.X - _virtual.Left, 0, _shot.Width - 1), y = Math.Clamp(screen.Y - _virtual.Top, 0, _shot.Height - 1);
        return _shot.GetPixel(x, y);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _cursor = Cursor.Position;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_shot, 0, 0);

        var c = At(_cursor);
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var local = new Point(_cursor.X - _virtual.Left, _cursor.Y - _virtual.Top);

        // The loupe sits below-right of the cursor, flipping when it would leave the screen.
        int lx = local.X + 24, ly = local.Y + 24;
        if (lx + Loupe + 4 > _virtual.Width) lx = local.X - 24 - Loupe;
        if (ly + Loupe + 40 > _virtual.Height) ly = local.Y - 24 - Loupe - 36;
        var box = new Rectangle(lx, ly, Loupe, Loupe);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            g.FillEllipse(shadow, box.X - 4, box.Y - 2, box.Width + 8, box.Height + 10);
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(box);
            g.SetClip(clip);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var src = new Rectangle(local.X - Grid / 2, local.Y - Grid / 2, Grid, Grid);
            g.DrawImage(_shot, box, src, GraphicsUnit.Pixel);
            using (var grid = new Pen(Color.FromArgb(40, 0, 0, 0)))
                for (int i = 1; i < Grid; i++)
                {
                    g.DrawLine(grid, box.X + i * Cell, box.Y, box.X + i * Cell, box.Bottom);
                    g.DrawLine(grid, box.X, box.Y + i * Cell, box.Right, box.Y + i * Cell);
                }
            g.ResetClip();
        }
        g.PixelOffsetMode = PixelOffsetMode.Default;
        var centre = new Rectangle(box.X + Grid / 2 * Cell, box.Y + Grid / 2 * Cell, Cell, Cell);
        using (var white = new Pen(Color.White, 2)) g.DrawRectangle(white, centre);
        using (var black = new Pen(Color.Black, 1)) g.DrawRectangle(black, Rectangle.Inflate(centre, 2, 2));
        using (var ring = new Pen(Color.FromArgb(220, 255, 255, 255), 3)) g.DrawEllipse(ring, box);

        // The colour and its code under the loupe.
        var label = new Rectangle(box.X + 8, box.Bottom + 8, box.Width - 16, 28);
        using (var bg = new SolidBrush(Color.FromArgb(235, 22, 22, 25)))
        using (var path = Ui.Round(label, 14))
            g.FillPath(bg, path);
        using (var sw = new SolidBrush(c)) g.FillEllipse(sw, label.X + 7, label.Y + 6, 16, 16);
        using (var font = Ui.Font(10f, FontStyle.Bold))
            TextRenderer.DrawText(g, hex, font, new Rectangle(label.X + 28, label.Y, label.Width - 32, label.Height), Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _cursor = Cursor.Position;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var c = At(Cursor.Position);
            _picked = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        // Arrow keys nudge the cursor a pixel at a time for precise picks.
        int dx = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0, dy = e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : 0;
        if (dx != 0 || dy != 0) { Cursor.Position = new Point(Cursor.Position.X + dx, Cursor.Position.Y + dy); _cursor = Cursor.Position; Invalidate(); }
        if (e.KeyCode == Keys.Enter) { var c = At(Cursor.Position); _picked = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; Close(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _shot.Dispose();
        base.Dispose(disposing);
    }
}
