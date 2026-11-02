using System;
using System.Collections.Generic;

namespace Game.Sim;

// Single numbers from rules.csv that belong to no unit or building row:
// drill rate, conveyor speed, start conditions... Already in ticks and milli-cells.
public sealed class GameRules
{
    private static readonly string[] Keys =
    {
        "start_primary", "start_secondary", "start_workers", "construction_start_hp_pct", "repair_cost_pct",
        "interact_range_mc", "drill_rate_per_ore_cell_ticks_per_unit", "drill_buffer", "manual_cargo",
        "manual_mining_rate_ticks_per_unit", "conveyor_item_speed_mc_per_tick", "conveyor_item_spacing_mc",
        "bridge_range", "storage_capacity",
    };

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

    internal static string ZeroJson()
    {
        var sb = new System.Text.StringBuilder("{");
        for (int i = 0; i < Keys.Length; i++)
            sb.Append(i == 0 ? "" : ",").Append('"').Append(Keys[i]).Append("\":0");
        return sb.Append('}').ToString();
    }

    private int Read(JsonNode root, string key)
    {
        if (System.Array.IndexOf(Keys, key) < 0)
            throw new System.InvalidOperationException($"Rule \"{key}\" is missing from GameRules.Keys.");
        _read.Add(key);
        int value = root.GetInt(key);
        if (value < 0)
            throw new FormatException($"rules.json: \"{key}\" is {value}, must not be negative.");
        return value;
    }
}
