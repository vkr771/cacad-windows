using System.Diagnostics;
using SkiaSharp;

namespace Cacad;

/// <summary>The island: one layered, click-through-where-transparent window at the top centre of the main screen.
/// Draws with Skia into a DIB and presents it with UpdateLayeredWindow.</summary>
public sealed partial class IslandWindow : Form, IIslandHost
{
    private enum Mode { Pill, Card, Toast }

    // ---------- services ----------
    public SettingsStore Settings { get; }
    public MediaService Media { get; } = new();
    public AudioService Audio { get; } = new();
    public ClipboardService Clipboard { get; } = new();
    public SystemStats Stats { get; } = new();
    public BrightnessService Brightness { get; } = new();
    public RadioService Radios { get; } = new();
    public Rectangle MonitorBounds => Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

    // ---------- panels ----------
    private readonly List<IslandPanel> _panels;
    private IslandPanel _panel;     // the one asked for
    private IslandPanel _shown;     // the one on screen (swaps when the old one has faded out)

    // ---------- springs (DIPs) ----------
    private readonly Spring _w, _h, _open, _fade, _notch, _top;
    private Mode _mode = Mode.Pill;
    private Theme _theme;

    // ---------- timing ----------
    private VsyncPump? _vsync;
    private readonly System.Windows.Forms.Timer _enter = new();
    private readonly System.Windows.Forms.Timer _leave = new();
    private readonly System.Windows.Forms.Timer _toastTimer = new();
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 250 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame, _lastDraw, _t;
    private bool _dirty = true;

    // ---------- pointer ----------
    private SKPoint? _pointer;      // island coordinates: x = 0 at the centre, y = 0 at the island's top
    private bool _captured;         // the shown panel owns the drag
    private int _hold;              // dialogs and drags keep the card open
    private bool _dragOver;
    private bool _trackingLeave;

    // ---------- state shown in the pill ----------
    private string _toastIcon = "check", _toastText = "";
    private SKColor? _toastColor;
    private float _toastK, _musicK;
    private bool _micMuted;
    private bool _hiddenByToggle, _hiddenFullscreen;
    private int _pollCount, _lastMinute = -1;
    private bool _micPolling;
    private int _shapeGen;

    private float _scale = 1;

    public static IslandWindow? Instance { get; private set; }
    public Features? Features { get; private set; }
    public Notifications? Notifier { get; private set; }
    public SystemPeeks? Peeks { get; private set; }
    public WeatherService Weather { get; }
    public bool CardOpen => _mode == Mode.Card;
    /// <summary>The pixel cat: its moods and reactions.</summary>
    public Companion? Companion { get; private set; }

    /// <summary>The cat's grid cell size in the pill and the card (DIPs).</summary>
    private const float CatPx = 1.55f;

    /// <summary>The room the cat's current pose takes (DIPs), plus a small gap; it follows the pose, so a cat with a
    /// prop beside it gets a little more.</summary>
    private float CatSlot => Settings.Companion && Companion != null ? CatArt.Measure(Companion.Pose, CatPx) + 6 : 0;

    /// <summary>The pill shows the clock (not music): the cat then sits right beside the time.</summary>
    private bool PillShowsClock => !(Media.HasSession && Media.IsPlaying);
    /// <summary>The sound the PC is playing, as four frequency bands, for the pill's visualizer.</summary>
    public AudioLoopback Loopback { get; } = new();

