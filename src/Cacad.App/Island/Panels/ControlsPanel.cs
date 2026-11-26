using SkiaSharp;

namespace Cacad;

public sealed class ControlsPanel : IslandPanel
{
    public ControlsPanel(IIslandHost host) : base(host) { }

    public override string Id => "controls";
    public override string Icon => "sliders-horizontal";
    public override string Title => "Controls";
    public override float Height => 204;
    public override bool Animating => true; // reflects system state; throttled by the island

    private SKRect _bright, _vol;
    private string? _drag;
    private readonly List<(SKRect r, string id)> _tiles = new();
    private readonly SKRect[] _modes = new SKRect[3];
    private float _volume = -1;
    private bool _micMuted, _muted;
    private int _power = -1;
    private DateTime _polled;

    public override void Shown()
    {
        Host.Brightness.Refresh();
        Poll(force: true);
    }

    private void Poll(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _polled).TotalMilliseconds < 700) return;
        _polled = DateTime.UtcNow;
        if (_drag != "vol") _volume = Host.Audio.Volume;
        _muted = Host.Audio.Muted;
        _micMuted = Host.Audio.MicMuted;
        _power = PowerMode.Get();
    }

    public override void Draw(Frame f)
    {
        Poll();
        var c = f.C;
        float a = f.A;
        _bright = SKRect.Create(16, 34, 54, 128);
        _vol = SKRect.Create(76, 34, 54, 128);
        var br = Host.Brightness;
        Slider(f, "bright", _bright, br.Supported && br.Level >= 0 ? br.Level / 100f : -1, "sun");
        Slider(f, "vol", _vol, _muted ? 0 : _volume, _muted || _volume <= 0.001f ? "volume-x" : _volume > 0.5f ? "volume-2" : "volume1");

        // Quick toggles, three by two.
        _tiles.Clear();
        float gx = 140, gw = f.W - 16 - gx, tw = (gw - 12) / 3, th = 61;
        var (hasBat, level, charging) = SystemStats.Battery();
        var radios = Host.Radios;
        var defs = new (string id, string icon, string name, string state, bool on)[]
        {
            ("wifi", "wifi", "Wi-Fi", radios.WifiName, radios.WifiOn),
            ("bt", "bluetooth", "Bluetooth", radios.HasBluetooth ? (radios.BluetoothOn ? "On" : "Off") : "Settings", radios.BluetoothOn),
            ("session", "leaf", "Focus session", FocusState(), Host.FindActivity("focus") != null),
            ("focus", "bell-off", "Do not disturb", "Settings", false),
            ("mic", _micMuted ? "mic-off" : "mic", "Mic", _micMuted ? "Muted" : "On", _micMuted),
            ("battery", charging || !hasBat ? "battery-charging" : "battery", hasBat ? $"{level * 100:0}%" : "Power", hasBat ? (charging ? "Plugged in" : "On battery") : "Plugged in", false),
        };
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            var r = SKRect.Create(gx + (i % 3) * (tw + 6), 34 + (i / 3) * (th + 6), tw, th);
            float k = Hov(f, "tile-" + d.id, G.In(r, f.Pointer));
            var onColor = d.id == "mic" ? new SKColor(255, 95, 87) : d.id == "session" ? new SKColor(52, 199, 89) : new SKColor(58, 120, 240);
            if (d.on) G.Fill(c, r, 14, onColor.WithAlpha(G.A((0.28f + 0.08f * k) * a)));
            else Tile(f, r, k);
            G.Circle(c, r.Left + 20, r.Top + 19, 11, d.on ? onColor.WithAlpha(G.A(a)) : G.White(0.1f * a));
            Icons.Draw(c, d.icon, r.Left + 20, r.Top + 19, 13, G.Paper(a));
            using var name = Fonts.Med(11);
            using var state = Fonts.Sub(10.5f);
            G.Text(c, G.Ellipsize(d.name, name, r.Width - 18), r.Left + 10, r.Top + 43, name, G.Paper(a));
            G.Text(c, G.Ellipsize(d.state, state, r.Width - 18), r.Left + 10, r.Top + 55, state, G.Paper(0.55f * a));
            _tiles.Add((r, d.id));
        }

        // Power mode.
        var seg = SKRect.Create(16, 170, f.W - 32, 26);
        G.Fill(c, seg, 13, G.White(0.07f * a));
        string[] labels = { "Battery saver", "Balanced", "Performance" };
        float sw = seg.Width / 3;
        using var font = Fonts.Med(11.5f);
        for (int i = 0; i < 3; i++)
        {
            var r = SKRect.Create(seg.Left + i * sw + 2, seg.Top + 2, sw - 4, seg.Height - 4);
            _modes[i] = r;
            bool on = _power == i;
            float k = Hov(f, "mode" + i, G.In(r, f.Pointer));
            if (on) G.Fill(c, r, 11, G.Paper(0.92f * a));
            else if (k > 0.003f) G.Fill(c, r, 11, G.White(0.06f * k * a));
            G.Text(c, labels[i], r.MidX, r.MidY + 4, font, on ? G.Ink.WithAlpha(G.A(a)) : G.Paper((_power < 0 ? 0.35f : 0.7f) * a), SKTextAlign.Center);
        }
    }

    /// <summary>A tall slider filled from the bottom, the level printed at its top.</summary>
    private void Slider(Frame f, string key, SKRect r, float level, string icon)
    {
        var c = f.C;
        float a = f.A;
        float k = Hov(f, key, G.In(r, f.Pointer) || _drag == key);
        G.Fill(c, r, 16, G.White((0.1f + 0.03f * k) * a));
        bool enabled = level >= 0;
        if (enabled && level > 0)
        {
            c.Save();
            c.ClipRoundRect(new SKRoundRect(r, 16), SKClipOperation.Intersect, true);
            float fh = r.Height * level;
            G.Fill(c, SKRect.Create(r.Left, r.Bottom - fh, r.Width, fh), 0, G.Paper(0.94f * a));
            c.Restore();
        }
        using var mono = Fonts.Mono(11);
        string label = enabled ? $"{level * 100:0}%" : "—";
        bool topLit = enabled && level > 0.88f, bottomLit = enabled && level > 0.12f;
        G.Text(c, label, r.MidX, r.Top + 20, mono, topLit ? G.Ink.WithAlpha(G.A(0.8f * a)) : G.Paper(0.75f * a), SKTextAlign.Center);
        Icons.Draw(c, icon, r.MidX, r.Bottom - 18, 15, bottomLit ? G.Ink.WithAlpha(G.A(0.85f * a)) : G.Paper((enabled ? 0.8f : 0.35f) * a));
    }

    public override bool Down(SKPoint p, MouseButtons b)
    {
        if (b != MouseButtons.Left) return false;
        if (_vol.Contains(p)) { _drag = "vol"; Drag(p); return true; }
        if (_bright.Contains(p) && Host.Brightness.Supported) { _drag = "bright"; Drag(p); return true; }
        return false;
    }

    public override void Drag(SKPoint p)
    {
        var r = _drag == "vol" ? _vol : _bright;
        float v = Math.Clamp((r.Bottom - p.Y) / r.Height, 0, 1);
        if (_drag == "vol") { _volume = v; _muted = false; Host.Audio.Volume = v; }
        else if (_drag == "bright") Host.Brightness.Set((int)MathF.Round(v * 100));
        Host.Invalidate();
    }

    public override void Up(SKPoint p) => _drag = null;

    /// <summary>The focus tile's line: time left while one runs, otherwise its length and today's count.</summary>
    private string FocusState()
    {
        if (Host.FindActivity("focus") is { } act) return act.Value() + " left";
        var s = Host.Settings;
        int done = s.FocusDay == DateTime.Now.ToString("yyyy-MM-dd") ? s.FocusToday : 0;
        return done > 0 ? $"{s.FocusMinutes} min · {done} today" : $"Start {s.FocusMinutes} min";
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        for (int i = 0; i < 3; i++)
            if (_modes[i].Contains(p) && _power >= 0) { PowerMode.Set(i); Poll(force: true); return; }
        foreach (var (r, id) in _tiles)
        {
            if (!r.Contains(p)) continue;
            switch (id)
            {
                case "wifi": Host.Radios.ToggleWifi(); break;
                case "bt": Host.Radios.ToggleBluetooth(); break;
                case "session":
                    // Start a focus session (your length from Settings), or stop the one running.
                    if (Host.FindActivity("focus") != null) Host.EndActivity("focus");
                    else IslandWindow.Instance?.Features?.StartFocus();
                    break;
                case "focus": Shell.Open("ms-settings:quiethours"); Host.Collapse(); break;
                case "mic":
                    bool m = Host.Audio.ToggleMic();
                    Host.Toast(m ? "mic-off" : "mic", m ? "Microphone muted" : "Microphone on", m ? new SKColor(255, 95, 87) : null);
                    break;
                case "battery": Shell.Open("ms-settings:batterysaver"); Host.Collapse(); break;
            }
            Poll(force: true);
            Host.Invalidate();
            return;
        }
    }

    public override bool Wheel(SKPoint p, int delta)
    {
        if (_bright.Contains(p) && Host.Brightness.Supported)
        {
            Host.Brightness.Set(Math.Clamp(Host.Brightness.Level + Math.Sign(delta) * 5, 0, 100));
            return true;
        }
        _volume = Math.Clamp(MathF.Round((Host.Audio.Volume + Math.Sign(delta) * 0.05f) * 20) / 20f, 0, 1);
        Host.Audio.Volume = _volume;
        _muted = false;
        Host.Invalidate();
        return true;
    }
}
