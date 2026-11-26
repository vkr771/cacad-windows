using SkiaSharp;

namespace Cacad;

/// <summary>Prism Glass: a refracting lens over the backdrop. An SkSL shader (run on the CPU by Skia's raster
/// pipeline) measures each pixel's distance to the island's rim with a signed-distance function, bends the
/// background along the rim's normal, splits it into colour fringes there, and magnifies and blurs the centre.
/// The lens is soft by nature, so it is rendered at half resolution and cached until the backdrop or the
/// island's shape changes; the crisp rim light is drawn on top as a vector stroke.</summary>
public static class PrismGlass
{
    private const string Sksl = @"
uniform shader soft;      // lightly blurred backdrop: the bent rim stays readable
uniform shader heavy;     // heavily blurred backdrop: the thick middle of the lens
uniform float2 center;    // island centre, device px
uniform float2 halfSize;  // island half extents, device px
uniform float radius;     // corner radius, device px
uniform float bezel;      // how far in from the rim the bending reaches
uniform float bendPx;     // strongest bend at the rim, px ('refract' is a built-in)
uniform float aberr;      // colour split, as a fraction of the bend
uniform float notch;      // 1: the top edge is the screen edge, not glass
uniform float dim;        // darkening for legible text when the card is open
uniform float profile;    // how the bend falls off inward: 2 = gentle (prism), 3+ = packed at the rim (liquid)
uniform float2 magBase;   // middle magnification at the centre (below 1 = magnified)
uniform float2 magEdge;   // ... and how much it relaxes toward the rim
uniform float split0;     // colour split in the middle, at the centre
uniform float split1;     // ... and added toward the rim
uniform float frost;      // share of the heavy blur in the middle
uniform float gatherK;    // light gathered at the rim
uniform float satK;       // colour boost
uniform float streak;     // 1: in a notch the middle samples one row above centre (prism's stretched streaks); 0: true position

float sdf(float2 q) {
    float2 d = abs(q) - (halfSize - radius);
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
}

half4 main(float2 p) {
    float2 qTrue = p - center;
    float2 q = qTrue;
    // A notch hangs from the screen edge: rows above the middle measure only to the sides.
    if (notch > 0.5) q.y = max(q.y, 0.0);

    float d = sdf(q);
    float t = clamp(1.0 + d / bezel, 0.0, 1.0);        // 1 on the rim, 0 deep inside

    float2 n = float2(sdf(q + float2(1.0, 0.0)) - sdf(q - float2(1.0, 0.0)),
                      sdf(q + float2(0.0, 1.0)) - sdf(q - float2(0.0, 1.0)));
    n = n / (length(n) + 1e-4);                         // outward normal

    // The rim bends light from around the glass into it: each pixel near the edge shows the scene just
    // outside, compressed, each colour by a different amount. Over a black top edge this is what pulls the
    // wallpaper's colour below the pill into it, instead of the glass only ever showing the black under itself.
    float bend = pow(t, profile) * bendPx;
    float2 o = n * bend;
    half3 rim;
    rim.r = soft.eval(p + o * (1.0 + aberr)).r;
    rim.g = soft.eval(p + o).g;
    rim.b = soft.eval(p + o * (1.0 - aberr)).b;

    // The middle is lens too, not frost: a barrel distortion over the whole surface, magnifying most at the
    // centre (the stretched streaks of thick glass) and easing toward the rim, with the colours splitting more
    // the further out they bend. Only a touch of the heavy blur is mixed in.
    float2 qm = mix(qTrue, q, streak);
    float2 nq = qm / halfSize;                           // -1..1 across the island
    float r2 = clamp(dot(nq, nq) * 0.5, 0.0, 1.0);
    float2 mag = magBase + magEdge * r2;
    float2 lq = qm * mag;
    float split = split0 + split1 * r2;
    half3 mid;
    mid.r = soft.eval(center + lq * (1.0 + split)).r;
    mid.g = soft.eval(center + lq).g;
    mid.b = soft.eval(center + lq * (1.0 - split)).b;
    mid = mix(mid, heavy.eval(center + lq).rgb, half(frost));

    half3 col = mix(mid, rim, smoothstep(0.0, 0.75, t));

    // Glass gathers light toward its edge: brighten what is there (glossy black stays black, colour glows)
    // rather than adding flat white, which turned a dark backdrop into dull grey.
    float gather = pow(t, 2.5);
    col += col * half(gather * gatherK) + half3(gather * 0.015);
    half luma = dot(col, half3(0.2126, 0.7152, 0.0722));
    col = mix(half3(luma), col, half(satK));

    col *= half(1.0 - dim);
    return half4(clamp(col, 0.0, 1.0), 1.0);
}";

