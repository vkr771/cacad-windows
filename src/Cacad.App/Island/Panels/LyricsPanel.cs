using SkiaSharp;

namespace Cacad;

/// <summary>The card's Lyrics tab: the song's synced lyrics as an Apple Music–style Lyrics View, over the cover's
/// colours. Click a line to jump the song there; scroll to look ahead or back (it returns to the sung line).</summary>
public sealed class LyricsPanel : IslandPanel
{
    public LyricsPanel(IIslandHost host) : base(host) { }

    public override string Id => "lyrics";
    public override string Icon => "mic-vocal";
    public override string Title => "Lyrics";
    public override float Height => 202;
    /// <summary>The tab shows only while a song with lyrics plays, with a few seconds' grace so a song change (the
    /// next lyrics loading) or a moment's pause does not make it blink away.</summary>
    public override bool Visible
    {
        get
        {
            bool now = Host.Settings.Lyrics && Host.Media.IsPlaying && Host.Lyrics.Current != null;
            if (now) _seen = DateTime.Now;
            return now || (Host.Settings.Lyrics && (DateTime.Now - _seen).TotalSeconds < 4);
        }
    }
    private DateTime _seen = DateTime.MinValue;
    public override double FrameInterval => 0;
    public override bool Animating => (Host.Media.IsPlaying && Host.Lyrics.Current != null) || _view.Moving;

    private readonly LyricsView _view = new(LyricsView.Card);
    private SKRect _box;
    private double _t;

    public override void DrawBackground(Frame f)
    {
        var art = Host.Media.Art;
        if (art == null || !Host.Media.HasSession) return;
        var c = f.C;
        float h = f.H;
        // The cover's colours, softly, with the text side kept dark enough to read.
        using (var p = new SKPaint { Color = SKColors.White.WithAlpha(G.A(0.55f * f.A)) })
            c.DrawImage(art.Glow, SKRect.Create(-60, -60, f.W + 120, h + 120), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), p);
        using var shade = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(f.W, 0),
            new[] { G.Black(0.55f * f.A), G.Black(0.35f * f.A) }, null, SKShaderTileMode.Clamp);
        using var sp = new SKPaint { Shader = shade };
        c.DrawRect(SKRect.Create(-12, -6, f.W + 24, h + 12), sp);
    }

    public override void Draw(Frame f)
    {
        var c = f.C;
        float a = f.A;
        _t = f.T;
        var m = Host.Media;
        _box = SKRect.Create(24, 34, f.W - 48, Height - 40);
        var song = Host.Lyrics.Current;
        if (!m.HasSession || song == null)
        {
            using var title = Fonts.Title(16);
            using var sub = Fonts.Sub(13);
            Icons.Draw(c, "mic-vocal", f.W / 2, 82, 26, G.Paper(0.35f * a));
            string head = !m.HasSession ? "Nothing playing" : !Host.Settings.Lyrics ? "Lyrics are off" : "No lyrics for this song";
            string more = !m.HasSession ? "Play a song and its lyrics show here."
                : !Host.Settings.Lyrics ? "Turn them on in Settings › Live."
                : "Lyrics come from lrclib.net, and this one isn't there yet.";
            G.Text(c, head, f.W / 2, 122, title, G.Paper(a), SKTextAlign.Center);
            G.Text(c, more, f.W / 2, 142, sub, G.Paper(0.55f * a), SKTextAlign.Center);
            return;
        }
        _view.Draw(c, song, _box, Host.LyricPos, f.T, a);
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        // Tap a line to sing from there.
        if (b != MouseButtons.Left || !Host.Media.CanSeek) return;
        if (_view.LineAt(p) is { } line) { Host.Media.Seek(Math.Max(0, line.At - 0.05)); Host.Invalidate(); }
    }

    public override bool Wheel(SKPoint p, int delta)
    {
        if (Host.Lyrics.Current == null) return false;
        _view.Scroll(delta / 120f * 34, _t);
        Host.Invalidate();
        return true;
    }
}
