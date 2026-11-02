using System.Collections.Generic;

namespace Game.Sim;

// One row of buildings.csv after Tools/csv2json.py.
public sealed class BuildingStats
{
    private readonly List<string> _produces;

    internal BuildingStats(JsonNode row)
    {
        Id = row.GetString("id");
        Faction = row.GetString("faction");
        Hp = row.GetInt("hp");
        Armor = row.GetInt("armor");
        SizeW = row.GetInt("size_w");
        SizeH = row.GetInt("size_h");
        CostPrimary = row.GetInt("cost_primary");
        CostSecondary = row.GetInt("cost_secondary");
        BuildTimeTicks = row.GetInt("build_time_ticks");
        MaxBuilders = row.GetInt("max_builders");
        VisionMilli = row.GetInt("vision_mc");
        Damage = row.GetInt("damage");
        AttackRangeMilli = row.GetInt("attack_range_mc");
        AttackCooldownTicks = row.GetInt("attack_cooldown_ticks");
        _produces = row.GetStringList("produces");
    }

    public string Id { get; }
    public string Faction { get; }
    public int Hp { get; }
    public int Armor { get; }

    // Footprint in whole cells: buildings snap to the grid.
    public int SizeW { get; }
    public int SizeH { get; }

    public int CostPrimary { get; }
    public int CostSecondary { get; }

    // Build work with one builder; each builder adds 1 per tick (drill.md, appendix).
    public int BuildTimeTicks { get; }

    public int MaxBuilders { get; }
    public int VisionMilli { get; }
    public int Damage { get; }
    public int AttackRangeMilli { get; }
    public int AttackCooldownTicks { get; }

    // Unit ids this building can produce.
    public IReadOnlyList<string> Produces => _produces;
}
