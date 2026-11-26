using SkiaSharp;

namespace Cacad;

/// <summary>A little game layer on the companion. Finishing focus sessions, timers and to-dos feeds it (XP and
/// happiness), and petting it (resting the pointer on it in the card) cheers it up. XP raises its level, and each
/// level unlocks a hat. Happiness slowly drops when it is not fed; a hungry companion looks it now and then.</summary>
public sealed class Pet
{
    public sealed record Hat(string Id, string Name, int Level);

    public static readonly Hat[] Hats =
    {
        new("", "None", 1),
        new("flower", "Flower", 2),
        new("beanie", "Beanie", 3),
        new("bow", "Big bow", 4),
        new("crown", "Crown", 5),
        new("halo", "Halo", 7),
        new("wizard", "Wizard hat", 9),
    };

    private readonly IslandWindow _w;
    private SettingsStore S => _w.Settings;

    public Pet(IslandWindow w) => _w = w;

    public int Xp => S.PetXp;
    public static int LevelFor(int xp) { int l = 1; while (xp >= Need(l + 1)) l++; return l; }
    /// <summary>Total XP to reach level n: 0, 30, 90, 180, 300…</summary>
    public static int Need(int level) => 15 * level * (level - 1);
    public int Level => LevelFor(S.PetXp);

    /// <summary>0..100, falling 3 points an hour since it was last fed.</summary>
    public double Happiness => Math.Clamp(S.PetHappy - (DateTime.Now - S.PetFedAt).TotalHours * 3, 0, 100);

    public bool Hungry => Happiness < 25;

    /// <summary>Feed it: a finished focus session (big), a timer or to-do (small), petting (tiny, a few a day).</summary>
    public void Feed(string why)
    {
        int xp; double happy;
        switch (why)
        {
            case "focus": xp = 12; happy = 30; break;
            case "todo": xp = 4; happy = 10; break;
            case "timer": xp = 2; happy = 5; break;
            case "pet":
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (S.PetDay != today) { S.PetDay = today; S.PetPetsToday = 0; }
                if (S.PetPetsToday >= 6) return;
                S.PetPetsToday++;
                xp = 1; happy = 4; break;
            default: return;
        }
        int before = Level;
        S.PetHappy = Math.Min(100, Happiness + happy);
        S.PetFedAt = DateTime.Now;
        S.PetXp += xp;
        S.Save();
        int after = Level;
        if (after > before)
        {
            var unlocked = Hats.FirstOrDefault(h => h.Level == after && h.Id.Length > 0);
            string name = CatArt.Species.FirstOrDefault(s => s.Id == S.CompanionSpecies)?.Name ?? "Your companion";
            _w.Toast("heart", unlocked != null ? $"{name} reached level {after}! Unlocked: {unlocked.Name}" : $"{name} reached level {after}!",
                new SKColor(255, 120, 170), null, 5000);
            _w.Companion?.Pulse("flex");
        }
    }

    /// <summary>The hat worn (only if unlocked).</summary>
    public string? HatId => Hats.FirstOrDefault(h => h.Id == S.PetHat && h.Id.Length > 0 && h.Level <= Level)?.Id;
}
