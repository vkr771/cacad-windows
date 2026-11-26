using SkiaSharp;

namespace Cacad;

/// <summary>Small drawing vocabulary shared by the island and its panels. All units are DIPs.</summary>
public static class G
{
    public static readonly SKColor PaperBase = new(239, 237, 232);
    public static readonly SKColor Ink = new(12, 12, 14);

    public static SKColor Paper(float a) => PaperBase.WithAlpha(A(a));
    public static SKColor White(float a) => SKColors.White.WithAlpha(A(a));
    public static SKColor Black(float a) => SKColors.Black.WithAlpha(A(a));
    public static SKColor With(SKColor c, float a) => c.WithAlpha(A(a * c.Alpha / 255f));
    public static byte A(float a) => (byte)Math.Clamp(a * 255f, 0f, 255f);

    public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Exponential ease toward a target, frame-rate independent.</summary>
    public static float Ease(float v, float target, float rate, float dt) => v + (target - v) * (1f - MathF.Exp(-dt * rate));

    public static void Fill(SKCanvas c, SKRect r, float radius, SKColor color)
    {
        if (color.Alpha == 0) return;
        using var p = new SKPaint { IsAntialias = true, Color = color };
        c.DrawRoundRect(r, radius, radius, p);
    }

    public static void Fill(SKCanvas c, float x, float y, float w, float h, float radius, SKColor color)
        => Fill(c, SKRect.Create(x, y, w, h), radius, color);

    public static void Stroke(SKCanvas c, SKRect r, float radius, SKColor color, float width = 1f)
    {
        if (color.Alpha == 0) return;
        using var p = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width };
        c.DrawRoundRect(r, radius, radius, p);
    }

    public static void Pill(SKCanvas c, float x, float y, float w, float h, SKColor color)
        => Fill(c, x, y, w, h, Math.Min(w, h) / 2f, color);

    public static void Circle(SKCanvas c, float cx, float cy, float r, SKColor color)
    {
        if (color.Alpha == 0) return;
        using var p = new SKPaint { IsAntialias = true, Color = color };
        c.DrawCircle(cx, cy, r, p);
    }

    public static void Text(SKCanvas c, string s, float x, float y, SKFont font, SKColor color, SKTextAlign align = SKTextAlign.Left)
    {
        if (color.Alpha == 0 || string.IsNullOrEmpty(s)) return;
        using var p = new SKPaint { IsAntialias = true, Color = color };
        c.DrawText(s, x, y, align, font, p);
    }

    public static float Measure(SKFont font, string s) => string.IsNullOrEmpty(s) ? 0 : font.MeasureText(s);

    public static string Ellipsize(string s, SKFont font, float width)
    {
        if (string.IsNullOrEmpty(s) || Measure(font, s) <= width) return s ?? "";
        int lo = 0, hi = s.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Measure(font, s[..mid] + "…") <= width) lo = mid; else hi = mid - 1;
        }
        return lo <= 0 ? "…" : s[..lo].TrimEnd() + "…";
    }

    /// <summary>Like <see cref="Ellipsize"/> but keeps the end (file extensions stay readable).</summary>
    public static string EllipsizeMiddle(string s, SKFont font, float width)
    {
        if (string.IsNullOrEmpty(s) || Measure(font, s) <= width) return s ?? "";
        int tail = Math.Min(6, s.Length / 3);
        string end = s[^tail..];
        string head = s[..^tail];
        while (head.Length > 1 && Measure(font, head + "…" + end) > width) head = head[..^1];
        return head + "…" + end;
    }

    public static void Image(SKCanvas c, SKImage img, SKRect dst, float alpha = 1, float radius = 0)
    {
        c.Save();
        if (radius > 0) c.ClipRoundRect(new SKRoundRect(dst, radius), SKClipOperation.Intersect, true);
        using var p = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(A(alpha)) };
        c.DrawImage(img, dst, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), p);
        c.Restore();
    }

    /// <summary>Draw an image cropped to fill <paramref name="dst"/> (centre crop, like CSS object-fit: cover).</summary>
    public static void ImageCover(SKCanvas c, SKImage img, SKRect dst, float alpha = 1, float radius = 0)
    {
        float sw = img.Width, sh = img.Height;
        float scale = Math.Max(dst.Width / sw, dst.Height / sh);
        float cw = dst.Width / scale, ch = dst.Height / scale;
        var src = SKRect.Create((sw - cw) / 2f, (sh - ch) / 2f, cw, ch);
        c.Save();
        if (radius > 0) c.ClipRoundRect(new SKRoundRect(dst, radius), SKClipOperation.Intersect, true);
        using var p = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(A(alpha)) };
        c.DrawImage(img, src, dst, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), p);
        c.Restore();
    }

    public static SKColorFilter Saturation(float amount)
    {
        float s = amount;
        float ir = 0.213f * (1 - s), ig = 0.715f * (1 - s), ib = 0.072f * (1 - s);
        return SKColorFilter.CreateColorMatrix(new[]
        {
            ir + s, ig, ib, 0, 0,
            ir, ig + s, ib, 0, 0,
            ir, ig, ib + s, 0, 0,
            0, 0, 0, 1, 0,
        });
    }

    public static bool In(SKRect r, SKPoint? p) => p is { } q && r.Contains(q);

    public static string FormatTime(double seconds)
    {
        int s = Math.Max(0, (int)seconds);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 60) return "now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h";
        return $"{(int)d.TotalDays}d";
    }
}
