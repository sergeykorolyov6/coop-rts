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

            state.PendingHits.Add(new PendingHit(b.Id, target.Id, DamageRules.Damage(b.Stats.Damage, target.Armor)));
            turret.Cooldown = b.Stats.AttackCooldownTicks;
        }
    }

    private static Entity? ChooseTarget(SimState state, Building turret)
    {
        Entity? best = null;
        long bestDist = long.MaxValue;
        foreach (Unit u in state.Units)
        {
            if (!CanShoot(state, turret, u))
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
        foreach (Building b in state.Buildings)
        {
            if (b == turret || !CanShoot(state, turret, b))
                continue;
            long d = EdgeDistance(turret, b);
            if (d < bestDist)
            {
                best = b;
                bestDist = d;
            }
        }
        return best;
    }

    private static bool CanShoot(SimState state, Building turret, Entity? target) =>
        target != null && !target.IsDead
        && state.Relations.IsEnemy(turret.OwnerId, target.OwnerId)
        && EdgeDistance(turret, target) <= turret.Stats.AttackRangeMilli
        && IsVisible(state, turret.OwnerId, target);

    // Never into the fog: the cell a unit stands in, or any cell of a building, is seen by the team.
    private static bool IsVisible(SimState state, int playerId, Entity target)
    {
        if (target is Unit u)
            return state.Map.InBounds(u.CellX, u.CellY) && state.Fog.IsVisible(playerId, u.CellX, u.CellY);
        var b = (Building)target;
        for (int y = b.CellY; y < b.CellY + b.Height; y++)
        {
            for (int x = b.CellX; x < b.CellX + b.Width; x++)
            {
                if (state.Fog.IsVisible(playerId, x, y))
                    return true;
            }
        }
        return false;
    }

    // Edge to edge: from the turret's footprint to a unit's circle or another footprint.
    public static long EdgeDistance(Building turret, Entity target)
    {
        if (target is Unit u)
        {
            long d = Combat.DistanceToRectangle(u.X, u.Y, turret) - u.Radius;
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
