namespace Cacad;

/// <summary>The settings window: dark, one scrolling page of sections. Every change applies live.</summary>
public sealed class SettingsForm : Form
{
    public static SettingsForm? Instance { get; private set; }

    private readonly SettingsStore _s;
    private readonly IslandWindow _island;
    private readonly Panel _scroll;
    private const int ContentW = 520;
    private int _y = 20;

    public SettingsForm(SettingsStore settings, IslandWindow island)
    {
        _s = settings;
        _island = island;
        Instance = this;

        Text = "cacad settings";
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
        Font = Ui.Font(9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(ContentW + 56, 640);
        AutoScaleMode = AutoScaleMode.Dpi;

        _scroll = new SteadyScroll { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg };
        Controls.Add(_scroll);
        Build();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.DarkTitleBar(Handle);
    }

    private void Changed()
    {
        _island.ApplySettings();
        _s.Save();
    }

    // ---------- layout helpers ----------

    private void Header(string text)
    {
        _y += 8;
        Add(new Label { Text = text.ToUpperInvariant(), ForeColor = Ui.Dim, Font = Ui.Font(8.5f, FontStyle.Bold), AutoSize = true }, 28, _y);
        _y += 26;
    }

    private void Add(Control c, int x, int y)
    {
        c.Location = new Point(x, y);
        _scroll.Controls.Add(c);
    }

    /// <summary>A row: title and description on the left, the control on the right (or below when wide).</summary>
    private void Row(string title, string desc, Control control, bool below = false)
    {
        var card = new Panel { BackColor = Ui.Card, Width = ContentW, Location = new Point(28, _y) };
        int textW = below ? ContentW - 36 : ContentW - control.Width - 52;
        var t = new Label { Text = title, Font = Ui.Font(9.75f, FontStyle.Bold), ForeColor = Ui.Text, AutoSize = false, Width = textW, Height = 20, Location = new Point(18, 14), BackColor = Ui.Card };
        var d = new Label { Text = desc, Font = Ui.Font(8.75f), ForeColor = Ui.Dim, AutoSize = false, Width = textW, Location = new Point(18, 36), BackColor = Ui.Card };
        d.Height = string.IsNullOrEmpty(desc) ? 0 : TextRenderer.MeasureText(desc, d.Font, new Size(textW, 999), TextFormatFlags.WordBreak).Height;
        card.Controls.Add(t);
        card.Controls.Add(d);
        int textBottom = 36 + d.Height;
        if (below)
        {
            control.Width = ContentW - 36;
            control.Location = new Point(18, textBottom + 10);
            card.Controls.Add(control);
            card.Height = control.Bottom + 16;
            control.SizeChanged += (_, _) => card.Height = control.Bottom + 16;
        }
        else
        {
            card.Height = Math.Max(textBottom + 14, control.Height + 28);
            control.Location = new Point(ContentW - control.Width - 18, (card.Height - control.Height) / 2);
            card.Controls.Add(control);
        }
        control.BackColor = Ui.Card;
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Ui.Line);
            using var path = Ui.Round(new RectangleF(0.5f, 0.5f, card.Width - 1, card.Height - 1), 12);
            e.Graphics.DrawPath(pen, path);
        };
        card.Region = null;
        _scroll.Controls.Add(card);
        _y = card.Bottom + 8;
        int last = card.Height;
        card.SizeChanged += (_, _) => { Shift(card, last); last = card.Height; };
    }

    /// <summary>A card grew (its segmented control wrapped): move everything below it down.</summary>
    private void Shift(Control card, int oldHeight)
    {
        int delta = card.Height - oldHeight;
        if (delta == 0) return;
        foreach (Control c in _scroll.Controls)
            if (c != card && c.Top > card.Top) c.Top += delta;
    }

    private Segmented Seg(IEnumerable<(string, string)> items, string value, Action<string> set)
    {
        var s = new Segmented(items, value) { Width = 230, Height = 28 };
        s.Changed += v => { set(v); Changed(); };
        return s;
    }

    private PreviewPicker Picker(PreviewPicker.Kind kind, IEnumerable<(string, string, string?)> items, string value, Func<string> themeDefault, Action<string> set)
    {
        var p = new PreviewPicker(kind, items, value, themeDefault) { Width = 230, Height = 100 };
        p.Changed += v => { set(v); Changed(); };
        return p;
    }

    private Toggle Switch(bool value, Action<bool> set)
    {
        var t = new Toggle(value);
        t.Changed += v => { set(v); Changed(); };
        return t;
    }

    private ValueSlider Slider(int min, int max, int step, int value, Func<int, string> fmt, Action<int> set)
    {
        var s = new ValueSlider(min, max, step, value, fmt) { Width = 230 };
        s.Changed += v => { set(v); Changed(); };
        return s;
    }

    // ---------- content ----------

    private void Build()
    {
        var title = new Label { Text = "cacad", Font = Ui.Font(16f, FontStyle.Bold), ForeColor = Ui.Text, AutoSize = true };
        Add(title, 26, _y);
        Add(new Label { Text = "The Dynamic Island for your PC. Rest the pointer on it to open it.", Font = Ui.Font(9f), ForeColor = Ui.Dim, AutoSize = true }, 28, _y + 34);
        _y += 70;

        Header("Appearance");
        Row("Shape", "A notch that hangs from the top edge, or a floating island just under it.",
            Seg(new[] { ("notch", "Notch"), ("island", "Island") }, _s.Shape, v => _s.Shape = v));
        Row("Surface", "The island's colour. The soft themes each bring their own accent, edge and a little ambience.",
            Seg(Themes.All.Select(t => (t.Id, t.Name)), _s.ThemeId, v => _s.ThemeId = v), below: true);
        // The Custom surface: an image of your own, or a gradient of two colours.
        Button SmallButton(string text, int width)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Ui.Text, BackColor = Ui.Knob, Width = width, Height = 28 };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }
        void CustomChanged() { CustomSurface.Reset(); if (_s.ThemeId != "custom") _s.ThemeId = "custom"; Changed(); }
        var pick = SmallButton(string.IsNullOrEmpty(_s.CustomImage) ? "Choose image…" : Path.GetFileName(_s.CustomImage), 150);
        pick.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = "Picture for the island", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|All files|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _s.CustomImage = dlg.FileName;
            pick.Text = Path.GetFileName(dlg.FileName);
            CustomChanged();
        };
        var clear = SmallButton("Use colours", 110);
        clear.Click += (_, _) => { _s.CustomImage = ""; pick.Text = "Choose image…"; CustomChanged(); };
        var imageRow = new FlowLayoutPanel { Width = 270, Height = 30, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Ui.Card };
        imageRow.Controls.Add(pick);
        imageRow.Controls.Add(clear);
        Row("Custom surface", "Pick Custom above, then an image of your own (cropped to fill the island), or two colours for a gradient.", imageRow);

        Button Swatch(Func<string> get, Action<string> set)
        {
            var b = SmallButton("", 60);
            void Paint() => b.BackColor = ColorTranslator.FromHtml(get());
            Paint();
            b.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = ColorTranslator.FromHtml(get()) };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                set($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
                Paint();
                CustomChanged();
            };
            return b;
        }
        var colours = new FlowLayoutPanel { Width = 130, Height = 30, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Ui.Card };
        colours.Controls.Add(Swatch(() => _s.CustomColor1, v => _s.CustomColor1 = v));
        colours.Controls.Add(Swatch(() => _s.CustomColor2, v => _s.CustomColor2 = v));
        Row("Gradient colours", "Used when no image is set.", colours);
        Row("Dim", "Darkens the surface so the text stays easy to read.", Slider(0, 80, 5, _s.CustomDim, v => $"{v}%", v => { _s.CustomDim = v; CustomSurface.Reset(); }));
        Row("Blur", "Softens the image.", Slider(0, 30, 2, _s.CustomBlur, v => v == 0 ? "Off" : $"{v}", v => { _s.CustomBlur = v; CustomSurface.Reset(); }));

        // Theme default: name the style the current surface picks, so the card says what it will look like.
        string ThemeBar() => ProgressBars.ThemeDefault(_s.ThemeId);
        var barItems = new List<(string, string, string?)> { ("", "Theme", ProgressBars.Styles.First(b => b.id == ThemeBar()).name) };
        barItems.AddRange(ProgressBars.Styles.Select(b => (b.id, b.name, (string?)null)));
        Row("Progress bar", "How the music card shows how far the song has played. Hover a card to try it.",
            Picker(PreviewPicker.Kind.ProgressBar, barItems, _s.BarStyle, ThemeBar, v => _s.BarStyle = v), below: true);
        Row("Visualizer", "What plays beside the artwork on the pill while music plays.",
            Picker(PreviewPicker.Kind.Visualizer, Visualizers.Styles.Select(v => (v.id, v.name, (string?)null)), _s.Visualizer, () => "pixels", v => _s.Visualizer = v), below: true);
        Row("Background", "Behind the music card: the cover's colours, or the plain surface.",
            Seg(new[] { ("album", "Album colours"), ("plain", "Plain") }, _s.AlbumBackground ? "album" : "plain", v => _s.AlbumBackground = v == "album"));

        Header("Size and motion");
        Row("Pill width", "", Slider(140, 320, 2, (int)_s.PillWidth, v => $"{v} px", v => _s.PillWidth = v));
        Row("Pill height", "", Slider(28, 48, 1, (int)_s.PillHeight, v => $"{v} px", v => _s.PillHeight = v));
        Row("Card width", "", Slider(360, 520, 4, (int)_s.CardWidth, v => $"{v} px", v => _s.CardWidth = v));
        Row("Notch flare", "How far the notch's top corners curve out into the edge.", Slider(0, 20, 1, (int)_s.CornerFlare, v => $"{v} px", v => _s.CornerFlare = v));
        Row("Island gap", "The space above the floating island.", Slider(0, 30, 1, _s.TopOffset, v => $"{v} px", v => _s.TopOffset = v));
        Row("Open delay", "How long the pointer rests before the card opens.", Slider(0, 1000, 25, _s.OpenDelayMs, v => $"{v} ms", v => _s.OpenDelayMs = v));
        Row("Close delay", "", Slider(0, 1500, 20, _s.CloseDelayMs, v => $"{v} ms", v => _s.CloseDelayMs = v));
        Row("Animation speed", "", Slider(50, 200, 10, _s.AnimationSpeedPct, v => $"{v}%", v => _s.AnimationSpeedPct = v));

        Header("Behaviour");
        Row("Clock in the pill", "Shown while nothing is playing.", Switch(_s.ShowClock, v => _s.ShowClock = v));
        Row("Clock face", "How the time looks in the pill. You can also scroll on the pill to flip through them.",
            Picker(PreviewPicker.Kind.Clock, ClockFaces.Styles.Select(s => (s.id, s.name, (string?)null)), ClockFaces.Normalize(_s.PillClockStyle), () => _s.ThemeId, v => _s.PillClockStyle = v), below: true);
        Row("24-hour clock", "", Switch(_s.Clock24h, v => _s.Clock24h = v));
        Row("Hide until hover", "Keep the top edge clear; the island appears when the pointer reaches it.", Switch(_s.HideUntilHover, v => _s.HideUntilHover = v));
        Row("Hide in fullscreen", "Get out of the way of games, videos and presentations.", Switch(_s.HideInFullscreen, v => _s.HideInFullscreen = v));
        Row("One mute key", "Ctrl+Alt+M mutes every microphone at once, in every app.", Switch(_s.MicMuteHotkey, v => _s.MicMuteHotkey = v));
        Row("Launch at startup", "", Switch(StartupRegistrar.IsEnabled, v => { StartupRegistrar.SetEnabled(v); _s.LaunchAtStartup = v; }));

        Header("Live");
        Row("Downloads", "A download in your Downloads folder shows in the pill; tap the finished toast to open it.", Switch(_s.LiveDownloads, v => _s.LiveDownloads = v));
        Row("Call timer", "While an app uses your microphone (Zoom, Teams, Discord…), the pill shows the call and its length.", Switch(_s.CallTimer, v => _s.CallTimer = v));
        Row("Charging", "A green surge across the pill when you plug in.", Switch(_s.ChargingAnimation, v => _s.ChargingAnimation = v));
        Row("Volume and brightness", "Changing the volume or brightness shows a bar in the pill.", Switch(_s.SystemHud, v => _s.SystemHud = v));
        Row("Hide Windows' volume pop-up", "Windows 10 only: its own pop-up hides while cacad shows the bar (it comes back when cacad quits).", Switch(_s.HideWindowsOsd, v => _s.HideWindowsOsd = v));
        Row("Caps Lock and Num Lock", "A moment in the pill when you switch them.", Switch(_s.LockPeeks, v => _s.LockPeeks = v));
        Row("Wi-Fi", "The network you joined, or that you went offline.", Switch(_s.NetworkPeeks, v => _s.NetworkPeeks = v));
        Row("Devices", "Bluetooth headphones and other devices connecting, and USB drives plugged in (click to open).", Switch(_s.DevicePeeks, v => _s.DevicePeeks = v));
        Row("Screenshots", "After a screenshot, its thumbnail in the pill: click to open, right-click to save.", Switch(_s.ScreenshotPeeks, v => _s.ScreenshotPeeks = v));
        Row("Weather", "A Weather tab and a heads-up before rain. From open-meteo.com: only your location (or the city below) is sent.", Switch(_s.Weather, v => { _s.Weather = v; IslandWindow.Instance?.Weather.Refresh(); }));
        var city = new Button { Text = string.IsNullOrEmpty(_s.WeatherCity) ? "My location" : _s.WeatherCity, FlatStyle = FlatStyle.Flat, ForeColor = Ui.Text, BackColor = Ui.Knob, Width = 140, Height = 28 };
        city.FlatAppearance.BorderSize = 0;
        city.Click += (_, _) =>
        {
            string? v = InputDialog.Ask("Weather for", "A city, e.g. Hyderabad (leave empty to use Windows' location)", Themes.Get(_s.ThemeId));
            if (v == null) return;
            _s.WeatherCity = v.Trim();
            city.Text = _s.WeatherCity.Length == 0 ? "My location" : _s.WeatherCity;
            IslandWindow.Instance?.Weather.Refresh();
            Changed();
        };
        Row("Weather place", "Windows' location needs Settings › Privacy › Location turned on for desktop apps.", city);
        Row("Temperature", "", Seg(new[] { ("c", "°C"), ("f", "°F") }, _s.WeatherFahrenheit ? "f" : "c", v => { _s.WeatherFahrenheit = v == "f"; IslandWindow.Instance?.Weather.Refresh(); }));
        Row("Notifications", "Messages from your apps (WhatsApp, Discord, Mail…) show on the island for a few seconds. Click one to open the notification centre; right-click to mute that app.", Switch(_s.Notifications, v => _s.Notifications = v));
        Row("Hide message text", "Show who it is from, but not what it says.", Switch(_s.NotificationsPrivate, v => _s.NotificationsPrivate = v));
        Row("Smart clipboard", "Copy a message with a code, a colour, an email or an address, and the pill offers what to do with it.", Switch(_s.SmartPeeks, v => _s.SmartPeeks = v));
        Row("Lyrics in the pill", "While a song plays, the line being sung shows in the pill. Lyrics come from lrclib.net (only the song's title, artist and length are sent) and are saved on your PC.", Switch(_s.Lyrics, v => _s.Lyrics = v));
        Row("Visualizer follows the music", "The pill's visualizer listens to the sound your PC plays, instead of a pattern.", Switch(_s.RealVisualizer, v => _s.RealVisualizer = v));
        Row("Focus session", "Right-click the island › Focus. A ring in the pill counts down, and a chime ends it.", Slider(5, 90, 5, _s.FocusMinutes, v => $"{v} min", v => _s.FocusMinutes = v));

        Header("Companion");
        Row("Companion", "A little friend on the island that reacts to what you're doing: music, coding, calls, downloads, the battery, the time of day…",
            Switch(_s.Companion, v => _s.Companion = v));
        (string, string, string) Now() => (_s.CompanionSpecies, _s.CompanionSkin, _s.CompanionStyle);
        var who = Picker(PreviewPicker.Kind.Companion, CatArt.Species.Select(s => (s.Id, s.Name, (string?)null)), _s.CompanionSpecies,
            () => _s.CompanionSkin, v => _s.CompanionSpecies = v);
        var style = Picker(PreviewPicker.Kind.Style, CatArt.Styles.Select(s => (s.Id, s.Name, (string?)null)), _s.CompanionStyle,
            () => _s.CompanionSpecies, v => _s.CompanionStyle = v);
        var skin = Picker(PreviewPicker.Kind.Skin, CatArt.Skins.Select(s => (s.Id, s.Name, (string?)null)), _s.CompanionSkin,
            () => _s.CompanionSpecies, v => _s.CompanionSkin = v);
        who.CompanionNow = style.CompanionNow = skin.CompanionNow = Now;
        Row("Who", "Every companion knows every reaction.", who, below: true);
        Row("Style", "How your companion is drawn: pixel art, anime, a flat ink toon, a cute kawaii mascot, or a hand-drawn sketch. Every companion has every style.", style, below: true);
        Row("Skin", "Natural is each companion's own coat; the others fit any of them, in every style.", skin, below: true);
        // The companion game: level, happiness, and the hats its level has unlocked.
        int level = Pet.LevelFor(_s.PetXp);
        double happy = Math.Clamp(_s.PetHappy - (DateTime.Now - _s.PetFedAt).TotalHours * 3, 0, 100);
        string next = Pet.Hats.FirstOrDefault(h => h.Level > level) is { } nh ? $" Next hat at level {nh.Level}: {nh.Name}." : "";
        var stats = new Label
        {
            AutoSize = false, Width = 230, Height = 56, ForeColor = Ui.Text, BackColor = Color.Transparent, Font = Ui.Font(10.5f, FontStyle.Bold),
            Text = $"Level {level}  ·  {_s.PetXp - Pet.Need(level)}/{Pet.Need(level + 1) - Pet.Need(level)} XP\nHappiness {happy:0}%",
            TextAlign = ContentAlignment.MiddleRight,
        };
        stats.Height = TextRenderer.MeasureText(stats.Text, stats.Font).Height + 8;   // fit both lines at any display scale
        Row("Level and happiness", "Feed it by finishing focus sessions (most), timers and to-dos; petting it in the card cheers it up. It gets hungry if it isn't fed for a day." + next, stats);
        var hats = Pet.Hats.Where(h => h.Level <= level).Select(h => (h.Id, h.Name)).ToArray();
        Row("Hat", hats.Length > 1 ? "Hats unlock as it levels up." : "Reach level 2 to unlock the first hat.",
            Seg(hats, hats.Any(h => h.Id == _s.PetHat) ? _s.PetHat : "", v => _s.PetHat = v), below: true);
        Row("Typing reactions", "Counts how fast you type (never what) for the typing pose, and spots one secret word.",
            Switch(_s.CompanionTyping, v => { _s.CompanionTyping = v; IslandWindow.Instance?.Companion?.SetTyping(v); }));
        foreach (var (id, name, about) in Companion.Groups)
            Row(name, about, Switch(!_s.CompanionOff.Contains(id), v =>
            {
                _s.CompanionOff.Remove(id);
                if (!v) _s.CompanionOff.Add(id);
            }));
        var bday = new Button { Text = string.IsNullOrEmpty(_s.Birthday) ? "Set" : _s.Birthday, FlatStyle = FlatStyle.Flat, ForeColor = Ui.Text, BackColor = Ui.Knob, Width = 90, Height = 28 };
        bday.FlatAppearance.BorderSize = 0;
        bday.Click += (_, _) =>
        {
            string? v = InputDialog.Ask("Your birthday", "Month-day, e.g. 03-27 (leave empty to clear)", Themes.Get(_s.ThemeId));
            if (v == null) return;
            v = v.Trim();
            if (v.Length == 0) _s.Birthday = "";
            else if (DateTime.TryParseExact(v, new[] { "MM-dd", "M-d", "dd/MM", "d/M" }, null, System.Globalization.DateTimeStyles.None, out var d)) _s.Birthday = d.ToString("MM-dd");
            else return;
            bday.Text = string.IsNullOrEmpty(_s.Birthday) ? "Set" : _s.Birthday;
            Changed();
        };
        Row("Birthday", "The cat wears a party hat on the day.", bday);

        Header("Gestures");
        Row("On the pill", "Swipe sideways (touchpad): next or previous track · Middle-click: play or pause · Hold: mute the microphone · Scroll: volume while music plays, clock faces otherwise.", new Label { Width = 1, Height = 1 });

        Header("Shortcuts");
        Row("Show or hide the island", "", KeyLabel("Ctrl + Alt + N"));
        Row("Open settings", "", KeyLabel("Ctrl + Alt + S"));
        Row("Mute microphone", "", KeyLabel("Ctrl + Alt + M"));

        Header("Performance");
        Row("C3E · surface engine",
            "Makes the surfaces cheaper to draw without changing how they look. Glass looks at the screen behind it less often while nothing changes " +
            "(and right away when something does); the animated surfaces render their next frame on a background thread; the glass worker reuses " +
            "its buffers instead of allocating new ones.",
            Switch(_s.EngineC3E, v => _s.EngineC3E = v));
        Row("Z3R · app engine",
            "Makes the whole app lighter. While only the companion moves, just its patch is redrawn, not the whole island. After 20 seconds of " +
            "nothing happening, cacad rests in Windows' efficiency mode at a lower priority and hands unused memory back, and wakes the instant " +
            "you hover, a song plays or anything shows.",
            Switch(_s.EngineZ3R, v => _s.EngineZ3R = v));
        var live = new Label { AutoSize = false, Width = 300, Height = 22, ForeColor = Ui.Text, BackColor = Color.Transparent, Font = Ui.Font(9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, Text = Z3R.Stats() };
        var liveTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        liveTimer.Tick += (_, _) => { if (Visible) live.Text = Z3R.Stats(); };
        liveTimer.Start();
        live.Disposed += (_, _) => liveTimer.Dispose();
        Row("Right now", "cacad's own use of your PC, live.", live);

        Header("Updates");
        Row("Updates", "New versions come from cacad's GitHub releases. Automatic downloads them and installs the next time your PC is idle (a quick restart; your settings stay).",
            Seg(new[] { ("auto", "Automatic"), ("notify", "Just tell me"), ("off", "Off") }, _s.Updates, v => _s.Updates = v), below: true);
        var check = new Button { Text = "Check now", FlatStyle = FlatStyle.Flat, ForeColor = Ui.Text, BackColor = Ui.Knob, Width = 110, Height = 28, Enabled = Updater.Enabled };
        check.FlatAppearance.BorderSize = 0;
        var up = Updater.Instance;
        string Status() => up?.Status ?? $"Version {Updater.CurrentText}.";
        check.Click += (_, _) => { if (up != null) _ = up.Check(true); };
        Row($"cacad {Updater.CurrentText}", Status(), check);
        // Keep the status line (the row's description) current while Settings is open.
        var statusLabel = _scroll.Controls.OfType<Panel>().LastOrDefault()?.Controls.OfType<Label>().ElementAtOrDefault(1);
        if (statusLabel != null) statusLabel.AutoEllipsis = true;
        if (up != null && statusLabel != null)
        {
            void Refresh() { if (!statusLabel.IsDisposed) statusLabel.Text = up.Status; }
            up.Changed += () => { if (statusLabel.IsHandleCreated) statusLabel.BeginInvoke(Refresh); };
        }

        Header("Data");
        var open = new Button { Text = "Open folder", FlatStyle = FlatStyle.Flat, ForeColor = Ui.Text, BackColor = Ui.Knob, Width = 110, Height = 28 };
        open.FlatAppearance.BorderSize = 0;
        open.Click += (_, _) => Shell.Open(SettingsStore.DataDir);
        Row("Your data stays on this PC", "Settings, clipboard history, notes and the shelf live in your AppData folder. cacad only goes online for lyrics (lrclib.net), weather (open-meteo.com) and updates (github.com).", open);

        _scroll.Controls.Add(new Panel { Location = new Point(0, _y + 20), Size = new Size(1, 1), BackColor = Ui.Bg });
    }

    private static Label KeyLabel(string keys) => new()
    {
        Text = keys,
        AutoSize = false,
        Size = new Size(130, 26),
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Ui.Text,
        BackColor = Ui.Knob,
        Font = Ui.Font(9f),
    };

    public void ShowAndFocus()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            _s.Save();
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}

internal static class StartupRegistrar
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue("cacad") != null;
            }
            catch { return false; }
        }
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;
            if (enable) key.SetValue("cacad", $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"");
            else key.DeleteValue("cacad", false);
        }
        catch { /* non-fatal */ }
    }
}

/// <summary>A scrolling panel that stays where you left it. WinForms scrolls a panel to bring the focused control
/// into view, so clicking back into the window (or on a card) used to jump the list to the top.</summary>
internal sealed class SteadyScroll : Panel
{
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
}
