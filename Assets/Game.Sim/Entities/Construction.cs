using System.Collections.Generic;

namespace Game.Sim;

// construction.md / drill.md: workers walk to a silhouette, pay when they start, build
// together up to MaxBuilders (+100 % each), the site freezes without builders; workers
// repair own and ally buildings for half the build price, paid by the repairer.
internal static class Construction
{
    public const string NeutralFaction = "neutral";

    // Null when the player may place the building there now.
    public static RejectReason? CheckPlacement(SimState state, int playerId, BuildingStats stats, int x, int y, Direction direction)
    {
        MapState map = state.Map;
        // One headquarters at a time for every player; the Legion may have several (answer 6).
        if (stats.Id == BuildingKinds.Headquarters && playerId != state.LegionPlayerId && state.FindHeadquarters(playerId) != null)
            return RejectReason.OneHeadquartersOnly;
        // The Legion's own buildings (its factory) are not for players (question 49);
        // neutral points of interest stand on the map from the start (neutral-buildings.md).
        if (stats.Faction == LegionAiSystem.Faction && playerId != state.LegionPlayerId)
            return RejectReason.NotAllowed;
        if (stats.Faction == NeutralFaction)
            return RejectReason.NotAllowed;
        if (!map.InBounds(x, y) || !map.InBounds(x + stats.SizeW - 1, y + stats.SizeH - 1))
            return RejectReason.NotAllowed;
        if (!state.Fog.IsAreaDiscovered(playerId, x, y, stats.SizeW, stats.SizeH))
            return RejectReason.NotDiscovered;

        bool anyOre = false;
        for (int cy = y; cy < y + stats.SizeH; cy++)
        {
            for (int cx = x; cx < x + stats.SizeW; cx++)
            {
                if (!map.IsBuildable(cx, cy))
                    return RejectReason.PlaceTaken;
                anyOre |= map.OreAt(cx, cy) != MapSetup.NoOre;
            }
        }

        if (stats.Id == BuildingKinds.Drill && !anyOre)
            return RejectReason.NotAllowed;
        return null;
    }

    public static RejectReason? CheckCanPay(SimState state, int playerId, BuildingStats stats)
    {
        return state.GetPlayer(playerId).Wallet.CanPay(stats.CostPrimary, stats.CostSecondary) ? null : RejectReason.NotEnoughResources;
    }

    // Start of tick: who is already on which plan (sticky), so new choices respect MaxBuilders
    // no matter in which order workers act.
    public static void BeginTick(SimState state)
    {
        state.BuildAssignments.Clear();
        foreach (Unit u in state.Units)
        {
            if (!u.IsDead && u.Worker != null && u.Order.Kind == UnitOrderKind.Build && u.Worker.CurrentPlanKey >= 0)
                Assign(state, u.Worker.CurrentPlanKey);
        }
        foreach (Building b in state.Buildings)
            b.WorkersThisTick = 0;
    }

    // End of tick: frozen/unfrozen sites and the builders count for the progress bar.
    public static void EndTick(SimState state, List<SimEvent> events)
    {
        foreach (Building b in state.Buildings)
        {
            if (b.IsDead)
                continue;
            b.BuildersLastTick = b.WorkersThisTick;
            if (b.IsComplete)
                continue;
            if (b.WorkersThisTick == 0 && b.State == BuildState.Constructing)
            {
                b.State = BuildState.Frozen;
                events.Add(new ConstructionFrozen(state.Tick, b.OwnerId, b.Id));
            }
            else if (b.WorkersThisTick > 0 && b.State == BuildState.Frozen)
            {
                b.State = BuildState.Constructing;
            }
        }
    }

