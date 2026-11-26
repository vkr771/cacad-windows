using SkiaSharp;

namespace Cacad;

/// <summary>Live Activities in the pill, toasts that can be tapped, and the pill's gestures.</summary>
public sealed partial class IslandWindow
{
    private readonly List<LiveActivity> _activities = new();
    private float _activityK;
    private LiveActivity? _shownActivity;   // what the pill draws (kept while fading out)
    private Action? _toastAction;
    private readonly System.Windows.Forms.Timer _longPress = new() { Interval = 600 };
    private bool _longPressFired;
    private int _hwheel;

    /// <summary>The activity the pill shows: the highest priority, then the newest.</summary>
    public LiveActivity? CurrentActivity => _activities.OrderByDescending(a => a.Priority).ThenByDescending(a => a.Started).FirstOrDefault();

    public LiveActivity? FindActivity(string id) => _activities.FirstOrDefault(a => a.Id == id);

    /// <summary>Start (or replace) an activity.</summary>
    public void StartActivity(LiveActivity act)
    {
        _activities.RemoveAll(a => a.Id == act.Id);
        _activities.Add(act);
        Invalidate();
    }

    public void EndActivity(string id)
    {
        if (_activities.RemoveAll(a => a.Id == id) > 0) Invalidate();
    }

    /// <summary>Per frame: the pill's width follows the activity, and the activity fades in or out.</summary>
    private bool StepActivities(float dt)
    {
        var cur = CurrentActivity;
        if (cur != null) _shownActivity = cur;
        if (_mode == Mode.Pill)
        {
            float target = cur != null ? Math.Min(Math.Max(PillW, cur.Width + CatSlot), CardW) : PillW;
            if (Math.Abs(_w.Target - target) > 0.5) _w.To(target, Spec(SpringSpec.Close));
        }
        float k = cur != null ? 1 : 0;
        _activityK = G.Ease(_activityK, k, 9, dt);
        if (Math.Abs(_activityK - k) < 0.004f) { _activityK = k; if (k == 0) _shownActivity = null; return false; }
        return true;
    }

    private double ActivityInterval => CurrentActivity is { } a && _mode == Mode.Pill ? a.Interval : double.MaxValue;

    private void DrawActivity(SKCanvas c, float a, float w, float h)
    {
        if (_shownActivity == null || a <= 0.003f) return;
        ActivityView.Draw(c, _shownActivity, w, h, a, _t);
    }

    // ---------- gestures on the pill ----------

    private void StartLongPress()
    {
        _longPressFired = false;
        _longPress.Stop();
        _longPress.Start();
    }

    private void WireGestures()
    {
        _longPress.Tick += (_, _) =>
        {
            // Hold the pill: mute or unmute every microphone.
            _longPress.Stop();
            _longPressFired = true;
            // On the card, close it first so the pill can say what happened.
            if (_mode == Mode.Card) Collapse();
            ToggleMic();
        };
    }

    /// <summary>A sideways swipe on a touchpad (WM_MOUSEHWHEEL): next or previous track.</summary>
    private void OnHorizontalWheel(int delta)
    {
        if (!Media.HasSession) return;
        _hwheel += delta;
        // On the card the music panel shows the change itself; on the pill a toast says what happened.
        if (_hwheel >= 120) { _hwheel = 0; Media.Next(); if (_mode != Mode.Card) Toast("music", "Next track"); }
        else if (_hwheel <= -120) { _hwheel = 0; Media.Previous(); if (_mode != Mode.Card) Toast("music", "Previous track"); }
    }
}
