using System.Collections.Generic;

namespace Game.Sim;

// manual-mining.md: dig a free ore cell, carry a load to a headquarters, come back.
internal static class ManualMining
{
    // Null when the player may mine the cell now.
    public static RejectReason? CheckCell(SimState state, int playerId, int x, int y)
    {
        if (!state.Map.InBounds(x, y) || state.Map.OreAt(x, y) == MapSetup.NoOre)
            return RejectReason.NotAllowed;
        if (!state.Fog.IsDiscovered(playerId, x, y))
            return RejectReason.NotDiscovered;
        if (state.Map.BuildingAt(x, y) != 0)
            return RejectReason.OreCellTaken;
        return null;
    }

    // Own or an ally's finished headquarters; never an enemy's (manual-mining.md, limits).
    public static bool CanDeliverTo(SimState state, int playerId, Building hq) =>
        !hq.IsDead && hq.IsComplete && hq.Stats.Id == BuildingKinds.Headquarters
        && state.Relations.IsAlly(playerId, hq.OwnerId);

    public static void Start(SimState state, Unit unit, int x, int y)
    {
        Worker worker = unit.Worker!;
        UnitOrders.Give(unit, UnitOrder.Mine(x, y));
        worker.DeliverToId = 0;
        // One resource type per trip: a full load, or a load of the other resource, goes home first.
        bool mustUnload = worker.Cargo > 0
            && (worker.Cargo >= state.Rules.ManualCargo || worker.CargoType != state.Map.OreAt(x, y));
        worker.Phase = mustUnload ? WorkerPhase.Carrying : WorkerPhase.GoingToOre;
    }

    public static void TickMine(SimState state, Unit unit, List<SimEvent> events)
    {
        Worker worker = unit.Worker!;
        int x = unit.Order.X, y = unit.Order.Y;

        if (state.Map.BuildingAt(x, y) != 0)
        {
            // The cell was taken (a drill or a conveyor started on it): hand in what was dug, then stop.
            events.Add(new MiningStopped(state.Tick, unit.OwnerId, unit.Id, x, y));
            worker.MiningAccumulator = 0;
            UnitOrders.Give(unit, worker.Cargo > 0 ? UnitOrder.ReturnCargo() : UnitOrder.Idle);
            if (worker.Cargo > 0)
                worker.Phase = WorkerPhase.Carrying;
            return;
        }

        switch (worker.Phase)
        {
            case WorkerPhase.GoingToOre:
                if (InReachOfCell(state, unit, x, y))
                {
                    worker.Phase = WorkerPhase.Digging;
                    return;
                }
                if (Movement.StepToPoint(state, unit, Grid.CellCenter(x), Grid.CellCenter(y)) == MoveResult.Unreachable)
                    UnitOrders.Give(unit, UnitOrder.Idle);
                return;

            case WorkerPhase.Digging:
                if (!InReachOfCell(state, unit, x, y))
                {
                    worker.Phase = WorkerPhase.GoingToOre;
                    return;
                }
                Dig(state, worker, state.Map.OreAt(x, y));
                return;

            case WorkerPhase.Carrying:
                if (Carry(state, unit, events) == CarryResult.Delivered)
                    worker.Phase = WorkerPhase.GoingToOre;
                return;

            default:
                worker.Phase = WorkerPhase.GoingToOre;
                return;
        }
    }

    // A single trip to a headquarters, then idle.
    public static void TickReturnCargo(SimState state, Unit unit, List<SimEvent> events)
    {
        if (unit.Worker!.Cargo == 0 || Carry(state, unit, events) == CarryResult.Delivered)
            UnitOrders.Give(unit, UnitOrder.Idle);
    }

