using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SkiaSharp;
using Windows.Devices.Radios;

namespace Cacad;

/// <summary>CPU, memory and battery, sampled once a second while something shows them.</summary>
public sealed class SystemStats
{
    private ulong _idle, _kernel, _user;
    private DateTime _at;

    public float Cpu { get; private set; }
    public float Ram { get; private set; }
    public double RamUsedGb { get; private set; }
    public double RamTotalGb { get; private set; }

    public void Sample()
    {
        if ((DateTime.UtcNow - _at).TotalMilliseconds < 900) return;
        _at = DateTime.UtcNow;
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong i = idle.Value, k = kernel.Value, u = user.Value;
            ulong di = i - _idle, dk = k - _kernel, du = u - _user;
            ulong total = dk + du;
            if (_idle != 0 && total > 0) Cpu = Math.Clamp(1f - (float)di / total, 0f, 1f);
            _idle = i; _kernel = k; _user = u;
        }
        var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref mem))
        {
            Ram = mem.dwMemoryLoad / 100f;
            RamTotalGb = mem.ullTotalPhys / 1073741824.0;
            RamUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
        }
    }

    public static (bool hasBattery, float level, bool charging) Battery()
    {
        var ps = SystemInformation.PowerStatus;
        bool has = ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;
        return (has, Math.Clamp(ps.BatteryLifePercent, 0f, 1f), ps.PowerLineStatus == PowerLineStatus.Online);
    }
}

/// <summary>Built-in display brightness through WMI. Desktop monitors usually do not expose it.</summary>
public sealed class BrightnessService
{
    private int _pending = -1;
    private bool _busy;

    public bool Supported { get; private set; } = true;
    public int Level { get; private set; } = -1;

    public BrightnessService() => Task.Run(Read);

    private void Read()
    {
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"root\WMI"), new SelectQuery("WmiMonitorBrightness"));
            foreach (ManagementObject o in s.Get()) { Level = Convert.ToInt32(o["CurrentBrightness"]); return; }
            Supported = false;
        }
        catch { Supported = false; }
    }

    public void Refresh() { if (Supported && !_busy) Task.Run(Read); }

    /// <summary>Set without blocking; rapid drags coalesce to the latest value.</summary>
    public void Set(int level)
    {
        if (!Supported) return;
        Level = Math.Clamp(level, 0, 100);
        _pending = Level;
        if (_busy) return;
        _busy = true;
        Task.Run(() =>
        {
            try
            {
                while (_pending >= 0)
                {
                    int v = _pending;
                    _pending = -1;
                    using var s = new ManagementObjectSearcher(new ManagementScope(@"root\WMI"), new SelectQuery("WmiMonitorBrightnessMethods"));
                    foreach (ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)v });
                }
            }
            catch { Supported = false; }
            finally { _busy = false; }
        });
    }
}

/// <summary>Wi-Fi and Bluetooth radios (Windows.Devices.Radios). Falls back to read-only state when access is refused.</summary>
public sealed class RadioService
{
    private Radio? _wifi, _bt;
    private bool _ready;

    public RadioService() => _ = InitAsync();

    private async Task InitAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return;
            foreach (var r in await Radio.GetRadiosAsync())
            {
                if (r.Kind == RadioKind.WiFi) _wifi ??= r;
                if (r.Kind == RadioKind.Bluetooth) _bt ??= r;
            }
            _ready = true;
        }
        catch { }
    }

    public bool HasWifi => _wifi != null || NetworkInterface.GetAllNetworkInterfaces().Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
    public bool HasBluetooth => _bt != null;

    public bool WifiOn => _wifi?.State == RadioState.On || (_wifi == null && NetworkInterface.GetAllNetworkInterfaces()
        .Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up));
    public bool BluetoothOn => _bt?.State == RadioState.On;

    public string WifiName
    {
        get
        {
            var n = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(i => i.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && i.OperationalStatus == OperationalStatus.Up);
            if (n != null) return "On";
            var eth = NetworkInterface.GetAllNetworkInterfaces().Any(i => i.NetworkInterfaceType == NetworkInterfaceType.Ethernet && i.OperationalStatus == OperationalStatus.Up);
            return WifiOn ? "On" : eth ? "Ethernet" : "Off";
        }
    }

    public async void ToggleWifi()
    {
        if (!_ready || _wifi == null) { Shell.Open("ms-settings:network-wifi"); return; }
        try { await _wifi.SetStateAsync(_wifi.State == RadioState.On ? RadioState.Off : RadioState.On); }
        catch { Shell.Open("ms-settings:network-wifi"); }
    }

    public async void ToggleBluetooth()
    {
        if (!_ready || _bt == null) { Shell.Open("ms-settings:bluetooth"); return; }
        try { await _bt.SetStateAsync(_bt.State == RadioState.On ? RadioState.Off : RadioState.On); }
        catch { Shell.Open("ms-settings:bluetooth"); }
    }
}

