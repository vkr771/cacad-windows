using SkiaSharp;

namespace Cacad;

/// <summary>Drawn materials for the island's surface: aurora, nebula, ocean, sunset, ember, obsidian, and the soft
/// rosé, lavender and sakura. All are SkSL shaders rendered at reduced resolution (C3E renders their frames ahead of
/// time); nebula's stars are drawn crisp on top. Every surface is dark at heart so white content stays readable.</summary>
public static class Surfaces
{
    private const string Common = @"
uniform float2 uOrigin;   // island top-left, device px
uniform float2 uSize;     // island size, device px
uniform float uTime;      // seconds
uniform float2 uPointer;  // 0..1 across the island
uniform float uOpen;      // 0 pill .. 1 card

float hash21(float2 p) { return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float vnoise(float2 p) {
    float2 i = floor(p), f = fract(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + float2(1.0, 0.0)), u.x),
               mix(hash21(i + float2(0.0, 1.0)), hash21(i + float2(1.0, 1.0)), u.x), u.y);
}
float fbm4(float2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * vnoise(p); p = p * 2.03 + float2(1.7, 9.2); a *= 0.5; }
    return v;
}
";

    // Northern lights: swaying curtains, green into teal, violet at the brightest folds.
    private const string Aurora = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize.y;
    float t = uTime * 0.08;
    float x = uv.x * 1.3 + fbm4(float2(uv.x * 0.7, t)) * 1.4;
    float curtain = fbm4(float2(x * 1.6, uv.y * 0.55 - t * 2.0));
    float band = smoothstep(0.38, 0.86, curtain);
    float centre = 0.38 + 0.16 * sin(uv.x * 1.7 + t * 5.0);
    float fall = exp(-pow(uv.y - centre, 2.0) * 5.0);
    float3 green = float3(0.14, 0.95, 0.58), teal = float3(0.08, 0.65, 0.95), violet = float3(0.62, 0.32, 1.0);
    float3 col = mix(green, teal, smoothstep(0.15, 0.85, uv.y + 0.2 * sin(t * 4.0 + uv.x)));
    col = mix(col, violet, smoothstep(0.6, 1.0, curtain));
    float3 sky = float3(0.012, 0.02, 0.05) + float3(0.0, 0.015, 0.045) * (1.0 - uv.y);
    float3 o = sky + col * band * fall * 0.95;
    o *= 1.0 - 0.3 * uOpen;
    return half4(half3(o), 1.0);
}";

    // Deep space: two layers of drifting cloud and a warm core where they overlap.
    private const string Nebula = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize.y;
    float t = uTime * 0.025;
    float n1 = fbm4(uv * 1.5 + float2(t, -t * 0.5));
    float n2 = fbm4(uv * 3.0 - float2(t * 1.3, t) + n1 * 1.2);
    float3 deep = float3(0.016, 0.008, 0.04);
    float3 o = deep
        + float3(0.55, 0.14, 0.78) * pow(n1, 3.0) * 1.5
        + float3(0.10, 0.32, 0.95) * pow(n2, 3.4) * 1.3
        + float3(1.00, 0.46, 0.36) * pow(n1 * n2, 4.0) * 2.6;
    o *= 1.0 - 0.3 * uOpen;
    return half4(half3(o), 1.0);
}";

    // Ocean: deep water with caustic light rippling across it, brighter towards the surface (top).
    private const string Ocean = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize.y;
    float yy = (fc.y - uOrigin.y) / uSize.y;
    float t = uTime * 0.35;
    float2 p = uv * 3.2;
    float c = 0.0;
    for (int i = 0; i < 3; i++) {
        float fi = float(i);
        p += float2(sin(p.y * 1.3 + t + fi), cos(p.x * 1.1 - t * 0.8 + fi)) * 0.35;
        c += 1.0 / (1.0 + 16.0 * abs(sin(p.x * 1.7 + p.y * 1.3)));
    }
    c /= 3.0;
    float3 col = mix(float3(0.0, 0.17, 0.24), float3(0.0, 0.035, 0.07), smoothstep(0.0, 1.0, yy));
    col += float3(0.30, 0.90, 0.95) * pow(c, 1.5) * 0.95 * (1.0 - yy * 0.65);
    col *= 1.0 - 0.3 * uOpen;
    return half4(half3(col), 1.0);
}";

    // Sunset: dusk sky in bands, purple to rose to amber, a low sun drifting along the horizon.
    private const string Sunset = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize;
    float aspect = uSize.x / uSize.y;
    float t = uTime * 0.05;
    float y = uv.y + 0.035 * sin(uv.x * 7.0 + t * 7.0);
    float3 col = mix(float3(0.10, 0.035, 0.20), float3(0.50, 0.10, 0.30), smoothstep(0.0, 0.65, y));
    col = mix(col, float3(0.95, 0.42, 0.14), smoothstep(0.6, 1.15, y));
    float2 s = float2(0.5 + 0.38 * sin(t), 1.08);
    float d = length((uv - s) * float2(aspect, 1.0));
    col += float3(1.0, 0.66, 0.32) * exp(-d * d * 5.0) * 0.55;
    col *= 0.6 - 0.2 * uOpen;
    return half4(half3(col), 1.0);
}";

    // Ember: near-black, heat glowing up from below, sparks rising and swaying.
    private const string Ember = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize.y;
    float yy = (fc.y - uOrigin.y) / uSize.y;
    float t = uTime;
    float heat = fbm4(float2(uv.x * 2.0, uv.y * 2.0 + t * 0.35));
    float glow = smoothstep(0.25, 1.35, yy + heat * 0.55);
    float3 col = float3(0.03, 0.012, 0.008) + float3(0.95, 0.27, 0.05) * pow(glow, 2.4) * 0.6;
    float2 g = uv * float2(13.0, 6.5) + float2(0.0, t * 1.5);
    float2 id = floor(g);
    float2 f = fract(g) - 0.5;
    float r = hash21(id);
    float spark = step(0.87, r) * smoothstep(0.11, 0.0, length(f + float2(sin(t * 1.3 + r * 20.0) * 0.22, 0.0)));
    col += float3(1.0, 0.62, 0.22) * spark * (0.4 + 0.6 * (1.0 - yy));
    col *= 1.0 - 0.3 * uOpen;
    return half4(half3(col), 1.0);
}";

    // Obsidian: polished black glass; a slow band of light sweeps across, and the bottom edge shows a faint
    // blue-violet sheen like volcanic glass.
    private const string Obsidian = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize;
    float t = uTime * 0.09;
    float3 col = float3(0.010, 0.010, 0.014) + float3(0.012, 0.012, 0.02) * (1.0 - uv.y);
    float sweep = fract(t) * 1.8 - 0.4;
    float dx = uv.x - sweep - uv.y * 0.45;
    float band = exp(-dx * dx * 90.0);
    col += float3(0.50, 0.53, 0.66) * band * 0.22;
    float iri = 0.5 + 0.5 * sin(uv.x * 7.0 + t * 9.0);
    col += mix(float3(0.28, 0.10, 0.55), float3(0.05, 0.32, 0.55), iri) * smoothstep(0.7, 1.0, uv.y) * 0.10;
    return half4(half3(col), 1.0);
}";

    // Rosé: deep rose satin, folds of light flowing slowly through it in rose gold.
    private const string Rose = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize.y;
    float t = uTime * 0.07;
    // Broad, smooth folds (low frequency, soft highlight) lit from the top left, like silk catching a lamp.
    float w = fbm4(uv * 0.7 + float2(t, -t * 0.6));
    float fold = sin((uv.x * 1.1 + uv.y * 0.8 + w * 1.6) * 2.2 + t * 3.0) * 0.5 + 0.5;
    float lamp = exp(-dot(uv - float2(0.3, -0.2), uv - float2(0.3, -0.2)) * 0.9);
    float3 col = float3(0.17, 0.04, 0.09) + float3(0.95, 0.58, 0.60) * pow(fold, 3.0) * 0.16 * (0.5 + lamp)
        + float3(0.40, 0.10, 0.20) * w * 0.3 + float3(0.55, 0.22, 0.30) * lamp * 0.18;
    col *= 1.0 - 0.25 * uOpen;
    return half4(half3(col), 1.0);
}";

    // Lavender: a dreamy mesh of soft colour, lilac, periwinkle and pink, drifting over deep violet, with a fine grain.
    private const string Lavender = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize.y;
    float t = uTime * 0.15;
    float3 col = float3(0.085, 0.055, 0.17);
    float2 a = float2(0.6 + 0.5 * sin(t * 0.9), 0.3 + 0.2 * cos(t * 1.1));
    float2 b = float2(1.8 + 0.6 * cos(t * 0.7), 0.7 + 0.25 * sin(t * 0.8));
    float2 c = float2(3.0 + 0.5 * sin(t * 0.6 + 1.0), 0.4 + 0.3 * cos(t * 0.9));
    col += float3(0.55, 0.40, 0.95) * exp(-dot(uv - a, uv - a) * 2.2) * 0.42;
    col += float3(0.35, 0.45, 1.00) * exp(-dot(uv - b, uv - b) * 1.6) * 0.32;
    col += float3(0.95, 0.55, 0.85) * exp(-dot(uv - c, uv - c) * 2.0) * 0.28;
    col += (hash21(fc) - 0.5) * 0.025;
    col *= 1.0 - 0.25 * uOpen;
    return half4(half3(col), 1.0);
}";

    // Sakura: a pink twilight with soft out-of-focus blossoms of light drifting across.
    private const string Sakura = Common + @"
