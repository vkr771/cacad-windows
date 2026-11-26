using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SkiaSharp;

namespace Cacad;

/// <summary>The island's live features: timers and focus sessions, downloads, calls, charging, idle mode and
/// smart clipboard peeks. Driven by the island's housekeeping poll (4 Hz) on the UI thread.</summary>
public sealed class Features : IDisposable
{
    private readonly IslandWindow _w;
    private SettingsStore S => _w.Settings;

    public Features(IslandWindow w)
    {
        _w = w;
        _w.Clipboard.Changed += OnClipboard;
        StartDownloads();
        RunDemos();
    }

    /// <summary>Dev hook: CACAD_DEMO="charging,timer,focus" shows those activities at start, to see them on demand.</summary>
    private void RunDemos()
    {
        var demos = Environment.GetEnvironmentVariable("CACAD_DEMO");
        if (string.IsNullOrEmpty(demos)) return;
        foreach (var d in demos.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            switch (d)
            {
                case "charging": ShowCharging(0.82f); break;
                case "timer": StartTimer(TimeSpan.FromSeconds(95), "Tea"); break;
                case "focus": StartFocus(); break;
            }
    }

    public void Poll()
    {
        PollTimers();
        PollDownloads();
        if (++_tick % 4 == 0)
        {
            PollCharging();
        }
        if (_tick % 8 == 0) PollCall();
    }

    private int _tick;

    // ---------- timers and focus ----------

    private DateTime _timerEnd, _focusEnd;
    private TimeSpan _timerLength, _focusLength;
    private string _timerLabel = "Timer";

    private static readonly SKColor Orange = new(255, 170, 70), Green = new(90, 220, 140), Red = new(255, 95, 87);

    public void PromptTimer()
    {
        string? text = _w.Prompt("Timer", "Minutes, e.g. 5, 1:30, 90s or 1h");
        if (string.IsNullOrWhiteSpace(text)) return;
        var d = ParseDuration(text);
        if (d == null || d.Value.TotalSeconds < 1) { _w.Toast("x", "Try 5, 1:30, 90s or 1h"); return; }
        StartTimer(d.Value, "Timer");
    }

    /// <summary>"5" (minutes), "1:30", "90s", "2m", "1h", "1h 20m".</summary>
    public static TimeSpan? ParseDuration(string s)
    {
        s = s.Trim().ToLowerInvariant();
        if (Regex.IsMatch(s, @"^\d+(\.\d+)?$")) return TimeSpan.FromMinutes(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
        var colon = Regex.Match(s, @"^(\d+):(\d{1,2})(?::(\d{1,2}))?$");
        if (colon.Success)
        {
            int a = int.Parse(colon.Groups[1].Value), b = int.Parse(colon.Groups[2].Value);
            return colon.Groups[3].Success ? new TimeSpan(a, b, int.Parse(colon.Groups[3].Value)) : new TimeSpan(0, a, b);
        }
        double total = 0;
        bool any = false;
        foreach (Match m in Regex.Matches(s, @"(\d+(?:\.\d+)?)\s*(h|hr|hours?|m|min|mins|minutes?|s|sec|secs|seconds?)"))
        {
            any = true;
            double v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            total += m.Groups[2].Value[0] switch { 'h' => v * 3600, 'm' => v * 60, _ => v };
        }
        return any ? TimeSpan.FromSeconds(total) : null;
    }

    public void StartTimer(TimeSpan length, string label)
    {
        _timerLength = length;
        _timerEnd = DateTime.Now + length;
        _timerLabel = label;
        _w.StartActivity(new LiveActivity
        {
            Id = "timer",
            Priority = 50,
            Icon = "clock",
            Color = Orange,
            Title = () => _timerLabel,
            Value = () => G.FormatTime(Math.Ceiling((_timerEnd - DateTime.Now).TotalSeconds)),
            Progress = () => (float)(1 - (_timerEnd - DateTime.Now).TotalSeconds / _timerLength.TotalSeconds),
            Stop = ("Stop timer", () => _w.EndActivity("timer")),
            Interval = 0.25,
            Width = 260,
        });
    }

    public void StartFocus()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (S.FocusDay != today) { S.FocusDay = today; S.FocusToday = 0; }
        _focusLength = TimeSpan.FromMinutes(Math.Max(1, S.FocusMinutes));
        _focusEnd = DateTime.Now + _focusLength;
        _w.StartActivity(new LiveActivity
        {
            Id = "focus",
            Priority = 55,
            Icon = "leaf",
            Color = Green,
            Title = () => S.FocusToday == 0 ? "Focus" : $"Focus · #{S.FocusToday + 1} today",
            Value = () => G.FormatTime(Math.Ceiling((_focusEnd - DateTime.Now).TotalSeconds)),
            Progress = () => (float)(1 - (_focusEnd - DateTime.Now).TotalSeconds / _focusLength.TotalSeconds),
            Stop = ("Stop focus session", () => _w.EndActivity("focus")),
            Interval = 0.25,
            Width = 280,
        });
        _w.Toast("leaf", $"Focus for {S.FocusMinutes} minutes. You've got this.", Green);
    }

    private void PollTimers()
    {
        var now = DateTime.Now;
        if (_w.FindActivity("timer") != null && now >= _timerEnd)
        {
            _w.EndActivity("timer");
            Chime();
            _w.Toast("clock", $"{_timerLabel} done", Orange, ms: 5000);
            _w.Companion?.Pulse("done");
            _w.Companion?.Pet.Feed("timer");
        }
        if (_w.FindActivity("focus") != null && now >= _focusEnd)
        {
            _w.EndActivity("focus");
            S.FocusToday++;
            S.Save();
            Chime();
            _w.Toast("leaf", $"Focus done · {S.FocusToday} today. Take a 5 minute break.", Green, ms: 6000);
            _w.Companion?.Pulse("done");
            _w.Companion?.Pet.Feed("focus");
        }
    }

    private static void Chime()
    {
        try
        {
            string wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav");
            if (!File.Exists(wav)) wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "chimes.wav");
            if (File.Exists(wav)) new System.Media.SoundPlayer(wav).Play();
            else System.Media.SystemSounds.Asterisk.Play();
        }
        catch { }
    }