/// <summary>The Windows power mode slider (Best power efficiency / Balanced / Best performance).</summary>
public static class PowerMode
{
    private static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid Balanced = Guid.Empty;
    private static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid mode);

    /// <summary>0 = efficiency, 1 = balanced, 2 = performance, -1 = unavailable.</summary>
    public static int Get()
    {
        try
        {
            if (PowerGetEffectiveOverlayScheme(out var g) != 0) return -1;
            return g == Efficiency ? 0 : g == Performance ? 2 : 1;
        }
        catch { return -1; }
    }

    public static void Set(int mode)
    {
        try { PowerSetActiveOverlayScheme(mode == 0 ? Efficiency : mode == 2 ? Performance : Balanced); } catch { }
    }
}

public static class Shell
{
    public static void Open(string target, string? args = null)
    {
        try { Process.Start(new ProcessStartInfo(target, args ?? "") { UseShellExecute = true }); } catch { }
    }

    public static void Reveal(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }
}

/// <summary>Shell thumbnails and icons for files and apps, cached by path.</summary>
public static class ShellImages
{
    private static readonly Dictionary<string, SKImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SKImage? Get(string path, int size = 96)
    {
        string key = path + "|" + size;
        if (Cache.TryGetValue(key, out var img)) return img;
        img = Load(path, size);
        Cache[key] = img;
        return img;
    }

    private static SKImage? Load(string path, int size)
    {
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            var iid = typeof(Native.IShellItemImageFactory).GUID;
            if (Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null) return null;
            if (factory.GetImage(new Native.SIZE { Cx = size, Cy = size }, 0, out hbmp) != 0 || hbmp == IntPtr.Zero) return null;
            return FromHBitmap(hbmp);
        }
        catch { return null; }
        finally { if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp); }
    }

    private static SKImage? FromHBitmap(IntPtr hbmp)
    {
        var bm = new Native.BITMAP();
        if (Native.GetObject(hbmp, Marshal.SizeOf<Native.BITMAP>(), ref bm) == 0 || bm.bmWidth <= 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bi = new Native.BITMAPINFO
        {
            BmiHeader = new Native.BITMAPINFOHEADER { BiSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), BiWidth = w, BiHeight = -h, BiPlanes = 1, BiBitCount = 32 },
        };
        IntPtr dc = Native.GetDC(IntPtr.Zero);
        Native.GetDIBits(dc, hbmp, 0, (uint)h, bmp.GetPixels(), ref bi, 0);
        Native.ReleaseDC(IntPtr.Zero, dc);

        // Thumbnails without alpha come back with A = 0 everywhere; make those opaque.
        unsafe
        {
            byte* p = (byte*)bmp.GetPixels();
            int n = w * h;
            bool anyAlpha = false;
            for (int i = 0; i < n; i++) if (p[i * 4 + 3] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 0; i < n; i++) p[i * 4 + 3] = 255;
        }
        var img = SKImage.FromBitmap(bmp);
        bmp.Dispose();
        return img;
    }
}

/// <summary>Detects a fullscreen app or game on the island's monitor.</summary>
public static class Fullscreen
{
    public static bool Active(Rectangle monitor)
    {
        try
        {
            if (Native.SHQueryUserNotificationState(out int state) == 0 && (state == 3 /* D3D fullscreen */ || state == 4 /* presentation */))
                return true;
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Native.GetShellWindow() || fg == Native.GetDesktopWindow()) return false;
            var cls = new System.Text.StringBuilder(64);
            Native.GetClassName(fg, cls, 64);
            string c = cls.ToString();
            if (c is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;
            if (!Native.GetWindowRect(fg, out var r)) return false;
            return r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
        }
        catch { return false; }
    }
}

public static class Screenshots
{
    /// <summary>Capture the whole screen the island is on, save it to Pictures\Screenshots and copy it.</summary>
    public static string? Capture(Rectangle bounds)
    {
        try
        {
            using var bmp = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Clipboard.SetImage(bmp);
            return path;
        }
        catch { return null; }
    }
}
