using SkiaSharp;

namespace Cacad;

/// <summary>Faces the cat can make.</summary>
public enum CatFace { Open, Happy, Closed, Determined, XEyes, Spiral, Surprised, Grumpy, Love, Droopy, Wink, Yawn, Shades, Sing }

/// <summary>A coat: fur, markings, inner ears, and the lit outline that keeps it visible on a dark island.</summary>
public sealed record CatSkin(string Id, string Name, SKColor Body, SKColor Mark, SKColor Ear, SKColor Rim);

/// <summary>A companion: its head shape on the shared grid, whether it has whiskers, and its own coat.</summary>
public sealed record CatSpecies(string Id, string Name, string[] Head, bool Whiskers, CatSkin Natural, bool Anime = false, SKColor Iris = default);

/// <summary>One pose of the cat: a face, what it wears, what is beside it, and how it moves.</summary>
public sealed record CatPose(CatFace Face, string? Worn = null, string? Side = null, string? Bubble = null,
    string Motion = "idle", string? Particles = null);

/// <summary>Pixel-art cat, drawn in code on a 16 x 11 grid (ears on row 0), plus its props. Original art in the
/// style of a small black pixel cat; everything is plain rectangles so it stays crisp at any scale and can take the
/// theme's accent colour.</summary>
public static partial class CatArt
{
    // ---------- palette ----------

    private static readonly Dictionary<char, SKColor> Palette = new()
    {
        ['w'] = new SKColor(239, 237, 232),   // white
        ['s'] = new SKColor(170, 172, 180),   // silver
        ['g'] = new SKColor(118, 120, 130),   // grey
        ['d'] = new SKColor(66, 68, 76),      // dark grey
        ['k'] = new SKColor(16, 16, 20),      // near black
        ['y'] = new SKColor(255, 210, 80),    // yellow
        ['o'] = new SKColor(255, 150, 60),    // orange
        ['r'] = new SKColor(240, 80, 80),     // red
        ['p'] = new SKColor(255, 140, 180),   // pink
        ['b'] = new SKColor(90, 150, 255),    // blue
        ['c'] = new SKColor(80, 220, 240),    // cyan
        ['G'] = new SKColor(90, 220, 130),    // green
        ['n'] = new SKColor(150, 100, 60),    // brown
        ['v'] = new SKColor(170, 120, 255),   // violet
    };

    // ---------- companions and skins ----------

    private static SKColor C(int r, int g, int b) => new((byte)r, (byte)g, (byte)b);

