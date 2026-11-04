using System.Collections.Generic;

namespace Game.Sim;

// demolish.md: a building being demolished goes after demolish_time_pct of its build time and
// gives demolish_refund_pct of its price back (nothing for belts); destroyed first — no refund.
internal static class DemolishSystem
{
    public static int DemolishTicks(SimState state, BuildingStats stats)
    {
        int ticks = (int)(((long)stats.BuildTimeTicks * state.Rules.DemolishTimePercent + 99) / 100);
        return ticks < 1 ? 1 : ticks;
    }

    public static bool Refunds(BuildingStats stats) => !BuildingKinds.IsWalkable(stats.Id);

    public static void Tick(SimState state, List<SimEvent> events)
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
            if (Refunds(b.Stats))
            {
                int pct = state.Rules.DemolishRefundPercent;
                Wallet wallet = state.GetPlayer(b.OwnerId).Wallet;
                e.RefundPrimary = b.Stats.CostPrimary * pct / 100;
                e.RefundSecondary = wallet.ResourceTypeCount > Resources.Secondary ? b.Stats.CostSecondary * pct / 100 : 0;
                if (e.RefundPrimary > 0)
                    wallet.Add(Resources.Primary, e.RefundPrimary);
                if (e.RefundSecondary > 0)
                    wallet.Add(Resources.Secondary, e.RefundSecondary);
            }
            events.Add(e);
        }
    }
}