    public IslandWindow(SettingsStore settings)
    {
        Instance = this;
        Settings = settings;
        settings.PillClockStyle = ClockFaces.Normalize(settings.PillClockStyle);
        if (settings.ThemeId == "wallpaper") settings.ThemeId = "custom";
        settings.ThemeId = Themes.Renamed(settings.ThemeId) ?? "black";
        CustomSurface.Settings = settings;
        Weather = new WeatherService(settings);
        Weather.Changed += OnWeather;
        ClockFaces.Weather = () => Weather.Current;
        _theme = Themes.Get(settings.ThemeId);

        _panels = new List<IslandPanel>
        {
            new MusicPanel(this),
            new LyricsPanel(this),
            new ClockPanel(this),
            new WeatherPanel(this),
            new ShelfPanel(this),
            new ClipboardPanel(this),
            new NotificationsPanel(this),
            new NotesPanel(this),
            new WidgetsPanel(this),
            new ControlsPanel(this),
            new LauncherPanel(this),
        };
        _panel = _shown = _panels.FirstOrDefault(p => p.Id == settings.LastPanel) ?? _panels[0];

        bool notch = settings.Shape != "island";
        _w = new Spring(PillW, SpringSpec.Close, 0.05);
        _h = new Spring(PillH, SpringSpec.Close, 0.05);
        _open = new Spring(0, SpringSpec.Fade, 0.002);
        _fade = new Spring(1, SpringSpec.Panel, 0.002);
        _notch = new Spring(notch ? 100 : 0, SpringSpec.Close, 0.05);
        _top = new Spring(notch ? 0 : settings.TopOffset, SpringSpec.Close, 0.05);

        Text = "cacad";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AllowDrop = true;
        Size = new Size(40, 40);

        // A tappable toast holds the pill closed: hovering it to tap must not open the card.
        _enter.Tick += (_, _) => { _enter.Stop(); if (_pointer != null && _mode != Mode.Card && !(_mode == Mode.Toast && _toastAction != null)) OpenFromPill(); };
        WireGestures();
        _leave.Tick += (_, _) => { _leave.Stop(); MaybeClose(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toastAction = null; if (_mode == Mode.Toast) SetMode(Mode.Pill); };
        _poll.Tick += (_, _) => UiProfile.Time("poll", OnPoll);
        Media.Changed += Invalidate;
        WireLyrics();
        _poll.Start();
        _hoverWatch.Tick += (_, _) => UiProfile.Time("hover", WatchHover);
        _hoverWatch.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED;
            return cp;
        }
    }

    // ---------- geometry ----------

    /// <summary>The resting pill's width: your setting, plus room for the cat. Beside the clock the cat only needs the
    /// pill to fit cat + gap + time (never narrower than your setting); with music it takes its own slot at the end.</summary>
    /// Never wider than the card: the window is sized for the card, so anything wider would lose its ends.
    private float PillW => Math.Min(PillWide, CardW);

    private float PillWide
    {
        get
        {
            float baseW = (float)Settings.PillWidth, slot = CatSlot;
            if (slot <= 0) return PillShowsClock ? baseW : Math.Max(baseW, LyricPillW);
            if (!PillShowsClock) return Math.Max(baseW, LyricPillW) + slot;
            float clockW = Settings.ShowClock ? ClockFaces.Width(Settings.PillClockStyle, PillH, Settings.Clock24h) : 0;
            return Math.Max(baseW, slot + 1 + clockW + 28);
        }
    }
    private float PillH => (float)Settings.PillHeight;
    private float CardW => (float)Settings.CardWidth;
    private float Flare => (float)Settings.CornerFlare;
    private float BoxW => CardW + 2 * (Flare + 10);
    private float BoxH => _panels.Max(p => p.Height) + Math.Max(0, Settings.TopOffset) + 14;
    private float Speed => Math.Clamp(Settings.AnimationSpeedPct, 25, 400) / 100f;
    private (double k, double c) Spec((double k, double c) s) => SpringSpec.Scaled(s, Speed);

    private void LayoutWindow()
    {
        _scale = DeviceDpi / 96f;
        var mon = MonitorBounds;
        int w = (int)Math.Ceiling(BoxW * _scale), h = (int)Math.Ceiling(BoxH * _scale);
        int x = mon.Left + (mon.Width - w) / 2, y = mon.Top;
        Bounds = new Rectangle(x, y, w, h);
        EnsureBitmap(w, h);
        _fullRedraw = true;
        if (IsHandleCreated)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Invalidate();
    }

    /// <summary>The island's top edge (DIPs from the screen top); a notch sits on the edge exactly.</summary>
    private float IslandTop => _notch.Target == 100 && _top.Value < 1 ? 0 : (float)_top.Value;

    private SKPoint ToIsland(Point client) => new(client.X / _scale - BoxW / 2, client.Y / _scale - IslandTop);
    private SKPoint ToCard(SKPoint p) => new(p.X + CardW / 2, p.Y);

    private bool InsideIsland(SKPoint p)
    {
        float extra = _notch.Value > 50 ? Flare : 0;
        return p.X >= -(float)_w.Value / 2 - 4 - extra && p.X <= (float)_w.Value / 2 + 4 + extra && p.Y >= -IslandTop && p.Y <= (float)_h.Value + 4;
    }

    // ---------- settings ----------

