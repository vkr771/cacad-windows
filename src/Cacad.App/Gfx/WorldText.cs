using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Cacad;

/// <summary>Text in any script (lyrics in Hindi, Telugu, Japanese, Korean, Arabic…). The app's own font covers Latin;
/// for anything else a Windows font that has those letters is picked (Nirmala UI, Yu Gothic, Malgun Gothic…) and the
/// text is shaped with HarfBuzz, so joined and stacked letters come out right.</summary>
public static class WorldText
{
    private static readonly Dictionary<int, SKTypeface> ByChar = new();
    private static readonly Dictionary<SKTypeface, SKShaper> Shapers = new();

    /// <summary>The font to use for <paramref name="s"/>: the app's own if it has every letter, else a system font.</summary>
    public static SKFont Font(string s, float size, SKTypeface preferred)
    {
        using (var test = new SKFont(preferred, size))
            if (test.ContainsGlyphs(s)) return new SKFont(preferred, size);
        foreach (var rune in s.EnumerateRunes())
        {
            if (rune.Value < 0x250) continue;                      // Latin: not the reason
            if (!ByChar.TryGetValue(rune.Value, out var face))
            {
                face = SKFontManager.Default.MatchCharacter("Segoe UI", SKFontStyle.Bold, null, rune.Value) ?? preferred;
                ByChar[rune.Value] = face;
            }
            return new SKFont(face, size);
        }
        return new SKFont(preferred, size);
    }

    private static SKShaper Shaper(SKTypeface face)
    {
        if (!Shapers.TryGetValue(face, out var s)) Shapers[face] = s = new SKShaper(face);
        return s;
    }

    private static bool Simple(SKFont font) => ReferenceEquals(font.Typeface, Fonts.Medium) || ReferenceEquals(font.Typeface, Fonts.SemiBold) || ReferenceEquals(font.Typeface, Fonts.Regular);

    public static float Measure(SKFont font, string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (Simple(font)) return font.MeasureText(s);
        return Shaper(font.Typeface).Shape(s, font).Width;
    }

    public static void Draw(SKCanvas c, string s, float x, float y, SKFont font, SKColor color, SKTextAlign align = SKTextAlign.Left)
    {
        if (color.Alpha == 0 || string.IsNullOrEmpty(s)) return;
        using var p = new SKPaint { IsAntialias = true, Color = color };
        if (Simple(font)) { c.DrawText(s, x, y, align, font, p); return; }
        c.DrawShapedText(Shaper(font.Typeface), s, x, y, align, font, p);
    }

    /// <summary>A soft dark halo behind text (drawn first), so light text stays readable over bright surfaces.</summary>
    public static void Shadow(SKCanvas c, string s, float x, float y, SKFont font)
    {
        if (string.IsNullOrEmpty(s)) return;
        using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 205), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, font.Size * 0.24f) };
        if (Simple(font)) c.DrawText(s, x, y + font.Size * 0.05f, SKTextAlign.Left, font, p);
        else c.DrawShapedText(Shaper(font.Typeface), s, x, y + font.Size * 0.05f, SKTextAlign.Left, font, p);
    }

    /// <summary>Cuts <paramref name="s"/> to fit, with an ellipsis.</summary>
    public static string Fit(string s, SKFont font, float width)
    {
        if (Measure(font, s) <= width) return s;
        int lo = 0, hi = s.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Measure(font, s[..mid].TrimEnd() + "…") <= width) lo = mid; else hi = mid - 1;
        }
        return s[..lo].TrimEnd() + "…";
    }
}
