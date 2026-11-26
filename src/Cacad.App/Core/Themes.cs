using SkiaSharp;

namespace Cacad;

public enum Ambience { None, Petals, Hearts, Stars, Glow }

public sealed record Theme(
    string Id,
    string Name,
    SKColor Fill,
    SKColor Edge,
    SKColor Accent,
    string Bar,
    Ambience Ambience = Ambience.None,
    bool IsGlass = false,
    bool Refract = false,
    string? Surface = null);   // a drawn material (see Surfaces): aurora | nebula | neon | titanium

public static class Themes
{
    public static readonly Theme[] All =
    {
        new("black", "Black", new(0xFF0B0B0C), new(0x14FFFFFF), SKColors.White, "wave"),
        new("graphite", "Graphite", new(0xFF1C1C1E), new(0x14FFFFFF), SKColors.White, "wave"),
        // "glass" keeps its id so saved settings still find it; it is the blur-and-tint look, now named for what it is.
        new("glass", "Frosted Glass", new(0x6617191C), new(0x59FFFFFF), SKColors.White, "wave", IsGlass: true),
        new("liquid", "Liquid Glass", new(0x22101014), new(0x00FFFFFF), SKColors.White, "wave", IsGlass: true, Refract: true),
        new("prism", "Prism Glass", new(0x33101014), new(0x00FFFFFF), SKColors.White, "wave", IsGlass: true, Refract: true),
        new("custom", "Custom", new(0xFF2A1B4D), new(0x30FFFFFF), SKColors.White, "wave", Surface: "custom"),
        new("aurora", "Aurora", new(0xFF05070D), new(0x3360FFC0), new(0xFF5CF2B0), "wave", Surface: "aurora"),
        new("nebula", "Nebula", new(0xFF06040D), new(0x33B08CFF), new(0xFFC59BFF), "wave", Surface: "nebula"),
        new("ocean", "Ocean", new(0xFF021820), new(0x334FE0E8), new(0xFF4FE0E8), "wave", Surface: "ocean"),
        new("sunset", "Sunset", new(0xFF1A0B1E), new(0x40FFA05A), new(0xFFFFA05A), "wave", Surface: "sunset"),
        new("ember", "Ember", new(0xFF0A0402), new(0x40FF7A3A), new(0xFFFF7A3A), "wave", Surface: "ember"),
        new("obsidian", "Obsidian", new(0xFF050507), new(0x38B8C0FF), new(0xFFB8C0FF), "wave", Surface: "obsidian"),
        new("rose", "Rosé", new(0xFF30101E), new(0x4DE8B4A0), new(0xFFFF8FB1), "hearts", Ambience.Hearts, Surface: "rose"),
        new("lavender", "Lavender", new(0xFF1A1230), new(0x4DC3A6FF), new(0xFFC3A6FF), "wave", Ambience.Stars, Surface: "lavender"),
        new("sakura", "Sakura", new(0xFF26101E), new(0x4DFFC4D2), new(0xFFFFB7C5), "wave", Ambience.Petals, Surface: "sakura"),
        new("manga", "Manga", new(0xFF0E0D10), new(0x59FFFFFF), new(0xFFFF4F5E), "wave", Surface: "manga"),
    };

    /// <summary>"wallpaper" was replaced by Custom.</summary>
    public static Theme Get(string? id) => id is "custom" or "wallpaper" ? CustomSurface.Theme() : All.FirstOrDefault(t => t.Id == Renamed(id)) ?? All[0];

    /// <summary>Surfaces that were retired map to the nearest one now.</summary>
    public static string? Renamed(string? id) => id switch { "neon" or "titanium" => "obsidian", "peach" => "sunset", _ => id };
}