    public void ApplySettings()
    {
        C3E.On = Settings.EngineC3E;
        Z3R.On = Settings.EngineZ3R;
        Z3R.Apply();
        _theme = Themes.Get(Settings.ThemeId);
        bool notch = Settings.Shape != "island";
        _notch.To(notch ? 100 : 0, Spec(SpringSpec.Close));
        _top.To(notch ? 0 : Settings.TopOffset, Spec(SpringSpec.Close));
        if (_mode == Mode.Pill) { _w.To(PillW, Spec(SpringSpec.Close)); _h.To(PillH, Spec(SpringSpec.Close)); }
        if (_mode == Mode.Card) { _w.To(CardW, Spec(SpringSpec.Open)); _h.To(_shown.Height, Spec(SpringSpec.Open)); }
        UpdateGlassTimer();
        LayoutWindow();
        RegisterHotkeys();
    }

    // ---------- modes ----------

    private void SetMode(Mode m)
    {
        if (_hiddenByToggle && m != Mode.Pill) return;
        var prev = _mode;
        _mode = m;
        switch (m)
        {
            case Mode.Card:
                _w.To(CardW, Spec(SpringSpec.Open));
                _h.To(_shown.Height, Spec(SpringSpec.Open));
                _open.To(1, Spec(SpringSpec.Fade));
                if (prev != Mode.Card) _shown.Shown();
                break;
            case Mode.Toast:
                using (var font = Fonts.Med(13))
                    { float lo = Math.Max(PillW, 220); _w.To(Math.Clamp(G.Measure(font, _toastText) + (_toastAction != null ? 90 : 72) + CatSlot, lo, Math.Max(lo, CardW)), Spec(SpringSpec.Close)); }
                _h.To(PillH, Spec(SpringSpec.Close));
                _open.To(0, Spec(SpringSpec.Fade));
                break;
            default:
                _w.To(PillW, Spec(SpringSpec.Close));
                _h.To(PillH, Spec(SpringSpec.Close));
                _open.To(0, Spec(SpringSpec.Fade));
                if (prev == Mode.Card)
                {
                    _shown.Hidden();
                    Settings.LastPanel = _shown.Id;
                }
                break;
        }
        Invalidate();
    }

    /// <summary>Open the card on the tab that matches the pill: Music while it plays, otherwise the Clock in the
    /// same face the pill was showing. Any other tab the user was on (shelf, clipboard…) is kept.</summary>
    private void OpenFromPill()
    {
        IslandPanel? match = Media.IsPlaying ? _panels.FirstOrDefault(p => p is MusicPanel)
            : Settings.ShowClock ? _panels.FirstOrDefault(p => p is ClockPanel) : null;
        // A tab that is gone (Lyrics, once the music stops) gives way too.
        if (!_shown.Visible) match ??= _panels[0];
        if (match != null && (_shown is MusicPanel || _shown is ClockPanel || !_shown.Visible) && match != _shown)
            _panel = _shown = match; // the card is closed, so swap without a fade
        SetMode(Mode.Card);
    }

    private void Choose(IslandPanel p)
    {
        if (p == _panel && p == _shown) return;
        _panel = p;
        _fade.To(0, Spec(SpringSpec.Panel));
        Invalidate();
    }

    private void ScheduleClose()
    {
        _enter.Stop();
        if (_mode != Mode.Card) return;
        _leave.Stop();
        _leave.Interval = Math.Max(1, Settings.CloseDelayMs);
        _leave.Start();
    }

    private void MaybeClose()
    {
        if (_mode == Mode.Card && _pointer == null && !_captured && _hold == 0 && !_dragOver)
            SetMode(Mode.Pill);
    }

    // ---------- IIslandHost ----------

    public new void Invalidate()
    {
        _dirty = true;
        if (_vsync != null && !_vsync.Running)
        {
            _lastFrame = _clock.Elapsed.TotalSeconds;
            _vsync.Run();
        }
    }

    public void Relayout()
    {
        if (_mode == Mode.Card) _h.To(_shown.Height, Spec(SpringSpec.Close));
        Invalidate();
    }

    /// <param name="action">tapping the pill while the toast shows runs this (and the pill does not open on hover meanwhile).</param>
    public void Toast(string icon, string text, SKColor? color = null, Action? action = null, int ms = 0)
    {
        if (_mode == Mode.Card || _hiddenByToggle) return;
        _toastIcon = icon;
        _toastText = text;
        _toastColor = color;
        _toastAction = action;
        SetMode(Mode.Toast);
        _toastTimer.Stop();
        _toastTimer.Interval = ms > 0 ? ms : action != null ? 4000 : 1900;
        _toastTimer.Start();
    }

    public void Collapse()
    {
        _leave.Stop();
        _enter.Stop();
        if (_mode == Mode.Card) SetMode(Mode.Pill);
    }

