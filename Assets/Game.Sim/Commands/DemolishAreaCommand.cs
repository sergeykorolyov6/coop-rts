using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// relay-and-demolish.md, U4.2: everything own that touches a rectangle of cells starts being
// demolished (demolish.md rules), and own workers' silhouettes there that nobody started are dropped.
// Never the headquarters, never what is invulnerable or not own; a demolition already going is left
// as it is. Corners in any order; the frame is cut by the map.
public sealed class DemolishAreaCommand : ICommand
{
    public const int Id = 18;

    public DemolishAreaCommand(int playerId, int tick, int x0, int y0, int x1, int y1)
    {
        PlayerId = playerId;
        Tick = tick;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public int X0 { get; }
    public int Y0 { get; }
    public int X1 { get; }
    public int Y1 { get; }

    public void WritePayload(BinaryWriter writer)
    {
        writer.Write(X0);
        writer.Write(Y0);
        writer.Write(X1);
        writer.Write(Y1);
    }

    public static DemolishAreaCommand Read(int playerId, int tick, BinaryReader reader) =>
        new DemolishAreaCommand(playerId, tick, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

    // What a player's frame takes down: own, standing, not invulnerable, not a headquarters (only
    // its own button demolishes that), not going already, and touching the frame. The preview of
    // the frame asks the same.
    public static bool Takes(Building b, int playerId, int minX, int minY, int maxX, int maxY) =>
        !b.IsDead && b.OwnerId == playerId && !b.Stats.Invulnerable && b.DemolishTicksLeft == 0
        && b.Stats.Behavior != BuildingBehavior.Headquarters
        && Grid.Touches(b.CellX, b.CellY, b.Width, b.Height, minX, minY, maxX, maxY);

    public void Execute(SimState state, List<SimEvent> events)
    {
        MapState map = state.Map;
        int minX = System.Math.Max(0, System.Math.Min(X0, X1)), maxX = System.Math.Min(map.Width - 1, System.Math.Max(X0, X1));
        int minY = System.Math.Max(0, System.Math.Min(Y0, Y1)), maxY = System.Math.Min(map.Height - 1, System.Math.Max(Y0, Y1));
        if (minX > maxX || minY > maxY)
            return;

        // In id order (the building list is), as every same-tick rule.
        foreach (Building b in state.Buildings)
        {
            if (Takes(b, PlayerId, minX, minY, maxX, maxY))
                b.DemolishTicksLeft = DemolishSystem.DemolishTicks(state.Rules, b.Stats);
        }

        // rebuild-ghosts.md: the owner's own ghosts in the frame go too.
        state._ghosts.RemoveAll(g => g.OwnerId == PlayerId && GhostRules.Touches(state, g, minX, minY, maxX, maxY));

        foreach (Unit unit in state.Units)
        {
            Worker? worker = unit.Worker;
            if (unit.IsDead || worker == null || unit.OwnerId != PlayerId)
                continue;
            for (int i = worker._plans.Count - 1; i >= 0; i--)
            {
                PlannedBuild plan = worker._plans[i];
                if (!Unstarted(state, plan, minX, minY, maxX, maxY))
                    continue;
                worker._plans.RemoveAt(i);
                if (worker.CurrentPlanKey == Construction.PlanKey(plan))
                {
                    worker.CurrentPlanKey = -1;
                    worker.WaitingForResources = false;
                }
            }
            for (int i = unit._queue.Count - 1; i >= 0; i--)
            {
                QueuedOrder queued = unit._queue[i];
                if (queued.Plans.Count == 0)
                    continue;
                var kept = new List<PlannedBuild>();
                foreach (PlannedBuild plan in queued.Plans)
                {
                    if (!Unstarted(state, plan, minX, minY, maxX, maxY))
                        kept.Add(plan);
                }
                if (kept.Count == queued.Plans.Count)
                    continue;
                // A build order left with nothing to build goes from the queue whole.
                if (kept.Count == 0)
                    unit._queue.RemoveAt(i);
                else
                    unit._queue[i] = new QueuedOrder(queued.Order, kept.ToArray());
            }
        }
    }

    // A silhouette nobody started that touches the frame (a started one is a building, taken down above).
    private static bool Unstarted(SimState state, PlannedBuild plan, int minX, int minY, int maxX, int maxY)
    {
        if (plan.ExistingSiteId != 0 || !state.Data.TryGetBuilding(plan.BuildingTypeId, out BuildingStats? stats))
            return false;
        return Grid.Touches(plan.CellX, plan.CellY, stats!.SizeW, stats.SizeH, minX, minY, maxX, maxY);
    }
}
