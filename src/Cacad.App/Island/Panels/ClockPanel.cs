using SkiaSharp;

namespace Cacad;

/// <summary>The time, large, in the face the pill uses (see <see cref="ClockFaces"/>). Click or scroll to change
/// the face; the pill follows.</summary>
public sealed class ClockPanel : IslandPanel
{
    public ClockPanel(IIslandHost host) : base(host) { }

    public override string Id => "clock";
    public override string Icon => "clock";
    public override string Title => "Clock";
    public override float Height => 160;
    public override bool Animating => true;
    public override double FrameInterval => Math.Min(_faces.Interval(Style, true), _t - _switchAt < 0.4 ? 0 : 1);

    private readonly ClockFaces _faces = new();
    private double _t, _switchAt = -9;
    private string Style => ClockFaces.Normalize(Host.Settings.PillClockStyle);

    public override void Shown() => _faces.Reset();

    public override void Draw(Frame f)
    {
        _t = f.T;
        // A new face slides up a little as it fades in.
        float sw = G.Clamp01((float)((_t - _switchAt) / 0.35));
        float ease = 1 - (1 - sw) * (1 - sw);
        f.C.Save();
        f.C.Translate(0, (1 - ease) * 10);
        _faces.DrawCard(f.C, Style, SKRect.Create(16, 34, f.W - 32, Height - 56), f.A * ease, f.Theme, Host.Settings.Clock24h, f.T, f.Dt);
        f.C.Restore();
        DrawIndicator(f);
    }

    /// <summary>Which face this is: its name and a row of dots.</summary>
    private void DrawIndicator(Frame f)
    {
        var styles = ClockFaces.Styles;
        int cur = Math.Max(0, Array.FindIndex(styles, s => s.id == Style));
        float x = f.W - 22;
        for (int i = styles.Length - 1; i >= 0; i--)
        {
            float w = i == cur ? 14 : 4;
            x -= w;
            G.Pill(f.C, x, f.H - 13, w, 4, G.Paper((i == cur ? 0.85f : 0.22f) * f.A));
            x -= 4;
        }
        using var mono = Fonts.Mono(9.5f);
        G.Text(f.C, styles[cur].name.ToUpperInvariant(), x - 6, f.H - 8, mono, G.Paper(0.35f * f.A), SKTextAlign.Right);
    }

    private void Cycle(int dir)
    {
        Host.Settings.PillClockStyle = ClockFaces.Next(Style, dir);
        Host.Settings.Save();
        _switchAt = _t;
        _faces.Reset();
        Host.Invalidate();
    }

    public override void Click(SKPoint p, MouseButtons b) => Cycle(b == MouseButtons.Right ? -1 : 1);

    public override bool Wheel(SKPoint p, int delta)
    {
        Cycle(delta < 0 ? 1 : -1);
        return true;
    }
}