    /// <summary>How a refracting glass behaves. Sizes are in DIPs (scaled to device pixels when drawn).</summary>
    public sealed record Look(string Id, float BezelDip, float BezelShare, float BendDip, float Profile, float Aberr,
        SKPoint MagBase, SKPoint MagEdge, float Split0, float Split1, float Frost, float Gather, float Saturation, bool Streak);

    /// <summary>Thick prism: the whole surface is lens, streaked and colour-split, with a wide bending band.</summary>
    public static readonly Look Prism = new("prism", 24, 0.34f, 18, 2.0f, 0.6f,
        new SKPoint(0.52f, 0.72f), new SKPoint(0.40f, 0.24f), 0.035f, 0.09f, 0.22f, 0.45f, 1.25f, Streak: true);

    /// <summary>Liquid Glass, as Apple's material: clear and nearly undistorted through the middle, the bending
    /// packed into a narrow band at the rim (a steep falloff), only a faint colour fringe, almost no frost.</summary>
    public static readonly Look Liquid = new("liquid", 18, 0.22f, 16, 3.4f, 0.22f,
        new SKPoint(0.965f, 0.965f), new SKPoint(0.035f, 0.035f), 0.0f, 0.012f, 0.05f, 0.30f, 0.92f, Streak: false);

    public static Look For(Theme theme) => theme.Id == "liquid" ? Liquid : Prism;

    private static SKRuntimeEffect? _effect;
    private static bool _failed;
    public static string? Error { get; private set; }

