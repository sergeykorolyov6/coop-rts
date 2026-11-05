using System.Collections.Generic;

namespace Game.Sim;

// factions.md: active abilities (abilities.csv) and the timers they leave on units. Point abilities
// aim within their range (a farther point is pulled in); area ones hit units only, never buildings.
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

    // May the unit use it now: it has it, it is ready, the hero's level and path allow it.
    public static bool IsReady(SimState state, Unit unit, AbilityStats ability)
    {
        int index = IndexOf(unit.Stats, ability.Id);
        return index >= 0 && unit._abilityCooldowns[index] == 0 && HeroSystem.Allows(state, unit, ability);
    }

    // True when the unit used it.
    public static bool TryUse(SimState state, Unit unit, AbilityStats ability, int x, int y, List<SimEvent>? events = null)
    {
        if (!IsReady(state, unit, ability))
            return false;
        (int cx, int cy) = ability.AtPoint ? Aim(unit, ability, x, y) : (unit.X, unit.Y);
        bool used = ability.Kind switch
        {
            AbilityKind.Blink => Blink(state, unit, ability, cx, cy),
            AbilityKind.DamageArea => DamageArea(state, unit, ability, cx, cy),
            AbilityKind.BoostArea => ForUnitsAround(state, unit, cx, cy, ability.RadiusMilli, own: true, u => ApplyBoost(u, ability)),
            AbilityKind.HealArea => ForUnitsAround(state, unit, cx, cy, ability.RadiusMilli, own: false, u => Heal(u, ability.Heal)),
            AbilityKind.Strike => Strike(state, unit, ability, cx, cy),
            _ => PayAndBoost(unit, ability),
        };
        if (!used)
            return false;
        unit._abilityCooldowns[IndexOf(unit.Stats, ability.Id)] = ability.CooldownTicks;
        // After a blink the effect's point is where the unit landed.
        events?.Add(new AbilityUsed(state.Tick, unit.OwnerId, unit.Id, ability.Id,
            ability.Kind == AbilityKind.Blink ? unit.X : cx, ability.Kind == AbilityKind.Blink ? unit.Y : cy));
        return true;
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

    // The point within range, pulled in along the line when farther.
    private static (int X, int Y) Aim(Unit unit, AbilityStats ability, int x, int y)
    {
        long dx = x - unit.X, dy = y - unit.Y;
        long dist = IntMath.Sqrt(dx * dx + dy * dy);
        if (dist <= ability.RangeMilli || dist == 0)
            return (x, y);
        return (unit.X + (int)(dx * ability.RangeMilli / dist), unit.Y + (int)(dy * ability.RangeMilli / dist));
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

    // Every enemy unit within the radius takes the damage (minus armor) with this tick's blows.
    private static bool DamageArea(SimState state, Unit user, AbilityStats ability, int x, int y)
    {
        foreach (Unit u in state.Units)
        {
            if (!u.IsDead && state.Relations.IsEnemy(user.OwnerId, u.OwnerId) && Movement.Distance(x, y, u.X, u.Y) - u.Radius <= ability.RadiusMilli)
                state.PendingHits.Add(new PendingHit(user.Id, u.Id, DamageRules.Damage(ability.Damage, u.Armor)));
        }
        return true;
    }

    // The enemy unit nearest to the point within the radius; none there — nothing used.
    private static bool Strike(SimState state, Unit user, AbilityStats ability, int x, int y)
    {
        Unit? best = null;
        long bestDist = long.MaxValue;
        foreach (Unit u in state.Units)
        {
            if (u.IsDead || !state.Relations.IsEnemy(user.OwnerId, u.OwnerId))
                continue;
            long d = Movement.Distance(x, y, u.X, u.Y) - u.Radius;
            if (d <= ability.RadiusMilli && d < bestDist)
            {
                best = u;
                bestDist = d;
            }
        }
        if (best == null)
            return false;
        state.PendingHits.Add(new PendingHit(user.Id, best.Id, DamageRules.Damage(ability.Damage, best.Armor)));
        return true;
    }

    // Own units only (own: true) or own and allied (own: false), the user too.
    private static bool ForUnitsAround(SimState state, Unit user, int x, int y, int radius, bool own, System.Action<Unit> act)
    {
        foreach (Unit u in state.Units)
        {
            bool mine = own ? u.OwnerId == user.OwnerId : state.Relations.IsAlly(user.OwnerId, u.OwnerId);
            if (!u.IsDead && mine && u.CampIndex < 0 && Movement.Distance(x, y, u.X, u.Y) <= radius)
                act(u);
        }
        return true;
    }

    private static void Heal(Unit u, int amount) => u.Hp = u.Hp + amount > u.MaxHp ? u.MaxHp : u.Hp + amount;

    // To the farthest free cell centre along the line; with damage, the landing hits around (Charge).
    private static bool Blink(SimState state, Unit unit, AbilityStats ability, int x, int y)
    {
        long dx = x - unit.X, dy = y - unit.Y;
        long dist = IntMath.Sqrt(dx * dx + dy * dy);
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
            if (ability.Damage > 0)
                DamageArea(state, unit, ability, tx, ty);
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