    /// <summary>Every companion, on the same 16 x 11 grid so all poses and props fit each one.
    /// '#' fur, 'e' inner ear, 'm' markings (ears, patches, muzzle).</summary>
    public static readonly CatSpecies[] Species =
    {
        new("cat", "Cat", new[]
        {
            ".#............#.",
            ".##..........##.",
            ".#e#........#e#.",
            ".##############.",
            "################",
            "################",
            "################",
            "################",
            "################",
            ".##############.",
            "..############..",
        }, true, new("natural", "Natural", C(62, 63, 72), C(200, 200, 205), C(104, 82, 96), C(138, 140, 152))),
        new("dog", "Pup", new[]
        {
            "................",
            "...##########...",
            "..############..",
            "mm############mm",
            "mmm##########mmm",
            "mmm##########mmm",
            "mmm##########mmm",
            ".mm##########mm.",
            "..############..",
            "...##########...",
            "....########....",
        }, false, new("natural", "Natural", C(184, 136, 88), C(108, 72, 44), C(108, 72, 44), C(222, 184, 138))),
        new("bunny", "Bunny", new[]
        {
            "...##......##...",
            "..#ee#....#ee#..",
            "..#ee#....#ee#..",
            "..#ee#....#ee#..",
            "..#ee#....#ee#..",
            "..#ee#....#ee#..",
            "...##......##...",
            "..############..",
            ".##############.",
            "################",
            "################",
            "################",
            ".##############.",
            "..############..",
        }, true, new("natural", "Natural", C(234, 230, 224), C(240, 170, 190), C(240, 170, 190), C(255, 255, 255))),
        new("fox", "Fox", new[]
        {
            "#..............#",
            "##............##",
            "#e#..........#e#",
            "#ee##########ee#",
            "################",
            "################",
            "################",
            "mm############mm",
            "mmm##########mmm",
            ".mmm########mmm.",
            "...mm######mm...",
        }, true, new("natural", "Natural", C(232, 118, 48), C(246, 240, 232), C(70, 42, 36), C(255, 172, 112))),
        new("panda", "Panda", new[]
        {
            ".mm..........mm.",
            "mmmm........mmmm",
            "mmmm########mmmm",
            ".##############.",
            "###mmm####mmm###",
            "##mmmm####mmmm##",
            "##mmmm####mmmm##",
            "###mm######mm###",
            "################",
            ".##############.",
            "..############..",
        }, false, new("natural", "Natural", C(236, 234, 230), C(30, 30, 34), C(30, 30, 34), C(176, 176, 186))),
        new("bear", "Bear", new[]
        {
            ".##..........##.",
            "#ee#........#ee#",
            "#ee##########ee#",
            ".##############.",
            "################",
            "################",
            "################",
            "#####mmmmmm#####",
            "####mmmmmmmm####",
            ".###mmmmmmmm###.",
            "..###mmmmmm###..",
        }, false, new("natural", "Natural", C(124, 82, 52), C(206, 166, 120), C(92, 58, 36), C(176, 128, 88))),
        new("mouse", "Mouse", new[]
        {
            ".###........###.",
            "#eee#......#eee#",
            "#eee########eee#",
            ".##############.",
            "################",
            "################",
            "################",
            "################",
            ".##############.",
            "..############..",
            "....########....",
        }, true, new("natural", "Natural", C(150, 152, 164), C(230, 230, 236), C(240, 168, 186), C(212, 214, 224))),

        // Anime girls (original designs), drawn smooth rather than in pixels: see CatArt.Anime.cs. A coat recolours
        // their hair (Body), and their ribbon, ears or clip (Ear). The grid is only their footprint.
        // Their pixel art: '#' hair, 'm' hair in shade, 'e' ribbon / clip / inner ear, 'f' face. The face keeps the
        // eye cells (columns 4-5 and 10-11, rows 5-6) and the mouth cells clear, so every pixel face fits.
        Girl("hana", "Hana", C(246, 150, 188), C(232, 62, 96), C(214, 70, 120), new[]   // long and straight, hime cut, a bow
        {
            "....#######ee...",
            "..#########eee..",
            ".############e#.",
            ".##############.",
            ".##ffffffffff##.",
            ".##ffffffffff##.",
            ".##ffffffffff##.",
            ".##ffffffffff##.",
            ".m#ffffffffff#m.",
            ".m#.ffffffff.#m.",
            ".mm..ffffff..mm.",
        }),
        Girl("neko", "Neko", C(52, 50, 72), C(240, 160, 190), C(170, 110, 220), new[]   // a bob with cat ears
        {
            ".##..........##.",
            ".#e#........#e#.",
            ".#e##########e#.",
            "################",
            "###ff#ffff#ff###",
            "##ffffffffffff##",
            "##ffffffffffff##",
            "##ffffffffffff##",
            ".#ffffffffffff#.",
            ".m#ffffffffff#m.",
            "..m..ffffff..m..",
        }),
        Girl("yuki", "Yuki", C(232, 236, 248), C(92, 150, 255), C(80, 160, 230), new[]   // twin tails with ribbons
        {
            "....########....",
            "...##########...",
            "e.############.e",
            "ee############ee",
            "m.#ff#ffff#ff#.m",
            "m.#ffffffffff#.m",
            "mm#ffffffffff#mm",
            "mm.ffffffffff.mm",
            "mm.ffffffffff.mm",
            ".m..ffffffff..m.",
            ".m...ffffff...m.",
        }),
        Girl("kitsu", "Kitsu", C(240, 140, 60), C(255, 240, 226), C(220, 140, 40), new[]   // fluffy, big fox ears
        {
            "#..............#",
            "##............##",
            "#e#..........#e#",
            "#ee##########ee#",
            "###f##ffff##f###",
            "##ffffffffffff##",
            "##ffffffffffff##",
            "#mffffffffffffm#",
            "#mffffffffffffm#",
            ".m.ffffffffff.m.",
            "....ffffffff....",
        }),
        Girl("hoshi", "Hoshi", C(84, 122, 232), C(255, 212, 80), C(230, 170, 50), new[]   // short, side ponytail, star clip
        {
            "....########....",
            "...##########...",
            "..e###########..",
            ".eee##########mm",
            ".#e##fff#fffff#m",
            ".##ffffffffff#mm",
            ".##ffffffffff#mm",
            ".#.ffffffffff.mm",
            "...ffffffffff.m.",
            "....ffffffff....",
            ".....ffffff.....",
        }),
        Girl("kuri", "Kuri", C(186, 100, 78), C(84, 182, 168), C(236, 150, 40), new[]   // wavy chestnut hair, pony ears, a beret
        {
            "##...eeeeee...##",
            "#m#.eeeeeeee.#m#",
            ".#m##########m#.",
            ".##############.",
            "###ff#fff#ff####",
            "##ffffffffffff##",
            "##ffffffffffff##",
            "##ffffffffffff##",
            "#mffffffffffffm#",
            "#m#ffffffffff#m#",
            ".mm.ffffffff.mm.",
        }),
    };

    /// <summary>Coats that fit any companion. "natural" is each companion's own.</summary>
    public static readonly CatSkin[] Skins =
    {
        new("natural", "Natural", default, default, default, default),
        new("charcoal", "Charcoal", C(62, 63, 72), C(196, 196, 204), C(104, 82, 96), C(138, 140, 152)),
        new("snow", "Snow", C(238, 236, 232), C(70, 70, 78), C(240, 170, 190), C(255, 255, 255)),
        new("ginger", "Ginger", C(228, 128, 52), C(250, 236, 220), C(184, 92, 60), C(255, 182, 112)),
        new("cream", "Cream", C(232, 206, 166), C(140, 100, 72), C(214, 150, 140), C(255, 236, 204)),
        new("midnight", "Midnight", C(36, 42, 84), C(120, 220, 255), C(90, 64, 150), C(100, 172, 255)),
        new("mint", "Mint", C(124, 212, 172), C(32, 96, 84), C(240, 170, 190), C(196, 255, 228)),
        new("sakura", "Sakura", C(255, 172, 202), C(156, 64, 106), C(255, 120, 162), C(255, 218, 232)),
        new("gold", "Gold", C(222, 172, 62), C(122, 82, 22), C(182, 122, 40), C(255, 228, 132)),
        new("void", "Void", C(14, 14, 18), C(150, 110, 255), C(150, 110, 255), C(170, 130, 255)),
    };

    private static CatSpecies _sp = Species[0];
    private static CatSkin _sk = Species[0].Natural;

    private static void Use(string? species, string? skin)
    {
        _sp = Species.FirstOrDefault(s => s.Id == species) ?? Species[0];
        _sk = skin is null or "natural" ? _sp.Natural : Skins.FirstOrDefault(s => s.Id == skin) ?? _sp.Natural;
    }

    private static SKColor Body => _sk.Body;
    private static SKColor Rim => _sk.Rim;
    private static string[] Head => _sp.Head;

