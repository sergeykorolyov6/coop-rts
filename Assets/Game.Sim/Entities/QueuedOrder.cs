using System.Collections.Generic;

namespace Game.Sim;

// An order given with Shift, waiting for the unit's current order to end (order-queue.md).
// A build order carries its own silhouettes: they join the worker's plans when it starts.
public sealed class QueuedOrder
{
    private static readonly PlannedBuild[] NoPlans = new PlannedBuild[0];

    private readonly PlannedBuild[] _plans;

    internal QueuedOrder(UnitOrder order, PlannedBuild[]? plans)
    {
        Order = order;
        _plans = plans ?? NoPlans;
    }

    public UnitOrder Order { get; }
    public IReadOnlyList<PlannedBuild> Plans => _plans;
}
