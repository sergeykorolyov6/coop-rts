namespace Game.Sim;

// One row of hero_levels.csv (factions.md, Yellow Star): experience needed and the hero's bonuses.
public sealed class HeroLevelStats
{
    internal HeroLevelStats(JsonNode row)
    {
        Level = row.GetInt("level");
        Xp = row.GetInt("xp");
        HpPercent = row.GetInt("hp_pct");
        DamagePercent = row.GetInt("damage_pct");
    }

    public int Level { get; }

    // Total experience to reach this level.
    public int Xp { get; }

    // Bonus to the hero's max HP and damage, percent.
    public int HpPercent { get; }
    public int DamagePercent { get; }
}
