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
            string other => throw new FormatException($"Unknown ability kind \"{other}\"."),
        };
        CooldownTicks = row.GetInt("cooldown_ticks");
        RangeMilli = row.GetInt("range_mc");
        DurationTicks = row.GetInt("duration_ticks");
        MoveSpeedPercent = row.GetInt("move_speed_pct");
        AttackSpeedPercent = row.GetInt("attack_speed_pct");
        HpCostPercent = row.GetInt("hp_cost_pct");
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
}