    // creep-camp.md: trophy runs work like mining trips; the pile is the "ore" and it runs out.
    public static void TickCarryTrophy(SimState state, Unit unit, List<SimEvent> events)
    {
        Worker worker = unit.Worker!;
        CreepCamp camp = state.CreepCamps[unit.Order.TargetId];
        if (worker.Phase == WorkerPhase.Carrying)
        {
            if (Carry(state, unit, events) == CarryResult.Delivered)
                worker.Phase = WorkerPhase.GoingToOre;
            return;
        }

        if (!InReachOfCell(state, unit, camp.CenterX, camp.CenterY))
        {
            Movement.StepToPoint(state, unit, camp.CenterMilliX, camp.CenterMilliY);
            return;
        }

        // Taken in one tick; same-tick takers go by worker id (units act in id order).
        int type = camp.TrophyPrimary > 0 ? Resources.Primary : Resources.Secondary;
        int left = type == Resources.Primary ? camp.TrophyPrimary : camp.TrophySecondary;
        int amount = System.Math.Min(camp.Stats.TrophyPerTrip, left);
        if (amount == 0)
        {
            UnitOrders.Give(unit, UnitOrder.Idle);
            return;
        }
        if (type == Resources.Primary)
            camp.TrophyPrimary -= amount;
        else
            camp.TrophySecondary -= amount;
        worker.CargoType = type;
        worker.Cargo = amount;
        worker.Phase = WorkerPhase.Carrying;
    }

    private static void Dig(SimState state, Worker worker, int resourceType)
    {
        worker.MiningAccumulator++;
        if (worker.MiningAccumulator < state.Rules.ManualMiningTicksPerUnit)
            return;
        worker.MiningAccumulator -= state.Rules.ManualMiningTicksPerUnit;
        worker.CargoType = resourceType;
        worker.Cargo++;
        if (worker.Cargo >= state.Rules.ManualCargo)
        {
            worker.MiningAccumulator = 0;
            worker.Phase = WorkerPhase.Carrying;
        }
    }

    private static CarryResult Carry(SimState state, Unit unit, List<SimEvent> events)
    {
        Worker worker = unit.Worker!;
        Building? hq = DeliveryTarget(state, unit);
        if (hq == null)
        {
            // No headquarters to carry to: wait with the load until one is rebuilt (answer 16).
            UnitOrders.Give(unit, UnitOrder.Idle);
            return CarryResult.Stopped;
        }

        if (Movement.DistanceToRectangle(unit, hq.CellX, hq.CellY, hq.Width, hq.Height) > state.Rules.InteractRangeMilli)
        {
            Movement.StepToRectangle(state, unit, hq.CellX, hq.CellY, hq.Width, hq.Height);
            return CarryResult.Carrying;
        }

        state.GetPlayer(hq.OwnerId).Wallet.Add(worker.CargoType, worker.Cargo);
        events.Add(new CargoDelivered(state.Tick, unit.Id, unit.OwnerId, hq.Id, hq.OwnerId, worker.CargoType, worker.Cargo));
        worker.Cargo = 0;
        worker.CargoType = Worker.NoCargo;
        return CarryResult.Delivered;
    }

    // The chosen headquarters if it still stands, otherwise the nearest own one (ties by id).
    private static Building? DeliveryTarget(SimState state, Unit unit)
    {
        Worker worker = unit.Worker!;
        if (worker.DeliverToId != 0)
        {
            Building? chosen = state.FindBuilding(worker.DeliverToId);
            if (chosen != null && CanDeliverTo(state, unit.OwnerId, chosen))
                return chosen;
        }

        Building? best = null;
        long bestDist = long.MaxValue;
        foreach (Building b in state.Buildings)
        {
            if (b.OwnerId != unit.OwnerId || !CanDeliverTo(state, unit.OwnerId, b))
                continue;
            long d = Movement.DistanceToRectangle(unit, b.CellX, b.CellY, b.Width, b.Height);
            if (d < bestDist)
            {
                bestDist = d;
                best = b;
            }
        }
        return best;
    }

    private static bool InReachOfCell(SimState state, Unit unit, int x, int y) =>
        Movement.DistanceToRectangle(unit, x, y, 1, 1) <= state.Rules.InteractRangeMilli;

    private enum CarryResult
    {
        Carrying,
        Delivered,
        Stopped,
    }
}
