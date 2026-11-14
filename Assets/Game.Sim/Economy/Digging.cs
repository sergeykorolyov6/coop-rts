using System.Collections.Generic;

namespace Game.Sim;

// terrain.md: miners dig a tunnel to a rock cell, one cell at a time, nearest first.
internal static class Digging
{
    // terrain.md: a technical bound — the order queue holds as much (order-queue.md).
    public const int MaxTunnel = UnitOrders.MaxQueue;

    // The rock cells to dig, in order, so the unit can walk from where it stands to the target
    // (included): the way through the fewest rock cells. Ground is walked as units walk (CanStep);
    // rock is crossed through cell sides only, without a cliff in between. Null when eternal rock
    // or cliffs leave no way. A 0-1 search: ground costs nothing, each rock cell one; neighbours in
    // a fixed order and ties by the order of the deque, so every machine digs the same tunnel.
    public static List<(int X, int Y)>? PlanTunnel(SimState state, Unit unit, int targetX, int targetY)
    {
        MapState map = state.Map;
        if (!map.IsDestructibleRock(targetX, targetY) || !map.InBounds(unit.CellX, unit.CellY))
            return null;
        int count = map.Width * map.Height;
        var cost = new int[count];
        var parent = new int[count];
        for (int i = 0; i < count; i++)
            cost[i] = int.MaxValue;
        int start = map.Index(unit.CellX, unit.CellY), target = map.Index(targetX, targetY);
        cost[start] = 0;
        parent[start] = -1;
        // Level by level: `now` holds cells reached through `level` rock cells (walked as a queue,
        // growing while ground leads on), `next` those one rock cell deeper. A cell found cheaper
        // later is simply met again; the stale copy is skipped.
        var now = new List<int> { start };
        var next = new List<int>();
        for (int level = 0; now.Count > 0 && cost[target] == int.MaxValue; level++)
        {
            for (int k = 0; k < now.Count; k++)
            {
                int cell = now[k];
                if (cost[cell] != level)
                    continue;
                int cx = cell % map.Width, cy = cell / map.Width;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + FlowField.Dx[d], ny = cy + FlowField.Dy[d];
                    if (!map.InBounds(nx, ny))
                        continue;
                    int n = map.Index(nx, ny);
                    int step;
                    if (!map.IsDestructibleRock(cx, cy) && map.IsPassable(nx, ny) && map.CanStep(cx, cy, nx, ny))
                        step = 0;
                    else if (d < 4 && (map.IsPassable(nx, ny) || map.IsDestructibleRock(nx, ny)) && map.LevelsJoin(cx, cy, nx, ny))
                        step = map.IsDestructibleRock(nx, ny) ? 1 : 0;
                    else
                        continue;
                    int nc = level + step;
                    if (nc >= cost[n])
                        continue;
                    cost[n] = nc;
                    parent[n] = cell;
                    (step == 0 ? now : next).Add(n);
                }
            }
            (now, next) = (next, now);
            next.Clear();
        }
        if (cost[target] == int.MaxValue)
            return null;

        var tunnel = new List<(int X, int Y)>();
        for (int cell = target; cell != -1; cell = parent[cell])
        {
            int x = cell % map.Width, y = cell / map.Width;
            if (map.IsDestructibleRock(x, y))
                tunnel.Add((x, y));
        }
        tunnel.Reverse();
        if (tunnel.Count > MaxTunnel)
            tunnel.RemoveRange(MaxTunnel, tunnel.Count - MaxTunnel);
        return tunnel;
    }

    // One tick of a Dig order: walk next to the cell, then dig it from its own level.
    public static void TickDig(SimState state, Unit unit, List<SimEvent> events)
    {
        MapState map = state.Map;
        int x = unit.Order.X, y = unit.Order.Y;
        if (!map.IsDestructibleRock(x, y))
        {
            // Dug out by someone else meanwhile: on to the next cell of the tunnel.
            unit.Order = UnitOrder.Idle;
            return;
        }
        if (Movement.DistanceToRectangle(unit, x, y, 1, 1) <= state.Rules.InteractRangeMilli && Sight.LevelAdjacent(map, unit, x, y))
        {
            string rock = map.TerrainAt(x, y)!.Id;
            if (map.DamageRock(x, y, unit.Stats.DigPerTick))
            {
                events.Add(new RockDestroyed(state.Tick, unit.OwnerId, unit.Id, x, y, rock));
                unit.Order = UnitOrder.Idle;
            }
            return;
        }
        if (state.Paths.ToRectangle(map, x, y, 1, 1).DistanceAt(unit.CellX, unit.CellY) == FlowField.Unreachable)
        {
            // Nowhere to dig it from (yet): skipped, the next cell may open the way.
            unit.Order = UnitOrder.Idle;
            return;
        }
        Movement.StepToRectangle(state, unit, x, y, 1, 1);
    }
}