    public static void TickBuild(SimState state, Unit unit, List<SimEvent> events)
    {
        Worker worker = unit.Worker!;
        List<PlannedBuild> plans = worker._plans;

        // Finished or destroyed sites drop out; bounded so a long line costs at most one pass.
        for (int guard = plans.Count + 1; guard > 0; guard--)
        {
            PlannedBuild? plan = CurrentPlan(worker) ?? ChoosePlan(state, worker);
            if (plan == null)
            {
                if (plans.Count == 0)
                    unit.Order = UnitOrder.Idle;
                return;  // every remaining site already has enough builders: wait
            }

            BuildingStats stats = state.Data.Building(plan.BuildingTypeId);
            Building? site = ResolveSite(state, unit.OwnerId, plan);
            if (plan.ExistingSiteId != 0 && (site == null || site.IsComplete))
            {
                DropPlan(worker, plan);
                continue;
            }

            if (Movement.DistanceToRectangle(unit, plan.CellX, plan.CellY, stats.SizeW, stats.SizeH) > state.Rules.InteractRangeMilli)
            {
                Movement.StepToRectangle(state, unit, plan.CellX, plan.CellY, stats.SizeW, stats.SizeH);
                return;
            }

            if (site == null)
            {
                // Checked again on arrival: the place may have been taken or the money spent meanwhile.
                // Same-tick starts resolve by worker id: units act in id order (answer 4).
                RejectReason? reason = CheckPlacement(state, unit.OwnerId, stats, plan.CellX, plan.CellY, plan.Direction);
                if (reason == null)
                    reason = CheckCanPay(state, unit.OwnerId, stats);
                if (reason == RejectReason.NotEnoughResources)
                {
                    events.Add(new CommandRejected(state.Tick, unit.OwnerId, reason.Value, unit.Id));
                    // The worker stops; its silhouettes stay until another order (conveyors.md, fail cases).
                    unit.Order = UnitOrder.Idle;
                    worker.CurrentPlanKey = -1;
                    return;
                }
                if (reason != null)
                {
                    events.Add(new CommandRejected(state.Tick, unit.OwnerId, reason.Value, unit.Id));
                    DropPlan(worker, plan);
                    if (plans.Count == 0)
                        unit.Order = UnitOrder.Idle;
                    return;
                }

                Wallet wallet = state.GetPlayer(unit.OwnerId).Wallet;
                wallet.TryPay(stats.CostPrimary, stats.CostSecondary);
                site = state.PlaceBuilding(unit.OwnerId, stats, plan.CellX, plan.CellY, plan.Direction, complete: false);
                plan.ExistingSiteId = site.Id;
            }

            if (site.WorkersThisTick >= stats.MaxBuilders)
                return;
            site.WorkersThisTick++;
            site.AddWork(1);
            if (site.IsComplete)
            {
                events.Add(new ConstructionCompleted(state.Tick, site.OwnerId, site.Id));
                DropPlan(worker, plan);
                if (plans.Count == 0)
                    unit.Order = UnitOrder.Idle;
            }
            return;
        }
    }

    // A target worth repairing by this unit now: own or ally, alive, damaged; buildings finished;
    // a unit never repairs itself (builder.md).
    public static bool IsRepairable(SimState state, Unit unit, Entity? target)
    {
        if (target == null || target.IsDead || target == unit || target.Hp >= target.MaxHp || !state.Relations.IsAlly(unit.OwnerId, target.OwnerId))
            return false;
        if (target is Building b)
            return b.IsComplete && !b.Stats.Invulnerable;
        return RepairWork((Unit)target) > 0;
    }

    public static void TickRepair(SimState state, Unit unit, List<SimEvent> events)
    {
        Entity? target = state.FindEntity(unit.Order.TargetId);
        if (!IsRepairable(state, unit, target))
        {
            UnitOrders.Give(unit, UnitOrder.Idle);
            return;
        }

        if (target is Building b)
        {
            if (Movement.DistanceToRectangle(unit, b.CellX, b.CellY, b.Width, b.Height) > state.Rules.InteractRangeMilli)
            {
                Movement.StepToRectangle(state, unit, b.CellX, b.CellY, b.Width, b.Height);
                return;
            }
            if (b.WorkersThisTick >= b.Stats.MaxBuilders)
                return;
            b.WorkersThisTick++;
        }
        else
        {
            var patient = (Unit)target!;
            long gap = Movement.Distance(unit.X, unit.Y, patient.X, patient.Y) - unit.Radius - patient.Radius;
            if (gap > state.Rules.InteractRangeMilli)
            {
                Movement.StepToPoint(state, unit, patient.X, patient.Y);
                return;
            }
        }

        int work = target is Building site ? site.BuildWork : RepairWork((Unit)target!);
        if (work == 0)
        {
            target!.Hp = target.MaxHp;
        }
        else
        {
            // One repairer restores MaxHp over the build time: MaxHp / work HP per tick, exactly.
            unit.RepairHpAccumulator += target!.MaxHp;
            while (unit.RepairHpAccumulator >= work && target.Hp < target.MaxHp)
            {
                if (!PayForOneHp(state, unit, target))
                {
                    if (!unit.Order.IsAuto)
                        events.Add(new CommandRejected(state.Tick, unit.OwnerId, RejectReason.NotEnoughResources, unit.Id));
                    UnitOrders.Give(unit, UnitOrder.Idle);
                    return;
                }
                unit.RepairHpAccumulator -= work;
                target.Hp++;
            }
        }

        if (target.Hp >= target.MaxHp)
            UnitOrders.Give(unit, UnitOrder.Idle);
    }

