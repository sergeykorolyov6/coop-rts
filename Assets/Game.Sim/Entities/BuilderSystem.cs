using System.Collections.Generic;

namespace Game.Sim;

// builder.md: every team player has one builder. A dead one comes back after builder_respawn
// next to the player's own finished headquarters, else next to the lowest-id ally's.
internal static class BuilderSystem
{
    public static void Tick(SimState state, List<SimEvent> events)
    {
        foreach (int playerId in state.TeamPlayerIds)
        {
            Player player = state.GetPlayer(playerId);
            // The faction's builder (factions.csv); a faction without one has nobody to bring back.
            string builderId = player.Faction?.BuilderUnitId ?? "";
            if (builderId.Length == 0)
                continue;
            UnitStats stats = state.Data.Unit(builderId);
            if (!player.IsBuilderDead)
            {
                if (state.FindBuilder(playerId) == null)
                {
                    player.BuilderRespawnTick = state.Tick + state.Rules.BuilderRespawnTicks;
                    events.Add(new BuilderDied(state.Tick, playerId, player.BuilderRespawnTick));
                }
                continue;
            }
            if (state.Tick < player.BuilderRespawnTick)
                continue;
            Building? hq = RespawnHeadquarters(state, playerId);
            if (hq == null)
                continue;
            Unit? builder = Spawn(state, playerId, stats, hq);
            if (builder == null)
                continue;
            player.BuilderRespawnTick = Entity.NeverTick;
            events.Add(new BuilderRespawned(state.Tick, playerId, builder.Id, hq.Id));
        }
    }

    // On a free cell around the headquarters; none free — waits (like a factory, factory.md).
    public static Unit? Spawn(SimState state, int playerId, UnitStats stats, Building hq)
    {
        List<(int X, int Y)> cells = state.FreeCellsAround(hq.CellX, hq.CellY, hq.Width, hq.Height, 1);
        if (cells.Count == 0)
            return null;
        return state.SpawnUnit(playerId, stats, Grid.CellCenter(cells[0].X), Grid.CellCenter(cells[0].Y));
    }

    private static Building? RespawnHeadquarters(SimState state, int playerId)
    {
        Building? own = state.FindHeadquarters(playerId);
        if (own != null && own.IsComplete)
            return own;
        foreach (int ally in state.TeamPlayerIds)
        {
            if (ally == playerId || !state.Relations.IsAlly(playerId, ally))
                continue;
            Building? hq = state.FindHeadquarters(ally);
            if (hq != null && hq.IsComplete)
                return hq;
        }
        return null;
    }
}
