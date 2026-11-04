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

    // Hands one unit over if the receiver takes it now. For Belt givers into a conveyor the
    // caller has already applied the single-input rule; here a conveyor only takes from
    // buildings (drill, storage, bridge output) with a free slot at its start.
    public static bool TryGive(SimState state, Building giver, Giver kind, Building receiver, int resourceType)
    {
        if (receiver.IsDead || !receiver.IsComplete)
            return false;
        bool own = giver.OwnerId == receiver.OwnerId;
        bool ally = state.Relations.IsAlly(giver.OwnerId, receiver.OwnerId);
        if (!ally)
            return false;

        // factory.md M2: a factory (or cave) supplied by conveyor takes from belts, drills and storages,
        // own or ally, up to factory_stock of each resource.
        Wallet? stock = receiver.Factory?.Stock;
        if (stock != null)
        {
            if (stock.Get(resourceType) >= state.Rules.FactoryStock)
                return false;
            stock.Add(resourceType, 1);
            return true;
        }

        switch (receiver.Stats.Behavior)
        {
            case BuildingBehavior.Headquarters:
                if (kind == Giver.Storage)
                    return false;
                // Only a headquarters credits a wallet: its owner's (answers 2, 15).
                state.GetPlayer(receiver.OwnerId).Wallet.Credit(resourceType, 1);
                return true;

            case BuildingBehavior.Storage:
                if (kind == Giver.Storage || receiver.Storage!.Total >= state.Rules.StorageCapacity)
                    return false;
                receiver.Storage.Add(resourceType);
                return true;

            case BuildingBehavior.Conveyor:
                if (kind == Giver.Belt && !own)
                    return false;
                Conveyor c = receiver.Conveyor!;
                if (c.HasItem || giver.OccupiesCell(receiver.CellX + receiver.Direction.Dx(), receiver.CellY + receiver.Direction.Dy()))
                    return false;
                c.ItemType = resourceType;
                c.ItemPos = 0;
                return true;

            case BuildingBehavior.Bridge:
                if (kind == Giver.Belt && !own)
                    return false;
                return ConveyorSystem.TryEnterBridge(state, giver, receiver, resourceType);
        }
        return false;
    }
}
