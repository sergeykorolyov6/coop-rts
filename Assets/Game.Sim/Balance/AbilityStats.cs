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
            "nova" => AbilityKind.Nova,
            "inspire" => AbilityKind.Inspire,
            string other => throw new FormatException($"Unknown ability kind \"{other}\"."),
        };
        CooldownTicks = row.GetInt("cooldown_ticks");
        RangeMilli = row.GetInt("range_mc");
        DurationTicks = row.GetInt("duration_ticks");
        MoveSpeedPercent = row.GetInt("move_speed_pct");
        AttackSpeedPercent = row.GetInt("attack_speed_pct");
        HpCostPercent = row.GetInt("hp_cost_pct");
        Damage = row.GetInt("damage");
        MinLevel = row.GetInt("min_level");
    }

    public string Id { get; }
    public string NameRu { get; }
    public AbilityKind Kind { get; }
    public int CooldownTicks { get; }

    // Blink: the farthest jump.
    public int RangeMilli { get; }

    // Boost: how long, and how much faster it moves and attacks (+50 = one and a half times).
    public int DurationTicks { get; }
    public int MoveSpeedPercent { get; }
    public int AttackSpeedPercent { get; }

    // Percent of max HP paid on use.
    public int HpCostPercent { get; }

    // Nova: damage to each enemy in range, before armor.
    public int Damage { get; }

    // The hero's level the ability opens at (factions.md); 1 for units without levels.
    public int MinLevel { get; }
}
