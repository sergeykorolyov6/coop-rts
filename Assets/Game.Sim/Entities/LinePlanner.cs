using System.Collections.Generic;

namespace Game.Sim;

// smart-line.md (U3): from the press cell to the cursor cell with one bend, what to order on
// every cell — new belt, skip, relay, a bridge over obstacles and cliffs — and which way the end
// looks. Only reads the state: the build menu calls it every frame, BuildCommand does the rest.
public static class LinePlanner
{
    // What stands on a cell of the path, seen from a belt laid along it.
    private enum Seen : byte { Free, Skip, Relay, Across, Obstacle }

    public static LinePlan Plan(SimState state, int playerId, string buildingTypeId, int startX, int startY, int endX, int endY,
        bool horizontalFirst, Direction single)
    {
        var cells = new List<LineCell>();
        if (!state.Data.TryGetBuilding(buildingTypeId, out BuildingStats? stats))
            return new LinePlan(cells, buildingTypeId, null, 0, 0, LineStop.FirstCell, 0, false, 0);

        List<(int X, int Y)> path = Path(startX, startY, endX, endY, horizontalFirst);
        int n = path.Count;
        var dirs = new Direction[n];
        for (int i = 0; i < n; i++)
            dirs[i] = n == 1 ? single : i < n - 1 ? Step(path[i], path[i + 1]) : Step(path[i - 1], path[i]);

        if (stats!.Behavior != BuildingBehavior.Conveyor)
            return Walls(state, playerId, stats, path, dirs);

        BuildingStats? bridge = FactionBridge(state, playerId);
        var seen = new Seen[n];
        for (int i = 0; i < n; i++)
            seen[i] = Look(state, playerId, stats, path[i].X, path[i].Y, dirs[i]);

        // An own belt across the path: on the first cell it is relaid (the player started on it);
        // on the last the line stops a cell before and looks into it; in the middle it is in the way.
        bool ontoBelt = n > 1 && seen[n - 1] == Seen.Across;
        if (ontoBelt)
            n--;
        if (seen[0] == Seen.Across)
            seen[0] = Seen.Relay;

        if (seen[0] == Seen.Obstacle)
        {
            for (int i = 0; i < n; i++)
                cells.Add(new LineCell(path[i].X, path[i].Y, dirs[i], LineCellKind.Broken));
            return new LinePlan(cells, stats.Id, bridge?.Id, 0, 0, LineStop.FirstCell, 0, false, 0);
        }

        LineStop stop = LineStop.None;
        int brokenSpan = 0;
        int index = 0;
        while (index < n)
        {
            bool obstacle = seen[index] == Seen.Obstacle || seen[index] == Seen.Across;
            bool cliff = !obstacle && index > 0 && !state.Map.LevelsJoin(path[index - 1].X, path[index - 1].Y, path[index].X, path[index].Y);
            if (!obstacle && !cliff)
            {
                cells.Add(new LineCell(path[index].X, path[index].Y, dirs[index], Kind(seen[index])));
                index++;
                continue;
            }

            // Under the span: index..last (none over a bare cliff); the bridge comes out at exit.
            int last = index - 1;
            if (obstacle)
            {
                last = index;
                while (last + 1 < n && (seen[last + 1] == Seen.Obstacle || seen[last + 1] == Seen.Across))
                    last++;
                // Obstacles at the very end are dropped: the line ends before them.
                if (last == n - 1)
                    break;
            }
            int exit = last + 1;
            stop = TryBridge(state, playerId, bridge, cells, path, seen, exit, n);
            if (stop != LineStop.None)
            {
                if (stop == LineStop.BridgeTooFar)
                    brokenSpan = System.Math.Abs(path[exit].X - path[index - 1].X) + System.Math.Abs(path[exit].Y - path[index - 1].Y) - 1;
                for (int i = index; i < n; i++)
                    cells.Add(new LineCell(path[i].X, path[i].Y, dirs[i], LineCellKind.Broken));
                break;
            }

            Direction span = Step(path[index - 1], path[exit]);
            cells[cells.Count - 1] = cells[cells.Count - 1].With(span, LineCellKind.BridgeIn);
            for (int i = index; i <= last; i++)
                cells.Add(new LineCell(path[i].X, path[i].Y, span, LineCellKind.UnderBridge));
            cells.Add(new LineCell(path[exit].X, path[exit].Y, span, LineCellKind.BridgeOut));
            index = exit + 1;
        }

        // Where the end looks: dragged onto an own belt — into that belt; otherwise it turns to a
        // receiver beside it, and failing that may still run into an own belt ahead.
        int stuckTo = 0;
        bool merges = false;
        if (stop == LineStop.None && path.Count > 1 && IsBelt(cells[cells.Count - 1].Kind))
        {
            if (!ontoBelt)
                stuckTo = Stick(state, playerId, cells);
            merges = stuckTo == 0 && FeedsOwnBelt(state, playerId, stats, cells[cells.Count - 1]);
        }

        int belts = 0, ends = 0;
        foreach (LineCell c in cells)
        {
            if (c.Kind == LineCellKind.Build)
                belts++;
            else if (c.Kind == LineCellKind.BridgeIn || c.Kind == LineCellKind.BridgeOut)
                ends++;
        }
        int primary = belts * stats.CostPrimary + (bridge != null ? ends * bridge.CostPrimary : 0);
        int secondary = belts * stats.CostSecondary + (bridge != null ? ends * bridge.CostSecondary : 0);
        return new LinePlan(cells, stats.Id, bridge?.Id, primary, secondary, stop, brokenSpan, merges, stuckTo);
    }

