using System.Collections.Generic;

namespace Game.Sim;

// combat.md: chase into range, strike every cooldown, idle units pick the nearest enemy in sight.
internal static class Combat
{
    public static bool CanAttack(Unit unit) => unit.Stats.Damage > 0 || unit.Stats.DamageVsBuildings > 0;

    // Edge to edge: circles for units, the footprint for buildings.
    public static long EdgeDistance(Unit unit, Entity target)
    {
        if (target is Unit u)
        {
            long d = Movement.Distance(unit.X, unit.Y, u.X, u.Y) - unit.Radius - u.Radius;
            return d < 0 ? 0 : d;
        }
        var b = (Building)target;
        long r = Movement.DistanceToRectangle(unit, b.CellX, b.CellY, b.Width, b.Height) - unit.Radius;
        return r < 0 ? 0 : r;
    }

    public static bool IsValidTarget(SimState state, Unit unit, Entity? target) =>
        target != null && !target.IsDead && state.Relations.IsEnemy(unit.OwnerId, target.OwnerId) && !IsInvulnerable(target);

    // neutral-buildings.md: caves and lighthouses take no damage, whoever owns them.
    public static bool IsInvulnerable(Entity target) => target is Building b && b.Stats.Invulnerable;

    // One tick against a target: step closer, or strike when in range and ready.
    public static void Engage(SimState state, Unit unit, Entity target)
    {
        unit.TargetId = target.Id;
        if (EdgeDistance(unit, target) <= unit.Stats.AttackRangeMilli)
        {
            if (unit.AttackCooldown == 0)
            {
                int raw = target is Building ? unit.Stats.DamageVsBuildings : unit.Stats.Damage;
                // factions.md, first strike: a blow after a pause hits harder (Red Lightning).
                FactionStats? faction = state.GetPlayer(unit.OwnerId).Faction;
                if (faction != null && faction.FirstStrikePercent != 100 && state.Tick - unit.LastAttackTick >= faction.FirstStrikeDelayTicks)
                    raw = raw * faction.FirstStrikePercent / 100;
                unit.LastAttackTick = state.Tick;
                state.PendingHits.Add(new PendingHit(unit.Id, target.Id, DamageRules.Damage(raw, target.Armor)));
                unit.AttackCooldown = unit.AttackCooldownTicks;
            }
            return;
        }

        if (target is Unit u)
            Movement.StepToPoint(state, unit, u.X, u.Y);
        else
        {
            var b = (Building)target;
            Movement.StepToRectangle(state, unit, b.CellX, b.CellY, b.Width, b.Height);
        }
    }

    public static void TickAttackOrder(SimState state, Unit unit)
    {
        Entity? target = state.FindEntity(unit.Order.TargetId);
        if (!IsValidTarget(state, unit, target))
        {
            // Target gone: behave as idle, which looks for the next enemy nearby.
            unit.Order = UnitOrder.Idle;
            unit.TargetId = 0;
            TickIdle(state, unit);
            return;
        }
        Engage(state, unit, target!);
    }

    // legion-ai.md: fight the enemy in sight (kept while it stays in sight), else walk on;
    // at the point the unit stops and behaves as idle.
    public static void TickAttackMove(SimState state, Unit unit)
    {
        Entity? target = state.FindEntity(unit.TargetId);
        if (!IsValidTarget(state, unit, target) || EdgeDistance(unit, target!) > unit.Stats.VisionMilli)
            target = CanAttack(unit) ? NearestEnemy(state, unit, unit.X, unit.Y, unit.Stats.VisionMilli) : null;
        if (target != null)
        {
            Engage(state, unit, target);
            return;
        }

        unit.TargetId = 0;
        int x = unit.Order.X, y = unit.Order.Y;
        switch (Movement.StepToPoint(state, unit, x, y))
        {
            case MoveResult.Arrived:
                unit.Order = UnitOrder.Idle;
                return;
            case MoveResult.Unreachable:
                (int cx, int cy) = Movement.NearestReachableCell(state, unit, x, y);
                unit.Order = cx == unit.CellX && cy == unit.CellY
                    ? UnitOrder.Idle
                    : UnitOrder.AttackMoveTo(Grid.CellCenter(cx), Grid.CellCenter(cy));
                return;
        }
    }

