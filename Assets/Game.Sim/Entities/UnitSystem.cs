using System.Collections.Generic;

namespace Game.Sim;

// Runs every unit's current order for one tick, in id order.
internal static class UnitSystem
{
    // How far from the move point (in sums of radii) stopped units still count as "the group
    // that already arrived": about 50 units of one cell fit inside.
    private const int GroupArrivalSpread = 4;

    public static void Tick(SimState state, List<SimEvent> events)
    {
        Construction.BeginTick(state);
        IReadOnlyList<Unit> units = state.Units;
        // Index loop: behaviours may spawn units, which land at the end and act next tick.
        int count = units.Count;
        for (int i = 0; i < count; i++)
        {
            units[i].StepX = 0;
            units[i].StepY = 0;
            if (units[i].AttackCooldown > 0)
                units[i].AttackCooldown--;
            AbilitySystem.TickTimers(units[i]);
        }
        for (int i = 0; i < count; i++)
        {
            Unit unit = units[i];
            if (unit.IsDead)
                continue;
            if (unit.CampIndex >= 0)
            {
                CreepCampSystem.TickCreep(state, unit);
                continue;
            }
            if (unit.Order.Kind == UnitOrderKind.Idle && unit._queue.Count > 0)
                UnitOrders.StartNext(state, unit, events);
            switch (unit.Order.Kind)
            {
                case UnitOrderKind.Idle:
                    if (unit.IsRepairer && AutoRepair.TickIdle(state, unit, events))
                        break;
                    Combat.TickIdle(state, unit);
                    break;
                case UnitOrderKind.Attack:
                    Combat.TickAttackOrder(state, unit);
                    break;
                case UnitOrderKind.Move:
                    unit.TargetId = 0;
                    TickMove(state, unit);
                    break;
                case UnitOrderKind.Mine:
                    ManualMining.TickMine(state, unit, events);
                    break;
                case UnitOrderKind.ReturnCargo:
                    ManualMining.TickReturnCargo(state, unit, events);
                    break;
                case UnitOrderKind.Build:
                    Construction.TickBuild(state, unit, events);
                    break;
                case UnitOrderKind.Repair:
                    Construction.TickRepair(state, unit, events);
                    break;
                case UnitOrderKind.AttackMove:
                    Combat.TickAttackMove(state, unit);
                    break;
                case UnitOrderKind.CarryTrophy:
                    ManualMining.TickCarryTrophy(state, unit, events);
                    break;
            }
        }
        Construction.EndTick(state, events);
    }

    private static void TickMove(SimState state, Unit unit)
    {
        int x = unit.Order.X, y = unit.Order.Y;
        switch (Movement.StepToPoint(state, unit, x, y))
        {
            case MoveResult.Arrived:
                unit.Order = UnitOrder.Idle;
                return;
            case MoveResult.Unreachable:
                (int cx, int cy) = Movement.NearestReachableCell(state, unit, x, y);
                if (cx == unit.CellX && cy == unit.CellY)
                    unit.Order = UnitOrder.Idle;
                else
                    unit.Order = UnitOrder.MoveTo(Grid.CellCenter(cx), Grid.CellCenter(cy));
                return;
        }

        // A group sent to one point: whoever bumps into an own unit that already stopped
        // closer to the point stops too, instead of shoving forever (as in SC2).
        long myDist2 = Movement.DistanceSquared(unit.X, unit.Y, x, y);
        foreach (Unit other in state.Units)
        {
            if (other == unit || other.IsDead || other.OwnerId != unit.OwnerId || other.Order.Kind != UnitOrderKind.Idle)
                continue;
            long touch = unit.Radius + other.Radius + unit.MoveSpeedMilliPerTick;
            // Only units standing around the point count, not one idling somewhere on the way.
            long nearTarget = (unit.Radius + other.Radius) * GroupArrivalSpread;
            long otherDist2 = Movement.DistanceSquared(other.X, other.Y, x, y);
            if (otherDist2 <= nearTarget * nearTarget && otherDist2 < myDist2
                && Movement.DistanceSquared(unit.X, unit.Y, other.X, other.Y) <= touch * touch)
            {
                unit.Order = UnitOrder.Idle;
                return;
            }
        }
    }
}