    private static SKColor Under(int col, int row)
    {
        int r = row + Head.Length - 11;
        char ch = r >= 0 && r < Head.Length && col >= 0 && col < 16 ? Head[r][col] : '.';
        return ch switch { 'e' => _sk.Ear, 'm' => _sk.Mark, 'f' => Skin, '.' => SKColors.Black, _ => _sk.Body };
    }

    private static bool Light(SKColor c) => (0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue) / 255f > 0.62f;
    private static readonly SKColor FaceInk = C(26, 24, 30);
    private static readonly SKColor Skin = C(255, 226, 206);   // the anime girls' faces (a coat recolours their hair)

    // ---------- props beside the cat (drawn right of the head) ----------

    private static readonly Dictionary<string, string[]> Side = new()
    {
        ["bulb"] = new[] { "..yyyy..", ".yyyyyy.", "yyywwyyy", "yyywyyyy", ".yyyyyy.", "..yyyy..", "..gggg..", "..ssss..", "...gg..." },
        ["bolt"] = new[] { "..yyy", ".yyy.", "yyy..", "yyyyy", "..yy.", ".yy..", ".y..." },
        ["coffee"] = new[] { ".wwww..", "wwwwww.", "nnnnnn.", "wwwwwwss", "wwwwww.s", "wwwwwwss", ".wwww..." },
        ["checklist"] = new[] { "ssssss", "sGsdds", "ssssss", "sGsdds", "ssssss", "sGsdds", "ssssss" },
        ["scroll"] = new[] { "nnnnnnn", ".wwwww.", ".wdddw.", ".wwwww.", ".wddww.", ".wwwww.", ".wdddw.", "nnnnnnn" },
        ["wrench"] = new[] { "s.s", "sss", ".s.", ".s.", ".s.", ".s.", "sss", "s.s" },
        ["laptop"] = new[] { ".ccccccc.", ".cbbbbbc.", ".cbwbbbc.", ".cbbbbbc.", ".ccccccc.", "sssssssss", ".sssssss." },
        ["arm"] = new[] { "..##....", ".####...", "##.###..", "#...###.", "....####", "...#####", "..######" },
        ["browser"] = new[] { "ggggggggg", "grgygGggg", "wwwwwwwww", "wbbwddddw", "wbbwwwwww", "wbbwdddww", "wwwwwwwww" },
        ["controller"] = new[] { ".ggggggg.", "gdgggggrg", "dddgggbgy", "gdggggGgg", "gg.....gg" },
        ["popcorn"] = new[] { ".wyw.w.", "wywwywy", ".wwwww.", "rwrwrwr", "rwrwrwr", ".rwrwr.", ".rwrwr." },
        ["keyboard"] = new[] { "ssssssssss", "sdsdsdsdss", "sdsdsdsdss", "ssddddddss" },
        ["camera"] = new[] { "..ddd...", "dddddddd", "ddswwsdd", "ddwwwwdd", "ddswwsdd", "dddddddd" },
        ["battery"] = new[] { "gggggggg.", "g......gg", "gr.....gg", "g......gg", "gggggggg." },
        ["plug"] = new[] { "s.s....", "s.s....", "sss.r.r", ".s...r.", ".s..r.r" },
        ["gift"] = new[] { "..r..r..", "...rr...", "rrrrrrrr", "bbbrrbbb", "bbbrrbbb", "bbbrrbbb", "bbbrrbbb" },
        ["fish"] = new[] { "...bbbb..", "b.bbbbbwb", "bbbbbbbbb", "b.bbbbbb.", "...bbbb.." },
        ["moon"] = new[] { "..yyy.", ".yy...", "yy....", "yy....", "yy....", ".yy...", "..yyy." },
        ["sun"] = new[] { "y..y..y", ".yyyyy.", ".yyyyy.", "yyyyyyy", ".yyyyy.", ".yyyyy.", "y..y..y" },
        ["note"] = new[] { "..www", "..w.w", "..w..", "..w..", "www..", "www.." },
        ["spiral"] = new[] { "ggggggg", "g.....g", "g.ggg.g", "g.g.g.g", "g.g.ggg", "g.g....", "g.ggggg" },
        ["bang"] = new[] { "yy", "yy", "yy", "yy", "..", "yy" },
        ["paw"] = new[] { ".#.#.", "#####", "#####", ".###." },
        ["docs"] = new[] { "wwww.", "wddw.", "wwwwss", "wddws.", ".sssss" },
        ["box"] = new[] { "nnnnnnn", "nwwwwwn", "nnnnnnn" },
        ["sweat"] = new[] { ".c", "cc", "cc" },
        ["umbrella"] = new[] { "..bbb..", ".bbbbb.", "bbbbbbb", "b.b.b.b", "...s...", "...s...", ".s.s...", "..s...." },
        ["snowflake"] = new[] { "..w..", "w.w.w", ".www.", "wwwww", ".www.", "w.w.w", "..w.." },
        ["robot"] = new[] { "...c...", "...s...", ".sssss.", "ssbsbss", "sssssss", "sswwwss", ".sssss." },
        ["chart"] = new[] { "....GGG", ".....GG", "G...G.G", "GG.G...", ".GG....", "..G....", "sssssss" },
        ["mic"] = new[] { ".ss.", "sdds", "sdds", ".ss.", "..g.", "..g.", "..g.", "..g." },
        ["fan"] = new[] { "..s..", "..s..", "sssss", "..s..", "..s.." },
    };

    /// <summary>How open the mouth is while singing, 0..1 (set each frame by the companion from the voice in the
    /// music).</summary>
    public static float SingOpen;

    /// <summary>The hat the companion has unlocked and chosen (see <see cref="Pet"/>), or null.</summary>
    public static string? HatId;

    // ---------- drawing ----------

