using System;

namespace Game.Sim;

// One row of abilities.csv (factions.md): an active ability, or a buff a faction trait applies.
public sealed class AbilityStats
{
    internal AbilityStats(JsonNode row)
    {
        Id = row.GetString("id");
        NameRu = row.GetString("name_ru");
        Kind = row.GetString("kind") switch
        {
            "blink" => AbilityKind.Blink,
            "boost" => AbilityKind.Boost,
            "damage_area" => AbilityKind.DamageArea,
            "boost_area" => AbilityKind.BoostArea,
            "heal_area" => AbilityKind.HealArea,
            "strike" => AbilityKind.Strike,
            string other => throw new FormatException($"Unknown ability kind \"{other}\"."),
        };
        AtPoint = row.GetString("target") switch
        {
            "self" => false,
            "point" => true,
            string other => throw new FormatException($"Unknown ability target \"{other}\"."),
        };
        CooldownTicks = row.GetInt("cooldown_ticks");
        RangeMilli = row.GetInt("range_mc");
        RadiusMilli = row.GetInt("radius_mc");
        DurationTicks = row.GetInt("duration_ticks");
        MoveSpeedPercent = row.GetInt("move_speed_pct");
        AttackSpeedPercent = row.GetInt("attack_speed_pct");
        HpCostPercent = row.GetInt("hp_cost_pct");
        Damage = row.GetInt("damage");
        Heal = row.GetInt("heal");
        MinLevel = row.GetInt("min_level");
        Effect = row.GetString("effect");
        Description = row.GetString("description");
        if (Kind == AbilityKind.Blink && !AtPoint)
            throw new FormatException($"Ability \"{Id}\": blink needs target point.");
    }

    public string Id { get; }
    public string NameRu { get; }
    public AbilityKind Kind { get; }

    // Aimed at a point (within RangeMilli of the user) rather than centred on the user.
    public bool AtPoint { get; }

    public int CooldownTicks { get; }

    // How far the point may be (the blink's farthest jump); a farther point is pulled in.
    public int RangeMilli { get; }

    // Area abilities: how far from the centre they reach; Strike: how near the point the target must be.
    public int RadiusMilli { get; }

    // Boosts: how long, and how much faster the unit moves and attacks (+50 = one and a half times).
    public int DurationTicks { get; }
    public int MoveSpeedPercent { get; }
    public int AttackSpeedPercent { get; }

    // Percent of max HP paid on use.
    public int HpCostPercent { get; }

    // Damage to each enemy hit (before armor), HP restored to each unit healed.
    public int Damage { get; }
    public int Heal { get; }

    // The hero's level the ability opens at (factions.md); 1 for units without levels.
    public int MinLevel { get; }

    // For the UI: the visual effect, and what the ability does in words.
    public string Effect { get; }
    public string Description { get; }
}
