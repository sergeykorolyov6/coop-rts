namespace Game.Sim;

internal static class UnitOrders
{
    // A new order replaces whatever the unit was doing. Like SC2, a worker drops its
    // not-yet-started silhouettes; sites already started stay and freeze (drill.md).
    public static void Give(Unit unit, UnitOrder order)
    {
        unit.Order = order;
        unit.TargetId = 0;
        Worker? worker = unit.Worker;
        if (worker != null)
        {
            worker._plans.Clear();
            worker.Phase = WorkerPhase.None;
            worker.CurrentPlanKey = -1;
            worker.RepairHpAccumulator = 0;
        }
    }
}
