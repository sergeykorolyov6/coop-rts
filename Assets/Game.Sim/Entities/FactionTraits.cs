using System.Collections.Generic;

namespace Game.Sim;

// factions.md: the passive traits of factions.csv — regeneration out of combat (Red Lightning's
// units, Blue Walls' buildings), the frenzy and the bounty for kills (the Black Legion).
// First strike lives where the blow is struck (Combat.Engage).
internal static class FactionTraits
{
    // pct % of MaxHp per second, exactly: MaxHp·pct per tick against 100·TicksPerSecond.
    private const int Threshold = 100 * Simulation.TicksPerSecond;

    public static void Regenerate(SimState state)
    {
        foreach (Unit u in state.Units)
        {
            // Creeps heal by their camp's own rule (creep-camp.md).
            if (u.IsDead || u.CampIndex >= 0)
                continue;
            FactionStats? f = state.GetPlayer(u.OwnerId).Faction;
            int accumulator = u.RegenAccumulator;
            u.Hp = Heal(state, u, f?.UnitRegenPercentPerSecond ?? 0, f?.RegenDelayTicks ?? 0, ref accumulator);
            u.RegenAccumulator = accumulator;
        }
        foreach (Building b in state.Buildings)
        {
            if (b.IsDead || !b.IsComplete || b.DemolishTicksLeft > 0)
                continue;
            FactionStats? f = state.GetPlayer(b.OwnerId).Faction;
            int accumulator = b.RegenAccumulator;
            b.Hp = Heal(state, b, f?.BuildingRegenPercentPerSecond ?? 0, f?.RegenDelayTicks ?? 0, ref accumulator);
            b.RegenAccumulator = accumulator;
        }
    }

    private static int Heal(SimState state, Entity e, int pct, int delay, ref int accumulator)
    {
        if (pct <= 0 || e.Hp >= e.MaxHp || state.Tick - e.LastDamagedTick < delay)
        {
            accumulator = 0;
            return e.Hp;
        }
        int hp = e.Hp;
        accumulator += e.MaxHp * pct;
        while (accumulator >= Threshold && hp < e.MaxHp)
        {
            accumulator -= Threshold;
            hp++;
        }
        return hp;
    }

    // The killing blow landed: the killer's faction may reward it.
    public static void OnKill(SimState state, int killerId, Entity victim)
    {
        Entity? killer = state.FindEntity(killerId);
        if (killer == null)
            return;
        FactionStats? f = state.GetPlayer(killer.OwnerId).Faction;
        if (f == null)
            return;
        if (killer is Unit unit && !unit.IsDead && f.OnKillAbilityId.Length > 0)
            AbilitySystem.ApplyBoost(unit, state.Data.Ability(f.OnKillAbilityId));
        if (victim is Building b && f.BountyPercent > 0 && state.Relations.IsEnemy(killer.OwnerId, b.OwnerId))
        {
            Wallet wallet = state.GetPlayer(killer.OwnerId).Wallet;
            wallet.Add(Resources.Primary, b.Stats.CostPrimary * f.BountyPercent / 100);
            if (wallet.ResourceTypeCount > Resources.Secondary)
                wallet.Add(Resources.Secondary, b.Stats.CostSecondary * f.BountyPercent / 100);
        }
    }
}
