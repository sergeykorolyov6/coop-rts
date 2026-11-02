using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// Right-click on the ground: the selected units walk there.
public sealed class MoveCommand : ICommand
{
    public const int Id = 1;

    private readonly int[] _unitIds;

    public MoveCommand(int playerId, int tick, int[] unitIds, int x, int y, bool queued = false)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        X = x;
        Y = y;
        Queued = queued;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;

    // Target point, milli-cells.
    public int X { get; }
    public int Y { get; }

    // order-queue.md: given with Shift — joins the queue of a busy unit.
    public bool Queued { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Queued);
    }

    public static MoveCommand Read(int playerId, int tick, BinaryReader reader) =>
        new MoveCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean());

    public void Execute(SimState state, List<SimEvent> events)
    {
        // Clamp into the map: a click past the edge means "as far as you can go".
        int maxX = Grid.CellToMilli(state.Map.Width) - 1, maxY = Grid.CellToMilli(state.Map.Height) - 1;
        int x = X < 0 ? 0 : X > maxX ? maxX : X;
        int y = Y < 0 ? 0 : Y > maxY ? maxY : Y;

        bool full = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            UnitOrder order = UnitOrder.MoveTo(x, y);
            if (!UnitOrders.Defer(unit, Queued, order, null, ref full))
                UnitOrders.Replace(unit, order);
        }
        if (full)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
    }
}