    public void Hold(bool hold)
    {
        _hold = Math.Max(0, _hold + (hold ? 1 : -1));
        if (_hold == 0)
        {
            // The pointer may have left while the dialog was up.
            if (Native.GetCursorPos(out var pt))
            {
                var p = ToIsland(PointToClient(new Point(pt.X, pt.Y)));
                _pointer = InsideIsland(p) ? p : null;
            }
            if (_pointer == null) ScheduleClose();
        }
    }

    public void DragFilesOut(IReadOnlyList<string> files)
    {
        Hold(true);
        try
        {
            Capture = false;
            DoDragDrop(new DataObject(DataFormats.FileDrop, files.ToArray()), DragDropEffects.Copy | DragDropEffects.Link);
        }
        finally
        {
            _captured = false;
            Hold(false);
        }
    }

    public string? Prompt(string title, string placeholder)
    {
        Hold(true);
        try { return InputDialog.Ask(title, placeholder, _theme); }
        finally { Hold(false); }
    }

    // ---------- lifecycle ----------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _vsync = new VsyncPump(Handle);
        Features = new Features(this);
        Notifier = new Notifications(this);
        Peeks = new SystemPeeks(this);
        WatchForeground();
        Companion = new Companion(this);
        Visualizers.Live = band => Settings.RealVisualizer && Loopback.Fresh ? Loopback.Level(band) : null;
        LayoutWindow();
        RegisterHotkeys();
        Native.AddClipboardFormatListener(Handle);
        UpdateGlassTimer();
        Invalidate();
    }

    private void RegisterHotkeys()
    {
        if (!IsHandleCreated) return;
        const uint ctrlAlt = 0x0002 | 0x0001, noRepeat = 0x4000;
        Native.UnregisterHotKey(Handle, Native.HOTKEY_TOGGLE);
        Native.UnregisterHotKey(Handle, Native.HOTKEY_SETTINGS);
        Native.UnregisterHotKey(Handle, Native.HOTKEY_MIC);
        Native.RegisterHotKey(Handle, Native.HOTKEY_TOGGLE, ctrlAlt | noRepeat, 'N');
        Native.RegisterHotKey(Handle, Native.HOTKEY_SETTINGS, ctrlAlt | noRepeat, 'S');
        if (Settings.MicMuteHotkey) Native.RegisterHotKey(Handle, Native.HOTKEY_MIC, ctrlAlt | noRepeat, 'M');
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Native.UnregisterHotKey(Handle, Native.HOTKEY_TOGGLE);
        Native.UnregisterHotKey(Handle, Native.HOTKEY_SETTINGS);
        Native.UnregisterHotKey(Handle, Native.HOTKEY_MIC);
        Native.RemoveClipboardFormatListener(Handle);
        _vsync?.Dispose();
        _poll.Stop();
        Features?.Dispose();
        Notifier?.Dispose();
        Peeks?.Dispose();
        UnwatchForeground();
        Companion?.Dispose();
        Loopback.Dispose();
        _glassTimer.Stop();
        Settings.LastPanel = _shown.Id;
        _glass?.Dispose();
        FreeBitmap();
        Media.Dispose();
        base.OnFormClosed(e);
    }

    public IntPtr HandleForTray => Handle;

    public void ToggleVisibility()
    {
        _hiddenByToggle = !_hiddenByToggle;
        if (_hiddenByToggle) Collapse();
        UpdateGlassTimer();
        Invalidate();
    }

    public bool IsHiddenByToggle => _hiddenByToggle;

    public void ToggleMic()
    {
        if (!Audio.HasMic) { Toast("mic-off", "No microphone found"); return; }
        bool muted = Audio.ToggleMic();
        _micMuted = muted;
        Toast(muted ? "mic-off" : "mic", muted ? "Microphone muted" : "Microphone on", muted ? new SKColor(255, 95, 87) : null);
        Invalidate();
    }

    /// <summary>Slow housekeeping: fullscreen apps, the mic state, hide-until-hover.</summary>
    private void OnPoll()
    {
        _pollCount++;
        Features?.Poll();
        Notifier?.Poll();
        PollLyrics();
        // Z3R rests when nothing is going on, and wakes the moment anything is.
        Z3R.Update(_pointer != null || _mode != Mode.Pill || Media.IsPlaying || CurrentActivity != null || _toastK > 0 || _captured);
        Weather.Poll(a => BeginInvoke(a));
        Companion?.Poll();
        // The real-sound visualizer listens only while the pill shows it.
        bool listen = Settings.RealVisualizer && Media.IsPlaying && !Hidden && _mode != Mode.Card;
        if (listen) Loopback.Start(); else Loopback.Stop();
        int minute = DateTime.Now.Minute;
        if (minute != _lastMinute) { _lastMinute = minute; Invalidate(); }
        if (_pollCount % 4 == 0)
        {
            bool fs = Settings.HideInFullscreen && Fullscreen.Active(MonitorBounds);
            if (fs != _hiddenFullscreen)
            {
                _hiddenFullscreen = fs;
                if (fs) Collapse();
                Invalidate();
            }
            // Enumerating capture devices takes ~10 ms of COM work: do it off the UI thread.
            if (!_micPolling)
            {
                _micPolling = true;
                Task.Run(() =>
                {
                    bool mic;
                    try { mic = Audio.MicMuted; } catch { mic = _micMuted; }
                    BeginInvoke(() =>
                    {
                        _micPolling = false;
                        if (mic != _micMuted) { _micMuted = mic; Invalidate(); }
                    });
                });
            }
        }

    }

    private readonly System.Windows.Forms.Timer _hoverWatch = new() { Interval = 50 };
    private int _offFor;

    /// <summary>Hidden until hover: a fully transparent window gets no mouse, so the cursor is watched instead,
    /// twenty times a second (quick enough that the pill appears as the pointer arrives).</summary>
    private bool CursorOnIsland() => Native.GetCursorPos(out var pt) && InsideIsland(ToIsland(PointToClient(new Point(pt.X, pt.Y))));

    private void WatchHover()
    {
        if (_hiddenByToggle || _hiddenFullscreen) return;
        // The pointer has really gone but no "mouse left" came (a window fading in from transparent, or a leave that
        // Windows dropped): leave now, so the card closes and a hidden pill hides again instead of getting stuck.
        if (_pointer != null)
        {
            if (_captured || CursorOnIsland()) { _offFor = 0; return; }
            // Two checks in a row (100 ms), so a pointer skimming the edge does not close the card by accident.
            if (++_offFor < 2) return;
            _offFor = 0;
            _pointer = null;
            _enter.Stop();
            ScheduleClose();
            Invalidate();
            return;
        }
        if (!Settings.HideUntilHover || _mode != Mode.Pill) return;
        if (!Native.GetCursorPos(out var pt)) return;
        var p = ToIsland(PointToClient(new Point(pt.X, pt.Y)));
        if (!InsideIsland(p)) return;
        _pointer = p;
        Invalidate();
        _enter.Stop();
        _enter.Interval = Math.Max(1, Settings.OpenDelayMs);
        _enter.Start();
        TrackLeave();
    }

    // ---------- frames ----------

    private void OnFrame()
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _lastFrame, 0.001, 0.05);
        _lastFrame = now;
        _t += dt;

        bool moving = false;
        moving |= _w.Step(dt);
        moving |= _h.Step(dt);
        moving |= _open.Step(dt);
        moving |= _fade.Step(dt);
        moving |= _notch.Step(dt);
        moving |= _top.Step(dt);

        // A tab change: the content fades out, the panel swaps and the card resizes, then it fades back in.
        if (_panel != _shown && _fade.Value < 0.08)
        {
            _shown.Hidden();
            _shown = _panel;
            if (_mode == Mode.Card) { _shown.Shown(); _h.To(_shown.Height, Spec(SpringSpec.Close)); }
            _fade.To(1, Spec(SpringSpec.Panel));
            moving = true;
        }

        // The Lyrics tab leaves when the music stops: an open card on it moves to Music.
        if (!_panel.Visible && _mode == Mode.Card) Choose(_panels[0]);

        // The pill shows music only while it plays; paused or stopped, it shows the clock.
        float musicTarget = Media.HasSession && Media.IsPlaying ? 1 : 0;
        _musicK = G.Ease(_musicK, musicTarget, 7, dt);
        if (Math.Abs(_musicK - musicTarget) < 0.004f) _musicK = musicTarget; else moving = true;

        float toastTarget = _mode == Mode.Toast ? 1 : 0;
        _toastK = G.Ease(_toastK, toastTarget, 14, dt);
        if (Math.Abs(_toastK - toastTarget) < 0.004f) _toastK = toastTarget; else moving = true;
        moving |= StepActivities(dt);

        // Hide until hover: the pill fades out when nothing needs it, and back in on hover, a toast, a Live
        // Activity (a notification, the volume bar…) or the open card. Animating its width no longer flashes it.
        float revealTarget = !Settings.HideUntilHover || _pointer != null || _mode != Mode.Pill || CurrentActivity != null ? 1 : 0;
        _reveal = G.Ease(_reveal, revealTarget, revealTarget > _reveal ? 18 : 9, dt);
        if (Math.Abs(_reveal - revealTarget) < 0.01f) _reveal = revealTarget; else moving = true;
        if (Settings.Companion) Companion?.Tick();
        // Count frames where the island's outline changes, so a glass capture can tell it went stale.
        if (moving) Interlocked.Increment(ref _shapeGen);

        // What keeps frames coming besides springs, and how often it needs them.
        double interval = double.MaxValue;
        if (!Hidden)
        {
            if (_mode == Mode.Card && _open.Value > 0.3)
            {
                if (_shown.Animating) interval = _shown.FrameInterval;
                if (_theme.Ambience != Ambience.None) interval = 0;
            }
            else
            {
                if (Media.IsPlaying) interval = 1 / 30.0; // the pill's visualizer
                if (_musicK < 1 && Settings.ShowClock) interval = Math.Min(interval, _pillClock.Interval(Settings.PillClockStyle, false));
                interval = Math.Min(interval, ActivityInterval);
            }
            // A moving material (aurora, nebula, neon) animates at its own, modest rate.
            interval = Math.Min(interval, Surfaces.Interval(_theme));
        }
        // Everything above needs the whole island; the companion alone needs only its own patch (Z3R).
        double fullInterval = interval;
        if (!Hidden && Settings.Companion && Companion != null) interval = Math.Min(interval, Companion.Interval);

        // Ticks arrive on the display's vsync grid; allow half a frame of slack so a 1/30 s target lands on
        // every second 60 Hz tick instead of slipping to every third.
        bool fullDue = _dirty || moving || now - _lastFullDraw >= fullInterval - 0.008;
        bool draw = fullDue || now - _lastDraw >= interval - 0.008;
        _dirty = false;
        // Z3R: a companion-only frame on the resting pill redraws just the companion's patch.
        _partial = !fullDue && Z3R.On && _mode == Mode.Pill && _toastK == 0 && !_catPatch.IsEmpty ? _catPatch : RectangleF.Empty;
        if (draw)
        {
            _lastDraw = now;
            if (_partial.IsEmpty) _lastFullDraw = now;
            Z3R.CountFrame(!_partial.IsEmpty);
            if (FrameStats.On)
            {
                double d0 = FrameStats.Now;
                int g2 = GC.CollectionCount(2), g1 = GC.CollectionCount(1);
                DrawAndPresent(dt);
                double total = FrameStats.Now - d0;
                if (total > 25)
                    FrameStats.Note($"frame: draw+present {total:0.0} gc1 +{GC.CollectionCount(1) - g1} gc2 +{GC.CollectionCount(2) - g2} panel {_shown.Id} mode {_mode}", total);
            }
            else DrawAndPresent(dt);
        }
        if (!moving && !_dirty && interval == double.MaxValue) { _vsync?.Pause(); FrameStats.Idle(); }
        else if (Z3R.On && !moving && !_dirty && interval >= 0.035 && _vsync != null)
        {
            // Z3R: nothing is in motion and the next frame is a while off (the companion's breath, a material's
            // step): stop the 60 Hz ticking and wake just before it is due, then draw on the next vsync as usual.
            double due = Math.Min(_lastDraw + interval, _lastFullDraw + fullInterval) - now - 0.012;
            _vsync.Pause();
            Z3R.WakeIn(Math.Max(0.001, due), () => { if (_vsync != null && !_vsync.Running) { _lastFrame = _clock.Elapsed.TotalSeconds; _vsync.Run(); } }, this);
        }
    }

    private bool Hidden => _hiddenByToggle || _hiddenFullscreen;
    private double _lastFullDraw;
    private RectangleF _partial, _catPatch;   // Z3R: this frame's patch (empty: whole island); where the companion sits

    // ---------- input ----------

    private void TrackLeave()
    {
        if (_trackingLeave) return;
        _trackingLeave = true;
        TrackMouseExt.TrackMouseEventSafe(Handle);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        TrackLeave();
        var p = ToIsland(e.Location);
        bool inside = InsideIsland(p);
        _pointer = inside ? p : null;
        if (_captured) _shown.Drag(ToCard(p));

        if (inside)
        {
            _leave.Stop();
            if (_mode != Mode.Card && !_enter.Enabled)
            {
                _enter.Interval = Math.Max(1, Settings.OpenDelayMs);
                _enter.Start();
            }
        }
        else ScheduleClose();
        if (inside) Companion?.Noticed();
        Cursor = inside && _mode == Mode.Card ? Cursors.Hand : Cursors.Default;
        if (_mode == Mode.Card) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _trackingLeave = false;
        // A window fading in from fully transparent can get a "mouse left" while the pointer is still on the island
        // (it had no pixels to hold the mouse yet): only believe it if the pointer really is outside.
        if (Settings.HideUntilHover && CursorOnIsland()) return;   // the next mouse move tracks again
        if (_captured) return;
        _pointer = null;
        _enter.Stop();
        ScheduleClose();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var p = ToIsland(e.Location);
        if (InsideIsland(p) && _mode != Mode.Card && e.Button == MouseButtons.Left) StartLongPress();
        if (!InsideIsland(p) || _mode != Mode.Card || HitTab(p) != null) return;
        _captured = _shown.Down(ToCard(p), e.Button);
        if (_captured) Capture = true;
        // A hold on the card's empty space mutes the mic too (pressing a control does not count).
        else if (e.Button == MouseButtons.Left) StartLongPress();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var p = ToIsland(e.Location);
        if (_captured)
        {
            _captured = false;
            Capture = false;
            _shown.Up(ToCard(p));
            if (!InsideIsland(p)) { _pointer = null; ScheduleClose(); }
            Invalidate();
            return;
        }
        if (!InsideIsland(p)) return;

        // Middle-click anywhere on the island: play or pause. (Hovering opens the card within the open delay, so
        // this has to work on the card too, or a middle-click on the pill would rarely land.)
        if (e.Button == MouseButtons.Middle)
        {
            if (!Media.HasSession) return;
            Media.TogglePlay(); // flips IsPlaying straight away; the app follows
            if (_mode != Mode.Card) Toast("music", Media.IsPlaying ? "Playing" : "Paused");
            Invalidate();
            return;
        }

        if (_mode != Mode.Card)
        {
            _longPress.Stop();
            if (_longPressFired) { _longPressFired = false; return; }       // the hold already muted the mic
            if (e.Button == MouseButtons.Right) { ShowContextMenu(); return; }
            if (_mode == Mode.Toast && _toastAction is { } act)
            {
                _toastAction = null;
                SetMode(Mode.Pill);
                act();
                return;
            }
            _enter.Stop();
            OpenFromPill();
            return;
        }

        _longPress.Stop();
        if (_longPressFired) { _longPressFired = false; return; }   // the hold already muted the mic

        // Poke the cat in the card's corner.
        if (Settings.Companion && CatCardRect.Contains(ToCard(p))) { Companion?.Poke(); Invalidate(); return; }

        var tab = HitTab(p);
        if (tab == "gear") { Collapse(); SettingsForm.Instance?.ShowAndFocus(); return; }
        if (tab != null)
        {
            if (e.Button == MouseButtons.Right) { ShowContextMenu(); return; }
            Choose(_panels.First(x => x.Id == tab));
            return;
        }
        _shown.Click(ToCard(p), e.Button);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var p = ToIsland(PointToClient(Cursor.Position));
        if (_mode == Mode.Card && InsideIsland(p))
        {
            if (p.Y <= 27)
            {
                // Over the tabs, the wheel walks through them.
                var tabs = Tabs;
                int i = tabs.IndexOf(_panel) + (e.Delta < 0 ? 1 : -1);
                Choose(tabs[(i + tabs.Count) % tabs.Count]);
            }
            else _shown.Wheel(ToCard(p), e.Delta);
        }
        else if (Media.IsPlaying)
        {
            // On the pill, while music plays, the wheel sets the volume.
            Audio.Volume = Math.Clamp(MathF.Round((Audio.Volume + Math.Sign(e.Delta) * 0.05f) * 20) / 20f, 0, 1);
        }
        else if (_mode == Mode.Pill && Settings.ShowClock)
        {
            // Otherwise it flips through the clock's faces.
            Settings.PillClockStyle = ClockFaces.Next(Settings.PillClockStyle, e.Delta < 0 ? 1 : -1);
            Settings.Save();
            _pillClock.Reset();
            _enter.Stop(); // scrolling is not a request to open the card
            Invalidate();
        }
    }

    // ---------- drag and drop onto the island ----------

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) { e.Effect = DragDropEffects.None; return; }
        Companion?.Pulse("reach");
        e.Effect = DragDropEffects.Copy;
        _dragOver = true;
        _leave.Stop();
        var shelf = _panels.First(p => p is ShelfPanel);
        if (_mode != Mode.Card) { _panel = _shown = shelf; SetMode(Mode.Card); }
        else Choose(shelf);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _dragOver = false;
        _pointer = null;
        ScheduleClose();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _dragOver = false;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var shelf = _panels.First(p => p is ShelfPanel);
            shelf.Drop(files);
            if (_shown != shelf) Choose(shelf);
        }
    }

    // ---------- menu ----------

    public void ShowContextMenu()
    {
        Hold(true);
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, BackColor = Color.FromArgb(28, 28, 31), ForeColor = Color.FromArgb(239, 237, 232) };
        var font = Fonts.UiFont(9.5f);
        if (font != null) menu.Font = font;
        // What is running can be stopped from here.
        foreach (var act in _activities.Where(a => a.Stop != null).ToList())
        {
            var (label, stop) = act.Stop!.Value;
            menu.Items.Add(label, null, (_, _) => stop());
        }
        if (_activities.Any(a => a.Stop != null)) menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Focus for 25 minutes", null, (_, _) => Features?.StartFocus());
        menu.Items.Add("Timer…", null, (_, _) => Features?.PromptTimer());
        menu.Items.Add("Pick a colour", null, (_, _) => Features?.PickColour());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => SettingsForm.Instance?.ShowAndFocus());
        menu.Items.Add(_hiddenByToggle ? "Show island" : "Hide island", null, (_, _) => ToggleVisibility());
        menu.Items.Add(_micMuted ? "Unmute microphone" : "Mute microphone", null, (_, _) => ToggleMic());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit cacad", null, (_, _) => Application.Exit());
        menu.Closed += (_, _) => { Hold(false); BeginInvoke(menu.Dispose); };
        menu.Show(Cursor.Position);
    }

    protected override void WndProc(ref Message m)
    {
        if (!FrameStats.On) { Dispatch(ref m); return; }
        double w0 = FrameStats.Now;
        int msg = m.Msg;
        Dispatch(ref m);
        FrameStats.Note($"window message 0x{msg:X4}", FrameStats.Now - w0);
    }

    private void Dispatch(ref Message m)
    {
        switch (m.Msg)
        {
            case 0x020E: // WM_MOUSEHWHEEL: a sideways touchpad swipe
                OnHorizontalWheel((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                m.Result = IntPtr.Zero;
                return;
            case 0x001A: // WM_SETTINGCHANGE
                base.WndProc(ref m);

                return;
            case Native.WM_HOTKEY:
                int id = m.WParam.ToInt32();
                if (id == Native.HOTKEY_TOGGLE) ToggleVisibility();
                else if (id == Native.HOTKEY_SETTINGS) SettingsForm.Instance?.ShowAndFocus();
                else if (id == Native.HOTKEY_MIC) ToggleMic();
                return;
            case Native.WM_CACAD_SHOW_SETTINGS:
                SettingsForm.Instance?.ShowAndFocus();
                return;
            case (int)Native.WM_APP_TRAY:
                long evt = m.LParam.ToInt64() & 0xFFFF;
                if (evt == Native.WM_RBUTTONUP) ShowContextMenu();
                else if (evt == 0x0203 || evt == 0x0202) SettingsForm.Instance?.ShowAndFocus();
                return;
            case VsyncPump.WM_FRAME:
                try { UiProfile.Time("frame", OnFrame); }
                finally { _vsync?.Done(); }
                return;
            case Native.WM_CLIPBOARDUPDATE:
                Clipboard.OnClipboardUpdate();
                return;
            case Native.WM_DPICHANGED:
            case Native.WM_DISPLAYCHANGE:
                base.WndProc(ref m);
                BeginInvoke(LayoutWindow);
                return;
        }
        base.WndProc(ref m);
    }
}

internal static class TrackMouseExt
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public int CbSize;
        public int DwFlags;
        public IntPtr HwndTrack;
        public uint DwHoverTime;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEvent);

    public static void TrackMouseEventSafe(IntPtr hwnd)
    {
        var t = new TRACKMOUSEEVENT
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<TRACKMOUSEEVENT>(),
            DwFlags = 0x0002, // TME_LEAVE
            HwndTrack = hwnd,
        };
        TrackMouseEvent(ref t);
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = true; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.FromArgb(239, 237, 232);
        base.OnRenderItemText(e);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(28, 28, 31);
        public override Color MenuBorder => Color.FromArgb(52, 52, 58);
        public override Color MenuItemBorder => Color.FromArgb(48, 48, 54);
        public override Color MenuItemSelected => Color.FromArgb(48, 48, 54);
        public override Color SeparatorDark => Color.FromArgb(52, 52, 58);
        public override Color SeparatorLight => Color.FromArgb(52, 52, 58);
        public override Color ImageMarginGradientBegin => Color.FromArgb(28, 28, 31);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(28, 28, 31);
        public override Color ImageMarginGradientEnd => Color.FromArgb(28, 28, 31);
    }
}
