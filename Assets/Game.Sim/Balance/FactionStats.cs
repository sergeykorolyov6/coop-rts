using System;
using System.Collections.Generic;

namespace Game.Sim;

// One row of factions.csv: what a player of the faction starts with and may build.
public sealed class FactionStats
{
    private readonly List<string> _buildings;

    internal FactionStats(JsonNode row)
    {
        Id = row.GetString("id");
        NameRu = row.GetString("name_ru");
        Role = ParseRole(row.GetString("role"));
        BuilderUnitId = row.GetString("builder");
        HeadquartersId = row.GetString("headquarters");
        _buildings = row.GetStringList("buildings");
        SingleHeadquarters = row.GetInt("single_headquarters") != 0;
        BuilderRespawnTicks = row.GetInt("builder_respawn_ticks");
        UnitRegenPercentPerSecond = row.GetInt("unit_regen_pct_per_s");
        BuildingRegenPercentPerSecond = row.GetInt("building_regen_pct_per_s");
        RegenDelayTicks = row.GetInt("regen_delay_ticks");
        FirstStrikePercent = row.GetInt("first_strike_pct");
        FirstStrikeDelayTicks = row.GetInt("first_strike_delay_ticks");
        OnKillAbilityId = row.GetString("on_kill_ability");
        BountyPercent = row.GetInt("bounty_pct");
    }

    public string Id { get; }
    public string NameRu { get; }
    public FactionRole Role { get; }

    // builder.md: the unit the player starts with and gets back after its death; empty for none.
    public string BuilderUnitId { get; }

    // Placed at the player's start on the map; empty when the faction has no start.
    public string HeadquartersId { get; }

    // What the faction may build, in the order of the build menu.
    public IReadOnlyList<string> Buildings => _buildings;

    // construction.md (answer 6): a human player has one headquarters at a time, the Legion several.
    public bool SingleHeadquarters { get; }

    // builder.md: how long the dead builder takes to come back.
    public int BuilderRespawnTicks { get; }

    // factions.md: units and finished buildings not hit for RegenDelayTicks heal this many
    // percent of max HP per second (Red Lightning's units, Blue Walls' buildings).
    public int UnitRegenPercentPerSecond { get; }
    public int BuildingRegenPercentPerSecond { get; }
    public int RegenDelayTicks { get; }

    // factions.md: a unit's blow after FirstStrikeDelayTicks without attacking deals this
    // percent of its damage (100 — no bonus).
    public int FirstStrikePercent { get; }
    public int FirstStrikeDelayTicks { get; }

    // factions.md: the boost a unit of the faction gets for a kill (the Legion's frenzy); empty for none.
    public string OnKillAbilityId { get; }

    // factions.md: percent of a destroyed enemy building's price the faction's player gets.
    public int BountyPercent { get; }

    public bool CanBuild(string buildingId) => _buildings.Contains(buildingId);

    private static FactionRole ParseRole(string text)
    {
        switch (text)
        {
            case "player": return FactionRole.Player;
            case "legion": return FactionRole.Legion;
            case "creeps": return FactionRole.Creeps;
            case "neutral": return FactionRole.Neutral;
            default: throw new FormatException($"Unknown faction role \"{text}\".");
        }
    }
}
