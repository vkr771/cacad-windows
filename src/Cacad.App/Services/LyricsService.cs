using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cacad;

/// <summary>Synced lyrics for the song playing, from LRCLIB (lrclib.net, a free, open lyrics database). Only the
/// song's title, artist and length are sent. Found lyrics are cached in %AppData%\cacad\lyrics so a song is looked up
/// once. Lookups run in the background; <see cref="Current"/> is read from the UI thread.</summary>
public sealed class LyricsService
{
    public sealed record Line(double At, string Text);
    public sealed record Song(string Key, IReadOnlyList<Line> Lines);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static LyricsService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("cacad/1.4 (Windows desktop app)");

    private readonly Dictionary<string, Song?> _memory = new();
    private string _wanted = "";

    /// <summary>Lyrics for the song now playing, or null (none found, still looking, or turned off).</summary>
    public Song? Current { get; private set; }
    public event Action? Changed;

    private static string Dir => Path.Combine(SettingsStore.DataDir, "lyrics");

    /// <summary>Called when the track changes (UI thread).</summary>
    public void Request(string title, string artist, string appId, double duration, Action<Action> post)
    {
        var (t, a) = Clean(title, artist, appId);
        string key = $"{a}\n{t}".ToLowerInvariant();
        if (key == _wanted) return;
        Log($"playing in {(appId.Length > 0 ? appId : "?")}: \"{title}\" by \"{artist}\"{(duration > 0 ? $", {duration:0}s" : ", no length reported")} -> search \"{t}\" / \"{a}\"");
        _wanted = key;
        Current = null;
        Changed?.Invoke();
        if (t.Length == 0) return;
        if (_memory.TryGetValue(key, out var known)) { Current = known; Changed?.Invoke(); return; }
        _ = Task.Run(async () =>
        {
            var song = await Find(key, t, a, duration);
            post(() =>
            {
                _memory[key] = song;
                if (_wanted != key) return;
                Current = song;
                Changed?.Invoke();
            });
        });
    }

    public void Clear() { _wanted = ""; if (Current != null) { Current = null; Changed?.Invoke(); } }

    /// <summary>The line being sung at <paramref name="pos"/> seconds (with a small lead so it appears on the beat),
    /// its index, and when it started; index -1 before the first line.</summary>
    public static (int index, Line? line) At(Song song, double pos)
    {
        pos += 0.25;
        int lo = 0, hi = song.Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (song.Lines[mid].At <= pos) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found < 0 ? (-1, null) : (found, song.Lines[found]);
    }

    // ---------- looking it up ----------

    private static async Task<Song?> Find(string key, string title, string artist, double duration)
    {
        string file = Path.Combine(Dir, Hash(key) + ".lrc");
        try
        {
            if (File.Exists(file))
            {
                string cached = await File.ReadAllTextAsync(file);
                if (cached.Length > 0) { Log($"cached  {artist} - {title}"); return Parse(key, cached); }
                // "No lyrics" is remembered for three days, then asked again (the database keeps growing).
                if ((DateTime.Now - File.GetLastWriteTime(file)).TotalDays < 3) { Log($"none (remembered)  {artist} - {title}"); return null; }
            }
        }
        catch { }

        string? lrc = null;
        try
        {
            // 1. An exact match (title, artist, length).
            var q = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (duration > 0) q += $"&duration={Math.Round(duration).ToString(CultureInfo.InvariantCulture)}";
            using (var res = await Http.GetAsync("https://lrclib.net/api/get?" + q))
                if (res.IsSuccessStatusCode)
                    lrc = Synced(JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement);
            // 2. A search by title and artist, 3. a free-text search, 4. the title alone (when the app reports the
            // artist oddly). Each prefers a version of the same length: a live or extended cut drifts out of sync.
            lrc ??= await Search($"track_name={Uri.EscapeDataString(title)}" + (artist.Length > 0 ? $"&artist_name={Uri.EscapeDataString(artist)}" : ""), duration, null);
            lrc ??= await Search($"q={Uri.EscapeDataString((title + " " + artist).Trim())}", duration, null);
            lrc ??= await Search($"track_name={Uri.EscapeDataString(title)}", duration, artist);
        }
        catch (Exception ex) { Log($"offline ({ex.GetType().Name})  {artist} - {title}"); return null; } // try again next time it plays

        Log($"{(lrc != null ? "found" : "none")}  {artist} - {title}  ({duration:0}s)");
        try
        {
            Directory.CreateDirectory(Dir);
            await File.WriteAllTextAsync(file, lrc ?? "");   // an empty file remembers "no lyrics"
        }
        catch { }
        return lrc == null ? null : Parse(key, lrc);
    }

