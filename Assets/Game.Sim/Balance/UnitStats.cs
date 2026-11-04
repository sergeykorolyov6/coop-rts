using System.Collections.Generic;

namespace Game.Sim;

// One row of units.csv after Tools/csv2json.py: ticks and milli-cells only.
public sealed class UnitStats
{
    private readonly List<string> _tags;

    internal UnitStats(JsonNode row)
    {
        Id = row.GetString("id");
        NameRu = row.GetString("name_ru");
        Faction = row.GetString("faction");
        Hp = row.GetInt("hp");
        Armor = row.GetInt("armor");
        Damage = row.GetInt("damage");
        DamageVsBuildings = row.GetInt("damage_vs_buildings");
        AttackRangeMilli = row.GetInt("attack_range_mc");
        AttackCooldownTicks = row.GetInt("attack_cooldown_ticks");
        MoveSpeedMilliPerTick = row.GetInt("move_speed_mc_per_tick");
        VisionMilli = row.GetInt("vision_mc");
        CostPrimary = row.GetInt("cost_primary");
        CostSecondary = row.GetInt("cost_secondary");
        BuildTimeTicks = row.GetInt("build_time_ticks");
        ProducedAt = row.GetString("produced_at");
        SizeMilli = row.GetInt("size_mc");
        _tags = row.GetStringList("tags");
    }

    public string Id { get; }

    // For the UI; the sim never reads it.
    public string NameRu { get; }
    public string Faction { get; }
    public int Hp { get; }
    public int Armor { get; }
    public int Damage { get; }
    public int DamageVsBuildings { get; }
    public int AttackRangeMilli { get; }
    public int AttackCooldownTicks { get; }
    public int MoveSpeedMilliPerTick { get; }
    public int VisionMilli { get; }
    public int CostPrimary { get; }
    public int CostSecondary { get; }
    public int BuildTimeTicks { get; }

    // Building id, or empty for units nobody produces (creeps).
    public string ProducedAt { get; }

    public int SizeMilli { get; }
    public IReadOnlyList<string> Tags => _tags;

    public bool HasTag(string tag) => _tags.Contains(tag);
}
