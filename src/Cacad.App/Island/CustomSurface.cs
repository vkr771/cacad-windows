using SkiaSharp;

namespace Cacad;

/// <summary>The Custom surface: your own image (cropped to fill the island, optionally blurred, dimmed so text stays
/// readable) or a two-colour gradient of your choosing. The accent (progress bar, clock details) is taken from the
/// image's strongest colour, or from the gradient.</summary>
public static class CustomSurface
{
    public static SettingsStore? Settings;

    private static SKImage? _image;
    private static string _imageKey = "";
    private static SKColor _imageAccent = SKColors.White;

    private static SettingsStore S => Settings ?? new SettingsStore();

    public static SKColor Color1 => Parse(S.CustomColor1, new SKColor(42, 27, 77));
    public static SKColor Color2 => Parse(S.CustomColor2, new SKColor(14, 60, 92));

    private static SKColor Parse(string hex, SKColor fallback) => SKColor.TryParse(hex, out var c) ? c : fallback;

    /// <summary>The image, loaded once (shrunk to a sensible size and blurred as set), or null.</summary>
    private static SKImage? Image()
    {
        string path = S.CustomImage;
        string key = $"{path}|{S.CustomBlur}";
        if (key == _imageKey) return _image;
        _imageKey = key;
        _image?.Dispose();
        _image = null;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var src = SKBitmap.Decode(path);
            if (src == null) return null;
            // The island is at most ~500 x 220 DIPs; 1000 px wide is plenty even at 200% scaling.
            float k = Math.Min(1, 1000f / src.Width);
            var info = new SKImageInfo(Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k)));
            using var surface = SKSurface.Create(info);
            using (var p = new SKPaint { ImageFilter = S.CustomBlur > 0 ? SKImageFilter.CreateBlur(S.CustomBlur, S.CustomBlur, SKShaderTileMode.Clamp) : null })
                surface.Canvas.DrawImage(SKImage.FromBitmap(src), SKRect.Create(info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), p);
            _image = surface.Snapshot();
            _imageAccent = StrongColour(src);
        }
        catch { _image = null; }
        return _image;
    }

    /// <summary>The most common vivid hue in the image, lifted so it reads as an accent on a dark island.</summary>
    private static SKColor StrongColour(SKBitmap bmp)
    {
        var bins = new float[24];
        var sums = new (float r, float g, float b, float n)[24];
        int step = Math.Max(1, Math.Max(bmp.Width, bmp.Height) / 64);
        for (int y = 0; y < bmp.Height; y += step)
            for (int x = 0; x < bmp.Width; x += step)
            {
                var c = bmp.GetPixel(x, y);
                c.ToHsv(out float h, out float s, out float v);
                if (s < 30 || v < 30) continue;
                int bin = (int)(h / 15) % 24;
                float w = s * v / 10000f;
                bins[bin] += w;
                sums[bin] = (sums[bin].r + c.Red * w, sums[bin].g + c.Green * w, sums[bin].b + c.Blue * w, sums[bin].n + w);
            }
        int best = Array.IndexOf(bins, bins.Max());
        if (bins[best] <= 0) return SKColors.White;
        var (r, g, b, n) = sums[best];
        var col = new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
        col.ToHsv(out float hh, out float ss, out float vv);
        return SKColor.FromHsv(hh, Math.Min(ss, 80), Math.Max(vv, 85));
    }

    public static Theme Theme()
    {
        bool img = Image() != null;
        SKColor accent;
        if (img) accent = _imageAccent;
        else
        {
            // The brighter of the two colours, lifted.
            var c = Lum(Color1) > Lum(Color2) ? Color1 : Color2;
            c.ToHsv(out float h, out float s, out float v);
            accent = SKColor.FromHsv(h, Math.Min(s, 75), Math.Max(v, 88));
        }
        return new Theme("custom", "Custom", Color1, new SKColor(255, 255, 255, 0x30), accent, "wave", Surface: "custom");
    }

    private static float Lum(SKColor c) => 0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue;

    /// <summary>Paints the surface into the island (DIPs: x = 0 at the centre, y from 0 down), already clipped.</summary>
    public static void Draw(SKCanvas c, float w, float h)
    {
        var r = SKRect.Create(-w / 2, 0, w, h);
        var img = Image();
        if (img != null)
        {
            // Cover: fill the island, cropping the image's longer side (centred).
            float k = Math.Max(r.Width / img.Width, r.Height / img.Height), iw = img.Width * k, ih = img.Height * k;
            c.DrawImage(img, SKRect.Create(r.MidX - iw / 2, r.MidY - ih / 2, iw, ih), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
        else
        {
            using var sh = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom), new[] { Color1, Color2 }, null, SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = sh };
            c.DrawRect(r, p);
        }
        // Dim, so white text stays readable on any picture.
        float dim = Math.Clamp(S.CustomDim, 0, 90) / 100f;
        if (dim > 0) using (var p = new SKPaint { Color = SKColors.Black.WithAlpha((byte)(255 * dim)) }) c.DrawRect(r, p);
    }

    /// <summary>Forget the loaded image (the setting changed).</summary>
    public static void Reset() { _imageKey = "\0"; }
}
