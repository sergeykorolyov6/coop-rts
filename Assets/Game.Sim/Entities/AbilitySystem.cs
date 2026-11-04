using System.Collections.Generic;

namespace Game.Sim;

// factions.md: active abilities — Blink (to a point within range), Boost (a timed buff paid
// with HP), Nova (damage around) and Inspire (a boost to own units around) — and their timers.
internal static class AbilitySystem
{
    public static void TickTimers(Unit unit)
    {
        int[] cooldowns = unit._abilityCooldowns;
        for (int i = 0; i < cooldowns.Length; i++)
        {
            if (cooldowns[i] > 0)
                cooldowns[i]--;
        }
        if (unit.BoostTicksLeft > 0 && --unit.BoostTicksLeft == 0)
        {
            unit.BoostMovePercent = 0;
            unit.BoostAttackPercent = 0;
        }
    }

    // True when the unit used it: it has the ability, it is ready, and the use is possible.
    public static bool TryUse(SimState state, Unit unit, AbilityStats ability, int x, int y)
    {
        int index = IndexOf(unit.Stats, ability.Id);
        if (index < 0 || unit._abilityCooldowns[index] > 0 || HeroSystem.LevelOf(state, unit) < ability.MinLevel)
            return false;
        bool used = ability.Kind switch
        {
            AbilityKind.Blink => Blink(state, unit, ability, x, y),
            AbilityKind.Nova => Nova(state, unit, ability),
            AbilityKind.Inspire => Inspire(state, unit, ability),
            _ => PayAndBoost(unit, ability),
        };
        if (used)
            unit._abilityCooldowns[index] = ability.CooldownTicks;
        return used;
    }

    // A longer boost replaces a shorter one; they never add up.
    public static void ApplyBoost(Unit unit, AbilityStats ability)
    {
        if (unit.BoostTicksLeft > ability.DurationTicks)
            return;
        unit.BoostTicksLeft = ability.DurationTicks;
        unit.BoostMovePercent = ability.MoveSpeedPercent;
        unit.BoostAttackPercent = ability.AttackSpeedPercent;
    }

    // Every enemy unit within range takes the damage (minus armor) with this tick's blows.
    private static bool Nova(SimState state, Unit user, AbilityStats ability)
    {
        foreach (Unit u in state.Units)
        {
            if (!u.IsDead && state.Relations.IsEnemy(user.OwnerId, u.OwnerId)
                && Movement.Distance(user.X, user.Y, u.X, u.Y) - u.Radius <= ability.RangeMilli)
                state.PendingHits.Add(new PendingHit(user.Id, u.Id, DamageRules.Damage(ability.Damage, u.Armor)));
        }
        return true;
    }

    // Every own unit within range, the user too, gets the boost.
    private static bool Inspire(SimState state, Unit user, AbilityStats ability)
    {
        foreach (Unit u in state.Units)
        {
            if (!u.IsDead && u.OwnerId == user.OwnerId && Movement.Distance(user.X, user.Y, u.X, u.Y) <= ability.RangeMilli)
                ApplyBoost(u, ability);
        }
        return true;
    }

    private static bool PayAndBoost(Unit unit, AbilityStats ability)
    {
        int cost = unit.MaxHp * ability.HpCostPercent / 100;
        if (cost > 0 && unit.Hp <= cost)
            return false;
        unit.Hp -= cost;
        ApplyBoost(unit, ability);
        return true;
    }

    // Towards the point, at most the range; onto the farthest free cell centre along the line.
    private static bool Blink(SimState state, Unit unit, AbilityStats ability, int x, int y)
    {
        long dx = x - unit.X, dy = y - unit.Y;
        long dist = IntMath.Sqrt(dx * dx + dy * dy);
        if (dist > ability.RangeMilli)
        {
            dx = dx * ability.RangeMilli / dist;
            dy = dy * ability.RangeMilli / dist;
            dist = ability.RangeMilli;
        }
        // Back along the line a quarter cell at a time until the spot is free.
        const int step = Grid.MilliPerCell / 4;
        for (long d = dist; d > 0; d -= step)
        {
            int tx = unit.X + (int)(dx * d / dist), ty = unit.Y + (int)(dy * d / dist);
            int cx = Grid.MilliToCell(tx), cy = Grid.MilliToCell(ty);
            if (!state.Map.InBounds(cx, cy) || !state.Map.IsPassable(cx, cy))
                continue;
            unit.X = tx;
            unit.Y = ty;
            unit.StepX = 0;
            unit.StepY = 0;
            return true;
        }
        return false;
    }

    private static int IndexOf(UnitStats stats, string abilityId)
    {
        IReadOnlyList<string> list = stats.Abilities;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == abilityId)
                return i;
        }
        return -1;
    }
}
