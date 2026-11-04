using System.Collections.Generic;

namespace Game.Sim;

public sealed class Unit : Entity
{
    internal Unit(int id, int ownerId, UnitStats stats, int x, int y)
        : base(id, ownerId, stats.Hp, stats.Armor)
    {
        Stats = stats;
        X = x;
        Y = y;
        if (stats.HasTag(UnitTags.Worker))
            Worker = new Worker();
        _abilityCooldowns = new int[stats.Abilities.Count];
    }

    // factions.md: ticks until each of Stats.Abilities is ready again (0 — ready).
    internal readonly int[] _abilityCooldowns;
    public int AbilityCooldown(int index) => _abilityCooldowns[index];

    // factions.md: a running boost (Overdrive, the Legion's frenzy): ticks left and how much
    // faster the unit moves and attacks meanwhile.
    public int BoostTicksLeft { get; internal set; }
    public int BoostMovePercent { get; internal set; }
    public int BoostAttackPercent { get; internal set; }

    // factions.md, first strike: when the unit last struck.
    public int LastAttackTick { get; internal set; } = NeverTick;

    // Speed and attack cooldown with the boost applied.
    public int MoveSpeedMilliPerTick => BoostTicksLeft > 0
        ? Stats.MoveSpeedMilliPerTick * (100 + BoostMovePercent) / 100
        : Stats.MoveSpeedMilliPerTick;

    public int AttackCooldownTicks => BoostTicksLeft > 0
        ? Stats.AttackCooldownTicks * 100 / (100 + BoostAttackPercent)
        : Stats.AttackCooldownTicks;

    public UnitStats Stats { get; }

    // Position of the unit's center, milli-cells.
    public int X { get; internal set; }
    public int Y { get; internal set; }

    public int Radius => Stats.SizeMilli / 2;

    // Displacement made by the unit's own movement this tick (zero when it stood still).
    // Reset at the start of every tick, so it never carries state between ticks.
    internal int StepX { get; set; }
    internal int StepY { get; set; }

    public UnitOrder Order { get; internal set; } = UnitOrder.Idle;

    // Orders given with Shift, next first (order-queue.md).
    internal readonly List<QueuedOrder> _queue = new List<QueuedOrder>();
    public IReadOnlyList<QueuedOrder> OrderQueue => _queue;

    // Present only on units tagged "worker".
    public Worker? Worker { get; }

    // Ticks until the next attack is allowed.
    public int AttackCooldown { get; internal set; }

    // Current attack target (entity id), 0 when none.
    public int TargetId { get; internal set; }

    // Set for creeps: index of the camp in SimState.CreepCamps.
    public int CampIndex { get; internal set; } = -1;
    public int HomeX { get; internal set; }
    public int HomeY { get; internal set; }

    // Creeps heal while their camp is calm (creep-camp.md, appendix).
    internal int RegenAccumulator { get; set; }

    // builder.md: builders (tag "worker") repair on orders, repairers on their own as well.
    public bool CanRepair => Worker != null || Stats.HasTag(UnitTags.Repairer);
    public bool IsBuilder => Stats.HasTag(UnitTags.Builder);
    public bool IsRepairer => Stats.HasTag(UnitTags.Repairer);

    // Repair: HP accumulator (MaxHp per tick against the target's build work) and cost
    // accumulators per resource (construction.md, "Реализация в Sim"; builder.md for units).
    internal int RepairHpAccumulator { get; set; }
    internal int RepairCostAccumulator { get; set; }
    internal int RepairCostAccumulatorSecondary { get; set; }

    public int CellX => Grid.MilliToCell(X);
    public int CellY => Grid.MilliToCell(Y);
}
