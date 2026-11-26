using SkiaSharp;

namespace Cacad;

/// <summary>Everything a panel needs for one frame. Coordinates are card DIPs: (0,0) is the card's top-left,
/// the tab strip takes y 0..28, content sits below it.</summary>
public sealed class Frame
{
    public SKCanvas C = null!;
    public float A;          // content alpha (fades with opening and tab changes)
    public float W;          // card width
    public float H;          // current card height (spring)
    public SKPoint? Pointer; // null when the pointer is outside the island
    public bool Pressed;     // a button is down
    public float Dt;         // seconds since the last frame
    public double T;         // seconds since start
    public Theme Theme = Themes.All[0];
}

/// <summary>What the island offers its panels.</summary>
public interface IIslandHost
{
    SettingsStore Settings { get; }
    MediaService Media { get; }
    AudioService Audio { get; }
    ClipboardService Clipboard { get; }
    SystemStats Stats { get; }
    BrightnessService Brightness { get; }
    RadioService Radios { get; }
    LyricsService Lyrics { get; }
    Notifications? Notifier { get; }
    WeatherService Weather { get; }
    /// <summary>Where the song is, in seconds, for lyrics (the player's position, or the island's own clock).</summary>
    double LyricPos { get; }
    Rectangle MonitorBounds { get; }

    /// <summary>Ask for another frame.</summary>
    void Invalidate();
    /// <summary>The panel's height changed; let the card's spring follow it.</summary>
    void Relayout();
    /// <summary>Show a short message in the pill.</summary>
    void Toast(string icon, string text, SKColor? color = null, Action? action = null, int ms = 0);
    /// <summary>Start (or replace, by id) a Live Activity in the pill.</summary>
    void StartActivity(LiveActivity activity);
    void EndActivity(string id);
    LiveActivity? FindActivity(string id);
    /// <summary>Close the card back to the pill.</summary>
    void Collapse();
    /// <summary>Keep the card open while a dialog or drag is in progress.</summary>
    void Hold(bool hold);
    /// <summary>Start an OLE drag of files out of the island.</summary>
    void DragFilesOut(IReadOnlyList<string> files);
    /// <summary>Ask the user for a line of text (to-dos, reminders). Null when cancelled.</summary>
    string? Prompt(string title, string placeholder);
}

public abstract class IslandPanel
{
    protected readonly IIslandHost Host;
    private readonly Dictionary<string, float> _hover = new();

    protected IslandPanel(IIslandHost host) => Host = host;

    public abstract string Id { get; }
    public abstract string Icon { get; }
    public abstract string Title { get; }

    /// <summary>The card's height while this panel shows.</summary>
    public virtual float Height => 150;
    /// <summary>Whether its tab shows right now.</summary>
    public virtual bool Visible => true;
    /// <summary>True while the panel wants frames on its own (playing music, ticking timers).</summary>
    public virtual bool Animating => false;
    /// <summary>Seconds between frames while <see cref="Animating"/> and nothing else moves (0 = every frame).</summary>
    public virtual double FrameInterval => 0.25;

    /// <summary>Drawn under the tab strip, e.g. the album colours behind the music card.</summary>
    public virtual void DrawBackground(Frame f) { }
    public abstract void Draw(Frame f);

    /// <summary>Pointer pressed; return true to capture it for a drag.</summary>
    public virtual bool Down(SKPoint p, MouseButtons button) => false;
    public virtual void Drag(SKPoint p) { }
    public virtual void Up(SKPoint p) { }
    public virtual void Click(SKPoint p, MouseButtons button) { }
    public virtual bool Wheel(SKPoint p, int delta) => false;
    public virtual void Shown() { }
    public virtual void Hidden() { }
    /// <summary>Files dropped on the island while this panel shows (default: ignored).</summary>
    public virtual bool Drop(string[] files) => false;

    /// <summary>Eased hover amount (0..1) for a keyed control.</summary>
    protected float Hov(Frame f, string key, bool on)
    {
        _hover.TryGetValue(key, out float v);
        float t = on ? 1 : 0;
        v = G.Ease(v, t, 14, f.Dt);
        if (Math.Abs(v - t) < 0.004f) v = t; else Host.Invalidate();
        _hover[key] = v;
        return v;
    }

    /// <summary>The soft square behind a hovered button.</summary>
    protected static void HoverSquare(Frame f, SKRect r, float radius, float k)
    {
        if (k > 0.003f) G.Fill(f.C, r, radius, G.White(0.14f * k * f.A));
    }

    /// <summary>A rounded tile, the panels' common surface.</summary>
    protected static void Tile(Frame f, SKRect r, float k = 0, float radius = 14)
        => G.Fill(f.C, r, radius, G.White((0.07f + 0.05f * k) * f.A));

    /// <summary>A small text button (chip). Returns its rect.</summary>
    protected SKRect Chip(Frame f, string key, string label, float x, float y, bool on, string? icon = null, float h = 22)
    {
        using var font = Fonts.Sub(11.5f);
        float tw = G.Measure(font, label);
        float w = tw + (icon != null ? 36 : 20);
        var r = SKRect.Create(x, y, w, h);
        float k = Hov(f, key, G.In(r, f.Pointer));
        G.Fill(f.C, r, h / 2, on ? G.Paper(0.92f * f.A) : G.White((0.08f + 0.08f * k) * f.A));
        var col = on ? G.Ink.WithAlpha(G.A(f.A)) : G.Paper((0.75f + 0.25f * k) * f.A);
        float tx = x + 10;
        if (icon != null) { Icons.Draw(f.C, icon, x + 16, y + h / 2, 13, col); tx = x + 26; }
        G.Text(f.C, label, tx, y + h / 2 + 4f, font, col);
        return r;
    }
}
