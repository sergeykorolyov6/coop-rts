using System.Collections.Generic;

namespace Game.Sim;

// turret.md: finished turrets pick the nearest visible enemy in range (units first, then
// buildings, ties by id), keep it while it stays valid, and strike every cooldown.
// Runs after the units, so its blows land together with theirs in Combat.ApplyHits.
internal static class TurretSystem
{
    public static void Tick(SimState state)
    {
        IReadOnlyList<Building> buildings = state.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            Building b = buildings[i];
            Turret? turret = b.Turret;
            if (turret == null || b.IsDead)
                continue;
            if (turret.Cooldown > 0)
                turret.Cooldown--;
            if (!b.IsComplete)
            {
                turret.TargetId = 0;
                continue;
            }

            Entity? target = state.FindEntity(turret.TargetId);
            if (!CanShoot(state, b, target))
                target = ChooseTarget(state, b);
            turret.TargetId = target?.Id ?? 0;
            if (target == null || turret.Cooldown > 0)
                continue;
            // factions.md: an ammo turret fires only while its stock pays for the shot.
            if (b.Stats.Ammo > 0 && (b.Stock == null || !b.Stock.TryPay(b.Stats.Ammo, 0)))
                continue;

            state.PendingHits.Add(new PendingHit(b.Id, target.Id, DamageRules.Damage(b.Stats.Damage, target.Armor)));
            turret.Cooldown = b.Stats.AttackCooldownTicks;
        }
    }

    private static Entity? ChooseTarget(SimState state, Building turret)
    {
        Entity? best = null;
        long bestDist = long.MaxValue;
        // Nothing farther than this from the turret's middle is in range: its longest range plus
        // its own footprint. Cheaper than a root for every unit and building of the match.
        int cx = Grid.CellToMilli(turret.CellX) + Grid.CellToMilli(turret.Width) / 2;
        int cy = Grid.CellToMilli(turret.CellY) + Grid.CellToMilli(turret.Height) / 2;
        long reach = turret.Stats.AttackRangeMilli + state.Rules.HighGroundRangeBonusMilli
            + Grid.CellToMilli(System.Math.Max(turret.Width, turret.Height));
        foreach (Unit u in state.Units)
        {
            long near = reach + u.Radius;
            long dx = u.X - cx, dy = u.Y - cy;
            if (dx > near || dx < -near || dy > near || dy < -near || !CanShoot(state, turret, u))
                continue;
            long d = EdgeDistance(turret, u);
            if (d < bestDist)
            {
                best = u;
                bestDist = d;
            }
        }
        if (best != null)
            return best;

        // Only the buckets the range reaches; a building may sit in several of them.
        state.BuildingIndex.Range(cx, cy, reach, out int minBx, out int minBy, out int maxBx, out int maxBy);
        for (int by = minBy; by <= maxBy; by++)
        {
            for (int bx = minBx; bx <= maxBx; bx++)
            {
                foreach (Building b in state.BuildingIndex.Bucket(state, bx, by))
                {
                    if (b == turret || !CanShoot(state, turret, b))
                        continue;
                    long d = EdgeDistance(turret, b);
                    if (d < bestDist || (d == bestDist && b.Id < best!.Id))
                    {
                        best = b;
                        bestDist = d;
                    }
                }
            }
        }
        return best;
    }

    private static bool CanShoot(SimState state, Building turret, Entity? target) =>
        target != null && !target.IsDead
        && state.Relations.IsEnemy(turret.OwnerId, target.OwnerId) && !Combat.IsInvulnerable(target)
        && EdgeDistance(turret, target) <= turret.Stats.AttackRangeMilli + Sight.HighGroundBonus(state, turret, target)
        && Sight.IsVisibleTo(state, turret.OwnerId, target);

    // Edge to edge: from the turret's footprint to a unit's circle or another footprint.
    public static long EdgeDistance(Building turret, Entity target)
    {
        if (target is Unit u)
        {
            long d = Movement.DistanceToBuilding(u.X, u.Y, turret) - u.Radius;
            return d < 0 ? 0 : d;
        }
        var b = (Building)target;
        long dx = Gap(turret.CellX, turret.Width, b.CellX, b.Width);
        long dy = Gap(turret.CellY, turret.Height, b.CellY, b.Height);
        return IntMath.Sqrt(dx * dx + dy * dy);
    }

    // Empty space between two cell spans along one axis, milli-cells.
    private static long Gap(int a, int aSize, int b, int bSize)
    {
        if (b >= a + aSize)
            return Grid.CellToMilli(b - (a + aSize));
        if (a >= b + bSize)
            return Grid.CellToMilli(a - (b + bSize));
        return 0;
    }
}
