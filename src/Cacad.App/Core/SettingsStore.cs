using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cacad;

public sealed class PinnedApp
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // Appearance
    public string ThemeId { get; set; } = "black";
    public string Shape { get; set; } = "notch";          // "notch" hangs from the edge, "island" floats under it
    public string BarStyle { get; set; } = "";            // "" = the theme's own; wave | pixels | comet | shape | simple | hearts
    public string Visualizer { get; set; } = "pixels";    // pixels | bars
    public bool AlbumBackground { get; set; } = true;
    public string PillClockStyle { get; set; } = "editorial"; // the clock face, shared by the pill and the Clock tab: editorial | flip | words | roll | poster

    // Geometry (DIPs)
    public double PillWidth { get; set; } = 212;
    public double PillHeight { get; set; } = 36;
    public double CardWidth { get; set; } = 400;
    public double CornerFlare { get; set; } = 10;
    public int TopOffset { get; set; } = 6;

    // Behaviour
    public int OpenDelayMs { get; set; } = 150;
    public int CloseDelayMs { get; set; } = 380;
    public int AnimationSpeedPct { get; set; } = 100;
    public bool HideUntilHover { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public bool Clock24h { get; set; }
    public bool LaunchAtStartup { get; set; }
    public bool MicMuteHotkey { get; set; } = true;
    public string LastPanel { get; set; } = "music";

    // Live features
    public bool LiveDownloads { get; set; } = true;     // a download in Downloads shows in the pill
    public bool CallTimer { get; set; } = true;         // an app using the mic shows "On a call" with a timer
    public bool ChargingAnimation { get; set; } = true;
    public bool SmartPeeks { get; set; } = true;        // codes, colours, addresses, emails in the clipboard
    public bool IdleMode { get; set; } = true;
    public int IdleMinutes { get; set; } = 3;
    public bool Lyrics { get; set; } = true;                  // synced lyrics in the pill (looked up on lrclib.net)
    public bool RealVisualizer { get; set; } = true;    // the pill's visualizer follows the actual sound
    public int FocusMinutes { get; set; } = 25;

    // The pixel cat
    public bool Notifications { get; set; } = true;            // mirror Windows notifications on the island
    public bool SystemHud { get; set; } = true;               // volume and brightness bar in the pill
    public bool EngineC3E { get; set; } = true;               // the surface engine (caching and pacing, same look)
    public bool EngineZ3R { get; set; } = true;               // the app engine (partial frames, rest mode, memory trim)
    public string CustomImage { get; set; } = "";             // the Custom surface: an image, or a gradient of two colours
    public string CustomColor1 { get; set; } = "#2A1B4D";
    public string CustomColor2 { get; set; } = "#0E3C5C";
    public int CustomDim { get; set; } = 35;
    public int CustomBlur { get; set; }
    public bool Weather { get; set; } = true;                 // weather tab, rain warnings (Open-Meteo)
    public string WeatherCity { get; set; } = "";             // empty: Windows' location
    public bool WeatherFahrenheit { get; set; }
    public bool HideWindowsOsd { get; set; } = true;          // hide Windows 10's own volume pop-up meanwhile
    public bool LockPeeks { get; set; } = true;               // Caps Lock / Num Lock
    public bool NetworkPeeks { get; set; } = true;            // Wi-Fi joined / offline
    public bool DevicePeeks { get; set; } = true;             // Bluetooth devices, USB drives
    public bool ScreenshotPeeks { get; set; } = true;         // a thumbnail after a screenshot
    public bool NotificationsPrivate { get; set; }             // show who, but not the message
    public List<string> NotificationsMuted { get; set; } = new();
    public bool Companion { get; set; } = true;
    public int PetXp { get; set; }                            // the companion game: XP (level), happiness, hat
    public double PetHappy { get; set; } = 70;
    public DateTime PetFedAt { get; set; } = DateTime.Now;
    public string PetDay { get; set; } = "";
    public int PetPetsToday { get; set; }
    public string PetHat { get; set; } = "";
    public string CompanionSpecies { get; set; } = "cat";     // which companion lives on the island
    public string CompanionSkin { get; set; } = "natural";    // its coat
    public string CompanionStyle { get; set; } = "pixel";     // its art style: pixel, toon, cute, real, sketch
    public string Updates { get; set; } = "auto";            // auto | notify | off (GitHub Releases)
    public bool CompanionTyping { get; set; } = true;          // count key presses (typing reactions, "meow")
    public List<string> CompanionOff { get; set; } = new();     // reaction groups turned off
    public string Birthday { get; set; } = "";                  // "MM-dd"
    public string FocusDay { get; set; } = "";
    public int FocusToday { get; set; }

    // Content
    public List<string> WorldClocks { get; set; } = new() { "Tokyo Standard Time" };
    public List<TodoItem> Todos { get; set; } = new();
    public List<PinnedApp> LauncherApps { get; set; } = new();
    public List<string> Shelf { get; set; } = new();

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cacad");
    public static string DataDir { get { Directory.CreateDirectory(Dir); return Dir; } }
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* corrupt → defaults */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* non-fatal */ }
    }
}
