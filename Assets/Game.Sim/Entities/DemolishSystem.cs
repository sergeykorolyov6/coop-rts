using System.Collections.Generic;

namespace Game.Sim;

// demolish.md: a building being demolished goes after demolish_time_pct of its build time and
// gives demolish_refund_pct of its price back (nothing for belts); destroyed first — no refund.
public static class DemolishSystem
{
    // Rounded up, at least one tick: a conveyor's 10 ticks give 3.
    public static int DemolishTicks(GameRules rules, BuildingStats stats)
    {
        int ticks = (int)(((long)stats.BuildTimeTicks * rules.DemolishTimePercent + 99) / 100);
        return ticks < 1 ? 1 : ticks;
    }

    // Rounded down per resource; belts (conveyors, bridge ends) give nothing back.
    public static (int Primary, int Secondary) Refund(GameRules rules, BuildingStats stats)
    {
        if (stats.IsBelt)
            return (0, 0);
        int pct = rules.DemolishRefundPercent;
        return (stats.CostPrimary * pct / 100, stats.CostSecondary * pct / 100);
    }

    internal static void Tick(SimState state, List<SimEvent> events)
    {
        foreach (Building b in state.Buildings)
        {
            if (b.IsDead || b.DemolishTicksLeft == 0)
                continue;
            b.DemolishTicksLeft--;
            if (b.DemolishTicksLeft > 0)
                continue;

            b.IsDead = true;
            var e = new EntityDestroyed(state.Tick, b.OwnerId, b.Id, isBuilding: true, demolished: true, typeId: b.Stats.Id);
            (int primary, int secondary) = Refund(state.Rules, b.Stats);
            Wallet wallet = state.GetPlayer(b.OwnerId).Wallet;
            e.RefundPrimary = primary;
            e.RefundSecondary = wallet.ResourceTypeCount > Resources.Secondary ? secondary : 0;
            if (e.RefundPrimary > 0)
                wallet.Add(Resources.Primary, e.RefundPrimary);
            if (e.RefundSecondary > 0)
                wallet.Add(Resources.Secondary, e.RefundSecondary);
            events.Add(e);
        }
    }
}
