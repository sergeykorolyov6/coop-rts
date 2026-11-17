using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// rebuild-ghosts.md (U5): the ghosts in a rectangle become silhouettes of the selected workers —
// own ghosts, and an ally's when includeAllies (the worker's player pays, the building stays the
// ally's). Nearest to the worker first, then each time nearest to the previous one; ties: destroyed
// earlier. Joins the worker's build like a BuildCommand with Queued.
public sealed class RebuildCommand : ICommand
{
    public const int Id = 20;

    private readonly int[] _unitIds;

    public RebuildCommand(int playerId, int tick, int[] unitIds, int x0, int y0, int x1, int y1, bool includeAllies = false, bool queued = false)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        IncludeAllies = includeAllies;
        Queued = queued;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public int X0 { get; }
    public int Y0 { get; }
    public int X1 { get; }
    public int Y1 { get; }
    public bool IncludeAllies { get; }
    public bool Queued { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(X0);
        writer.Write(Y0);
        writer.Write(X1);
        writer.Write(Y1);
        writer.Write(IncludeAllies);
        writer.Write(Queued);
    }

    public static RebuildCommand Read(int playerId, int tick, BinaryReader reader) =>
        new RebuildCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            reader.ReadInt32(), reader.ReadBoolean(), reader.ReadBoolean());

    public void Execute(SimState state, List<SimEvent> events)
    {
        int minX = System.Math.Min(X0, X1), maxX = System.Math.Max(X0, X1);
        int minY = System.Math.Min(Y0, Y1), maxY = System.Math.Max(Y0, Y1);
        var picked = new List<Ghost>();
        foreach (Ghost g in state.Ghosts)
        {
            bool mine = g.OwnerId == PlayerId;
            if ((mine || (IncludeAllies && state.Relations.IsAlly(PlayerId, g.OwnerId)))
                && GhostRules.Touches(state, g, minX, minY, maxX, maxY))
                picked.Add(g);
        }
        if (picked.Count == 0)
            return;

        bool full = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            Worker? worker = unit.Worker;
            if (worker == null)
                continue;
            PlannedBuild[] plans = InOrder(state, unit, picked);
            if (Queued && unit.Order.Kind == UnitOrderKind.Build && unit._queue.Count == 0)
            {
                worker._plans.AddRange(plans);
                continue;
            }
            if (UnitOrders.Defer(unit, Queued, UnitOrder.Build(), plans, ref full))
                continue;
            UnitOrders.Replace(unit, UnitOrder.Build());
            worker._plans.AddRange(plans);
        }
        if (full)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
    }

    private PlannedBuild[] InOrder(SimState state, Unit unit, List<Ghost> ghosts)
    {
        var left = new List<Ghost>(ghosts);
        var plans = new List<PlannedBuild>();
        long x = unit.X, y = unit.Y;
        while (left.Count > 0 && plans.Count < BuildCommand.MaxSites)
        {
            int best = 0;
            long bestDist = long.MaxValue;
            for (int i = 0; i < left.Count; i++)
            {
                (long cx, long cy) = Centre(state, left[i]);
                long d = (cx - x) * (cx - x) + (cy - y) * (cy - y);
                Ghost g = left[i];
                if (d < bestDist || (d == bestDist && (g.DestroyedTick < left[best].DestroyedTick
                    || (g.DestroyedTick == left[best].DestroyedTick && g.Id < left[best].Id))))
                {
                    best = i;
                    bestDist = d;
                }
            }
            Ghost next = left[best];
            left.RemoveAt(best);
            plans.Add(new PlannedBuild(next.BuildingTypeId, next.CellX, next.CellY, next.Direction)
            {
                OwnerId = next.OwnerId == unit.OwnerId ? 0 : next.OwnerId,
            });
            (x, y) = Centre(state, next);
        }
        return plans.ToArray();
    }

    private static (long X, long Y) Centre(SimState state, Ghost g)
    {
        BuildingStats stats = state.Data.Building(g.BuildingTypeId);
        return (Grid.CellToMilli(g.CellX) + Grid.CellToMilli(stats.SizeW) / 2, Grid.CellToMilli(g.CellY) + Grid.CellToMilli(stats.SizeH) / 2);
    }
}
