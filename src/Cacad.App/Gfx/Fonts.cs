using System.Reflection;
using SkiaSharp;

namespace Cacad;

/// <summary>Geist typefaces embedded in the app (SIL OFL). Matches the reference typography:
/// F_TITLE = 600 16px Geist, F_SUB = 400 13px Geist, F_MONO = 500 11px Geist Mono.</summary>
public static class Fonts
{
    public static readonly SKTypeface Regular;
    public static readonly SKTypeface Medium;
    public static readonly SKTypeface SemiBold;
    public static readonly SKTypeface MonoMedium;

    static Fonts()
    {
        Regular = Load("Geist-Regular.ttf");
        Medium = Load("Geist-Medium.ttf");
        SemiBold = Load("Geist-SemiBold.ttf");
        MonoMedium = Load("GeistMono-Medium.ttf");
    }

    private static SKTypeface Load(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        string? resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (resource == null) return SKTypeface.Default;
        using var stream = asm.GetManifestResourceStream(resource)!;
        return SKTypeface.FromStream(stream) ?? SKTypeface.Default;
    }

    public static SKFont Title(float size = 16f) => new(SemiBold, size);      // 600
    public static SKFont Sub(float size = 13f) => new(Regular, size);         // 400
    public static SKFont Mono(float size = 11f) => new(MonoMedium, size);     // 500
    public static SKFont Med(float size = 13f) => new(Medium, size);          // 500

    // Display faces for the clock, from fonts Windows ships. Each falls back to Geist if missing.
    private static readonly Dictionary<string, SKTypeface> SystemFaces = new();

    private static SKTypeface SystemFace(string family, SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant, SKTypeface fallback)
    {
        string key = $"{family}|{weight}|{width}|{slant}";
        if (SystemFaces.TryGetValue(key, out var tf)) return tf;
        var found = SKFontManager.Default.MatchFamily(family, new SKFontStyle(weight, width, slant));
        tf = found != null && found.FamilyName.StartsWith(family, StringComparison.OrdinalIgnoreCase) ? found : fallback;
        SystemFaces[key] = tf;
        return tf;
    }

    /// <summary>Impact: heavy condensed capitals, the closest thing Windows ships to comic sound-effect lettering.</summary>
    public static SKFont Comic(float size) => new(SystemFace("Impact", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright, SystemFace("Bahnschrift", SKFontStyleWeight.Bold, SKFontStyleWidth.Condensed, SKFontStyleSlant.Upright, SemiBold)), size);

    /// <summary>Bahnschrift: a DIN-like face with condensed cuts, made for big numerals.</summary>
    public static SKFont Din(float size, SKFontStyleWeight weight = SKFontStyleWeight.SemiBold, SKFontStyleWidth width = SKFontStyleWidth.Condensed)
        => new(SystemFace("Bahnschrift", weight, width, SKFontStyleSlant.Upright, SemiBold), size);

    /// <summary>An italic serif (Georgia, else Sitka) for editorial contrast.</summary>
    public static SKFont SerifItalic(float size)
    {
        var tf = SystemFace("Georgia", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic, Regular);
        if (tf == Regular) tf = SystemFace("Sitka", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic, Regular);
        return new SKFont(tf, size);
    }

    // The collection must outlive every Font made from it: GDI+ frees the family when it is collected,
    // and a WinForms control still drawing with that font then reads freed memory (an AccessViolation crash).
    private static System.Drawing.Text.PrivateFontCollection? _uiFonts;
    private static FontFamily? _uiFamily;
    private static bool _uiTried;

    private static FontFamily? UiFamily()
    {
        if (_uiTried) return _uiFamily;
        _uiTried = true;
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "cacad-fonts");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "Geist-Regular.ttf");
            if (!File.Exists(path))
            {
                var asm = Assembly.GetExecutingAssembly();
                string? res = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Geist-Regular.ttf", StringComparison.OrdinalIgnoreCase));
                if (res == null) return null;
                using var src = asm.GetManifestResourceStream(res)!;
                using var dst = File.Create(path);
                src.CopyTo(dst);
            }
            _uiFonts = new System.Drawing.Text.PrivateFontCollection();
            _uiFonts.AddFontFile(path);
            _uiFamily = _uiFonts.Families.FirstOrDefault(f => f.Name.Contains("Geist", StringComparison.OrdinalIgnoreCase));
        }
        catch { _uiFamily = null; }
        return _uiFamily;
    }

    /// <summary>WinForms UI font: Geist when it loads, otherwise null (callers fall back to Segoe UI).</summary>
    public static Font? UiFont(float points = 9f, FontStyle style = FontStyle.Regular)
    {
        var fam = UiFamily();
        if (fam == null) return null;
        try { return new Font(fam, points, style, GraphicsUnit.Point); }
        catch { return null; }
    }
}
