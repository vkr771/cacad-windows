using SkiaSharp;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Cacad;

/// <summary>Windows notifications, mirrored on the island: when an app shows a toast (WhatsApp, Discord, Mail…), the
/// pill grows into a banner with the app's icon, who it is from and the message, for a few seconds. Click it to
/// open the notification centre. Reads Windows' notification list (with your permission in Windows Settings ›
/// Privacy › Notifications); nothing leaves the PC.</summary>
public sealed class Notifications : IDisposable
{
    private readonly IslandWindow _w;
    private SettingsStore S => _w.Settings;
    private readonly HashSet<uint> _seen = new();
    private readonly Dictionary<string, SKImage?> _icons = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<Item> _queue = new();
    private Item? _showing;
    private DateTime _showUntil;
    private bool _seeded;

    public sealed record Item(string App, string Title, string Body, SKImage? Icon, DateTime At);

    /// <summary>The last few, newest first (for the card).</summary>
    public List<Item> Recent { get; } = new();
    public string Status { get; private set; } = "starting";

    public Notifications(IslandWindow w)
    {
        _w = w;
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        UserNotificationListener? listener = null;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (!S.Notifications) { await Task.Delay(2000, _stop.Token); continue; }
                listener ??= UserNotificationListener.Current;
                var access = listener.GetAccessStatus();
                if (access == UserNotificationListenerAccessStatus.Unspecified)
                    access = await listener.RequestAccessAsync();
                Status = access.ToString();
                if (access != UserNotificationListenerAccessStatus.Allowed) { await Task.Delay(10000, _stop.Token); continue; }

                var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                var fresh = new List<Item>();
                foreach (var n in list)
                {
                    if (!_seen.Add(n.Id) || !_seeded) continue;
                    // Only what just arrived (the list also holds older ones still in the notification centre).
                    if ((DateTimeOffset.Now - n.CreationTime).TotalSeconds > 30) continue;
                    var item = Read(n);
                    Log($"new {n.Id}: {item}");
                    if (item != null) fresh.Add(item);
                }
                _seeded = true;
                if (fresh.Count > 0) _w.BeginInvoke(() => Arrived(fresh));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Status = "error: " + ex.Message; Log(ex.ToString()); }
            // Z3R: while you are away from the PC (no input for a minute) Windows' list is read every 5 s instead
            // of every 1.2 s; it is back to 1.2 s the moment you touch the keyboard or mouse.
            int wait = Z3R.On && Features.IdleSeconds() > 60 ? 5000 : 1200;
            try { await Task.Delay(wait, _stop.Token); } catch { break; }
        }
    }

    private Item? Read(UserNotification n)
    {
        string app = "";
        try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; } catch { }
        if (app.Equals("cacad", StringComparison.OrdinalIgnoreCase)) return null;
        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        var texts = binding?.GetTextElements().Select(t => Clean(t.Text)).Where(t => t.Length > 0).ToList() ?? new();
        if (texts.Count == 0) return null;
        string title = texts[0], body = string.Join("  ", texts.Skip(1));
        if (app.Length == 0) app = title;
        return new Item(app, title, body, Icon(n, app), DateTime.Now);
    }

    /// <summary>Emoji and other symbols the pill's font cannot draw are dropped, and line breaks become spaces.</summary>
    private static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (char.IsSurrogate(ch) || ch is >= '︀' and <= '️' || ch == '‍') continue;
            sb.Append(char.IsControl(ch) ? ' ' : ch);
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private SKImage? Icon(UserNotification n, string app)
    {
        lock (_icons)
            if (_icons.TryGetValue(app, out var cached)) return cached;
        SKImage? img = null;
        try
        {
            var stream = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64)).OpenReadAsync().AsTask().GetAwaiter().GetResult();
            using var s = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            img = SKImage.FromEncodedData(ms.ToArray());
        }
        catch { }
        lock (_icons) _icons[app] = img;
        return img;
    }

    // ---------- on the island (UI thread) ----------

    private void Arrived(List<Item> items)
    {
        foreach (var it in items)
        {
            Recent.Insert(0, it);
            if (S.NotificationsMuted.Contains(it.App)) continue;
            // A focus session keeps them quiet (they still wait in Windows' notification centre).
            if (_w.FindActivity("focus") != null) continue;
            _queue.Enqueue(it);
        }
        if (Recent.Count > 20) Recent.RemoveRange(20, Recent.Count - 20);
        if (_queue.Count > 0) _w.Companion?.Pulse("ping");
        Next();
    }

    /// <summary>Show the next banner, or end when the queue is empty. Each stays about five seconds.</summary>
    private void Next()
    {
        if (_showing != null && DateTime.Now < _showUntil) return;
        if (_queue.Count == 0)
        {
            if (_showing != null) { _showing = null; _w.EndActivity("notify"); }
            return;
        }
        var it = _showing = _queue.Dequeue();
        _showUntil = DateTime.Now.AddSeconds(5.5);
        using var titleFont = Fonts.Med(12.5f);
        using var bodyFont = Fonts.Sub(11.5f);
        string body = S.NotificationsPrivate ? "New message" : it.Body;
        float textW = Math.Max(G.Measure(titleFont, Title(it)), G.Measure(bodyFont, body));
        _w.StartActivity(new LiveActivity
        {
            Id = "notify",
            Priority = 50,
            Width = Math.Clamp(textW + 86, 300, 440),
            Interval = 1 / 20.0,
            Click = () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-actioncenter:") { UseShellExecute = true }); } catch { } Dismiss(); },
            Stop = ($"Mute {it.App}", () => { if (!S.NotificationsMuted.Contains(it.App)) S.NotificationsMuted.Add(it.App); S.Save(); Dismiss(); }),
            Draw = (c, w, h, a, t) => DrawBanner(c, it, body, w, h, a),
        });
    }

    private void Dismiss() { _showUntil = DateTime.MinValue; Next(); }

    /// <summary>Called from the island's housekeeping poll.</summary>
    public void Poll() => Next();

    private string Title(Item it) => it.Title == it.App ? it.App : $"{it.Title}";

    private void DrawBanner(SKCanvas c, Item it, string body, float w, float h, float a)
    {
        float L = -w / 2, R = w / 2, cy = h / 2;
        var icon = SKRect.Create(L + 8, cy - 12, 24, 24);
        if (it.Icon != null) G.Image(c, it.Icon, icon, a, 6);
        else
        {
            G.Fill(c, icon, 6, G.White(0.14f * a));
            Icons.Draw(c, "bell", icon.MidX, icon.MidY, 13, G.Paper(0.8f * a));
        }
        float x = icon.Right + 9, room = R - 14 - x;
        using var appFont = Fonts.Sub(10);
        string app = it.App.ToUpperInvariant();
        float appW = Math.Min(G.Measure(appFont, app), room * 0.45f);
        using var titleFont = Fonts.Med(12.5f);
        using var bodyFont = Fonts.Sub(11.5f);
        if (body.Length == 0)
        {
            G.Text(c, G.Ellipsize(Title(it), titleFont, room - appW - 8), x, cy + 4.5f, titleFont, G.Paper(a));
            G.Text(c, G.Ellipsize(app, appFont, appW), R - 14, cy + 4, appFont, G.Paper(0.45f * a), SKTextAlign.Right);
            return;
        }
        G.Text(c, G.Ellipsize(Title(it), titleFont, room - appW - 8), x, cy - 1.5f, titleFont, G.Paper(a));
        G.Text(c, G.Ellipsize(app, appFont, appW), R - 14, cy - 2, appFont, G.Paper(0.45f * a), SKTextAlign.Right);
        G.Text(c, G.Ellipsize(body, bodyFont, room), x, cy + 12, bodyFont, G.Paper(0.68f * a));
    }

    private static readonly bool Logging = Environment.GetEnvironmentVariable("CACAD_NOTIFYLOG") == "1";
    private static void Log(string s)
    {
        if (Logging) try { File.AppendAllText(Path.Combine(SettingsStore.DataDir, "notify.log"), $"{DateTime.Now:HH:mm:ss} {s}\r\n"); } catch { }
    }

    public void Dispose() => _stop.Cancel();
}
