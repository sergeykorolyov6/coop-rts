using System;
using System.Collections.Generic;

namespace Game.Sim;

// Single numbers from rules.csv that belong to no unit or building row:
// drill rate, conveyor speed, start conditions... Already in ticks and milli-cells.
public sealed class GameRules
{
    private readonly List<string> _read = new List<string>();

    internal GameRules(JsonNode root)
    {
        if (root.Kind != JsonKind.Object)
            throw new FormatException("rules.json: expected an object.");

        StartPrimary = Read(root, "start_primary");
        StartSecondary = Read(root, "start_secondary");
        StartWorkers = Read(root, "start_workers");
        ConstructionStartHpPercent = Read(root, "construction_start_hp_pct");
        RepairCostPercent = Read(root, "repair_cost_pct");
        InteractRangeMilli = Read(root, "interact_range_mc");
        DrillTicksPerUnitPerOreCell = Read(root, "drill_rate_per_ore_cell_ticks_per_unit");
        DrillBuffer = Read(root, "drill_buffer");
        ManualCargo = Read(root, "manual_cargo");
        ManualMiningTicksPerUnit = Read(root, "manual_mining_rate_ticks_per_unit");
        ConveyorItemSpeedMilliPerTick = Read(root, "conveyor_item_speed_mc_per_tick");
        ConveyorItemSpacingMilli = Read(root, "conveyor_item_spacing_mc");
        BridgeRange = Read(root, "bridge_range");
        StorageCapacity = Read(root, "storage_capacity");

        // An unknown key is a typo or a rule the sim does not implement yet; both should be loud.
        foreach (KeyValuePair<string, JsonNode> field in root.Fields)
        {
            if (!_read.Contains(field.Key))
                throw new FormatException($"rules.json: unknown rule \"{field.Key}\".");
        }
    }

    public int StartPrimary { get; }
    public int StartSecondary { get; }
    public int StartWorkers { get; }
    public int ConstructionStartHpPercent { get; }
    public int RepairCostPercent { get; }
    public int InteractRangeMilli { get; }

    // Accumulator threshold: each ore cell under a drill adds 1 per tick (drill.md, appendix).
    public int DrillTicksPerUnitPerOreCell { get; }

    public int DrillBuffer { get; }
    public int ManualCargo { get; }
    public int ManualMiningTicksPerUnit { get; }
    public int ConveyorItemSpeedMilliPerTick { get; }
    public int ConveyorItemSpacingMilli { get; }

    // Cells between bridge ends along a straight line.
    public int BridgeRange { get; }

    public int StorageCapacity { get; }

    private int Read(JsonNode root, string key)
    {
        _read.Add(key);
        int value = root.GetInt(key);
        if (value < 0)
            throw new FormatException($"rules.json: \"{key}\" is {value}, must not be negative.");
        return value;
    }
}
