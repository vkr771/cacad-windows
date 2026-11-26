using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace Cacad;

/// <summary>The pixel cat's mind: picks a mood from what is happening on the PC (by priority), lets short reactions
/// take over for a moment, and runs the little life in it (blinks, ear twitches, a pop when its mood changes).
/// No AI and nothing leaves the PC: it reads the foreground app's name and title, the music, the battery, the network
/// and how fast you type (a count, never the keys, apart from spotting "meow").</summary>
public sealed class Companion : IDisposable
{
    private readonly IslandWindow _w;
    private SettingsStore S => _w.Settings;
    /// <summary>When Print Screen was last pressed (so a screenshot copied right after is shown as one).</summary>
    public static DateTime LastPrintScreen = DateTime.MinValue;
    private readonly Random _rng = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double Now => _clock.Elapsed.TotalSeconds;

    public Pet Pet { get; }

    public Companion(IslandWindow w)
    {
        _w = w;
        Pet = new Pet(w);
        NetworkChange.NetworkAvailabilityChanged += (_, _) => _onlineDirty = true;
        NetworkChange.NetworkAddressChanged += (_, _) => _onlineDirty = true;
        if (Environment.GetEnvironmentVariable("CACAD_CATSHEET") is { Length: > 0 } sheet)
            try { if (Path.GetFileName(sheet).StartsWith("showcase-")) Showcase(sheet); else if (Path.GetFileName(sheet).StartsWith("style-")) RenderStyle(sheet); else if (sheet.Contains("surfaces")) SurfaceSheet(sheet); else if (sheet.Contains("bars")) BarSheet(sheet); else if (sheet.Contains("clocks")) ClockSheet(sheet); else if (sheet.Contains("lyrics")) LyricsSheet(sheet); else if (sheet.Contains("girls")) RenderGirls(sheet); else if (sheet.Contains("zoo")) RenderZoo(sheet); else RenderSheet(sheet); }
            catch (Exception ex) { try { File.AppendAllText(Path.Combine(SettingsStore.DataDir, "crash.log"), $"==== cat sheet\r\n{ex}\r\n\r\n"); } catch { } }
        _w.Clipboard.Changed += OnClipboard;
        if (S.CompanionTyping) StartKeyboard();
        Pulse(DateTime.Now.Hour is >= 5 and < 11 ? "morning" : "hi");
    }

    // ---------- the poses ----------

    private static readonly Dictionary<string, (string group, CatPose pose)> Moods = new()
    {
        ["neutral"] = ("", new(CatFace.Open)),
        // music
        ["music"] = ("music", new(CatFace.Happy, Worn: "headphones", Motion: "bob", Particles: "notes")),
        ["rainy"] = ("time", new(CatFace.Open, Side: "umbrella")),
        ["snowy"] = ("time", new(CatFace.Happy, Side: "snowflake")),
        ["hungry"] = ("interact", new(CatFace.Droopy, Bubble: "feed me?")),
        ["singing"] = ("music", new(CatFace.Sing, Worn: "headphones", Side: "mic", Motion: "bob", Particles: "notes")),
        ["musicpaused"] = ("music", new(CatFace.Open, Worn: "neckphones")),
        ["coverears"] = ("music", new(CatFace.XEyes, Worn: "pawears", Motion: "shake")),
        ["shush"] = ("music", new(CatFace.Wink, Worn: "pawmouth", Bubble: "shh")),
        ["songchange"] = ("music", new(CatFace.Surprised, Worn: "headphones", Side: "note")),
        // work and apps
        ["idea"] = ("work", new(CatFace.Open, Side: "bulb", Particles: "sparkles")),
        ["focus"] = ("work", new(CatFace.Determined, Side: "coffee+bolt")),
        ["done"] = ("work", new(CatFace.Happy, Worn: "glasses", Side: "checklist")),
        ["reading"] = ("work", new(CatFace.Open, Worn: "glasses", Side: "scroll")),
        ["building"] = ("work", new(CatFace.Open, Worn: "hardhat", Side: "wrench")),
        ["ai"] = ("work", new(CatFace.Happy, Side: "robot", Particles: "sparkles")),
        ["aihello"] = ("work", new(CatFace.Surprised, Side: "robot", Bubble: "beep boop?", Motion: "tilt")),
        ["trading"] = ("work", new(CatFace.Determined, Worn: "glasses", Side: "chart")),
        ["tradehello"] = ("work", new(CatFace.Shades, Side: "chart", Bubble: "to the moon!", Particles: "coins", Motion: "jump")),
        ["coding"] = ("work", new(CatFace.Happy, Side: "laptop", Particles: "sparkles")),
        ["typing"] = ("work", new(CatFace.Determined, Side: "keyboard", Motion: "type")),
        ["browsing"] = ("work", new(CatFace.Open, Side: "browser")),
        ["gaming"] = ("work", new(CatFace.Determined, Side: "controller", Motion: "type")),
        ["call"] = ("work", new(CatFace.Open, Worn: "headset")),
        ["callmuted"] = ("work", new(CatFace.Wink, Worn: "pawmouth", Bubble: "muted")),
        ["meeting"] = ("work", new(CatFace.Open, Worn: "tie")),
        ["popcorn"] = ("work", new(CatFace.Happy, Side: "popcorn")),
        ["screenshot"] = ("work", new(CatFace.Surprised, Side: "camera", Particles: "flash")),
        ["juggle"] = ("work", new(CatFace.Surprised, Side: "docs")),
        // system
        ["flex"] = ("system", new(CatFace.Determined, Side: "arm+bolt")),
        ["sweat"] = ("system", new(CatFace.Droopy, Side: "battery", Particles: "sweat")),
        ["critical"] = ("system", new(CatFace.Closed, Motion: "flat", Bubble: "charge me")),
        ["offline"] = ("system", new(CatFace.Droopy, Side: "plug")),
        ["hot"] = ("system", new(CatFace.Droopy, Side: "fan", Particles: "sweat")),
        ["dizzy"] = ("system", new(CatFace.Spiral, Side: "spiral", Motion: "wobble")),
        ["gift"] = ("system", new(CatFace.Happy, Side: "gift", Particles: "sparkles")),
        // time of day
        ["hi"] = ("time", new(CatFace.Happy, Bubble: "Hi!")),
        ["ping"] = ("system", new(CatFace.Surprised, Side: "bang", Motion: "jump")),
        ["morning"] = ("time", new(CatFace.Yawn, Side: "sun", Motion: "tilt")),
        ["sleep"] = ("time", new(CatFace.Closed, Particles: "zzz")),
        ["lunch"] = ("time", new(CatFace.Happy, Side: "fish")),
        ["latenight"] = ("time", new(CatFace.Yawn, Side: "moon")),
        ["weekend"] = ("time", new(CatFace.Shades)),
        ["birthday"] = ("time", new(CatFace.Happy, Worn: "partyhat", Particles: "sparkles", Bubble: "yay!")),
        // you
        ["purr"] = ("interact", new(CatFace.Happy, Particles: "hearts")),
        ["poked"] = ("interact", new(CatFace.Surprised, Side: "bang", Motion: "jump")),
        ["grumpy"] = ("interact", new(CatFace.Grumpy, Bubble: "hmph")),
        ["wave"] = ("interact", new(CatFace.Happy, Side: "paw", Bubble: "hey")),
        ["reach"] = ("interact", new(CatFace.Surprised, Worn: "pawsup")),
        // fun
        ["meow"] = ("fun", new(CatFace.Happy, Bubble: "meow!")),
        ["curled"] = ("fun", new(CatFace.Closed, Motion: "ball", Particles: "zzz")),
        ["yawn"] = ("fun", new(CatFace.Yawn)),
        ["groom"] = ("fun", new(CatFace.Closed, Side: "paw")),
        ["tilt"] = ("fun", new(CatFace.Open, Motion: "tilt")),
    };