    // Idle fighters (not workers) answer enemies within their own sight; a target that
    // leaves it is dropped and the unit stays where it is.
    public static void TickIdle(SimState state, Unit unit)
    {
        if (unit.Worker != null || !CanAttack(unit))
            return;

        Entity? target = state.FindEntity(unit.TargetId);
        if (!IsValidTarget(state, unit, target) || EdgeDistance(unit, target!) > unit.Stats.VisionMilli)
            target = NearestEnemy(state, unit, unit.X, unit.Y, unit.Stats.VisionMilli);
        if (target == null)
        {
            unit.TargetId = 0;
            return;
        }
        Engage(state, unit, target);
    }

    // Units first, then buildings; nearest edge distance; ties by lower id.
    public static Entity? NearestEnemy(SimState state, Unit unit, int x, int y, long radius)
    {
        Entity? best = null;
        long bestDist = long.MaxValue;
        foreach (Unit u in state.Units)
        {
            if (u == unit || !IsValidTarget(state, unit, u))
                continue;
            long d = Movement.Distance(x, y, u.X, u.Y) - u.Radius;
            if (d <= radius && d < bestDist)
            {
                best = u;
                bestDist = d;
            }
        }
        if (best != null)
            return best;

        foreach (Building b in state.Buildings)
        {
            if (!IsValidTarget(state, unit, b))
                continue;
            long d = DistanceToRectangle(x, y, b);
            if (d <= radius && d < bestDist)
            {
                best = b;
                bestDist = d;
            }
        }
        return best;
    }

    public static long DistanceToRectangle(int x, int y, Building b)
    {
        long minX = Grid.CellToMilli(b.CellX), maxX = Grid.CellToMilli(b.CellX + b.Width);
        long minY = Grid.CellToMilli(b.CellY), maxY = Grid.CellToMilli(b.CellY + b.Height);
        long dx = x < minX ? minX - x : x > maxX ? x - maxX : 0;
        long dy = y < minY ? minY - y : y > maxY ? y - maxY : 0;
        return IntMath.Sqrt(dx * dx + dy * dy);
    }

    // All blows of the tick at once, in attacker id order: two units can kill each other.
    public static void ApplyHits(SimState state, List<SimEvent> events)
    {
        foreach (PendingHit hit in state.PendingHits)
        {
            Entity? target = state.FindEntity(hit.TargetId);
            if (target == null || target.IsDead || IsInvulnerable(target))
                continue;
            target.Hp -= hit.Amount;
            target.LastDamagedTick = state.Tick;
            target.LastAttackerId = hit.AttackerId;
            events.Add(new HitLanded(state.Tick, hit.AttackerId, target.Id, hit.Amount));

            if (state.Tick - target.LastAlarmTick >= state.Rules.AttackAlarmCooldownTicks)
            {
                target.LastAlarmTick = state.Tick;
                if (target is Building b)
                    events.Add(new BuildingAttacked(state.Tick, b.OwnerId, b.Id, hit.AttackerId));
                else
                    events.Add(new UnitAttacked(state.Tick, target.OwnerId, target.Id, hit.AttackerId));
            }
            if (target.Hp <= 0)
            {
                target.Hp = 0;
                target.IsDead = true;
                FactionTraits.OnKill(state, hit.AttackerId, target);
                events.Add(new EntityDestroyed(state.Tick, target.OwnerId, target.Id, target is Building, typeId: target is Building tb ? tb.Stats.Id : ((Unit)target).Stats.Id));
            }
        }
        state.PendingHits.Clear();
    }
}
