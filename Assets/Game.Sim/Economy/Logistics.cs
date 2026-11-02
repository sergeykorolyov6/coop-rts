using System.Collections.Generic;

namespace Game.Sim;

// Hand-over of single resource units between buildings: drill → neighbour, conveyor → front.
internal static class Logistics
{
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

    // Hands one unit from a drill or storage to a neighbouring receiver if it takes it.
    // Ally buildings take, enemy and neutral ones never (conveyors.md, "Бур → лента").
    public static bool TryGiveFromBuilding(SimState state, Building giver, Building receiver, int resourceType)
    {
        if (!receiver.IsComplete || !state.Relations.IsAlly(giver.OwnerId, receiver.OwnerId))
            return false;
        if (receiver.Stats.Id == BuildingKinds.Headquarters)
        {
            // Only a headquarters credits the wallet, its owner's (answers 2, 15).
            state.GetPlayer(receiver.OwnerId).Wallet.Add(resourceType, 1);
            return true;
        }
        return false;
    }
}
