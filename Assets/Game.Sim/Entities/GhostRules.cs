using System.Collections.Generic;

namespace Game.Sim;

// rebuild-ghosts.md: when a ghost is left, when it goes, and which one a repairer takes.
internal static class GhostRules
{
    // A team player's building destroyed by damage: not when its owner was demolishing it, not an
    // invulnerable one, not a kind the owner's faction does not build. Beyond ghost_limit the
    // owner's oldest ghost goes.
    public static void Leave(SimState state, Building b)
    {
        if (!state.IsTeamPlayer(b.OwnerId) || b.Stats.Invulnerable || b.DemolishTicksLeft > 0
            || !state.GetPlayer(b.OwnerId).CanBuild(b.Stats.Id))
            return;
        List<Ghost> ghosts = state._ghosts;
        ghosts.Add(new Ghost(state.NextGhostId++, b.OwnerId, b.Stats.Id, b.CellX, b.CellY, b.Direction, state.Tick));
        int count = 0;
        foreach (Ghost g in ghosts)
        {
            if (g.OwnerId == b.OwnerId)
                count++;
        }
        for (int i = 0; i < ghosts.Count && count > state.Rules.GhostLimit; i++)
        {
            if (ghosts[i].OwnerId != b.OwnerId)
                continue;
            ghosts.RemoveAt(i);
            count--;
            i--;
        }
    }

    // A building (any, anyone's) started over a ghost's cells takes its place.
    public static void RemoveUnder(SimState state, int x, int y, int w, int h)
    {
        state._ghosts.RemoveAll(g => Touches(state, g, x, y, x + w - 1, y + h - 1));
    }

    public static bool Touches(SimState state, Ghost g, int minX, int minY, int maxX, int maxY)
    {
        BuildingStats stats = state.Data.Building(g.BuildingTypeId);
        return Grid.Touches(g.CellX, g.CellY, stats.SizeW, stats.SizeH, minX, minY, maxX, maxY);
    }

    public static Ghost? Find(SimState state, int id)
    {
        foreach (Ghost g in state._ghosts)
        {
            if (g.Id == id)
                return g;
        }
        return null;
    }

    // U5.3: the nearest own or ally ghost within auto_repair_radius the repairer's player can pay for,
    // whose place is free, with no enemy in sight within ai_rebuild_safe_radius of it; ties by lower id.
    public static Ghost? NearestFor(SimState state, Unit unit)
    {
        Ghost? best = null;
        long bestDist = long.MaxValue;
        foreach (Ghost g in state._ghosts)
        {
            if (!state.Relations.IsAlly(unit.OwnerId, g.OwnerId))
                continue;
            BuildingStats stats = state.Data.Building(g.BuildingTypeId);
            long d = Movement.DistanceToRectangle(unit, g.CellX, g.CellY, stats.SizeW, stats.SizeH);
            if (d > state.Rules.AutoRepairRadiusMilli || d >= bestDist)
                continue;
            if (Construction.CheckCanPay(state, unit.OwnerId, stats) != null
                || Construction.CheckPlacement(state, g.OwnerId, stats, g.CellX, g.CellY, g.Direction) != null
                || EnemyNear(state, unit.OwnerId, g, stats))
                continue;
            best = g;
            bestDist = d;
        }
        return best;
    }

    private static bool EnemyNear(SimState state, int playerId, Ghost g, BuildingStats stats)
    {
        long radius = state.Rules.AiRebuildSafeRadiusMilli;
        foreach (Unit u in state.Units)
        {
            if (u.IsDead || !state.Relations.IsEnemy(playerId, u.OwnerId) || !state.Fog.IsVisible(playerId, u.CellX, u.CellY))
                continue;
            if (Movement.DistanceToRectangle(u, g.CellX, g.CellY, stats.SizeW, stats.SizeH) <= radius)
                return true;
        }
        return false;
    }
}
