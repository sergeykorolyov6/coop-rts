using System.Collections.Generic;

namespace Game.Sim;

// creep-camp.md: the whole camp wakes at once, creeps fight within their leash,
// go home when nothing is left to reach, and heal only once the camp is calm again.
internal static class CreepCampSystem
{
    // Start of tick, from last tick's positions and blows.
    public static void BeginTick(SimState state, List<SimEvent> events)
    {
        foreach (CreepCamp camp in state.CreepCamps)
        {
            if (camp.State == CreepCampState.Cleared)
                continue;

            int alive = 0;
            foreach (int id in camp._creepIds)
            {
                Unit? creep = state.FindUnit(id);
                if (creep == null || creep.IsDead)
                    continue;
                alive++;
                if (creep.LastDamagedTick >= state.Tick - 1)
                {
                    if (creep.LastDamagedTick > camp.LastDamageTick)
                        camp.LastDamageTick = creep.LastDamagedTick;
                    if (!camp._attackers.Contains(creep.LastAttackerId))
                        camp._attackers.Add(creep.LastAttackerId);
                }
            }

            if (alive == 0)
            {
                camp.State = CreepCampState.Cleared;
                camp.TrophyPrimary = camp.Stats.TrophyPrimary;
                camp.TrophySecondary = camp.Stats.TrophySecondary;
                camp._attackers.Clear();
                events.Add(new CampCleared(state.Tick, camp.Index));
                continue;
            }

            bool hit = camp.LastDamageTick >= state.Tick - 1;
            if (hit || AnyIntruder(state, camp))
            {
                if (camp.State == CreepCampState.Calm)
                    events.Add(new CampAlerted(state.Tick, camp.Index));
                camp.State = CreepCampState.Alerted;
            }
            else if (camp.State == CreepCampState.Returning && AllHome(state, camp)
                     && state.Tick - camp.LastDamageTick >= camp.Stats.CalmDelayTicks)
            {
                camp.State = CreepCampState.Calm;
                camp._attackers.Clear();
            }

            Regenerate(state, camp);
        }
    }

    // After every unit acted: an alerted camp whose creeps found nothing to reach goes home.
    public static void EndTick(SimState state)
    {
        foreach (CreepCamp camp in state.CreepCamps)
        {
            if (camp.State != CreepCampState.Alerted)
                continue;
            bool anyTarget = false;
            foreach (int id in camp._creepIds)
            {
                Unit? creep = state.FindUnit(id);
                anyTarget |= creep != null && !creep.IsDead && creep.TargetId != 0;
            }
            if (!anyTarget)
                camp.State = CreepCampState.Returning;
        }
    }

    public static void TickCreep(SimState state, Unit creep)
    {
        CreepCamp camp = state.CreepCamps[creep.CampIndex];
        if (camp.State == CreepCampState.Alerted)
        {
            Entity? target = ChooseTarget(state, camp, creep);
            if (target != null)
            {
                EngageLeashed(state, creep, target, camp.Stats.LeashRadiusMilli);
                return;
            }
        }

        creep.TargetId = 0;
        if (creep.X != creep.HomeX || creep.Y != creep.HomeY)
            Movement.StepToPoint(state, creep, creep.HomeX, creep.HomeY);
    }

    // Keep the current target while it can be reached; otherwise units first, then buildings,
    // the nearest to the creep, ties by lower id. Candidates: anything foreign in the aggro
    // radius, plus whoever hit the camp.
    private static Entity? ChooseTarget(SimState state, CreepCamp camp, Unit creep)
    {
        Entity? current = state.FindEntity(creep.TargetId);
        if (current != null && !current.IsDead && state.Relations.IsEnemy(creep.OwnerId, current.OwnerId) && !Combat.IsInvulnerable(current) && Reachable(camp, creep, current))
            return current;

        Entity? best = null;
        long bestDist = long.MaxValue;
        foreach (Unit u in state.Units)
        {
            if (u.IsDead || !state.Relations.IsEnemy(creep.OwnerId, u.OwnerId) || !(InRadius(camp, u) || camp._attackers.Contains(u.Id)) || !Reachable(camp, creep, u))
                continue;
            long d = Movement.DistanceSquared(creep.X, creep.Y, u.X, u.Y);
            if (d < bestDist)
            {
                best = u;
                bestDist = d;
            }
        }
        if (best != null)
            return best;

        foreach (Building b in state.Buildings)
        {
            if (b.IsDead || !state.Relations.IsEnemy(creep.OwnerId, b.OwnerId) || b.Stats.Invulnerable || !(InRadius(camp, b) || camp._attackers.Contains(b.Id)) || !Reachable(camp, creep, b))
                continue;
            long d = Movement.DistanceToBuilding(creep.X, creep.Y, b);
            if (d < bestDist)
            {
                best = b;
                bestDist = d;
            }
        }
        return best;
    }

