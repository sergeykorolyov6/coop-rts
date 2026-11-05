using System.Collections.Generic;

namespace Game.Sim;

// Ground movement: follow a flow field cell by cell, then separate overlapping units.
internal static class Movement
{
    // One tick toward a point.
    public static MoveResult StepToPoint(SimState state, Unit unit, int x, int y)
    {
        int cx = Grid.MilliToCell(x), cy = Grid.MilliToCell(y);
        FlowField field = state.Paths.ToCell(state.Map, cx, cy);
        int here = field.DistanceAt(unit.CellX, unit.CellY);
        if (here == FlowField.Unreachable)
            return MoveResult.Unreachable;
        if (here == 0)
        {
            // In the goal cell. A blocked target leads to the cells around it: stop at the cell center.
            bool exact = state.Map.IsPassable(cx, cy);
            return StepStraight(unit, exact ? x : Grid.CellCenter(unit.CellX), exact ? y : Grid.CellCenter(unit.CellY))
                ? MoveResult.Arrived
                : MoveResult.Moving;
        }
        StepAlong(state, unit, field);
        return MoveResult.Moving;
    }

    // The reachable cell nearest to (x, y), for a target the unit cannot get to (walled in,
    // across water): like SC2, it walks as close as it can. Ties go to the lower cell index.
    public static (int X, int Y) NearestReachableCell(SimState state, Unit unit, int x, int y)
    {
        MapState map = state.Map;
        FlowField fromUnit = state.Paths.ToCell(map, unit.CellX, unit.CellY);
        int bestX = unit.CellX, bestY = unit.CellY;
        long best = long.MaxValue;
        for (int cy = 0; cy < map.Height; cy++)
        {
            for (int cx = 0; cx < map.Width; cx++)
            {
                if (fromUnit.DistanceAt(cx, cy) == FlowField.Unreachable)
                    continue;
                long dx = Grid.CellCenter(cx) - x, dy = Grid.CellCenter(cy) - y;
                long d2 = dx * dx + dy * dy;
                if (d2 < best)
                {
                    best = d2;
                    bestX = cx;
                    bestY = cy;
                }
            }
        }
        return (bestX, bestY);
    }

    // One tick toward any cell touching the rectangle. Arrival (being within reach)
    // is for the caller to judge: units stop as soon as they can work.
    public static void StepToRectangle(SimState state, Unit unit, int x, int y, int w, int h)
    {
        FlowField field = state.Paths.ToRectangle(state.Map, x, y, w, h);
        if (field.DistanceAt(unit.CellX, unit.CellY) == 0)
        {
            // Next to it but pushed out of reach by the crowd: close in on the nearest point of the
            // rectangle. The caller stops the unit once it can work, long before the edge.
            int minX = Grid.CellToMilli(x), maxX = Grid.CellToMilli(x + w), minY = Grid.CellToMilli(y), maxY = Grid.CellToMilli(y + h);
            int px = unit.X < minX ? minX : unit.X > maxX ? maxX : unit.X;
            int py = unit.Y < minY ? minY : unit.Y > maxY ? maxY : unit.Y;
            StepStraight(unit, px, py);
            return;
        }
        StepAlong(state, unit, field);
    }

    // Distance from the unit's center to the nearest point of a cell rectangle, milli-cells.
    public static long DistanceToRectangle(Unit unit, int cellX, int cellY, int w, int h) =>
        DistanceToRectangle(unit.X, unit.Y, cellX, cellY, w, h);

    // From a point to a building's footprint, milli-cells.
    public static long DistanceToBuilding(int x, int y, Building b) =>
        DistanceToRectangle(x, y, b.CellX, b.CellY, b.Width, b.Height);

    public static long DistanceToRectangle(int x, int y, int cellX, int cellY, int w, int h)
    {
        long minX = Grid.CellToMilli(cellX), maxX = Grid.CellToMilli(cellX + w);
        long minY = Grid.CellToMilli(cellY), maxY = Grid.CellToMilli(cellY + h);
        long dx = x < minX ? minX - x : x > maxX ? x - maxX : 0;
        long dy = y < minY ? minY - y : y > maxY ? y - maxY : 0;
        return IntMath.Sqrt(dx * dx + dy * dy);
    }

    public static long Distance(int x1, int y1, int x2, int y2) => IntMath.Sqrt(DistanceSquared(x1, y1, x2, y2));

    // For comparisons against r·r without the root.
    public static long DistanceSquared(int x1, int y1, int x2, int y2)
    {
        long dx = x2 - x1, dy = y2 - y1;
        return dx * dx + dy * dy;
    }

    private static bool StepAlong(SimState state, Unit unit, FlowField field)
    {
        if (!field.TryNextCell(state.Map, unit.CellX, unit.CellY, out int nx, out int ny))
        {
            // At the goal (or cut off): settle into the cell instead of hovering on its edge.
            return StepStraight(unit, Grid.CellCenter(unit.CellX), Grid.CellCenter(unit.CellY));
        }
        StepStraight(unit, Grid.CellCenter(nx), Grid.CellCenter(ny));
        return false;
    }

    // Straight line at the unit's speed; snaps onto the target when it is within one step.
    private static bool StepStraight(Unit unit, int x, int y)
    {
        long dx = x - unit.X, dy = y - unit.Y;
        long dist = IntMath.Sqrt(dx * dx + dy * dy);
        int speed = unit.MoveSpeedMilliPerTick;
        if (dist <= speed)
        {
            unit.StepX = x - unit.X;
            unit.StepY = y - unit.Y;
            unit.X = x;
            unit.Y = y;
            return true;
        }
        unit.StepX = (int)(dx * speed / dist);
        unit.StepY = (int)(dy * speed / dist);
        unit.X += unit.StepX;
        unit.Y += unit.StepY;
        return false;
    }

