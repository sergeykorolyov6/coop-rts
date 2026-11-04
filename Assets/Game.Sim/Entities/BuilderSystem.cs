using System.Collections.Generic;

namespace Game.Sim;

// builder.md: every team player has one builder (and a Yellow Star player one hero, factions.md).
// A dead one comes back after its faction's respawn time next to the player's own finished
// headquarters, else next to the lowest-id ally's.
internal static class BuilderSystem
{
    public static void Tick(SimState state, List<SimEvent> events)
    {
        foreach (int playerId in state.TeamPlayerIds)
        {
            Player player = state.GetPlayer(playerId);
            FactionStats? faction = player.Faction;
            if (faction == null)
                continue;
            // The faction's builder (factions.csv); a faction without one has nobody to bring back.
            if (faction.BuilderUnitId.Length > 0)
            {
                int tick = player.BuilderRespawnTick;
                Unit? back = Keep(state, playerId, faction.BuilderUnitId, state.FindBuilder(playerId), faction.BuilderRespawnTicks, ref tick, out bool died, out Building? at);
                player.BuilderRespawnTick = tick;
                if (died)
                    events.Add(new BuilderDied(state.Tick, playerId, tick));
                if (back != null)
                    events.Add(new BuilderRespawned(state.Tick, playerId, back.Id, at!.Id));
            }
            // factions.md: the hero comes back the same way, keeping its level.
            if (faction.HeroUnitId.Length > 0)
            {
                int tick = player.HeroRespawnTick;
                Unit? back = Keep(state, playerId, faction.HeroUnitId, state.FindHero(playerId), faction.HeroRespawnTicks, ref tick, out bool died, out _);
                player.HeroRespawnTick = tick;
                if (died)
                    events.Add(new HeroDied(state.Tick, playerId, tick));
                if (back != null)
                {
                    HeroSystem.ApplyLevel(state, back, player.HeroLevel);
                    back.Hp = back.MaxHp;
                    events.Add(new HeroRespawned(state.Tick, playerId, back.Id));
                }
            }
        }
    }

    // One unique unit: notices its death (respawnTick becomes when it may come back) and brings it
    // back at a headquarters once the time has come. NeverTick in respawnTick means it lives.
    private static Unit? Keep(SimState state, int playerId, string unitId, Unit? living, int respawnTicks, ref int respawnTick, out bool died, out Building? at)
    {
        died = false;
        at = null;
        if (respawnTick == Entity.NeverTick)
        {
            if (living == null)
            {
                respawnTick = state.Tick + respawnTicks;
                died = true;
            }
            return null;
        }
        if (state.Tick < respawnTick)
            return null;
        Building? hq = RespawnHeadquarters(state, playerId);
        if (hq == null)
            return null;
        Unit? unit = Spawn(state, playerId, state.Data.Unit(unitId), hq);
        if (unit != null)
        {
            respawnTick = Entity.NeverTick;
            at = hq;
        }
        return unit;
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