    /// <summary>Draw the cat with its top-left at (x, y), each grid cell <paramref name="px"/> DIPs.</summary>
    public static void Draw(SKCanvas c, CatPose pose, float x, float y, float px, float a, double t, float beat, float pop, bool blink, bool earTwitch,
        string? species = null, string? skin = null, string? style = null)
    {
        if (a <= 0.003f) return;
        Use(species, skin);
        _style = Styles.Any(st => st.Id == style) ? style! : "pixel";
        c.Save();
        // Motion: breathing, bobbing to music, a dizzy wobble, a jump, a pop on a mood change.
        float headW = 16 * px, headH = 11 * px, cx = x + headW / 2, by = y + headH;
        float dy = 0, rot = 0, sx = 1, sy = 1 + 0.025f * MathF.Sin((float)t * 2.2f);
        switch (pose.Motion)
        {
            case "bob": dy = -beat * 2.4f * px; rot = MathF.Sin((float)t * 6) * 4 * Math.Max(0.3f, beat); break;
            case "wobble": rot = MathF.Sin((float)t * 5) * 9; break;
            case "jump": dy = -MathF.Abs(MathF.Sin((float)t * 9)) * 3 * px; break;
            case "shake": rot = MathF.Sin((float)t * 30) * 4; break;
            case "tilt": rot = 10 * MathF.Sin((float)t * 1.2f); break;
            case "flat": sy = 0.82f; break;
            case "type": dy = (MathF.Sin((float)t * 18) > 0 ? -0.5f : 0) * px; break;
        }
        float s = 1 + pop * 0.22f;
        c.Translate(cx, by + dy);
        c.RotateDegrees(rot);
        c.Scale(sx * s, sy * s);
        c.Translate(-cx, -by);

        bool ball = pose.Face == CatFace.Closed && pose.Motion == "ball" && !_sp.Anime;   // the girls just close their eyes
        var shown = blink && pose.Face is CatFace.Open or CatFace.Determined or CatFace.Surprised ? CatFace.Closed : pose.Face;
        if (_sp.Anime && _style == "anime") DrawGirl(c, pose, shown, x, y, px, a, t);
        else if (_sp.Anime && _style != "pixel") DrawGirlStyled(c, pose, shown, x, y, px, a, t);
        else if (_style != "pixel") DrawStyled(c, pose, shown, ball, x, y, px, a, t, earTwitch);
        else if (ball) DrawBall(c, x, y, px, a);
        else
        {
            DrawWornBehind(c, pose.Worn, x, y, px, a);
            DrawHead(c, x, y, px, a, earTwitch);
            if (_sp.Whiskers) Whiskers(c, x, y, px, a);
            DrawFace(c, blink && pose.Face is CatFace.Open or CatFace.Determined or CatFace.Surprised ? CatFace.Closed : pose.Face, x, y, px, a);
            DrawWorn(c, pose.Worn, x, y, px, a, t);
            if (HatId != null && pose.Worn is not ("hardhat" or "partyhat")) DrawHat(c, HatId, x, y, px, a, t);
        }
        c.Restore();

        if (pose.Side != null) DrawSide(c, pose.Side, x + 17 * px, y, px, a, t);
        if (pose.Particles != null) Particles(c, pose.Particles, x, y, px, a, t);
        // A word bubble beside the head (above it would be cut off by the pill at the top of the screen), after any prop.
        if (pose.Bubble != null) Bubble(c, pose.Bubble, x + (17.5f + SideCells(pose.Side)) * px, y + 8.5f * px, a);
    }

    /// <summary>How many grid cells the props beside the head take (0 for none).</summary>
    private static float SideCells(string? side)
    {
        if (side == null) return 0;
        float ox = 0;
        foreach (var part in side.Split('+'))
            ox += part switch { "fan" => 6, "docs" => 8, _ => Side.TryGetValue(part, out var art) ? art.Max(r => r.Length) + 1 : 0 };
        return ox;
    }

    /// <summary>How far the whiskers reach out past the head, in grid cells.</summary>
    public const float Whisker = 2.7f;

    /// <summary>The pose's drawn width in DIPs, whiskers to the end of its prop or bubble. The cat is drawn with its
    /// head starting <see cref="Whisker"/> cells in from the left of this.</summary>
    public static float Measure(CatPose pose, float px)
    {
        float right = 16 + Whisker;                                   // head and right whiskers
        if (pose.Side != null)
        {
            float ox = 0;
            foreach (var part in pose.Side.Split('+'))
                ox += part switch { "fan" => 6, "docs" => 8, _ => Side.TryGetValue(part, out var art) ? art.Max(r => r.Length) + 1 : 0 };
            right = Math.Max(right, 17 + ox - 1);
        }
        float w = (Whisker + right) * px;
        if (pose.Bubble != null)
        {
            using var font = Fonts.Med(8.5f);
            w = Math.Max(w, (Whisker + 17.5f + SideCells(pose.Side)) * px + G.Measure(font, pose.Bubble) + 8);
        }
        return w;
    }

    private static void Cell(SKCanvas c, SKPaint p, float x, float y, float px, int col, int row)
        => c.DrawRect(x + col * px, y + row * px, px + 0.05f, px + 0.05f, p);