    // The press cell first, along the first axis to the bend, then along the other; at most MaxSites cells.
    private static List<(int X, int Y)> Path(int x0, int y0, int x1, int y1, bool horizontalFirst)
    {
        var path = new List<(int X, int Y)> { (x0, y0) };
        int x = x0, y = y0;
        for (int leg = 0; leg < 2; leg++)
        {
            bool horizontal = (leg == 0) == horizontalFirst;
            while (path.Count < BuildCommand.MaxSites && (horizontal ? x != x1 : y != y1))
            {
                if (horizontal)
                    x += System.Math.Sign(x1 - x);
                else
                    y += System.Math.Sign(y1 - y);
                path.Add((x, y));
            }
        }
        return path;
    }

    private static Seen Look(SimState state, int playerId, BuildingStats belt, int x, int y, Direction direction)
    {
        Building? own = Construction.RelayableBelt(state, playerId, belt, x, y);
        if (own != null)
        {
            if (own.Direction == direction)
                return Seen.Skip;
            return own.Direction == Opposite(direction) ? Seen.Relay : Seen.Across;
        }
        return Construction.CheckPlacement(state, playerId, belt, x, y, direction) == null ? Seen.Free : Seen.Obstacle;
    }

    private static LineCellKind Kind(Seen seen) =>
        seen == Seen.Skip ? LineCellKind.Skip : seen == Seen.Relay ? LineCellKind.Relay : LineCellKind.Build;

    // A cell of the line that is or will be a belt cell (not a bridge end, not a broken one).
    private static bool IsBelt(LineCellKind kind) =>
        kind == LineCellKind.Build || kind == LineCellKind.Relay || kind == LineCellKind.Skip;

    // May a bridge go from the last placed cell (its input end) to path[exit] (its output end): both
    // new cells, on one straight line within the bridge range, and the line goes straight on after the
    // output (it gives only forward). None when it may; otherwise why not.
    private static LineStop TryBridge(SimState state, int playerId, BuildingStats? bridge, List<LineCell> cells,
        List<(int X, int Y)> path, Seen[] seen, int exit, int n)
    {
        LineCell entry = cells[cells.Count - 1];
        if (bridge == null || entry.Kind != LineCellKind.Build || seen[exit] != Seen.Free)
            return LineStop.NoBridge;
        (int X, int Y) input = (entry.X, entry.Y), output = path[exit];
        if ((input.X == output.X) == (input.Y == output.Y))
            return LineStop.NoBridge;
        if (!ConveyorSystem.InBridgeRange(state, input.X, input.Y, output.X, output.Y))
            return LineStop.BridgeTooFar;
        Direction span = Step(input, output);
        if (exit + 1 < n && Step(output, path[exit + 1]) != span)
            return LineStop.NoBridge;
        return Construction.CheckPlacement(state, playerId, bridge, input.X, input.Y, span) == null
            && Construction.CheckPlacement(state, playerId, bridge, output.X, output.Y, span) == null
            ? LineStop.None : LineStop.NoBridge;
    }

