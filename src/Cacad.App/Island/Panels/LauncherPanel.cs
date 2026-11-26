using SkiaSharp;

namespace Cacad;

public sealed class LauncherPanel : IslandPanel
{
    public LauncherPanel(IIslandHost host) : base(host) { }

    public override string Id => "launcher";
    public override string Icon => "grip-horizontal";
    public override string Title => "Launcher";
    public override float Height => 202;

    private const int Slots = 6;
    private const float AppsTop = 116;
    private readonly SKRect[] _actions = new SKRect[6];
    private readonly SKRect[] _slots = new SKRect[Slots];

    public override void Draw(Frame f)
    {
        var c = f.C;
        float a = f.A;
        var defs = new (string icon, string label, SKColor? dot)[]
        {
            ("camera", "Screenshot", null),
            ("scissors", "Snip", null),
            ("video", "Record", new SKColor(255, 95, 87)),
            ("leaf", "Focus", null),
            ("clock", "Timer", null),
            ("palette", "Pick colour", null),
        };
        float bw = (f.W - 32 - 16) / 3;
        using var font = Fonts.Med(12);
        for (int i = 0; i < defs.Length; i++)
        {
            var r = SKRect.Create(16 + i % 3 * (bw + 8), 30 + i / 3 * 42, bw, 34);
            _actions[i] = r;
            float k = Hov(f, "act" + i, G.In(r, f.Pointer));
            Tile(f, r, k, 12);
            float tw = G.Measure(font, defs[i].label) + 22;
            float x = r.MidX - tw / 2;
            if (defs[i].dot is { } dot) G.Circle(c, x + 6, r.MidY, 4.5f, dot.WithAlpha(G.A(a)));
            else Icons.Draw(c, defs[i].icon, x + 6, r.MidY, 14, G.Paper(a));
            G.Text(c, defs[i].label, x + 20, r.MidY + 4.5f, font, G.Paper(a));
        }

        var apps = Host.Settings.LauncherApps;
        float pitch = (f.W - 32) / Slots;
        using var name = Fonts.Sub(11);
        for (int i = 0; i < Slots; i++)
        {
            var box = SKRect.Create(16 + i * pitch + (pitch - 44) / 2, AppsTop + 4, 44, 44);
            _slots[i] = SKRect.Create(16 + i * pitch, AppsTop, pitch, 80);
            float k = Hov(f, "slot" + i, G.In(_slots[i], f.Pointer));
            if (i < apps.Count)
            {
                if (k > 0.003f) G.Fill(c, SKRect.Create(box.Left - 6, box.Top - 4, box.Width + 12, 74), 12, G.White(0.08f * k * a));
                var img = ShellImages.Get(apps[i].Path, 96);
                if (img != null) G.Image(c, img, SKRect.Create(box.Left + 4, box.Top + 4, 36, 36), a);
                else Icons.Draw(c, "monitor", box.MidX, box.MidY, 24, G.Paper(0.6f * a));
                G.Text(c, G.Ellipsize(apps[i].Name, name, pitch - 6), box.MidX, box.Bottom + 16, name, G.Paper(0.85f * a), SKTextAlign.Center);
            }
            else
            {
                using (var dash = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, Color = G.White((0.22f + 0.2f * k) * a), PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0) })
                    c.DrawRoundRect(box, 12, 12, dash);
                Icons.Draw(c, "plus", box.MidX, box.MidY, 16, G.Paper((0.5f + 0.4f * k) * a));
                G.Text(c, "Add", box.MidX, box.Bottom + 16, name, G.Paper(0.55f * a), SKTextAlign.Center);
            }
        }
    }

    public override void Click(SKPoint p, MouseButtons b)
    {
        if (_actions[0].Contains(p))
        {
            Host.Collapse();
            // Let the card close before the picture is taken.
            var t = new System.Windows.Forms.Timer { Interval = 450 };
            t.Tick += (_, _) =>
            {
                t.Dispose();
                var path = Screenshots.Capture(Host.MonitorBounds);
                if (path != null)
                {
                    Host.Toast("camera", "Screenshot saved and copied");
                    IslandWindow.Instance?.Companion?.Pulse("screenshot");
                }
            };
            t.Start();
            return;
        }
        if (_actions[1].Contains(p)) { Host.Collapse(); Shell.Open("ms-screenclip:"); return; }
        if (_actions[2].Contains(p))
        {
            // Xbox Game Bar's record shortcut (Win+Alt+R).
            Host.Collapse();
            Native.Chord(0x5B, 0x12, (byte)'R');
            return;
        }
        var features = IslandWindow.Instance?.Features;
        if (_actions[3].Contains(p)) { Host.Collapse(); features?.StartFocus(); return; }
        if (_actions[4].Contains(p)) { features?.PromptTimer(); Host.Collapse(); return; }
        if (_actions[5].Contains(p)) { features?.PickColour(); return; }

        var apps = Host.Settings.LauncherApps;
        for (int i = 0; i < Slots; i++)
        {
            if (!_slots[i].Contains(p)) continue;
            if (i < apps.Count)
            {
                if (b == MouseButtons.Right) { apps.RemoveAt(i); Host.Settings.Save(); Host.Invalidate(); }
                else { Shell.Open(apps[i].Path); Host.Collapse(); }
            }
            else AddApp();
            return;
        }
    }

    private void AddApp()
    {
        Host.Hold(true);
        try
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Pin an app to the island",
                Filter = "Apps and shortcuts|*.exe;*.lnk;*.url;*.appref-ms|All files|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + "\\Programs",
                DereferenceLinks = false,
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            string path = dlg.FileName;
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(info.FileDescription)) name = info.FileDescription!;
            }
            catch { }
            Host.Settings.LauncherApps.Add(new PinnedApp { Path = path, Name = name });
            Host.Settings.Save();
        }
        finally { Host.Hold(false); }
    }
}
