using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// manual-mining.md: right-click on an ally's (or own) headquarters with a loaded worker:
// hand the cargo in there, and keep delivering there on later trips.
public sealed class DeliverCargoCommand : ICommand
{
    public const int Id = 3;

    private readonly int[] _unitIds;

    public DeliverCargoCommand(int playerId, int tick, int[] unitIds, int headquartersId)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        HeadquartersId = headquartersId;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public int HeadquartersId { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(HeadquartersId);
    }

    public static DeliverCargoCommand Read(int playerId, int tick, BinaryReader reader) =>
        new DeliverCargoCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        Building? hq = state.FindBuilding(HeadquartersId);
        if (hq == null || !ManualMining.CanDeliverTo(state, PlayerId, hq))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }

        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            Worker? worker = unit.Worker;
            if (worker == null)
                continue;
            worker.DeliverToId = hq.Id;
            if (worker.Cargo > 0)
            {
                // Mining continues after the drop-off; any other order is replaced by a single trip.
                if (unit.Order.Kind != UnitOrderKind.Mine)
                    UnitOrders.Give(unit, UnitOrder.ReturnCargo());
                worker.DeliverToId = hq.Id;
                worker.Phase = WorkerPhase.Carrying;
            }
        }
    }
}