    private static void DrawHead(SKCanvas c, float x, float y, float px, float a, bool earTwitch)
    {
        using var fur = new SKPaint { Color = Body.WithAlpha(G.A(a)) };
        using var ear = new SKPaint { Color = _sk.Ear.WithAlpha(G.A(a)) };
        using var mark = new SKPaint { Color = _sk.Mark.WithAlpha(G.A(a)) };
        using var face = new SKPaint { Color = Skin.WithAlpha(G.A(a)) };
        using var rim = new SKPaint { Color = Rim.WithAlpha(G.A(a)) };
        // Rows past 11 are extra height above the head (a bunny's tall ears): shift up so row 0 of the face stays put.
        y -= (Head.Length - 11) * px;
        for (int row = 0; row < Head.Length; row++)
            for (int col = 0; col < 16; col++)
            {
                char ch = Head[row][col];
                if (ch == '.') continue;
                // An ear twitch: the left ear's tip drops a cell for a moment.
                if (earTwitch && row == 0 && col == Head[0].IndexOfAny(new[] { '#', 'm' })) continue;
                Cell(c, ch switch { 'e' => ear, 'm' => mark, 'f' => face, _ => fur }, x, y, px, col, row);
                if (ch is 'e' or 'f') continue;
                // A lit outline keeps a black cat visible on a black island: along the top of top-facing cells,
                // and down the outer side of side-facing ones (a strip per cell side, so the sides stay smooth).
                float t = px * 0.32f;
                bool top = row == 0 || Head[row - 1][col] == '.';
                bool left = col == 0 || Head[row][col - 1] == '.';
                bool right = col == 15 || Head[row][col + 1] == '.';
                if (top) c.DrawRect(x + col * px, y + row * px, px + 0.05f, t, rim);
                if (left) c.DrawRect(x + col * px, y + row * px, t, px + 0.05f, rim);
                if (right) c.DrawRect(x + (col + 1) * px - t, y + row * px, t, px + 0.05f, rim);
            }
    }

    private static SKColor Mix(SKColor a, SKColor b, float k) => new(
        (byte)(a.Red + (b.Red - a.Red) * k), (byte)(a.Green + (b.Green - a.Green) * k), (byte)(a.Blue + (b.Blue - a.Blue) * k));


    private static void DrawBall(SKCanvas c, float x, float y, float px, float a)
    {
        // Curled up: a round loaf with ear tips and a tail wrapped round.
        using var fur = new SKPaint { IsAntialias = true, Color = Body.WithAlpha(G.A(a)) };
        using var rim = new SKPaint { IsAntialias = true, Color = Rim.WithAlpha(G.A(a)), Style = SKPaintStyle.Stroke, StrokeWidth = px * 0.5f };
        var r = SKRect.Create(x + px, y + 4 * px, 14 * px, 7 * px);
        c.DrawRoundRect(r, 3.5f * px, 3.5f * px, fur);
        c.DrawRoundRect(r, 3.5f * px, 3.5f * px, rim);
        Cell(c, fur, x, y, px, 3, 3); Cell(c, fur, x, y, px, 4, 3); Cell(c, fur, x, y, px, 3, 2);
        Cell(c, fur, x, y, px, 7, 3); Cell(c, fur, x, y, px, 8, 3); Cell(c, fur, x, y, px, 8, 2);
        using var eye = new SKPaint { Color = Light(Body) ? FaceInk.WithAlpha(G.A(0.8f * a)) : G.Paper(0.8f * a) };
        c.DrawRect(x + 4 * px, y + 6.5f * px, 2 * px, 0.6f * px, eye);
        c.DrawRect(x + 7 * px, y + 6.5f * px, 2 * px, 0.6f * px, eye);
    }

    private static void Whiskers(SKCanvas c, float x, float y, float px, float a)
    {
        using var p = new SKPaint { IsAntialias = true, Color = G.Paper(0.55f * a), StrokeWidth = Math.Max(0.6f, px * 0.32f) };
        float wy = y + 7.5f * px;
        c.DrawLine(x - 0.2f * px, wy, x - 2.6f * px, wy - 0.6f * px, p);
        c.DrawLine(x - 0.2f * px, wy + px, x - 2.6f * px, wy + 1.4f * px, p);
        c.DrawLine(x + 16.2f * px, wy, x + 18.6f * px, wy - 0.6f * px, p);
        c.DrawLine(x + 16.2f * px, wy + px, x + 18.6f * px, wy + 1.4f * px, p);
    }

