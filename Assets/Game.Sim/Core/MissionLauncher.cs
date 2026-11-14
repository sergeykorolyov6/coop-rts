using System.Collections.Generic;

namespace Game.Sim;

// mission.md: one way to turn a mission row and its map into a match, shared by the game,
// saves and tests. Humans are players 1..N, allied; the Legion 9, the creeps 10, the neutral
// buildings' owner 11; the Legion's further bases (legion-ai.md, T5) 12, 13, …
public static class MissionLauncher
{
    public const int LegionPlayerId = 9;
    public const int CreepPlayerId = 10;
    public const int NeutralPlayerId = 11;

    // The player of the map's Legion base number baseIndex (from 0).
    public static int LegionPlayerIdFor(int baseIndex) => baseIndex == 0 ? LegionPlayerId : NeutralPlayerId + baseIndex;

    // Primary and secondary ore.
    public const int ResourceTypeCount = Resources.Secondary + 1;

    // factions: one per start slot, chosen in the network lobby (network.md); null — the mission's own.
    public static MatchSetup CreateSetup(GameData data, MissionStats mission, string mapJson, int seed, IReadOnlyList<string>? factions = null)
    {
        var humans = new List<int>();
        for (int id = 1; id <= mission.Players; id++)
            humans.Add(id);
        MapSetup map = MapLoader.Parse(mapJson, humans, data);

        var legions = new List<int>();
        for (int i = 0; mission.HasLegion && i < map.LegionBases.Count; i++)
            legions.Add(LegionPlayerIdFor(i));
        var playerIds = new List<int>(humans) { CreepPlayerId, NeutralPlayerId };
        playerIds.AddRange(legions);
        var relations = new List<RelationSetup>();
        foreach (int a in humans)
        {
            foreach (int b in humans)
            {
                if (a < b)
                    relations.Add(new RelationSetup(a, b, Relation.Ally));
            }
            relations.Add(new RelationSetup(a, CreepPlayerId, Relation.Enemy));
            foreach (int legion in legions)
                relations.Add(new RelationSetup(a, legion, Relation.Enemy));
        }
        // The Legion's bases are one enemy: they never fight each other.
        foreach (int a in legions)
        {
            foreach (int b in legions)
            {
                if (a < b)
                    relations.Add(new RelationSetup(a, b, Relation.Ally));
            }
        }
        // factions.md: the mission names the faction of every start slot; the rest get the default.
        IReadOnlyList<string> chosen = factions ?? mission.Factions;
        var slots = new List<PlayerFaction>();
        for (int i = 0; i < humans.Count && i < chosen.Count; i++)
            slots.Add(new PlayerFaction(humans[i], chosen[i]));
        return new MatchSetup(seed, data, map, ResourceTypeCount, playerIds, relations,
            CreepPlayerId, MatchSetup.NoPlayer, NeutralPlayerId, mission.Goal, slots, legions);
    }
}
