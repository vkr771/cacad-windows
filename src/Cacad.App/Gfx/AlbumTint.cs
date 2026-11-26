using SkiaSharp;

namespace Cacad;

/// <summary>Colours taken from a cover: <see cref="Strong"/> for bars and badges, <see cref="TextColor"/> for the artist line,
/// and <see cref="Strip"/>, the cover squeezed to a few columns so that stretched across the card it paints the album's streaks.</summary>
public sealed class AlbumArt : IDisposable
{
    public SKImage Image { get; }
    public SKImage Strip { get; }
    public SKColor? Strong { get; }
    public SKColor? TextColor { get; }

    private AlbumArt(SKImage image, SKImage strip, SKColor? strong, SKColor? text)
    {
        Image = image;
        Strip = strip;
        Strong = strong;
        TextColor = text;
    }

    public static AlbumArt? From(SKBitmap? bmp)
    {
        if (bmp == null || bmp.Width < 2 || bmp.Height < 2) return null;

        // Many players hand Windows a 16:9 frame with the square cover in the middle; keep the square.
        SKBitmap square = bmp;
        if (Math.Abs(bmp.Width - bmp.Height) > 4)
        {
            int side = Math.Min(bmp.Width, bmp.Height);
            square = new SKBitmap(side, side, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var sc = new SKCanvas(square);
            sc.DrawBitmap(bmp, SKRect.Create((bmp.Width - side) / 2f, (bmp.Height - side) / 2f, side, side), SKRect.Create(0, 0, side, side));
        }

        var image = SKImage.FromBitmap(square.Copy());
        var (strong, text) = Tint(square);
        var strip = Squeeze(square);
        if (!ReferenceEquals(square, bmp)) square.Dispose();
        return new AlbumArt(image, strip, strong, text);
    }

    private static SKImage Squeeze(SKBitmap src)
    {
        using var surface = new SKBitmap(6, 60, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(surface))
        using (var p = new SKPaint { ColorFilter = G.Saturation(1.7f) }) // baked once, not filtered every frame
        {
            float w = src.Width, h = src.Height;
            c.DrawBitmap(src, SKRect.Create(w * 0.125f, 0, w * 0.75f, h), SKRect.Create(0, 0, 6, 60), p);
        }
        return SKImage.FromBitmap(surface.Copy());
    }

    /// <summary>The cover blurred and saturated into a soft glow, <see cref="GlowPad"/> DIPs of bleed on each side
    /// of a 134 DIP square. Made once per track: a 22 px blur every frame was one of the costliest draws.</summary>
    public const float GlowPad = 40;

    private static SKImage MakeGlow(SKBitmap src)
    {
        const int size = 134, pad = (int)GlowPad, full = size + pad * 2;
        using var surface = SKSurface.Create(new SKImageInfo(full, full, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var p = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(22, 22), ColorFilter = G.Saturation(1.6f) })
        using (var img = SKImage.FromBitmap(src))
        {
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(img, SKRect.Create(pad, pad, size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), p);
        }
        return surface.Snapshot().ToRasterImage(true);
    }

    public SKImage Glow => _glow ??= MakeGlowFromImage();
    private SKImage? _glow;

    private SKImage MakeGlowFromImage()
    {
        using var bmp = SKBitmap.FromImage(Image);
        return MakeGlow(bmp);
    }

    /// <summary>Weighted average of saturated, mid-light pixels; null when the cover is near grey.</summary>
    private static (SKColor?, SKColor?) Tint(SKBitmap src)
    {
        using var small = new SKBitmap(24, 24, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(small))
            c.DrawBitmap(src, SKRect.Create(0, 0, src.Width, src.Height), SKRect.Create(0, 0, 24, 24));

        double r = 0, g = 0, b = 0, wsum = 0;
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                var px = small.GetPixel(x, y);
                var (_, s, l) = ToHsl(px.Red, px.Green, px.Blue);
                double k = s * s * (1 - Math.Abs(l - 0.55) * 1.6);
                if (k <= 0) continue;
                r += px.Red * k; g += px.Green * k; b += px.Blue * k; wsum += k;
            }
        if (wsum <= 0) return (null, null);
        var (hh, ss, _) = ToHsl((byte)(r / wsum), (byte)(g / wsum), (byte)(b / wsum));
        if (ss < 0.12) return (null, null);
        return (FromHsl(hh, Math.Min(1, ss * 1.2), 0.66), FromHsl(hh, Math.Min(1, ss * 1.1), 0.72));
    }

    private static (double h, double s, double l) ToHsl(byte rb, byte gb, byte bb)
    {
        double r = rb / 255.0, g = gb / 255.0, b = bb / 255.0;
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2;
        if (mx - mn < 1e-9) return (0, 0, l);
        double d = mx - mn, s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
        double h = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static SKColor FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, hp = ((h % 360) + 360) % 360 / 60, x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r, double g, double b) = hp < 1 ? (c, x, 0d) : hp < 2 ? (x, c, 0d) : hp < 3 ? (0d, c, x) : hp < 4 ? (0d, x, c) : hp < 5 ? (x, 0d, c) : (c, 0d, x);
        double m = l - c / 2;
        return new SKColor((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public void Dispose()
    {
        Image.Dispose();
        Strip.Dispose();
        _glow?.Dispose();
    }
}
