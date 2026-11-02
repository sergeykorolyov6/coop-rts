using System.Collections.Generic;

namespace Game.Sim;

// Moves resources along conveyors and over bridges (conveyors.md, appendix).
// Each tick every chain is walked from its head (the cell that hands off to something other
// than an own conveyor) back to its tail, so a unit can move into the room its leader freed
// in the same tick: one unit per CellLength / speed ticks = 4 units/s.
internal static class ConveyorSystem
{
    private static readonly List<Building> Belts = new List<Building>();
    private static readonly List<Building> Order = new List<Building>();
    private static readonly List<bool> Visited = new List<bool>();

    public static void Tick(SimState state)
    {
        CollectBelts(state);
        BuildOrder(state);
        foreach (Building belt in Order)
            MoveItem(state, belt);
        MoveBridges(state);
    }

    // The conveyor feeding this one: the one behind it if it points here, otherwise the
    // lowest-id one pointing here from a side. Others stand as before a wall (no merging, answer 14).
    public static Building? AcceptedInput(SimState state, Building belt)
    {
        Direction d = belt.Direction;
        Building? back = FeederAt(state, belt, belt.CellX - d.Dx(), belt.CellY - d.Dy());
        if (back != null)
            return back;
        // Sides: rotate the direction by ±90°.
        Building? left = FeederAt(state, belt, belt.CellX - d.Dy(), belt.CellY + d.Dx());
        Building? right = FeederAt(state, belt, belt.CellX + d.Dy(), belt.CellY - d.Dx());
        if (left == null)
            return right;
        if (right == null)
            return left;
        return left.Id < right.Id ? left : right;
    }

    // An own complete conveyor at (x, y) pointing into belt.
    private static Building? FeederAt(SimState state, Building belt, int x, int y)
    {
        if (!state.Map.InBounds(x, y))
            return null;
        Building? f = state.FindBuilding(state.Map.BuildingAt(x, y));
        if (f == null || f.IsDead || !f.IsComplete || f.Conveyor == null || f.OwnerId != belt.OwnerId)
            return null;
        return f.CellX + f.Direction.Dx() == belt.CellX && f.CellY + f.Direction.Dy() == belt.CellY ? f : null;
    }

    // The own conveyor this one feeds into, if that conveyor accepts it.
    private static Building? Next(SimState state, Building belt)
    {
        Building? front = FrontBuilding(state, belt.CellX + belt.Direction.Dx(), belt.CellY + belt.Direction.Dy());
        if (front == null || front.Conveyor == null || !front.IsComplete || front.OwnerId != belt.OwnerId)
            return null;
        return AcceptedInput(state, front) == belt ? front : null;
    }

    private static Building? FrontBuilding(SimState state, int x, int y)
    {
        if (!state.Map.InBounds(x, y))
            return null;
        Building? b = state.FindBuilding(state.Map.BuildingAt(x, y));
        return b == null || b.IsDead ? null : b;
    }

    private static void CollectBelts(SimState state)
    {
        Belts.Clear();
        foreach (Building b in state.Buildings)
        {
            if (b.Conveyor != null && !b.IsDead && b.IsComplete)
                Belts.Add(b);
        }
    }

    // Heads first, each followed by its chain backwards; leftover cells form loops, which
    // start from their lowest id. All by id order, so every machine walks the same way.
    private static void BuildOrder(SimState state)
    {
        Order.Clear();
        Visited.Clear();
        for (int i = 0; i < Belts.Count; i++)
            Visited.Add(false);

        for (int i = 0; i < Belts.Count; i++)
        {
            if (Next(state, Belts[i]) == null)
                Walk(state, i);
        }
        for (int i = 0; i < Belts.Count; i++)
        {
            if (!Visited[i])
                Walk(state, i);
        }
    }

    private static void Walk(SimState state, int index)
    {
        while (index >= 0 && !Visited[index])
        {
            Visited[index] = true;
            Building belt = Belts[index];
            Order.Add(belt);
            Building? prev = AcceptedInput(state, belt);
            index = prev == null ? -1 : IndexOf(prev);
        }
    }

