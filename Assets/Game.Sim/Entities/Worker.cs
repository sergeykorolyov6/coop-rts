using System.Collections.Generic;

namespace Game.Sim;

// Worker-only state of a unit: what it carries and what it plans to build.
public sealed class Worker
{
    public const int NoCargo = -1;

    internal readonly List<PlannedBuild> _plans = new List<PlannedBuild>();

    // Resource type of the cargo, or NoCargo.
    public int CargoType { get; internal set; } = NoCargo;
    public int Cargo { get; internal set; }

    // Integer accumulator for digging speed (manual-mining.md, appendix).
    internal int MiningAccumulator { get; set; }

    // Headquarters chosen to deliver to (an ally's, by right-click), 0 for the nearest own one.
    public int DeliverToId { get; internal set; }

    public WorkerPhase Phase { get; internal set; }

    // Planned and started construction sites, built in order (drill.md: "Запланирован").
    public IReadOnlyList<PlannedBuild> Plans => _plans;

    // Repair cost accumulator of this worker (construction.md, "Реализация в Sim").
    internal int RepairCostAccumulator { get; set; }
}
