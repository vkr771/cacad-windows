using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cacad;

/// <summary>Updates from the project's GitHub Releases.
///
/// Every few hours (and 20 seconds after start) it asks GitHub for the latest release. A release is a tag like
/// "v1.3.1" with the exes attached as "cacad-x64.exe" and "cacad-arm64.exe", plus "SHA256SUMS.txt" listing their
/// hashes. If it is newer than this copy, it downloads the exe for this PC and checks its hash. Then:
///  - Automatic (the default): it installs the next time the PC has been left alone for a few minutes, or right away
///    if you tap "Restart" on the island's note. Installing swaps the exe in place and restarts cacad; settings are
///    kept (they live in AppData, not beside the exe).
///  - Notify: the island says a new version is out; tap to install.
///  - Off: never checks.
/// Only github.com hosts are trusted for downloads, and a file whose hash does not match is thrown away.
///
/// Swapping a running exe: Windows will not overwrite it, but will rename it. So the running exe becomes
/// "cacad.old.exe", the new one takes its name, the new one starts with "--updated" (it waits for this one to exit,
/// see Program), and this one quits. The new copy deletes the .old file. If any step fails, the old exe is put back.</summary>
public sealed class Updater
{
    /// <summary>The GitHub repository releases come from ("owner/name"). Empty: updates are off.</summary>
    public const string Repo = "vkr771/cacad-windows";

