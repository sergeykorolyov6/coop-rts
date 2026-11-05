using System.Collections.Generic;

namespace Game.Sim;

// One row of hero_paths.csv (factions.md): a branch the player picks for the hero once a mission —
// its abilities and how the hero grows with each level beyond the common growth.
public sealed class HeroPathStats
{
    private readonly List<string> _abilities;

    internal HeroPathStats(JsonNode row)
    {
        Id = row.GetString("id");
        NameRu = row.GetString("name_ru");
        HeroUnitId = row.GetString("hero");
        _abilities = row.GetStringList("abilities");
        LevelHpPercent = row.GetInt("level_hp_pct");
        LevelDamagePercent = row.GetInt("level_damage_pct");
        AuraDamagePercent = row.GetInt("aura_damage_pct");
        Description = row.GetString("description");
    }

    public string Id { get; }
    public string NameRu { get; }

    // The hero unit the path is for.
    public string HeroUnitId { get; }

    // Ability ids of abilities.csv, in button order.
    public IReadOnlyList<string> Abilities => _abilities;
    public bool HasAbility(string abilityId) => _abilities.Contains(abilityId);

    // Per level above the first, on top of hero_levels.csv.
    public int LevelHpPercent { get; }
    public int LevelDamagePercent { get; }

    // Added to the faction's aura.
    public int AuraDamagePercent { get; }

    // For the UI.
    public string Description { get; }
}
