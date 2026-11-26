using SkiaSharp;
using Windows.Media.Control;

namespace Cacad;

/// <summary>What is playing, from any app that reports media to Windows (Spotify, browsers, Apple Music, VLC…).
/// Events from WinRT arrive on the thread pool; everything public is updated on the UI thread.</summary>
public sealed class MediaService : IDisposable
{
    private readonly SynchronizationContext _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _generation;

    public event Action? Changed;

    public bool HasSession { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string AppId { get; private set; } = "";
    public bool IsPlaying { get; private set; }
    public bool CanNext { get; private set; }
    public bool CanPrev { get; private set; }
    public bool CanSeek { get; private set; }
    public AlbumArt? Art { get; private set; }
    /// <summary>Increments on every track change, so views can animate the swap.</summary>
    public int TrackVersion { get; private set; }

    private TimeSpan _position, _duration;
    private DateTime _positionAt;

    public MediaService()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _ = InitAsync();
    }

    public double Duration => _duration.TotalSeconds;

    /// <summary>Position extrapolated from the last report while playing.</summary>
    public double Position
    {
        get
        {
            double p = _position.TotalSeconds;
            if (IsPlaying) p += (DateTime.UtcNow - _positionAt).TotalSeconds;
            return Duration > 0 ? Math.Clamp(p, 0, Duration) : Math.Max(0, p);
        }
    }

    private async Task InitAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => Post(Attach);
            _mgr.SessionsChanged += (_, _) => Post(Attach);
            Post(Attach);
        }
        catch { /* media API unavailable */ }
    }

    private void Post(Action a) => _ui.Post(_ => { try { a(); } catch { } }, null);

    private GlobalSystemMediaTransportControlsSession? Pick()
    {
        if (_mgr == null) return null;
        var current = _mgr.GetCurrentSession();
        if (current != null) return current;
        // Fall back to any session that is playing, then any at all.
        var all = _mgr.GetSessions();
        return all.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
               ?? all.FirstOrDefault();
    }

    private void Attach()
    {
        var s = Pick();
        if (ReferenceEquals(s, _session)) return;
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }
        _session = s;
        HasSession = s != null;
        if (s == null)
        {
            Title = Artist = AppId = "";
            IsPlaying = false;
            SetArt(null);
            Changed?.Invoke();
            return;
        }
        AppId = s.SourceAppUserModelId ?? "";
        s.MediaPropertiesChanged += OnProps;
        s.PlaybackInfoChanged += OnPlayback;
        s.TimelinePropertiesChanged += OnTimeline;
        ReadPlayback();
        ReadTimeline();
        _ = ReadPropsAsync();
    }

    private void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Post(() => _ = ReadPropsAsync());
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Post(() => { ReadPlayback(); ReadTimeline(); Changed?.Invoke(); });
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Post(() => { ReadTimeline(); Changed?.Invoke(); });

    private void ReadPlayback()
    {
        var info = _session?.GetPlaybackInfo();
        if (info == null) return;
        bool was = IsPlaying;
        double pos = Position;
        IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        CanNext = info.Controls?.IsNextEnabled ?? false;
        CanPrev = info.Controls?.IsPreviousEnabled ?? false;
        CanSeek = info.Controls?.IsPlaybackPositionEnabled ?? false;
        if (was != IsPlaying)
        {
            _position = TimeSpan.FromSeconds(pos);
            _positionAt = DateTime.UtcNow;
        }
    }

    private void ReadTimeline()
    {
        var t = _session?.GetTimelineProperties();
        if (t == null) return;
        _duration = t.EndTime - t.StartTime;
        _position = t.Position - t.StartTime;
        var updated = t.LastUpdatedTime.UtcDateTime;
        // Some apps report a stale LastUpdatedTime; never extrapolate further back than now.
        _positionAt = updated > DateTime.UtcNow || updated.Year < 2000 ? DateTime.UtcNow : updated;
        if (IsPlaying && Duration > 0 && Position >= Duration) _positionAt = DateTime.UtcNow;
    }

    private async Task ReadPropsAsync()
    {
        var s = _session;
        if (s == null) return;
        int gen = ++_generation;
        GlobalSystemMediaTransportControlsSessionMediaProperties? props;
        try { props = await s.TryGetMediaPropertiesAsync(); }
        catch { return; }
        if (gen != _generation || props == null) return;

        string title = props.Title ?? "", artist = props.Artist ?? "";
        if (string.IsNullOrEmpty(artist)) artist = props.AlbumArtist ?? "";
        bool newTrack = title != Title || artist != Artist;
        Title = title;
        Artist = artist;
        if (newTrack) TrackVersion++;

        SKBitmap? bmp = null;
        if (props.Thumbnail != null)
        {
            try
            {
                using var ras = await props.Thumbnail.OpenReadAsync();
                using var stream = ras.AsStreamForRead();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                bmp = SKBitmap.Decode(ms);
            }
            catch { bmp = null; }
        }
        if (gen != _generation) { bmp?.Dispose(); return; }
        SetArt(AlbumArt.From(bmp));
        bmp?.Dispose();
        Changed?.Invoke();
    }

    private void SetArt(AlbumArt? art)
    {
        var old = Art;
        Art = art;
        old?.Dispose();
    }

    public async void TogglePlay()
    {
        if (_session == null) return;
        // Optimistic: flip now so the morph starts on the click, the real state follows.
        double pos = Position;
        IsPlaying = !IsPlaying;
        _position = TimeSpan.FromSeconds(pos);
        _positionAt = DateTime.UtcNow;
        Changed?.Invoke();
        try { await _session.TryTogglePlayPauseAsync(); } catch { }
    }

    public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }

    public async void Seek(double seconds)
    {
        if (_session == null) return;
        _position = TimeSpan.FromSeconds(seconds);
        _positionAt = DateTime.UtcNow;
        try
        {
            var start = _session.GetTimelineProperties()?.StartTime ?? TimeSpan.Zero;
            await _session.TryChangePlaybackPositionAsync((start + TimeSpan.FromSeconds(seconds)).Ticks);
        }
        catch { }
    }

    public void Dispose() => SetArt(null);
}
