using System.Collections.Generic;

namespace Game.Sim;

// mission.md: one way to turn a mission row and its map into a match, shared by the game,
// saves and tests. Humans are players 1..N, allied; the Legion 9, the creeps 10, the neutral
// buildings' owner 11.
public static class MissionLauncher
{
    public const int LegionPlayerId = 9;
    public const int CreepPlayerId = 10;
    public const int NeutralPlayerId = 11;

    // Primary and secondary ore.
    public const int ResourceTypeCount = Resources.Secondary + 1;

    public static MatchSetup CreateSetup(GameData data, MissionStats mission, string mapJson, int seed)
    {
        var humans = new List<int>();
        for (int id = 1; id <= mission.Players; id++)
            humans.Add(id);
        MapSetup map = MapLoader.Parse(mapJson, humans);

        bool legion = mission.HasLegion && map.LegionSlots.Count > 0;
        var playerIds = new List<int>(humans) { CreepPlayerId, NeutralPlayerId };
        if (legion)
            playerIds.Add(LegionPlayerId);
        var relations = new List<RelationSetup>();
        foreach (int a in humans)
        {
            foreach (int b in humans)
            {
                if (a < b)
                    relations.Add(new RelationSetup(a, b, Relation.Ally));
            }
            relations.Add(new RelationSetup(a, CreepPlayerId, Relation.Enemy));
            if (legion)
                relations.Add(new RelationSetup(a, LegionPlayerId, Relation.Enemy));
        }
        return new MatchSetup(seed, data, map, ResourceTypeCount, playerIds, relations,
            CreepPlayerId, legion ? LegionPlayerId : MatchSetup.NoPlayer, NeutralPlayerId, mission.Goal);
    }
}