    public static readonly (string id, string name, string about)[] Groups =
    {
        ("music", "Music", "headphones and beat-bobbing, song changes, paused, too loud, muted"),
        ("work", "Work & apps", "coding, typing, reading, browsing, gaming, AI apps, trading, calls, meetings, videos, downloads, focus"),
        ("system", "System", "low battery, offline, running hot, memory full, charging, download done"),
        ("time", "Time of day", "hello, morning, lunch, late night, weekends, your birthday, sleeping when you're away"),
        ("interact", "Petting", "pet it (hover in the card), poke it (click), it waves if you've been away"),
        ("fun", "Little moments", "yawns, grooming, curling up, and a hidden word"),
    };

    /// <summary>Dev hook: every mood, large and labelled, on one sheet (CACAD_CATSHEET=path.png).</summary>
    public static void RenderSheet(string path)
    {
        const float px = 5;
        int cols = 6, rows = (Moods.Count + cols - 1) / cols;
        float cellW = 190, cellH = 140;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)(cols * cellW), (int)(rows * cellH)));
        var c = surface.Canvas;
        c.Clear(new SkiaSharp.SKColor(8, 8, 10));
        int i = 0;
        using var font = Fonts.Med(13);
        foreach (var (name, (_, pose)) in Moods)
        {
            float x = i % cols * cellW, y = i / cols * cellH;
            CatArt.Draw(c, pose, x + 26, y + 42, px, 1, 1.3 + i * 0.37, 0.6f, 0, false, false);
            G.Text(c, name, x + 10, y + cellH - 10, font, G.Paper(0.6f));
            i++;
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Dev hook: every drawn surface, as a pill and as a card, with a clock and lyric line on top.</summary>
    public static void SurfaceSheet(string path)
    {
        C3E.On = false;
        var themes = Themes.All.Where(t => t.Surface != null && t.Surface != "custom").ToArray();
        const float cw = 420, ch = 150, pw = 230, ph = 37;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)(cw + pw + 60), (int)(themes.Length * (ch + 14) + 14)));
        var c = surface.Canvas; c.Clear(new SkiaSharp.SKColor(40, 40, 46));
        using var big = Fonts.Title(30); using var small = Fonts.Med(13);
        for (int i = 0; i < themes.Length; i++)
        {
            var t = themes[i]; float y = 14 + i * (ch + 14);
            void Island(float x, float yy, float w, float h, bool card)
            {
                c.Save();
                using var clip = new SkiaSharp.SKRoundRect(SkiaSharp.SKRect.Create(x, yy, w, h), card ? 22 : h / 2);
                c.ClipRoundRect(clip, SkiaSharp.SKClipOperation.Intersect, true);
                using (var fill = new SkiaSharp.SKPaint { Color = t.Fill }) c.DrawRect(SkiaSharp.SKRect.Create(x, yy, w, h), fill);
                c.Translate(x + w / 2, yy);
                Surfaces.DrawBase(c, t, SkiaSharp.SKRect.Create(x, yy, w, h), w, h, 1, 7.3, new SkiaSharp.SKPoint(0.5f, 0.5f), card ? 1 : 0, false);
                if (card) { G.Text(c, "4:12", -w / 2 + 22, 54, big, G.Paper(1)); G.Text(c, t.Name, -w / 2 + 22, 80, small, G.Paper(0.6f)); }
                else G.Text(c, "City lights fade into the morning", 0, h / 2 + 5, small, G.Paper(1), SkiaSharp.SKTextAlign.Center);
                c.Restore();
            }
            Island(10, y, cw, ch, true);
            Island(cw + 40, y + ch / 2 - ph / 2, pw, ph, false);
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Dev hook: every progress bar and visualizer, with made-up levels.</summary>
    public static void BarSheet(string path)
    {
        var bars = ProgressBars.Styles; var vis = Visualizers.Styles;
        int rows = Math.Max(bars.Length, vis.Length);
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(620, rows * 44 + 20));
        var c = surface.Canvas; c.Clear(new SkiaSharp.SKColor(14, 14, 18));
        var accent = new SkiaSharp.SKColor(170, 140, 255);
        Visualizers.Live = b => 0.35f + 0.6f * (b == 0 ? 0.9f : b == 1 ? 0.5f : b == 2 ? 0.75f : 0.3f);
        using var label = Fonts.Sub(11);
        for (int i = 0; i < bars.Length; i++)
        {
            float y = 20 + i * 44;
            G.Text(c, bars[i].name, 10, y + 4, label, G.Paper(0.6f));
            var st = new BarState();
            for (int k = 0; k < 20; k++) ProgressBars.Draw(SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(4, 4)).Canvas, st, bars[i].id, 100, 300, y, accent, 0.58f, 1, 0, -1, 2.3f + k / 30f, 1, true, 1 / 30f, "demo");
            ProgressBars.Draw(c, st, bars[i].id, 100, 300, y, accent, 0.58f, 1, 0, -1, 3f, 1, true, 1 / 30f, "demo");
        }
        for (int i = 0; i < vis.Length; i++)
        {
            float y = 20 + i * 44;
            G.Text(c, vis[i].name, 450, y + 4, label, G.Paper(0.6f));
            G.Fill(c, SkiaSharp.SKRect.Create(520, y - 16, 80, 32), 16, new SkiaSharp.SKColor(4, 4, 6));
            Visualizers.Draw(c, vis[i].id, 590, y, accent, 1.7f);
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Dev hook: every clock face, in the pill and in the card.</summary>
    public static void ClockSheet(string path)
    {
        var styles = ClockFaces.Styles;
        const float cw = 448, ch = 160, rowH = ch + 60;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)(cw * 2 + 40), (int)(rowH * ((styles.Length + 1) / 2) + 10)));
        var c = surface.Canvas;
        c.Clear(new SkiaSharp.SKColor(30, 30, 34));
        var theme = Themes.Get("black");
        for (int i = 0; i < styles.Length; i++)
        {
            float x = 10 + i % 2 * (cw + 20), y = 10 + i / 2 * rowH;
            var faces = new ClockFaces();
            float pw = ClockFaces.Width(styles[i].id, 37, false) + 40, px0 = x + cw / 2;
            c.Save(); c.Translate(px0, y);
            G.Fill(c, SkiaSharp.SKRect.Create(-pw / 2, 0, pw, 37), 18.5f, new SkiaSharp.SKColor(4, 4, 6));
            faces.DrawPill(c, styles[i].id, pw, 37, 1, theme, false, 10, 1 / 30f);
            c.Restore();
            var cardR = SkiaSharp.SKRect.Create(x, y + 46, cw, ch);
            if (i % 2 == 0) G.Fill(c, cardR, 22, new SkiaSharp.SKColor(8, 8, 10));
            else using (var gsh = SkiaSharp.SKShader.CreateLinearGradient(new SkiaSharp.SKPoint(cardR.Left, cardR.Top), new SkiaSharp.SKPoint(cardR.Right, cardR.Bottom), new[] { new SkiaSharp.SKColor(40, 30, 90), new SkiaSharp.SKColor(120, 50, 110), new SkiaSharp.SKColor(30, 80, 120) }, null, SkiaSharp.SKShaderTileMode.Clamp)) using (var gp = new SkiaSharp.SKPaint { IsAntialias = true, Shader = gsh }) c.DrawRoundRect(cardR, 22, 22, gp);
            c.Save(); c.Translate(x, y + 46);
            new ClockFaces().DrawCard(c, styles[i].id, SkiaSharp.SKRect.Create(16, 34, cw - 32, ch - 56), 1, theme, false, 10, 1 / 30f);
            c.Restore();
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Dev hook: the Lyrics View (card and pill) through a line change, from a saved song.</summary>
    public static void LyricsSheet(string path)
    {
        var file = Directory.GetFiles(Path.Combine(SettingsStore.DataDir, "lyrics"), "*.lrc").Where(p => new FileInfo(p).Length > 0).OrderByDescending(p => new FileInfo(p).Length).First();
        var song = LyricsService.Parse("sheet", File.ReadAllText(file))!;
        int li = Math.Min(4, song.Lines.Count - 2);
        double t0 = song.Lines[li].At + 1.0;
        double[] times = { 0, 0.4, 0.9, 1.4, 1.9 };
        double jump = song.Lines[li + 1].At - t0 - 0.35;
        const float cw = 448, ch = 200, pw = 330, ph = 37;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)(cw * 2 + 30), (int)((ch + 20) * 5)));
        var c = surface.Canvas;
        c.Clear(new SkiaSharp.SKColor(20, 20, 24));
        var card = new LyricsView(LyricsView.Card);
        var pill = new LyricsView(LyricsView.Pill);
        // Warm up at t0 (settled), then step through a line change frame by frame.
        double clock = 100;
        card.Draw(SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(10, 10)).Canvas, song, SkiaSharp.SKRect.Create(24, 34, cw - 48, 160), t0, clock, 1);
        pill.Draw(SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(10, 10)).Canvas, song, SkiaSharp.SKRect.Create(0, 0, pw - 74, ph), t0, clock, 1);
        var shots = new[] { 0.0, jump, jump + 0.12, jump + 0.24, jump + 0.4, jump + 0.6, jump + 0.9, jump + 1.4, jump + 2.4, jump + 3.4 };
        double prev = 0;
        for (int k = 0; k < shots.Length; k++)
        {
            // Advance in small steps so the springs run as they would at 60 Hz.
            for (double s = prev; s < shots[k]; s += 1 / 60.0) { clock += 1 / 60.0; using var tmp = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(10, 10)); card.Draw(tmp.Canvas, song, SkiaSharp.SKRect.Create(24, 34, cw - 48, 160), t0 + s, clock, 1); pill.Draw(tmp.Canvas, song, SkiaSharp.SKRect.Create(0, 0, pw - 74, ph), t0 + s, clock, 1); }
            prev = shots[k];
            int slot = k;
            float x = slot % 2 * (cw + 30), y = slot / 2 * (ch + 20);
            c.Save(); c.Translate(x, y);
            G.Fill(c, SkiaSharp.SKRect.Create(0, 0, cw, ch), 20, new SkiaSharp.SKColor(40, 34, 44));
            card.Draw(c, song, SkiaSharp.SKRect.Create(24, 34, cw - 48, 160), t0 + shots[k], clock, 1);
            using (var f = Fonts.Mono(11)) G.Text(c, $"+{shots[k]:0.00}s", cw - 70, 20, f, G.Paper(0.6f));
            G.Fill(c, SkiaSharp.SKRect.Create(cw - pw - 10, ch - ph - 6, pw, ph), ph / 2, k % 2 == 0 ? new SkiaSharp.SKColor(8, 8, 10) : new SkiaSharp.SKColor(176, 186, 200));
            pill.Draw(c, song, SkiaSharp.SKRect.Create(cw - pw - 10 + 40, ch - ph - 6, pw - 74, ph), t0 + shots[k], clock, 1);
            c.Restore();
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Dev hook: each anime girl, large, in every face and a few worn things.</summary>
    public static void RenderGirls(string path)
    {
        const float px = 7, cellW = 150, cellH = 130;
        var poses = Enum.GetValues<CatFace>().Select(f => new CatPose(f)).Concat(new[] { new CatPose(CatFace.Happy, "headphones"), new CatPose(CatFace.Determined, "hardhat"), new CatPose(CatFace.Open, "glasses"), new CatPose(CatFace.XEyes, "pawears"), new CatPose(CatFace.Happy, "partyhat") }).ToArray();
        var girls = CatArt.Species.Where(s => s.Anime || s.Id is "cat" or "panda").ToArray();
        CatArt.SingOpen = 0.8f;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)(poses.Length * cellW), (int)(girls.Length * cellH)));
        var c = surface.Canvas;
        c.Clear(new SkiaSharp.SKColor(8, 8, 10));
        for (int r = 0; r < girls.Length; r++)
            for (int k = 0; k < poses.Length; k++)
                CatArt.Draw(c, poses[k], k * cellW + 18, r * cellH + 40, px, 1, 1.0, 0.5f, 0, false, false, girls[r].Id, "natural");
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Dev hook ("showcase-styles.png"): companions (columns) in every art style (rows), on transparent; and
    /// ("showcase-moods.png") two companions acting out a row of reactions. For the README.</summary>
    public static void Showcase(string path)
    {
        const float px = 6, cellW = 150, cellH = 112;
        bool moods = path.Contains("moods");
        string[] species = { "cat", "bunny", "fox", "panda", "hana", "kuri", "yuki" };
        CatPose[] poses = { new(CatFace.Happy), new(CatFace.Open), new(CatFace.Love), new(CatFace.Wink), new(CatFace.Surprised), new(CatFace.Happy, Particles: "hearts"), new(CatFace.Open) };
        string[] moodKeys = { "music", "coding", "ai", "trading", "rainy", "sleep", "birthday", "coverears" };
        (string sp, string style)[] rowsM = { ("cat", "pixel"), ("kuri", "anime") };
        int cols = moods ? moodKeys.Length : species.Length, rows = moods ? rowsM.Length : CatArt.Styles.Length;
        float w = moods ? 190 : cellW;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)(cols * w), (int)(rows * cellH)));
        var c = surface.Canvas;
        c.Clear(SkiaSharp.SKColors.Transparent);
        CatArt.SingOpen = 0.6f;
        for (int r = 0; r < rows; r++)
            for (int k = 0; k < cols; k++)
            {
                var pose = moods ? Moods[moodKeys[k]].pose : poses[k];
                string sp = moods ? rowsM[r].sp : species[k], style = moods ? rowsM[r].style : CatArt.Styles[r].Id;
                float x = k * w + (w - CatArt.Measure(pose, px)) / 2 + CatArt.Whisker * px;
                CatArt.Draw(c, pose, x, r * cellH + 26, px, 1, 2.0 + k * 0.7, 0.6f, 0, false, false, sp, "natural", style);
            }
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Dev hook: one art style ("style-toon.png" etc.): every animal (rows) making every face, curled up,
    /// and in a few coats.</summary>
    public static void RenderStyle(string path)
    {
        string style = CatArt.Styles.Select(s => s.Id).FirstOrDefault(id => Path.GetFileName(path).Contains("-" + id)) ?? "toon";
        bool small = path.Contains("small"); float px = small ? 2.0f : 5, cellW = small ? 52 : 130, cellH = small ? 40 : 100;
        var faces = Enum.GetValues<CatFace>();
        var extra = new[] { (new CatPose(CatFace.Happy, "headphones", Particles: "notes"), "natural"), (new CatPose(CatFace.Closed, Motion: "ball"), "natural"),
            (new CatPose(CatFace.Open), "charcoal"), (new CatPose(CatFace.Open), "snow"), (new CatPose(CatFace.Open), "ginger"), (new CatPose(CatFace.Open), "void") };
        var animals = CatArt.Species.Where(s => s.Anime == path.Contains("girls")).ToArray();
        int cols = faces.Length + extra.Length, rows = animals.Length;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)(cols * cellW), (int)(rows * cellH)));
        var c = surface.Canvas;
        c.Clear(new SkiaSharp.SKColor(8, 8, 10));
        for (int r = 0; r < rows; r++)
            for (int k = 0; k < cols; k++)
            {
                var (pose, skin) = k < faces.Length ? (new CatPose(faces[k]), "natural") : extra[k - faces.Length];
                CatArt.SingOpen = 0.8f;
                CatArt.Draw(c, pose, k * cellW + 5.4f * px, r * cellH + 6 * px, px, 1, 1.0, 0.5f, 0, false, false, animals[r].Id, skin, style);
            }
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Dev hook: every companion in every skin, plus a few moods for each companion.</summary>
    public static void RenderZoo(string path)
    {
        const float px = 5, cellW = 130, cellH = 100;
        var poses = new[] { new CatPose(CatFace.Open), new CatPose(CatFace.Happy, "headphones", Particles: "notes"), new CatPose(CatFace.Shades, "hardhat"), new CatPose(CatFace.Yawn), new CatPose(CatFace.Love), new CatPose(CatFace.Closed, Motion: "ball") };
        int cols = CatArt.Skins.Length + poses.Length, rows = CatArt.Species.Length;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo((int)(cols * cellW), (int)(rows * cellH)));
        var c = surface.Canvas;
        c.Clear(new SkiaSharp.SKColor(8, 8, 10));
        for (int r = 0; r < rows; r++)
            for (int k = 0; k < cols; k++)
            {
                var sp = CatArt.Species[r];
                bool skinCol = k < CatArt.Skins.Length;
                var pose = skinCol ? new CatPose(CatFace.Open) : poses[k - CatArt.Skins.Length];
                CatArt.Draw(c, pose, k * cellW + 25, r * cellH + 30, px, 1, 1.0, 0.5f, 0, false, false, sp.Id, skinCol ? CatArt.Skins[k].Id : "natural");
            }
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private bool Allowed(string mood) => Moods.TryGetValue(mood, out var m) && (m.group == "" || !S.CompanionOff.Contains(m.group));

    // ---------- what the island draws ----------

    private string _mood = "neutral", _shown = "neutral";
    private string? _event;
    private double _eventUntil, _popAt = -9, _blinkAt, _blinkNext = 3, _twitchAt = -9, _twitchNext = 7;

    public CatPose Pose => Moods[_shown].pose;
    public string Mood => _shown;
    public float Pop => (float)Math.Clamp(1 - (Now - _popAt) / 0.3, 0, 1);
    public bool Blink => Now - _blinkAt < 0.13;
    public bool EarTwitch => Now - _twitchAt < 0.16;
    public double Time => Now;

    /// <summary>How fast the cat needs frames: lively while it moves or just changed, calm otherwise.</summary>
    public double Interval => Pop > 0 || Pose.Motion is "bob" or "jump" or "shake" or "wobble" or "type" || Pose.Particles != null ? 1 / 24.0 : 1 / 10.0;

    /// <summary>Beat strength 0..1 for bobbing: the real bass when the visualizer hears it, a steady sway otherwise.</summary>
    public float Beat => _w.Loopback.Fresh ? _w.Loopback.Level(0) : 0.5f + 0.5f * MathF.Sin((float)Now * 4.2f);

    /// <summary>Per frame: blinks, ear twitches, and switching to the mood that should show.</summary>
    public void Tick()
    {
        double t = Now;
        if (t > _blinkNext) { _blinkAt = t; _blinkNext = t + 2.5 + _rng.NextDouble() * 4; }
        if (t > _twitchNext) { _twitchAt = t; _twitchNext = t + 5 + _rng.NextDouble() * 8; }
        if (_event != null && t > _eventUntil) _event = null;
        // Lip sync: the mouth follows the voice (the mid bands of the sound playing), or a syllable-like rhythm
        // when the island is not listening to the sound.
        float voice = _w.Loopback.Fresh
            ? Math.Clamp(Math.Max(_w.Loopback.Level(1), _w.Loopback.Level(2)) * 1.25f - 0.15f, 0, 1)
            : 0.5f + 0.5f * MathF.Sin((float)t * 7.3f) * MathF.Sin((float)t * 2.1f + 1);
        CatArt.SingOpen += (voice - CatArt.SingOpen) * 0.45f;
        CatArt.HatId = Pet.HatId;
        string want = _event ?? _mood;
        if (want != _shown)
        {
            _shown = want;
            _popAt = t;
            _w.Invalidate();   // a new pose can be a different size: a whole frame, not a patch
        }
    }

    // ---------- reactions (short) ----------

    private static readonly Dictionary<string, double> Durations = new()
    {
        ["hi"] = 3, ["morning"] = 4, ["songchange"] = 2, ["idea"] = 3, ["done"] = 3, ["gift"] = 3.5, ["flex"] = 3.5,
        ["screenshot"] = 2, ["juggle"] = 3, ["poked"] = 1.2, ["grumpy"] = 3, ["wave"] = 3.5, ["reach"] = 1.5,
        ["aihello"] = 2.8, ["tradehello"] = 2.8, ["meow"] = 2.5, ["ping"] = 1.6, ["yawn"] = 2.5, ["coverears"] = 2.5, ["groom"] = 3, ["tilt"] = 3,
    };

    /// <summary>A short reaction (a gift when a download finishes, a checklist when something is done...).</summary>
    public void Pulse(string mood)
    {
        if (!S.Companion || !Allowed(mood)) return;
        _event = mood;
        _eventUntil = Now + (Durations.TryGetValue(mood, out var d) ? d : 2.5);
        _w.Invalidate();
    }

    // ---------- petting and poking (card corner) ----------

    private readonly List<double> _pokes = new();
    private bool _petting;
    private double _lastPetFed = -999;

    public void Hover(bool over)
    {
        if (over == _petting) return;
        _petting = over;
        // Petting feeds it a little (a few times a day, at most once a minute).
        if (over && Now - _lastPetFed > 60) { _lastPetFed = Now; Pet.Feed("pet"); }
        _lastNoticed = Now;
    }

    public void Poke()
    {
        double t = Now;
        _pokes.RemoveAll(p => t - p > 3);
        _pokes.Add(t);
        Pulse(_pokes.Count >= 5 ? "grumpy" : "poked");
        if (_pokes.Count >= 5) _pokes.Clear();
    }

    private double _lastNoticed;

    /// <summary>Called whenever the pointer is on the island: the cat has been noticed.</summary>
    public void Noticed() => _lastNoticed = Now;

    // ---------- the steady mood ----------

    private double _cpuHotSince = -1, _lastPlaying = -999, _nextRare = 60 * 45, _lastWave;
    private int _seenTrack = -1;
    private bool _wasIdle;

    /// <summary>Four times a second, from the island's poll.</summary>
    public void Poll()
    {
        if (!S.Companion) return;
        double t = Now;
        var media = _w.Media;
        if (media.IsPlaying) _lastPlaying = t;
        if (media.TrackVersion != _seenTrack)
        {
            if (_seenTrack >= 0 && media.IsPlaying) Pulse("songchange");
            _seenTrack = media.TrackVersion;
        }

        bool idle = Features.IdleSeconds() > 180;
        if (_wasIdle && !idle) Pulse("hi"); // back from being away
        _wasIdle = idle;

        // Ignored for two hours while you are busy elsewhere: a little wave.
        if (t - _lastNoticed > 2 * 3600 && t - _lastWave > 2 * 3600 && Features.IdleSeconds() < 60) { _lastWave = t; Pulse("wave"); }

        _w.Stats.Sample();
        if (_w.Stats.Cpu > 0.8f) { if (_cpuHotSince < 0) _cpuHotSince = t; } else _cpuHotSince = -1;
        if (t % 1 < 0.26) ReadForeground();

        string m = Choose(t, idle);
        // Now and then, when nothing much is happening, a small moment of its own.
        if (m == "neutral" && t > _nextRare)
        {
            _nextRare = t + 60 * (40 + _rng.Next(50));
            Pulse(new[] { "yawn", "groom", "tilt" }[_rng.Next(3)]);
        }
        _mood = _petting && Allowed("purr") ? "purr" : m;
    }

    private string Choose(double t, bool idle)
    {
        string? Try(string mood, bool when) => when && Allowed(mood) ? mood : null;
        var (hasBattery, level, charging) = SystemStats.Battery();
        var media = _w.Media;
        var app = _app;
        bool typingFast = TypingRate() >= 3.5;

        // The microphone is only asked about during a call (listing every microphone is slow).
        bool inCall = _w.FindActivity("call") != null;
        return Try(inCall && _w.Audio.MicMuted ? "callmuted" : "call", inCall)
            ?? Try("critical", hasBattery && !charging && level < 0.10f)
            ?? Try("offline", !_online)
            ?? Try(Features.IdleSeconds() > 30 * 60 ? "curled" : "sleep", idle)
            ?? Try("sweat", hasBattery && !charging && level < 0.20f)
            ?? Try("dizzy", _w.Stats.Ram > 0.92f || _w.Stats.Cpu > 0.97f)
            ?? Try("hot", _cpuHotSince >= 0 && t - _cpuHotSince > 15)
            ?? Try("gaming", app.Kind == "game")
            ?? Try("meeting", app.Kind == "meeting")
            ?? Try("building", _w.FindActivity("download") != null)
            ?? Try("focus", _w.FindActivity("focus") != null)
            ?? Try("popcorn", app.Kind == "video")
            ?? Try("trading", app.Kind == "trading")
            ?? Try("ai", app.Kind == "ai")
            ?? Try("shush", media.IsPlaying && _muted)
            ?? Try("singing", _w.Singing)
            ?? Try("music", media.IsPlaying)
            ?? Try(typingFast ? "typing" : "coding", app.Kind == "code")
            ?? Try("reading", app.Kind == "read")
            ?? Try("typing", typingFast)
            ?? Try("browsing", app.Kind == "browser")
            ?? Try("musicpaused", media.HasSession && t - _lastPlaying < 600)
            ?? Try("hungry", Pet.Hungry && DateTime.Now.Minute % 15 < 2)
            ?? Try("rainy", _w.Sky == "rain")
            ?? Try("snowy", _w.Sky == "snow")
            ?? Try("latenight", DateTime.Now.Hour < 5)
            ?? Try("birthday", IsBirthday())
            ?? Try("lunch", DateTime.Now.TimeOfDay is var tod && tod >= new TimeSpan(12, 30, 0) && tod < new TimeSpan(14, 0, 0))
            ?? Try("weekend", DateTime.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            ?? "neutral";
    }

    private bool IsBirthday() =>
        DateTime.TryParseExact(S.Birthday, "MM-dd", null, System.Globalization.DateTimeStyles.None, out var b) &&
        b.Month == DateTime.Now.Month && b.Day == DateTime.Now.Day;

    // ---------- the world, read once a second ----------

    private (string Kind, string Name) _app = ("", "");
    private bool _online = true, _muted, _onlineDirty = true;
    private float _volume;
    private readonly Dictionary<uint, (string name, string path)> _procs = new();

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    private void ReadForeground()
    {
        // Z3R: online or not comes from Windows' network events (asking every second lists every adapter, ~50 ms).
        if (!Z3R.On || _onlineDirty) { _onlineDirty = false; _online = NetworkInterface.GetIsNetworkAvailable(); }
        // Turning the volume up to full while music plays: a short "too loud" moment, then back to the headphones
        // (staying at full volume is normal on many PCs, so it is not a lasting mood).
        float v = _w.Audio.Volume;
        if (v >= 0.99f && _volume < 0.99f && _volume > 0 && _w.Media.IsPlaying) Pulse("coverears");
        _volume = v;
        _muted = _w.Audio.Muted;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == _w.Handle) return;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!_procs.TryGetValue(pid, out var proc))
            {
                if (_procs.Count > 200) _procs.Clear();
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    string path = "";
                    try { path = p.MainModule?.FileName ?? ""; } catch { }
                    proc = (p.ProcessName.ToLowerInvariant(), path.ToLowerInvariant());
                }
                catch { proc = ("", ""); }
                _procs[pid] = proc;
            }
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            var was = _app.Kind;
            _app = (Classify(proc.name, proc.path, sb.ToString()), proc.name);
            // Opening an AI app or a trading app: a quick hello first.
            if (_app.Kind != was && _app.Kind == "ai") Pulse("aihello");
            if (_app.Kind != was && _app.Kind == "trading") Pulse("tradehello");
        }
        catch { }
    }

    private static readonly HashSet<string> Code = new()
    {
        "code", "code - insiders", "devenv", "rider64", "idea64", "pycharm64", "webstorm64", "clion64", "goland64", "phpstorm64",
        "studio64", "sublime_text", "notepad++", "cursor", "zed", "windowsterminal", "wt", "cmd", "powershell", "pwsh",
        "alacritty", "wezterm-gui", "eclipse", "fleet", "windsurf",
    };
    private static readonly HashSet<string> Read = new() { "acrord32", "acrobat", "sumatrapdf", "foxitpdfreader", "foxitphantom", "winword", "kindle", "calibre", "okular", "drawboard pdf" };
    private static readonly HashSet<string> Browsers = new() { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "librewolf", "waterfox", "thorium", "zen" };
    private static readonly HashSet<string> Video = new() { "vlc", "mpc-hc64", "mpc-be64", "potplayermini64", "wmplayer", "video.ui", "microsoft.media.player", "mpv" };
    // AI assistants: desktop apps, and their sites in a browser tab (matched on the tab title).
    private static readonly HashSet<string> Ai = new() { "claude", "chatgpt", "copilot", "m365copilot", "perplexity", "gemini", "lm studio", "lmstudio", "ollama app", "jan", "msty", "poe", "deepseek" };
    private static readonly string[] AiSites = { "chatgpt", "claude", "gemini", "perplexity", "copilot", "deepseek", "grok", "poe -", "le chat", "huggingchat", "notebooklm", "ai studio" };
    // Trading and markets: MetaTrader (terminal64), TradingView, cTrader, NinjaTrader and friends, and broker or
    // market sites in a browser tab.
    private static readonly HashSet<string> Trading = new() { "tradingview", "ctrader", "ninjatrader", "thinkorswim", "tws", "tradestation", "metastock", "amibroker", "sierrachart", "quantower", "metatrader", "binance", "bybit", "kite", "zerodha", "upstox", "angelone", "dhan", "fyers" };
    private static readonly string[] TradingSites = { "tradingview", "zerodha", "kite", "groww", "upstox", "angel one", "dhan", "fyers", "binance", "coinbase", "bybit", "robinhood", "webull", "etoro", "investing.com", "yahoo finance", "moneycontrol", "screener.in", "nseindia", "sensibull", "coinmarketcap", "forex factory" };
    private static readonly HashSet<string> Meeting = new() { "teams", "ms-teams", "zoom", "webex", "ciscocollabhost", "skype" };
    private static readonly HashSet<string> Games = new()
    {
        "robloxplayerbeta", "valorant", "valorant-win64-shipping", "leagueoflegends", "league of legends", "cs2", "dota2", "gta5",
        "fortniteclient-win64-shipping", "minecraft", "javaw", "rocketleague", "overwatch", "apex_legends", "r5apex", "eldenring",
        "genshinimpact", "bgmi", "pubg", "tslgame", "cod", "fifa", "eafc", "rainbowsix",
    };

    private static string Classify(string name, string path, string title)
    {
        string tl = title.ToLowerInvariant();
        if (Games.Contains(name) && (name != "javaw" || tl.Contains("minecraft"))) return "game";
        if (path.Contains(@"steamapps\common") || path.Contains(@"\epic games\") || path.Contains(@"\riot games\") ||
            path.Contains(@"\xboxgames\") || path.Contains(@"\gog games\")) return "game";
        if (Meeting.Contains(name)) return "meeting";
        if (Trading.Contains(name) || (name is "terminal" or "terminal64" && !path.Contains("windowsapps")) || path.Contains("metatrader")) return "trading";
        if (Ai.Contains(name)) return "ai";
        if (Code.Contains(name)) return "code";
        if (Read.Contains(name)) return "read";
        if (Video.Contains(name)) return "video";
        if (Browsers.Contains(name))
        {
            if (tl.Contains("google meet") || tl.StartsWith("meet -") || tl.Contains("zoom meeting")) return "meeting";
            if (tl.Contains("youtube") || tl.Contains("netflix") || tl.Contains("prime video") || tl.Contains("disney+") ||
                tl.Contains("hotstar") || tl.Contains("jiocinema") || tl.Contains("twitch") || tl.Contains("crunchyroll")) return "video";
            if (tl.Contains(".pdf")) return "read";
            if (AiSites.Any(tl.Contains)) return "ai";
            if (TradingSites.Any(tl.Contains)) return "trading";
            if (tl.Contains("github") || tl.Contains("stack overflow") || tl.Contains("codepen") || tl.Contains("replit")) return "code";
            return "browser";
        }
        return "";
    }

    // ---------- copy spree ----------

    private readonly List<double> _copies = new();

    private void OnClipboard()
    {
        double t = Now;
        _copies.RemoveAll(c => t - c > 8);
        _copies.Add(t);
        if (_copies.Count >= 4) { _copies.Clear(); Pulse("juggle"); }
    }

    // ---------- typing: a count of key presses, and a 4-letter window to spot "meow" ----------

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    private IntPtr _hook;
    private HookProc? _proc;               // kept alive: the hook calls it
    private readonly double[] _keys = new double[64];
    private int _keyCount;
    private string _last4 = "";

    private void StartKeyboard()
    {
        _proc = OnKey;
        _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, GetModuleHandle(null), 0);
    }

    private void StopKeyboard()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        // Only key-downs, and only counted; letters go into a 4-letter window that is checked for "meow" and nothing else.
        if (code >= 0 && (wParam == 0x0100 || wParam == 0x0104))
        {
            int vk = Marshal.ReadInt32(lParam);
            _keys[_keyCount++ % _keys.Length] = Now;
            if (vk == 0x2C) { LastPrintScreen = DateTime.Now; Pulse("screenshot"); } // Print Screen
            if (vk is >= 'A' and <= 'Z')
            {
                _last4 = (_last4 + (char)vk).Length > 4 ? (_last4 + (char)vk)[^4..] : _last4 + (char)vk;
                if (_last4 == "MEOW") { _last4 = ""; Pulse("meow"); }
            }
            else _last4 = "";
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Key presses per second over the last three seconds.</summary>
    private double TypingRate()
    {
        double t = Now, n = 0;
        foreach (var k in _keys) if (k > 0 && t - k < 3) n++;
        return n / 3;
    }

    public void SetTyping(bool on)
    {
        if (on && _hook == IntPtr.Zero) StartKeyboard();
        else if (!on) StopKeyboard();
    }

    public void Dispose()
    {
        StopKeyboard();
        _w.Clipboard.Changed -= OnClipboard;
    }
}