    private static SKRuntimeEffect? Effect()
    {
        if (_effect != null || _failed) return _effect;
        _effect = SKRuntimeEffect.CreateShader(Sksl, out var errors);
        if (_effect == null)
        {
            // Fall back to Frosted Glass, and leave the compiler's reason where it can be found.
            _failed = true;
            Error = errors;
            try { File.AppendAllText(Path.Combine(SettingsStore.DataDir, "crash.log"), $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Prism Glass shader did not compile:\r\n{errors}\r\n\r\n"); } catch { }
        }
        return _effect;
    }

    // ---------- the cache ----------

    private static SKImage? _cached;
    private static (string look, int version, int x, int y, int w, int h, int r, bool notch, int dim, int div) _key;
    private static SKRect _cachedRect;

    /// <summary>Fill the current clip (the island's shape) with the lens. <paramref name="island"/> is in device pixels.</summary>
    public static bool Draw(SKCanvas c, Look look, SKImage heavy, SKImage soft, int backdropVersion, SKRect island, float radius,
        bool notch, float open, float scale, float windowW, float windowH, bool moving = false)
    {
        var effect = Effect();
        if (effect == null) return false;
        // Adaptive tint: over a bright scene, darken just enough that white text and digits stay readable;
        // over a dark one, add nothing. The open card always gets a little more for its denser content.
        float luma = Brightness(heavy, island, windowW, windowH, backdropVersion);
        // Aim a little below the target: the rim's gathered light brightens the result again afterwards.
        const float target = 0.34f;
        float needed = luma > target ? 1 - target / luma : 0;
        float dim = Math.Clamp(Math.Max(needed, 0.30f * open), 0, 0.7f);
        // Quantise the key so tiny spring jitter does not rebuild the lens every frame at rest.
        // In motion the lens is a blur of movement anyway: a third of the resolution; settled, half.
        int div = moving ? 3 : 2;
        var key = (look.Id, backdropVersion, (int)island.Left, (int)island.Top, (int)island.Width, (int)island.Height, (int)radius, notch, (int)(dim * 50), div);
        var area = SKRect.Create(island.Left - 2, island.Top - 2, island.Width + 4, island.Height + 4);

        if (_cached == null || key != _key)
        {
            _cached?.Dispose();
            _cached = Render(effect, look, heavy, soft, island, area, radius, notch, dim, scale, windowW, windowH, div);
            _key = key;
            _cachedRect = area;
        }
        if (_cached == null) return false;
        c.Save();
        c.ResetMatrix();
        c.DrawImage(_cached, _cachedRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        c.Restore();
        return true;
    }

    private static (int version, int x, int y, int w, int h) _lumaKey;
    private static float _luma;

    /// <summary>Average brightness (0..1) of the backdrop under the island, from a coarse grid of samples.
    /// Cached per backdrop and island bounds, so it costs nothing on a frame that only animates content.</summary>
    private static float Brightness(SKImage heavy, SKRect island, float windowW, float windowH, int version)
    {
        var key = (version, (int)island.Left / 8, (int)island.Top / 8, (int)island.Width / 8, (int)island.Height / 8);
        if (key == _lumaKey) return _luma;
        _lumaKey = key;
        using var pixmap = heavy.PeekPixels();
        if (pixmap == null) return _luma = 0;
        float sx = heavy.Width / windowW, sy = heavy.Height / windowH, sum = 0;
        int n = 0;
        for (int j = 0; j < 6; j++)
            for (int i = 0; i < 16; i++)
            {
                int x = (int)((island.Left + island.Width * (i + 0.5f) / 16) * sx), y = (int)((island.Top + island.Height * (j + 0.5f) / 6) * sy);
                if (x < 0 || y < 0 || x >= heavy.Width || y >= heavy.Height) continue;
                var c = pixmap.GetPixelColor(x, y);
                sum += (0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue) / 255f;
                n++;
            }
        return _luma = n == 0 ? 0 : sum / n;
    }

    private static SKImage? Render(SKRuntimeEffect effect, Look look, SKImage heavy, SKImage soft, SKRect island, SKRect area, float radius,
        bool notch, float dim, float scale, float windowW, float windowH, int div)
    {
        // A fraction of the resolution: the lens is soft anyway.
        int w = Math.Max(1, (int)Math.Ceiling(area.Width / div)), h = Math.Max(1, (int)Math.Ceiling(area.Height / div));
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface == null) return null;

        // The backdrops are half-size images of the whole window: map them back over it.
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        using var softShader = soft.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, SKMatrix.CreateScale(windowW / soft.Width, windowH / soft.Height));
        using var heavyShader = heavy.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, SKMatrix.CreateScale(windowW / heavy.Width, windowH / heavy.Height));

        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["center"] = new SKPoint(island.MidX, island.MidY),
            ["halfSize"] = new SKPoint(island.Width / 2, island.Height / 2),
            ["radius"] = Math.Min(radius, Math.Min(island.Width, island.Height) / 2),
            // How far in the bending reaches: a share of the shorter side, never less than a fixed band (or half the pill).
            ["bezel"] = Math.Max(Math.Min(look.BezelDip * scale, island.Height * 0.5f), Math.Min(island.Width, island.Height) * look.BezelShare),
            ["bendPx"] = look.BendDip * scale,
            ["aberr"] = look.Aberr,
            ["notch"] = notch ? 1f : 0f,
            ["dim"] = dim,
            ["profile"] = look.Profile,
            ["magBase"] = look.MagBase,
            ["magEdge"] = look.MagEdge,
            ["split0"] = look.Split0,
            ["split1"] = look.Split1,
            ["frost"] = look.Frost,
            ["gatherK"] = look.Gather,
            ["satK"] = look.Saturation,
            ["streak"] = look.Streak ? 1f : 0f,
        };
        var children = new SKRuntimeEffectChildren(effect)
        {
            ["soft"] = softShader,
            ["heavy"] = heavyShader,
        };
        using var shader = effect.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader };
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        // Device coordinates in, half-size pixels out: the shader still sees device pixels.
        canvas.Scale(w / area.Width, h / area.Height);
        canvas.Translate(-area.Left, -area.Top);
        canvas.DrawRect(area, paint);
        return surface.Snapshot().ToRasterImage(true);
    }

    /// <summary>The rim light: crisp, brightest along the top-left. Drawn in the island's own (DIP) coordinates,
    /// clipped to its shape, so half of the stroke falls inside.</summary>
    public static void DrawRim(SKCanvas c, Look look, SKPath shape, float w, float h, bool notch)
    {
        bool liquid = look == Liquid;
        // Liquid Glass: a thin specular line that catches the light at two opposite corners and fades along the sides.
        using var light = liquid
            ? SKShader.CreateLinearGradient(new SKPoint(-w / 2, 0), new SKPoint(w / 2, h),
                new[] { G.White(0.95f), G.White(0.22f), G.White(0.04f), G.White(0.04f), G.White(0.22f), G.White(0.75f) },
                new[] { 0f, 0.16f, 0.36f, 0.64f, 0.84f, 1f }, SKShaderTileMode.Clamp)
            : SKShader.CreateLinearGradient(new SKPoint(-w / 2, 0), new SKPoint(w / 2, h),
                new[] { G.White(0.85f), G.White(0.35f), G.White(0.12f), G.White(0.45f) }, new[] { 0f, 0.35f, 0.7f, 1f }, SKShaderTileMode.Clamp);
        using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = liquid ? 1.8f : 2.4f, Shader = light };
        c.DrawPath(shape, p);
        // A second, faint line further in: the inner face of the glass catching light.
        using var inner = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f, Color = G.White(0.06f), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) };
        c.DrawPath(shape, inner);
    }
}
