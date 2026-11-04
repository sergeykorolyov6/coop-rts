using System.Collections.Generic;

namespace Game.Sim;

internal static class UnitOrders
{
    // order-queue.md: a technical bound on the state, not balance.
    public const int MaxQueue = 32;

    // A new order replaces whatever the unit was doing. Like SC2, a worker drops its
    // not-yet-started silhouettes; sites already started stay and freeze (drill.md).
    // The Shift queue stays: an order ending inside the sim (Idle) must not wipe it.
    public static void Give(Unit unit, UnitOrder order)
    {
        unit.Order = order;
        unit.TargetId = 0;
        unit.RepairHpAccumulator = 0;
        Worker? worker = unit.Worker;
        if (worker != null)
        {
            worker._plans.Clear();
            worker.Phase = WorkerPhase.None;
            worker.CurrentPlanKey = -1;
        }
    }

    // A player's order without Shift: the queue goes too.
    public static void Replace(Unit unit, UnitOrder order)
    {
        unit._queue.Clear();
        Give(unit, order);
    }

    // With Shift, a busy unit takes the order into its queue instead of starting it; an idle
    // one starts at once (order-queue.md). True when the order went into the queue (or was
    // dropped because the queue is full: then `full` is set for the command to report).
    public static bool Defer(Unit unit, bool queued, UnitOrder order, PlannedBuild[]? plans, ref bool full)
    {
        if (!queued || (unit.Order.Kind == UnitOrderKind.Idle && unit._queue.Count == 0))
            return false;
        if (unit._queue.Count >= MaxQueue)
            full = true;
        else
            unit._queue.Add(new QueuedOrder(order, plans));
        return true;
    }

    // Start of the unit's turn with nothing to do: the next queued order becomes current.
    // Targets are checked again by the order's own tick; a mining cell is checked here,
    // since starting to mine changes the worker's state.
    public static void StartNext(SimState state, Unit unit, List<SimEvent> events)
    {
        QueuedOrder next = unit._queue[0];
        unit._queue.RemoveAt(0);
        UnitOrder order = next.Order;
        Worker? worker = unit.Worker;

        switch (order.Kind)
        {
            case UnitOrderKind.Mine:
                RejectReason? reason = ManualMining.CheckCell(state, unit.OwnerId, order.X, order.Y);
                if (reason == null && state.FindHeadquarters(unit.OwnerId) == null)
                    reason = RejectReason.NoHeadquarters;
                if (reason != null)
                {
                    events.Add(new CommandRejected(state.Tick, unit.OwnerId, reason.Value, unit.Id));
                    return;
                }
                ManualMining.Start(state, unit, order.X, order.Y);
                return;
            case UnitOrderKind.Build:
                Give(unit, order);
                foreach (PlannedBuild plan in next.Plans)
                    worker!._plans.Add(plan);
                return;
            case UnitOrderKind.Attack:
                Give(unit, order);
                unit.TargetId = order.TargetId;
                return;
            case UnitOrderKind.CarryTrophy:
                CreepCamp camp = state.CreepCamps[order.TargetId];
                if (camp.State != CreepCampState.Cleared || camp.TrophyPrimary + camp.TrophySecondary == 0)
                    return;
                Give(unit, order);
                worker!.DeliverToId = 0;
                worker.Phase = worker.Cargo > 0 ? WorkerPhase.Carrying : WorkerPhase.GoingToOre;
                return;
            default:
                Give(unit, order);
                return;
        }
    }
}