    // Chase and strike, but never step farther than the leash from home.
    private static void EngageLeashed(SimState state, Unit creep, Entity target, int leash)
    {
        int x = creep.X, y = creep.Y;
        Combat.Engage(state, creep, target);
        if (Movement.DistanceSquared(creep.HomeX, creep.HomeY, creep.X, creep.Y) > (long)leash * leash)
        {
            creep.X = x;
            creep.Y = y;
            creep.StepX = 0;
            creep.StepY = 0;
        }
    }

    // The creep could strike it without leaving its leash.
    private static bool Reachable(CreepCamp camp, Unit creep, Entity target)
    {
        long reach = camp.Stats.LeashRadiusMilli + creep.Radius + creep.Stats.AttackRangeMilli;
        if (target is Unit u)
            return Movement.Distance(creep.HomeX, creep.HomeY, u.X, u.Y) - u.Radius <= reach;
        return Movement.DistanceToBuilding(creep.HomeX, creep.HomeY, (Building)target) <= reach;
    }

    private static bool InRadius(CreepCamp camp, Entity e)
    {
        long r = camp.Stats.AggroRadiusMilli;
        if (e is Unit u)
            return Movement.DistanceSquared(camp.CenterMilliX, camp.CenterMilliY, u.X, u.Y) <= r * r;
        return Movement.DistanceToBuilding(camp.CenterMilliX, camp.CenterMilliY, (Building)e) <= r;
    }

    private static bool AnyIntruder(SimState state, CreepCamp camp)
    {
        foreach (Unit u in state.Units)
        {
            if (!u.IsDead && state.Relations.IsEnemy(state.CreepPlayerId, u.OwnerId) && InRadius(camp, u))
                return true;
        }
        foreach (Building b in state.Buildings)
        {
            if (!b.IsDead && state.Relations.IsEnemy(state.CreepPlayerId, b.OwnerId) && !b.Stats.Invulnerable && InRadius(camp, b))
                return true;
        }
        return false;
    }

    // Home is reached exactly by StepToPoint; a little slack covers the crowd pushing creeps.
    private static bool AllHome(SimState state, CreepCamp camp)
    {
        foreach (int id in camp._creepIds)
        {
            Unit? creep = state.FindUnit(id);
            if (creep != null && !creep.IsDead && Movement.DistanceSquared(creep.HomeX, creep.HomeY, creep.X, creep.Y) > HomeSlack * HomeSlack)
                return false;
        }
        return true;
    }

    private const long HomeSlack = Grid.MilliPerCell / 4;

    // pct % of MaxHp per second, exactly: MaxHp·pct per tick against 100·TicksPerSecond.
    private static void Regenerate(SimState state, CreepCamp camp)
    {
        const int threshold = 100 * Simulation.TicksPerSecond;
        foreach (int id in camp._creepIds)
        {
            Unit? creep = state.FindUnit(id);
            if (creep == null || creep.IsDead)
                continue;
            if (camp.State != CreepCampState.Calm || creep.Hp >= creep.MaxHp)
            {
                creep.RegenAccumulator = 0;
                continue;
            }
            creep.RegenAccumulator += creep.MaxHp * camp.Stats.RegenPercentPerSecond;
            while (creep.RegenAccumulator >= threshold && creep.Hp < creep.MaxHp)
            {
                creep.RegenAccumulator -= threshold;
                creep.Hp++;
            }
        }
    }
}