    // ---------- colour picker ----------

    public void PickColour()
    {
        _w.Collapse();
        var hex = ColorPicker.Pick();
        if (hex == null) return;
        try { Clipboard.SetText(hex); } catch { }
        // The clipboard listener's smart peek shows the swatch.
    }

    // ---------- downloads ----------

    private static readonly string[] TempExtensions = { ".crdownload", ".part", ".partial", ".download", ".opdownload" };
    private FileSystemWatcher? _dl;
    private readonly Dictionary<string, (long size, DateTime at, double speed)> _downloads = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsTemp(string path) => TempExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static string Downloads()
    {
        try
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p) == 0)
            {
                string s = Marshal.PtrToStringUni(p) ?? "";
                Marshal.FreeCoTaskMem(p);
                if (Directory.Exists(s)) return s;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

    private void StartDownloads()
    {
        try
        {
            string dir = Downloads();
            if (!Directory.Exists(dir)) return;
            _dl = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size, IncludeSubdirectories = false };
            void OnUi(Action a) { try { _w.BeginInvoke(a); } catch { } }
            _dl.Created += (_, e) => { if (IsTemp(e.FullPath)) OnUi(() => Track(e.FullPath)); };
            _dl.Changed += (_, e) => { if (IsTemp(e.FullPath)) OnUi(() => Track(e.FullPath)); };
            _dl.Renamed += (_, e) => OnUi(() => Renamed(e.OldFullPath, e.FullPath));
            _dl.EnableRaisingEvents = true;
        }
        catch { _dl = null; }
    }

    private void Track(string temp)
    {
        if (!S.LiveDownloads || _downloads.ContainsKey(temp)) return;
        _downloads[temp] = (0, DateTime.UtcNow, 0);
    }

    private void Renamed(string from, string to)
    {
        if (IsTemp(to)) { if (_downloads.Remove(from, out var s)) _downloads[to] = s; else Track(to); return; }
        // A temp file renamed to its real name: the download finished.
        if (!_downloads.Remove(from) && !IsTemp(from)) return;
        UpdateDownloadActivity();
        if (!S.LiveDownloads) return;
        string name = Path.GetFileName(to);
        _w.Toast("download", $"Downloaded {name}", Green, action: () => Shell.Open(to), ms: 5000);
        _w.Companion?.Pulse("gift");
    }

    private static string CleanName(string temp)
    {
        string n = Path.GetFileName(temp);
        foreach (var e in TempExtensions) if (n.EndsWith(e, StringComparison.OrdinalIgnoreCase)) n = n[..^e.Length];
        return n.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase) ? "a file" : n;
    }

    private void PollDownloads()
    {
        if (_downloads.Count == 0) return;
        foreach (var path in _downloads.Keys.ToList())
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) { _downloads.Remove(path); continue; }   // cancelled
                var (size, at, speed) = _downloads[path];
                double secs = (DateTime.UtcNow - at).TotalSeconds;
                if (secs >= 0.9)
                {
                    double now = Math.Max(0, (fi.Length - size) / secs);
                    _downloads[path] = (fi.Length, DateTime.UtcNow, speed == 0 ? now : speed * 0.6 + now * 0.4);
                }
            }
            catch { }
        }
        UpdateDownloadActivity();
    }

    private void UpdateDownloadActivity()
    {
        if (_downloads.Count == 0 || !S.LiveDownloads) { _w.EndActivity("download"); return; }
        if (_w.FindActivity("download") != null) return;
        _w.StartActivity(new LiveActivity
        {
            Id = "download",
            Priority = 30,
            Icon = "download",
            Color = new SKColor(110, 170, 255),
            Title = () => _downloads.Count == 1 ? CleanName(_downloads.Keys.First()) : $"{_downloads.Count} downloads",
            Value = () =>
            {
                long total = _downloads.Values.Sum(d => d.size);
                double speed = _downloads.Values.Sum(d => d.speed);
                return speed > 1024 ? $"{Bytes(total)} · {Bytes((long)speed)}/s" : Bytes(total);
            },
            Progress = () => float.NaN,
            Interval = 1 / 20.0,
            Width = 320,
        });
    }

    private static string Bytes(long b) => b >= 1L << 30 ? $"{b / 1073741824.0:0.0} GB" : b >= 1 << 20 ? $"{b / 1048576.0:0.0} MB" : $"{b / 1024.0:0} KB";

    // ---------- calls: an app using the microphone ----------

    private string? _callApp;
    private DateTime _callSince;

    private void PollCall()
    {
        var (app, since) = S.CallTimer ? MicInUse() : (null, DateTime.MinValue);
        if (app == _callApp) return;
        _callApp = app;
        if (app == null) { _w.EndActivity("call"); return; }
        _callSince = since;
        _w.StartActivity(new LiveActivity
        {
            Id = "call",
            Priority = 40,
            Icon = "mic",
            Color = Green,
            Title = () => $"On a call · {_callApp}",
            Value = () => G.FormatTime((DateTime.Now - _callSince).TotalSeconds),
            Stop = ("Mute or unmute microphone", _w.ToggleMic),
            Interval = 1.0,
            Width = 300,
            Draw = DrawCall,
        });
    }

    /// <summary>The call row, its microphone red while muted.</summary>
    private void DrawCall(SKCanvas c, float w, float h, float a, double t)
    {
        var act = _w.FindActivity("call");
        if (act == null) return;
        bool muted = _w.Audio.MicMuted;
        act.Icon = muted ? "mic-off" : "mic";
        act.Color = muted ? Red : Green;
        var draw = act.Draw;
        act.Draw = null;
        ActivityView.Draw(c, act, w, h, a, t);
        act.Draw = draw;
        if (!muted)
        {
            // A soft pulse round the mic while it is live.
            float pulse = 0.5f + 0.5f * MathF.Sin((float)t * 3);
            G.Circle(c, -w / 2 + 20, h / 2, 11.5f + 3 * pulse, Green.WithAlpha(G.A(0.12f * (1 - pulse) * a)));
        }
    }

    /// <summary>Which app is using a microphone right now, from Windows' own privacy record
    /// (Settings › Privacy › Microphone keeps a start and stop time per app; a stop of 0 means "in use").</summary>
    private static (string? app, DateTime since) MicInUse()
    {
        const string root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
        try
        {
            using var mic = Registry.CurrentUser.OpenSubKey(root);
            if (mic == null) return (null, default);
            foreach (var (key, name) in Apps(mic))
            {
                using (key)
                {
                    if (key.GetValue("LastUsedTimeStop") is long stop && stop == 0 && key.GetValue("LastUsedTimeStart") is long start && start > 0)
                    {
                        if (name.Equals("cacad", StringComparison.OrdinalIgnoreCase)) continue;
                        return (Friendly(name), DateTime.FromFileTime(start));
                    }
                }
            }
        }
        catch { }
        return (null, default);
    }

    private static IEnumerable<(RegistryKey key, string name)> Apps(RegistryKey mic)
    {
        using (var np = mic.OpenSubKey("NonPackaged"))
            if (np != null)
                foreach (var sub in np.GetSubKeyNames())
                    if (np.OpenSubKey(sub) is { } k) yield return (k, Path.GetFileNameWithoutExtension(sub.Replace('#', '\\')));
        foreach (var sub in mic.GetSubKeyNames())
            if (sub != "NonPackaged" && mic.OpenSubKey(sub) is { } k) yield return (k, sub.Split('_')[0]);
    }

    private static string Friendly(string name) => name.ToLowerInvariant() switch
    {
        var n when n.Contains("zoom") => "Zoom",
        var n when n.Contains("teams") => "Teams",
        var n when n.Contains("discord") => "Discord",
        var n when n.Contains("slack") => "Slack",
        var n when n.Contains("skype") => "Skype",
        var n when n.Contains("whatsapp") => "WhatsApp",
        var n when n.Contains("telegram") => "Telegram",
        "chrome" => "Chrome",
        "msedge" => "Edge",
        "firefox" => "Firefox",
        "obs64" or "obs32" => "OBS",
        _ => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name,
    };

    // ---------- charging ----------

    private PowerLineStatus _line = SystemInformation.PowerStatus.PowerLineStatus;

    private void PollCharging()
    {
        var ps = SystemInformation.PowerStatus;
        var line = ps.PowerLineStatus;
        bool pluggedIn = line == PowerLineStatus.Online && _line == PowerLineStatus.Offline;
        _line = line;
        var act = _w.FindActivity("charging");
        if (act != null && (DateTime.Now - act.Started).TotalSeconds > 3.4) _w.EndActivity("charging");
        if (!pluggedIn || !S.ChargingAnimation) return;
        bool battery = ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;
        if (!battery) return;
        ShowCharging(Math.Clamp(ps.BatteryLifePercent, 0f, 1f));
    }

    /// <summary>A green surge sweeps across the pill and leaves it filled to the battery level.</summary>
    public void ShowCharging(float level)
    {
        _w.Companion?.Pulse("flex");
        var start = DateTime.Now;
        _w.StartActivity(new LiveActivity
        {
            Id = "charging",
            Priority = 90,
            Width = 290,
            Interval = 0,
            Draw = (c, w, h, a, t) =>
            {
                float u = (float)Math.Clamp((DateTime.Now - start).TotalSeconds / 3.2, 0, 1);
                float L = -w / 2;
                // The fill rises to the level...
                float fillTo = L + w * level * Math.Min(1, u * 2.2f);
                using (var fill = SKShader.CreateLinearGradient(new SKPoint(L, 0), new SKPoint(fillTo + 1, 0),
                           new[] { Green.WithAlpha(G.A(0.05f * a)), Green.WithAlpha(G.A(0.32f * a)) }, null, SKShaderTileMode.Clamp))
                using (var p = new SKPaint { Shader = fill })
                    c.DrawRect(L, 0, fillTo - L, h, p);
                // ...while a bright surge runs right across, once.
                float sx = L - 60 + (w + 120) * MathF.Min(1, u * 1.4f);
                using (var surge = SKShader.CreateLinearGradient(new SKPoint(sx - 60, 0), new SKPoint(sx + 20, 0),
                           new[] { Green.WithAlpha(0), Green.WithAlpha(G.A(0.75f * a)), SKColors.White.WithAlpha(G.A(0.9f * a)), Green.WithAlpha(0) },
                           new[] { 0f, 0.7f, 0.85f, 1f }, SKShaderTileMode.Clamp))
                using (var p = new SKPaint { Shader = surge, BlendMode = SKBlendMode.Plus })
                    c.DrawRect(sx - 60, 0, 80, h, p);
                Icons.Draw(c, "zap", L + 20, h / 2, 15, Green.WithAlpha(G.A(a)), 2f);
                using var font = Fonts.Med(13);
                G.Text(c, "Charging", L + 38, h / 2 + 4.6f, font, G.Paper(a));
                using var big = Fonts.Title(15);
                G.Text(c, $"{level * 100:0}%", w / 2 - 14, h / 2 + 5.2f, big, Green.WithAlpha(G.A(a)), SKTextAlign.Right);
            },
        });
    }

    // ---------- idle mode ----------

    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>Seconds since the last keyboard or mouse input anywhere.</summary>
    public static double IdleSeconds()
    {
        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref li)) return 0;
        return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
    }

    // ---------- smart clipboard peeks ----------

    private ClipItem? _lastPeek;

    private void OnClipboard()
    {
        if (!S.SmartPeeks) return;
        var item = _w.Clipboard.Items.FirstOrDefault();
        void Idea() => _w.Companion?.Pulse("idea");
        if (item == null || ReferenceEquals(item, _lastPeek) || (DateTime.Now - item.At).TotalSeconds > 2) return;
        _lastPeek = item;
        if (item.Kind is not (ClipKind.Text or ClipKind.Link)) return;
        string text = item.Text.Trim();

        if (ParseColour(text) is { } colour)
        {
            Idea(); _w.Toast("swatch", $"{text} copied", colour, ms: 3000);
            return;
        }
        var code = Regex.Match(text, @"(?i)(?:code|otp|verification|passcode|password|pin)\D{0,24}?(\d{4,8})\b");
        if (code.Success && text.Length > code.Groups[1].Value.Length + 2)
        {
            string digits = code.Groups[1].Value;
            Idea(); _w.Toast("copy", $"Tap to copy the code {digits}", null, () =>
            {
                try { Clipboard.SetText(digits); _w.Toast("check", $"Code {digits} copied"); } catch { }
            });
            return;
        }
        if (Regex.IsMatch(text, @"^[\w.+-]+@[\w-]+(\.[\w-]+)+$"))
        {
            Idea(); _w.Toast("message-circle", $"Tap to write to {text}", null, () => Shell.Open("mailto:" + text));
            return;
        }
        if (text.Length < 160 && Regex.IsMatch(text, @"(?i)^\d+[\w\s,./#-]{4,}\b(street|st|road|rd|avenue|ave|lane|ln|drive|dr|boulevard|blvd|way|place|pl|court|ct|nagar|colony|marg|sector|layout)\b"))
        {
            Idea(); _w.Toast("globe", "Tap to open this address in Maps", null,
                () => Shell.Open("https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(text)));
        }
    }

    /// <summary>#RGB, #RRGGBB, RRGGBB, or rgb(r, g, b).</summary>
    public static SKColor? ParseColour(string s)
    {
        var hex = Regex.Match(s, @"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$|^([0-9a-fA-F]{6})$");
        if (hex.Success)
        {
            string h = hex.Groups[1].Success ? hex.Groups[1].Value : hex.Groups[2].Value;
            if (h.Length == 3) h = string.Concat(h.Select(ch => $"{ch}{ch}"));
            // Six bare digits are more likely a code than a colour.
            if (!s.StartsWith('#') && h.All(char.IsDigit)) return null;
            return new SKColor(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..], 16));
        }
        var rgb = Regex.Match(s, @"(?i)^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})");
        if (rgb.Success)
            return new SKColor((byte)Math.Min(255, int.Parse(rgb.Groups[1].Value)), (byte)Math.Min(255, int.Parse(rgb.Groups[2].Value)), (byte)Math.Min(255, int.Parse(rgb.Groups[3].Value)));
        return null;
    }

    public void Dispose()
    {
        _w.Clipboard.Changed -= OnClipboard;
        _dl?.Dispose();
    }
}
