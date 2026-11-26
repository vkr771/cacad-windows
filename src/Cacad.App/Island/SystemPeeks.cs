using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SkiaSharp;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Cacad;

/// <summary>What the PC does, shown in the pill the way a phone's Dynamic Island does:
/// a volume and brightness bar instead of Windows' own pop-up, Caps Lock and Num Lock, Wi-Fi joining and dropping,
/// Bluetooth devices connecting, USB drives plugged in, and a screenshot's thumbnail right after you take one.</summary>
public sealed class SystemPeeks : IDisposable
{
    private readonly IslandWindow _w;
    private SettingsStore S => _w.Settings;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };

    public SystemPeeks(IslandWindow w)
    {
        _w = w;
        _timer.Tick += (_, _) => UiProfile.Time("peeks", Tick);
        _timer.Start();
        WatchBrightness();
        NetworkChange.NetworkAddressChanged += (_, _) => _w.BeginInvoke(() => _netDue = DateTime.Now.AddSeconds(2));
        NetworkChange.NetworkAvailabilityChanged += (_, _) => _w.BeginInvoke(() => _netDue = DateTime.Now.AddSeconds(2));
        _net = NetName();
        WatchBluetooth();
        _drives = Drives();
        _w.Clipboard.Changed += OnClipboard;
        WatchScreenshotFolder();
    }

    private int _tick;

    private void Tick()
    {
        _tick++;
        PollVolume();
        PollLocks();
        EndHud();
        if (_tick % 20 == 0) PollDrives();
        if (_tick % 20 == 5) HideWindowsOsd();
        if (DateTime.Now >= _netDue) { _netDue = DateTime.MaxValue; PollNetwork(); }
    }

    // ---------- 1. volume and brightness ----------

    private float _vol = -1;
    private bool _mute;
    private DateTime _hudUntil = DateTime.MinValue;
    private string _hudKind = "volume";
    private float _hudValue;
    private readonly Spring _hudBar = new(0, (420, 40), 0.002);
    private double _hudLast;

    private void PollVolume()
    {
        if (!S.SystemHud) return;
        float v;
        bool m;
        try { v = _w.Audio.Volume; m = _w.Audio.Muted; } catch { return; }
        if (_vol < 0) { _vol = v; _mute = m; return; }
        if (Math.Abs(v - _vol) < 0.004f && m == _mute) return;
        _vol = v;
        _mute = m;
        // Changes made in the card (its own slider) need no bar.
        if (_w.CardOpen) return;
        ShowHud(m ? "mute" : "volume", m ? 0 : v);
    }

    private ManagementEventWatcher? _brightWatch;

    private void WatchBrightness()
    {
        try
        {
            _brightWatch = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _brightWatch.EventArrived += (_, e) =>
            {
                try
                {
                    int level = Convert.ToInt32(e.NewEvent["Brightness"]);
                    _w.BeginInvoke(() => { if (S.SystemHud && !_w.CardOpen) ShowHud("brightness", level / 100f); });
                }
                catch { }
            };
            _brightWatch.Start();
        }
        catch { _brightWatch = null; } // a desktop monitor: no brightness events
    }

    private void ShowHud(string kind, float value)
    {
        bool fresh = _w.FindActivity("hud") == null || _hudKind != kind;
        _hudKind = kind;
        _hudValue = value;
        _hudUntil = DateTime.Now.AddSeconds(1.6);
        if (fresh) _hudBar.Snap(value);
        _hudBar.To(value);
        if (!fresh) { _w.Invalidate(); return; }
        _w.StartActivity(new LiveActivity
        {
            Id = "hud",
            Priority = 90,
            Width = 290,
            Interval = 1 / 60.0,
            Draw = DrawHud,
        });
    }

    private void EndHud()
    {
        if (DateTime.Now > _hudUntil && _w.FindActivity("hud") != null) _w.EndActivity("hud");
    }

    private void DrawHud(SKCanvas c, float w, float h, float a, double t)
    {
        double dt = Math.Clamp(t - _hudLast, 0, 0.1);
        _hudLast = t;
        if (_hudBar.Step(dt)) _w.Invalidate();
        float v = (float)Math.Clamp(_hudBar.Value, 0, 1);
        float L = -w / 2, R = w / 2, cy = h / 2;
        string icon = _hudKind switch
        {
            "mute" => "volume-x",
            "brightness" => "sun",
            _ => "volume-2",
        };
        Icons.Draw(c, _hudKind == "volume" ? VolumeIcon(v) : icon, L + 22, cy, 15, G.Paper(a));
        float bl = L + 42, br = R - 50, bh = 6;
        var track = SKRect.Create(bl, cy - bh / 2, br - bl, bh);
        G.Fill(c, track, bh / 2, G.White(0.16f * a));
        if (v > 0.002f)
        {
            var color = _hudKind == "brightness" ? new SKColor(255, 214, 102) : G.PaperBase;
            G.Fill(c, SKRect.Create(bl, cy - bh / 2, Math.Max(bh, (br - bl) * v), bh), bh / 2, color.WithAlpha(G.A(a)));
        }
        using var font = Fonts.Mono(11.5f);
        string label = _hudKind == "mute" ? "Muted" : $"{Math.Round(_hudValue * 100)}";
        G.Text(c, label, R - 16, cy + 4, font, G.Paper(0.8f * a), SKTextAlign.Right);
    }

    private static string VolumeIcon(float v) => v <= 0.001f ? "volume-x" : v < 0.5f ? "volume-1" : "volume-2";

    // Windows 10 draws its own volume pop-up in a hidden "NativeHWNDHost" window. Minimising that window hides the
    // pop-up for as long as cacad runs (it is restored when cacad quits or the setting is turned off). Windows 11
    // draws its pop-up differently, so there this does nothing and both show.
    private IntPtr _osd;
    private bool _osdHidden;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);

    private void HideWindowsOsd()
    {
        bool want = S.SystemHud && S.HideWindowsOsd && Environment.OSVersion.Version.Build < 22000;
        if (!want) { RestoreOsd(); return; }
        if (_osdHidden && IsWindow(_osd)) return;
        IntPtr h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, "NativeHWNDHost", "")) != IntPtr.Zero)
            if (FindWindowEx(h, IntPtr.Zero, "DirectUIHWND", "") != IntPtr.Zero) break;
        if (h == IntPtr.Zero) return; // appears after the first volume key press; looked for again in two seconds
        _osd = h;
        ShowWindow(h, 6); // SW_MINIMIZE
        _osdHidden = true;
    }

    private void RestoreOsd()
    {
        if (!_osdHidden) return;
        if (IsWindow(_osd)) ShowWindow(_osd, 9); // SW_RESTORE
        _osdHidden = false;
    }

    // ---------- 5. Caps Lock, Num Lock, Wi-Fi ----------

    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    private int _locks = -1;

    private void PollLocks()
    {
        int caps = GetKeyState(0x14) & 1, num = GetKeyState(0x90) & 1, now = caps | num << 1;
        if (_locks < 0) { _locks = now; return; }
        if (now == _locks) return;
        int changed = now ^ _locks;
        _locks = now;
        if (!S.LockPeeks) return;
        if ((changed & 1) != 0) _w.Toast("type", caps == 1 ? "Caps Lock on" : "Caps Lock off", caps == 1 ? new SKColor(255, 196, 80) : null, null, 1400);
        else _w.Toast("lock", num == 1 ? "Num Lock on" : "Num Lock off", null, null, 1400);
    }

    private string _net;
    private DateTime _netDue = DateTime.MaxValue;

    private static string NetName()
    {
        try
        {
            var p = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (p == null) return "";
            if (p.IsWlanConnectionProfile) return "wifi:" + p.ProfileName;
            return "other:" + p.ProfileName;
        }
        catch { return ""; }
    }

    private void PollNetwork()
    {
        string now = NetName();
        if (now == _net) return;
        string was = _net;
        _net = now;
        if (!S.NetworkPeeks) return;
        if (now.Length == 0) { _w.Toast("wifi", "You're offline", new SKColor(255, 95, 87), null, 2600); return; }
        string name = now[(now.IndexOf(':') + 1)..];
        if (now.StartsWith("wifi:")) _w.Toast("wifi", $"Connected to {name}", new SKColor(90, 200, 250), null, 2400);
        else _w.Toast("globe", was.Length == 0 ? "Back online" : $"Connected · {name}", new SKColor(90, 200, 250), null, 2400);
    }

    // ---------- 2. Bluetooth devices and USB drives ----------

    private DeviceWatcher? _bt;
    private bool _btReady;
    private readonly Dictionary<string, string> _btNames = new();

    private void WatchBluetooth()
    {
        try
        {
            string sel = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            _bt = DeviceInformation.CreateWatcher(sel);
            _bt.Added += (_, d) =>
            {
                lock (_btNames) _btNames[d.Id] = d.Name;
                if (_btReady) _w.BeginInvoke(() => Device(d.Name, true));
            };
            _bt.Removed += (_, d) =>
            {
                string? name;
                lock (_btNames) { _btNames.TryGetValue(d.Id, out name); _btNames.Remove(d.Id); }
                if (_btReady && name != null) _w.BeginInvoke(() => Device(name, false));
            };
            _bt.Updated += (_, _) => { };
            _bt.EnumerationCompleted += (_, _) => _btReady = true;   // devices already connected are not news
            _bt.Start();
        }
        catch { _bt = null; }
    }

    private void Device(string name, bool connected)
    {
        if (!S.DevicePeeks || string.IsNullOrWhiteSpace(name)) return;
        _w.Toast("bluetooth", connected ? $"{name} connected" : $"{name} disconnected", connected ? new SKColor(90, 160, 255) : null, null, 2600);
        if (connected) _w.Companion?.Pulse("wave");
    }

    private Dictionary<string, string> _drives;

    private static Dictionary<string, string> Drives()
    {
        var d = new Dictionary<string, string>();
        try
        {
            foreach (var di in DriveInfo.GetDrives())
                if (di.DriveType == DriveType.Removable && di.IsReady)
                    d[di.Name] = string.IsNullOrWhiteSpace(di.VolumeLabel) ? "USB drive" : di.VolumeLabel;
        }
        catch { }
        return d;
    }

    private void PollDrives()
    {
        var now = Drives();
        if (S.DevicePeeks)
        {
            foreach (var (root, label) in now)
                if (!_drives.ContainsKey(root))
                {
                    string r = root;
                    _w.Toast("folder-open", $"{label} ({root.TrimEnd('\\')})  ·  open", new SKColor(120, 200, 140),
                        () => { try { Process.Start(new ProcessStartInfo("explorer.exe", r) { UseShellExecute = true }); } catch { } }, 4000);
                }
            foreach (var (root, label) in _drives)
                if (!now.ContainsKey(root)) _w.Toast("folder", $"{label} removed", null, null, 1800);
        }
        _drives = now;
    }

    // ---------- 3. screenshots ----------

    private static readonly System.Text.RegularExpressions.Regex Snippers = new("snip|clipping|screen ?sketch|screenshot|sharex|greenshot|lightshot|flameshot|capture",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private DateTime _shotAt = DateTime.MinValue;
    private FileSystemWatcher? _shotDir;

    private void OnClipboard()
    {
        if (!S.ScreenshotPeeks) return;
        var item = _w.Clipboard.Items.FirstOrDefault();
        if (item is not { Kind: ClipKind.Image, Image: not null } || (DateTime.Now - item.At).TotalSeconds > 2) return;
        // Copied by a screenshot tool (Win+Shift+S, Snipping Tool, ShareX…) or just after Print Screen.
        bool snip = Snippers.IsMatch(item.Source) || (DateTime.Now - Companion.LastPrintScreen).TotalSeconds < 4;
        if (!snip) return;
        ShowShot(item.Image, item.ImagePath);
    }

    private void WatchScreenshotFolder()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            if (!Directory.Exists(dir)) return;
            _shotDir = new FileSystemWatcher(dir, "*.png") { EnableRaisingEvents = true };
            _shotDir.Created += (_, e) => _w.BeginInvoke(async () =>
            {
                if (!S.ScreenshotPeeks || (DateTime.Now - _shotAt).TotalSeconds < 4) return; // already shown from the clipboard
                await Task.Delay(400); // let Windows finish writing it
                try
                {
                    using var bmp = SKBitmap.Decode(e.FullPath);
                    if (bmp == null) return;
                    using var small = bmp.Resize(new SKImageInfo(Math.Max(1, bmp.Width / 8), Math.Max(1, bmp.Height / 8)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    ShowShot(SKImage.FromBitmap(small ?? bmp), e.FullPath);
                }
                catch { }
            });
        }
        catch { _shotDir = null; }
    }

    private void ShowShot(SKImage image, string? path)
    {
        _shotAt = DateTime.Now;
        _w.Companion?.Pulse("screenshot");
        var shown = DateTime.Now;
        string? file = path;
        _w.StartActivity(new LiveActivity
        {
            Id = "shot",
            Priority = 70,
            Width = 330,
            Interval = 1 / 30.0,
            Click = () =>
            {
                if (file != null && File.Exists(file)) try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { }
                _w.EndActivity("shot");
            },
            Stop = ("Save to Pictures › Screenshots", () =>
            {
                try
                {
                    if (file == null || !File.Exists(file)) return;
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                    Directory.CreateDirectory(dir);
                    string to = Path.Combine(dir, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
                    if (!file.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) File.Copy(file, to);
                    _w.Toast("check", "Saved to Screenshots", new SKColor(120, 200, 140));
                }
                catch { }
                _w.EndActivity("shot");
            }),
            Draw = (c, w, h, a, t) =>
            {
                // A flash as it lands, then the thumbnail and what you can do with it.
                double age = (DateTime.Now - shown).TotalSeconds;
                if (age > 5.5) { _w.BeginInvoke(() => _w.EndActivity("shot")); }
                float L = -w / 2, R = w / 2, cy = h / 2;
                float th = h - 12, tw = Math.Clamp(th * image.Width / Math.Max(1f, image.Height), th * 0.8f, th * 2.2f);
                var r = SKRect.Create(L + 10, cy - th / 2, tw, th);
                G.ImageCover(c, image, r, a, 5);
                G.Stroke(c, r, 5, G.White(0.35f * a), 1);
                float flash = (float)Math.Clamp(1 - age / 0.35, 0, 1);
                if (flash > 0) G.Fill(c, r, 5, G.White(0.8f * flash * a));
                using var title = Fonts.Med(12.5f);
                using var sub = Fonts.Sub(10.5f);
                float x = r.Right + 10;
                G.Text(c, "Screenshot copied", x, cy - 1.5f, title, G.Paper(a));
                G.Text(c, "Click to open  ·  right-click to save", x, cy + 12, sub, G.Paper(0.55f * a));
                Icons.Draw(c, "camera", R - 18, cy, 14, G.Paper(0.5f * a));
            },
        });
    }

    public void Dispose()
    {
        _timer.Dispose();
        RestoreOsd();
        try { _brightWatch?.Stop(); _brightWatch?.Dispose(); } catch { }
        try { _bt?.Stop(); } catch { }
        _shotDir?.Dispose();
    }
}