    private static void DrawFace(SKCanvas c, CatFace face, float x, float y, float px, float a)
    {
        using var w = new SKPaint { Color = G.Paper(a) };
        using var pink = new SKPaint { Color = Palette['p'].WithAlpha(G.A(a)) };
        using var k = new SKPaint { Color = new SKColor(10, 10, 12, G.A(a)) };
        // Eyes and mouth go dark on a light coat (or a light patch), so every face stays readable.
        void P(SKPaint p, params int[] xy)
        {
            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                if (p == w) w.Color = Light(Under(xy[i], xy[i + 1])) ? FaceInk.WithAlpha(G.A(a)) : G.Paper(a);
                Cell(c, p, x, y, px, xy[i], xy[i + 1]);
            }
        }
        void Smile() => P(w, 7, 8, 8, 8, 6, 7, 9, 7);
        // The girls' open eyes in pixels: a lash line, a coloured iris darker below, a highlight, blush, a small mouth.
        if (_sp.Anime && face is CatFace.Open or CatFace.Surprised)
        {
            using var lash = new SKPaint { Color = FaceInk.WithAlpha(G.A(a)) };
            using var iris = new SKPaint { Color = _sp.Iris.WithAlpha(G.A(a)) };
            using var deep = new SKPaint { Color = Mix(_sp.Iris, SKColors.Black, 0.45f).WithAlpha(G.A(a)) };
            using var shine = new SKPaint { Color = SKColors.White.WithAlpha(G.A(a)) };
            using var blush = new SKPaint { Color = new SKColor(255, 130, 160).WithAlpha(G.A(0.85f * a)) };
            using var lip = new SKPaint { Color = new SKColor(200, 80, 104).WithAlpha(G.A(a)) };
            foreach (int ex in new[] { 4, 10 })
            {
                int outer = ex == 4 ? 3 : 12;
                Cell(c, lash, x, y, px, ex, 5); Cell(c, lash, x, y, px, ex + 1, 5); Cell(c, lash, x, y, px, outer, 5);
                Cell(c, shine, x, y, px, ex, 6); Cell(c, iris, x, y, px, ex + 1, 6);
                Cell(c, iris, x, y, px, ex, 7); Cell(c, deep, x, y, px, ex + 1, 7);
            }
            Cell(c, blush, x, y, px, 3, 8); Cell(c, blush, x, y, px, 12, 8);
            if (face == CatFace.Surprised) { Cell(c, lip, x, y, px, 7, 8); Cell(c, lip, x, y, px, 8, 8); Cell(c, lip, x, y, px, 7, 9); Cell(c, lip, x, y, px, 8, 9); }
            else { Cell(c, lip, x, y, px, 7, 9); Cell(c, lip, x, y, px, 8, 9); Cell(c, lip, x, y, px, 6, 8); Cell(c, lip, x, y, px, 9, 8); }
            return;
        }
        switch (face)
        {
            case CatFace.Open:
                P(w, 4, 5, 5, 5, 4, 6, 5, 6, 10, 5, 11, 5, 10, 6, 11, 6);
                Smile();
                break;
            case CatFace.Happy:
                P(w, 3, 6, 4, 5, 5, 5, 6, 6, 9, 6, 10, 5, 11, 5, 12, 6);
                P(w, 6, 8, 7, 9, 8, 8, 9, 9, 10, 8);
                break;
            case CatFace.Closed:
                P(w, 3, 6, 4, 6, 5, 6, 10, 6, 11, 6, 12, 6);
                P(w, 7, 8, 8, 8);
                break;
            case CatFace.Determined:
                P(w, 3, 4, 4, 5, 12, 4, 11, 5, 5, 6, 10, 6);
                P(w, 7, 8, 8, 8, 6, 8, 9, 8);
                break;
            case CatFace.XEyes:
                P(w, 3, 5, 5, 5, 4, 6, 3, 7, 5, 7, 10, 5, 12, 5, 11, 6, 10, 7, 12, 7);
                P(w, 6, 9, 7, 8, 8, 8, 9, 9);
                break;
            case CatFace.Spiral:
                P(w, 3, 5, 4, 5, 5, 5, 3, 6, 5, 6, 3, 7, 4, 7, 10, 5, 11, 5, 12, 5, 10, 6, 12, 6, 10, 7, 11, 7, 12, 7);
                P(w, 6, 9, 7, 8, 8, 9, 9, 8);
                break;
            case CatFace.Surprised:
                P(w, 4, 4, 5, 4, 4, 5, 5, 5, 4, 6, 5, 6, 10, 4, 11, 4, 10, 5, 11, 5, 10, 6, 11, 6);
                P(w, 7, 8, 8, 8, 7, 9, 8, 9);
                break;
            case CatFace.Grumpy:
                P(w, 3, 4, 4, 4, 5, 5, 12, 4, 11, 4, 10, 5, 4, 6, 5, 6, 10, 6, 11, 6);
                P(w, 6, 9, 7, 8, 8, 8, 9, 9);
                break;
            case CatFace.Love:
                P(pink, 3, 5, 5, 5, 3, 6, 4, 6, 5, 6, 4, 7, 10, 5, 12, 5, 10, 6, 11, 6, 12, 6, 11, 7);
                Smile();
                break;
            case CatFace.Droopy:
                P(w, 3, 6, 4, 6, 5, 6, 10, 6, 11, 6, 12, 6, 4, 7, 11, 7);
                P(w, 7, 9, 8, 9);
                break;
            case CatFace.Wink:
                P(w, 4, 5, 5, 5, 4, 6, 5, 6, 10, 6, 11, 5, 12, 6);
                Smile();
                break;
            case CatFace.Sing:
            {
                // Eyes shut happily; the mouth opens with the voice: a small line, a round "o", a big "O".
                P(w, 3, 6, 4, 5, 5, 5, 6, 6, 9, 6, 10, 5, 11, 5, 12, 6);
                if (SingOpen < 0.3f) P(w, 7, 8, 8, 8);
                else if (SingOpen < 0.65f) { P(w, 7, 8, 8, 8, 7, 9, 8, 9); P(pink, 7, 9); }
                else { P(w, 7, 7, 8, 7, 6, 8, 9, 8, 6, 9, 9, 9, 7, 10, 8, 10); P(pink, 7, 8, 8, 8, 7, 9, 8, 9); }
                break;
            }
            case CatFace.Yawn:
                P(w, 3, 6, 4, 6, 5, 6, 10, 6, 11, 6, 12, 6);
                P(w, 7, 7, 8, 7, 6, 8, 9, 8, 6, 9, 9, 9, 7, 10, 8, 10);
                P(pink, 7, 8, 8, 8, 7, 9, 8, 9);
                break;
            case CatFace.Shades:
                P(k, 3, 5, 4, 5, 5, 5, 6, 5, 3, 6, 4, 6, 5, 6, 6, 6, 9, 5, 10, 5, 11, 5, 12, 5, 9, 6, 10, 6, 11, 6, 12, 6, 7, 5, 8, 5);
                P(w, 4, 5, 10, 5);
                Smile();
                break;
        }
    }

    // ---------- worn things ----------

    /// <summary>Parts drawn behind the head (a headphone band, a hat's back).</summary>
    private static void DrawWornBehind(SKCanvas c, string? worn, float x, float y, float px, float a)
    {
        if (worn is not ("headphones" or "headset")) return;
        using var band = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f * px, Color = Palette['g'].WithAlpha(G.A(a)) };
        c.DrawArc(SKRect.Create(x + 0.5f * px, y - 2 * px, 15 * px, 12 * px), 180, 180, false, band);
    }

    /// <summary>The unlockable hats, in pixels, sitting on the head (row 0 is the top of the head).</summary>
    private static void DrawHat(SKCanvas c, string hat, float x, float y, float px, float a, double t)
    {
        void Art(int col, int row, params string[] rows)
        {
            using var p = new SKPaint();
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r].Length; i++)
                {
                    char ch = rows[r][i];
                    if (ch == '.') continue;
                    p.Color = Palette[ch].WithAlpha(G.A(a));
                    Cell(c, p, x, y, px, col + i, row + r);
                }
        }
        switch (hat)
        {
            case "flower": Art(11, -2, ".p.", "pyp", ".p.", ".G."); break;
            case "beanie": Art(3, -4, "....ww....", "...rrrr...", "..rrrrrr..", ".rrrrrrrr.", "wwwwwwwwww"); break;
            case "bow": Art(4, -3, "pp....pp", "pppyyppp", "pp....pp"); break;
            case "crown": Art(4, -4, "y..y..y", "yy.y.yy", "yyyyyyy", "yrybyry"); break;
            case "wizard": Art(3, -7, ".....v....", "....vv....", "....vvv...", "...vvyvv..", "...vvvvv..", "..vvvvvvv.", "vvvvvvvvvv"); break;
            case "halo":
            {
                float bob = MathF.Sin((float)t * 2) * 0.4f * px;
                using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.9f * px, Color = Palette['y'].WithAlpha(G.A(a)) };
                c.DrawOval(SKRect.Create(x + 3.5f * px, y - 3.6f * px + bob, 9 * px, 2.2f * px), ring);
                break;
            }
        }
    }

    private static void DrawWorn(SKCanvas c, string? worn, float x, float y, float px, float a, double t)
    {
        if (worn == null) return;
        SKPaint Pal(char ch, float k = 1) => new() { IsAntialias = false, Color = Palette[ch].WithAlpha(G.A(a * k)) };
        void Rows(SKPaint p, int col, int row, params string[] rows)
        {
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r].Length; i++)
                    if (rows[r][i] != '.') Cell(c, p, x, y, px, col + i, row + r);
        }
        switch (worn)
        {
            case "headphones":
            case "headset":
            case "neckphones":
            {
                using var cup = Pal('d');
                using var pad = Pal(worn == "headset" ? 'c' : 'v');
                int r0 = worn == "neckphones" ? 9 : 4;
                Rows(cup, -1, r0, "##", "##", "##", "##");
                Rows(cup, 15, r0, "##", "##", "##", "##");
                Rows(pad, -1, r0 + 1, "#.", "#.");
                Rows(pad, 16, r0 + 1, ".#", ".#");
                if (worn == "headset")
                {
                    using var boom = Pal('s');
                    Rows(boom, 2, 8, "#....", ".###.", "....#");
                }
                break;
            }
            case "hardhat":
            {
                using var hat = Pal('y');
                using var dark = Pal('o');
                Rows(hat, 3, -3, "...######...", ".##########.", "############");
                Rows(dark, 2, 0, "##############");
                break;
            }
            case "glasses":
            {
                using var f = Pal('w', 0.9f);
                Rows(f, 3, 4, "####..####", "#..####..#", "####..####");
                break;
            }
            case "partyhat":
            {
                using var hat = Pal('p');
                using var dot = Pal('y');
                Rows(hat, 6, -5, "..#.", ".##.", ".###", "####");
                Rows(dot, 7, -6, "#");
                break;
            }
            case "tie":
            {
                using var tie = Pal('r');
                Rows(tie, 7, 10, "##", ".#", "##");
                break;
            }
            case "pawmouth":
            {
                using var paw = new SKPaint { Color = Rim.WithAlpha(G.A(a)) };
                Rows(paw, 6, 7, ".##.", "####", "####");
                break;
            }
            case "pawears":
            {
                using var paw = new SKPaint { Color = Rim.WithAlpha(G.A(a)) };
                Rows(paw, -1, 1, "##", "###", "##");
                Rows(paw, 14, 1, ".##", "###", ".##");
                break;
            }
            case "pawsup":
            {
                using var paw = new SKPaint { Color = Rim.WithAlpha(G.A(a)) };
                float lift = MathF.Abs(MathF.Sin((float)t * 4));
                c.Save(); c.Translate(0, -lift * px);
                Rows(paw, 2, 10, "###", "###");
                Rows(paw, 11, 10, "###", "###");
                c.Restore();
                break;
            }
        }
    }

    // ---------- props beside, particles, bubble ----------

    private static void DrawSide(SKCanvas c, string id, float x, float y, float px, float a, double t)
    {
        var parts = id.Split('+');
        float ox = 0;
        foreach (var part in parts)
        {
            if (!Side.TryGetValue(part, out var art)) continue;
            int h = art.Length, w = art.Max(r => r.Length);
            // Each prop sits on the cat's chin line, with a small idle sway.
            float sway = MathF.Sin((float)t * 1.6f + ox) * 0.4f * px;
            float top = y + (11 - h) * px + sway;
            if (part == "fan") { DrawFan(c, x + ox * px, y + 3 * px, px, a, t); ox += 6; continue; }
            if (part == "docs") { DrawJuggle(c, x + ox * px, y, px, a, t); ox += 8; continue; }
            if (part == "paw" ) top -= MathF.Abs(MathF.Sin((float)t * 6)) * 2 * px;
            using var p = new SKPaint();
            for (int r = 0; r < h; r++)
                for (int i = 0; i < art[r].Length; i++)
                {
                    char ch = art[r][i];
                    if (ch == '.') continue;
                    p.Color = ch == '#' ? Rim.WithAlpha(G.A(a)) : Palette[ch].WithAlpha(G.A(a));
                    c.DrawRect(x + (ox + i) * px, top + r * px, px + 0.05f, px + 0.05f, p);
                }
            ox += w + 1;
        }
    }

    private static void DrawFan(SKCanvas c, float x, float y, float px, float a, double t)
    {
        using var p = new SKPaint { IsAntialias = true, Color = Palette['s'].WithAlpha(G.A(a)) };
        float cx = x + 2.5f * px, cy = y + 3.5f * px;
        for (int i = 0; i < 3; i++)
        {
            c.Save();
            c.Translate(cx, cy);
            c.RotateDegrees((float)(t * 720 % 360) + i * 120);
            c.DrawRect(-0.6f * px, -3 * px, 1.2f * px, 3 * px, p);
            c.Restore();
        }
        c.DrawRect(cx - 0.5f * px, cy, px, 4 * px, p);
    }

    private static void DrawJuggle(SKCanvas c, float x, float y, float px, float a, double t)
    {
        using var paper = new SKPaint { Color = Palette['w'].WithAlpha(G.A(a)) };
        using var line = new SKPaint { Color = Palette['d'].WithAlpha(G.A(a)) };
        for (int i = 0; i < 3; i++)
        {
            float ph = (float)t * 3 + i * 2.1f;
            float dx = 3 + MathF.Cos(ph) * 3, dy = 5 - MathF.Abs(MathF.Sin(ph)) * 6;
            c.DrawRect(x + dx * px, y + dy * px, 3 * px, 4 * px, paper);
            c.DrawRect(x + (dx + 0.5f) * px, y + (dy + 1) * px, 2 * px, 0.5f * px, line);
        }
    }

    private static void Particles(SKCanvas c, string kind, float x, float y, float px, float a, double t)
    {
        using var p = new SKPaint();
        for (int i = 0; i < 3; i++)
        {
            float life = (float)((t * 0.5 + i / 3.0) % 1.0);
            float fade = MathF.Sin(life * MathF.PI) * a;
            float fx = x + (12 + i * 2.5f) * px + MathF.Sin((float)t * 2 + i) * px, fy = y + (2 - life * 6) * px;
            switch (kind)
            {
                case "hearts":
                    p.Color = Palette['p'].WithAlpha(G.A(fade));
                    foreach (var (cx, cy) in new[] { (0, 0), (2, 0), (0, 1), (1, 1), (2, 1), (1, 2) })
                        c.DrawRect(fx + cx * px * 0.7f, fy + cy * px * 0.7f, px * 0.75f, px * 0.75f, p);
                    break;
                case "zzz":
                {
                    using var font = new SKFont(Fonts.MonoMedium, (5 + i * 1.5f) * px * 0.55f + 5);
                    G.Text(c, "z", fx, fy + 3 * px, font, G.Paper(fade));
                    break;
                }
                case "notes":
                {
                    // A pixel quaver: a 2x2 head, a stem, and a flag (the font has no music symbols).
                    p.Color = Palette['v'].WithAlpha(G.A(fade));
                    float q = px * 0.7f;
                    foreach (var (nx, ny) in new[] { (0, 3), (1, 3), (0, 4), (1, 4), (1, 0), (1, 1), (1, 2), (2, 0), (3, 1) })
                        c.DrawRect(fx + nx * q, fy + ny * q, q + 0.05f, q + 0.05f, p);
                    break;
                }
                case "coins":
                {
                    // Little gold coins floating up, turning as they go.
                    float turn = MathF.Abs(MathF.Cos((float)t * 4 + i));
                    p.Color = Palette['o'].WithAlpha(G.A(fade));
                    c.DrawOval(SKRect.Create(fx - px * 0.95f * turn, fy - px * 0.95f, px * 1.9f * Math.Max(0.2f, turn), px * 1.9f), p);
                    p.Color = Palette['y'].WithAlpha(G.A(fade));
                    c.DrawOval(SKRect.Create(fx - px * 0.65f * turn, fy - px * 0.65f, px * 1.3f * Math.Max(0.15f, turn), px * 1.3f), p);
                    break;
                }
                case "sparkles":
                    p.Color = Palette[i % 2 == 0 ? 'y' : 'w'].WithAlpha(G.A(fade));
                    c.DrawRect(fx, fy, px * 0.6f, px * 1.8f, p);
                    c.DrawRect(fx - px * 0.6f, fy + px * 0.6f, px * 1.8f, px * 0.6f, p);
                    break;
                case "sweat":
                    p.Color = Palette['c'].WithAlpha(G.A(fade));
                    c.DrawRect(x - (1 + i) * px * 0.8f, y + (3 + life * 5) * px, px * 0.8f, px * 1.2f, p);
                    break;
                case "flash":
                    if (i == 0)
                    {
                        float k = (float)Math.Max(0, 1 - (t % 2.0) * 2);
                        p.Color = G.White(0.85f * k * a);
                        c.DrawRect(x - 4 * px, y - 2 * px, 40 * px, 16 * px, p);
                    }
                    break;
            }
        }
    }

    private static void Bubble(SKCanvas c, string text, float x, float y, float a)
    {
        using var font = Fonts.Med(8.5f);
        float w = G.Measure(font, text) + 8, h = 12;
        var r = SKRect.Create(x, y - h + 4, w, h);
        G.Fill(c, r, 4, G.Paper(0.95f * a));
        using var tail = new SKPaint { Color = G.Paper(0.95f * a) };
        c.DrawRect(r.Left + 2, r.Bottom - 0.5f, 2.5f, 2.5f, tail);
        G.Text(c, text, r.Left + 4, r.Bottom - 3.2f, font, G.Ink.WithAlpha(G.A(a)));
    }
}
