using System.Collections.Generic;

namespace Game.Sim;

// Who may hand a resource unit to whom (conveyors.md):
//  - drills give to own or ally conveyors, bridge inputs, storages and headquarters;
//  - storages give to own or ally conveyors and bridge inputs only;
//  - conveyors and bridge outputs give to own conveyors and bridge inputs (never someone
//    else's, even an ally's: answer 14), and to own or ally storages and headquarters.
// A conveyor never takes from the building in front of it; a headquarters credits its owner.
internal static class Logistics
{
    public enum Giver
    {
        Drill,
        Storage,
        Belt,
    }

    private static readonly List<Building> PassTo = new List<Building>();

    // One unit on to the next neighbouring belt or bridge input that takes it, round-robin, as a
    // storage gives (own or ally; the belt that brought it looks into the building and refuses).
    private static bool PassOn(SimState state, Building b, int resourceType)
    {
        Neighbours(state, b, PassTo);
        int n = PassTo.Count;
        for (int k = 0; k < n; k++)
        {
            int index = (b.PassCursor + k) % n;
            Building next = PassTo[index];
            if (next.Conveyor == null && next.Bridge == null)
                continue;
            if (TryGive(state, b, Giver.Storage, next, resourceType))
            {
                b.PassCursor = (index + 1) % n;
                return true;
            }
        }
        return false;
    }

    // Neighbours sharing an edge with the footprint, in a fixed order (bottom row left to right,
    // right column up, top row right to left, left column down), each building once.
    public static void Neighbours(SimState state, Building b, List<Building> result)
    {
        result.Clear();
        MapState map = state.Map;
        int x0 = b.CellX, y0 = b.CellY, x1 = b.CellX + b.Width - 1, y1 = b.CellY + b.Height - 1;
        for (int x = x0; x <= x1; x++) Add(x, y0 - 1);
        for (int y = y0; y <= y1; y++) Add(x1 + 1, y);
        for (int x = x1; x >= x0; x--) Add(x, y1 + 1);
        for (int y = y1; y >= y0; y--) Add(x0 - 1, y);

        void Add(int x, int y)
        {
            if (!map.InBounds(x, y))
                return;
            Building? n = state.FindBuilding(map.BuildingAt(x, y));
            if (n != null && !n.IsDead && !result.Contains(n))
                result.Add(n);
        }
    }

    // Whether the receiver ever takes from this giver: who they are, where they stand, which way
    // they look — whatever room it has right now. TryGive asks first; the UI asks to draw the links.
    internal static bool Connects(SimState state, Building giver, Giver kind, Building receiver)
    {
        if (receiver.IsDead || !receiver.IsComplete)
            return false;
        // heights.md: a resource passes between neighbours only where a unit could step; a bridge
        // links its two ends by itself and so carries over a cliff.
        if (!state.Map.LevelsJoin(giver.CellX, giver.CellY, receiver.CellX, receiver.CellY))
            return false;
        if (!state.Relations.IsAlly(giver.OwnerId, receiver.OwnerId))
            return false;
        // factory.md M2: a factory, cave or ammo turret supplied by conveyor takes from belts, drills
        // and storages, own or ally.
        if (receiver.Stock != null)
            return true;

        // A belt never feeds someone else's belt or bridge, even an ally's (answer 14).
        bool foreignBelt = kind == Giver.Belt && giver.OwnerId != receiver.OwnerId;
        switch (receiver.Stats.Behavior)
        {
            case BuildingBehavior.Headquarters:
            case BuildingBehavior.Storage:
                return kind != Giver.Storage;
            case BuildingBehavior.Conveyor:
                // Never from the building it looks into.
                return !foreignBelt && !giver.OccupiesCell(receiver.CellX + receiver.Direction.Dx(), receiver.CellY + receiver.Direction.Dy());
            case BuildingBehavior.Bridge:
                return !foreignBelt && ConveyorSystem.BridgeTakesFrom(state, giver, receiver);
        }
        return false;
    }

    // Hands one unit over if the receiver takes it now. A conveyor here takes only from buildings
    // (drill, storage, bridge output, a stock passing on) with its cell free; belt to belt goes
    // through ConveyorSystem and its turns (belt-merge.md).
    public static bool TryGive(SimState state, Building giver, Giver kind, Building receiver, int resourceType)
    {
        if (!Connects(state, giver, kind, receiver))
            return false;

        // A stock takes up to factory_stock of each resource. factory-auto.md: what it does not need,
        // or has full, it passes on to a belt or bridge input leading away; with nowhere to pass, it
        // keeps what still fits; else the giver stands.
        Wallet? stock = receiver.Stock;
        if (stock != null)
        {
            bool fits = stock.Get(resourceType) < state.Rules.FactoryStock;
            if (fits && receiver.Stats.Needs(resourceType))
            {
                stock.Add(resourceType, 1);
                return true;
            }
            if (PassOn(state, receiver, resourceType))
                return true;
            if (!fits)
                return false;
            stock.Add(resourceType, 1);
            return true;
        }

        switch (receiver.Stats.Behavior)
        {
            case BuildingBehavior.Headquarters:
                // Only a headquarters credits a wallet: its owner's (answers 2, 15).
                state.GetPlayer(receiver.OwnerId).Wallet.Credit(resourceType, 1);
                return true;

            case BuildingBehavior.Storage:
                if (receiver.Storage!.Total >= state.Rules.StorageCapacity)
                    return false;
                receiver.Storage.Add(resourceType);
                return true;

            case BuildingBehavior.Conveyor:
                Conveyor c = receiver.Conveyor!;
                if (c.HasItem)
                    return false;
                c.ItemType = resourceType;
                c.ItemPos = 0;
                return true;

            case BuildingBehavior.Bridge:
                return ConveyorSystem.TryEnterBridge(state, receiver, resourceType);
        }
        return false;
    }

    private static readonly List<Building> Beside = new List<Building>();

    // factory-auto.md, for the UI: the belts and bridge ends beside a building with a stock that
    // hand it resources (Feeds), and those its surplus is passed on to — each with the way the
    // resource flows on it. Asks Connects, as the handing over itself does.
    public static void StockLinks(SimState state, Building b, List<(Building Link, Direction Flow, bool Feeds)> links)
    {
        links.Clear();
        if (b.Stock == null || b.IsDead || !b.IsComplete)
            return;
        Neighbours(state, b, Beside);
        foreach (Building n in Beside)
        {
            if (!ConveyorSystem.TryFlow(state, n, out Direction flow))
                continue;
            if (Connects(state, b, Giver.Storage, n))
                links.Add((n, flow, false));
            // A belt, or the output end of a bridge, whose next cell along the flow is the building.
            else if (n.IsComplete && (n.Conveyor != null || n.Bridge!.IsOutputEnd)
                && b.OccupiesCell(n.CellX + flow.Dx(), n.CellY + flow.Dy()) && Connects(state, n, Giver.Belt, b))
                links.Add((n, flow, true));
        }
    }
}
