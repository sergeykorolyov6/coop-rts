using System.Collections.Generic;

namespace Game.Sim;

// Moves resources along conveyors and over bridges (conveyors.md, appendix).
// Each tick the belts are walked from their heads (cells that hand off to something other than an
// own conveyor) back through all their inputs, so a unit can move into the room its leader freed
// in the same tick: one unit per CellLength / speed ticks = 4 units/s.
// belt-merge.md: a cell takes from every own belt pointing into it — behind, left, right; when
// several are ready on one tick they go in turn round that circle after the one that entered last.
// "Ready" is judged on the positions at the start of the tick, so the walking order never decides.
internal static class ConveyorSystem
{
    private static readonly List<Building> Belts = new List<Building>();
    private static readonly List<Building> Order = new List<Building>();
    private static readonly List<bool> Visited = new List<bool>();
    private static readonly List<bool> Ready = new List<bool>();
    private static readonly List<int> Stack = new List<int>();
    private static readonly Building?[] Feeders = new Building?[3];

    public static void Tick(SimState state)
    {
        CollectBelts(state);
        int speed = state.Rules.ConveyorItemSpeedMilliPerTick;
        Ready.Clear();
        foreach (Building b in Belts)
            Ready.Add(b.Conveyor!.HasItem && b.Conveyor.ItemPos + speed >= Conveyor.CellLength);
        BuildOrder(state);
        foreach (Building belt in Order)
            MoveItem(state, belt);
        MoveBridges(state);
    }

    // Fills Feeders with the own complete conveyors pointing into belt: [0] behind, [1] left,
    // [2] right (null where none) — the order of Conveyor.FromBack, FromLeft, FromRight.
    private static void FindFeeders(SimState state, Building belt)
    {
        Direction d = belt.Direction;
        Feeders[0] = FeederAt(state, belt, belt.CellX - d.Dx(), belt.CellY - d.Dy());
        // Sides: the direction turned by ±90°.
        Feeders[1] = FeederAt(state, belt, belt.CellX - d.Dy(), belt.CellY + d.Dx());
        Feeders[2] = FeederAt(state, belt, belt.CellX + d.Dy(), belt.CellY - d.Dx());
    }

    // For the UI (PlacementPreview.BeltInputs): which sides of the belt are fed.
    public static (bool Back, bool Left, bool Right) Inputs(SimState state, Building belt)
    {
        FindFeeders(state, belt);
        return (Feeders[0] != null, Feeders[1] != null, Feeders[2] != null);
    }

    // Which way a resource moves on a belt cell or on a linked bridge end (from its input end to
    // its output end); false for an unlinked end and for anything else.
    public static bool TryFlow(SimState state, Building b, out Direction flow)
    {
        flow = b.Direction;
        if (b.Conveyor != null)
            return true;
        if (b.Bridge == null)
            return false;
        bool output = b.Bridge.IsOutputEnd;
        Building? other = state.FindBuilding(output ? b.Bridge.LinkedFromId : b.Bridge.LinkedToId);
        if (other == null)
            return false;
        int dx = output ? b.CellX - other.CellX : other.CellX - b.CellX;
        int dy = output ? b.CellY - other.CellY : other.CellY - b.CellY;
        flow = dx > 0 ? Direction.Right : dx < 0 ? Direction.Left : dy > 0 ? Direction.Up : Direction.Down;
        return true;
    }

    // An own complete conveyor at (x, y) pointing into belt, with no cliff between (heights.md).
    private static Building? FeederAt(SimState state, Building belt, int x, int y)
    {
        if (!state.Map.InBounds(x, y))
            return null;
        Building? f = state.FindBuilding(state.Map.BuildingAt(x, y));
        if (f == null || f.IsDead || !f.IsComplete || f.Conveyor == null || f.OwnerId != belt.OwnerId)
            return null;
        if (f.CellX + f.Direction.Dx() != belt.CellX || f.CellY + f.Direction.Dy() != belt.CellY)
            return null;
        return state.Map.LevelsJoin(f.CellX, f.CellY, belt.CellX, belt.CellY) ? f : null;
    }

    // The own conveyor this one feeds into: any own complete belt ahead that does not point back
    // here (head-on belts give each other nothing).
    private static Building? Next(SimState state, Building belt)
    {
        Building? front = FrontBuilding(state, belt.CellX + belt.Direction.Dx(), belt.CellY + belt.Direction.Dy());
        if (front == null || front.Conveyor == null || !front.IsComplete || front.OwnerId != belt.OwnerId)
            return null;
        // heights.md: no belt-to-belt over a cliff.
        if (!state.Map.LevelsJoin(belt.CellX, belt.CellY, front.CellX, front.CellY))
            return null;
        if (front.CellX + front.Direction.Dx() == belt.CellX && front.CellY + front.Direction.Dy() == belt.CellY)
            return null;
        return front;
    }

    // The input of next whose turn it is to enter: the first ready one round the circle
    // back → left → right after the one that entered last; null when none is ready.
    private static Building? TurnHolder(SimState state, Building next, out int side)
    {
        FindFeeders(state, next);
        int last = next.Conveyor!.LastFeeder;
        for (int k = 0; k < 3; k++)
        {
            side = (last + k) % 3 + 1;
            Building? feeder = Feeders[side - 1];
            if (feeder != null && Ready[IndexOf(feeder)])
                return feeder;
        }
        side = Conveyor.FromNobody;
        return null;
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

    // Heads first, each followed by all its inputs backwards (behind first), so every cell moves
    // before the cells that feed it; leftover cells form loops, which start from their lowest id.
    // All by id order, so every machine walks the same way.
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
        Stack.Clear();
        Stack.Add(index);
        while (Stack.Count > 0)
        {
            int i = Stack[Stack.Count - 1];
            Stack.RemoveAt(Stack.Count - 1);
            if (Visited[i])
                continue;
            Visited[i] = true;
            Building belt = Belts[i];
            Order.Add(belt);
            FindFeeders(state, belt);
            for (int f = 2; f >= 0; f--)
            {
                if (Feeders[f] != null)
                    Stack.Add(IndexOf(Feeders[f]!));
            }
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
            // belt-merge.md: several inputs ready — the others wait at the edge for their turn.
            if (TurnHolder(state, next, out int side) != belt)
            {
                c.ItemPos = L;
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
            n.LastFeeder = side;
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

    // A bridge end takes from a giver when it is an input linked to a finished output; never from
    // the side facing its output.
    public static bool BridgeTakesFrom(SimState state, Building giver, Building input)
    {
        Building? output = state.FindBuilding(input.Bridge!.LinkedToId);
        if (output == null || output.IsDead || !output.IsComplete)
            return false;
        int dx = System.Math.Sign(output.CellX - input.CellX), dy = System.Math.Sign(output.CellY - input.CellY);
        return !giver.OccupiesCell(input.CellX + dx, input.CellY + dy);
    }

    // An input that takes from the giver (BridgeTakesFrom) takes a unit once the last one that
    // entered has moved one spacing on.
    public static bool TryEnterBridge(SimState state, Building input, int resourceType)
    {
        List<BridgeItem> items = input.Bridge!._items;
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