    private static int IndexOf(Building b)
    {
        int lo = 0, hi = Belts.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Belts[mid].Id == b.Id)
                return mid;
            if (Belts[mid].Id < b.Id)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return -1;
    }

    private static void MoveItem(SimState state, Building belt)
    {
        Conveyor c = belt.Conveyor!;
        if (!c.HasItem)
            return;
        int speed = state.Rules.ConveyorItemSpeedMilliPerTick;
        int spacing = state.Rules.ConveyorItemSpacingMilli;
        const int L = Conveyor.CellLength;
        int target = c.ItemPos + speed;

        Building? next = Next(state, belt);
        if (next != null)
        {
            Conveyor n = next.Conveyor!;
            if (n.HasItem)
            {
                // Keep the spacing to the unit ahead; never past the front edge of this cell.
                int limit = System.Math.Min(L, L + n.ItemPos - spacing);
                c.ItemPos = System.Math.Max(c.ItemPos, System.Math.Min(target, limit));
                return;
            }
            if (target < L)
            {
                c.ItemPos = target;
                return;
            }
            // Over the edge into the next cell, still keeping the spacing to the unit after it.
            Building? after = Next(state, next);
            int limitNext = after != null && after.Conveyor!.HasItem ? System.Math.Min(L, L + after.Conveyor.ItemPos - spacing) : L;
            int posNext = System.Math.Min(target - L, limitNext);
            if (posNext < 0)
            {
                c.ItemPos = L;
                return;
            }
            n.ItemType = c.ItemType;
            n.ItemPos = posNext;
            c.Clear();
            return;
        }

        if (target < L)
        {
            c.ItemPos = target;
            return;
        }
        Building? front = FrontBuilding(state, belt.CellX + belt.Direction.Dx(), belt.CellY + belt.Direction.Dy());
        if (front != null && front.Conveyor == null && Logistics.TryGive(state, belt, Logistics.Giver.Belt, front, c.ItemType))
            c.Clear();
        else
            c.ItemPos = L;  // nowhere to go: stands at the front edge, the line jams behind it
    }

    // A bridge input takes a unit when it is linked to a finished output and the last unit
    // that entered has moved one spacing on. It never takes from the side facing its output.
    public static bool TryEnterBridge(SimState state, Building giver, Building input, int resourceType)
    {
        ConveyorBridge bridge = input.Bridge!;
        Building? output = state.FindBuilding(bridge.LinkedToId);
        if (output == null || output.IsDead || !output.IsComplete)
            return false;
        int dx = System.Math.Sign(output.CellX - input.CellX), dy = System.Math.Sign(output.CellY - input.CellY);
        if (giver.OccupiesCell(input.CellX + dx, input.CellY + dy))
            return false;
        List<BridgeItem> items = bridge._items;
        if (items.Count > 0 && items[items.Count - 1].Pos < state.Rules.ConveyorItemSpacingMilli)
            return false;
        items.Add(new BridgeItem(resourceType, 0));
        return true;
    }

    private static void MoveBridges(SimState state)
    {
        int speed = state.Rules.ConveyorItemSpeedMilliPerTick;
        int spacing = state.Rules.ConveyorItemSpacingMilli;
        foreach (Building input in state.Buildings)
        {
            ConveyorBridge? bridge = input.Bridge;
            if (bridge == null || input.IsDead || bridge._items.Count == 0)
                continue;
            Building? output = state.FindBuilding(bridge.LinkedToId);
            if (output == null || output.IsDead)
                continue;

            int length = Grid.CellToMilli(System.Math.Abs(output.CellX - input.CellX) + System.Math.Abs(output.CellY - input.CellY));
            List<BridgeItem> items = bridge._items;
            for (int i = 0; i < items.Count; i++)
            {
                BridgeItem item = items[i];
                int limit = i == 0 ? length : items[i - 1].Pos - spacing;
                item.Pos = System.Math.Max(item.Pos, System.Math.Min(item.Pos + speed, limit));
                items[i] = item;
            }

            if (items[0].Pos >= length)
            {
                // Out of the output end, forward along the bridge's line.
                int dx = System.Math.Sign(output.CellX - input.CellX), dy = System.Math.Sign(output.CellY - input.CellY);
                Building? front = FrontBuilding(state, output.CellX + dx, output.CellY + dy);
                if (front != null && Logistics.TryGive(state, output, Logistics.Giver.Belt, front, items[0].Type))
                    items.RemoveAt(0);
            }
        }
    }

    // Links a new bridge end (conveyors.md: the second end links to the first by itself):
    // an own unlinked input end in line within range sends to it; failing that, it becomes
    // the input of an own output end that lost its input. The most recent candidate wins.
    public static void LinkNewBridgeEnd(SimState state, Building end)
    {
        Building? input = null, output = null;
        foreach (Building b in state.Buildings)
        {
            if (b == end || b.Bridge == null || b.IsDead || b.OwnerId != end.OwnerId || !InBridgeRange(state, b, end))
                continue;
            if (b.Bridge.LinkedToId == 0 && !b.Bridge.IsOutputEnd)
                input = b;
            else if (b.Bridge.IsOutputEnd && b.Bridge.LinkedFromId == 0)
                output = b;
        }

        if (input != null)
            Link(input, end);
        else if (output != null)
            Link(end, output);
    }

    public static bool InBridgeRange(SimState state, Building a, Building b) =>
        InBridgeRange(state, a.CellX, a.CellY, b.CellX, b.CellY);

    public static bool InBridgeRange(SimState state, int x1, int y1, int x2, int y2)
    {
        int dx = System.Math.Abs(x2 - x1), dy = System.Math.Abs(y2 - y1);
        return (dx == 0) != (dy == 0) && dx + dy <= state.Rules.BridgeRange;
    }

    private static void Link(Building input, Building output)
    {
        input.Bridge!.LinkedToId = output.Id;
        output.Bridge!.LinkedFromId = input.Id;
        output.Bridge.IsOutputEnd = true;
    }

    // A destroyed end: units in flight are lost, the other end waits for a new partner.
    public static void Unlink(SimState state, Building dead)
    {
        ConveyorBridge bridge = dead.Bridge!;
        Building? output = state.FindBuilding(bridge.LinkedToId);
        if (output != null)
            output.Bridge!.LinkedFromId = 0;
        Building? input = state.FindBuilding(bridge.LinkedFromId);
        if (input != null)
        {
            input.Bridge!.LinkedToId = 0;
            input.Bridge._items.Clear();
        }
        bridge._items.Clear();
    }
}