    // builder.md: a unit is repaired over its own build time.
    private static int RepairWork(Unit target) => target.Stats.BuildTimeTicks;

    private static (int Primary, int Secondary) Cost(Entity target) =>
        target is Building b ? (b.Stats.CostPrimary, b.Stats.CostSecondary) : (((Unit)target).Stats.CostPrimary, ((Unit)target).Stats.CostSecondary);

    // The repairer's player can pay for at least the next unit of every resource the repair uses.
    public static bool CanAffordRepair(SimState state, Unit unit, Entity target)
    {
        if (state.Rules.RepairCostPercent == 0)
            return true;
        (int primary, int secondary) = Cost(target);
        Wallet wallet = state.GetPlayer(unit.OwnerId).Wallet;
        return wallet.CanPay(primary > 0 ? 1 : 0, secondary > 0 ? 1 : 0);
    }

    // Each HP adds the build price to the repairer's counter; every MaxHp × 100 / pct of it
    // costs one unit (construction.md: 30 per HP, 1 unit per 600 for a drill at 50 %).
    private static bool PayForOneHp(SimState state, Unit unit, Entity target)
    {
        int pct = state.Rules.RepairCostPercent;
        if (pct == 0)
            return true;
        int threshold = (int)((long)target.MaxHp * 100 / pct);
        if (threshold <= 0)
            return true;

        (int costPrimary, int costSecondary) = Cost(target);
        int primary = unit.RepairCostAccumulator + costPrimary;
        int secondary = unit.RepairCostAccumulatorSecondary + costSecondary;
        int duePrimary = primary / threshold, dueSecondary = secondary / threshold;

        if (!state.GetPlayer(unit.OwnerId).Wallet.TryPay(duePrimary, dueSecondary))
            return false;
        unit.RepairCostAccumulator = primary - duePrimary * threshold;
        unit.RepairCostAccumulatorSecondary = secondary - dueSecondary * threshold;
        return true;
    }

    private static PlannedBuild? CurrentPlan(Worker worker)
    {
        if (worker.CurrentPlanKey < 0)
            return null;
        foreach (PlannedBuild p in worker._plans)
        {
            if (PlanKey(p) == worker.CurrentPlanKey)
                return p;
        }
        worker.CurrentPlanKey = -1;
        return null;
    }

    // The first plan in line that still has room for a builder.
    private static PlannedBuild? ChoosePlan(SimState state, Worker worker)
    {
        foreach (PlannedBuild p in worker._plans)
        {
            int key = PlanKey(p);
            state.BuildAssignments.TryGetValue(key, out int assigned);
            if (assigned < state.Data.Building(p.BuildingTypeId).MaxBuilders)
            {
                Assign(state, key);
                worker.CurrentPlanKey = key;
                return p;
            }
        }
        return null;
    }

    // A site another own or ally worker already started on this silhouette counts as ours to help with.
    private static Building? ResolveSite(SimState state, int playerId, PlannedBuild plan)
    {
        if (plan.ExistingSiteId != 0)
        {
            Building? known = state.FindBuilding(plan.ExistingSiteId);
            return known == null || known.IsDead ? null : known;
        }
        Building? there = state.FindBuilding(state.Map.BuildingAt(plan.CellX, plan.CellY));
        // Finished ones count too: the caller then drops the plan as done instead of reporting "place taken".
        if (there != null && !there.IsDead && there.CellX == plan.CellX && there.CellY == plan.CellY
            && there.Stats.Id == plan.BuildingTypeId && state.Relations.IsAlly(playerId, there.OwnerId))
        {
            plan.ExistingSiteId = there.Id;
            return there;
        }
        return null;
    }

    private static void DropPlan(Worker worker, PlannedBuild plan)
    {
        worker._plans.Remove(plan);
        if (worker.CurrentPlanKey == PlanKey(plan))
            worker.CurrentPlanKey = -1;
    }

    private static void Assign(SimState state, int key)
    {
        state.BuildAssignments.TryGetValue(key, out int count);
        state.BuildAssignments[key] = count + 1;
    }

    // Sites never overlap, so the bottom-left cell identifies a plan; 16 bits per axis is plenty.
    private static int PlanKey(PlannedBuild p) => (p.CellY << 16) | p.CellX;
}