    /// <summary>The first synced result of a search, of about the right length. With <paramref name="artistHint"/>,
    /// a result must share a word with it (so a title-only search does not pick a different song).</summary>
    private static async Task<string?> Search(string query, double duration, string? artistHint)
    {
        using var res = await Http.GetAsync("https://lrclib.net/api/search?" + query);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        string? loose = null;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (Synced(item) is not { } found) continue;
            if (artistHint is { Length: > 0 })
            {
                string who = item.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "";
                var words = artistHint.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2);
                if (!words.Any(w => who.ToLowerInvariant().Contains(w))) continue;
            }
            double d = item.TryGetProperty("duration", out var dd) && dd.ValueKind == JsonValueKind.Number ? dd.GetDouble() : 0;
            if (duration <= 0 || d <= 0) { loose ??= found; continue; }
            if (Math.Abs(d - duration) <= 4) return found;
        }
        // Length unknown (the player does not report it): the first synced result.
        return duration <= 0 ? loose : null;
    }

    /// <summary>A short local log of lookups (%AppData%\cacad\lyrics.log), to see why a song has no lyrics.</summary>
    public static void Log(string s)
    {
        try
        {
            string path = Path.Combine(SettingsStore.DataDir, "lyrics.log");
            var lines = File.Exists(path) ? File.ReadAllLines(path).TakeLast(80).ToList() : new List<string>();
            lines.Add($"{DateTime.Now:MM-dd HH:mm:ss}  {s}");
            File.WriteAllLines(path, lines);
        }
        catch { }
    }

    private static string? Synced(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String
        && s.GetString() is { Length: > 0 } text ? text : null;

    private static readonly Regex Stamp = new(@"\[(\d+):(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);

    /// <summary>Parses LRC: lines like "[01:23.45]words", possibly with several timestamps on one line.</summary>
    public static Song? Parse(string key, string lrc)
    {
        var lines = new List<Line>();
        foreach (var raw in lrc.Split('\n'))
        {
            var stamps = Stamp.Matches(raw);
            if (stamps.Count == 0) continue;
            string text = raw[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match m in stamps)
                lines.Add(new Line(int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), text));
        }
        lines.Sort((x, y) => x.At.CompareTo(y.At));
        return lines.Any(l => l.Text.Length > 0) ? new Song(key, lines) : null;
    }

    // ---------- tidying what players report ----------

    private static readonly Regex Noise = new(
        @"\s*[\(\[](official|lyric|lyrics|audio|video|music video|mv|hd|hq|4k|visualizer|remaster(ed)?[^\)\]]*|explicit|clean)[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>What to search for. A browser tab (YouTube…) reports "Artist - Song (Official Video)" with the channel as
    /// the artist; a music app reports the song and artist, sometimes as "Song - Remastered 2011".</summary>
    public static (string title, string artist) Clean(string title, string artist, string appId)
    {
        string t = Noise.Replace(title, "");
        // Film songs: "Song (From \"Movie\")", "Song [From Movie]".
        t = Regex.Replace(t, @"\s*[\(\[]\s*from\b[^\)\]]*[\)\]]?", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*\|.*$", "");
        t = Regex.Replace(t, @"\s*[\(\[]?\s*(feat\.?|ft\.?|featuring)\s[^\)\]]*[\)\]]?", "", RegexOptions.IgnoreCase);
        string a = Regex.Replace(artist, @"(VEVO|\s*-\s*Topic|\s*Official)$", "", RegexOptions.IgnoreCase).Trim();
        bool browser = Regex.IsMatch(appId, "chrome|msedge|firefox|brave|opera|vivaldi|browser", RegexOptions.IgnoreCase);
        int dash = t.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && (browser || a.Length == 0 || t.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
        {
            a = t[..dash].Trim();
            t = t[(dash + 3)..];
        }
        else t = Regex.Replace(t, @"\s+-\s+.*(remaster|live|version|edit|mix|mono|stereo).*$", "", RegexOptions.IgnoreCase);
        int comma = a.IndexOfAny(new[] { ',', '&' });
        if (comma > 0) a = a[..comma];
        return (t.Trim(' ', '"', '\''), a.Trim());
    }

    private static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..16];
}
