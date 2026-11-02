using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// manual-mining.md: right-click with workers on a free ore cell.
public sealed class MineCommand : ICommand
{
    public const int Id = 2;

    private readonly int[] _unitIds;

    public MineCommand(int playerId, int tick, int[] unitIds, int cellX, int cellY, bool queued = false)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        CellX = cellX;
        CellY = cellY;
        Queued = queued;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public int CellX { get; }
    public int CellY { get; }

    // order-queue.md: given with Shift — joins the queue of a busy unit.
    public bool Queued { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(CellX);
        writer.Write(CellY);
        writer.Write(Queued);
    }

    public static MineCommand Read(int playerId, int tick, BinaryReader reader) =>
        new MineCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean());

    public void Execute(SimState state, List<SimEvent> events)
    {
        RejectReason? reason = ManualMining.CheckCell(state, PlayerId, CellX, CellY);
        if (reason == null && state.FindHeadquarters(PlayerId) == null)
            reason = RejectReason.NoHeadquarters;
        if (reason != null)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, reason.Value));
            return;
        }

        bool full = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            if (unit.Worker == null || UnitOrders.Defer(unit, Queued, UnitOrder.Mine(CellX, CellY), null, ref full))
                continue;
            unit._queue.Clear();
            ManualMining.Start(state, unit, CellX, CellY);
        }
        if (full)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
    }
}