    // Simple avoidance: overlapping units push each other apart, half each. Deltas are
    // summed first and applied after, so the result does not depend on pair order;
    // a push into a cell a unit cannot stand on is dropped.
    public static void Separate(SimState state)
    {
        IReadOnlyList<Unit> units = state.Units;
        int n = units.Count;
        if (n < 2)
            return;
        var pushX = new int[n];
        var pushY = new int[n];

        for (int i = 0; i < n; i++)
        {
            Unit a = units[i];
            if (a.IsDead)
                continue;
            for (int j = i + 1; j < n; j++)
            {
                Unit b = units[j];
                if (b.IsDead)
                    continue;
                long minDist = a.Radius + b.Radius;
                long dx = b.X - a.X, dy = b.Y - a.Y;
                if (dx >= minDist || dx <= -minDist || dy >= minDist || dy <= -minDist)
                    continue;
                long d2 = dx * dx + dy * dy;
                if (d2 >= minDist * minDist)
                    continue;

                long d = IntMath.Sqrt(d2);
                bool aMoves = a.StepX != 0 || a.StepY != 0, bMoves = b.StepX != 0 || b.StepY != 0;
                if (aMoves != bMoves)
                {
                    // A walking unit parts standing ones sideways and keeps going; pushing them
                    // straight ahead would shove them all the way to its destination.
                    Unit mover = aMoves ? a : b, still = aMoves ? b : a;
                    int stillIndex = aMoves ? j : i;
                    long rx = still.X - mover.X, ry = still.Y - mover.Y;
                    long hx = mover.StepX, hy = mover.StepY;
                    long cross = hx * ry - hy * rx;
                    bool left = cross > 0 || (cross == 0 && still.Id < mover.Id);
                    long sx = left ? -hy : hy, sy = left ? hx : -hx;
                    long len = IntMath.Sqrt(sx * sx + sy * sy);
                    long amount = minDist - d;
                    pushX[stillIndex] += (int)(sx * amount / len);
                    pushY[stillIndex] += (int)(sy * amount / len);
                    continue;
                }

                if (d == 0)
                {
                    // Same spot: the lower id steps left, the higher one right.
                    dx = 1;
                    dy = 0;
                    d = 1;
                }
                long half = (minDist - d + 1) / 2;
                int px = (int)(dx * half / d), py = (int)(dy * half / d);
                pushX[i] -= px;
                pushY[i] -= py;
                pushX[j] += px;
                pushY[j] += py;

                if (aMoves && bMoves)
                {
                    // Two walkers meeting: each also steps to its own right, so units coming head-on
                    // slide past each other instead of pushing back and forth forever.
                    KeepRight(a, half, ref pushX[i], ref pushY[i]);
                    KeepRight(b, half, ref pushX[j], ref pushY[j]);
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            Unit u = units[i];
            if (pushX[i] == 0 && pushY[i] == 0)
                continue;
            int nx = u.X + pushX[i], ny = u.Y + pushY[i];
            if (state.Map.IsPassable(Grid.MilliToCell(nx), Grid.MilliToCell(u.Y)))
                u.X = nx;
            if (state.Map.IsPassable(Grid.MilliToCell(u.X), Grid.MilliToCell(ny)))
                u.Y = ny;
        }
    }

    private static void KeepRight(Unit u, long amount, ref int pushX, ref int pushY)
    {
        long hx = u.StepX, hy = u.StepY;
        long len = IntMath.Sqrt(hx * hx + hy * hy);
        if (len == 0)
            return;
        // Right of the heading (hx, hy) is (hy, -hx).
        pushX += (int)(hy * amount / len);
        pushY += (int)(-hx * amount / len);
    }

    // A building finished or spawned on top of a unit: move it to the nearest free cell.
    public static void PushOutOfBlockedCells(SimState state)
    {
        foreach (Unit u in state.Units)
        {
            if (u.IsDead || state.Map.IsPassable(u.CellX, u.CellY))
                continue;
            FlowField field = state.Paths.ToCell(state.Map, u.CellX, u.CellY);
            // The field to a blocked cell leads to the passable ring around it; take the nearest
            // ring cell in fixed scan order.
            int bestX = u.CellX, bestY = u.CellY, best = FlowField.Unreachable;
            for (int ring = 1; best == FlowField.Unreachable && ring <= state.Map.Width + state.Map.Height; ring++)
            {
                for (int y = u.CellY - ring; y <= u.CellY + ring; y++)
                {
                    for (int x = u.CellX - ring; x <= u.CellX + ring; x++)
                    {
                        bool onRing = y == u.CellY - ring || y == u.CellY + ring || x == u.CellX - ring || x == u.CellX + ring;
                        if (!onRing || !state.Map.IsPassable(x, y) || field.DistanceAt(x, y) != 0)
                            continue;
                        long dist = Distance(u.X, u.Y, Grid.CellCenter(x), Grid.CellCenter(y));
                        if (dist < best)
                        {
                            best = (int)dist;
                            bestX = x;
                            bestY = y;
                        }
                    }
                }
            }
            if (best != FlowField.Unreachable)
            {
                u.X = Grid.CellCenter(bestX);
                u.Y = Grid.CellCenter(bestY);
            }
        }
    }
}