    public static Version Current { get; } = Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0));
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    private static readonly string? TestDir = Environment.GetEnvironmentVariable("CACAD_UPDATE_TEST");
    private static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    private static string AssetName => $"cacad-{Arch}.exe";
    private static string UpdateDir => Path.Combine(SettingsStore.DataDir, "update");

    private readonly SettingsStore _s;
    private readonly IslandWindow _w;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = TestDir != null ? 3000 : 60_000 };
    private DateTime _nextCheck = DateTime.Now.AddSeconds(TestDir != null ? 4 : 20);
    private bool _busy;
    private (Version version, string path, string page)? _ready;

    /// <summary>What the updater is doing, for Settings.</summary>
    public string Status { get; private set; } = "";
    public event Action? Changed;

    public static Updater? Instance { get; private set; }

    public static bool Enabled => Repo.Length > 0 || TestDir != null;

    public Updater(SettingsStore s, IslandWindow w)
    {
        _s = s;
        _w = w;
        Instance = this;
        Status = Enabled ? $"You're on {CurrentText}." : $"Version {CurrentText}. Updates turn on in published builds.";
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Set(string status) { Status = status; Changed?.Invoke(); }

    private void Tick()
    {
        if (!Enabled || _s.Updates == "off") return;
        // A downloaded update installs once the PC has been left alone for a while (nobody is mid-task).
        if (_ready is { } r && _s.Updates == "auto" && IdleSeconds() > (TestDir != null ? 0 : 180)) { Apply(); return; }
        if (DateTime.Now >= _nextCheck) _ = Check(false);
    }

    /// <summary>Ask GitHub for the latest release; download it if it is newer. <paramref name="manual"/>: from the
    /// Settings button (reports "up to date", and installs without waiting for an idle moment).</summary>
    public async Task Check(bool manual)
    {
        if (!Enabled || _busy) return;
        _busy = true;
        _nextCheck = DateTime.Now.AddHours(6);
        try
        {
            if (_ready is { } have) { if (manual) Apply(); return; }
            Set("Checking for updates…");
            var rel = await Latest();
            if (rel == null || rel.Value.version <= Current) { Set($"Up to date ({CurrentText})."); return; }
            var (version, exeUrl, sumsUrl, page) = rel.Value;
            string v = $"{version.Major}.{version.Minor}.{version.Build}";
            if (_s.Updates == "notify" && !manual)
            {
                Set($"Version {v} is out.");
                _w.Toast("download", $"cacad {v} is out · tap to update", null, () => _ = Download(rel.Value, true), 8000);
                return;
            }
            await Download(rel.Value, manual);
        }
        catch (Exception ex)
        {
            Set(manual ? "Couldn't reach GitHub. Try again later." : $"You're on {CurrentText}.");
            Log("check failed: " + ex.Message);
        }
        finally { _busy = false; }
    }

    private async Task Download((Version version, string exeUrl, string sumsUrl, string page) rel, bool now)
    {
        string v = $"{rel.version.Major}.{rel.version.Minor}.{rel.version.Build}";
        try
        {
            Set($"Downloading {v}…");
            Directory.CreateDirectory(UpdateDir);
            string sums = System.Text.Encoding.UTF8.GetString(await Fetch(rel.sumsUrl));
            string? want = sums.Split('\n').Select(l => l.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length == 2 && p[1].TrimStart('*') == AssetName).Select(p => p[0].ToLowerInvariant()).FirstOrDefault();
            if (want == null) throw new InvalidDataException("no hash listed for " + AssetName);
            byte[] exe = await Fetch(rel.exeUrl);
            string got = Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant();
            if (got != want) throw new InvalidDataException("hash mismatch");
            string path = Path.Combine(UpdateDir, $"cacad-{v}.exe");
            await File.WriteAllBytesAsync(path, exe);
            _ready = (rel.version, path, rel.page);
            if (now) { Apply(); return; }
            Set($"Version {v} is ready · installs when you step away.");
            _w.Toast("download", $"cacad {v} is ready · tap to restart", null, Apply, 8000);
        }
        catch (Exception ex)
        {
            Set("Download failed · cacad will retry later.");
            Log("download failed: " + ex.Message);
        }
    }

    /// <summary>Swap in the downloaded exe and restart into it.</summary>
    public void Apply()
    {
        if (_ready is not { } r) return;
        string? exe = Environment.ProcessPath;
        if (exe == null || !File.Exists(r.path)) { _ready = null; return; }
        string old = Path.Combine(Path.GetDirectoryName(exe)!, "cacad.old.exe");
        bool moved = false;
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            moved = true;
            File.Copy(r.path, exe, true);
            _s.Save();
            Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
            Log($"updated {CurrentText} -> {r.version}");
            Application.Exit();
        }
        catch (Exception ex)
        {
            // Put the old exe back (a folder we can't write to, a locked file…) and send them to the download page.
            Log("install failed: " + ex.Message);
            try { if (moved && !File.Exists(exe)) File.Move(old, exe); } catch { }
            _ready = null;
            Set("Couldn't install here · get it from the releases page.");
            _w.Toast("download", "Update couldn't install · tap for the download page", null, () => Shell.Open(r.page), 8000);
        }
    }

    /// <summary>After an update: remove the previous exe and the downloaded copy, and say what happened.</summary>
    public void AfterStart(bool updated)
    {
        var tidy = new System.Windows.Forms.Timer { Interval = 4000 };
        tidy.Tick += (_, _) =>
        {
            tidy.Dispose();
            try
            {
                string? exe = Environment.ProcessPath;
                if (exe != null)
                {
                    string old = Path.Combine(Path.GetDirectoryName(exe)!, "cacad.old.exe");
                    if (File.Exists(old)) File.Delete(old);
                }
                if (Directory.Exists(UpdateDir)) Directory.Delete(UpdateDir, true);
            }
            catch { /* the old process may still be closing; next start tries again */ }
            if (updated)
                _w.Toast("check", $"Updated to cacad {CurrentText}", null, () => Shell.Open(Repo.Length > 0 ? $"https://github.com/{Repo}/releases/tag/v{CurrentText}" : "about:blank"), 6000);
        };
        tidy.Start();
    }

    // ---------- GitHub ----------

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"cacad-updater/{CurrentText}");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    private static async Task<(Version version, string exeUrl, string sumsUrl, string page)?> Latest()
    {
        string json = TestDir != null
            ? await File.ReadAllTextAsync(Path.Combine(TestDir, "release.json"))
            : await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        string? exeUrl = null, sumsUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "", url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase)) exeUrl = url;
            if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
        }
        if (exeUrl == null || sumsUrl == null) return null;
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return (Normalize(version), exeUrl, sumsUrl, page);
    }

    private static async Task<byte[]> Fetch(string url)
    {
        if (TestDir != null) return await File.ReadAllBytesAsync(Path.Combine(TestDir, Path.GetFileName(new Uri(url).LocalPath)));
        if (!Trusted(new Uri(url))) throw new InvalidDataException("untrusted host " + new Uri(url).Host);
        using var resp = await Http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        // GitHub redirects asset downloads to its own storage; make sure we ended up there.
        if (resp.RequestMessage?.RequestUri is { } final && !Trusted(final)) throw new InvalidDataException("redirected to " + final.Host);
        return await resp.Content.ReadAsByteArrayAsync();
    }

    private static bool Trusted(Uri u) => u.Scheme == "https" &&
        (u.Host == "github.com" || u.Host == "api.github.com" || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    // ---------- helpers ----------

    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);

    private static double IdleSeconds()
    {
        var li = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref li) ? (Environment.TickCount - (int)li.Time) / 1000.0 : 0;
    }

    private static void Log(string line)
    {
        try { File.AppendAllText(Path.Combine(SettingsStore.DataDir, "update.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\r\n"); } catch { }
    }
}