    // The end turns to a receiver beside it: straight on, then left, then right — never back.
    // A belt that already lies there (Skip) is not turned: only a receiver straight ahead counts.
    private static int Stick(SimState state, int playerId, List<LineCell> cells)
    {
        LineCell end = cells[cells.Count - 1];
        Direction ahead = cells.Count > 1 ? Step((cells[cells.Count - 2].X, cells[cells.Count - 2].Y), (end.X, end.Y)) : end.Direction;
        for (int turn = 0; turn < 3; turn++)
        {
            Direction d = turn == 0 ? ahead : turn == 1 ? Left(ahead) : Right(ahead);
            if (end.Kind == LineCellKind.Skip && d != end.Direction)
                continue;
            int x = end.X + d.Dx(), y = end.Y + d.Dy();
            if (!state.Map.InBounds(x, y) || !state.Map.LevelsJoin(end.X, end.Y, x, y))
                continue;
            Building? there = state.FindBuilding(state.Map.BuildingAt(x, y));
            if (there == null || !Receives(state, playerId, there))
                continue;
            cells[cells.Count - 1] = end.With(d, end.Kind);
            return there.Id;
        }
        return 0;
    }

    // What takes from a belt (Logistics.TryGive): an own or ally headquarters, storage or building
    // with a stock; an own bridge input.
    private static bool Receives(SimState state, int playerId, Building b)
    {
        if (b.IsDead || !state.Relations.IsAlly(playerId, b.OwnerId))
            return false;
        if (b.Stock != null || b.Stats.Behavior == BuildingBehavior.Headquarters || b.Stats.Behavior == BuildingBehavior.Storage)
            return true;
        return b.Bridge != null && b.OwnerId == playerId && !b.Bridge.IsOutputEnd;
    }

    // The cell the end looks at is an own belt that takes from it: no cliff between, not head-on.
    private static bool FeedsOwnBelt(SimState state, int playerId, BuildingStats belt, LineCell end)
    {
        int x = end.X + end.Direction.Dx(), y = end.Y + end.Direction.Dy();
        if (!state.Map.InBounds(x, y) || !state.Map.LevelsJoin(end.X, end.Y, x, y))
            return false;
        Building? ahead = Construction.RelayableBelt(state, playerId, belt, x, y);
        return ahead != null && ahead.Direction != Opposite(end.Direction);
    }

    // A wall line: every cell it can stand on, the rest skipped, the line goes on.
    private static LinePlan Walls(SimState state, int playerId, BuildingStats stats, List<(int X, int Y)> path, Direction[] dirs)
    {
        var cells = new List<LineCell>();
        int built = 0;
        for (int i = 0; i < path.Count; i++)
        {
            bool free = Construction.CheckPlacement(state, playerId, stats, path[i].X, path[i].Y, dirs[i]) == null;
            cells.Add(new LineCell(path[i].X, path[i].Y, dirs[i], free ? LineCellKind.Build : LineCellKind.Skip));
            if (free)
                built++;
        }
        LineStop stop = built == 0 && path.Count == 1 ? LineStop.FirstCell : LineStop.None;
        return new LinePlan(cells, stats.Id, null, built * stats.CostPrimary, built * stats.CostSecondary, stop, 0, false, 0);
    }

    // The bridge the player's faction builds, null when it has none.
    private static BuildingStats? FactionBridge(SimState state, int playerId)
    {
        FactionStats? faction = state.GetPlayer(playerId).Faction;
        if (faction == null)
            return null;
        foreach (string id in faction.Buildings)
        {
            if (state.Data.TryGetBuilding(id, out BuildingStats? b) && b!.Behavior == BuildingBehavior.Bridge)
                return b;
        }
        return null;
    }

    private static Direction Step((int X, int Y) from, (int X, int Y) to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (System.Math.Abs(dx) >= System.Math.Abs(dy))
            return dx >= 0 ? Direction.Right : Direction.Left;
        return dy > 0 ? Direction.Up : Direction.Down;
    }

    private static Direction Opposite(Direction d) => (Direction)(((int)d + 2) % 4);

    private static Direction Left(Direction d) => (Direction)(((int)d + 1) % 4);

    private static Direction Right(Direction d) => (Direction)(((int)d + 3) % 4);
}
