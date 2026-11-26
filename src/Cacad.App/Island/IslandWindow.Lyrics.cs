using SkiaSharp;

namespace Cacad;

/// <summary>Synced lyrics: looked up when a song starts, shown in the pill (a small Lyrics View between the cover
/// and the visualizer) and in the card's Lyrics tab. See <see cref="LyricsView"/>.</summary>
public sealed partial class IslandWindow
{
    public LyricsService Lyrics { get; } = new();

    private int _lyricTrack = -1;
    private DateTime _lyricDue = DateTime.MaxValue;
    private float _lyricW;                 // the pill width this song's lyrics want (from its longest line)

    // Some players never report how far into the song they are. For those, the island keeps its own clock from
    // when the track started, paused while the music is paused.
    private double _ownBase;
    private DateTime? _ownSince;

    private const float LyricSize = 12.5f, LyricMin = 300, LyricMax = 440;
    private float CoverW => 24 + 6 + 10;    // cover and its gaps, left of the text
    private float VisW => 34;               // the visualizer and its gap, right of the text

    private bool HasTimeline => Media.Duration > 0;
    public double LyricPos => HasTimeline ? Media.Position : _ownBase + (_ownSince is { } s ? (DateTime.Now - s).TotalSeconds : 0);

    private bool LyricsShown => Settings.Lyrics && Media.IsPlaying && Lyrics.Current != null;

    private void WireLyrics()
    {
        Lyrics.Changed += () =>
        {
            _lyricW = 0;
            if (Lyrics.Current is { } song)
            {
                float longest = 0;
                foreach (var line in song.Lines.Where(l => l.Text.Length > 0))
                {
                    using var f = WorldText.Font(line.Text, LyricSize, Fonts.Medium);
                    longest = Math.Max(longest, WorldText.Measure(f, line.Text));
                }
                _lyricW = Math.Clamp(longest + CoverW + VisW + 16, LyricMin, LyricMax);
            }
            Invalidate();
        };
    }

    /// <summary>From the housekeeping poll: look up the song a moment after it changes (so its length has arrived),
    /// and keep the fallback clock.</summary>
    private void PollLyrics()
    {
        if (!Settings.Lyrics || !Media.HasSession) { Lyrics.Clear(); _lyricTrack = -1; return; }
        if (Media.TrackVersion != _lyricTrack)
        {
            _lyricTrack = Media.TrackVersion;
            _lyricDue = DateTime.Now.AddSeconds(1.2);
            _ownBase = 0;
            _ownSince = Media.IsPlaying ? DateTime.Now : null;
        }
        // The fallback clock runs only while playing.
        if (Media.IsPlaying && _ownSince == null) _ownSince = DateTime.Now;
        if (!Media.IsPlaying && _ownSince is { } since) { _ownBase += (DateTime.Now - since).TotalSeconds; _ownSince = null; }
        if (DateTime.Now >= _lyricDue)
        {
            _lyricDue = DateTime.MaxValue;
            Lyrics.Request(Media.Title, Media.Artist, Media.AppId, Media.Duration, a => BeginInvoke(a));
        }
    }

    /// <summary>True while a lyric line is being sung right now (not in a break or the pause after a line), so the
    /// companion can sing along.</summary>
    private DateTime _singUntil;
    public bool Singing
    {
        get
        {
            if (!LyricsShown || Lyrics.Current is not { } song) return false;
            double pos = LyricPos;
            var (i, line) = LyricsService.At(song, pos);
            bool now = line is { Text.Length: > 0 } && LyricsView.Sung(song, i, pos - 0.35) < 1;
            // Short gaps between lines do not stop the singing (no flicker between poses).
            if (now) _singUntil = DateTime.Now.AddSeconds(1.6);
            return now || DateTime.Now < _singUntil;
        }
    }

    /// <summary>The pill width lyrics want (0 when none show).</summary>
    private float LyricPillW => LyricsShown ? _lyricW : 0;

    private readonly LyricsView _pillLyrics = new(LyricsView.Pill);

    /// <summary>The pill's Lyrics View, between the cover and the visualizer.</summary>
    private void DrawLyric(SKCanvas c, float a, float w, float h)
    {
        var song = Lyrics.Current;
        if (song == null) return;
        float L = -w / 2 + CoverW, R = w / 2 - VisW;
        _pillLyrics.Draw(c, song, SKRect.Create(L, 0, R - L, h), LyricPos, _t, a);
    }
}

public sealed partial class IslandWindow
{
    private DateTime _rainWarned = DateTime.MinValue;

    /// <summary>New weather: a heads-up in the pill when rain is likely within the next hour and a half (once per
    /// spell of rain), and the companion takes out its umbrella while it rains.</summary>
    private void OnWeather()
    {
        Invalidate();
        if (Weather.Current is not { } w || WeatherService.Rainy(w.Code)) return;
        if ((DateTime.Now - _rainWarned).TotalHours < 3) return;
        var soon = w.Hours.FirstOrDefault(h => h.At > DateTime.Now.AddMinutes(-30) && h.At < DateTime.Now.AddMinutes(90) && h.Rain >= 60);
        if (soon == null) return;
        _rainWarned = DateTime.Now;
        string when = soon.At <= DateTime.Now ? "soon" : "around " + soon.At.ToString(Settings.Clock24h ? "HH:mm" : "h tt").ToLowerInvariant();
        Toast("umbrella", $"Rain likely {when} · {soon.Rain}%", new SKColor(110, 180, 255), null, 4500);
    }

    /// <summary>It is raining (or snowing) where you are, for the companion.</summary>
    public string? Sky => Weather.Current is { } w && (DateTime.Now - w.Fetched).TotalHours < 2
        ? WeatherService.Rainy(w.Code) ? "rain" : WeatherService.Snowy(w.Code) ? "snow" : null : null;
}
