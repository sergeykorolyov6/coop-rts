namespace Game.Sim;

// builder.md: an idle repairer looks around every ScanInterval ticks for the nearest damaged own
// or ally building or unit within auto_repair_radius and goes to repair it.
internal static class AutoRepair
{
    private const int ScanInterval = 10;

    public static bool TickIdle(SimState state, Unit unit, System.Collections.Generic.List<SimEvent> events)
    {
        if ((state.Tick + unit.Id) % ScanInterval != 0)
            return false;
        Entity? target = Nearest(state, unit);
        if (target == null)
            return false;
        UnitOrders.Give(unit, UnitOrder.Repair(target.Id, isAuto: true));
        Construction.TickRepair(state, unit, events);
        return true;
    }

    // Edge distance; ties by lower id: units first in the walk, then buildings, so equal
    // distances keep the order of ids within each list (both are sorted by id).
    private static Entity? Nearest(SimState state, Unit unit)
    {
        long radius = state.Rules.AutoRepairRadiusMilli;
        Entity? best = null;
        long bestDist = long.MaxValue;
        foreach (Unit u in state.Units)
            Consider(u);
        foreach (Building b in state.Buildings)
            Consider(b);
        return best;

        void Consider(Entity e)
        {
            if (!Construction.IsRepairable(state, unit, e) || !Construction.CanAffordRepair(state, unit, e))
                return;
            long d = Combat.EdgeDistance(unit, e);
            if (d > radius || d > bestDist || (d == bestDist && best != null && best.Id < e.Id))
                return;
            best = e;
            bestDist = d;
        }
    }
}
