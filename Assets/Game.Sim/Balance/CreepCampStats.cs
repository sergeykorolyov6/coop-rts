using System.Collections.Generic;

namespace Game.Sim;

// One row of creep_camps.csv after Tools/csv2json.py (creep-camp.md).
public sealed class CreepCampStats
{
    private readonly List<string> _creeps;

    internal CreepCampStats(JsonNode row)
    {
        Id = row.GetString("id");
        _creeps = row.GetStringList("creeps");
        AggroRadiusMilli = row.GetInt("aggro_radius_mc");
        LeashRadiusMilli = row.GetInt("leash_radius_mc");
        RegenPercentPerSecond = row.GetInt("regen_pct_per_s");
        CalmDelayTicks = row.GetInt("calm_delay_ticks");
        TrophyPrimary = row.GetInt("trophy_primary");
        TrophySecondary = row.GetInt("trophy_secondary");
        TrophyPerTrip = row.GetInt("trophy_per_trip");
    }

    public string Id { get; }

    // Unit ids, one entry per creep; repeats mean several creeps of a kind.
    public IReadOnlyList<string> Creeps => _creeps;

    public int AggroRadiusMilli { get; }
    public int LeashRadiusMilli { get; }

    // Whole percent of max HP per second; the sim turns it into an exact accumulator.
    public int RegenPercentPerSecond { get; }

    public int CalmDelayTicks { get; }
    public int TrophyPrimary { get; }
    public int TrophySecondary { get; }
    public int TrophyPerTrip { get; }
}