half4 main(float2 fc) {
    float2 uv = (fc - uOrigin) / uSize.y;
    float yy = (fc.y - uOrigin.y) / uSize.y;
    float aspect = uSize.x / uSize.y;
    float t = uTime * 0.1;
    float3 col = mix(float3(0.26, 0.09, 0.20), float3(0.09, 0.045, 0.13), yy);
    float gx = uv.x - aspect * 0.5;
    col += float3(0.45, 0.16, 0.30) * exp(-gx * gx * 0.6 - yy * 3.0) * 0.25;
    for (int i = 0; i < 7; i++) {
        float fi = float(i);
        float2 c = float2(fract(hash21(float2(fi, 1.0)) + t * 0.06 * (0.5 + hash21(float2(fi, 2.0)))) * (aspect + 0.6) - 0.3,
                          0.15 + 0.7 * hash21(float2(fi, 3.0)) + 0.08 * sin(t * 2.0 + fi));
        float r = 0.16 + 0.2 * hash21(float2(fi, 4.0));
        col += float3(1.0, 0.66, 0.80) * smoothstep(r, r * 0.55, length(uv - c)) * 0.16;
    }
    col *= 1.0 - 0.25 * uOpen;
    return half4(half3(col), 1.0);
}";

    private sealed record Def(string Sksl, bool Animated, double Interval);

    private static readonly Dictionary<string, Def> Defs = new()
    {
        ["aurora"] = new(Aurora, true, 1 / 24.0),
        ["nebula"] = new(Nebula, true, 1 / 15.0),
        ["ocean"] = new(Ocean, true, 1 / 20.0),
        ["sunset"] = new(Sunset, true, 1 / 10.0),
        ["ember"] = new(Ember, true, 1 / 20.0),
        ["obsidian"] = new(Obsidian, true, 1 / 15.0),
        ["rose"] = new(Rose, true, 1 / 12.0),
        ["lavender"] = new(Lavender, true, 1 / 15.0),
        ["sakura"] = new(Sakura, true, 1 / 12.0),
    };

    private static readonly Dictionary<string, SKRuntimeEffect?> Effects = new();

    private static SKRuntimeEffect? Effect(string id)
    {
        if (Effects.TryGetValue(id, out var e)) return e;
        if (!Defs.TryGetValue(id, out var def)) return Effects[id] = null;
        e = SKRuntimeEffect.CreateShader(def.Sksl, out var errors);
        if (e == null)
            try { File.AppendAllText(Path.Combine(SettingsStore.DataDir, "crash.log"), $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Surface '{id}' shader did not compile:\r\n{errors}\r\n\r\n"); } catch { }
        return Effects[id] = e;
    }

    /// <summary>Seconds between frames a surface needs to animate (MaxValue: still).</summary>
    public static double Interval(Theme t) => t.Surface switch
    {
        null => double.MaxValue,
        "manga" => Manga.Interval,
        _ => Defs.TryGetValue(t.Surface, out var d) ? d.Interval : double.MaxValue,
    };

    // ---------- cached shader layer ----------

    private static SKImage? _cached;
    private static (string id, int x, int y, int w, int h, int div, int frame, int open) _key;
    private static (string id, int x, int y, int w, int h, int div, int frame, int open) Key(string id, int x, int y, int w, int h, int div, int frame, int open) => (id, x, y, w, h, div, frame, open);

    /// <summary>The material under the island's content. <paramref name="island"/> is in device pixels; the canvas is
    /// in island DIPs (x = 0 at the centre), clipped to the shape.</summary>
    public static void DrawBase(SKCanvas c, Theme theme, SKRect island, float w, float h, float scale, double time, SKPoint pointer01, float open, bool moving)
    {
        string id = theme.Surface!;
        if (id == "custom") { CustomSurface.Draw(c, w, h); return; }
        if (id == "manga") { Manga.Draw(c, w, h, time, open); return; }
        var effect = Effect(id);
        if (effect != null && Defs.TryGetValue(id, out var def))
        {
            int div = def.Animated || moving ? 3 : 2;
            // Animated layers change every frame; a still one only when its size does.
            int frame = def.Animated ? (int)(time / def.Interval) : 0;
            var key = Key(id, (int)island.Left, (int)island.Top, (int)island.Width, (int)island.Height, div, frame, (int)(open * 40));
            if (_cached == null || key != _key)
            {
                // C3E: a moving material's next frame is rendered on a worker; until it lands, the last frame stays
                // up (same size and place only; a resize or the first frame renders here, so nothing ever stretches).
                bool sameFrameShape = _cached != null && (_key.id, _key.x, _key.y, _key.w, _key.h, _key.div, _key.open) == (key.id, key.x, key.y, key.w, key.h, key.div, key.open);
                if (C3E.On && def.Animated && sameFrameShape)
                {
                    var t0 = time;
                    var shape = (key.id, key.x, key.y, key.w, key.h, key.div, key.open);
                    var (ready, readyFrame) = C3E.Ahead(shape, _key.frame, frame, () => Render(effect, island, div, t0, pointer01, open));
                    if (ready != null) { _cached?.Dispose(); _cached = ready; _key = key with { frame = readyFrame }; }
                }
                else
                {
                    _cached?.Dispose();
                    _cached = Render(effect, island, div, time, pointer01, open);
                    _key = key;
                }
            }
            if (_cached != null)
            {
                c.Save();
                c.ResetMatrix();
                c.DrawImage(_cached, SKRect.Create(island.Left - 2, island.Top - 2, island.Width + 4, island.Height + 4),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                c.Restore();
            }
        }

        if (id == "nebula") Stars(c, w, h, time);
    }

    private static SKImage? Render(SKRuntimeEffect effect, SKRect island, int div, double time, SKPoint pointer01, float open)
    {
        var area = SKRect.Create(island.Left - 2, island.Top - 2, island.Width + 4, island.Height + 4);
        int pw = Math.Max(1, (int)Math.Ceiling(area.Width / div)), ph = Math.Max(1, (int)Math.Ceiling(area.Height / div));
        using var surface = SKSurface.Create(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface == null) return null;
        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["uOrigin"] = new SKPoint(island.Left, island.Top),
            ["uSize"] = new SKPoint(Math.Max(1, island.Width), Math.Max(1, island.Height)),
            ["uTime"] = (float)(time % 10000),
            ["uPointer"] = pointer01,
            ["uOpen"] = open,
        };
        using var shader = effect.ToShader(uniforms);
        using var paint = new SKPaint { Shader = shader };
        var canvas = surface.Canvas;
        canvas.Scale(pw / area.Width, ph / area.Height);
        canvas.Translate(-area.Left, -area.Top);
        canvas.DrawRect(area, paint);
        return surface.Snapshot().ToRasterImage(true);
    }

    // ---------- crisp vector layers (island DIPs) ----------

    private static float Hash(int n)
    {
        uint x = unchecked((uint)n * 2654435761u);
        x ^= x >> 15; x = unchecked(x * 2246822519u); x ^= x >> 13;
        return (x & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Stars at fixed places across the full card, so they stay put while the island grows and shrinks.</summary>
    private static void Stars(SKCanvas c, float w, float h, double time)
    {
        using var p = new SKPaint { IsAntialias = true };
        float t = (float)time;
        const float spanW = 560, spanH = 240;
        for (int i = 0; i < 70; i++)
        {
            float x = (Hash(i * 3 + 1) - 0.5f) * spanW, y = Hash(i * 7 + 2) * spanH;
            if (Math.Abs(x) > w / 2 + 2 || y > h + 2) continue;
            float tw = 0.25f + 0.75f * MathF.Pow(0.5f + 0.5f * MathF.Sin(t * (0.6f + 1.8f * Hash(i * 11 + 3)) + i * 1.7f), 3);
            float size = 0.5f + 1.1f * Hash(i * 13 + 4) * Hash(i * 17 + 5);
            p.Color = new SKColor(255, 250, 240, G.A(tw * 0.9f));
            c.DrawCircle(x, y, size, p);
            if (size > 1.15f && tw > 0.7f)
            {
                // The brightest few get a faint four-point sparkle.
                p.Color = new SKColor(255, 250, 240, G.A((tw - 0.7f) * 1.2f));
                c.DrawRect(x - size * 3, y - 0.3f, size * 6, 0.6f, p);
                c.DrawRect(x - 0.3f, y - size * 3, 0.6f, size * 6, p);
            }
        }
    }

    /// <summary>The rim, on top of the content. False: the island draws its usual hairline (every surface does now).</summary>
    public static bool DrawRim(SKCanvas c, Theme theme, SKPath shape, float w, float h, double time) => false;
}
