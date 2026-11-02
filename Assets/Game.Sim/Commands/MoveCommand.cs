using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// Right-click on the ground: the selected units walk there.
public sealed class MoveCommand : ICommand
{
    public const int Id = 1;

    private readonly int[] _unitIds;

    public MoveCommand(int playerId, int tick, int[] unitIds, int x, int y)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        X = x;
        Y = y;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;

    // Target point, milli-cells.
    public int X { get; }
    public int Y { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(X);
        writer.Write(Y);
    }

    public static MoveCommand Read(int playerId, int tick, BinaryReader reader) =>
        new MoveCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32(), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        // Clamp into the map: a click past the edge means "as far as you can go".
        int maxX = Grid.CellToMilli(state.Map.Width) - 1, maxY = Grid.CellToMilli(state.Map.Height) - 1;
        int x = X < 0 ? 0 : X > maxX ? maxX : X;
        int y = Y < 0 ? 0 : Y > maxY ? maxY : Y;

        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
            UnitOrders.Give(unit, UnitOrder.MoveTo(x, y));
    }
}
