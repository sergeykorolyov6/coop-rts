using System.Collections.Generic;

namespace Game.Sim;

// One row of buildings.csv after Tools/csv2json.py.
public sealed class BuildingStats
{
    private readonly List<string> _produces;

    internal BuildingStats(JsonNode row)
    {
        Id = row.GetString("id");
        NameRu = row.GetString("name_ru");
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
        Behavior = ParseBehavior(row.GetString("behavior"));
        Supply = row.GetString("supply");
        Invulnerable = row.GetInt("invulnerable") != 0;
        LinePlacement = row.GetString("placement") == "line";
    }

    public string Id { get; }

    // For the UI; the sim never reads it.
    public string NameRu { get; }
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

    public BuildingBehavior Behavior { get; }

    // Units walk over belts: conveyors and bridge ends (conveyors.md, questions 27, 43).
    public bool IsBelt => Behavior == BuildingBehavior.Conveyor || Behavior == BuildingBehavior.Bridge;

    // Unit ids this building can produce.
    public IReadOnlyList<string> Produces => _produces;

    // factory.md M2: "conveyor" — production is paid from the building's own stock that belts
    // fill; empty — from the owner's wallet (the Legion's factory).
    public string Supply { get; }
    public bool SuppliedByConveyor => Supply == SupplyConveyor;
    public const string SupplyConveyor = "conveyor";

    // neutral-buildings.md: points of interest nobody can damage.
    public bool Invulnerable { get; }

    // For the UI: placed by dragging a line (conveyors, walls).
    public bool LinePlacement { get; }

    private static BuildingBehavior ParseBehavior(string text)
    {
        switch (text)
        {
            case "": return BuildingBehavior.None;
            case "headquarters": return BuildingBehavior.Headquarters;
            case "drill": return BuildingBehavior.Drill;
            case "conveyor": return BuildingBehavior.Conveyor;
            case "bridge": return BuildingBehavior.Bridge;
            case "storage": return BuildingBehavior.Storage;
            case "lighthouse": return BuildingBehavior.Lighthouse;
            default: throw new System.FormatException($"Unknown building behavior \"{text}\".");
        }
    }
}
