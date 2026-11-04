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
