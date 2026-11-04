using System;

namespace Game.Sim;

// One row of missions.csv (mission.md): which map, how many players, whether the Legion plays, the goal.
public sealed class MissionStats
{
    internal MissionStats(JsonNode row)
    {
        Id = row.GetString("id");
        NameRu = row.GetString("name_ru");
        Map = row.GetString("map");
        Players = row.GetInt("players");
        HasLegion = row.GetInt("legion") != 0;
        Goal = ParseGoal(row.GetString("goal"));
        Factions = row.GetStringList("factions");
        Colors = row.GetStringList("colors");
        if (Players < 1)
            throw new FormatException($"Mission \"{Id}\" needs at least one player.");
        if (Goal == MissionGoal.DestroyLegion && !HasLegion)
            throw new FormatException($"Mission \"{Id}\": destroy_legion without the Legion.");
    }

    public string Id { get; }
    public string NameRu { get; }

    // Map file name without extension: Assets/Data/maps/<Map>.json.
    public string Map { get; }
    public int Players { get; }
    public bool HasLegion { get; }
    public MissionGoal Goal { get; }

    // Faction of start slot 1, 2, …; a slot past the list gets the default player faction.
    public System.Collections.Generic.IReadOnlyList<string> Factions { get; }

    // For the UI: colour name of start slot 1, 2, … (the sim never reads it).
    public System.Collections.Generic.IReadOnlyList<string> Colors { get; }

    private static MissionGoal ParseGoal(string text)
    {
        switch (text)
        {
            case "none": return MissionGoal.None;
            case "destroy_legion": return MissionGoal.DestroyLegion;
            case "kill_boss": return MissionGoal.KillBoss;
            default: throw new FormatException($"Unknown mission goal \"{text}\".");
        }
    }
}
