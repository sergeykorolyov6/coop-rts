using System;
using System.Collections.Generic;

namespace Game.Sim;

// One row of waves.csv: a Legion wave for one player (legion-ai.md).
public sealed class WaveStats
{
    public const string TargetEconomy = "economy";
    public const string TargetBase = "base";

    private readonly List<WaveUnit> _units = new List<WaveUnit>();

    internal WaveStats(JsonNode row)
    {
        Wave = row.GetInt("wave");
        TimeTicks = row.GetInt("time_ticks");
        Target = row.GetString("target");
        if (Target != TargetEconomy && Target != TargetBase)
            throw new FormatException($"wave {Wave}: target \"{Target}\" must be \"{TargetEconomy}\" or \"{TargetBase}\".");
        foreach (JsonNode item in row.Get("units").Items)
            _units.Add(new WaveUnit(item.GetString("id"), item.GetInt("count")));
    }

    public int Wave { get; }
    public int TimeTicks { get; }
    public string Target { get; }

    // In table order: the order the Legion produces them in.
    public IReadOnlyList<WaveUnit> Units => _units;
}
