using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// creep-camp.md: right-click with workers on the trophy of a cleared camp.
public sealed class CarryTrophyCommand : ICommand
{
    public const int Id = 9;

    private readonly int[] _unitIds;

    public CarryTrophyCommand(int playerId, int tick, int[] unitIds, int campIndex, bool queued = false)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        CampIndex = campIndex;
        Queued = queued;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public int CampIndex { get; }

    // order-queue.md: given with Shift — joins the queue of a busy unit.
    public bool Queued { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(CampIndex);
        writer.Write(Queued);
    }

    public static CarryTrophyCommand Read(int playerId, int tick, BinaryReader reader) =>
        new CarryTrophyCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32(), reader.ReadBoolean());

    public void Execute(SimState state, List<SimEvent> events)
    {
        if (CampIndex < 0 || CampIndex >= state.CreepCamps.Count)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        CreepCamp camp = state.CreepCamps[CampIndex];
        if (camp.State != CreepCampState.Cleared || camp.TrophyPrimary + camp.TrophySecondary == 0)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        bool full = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            Worker? worker = unit.Worker;
            if (worker == null || UnitOrders.Defer(unit, Queued, UnitOrder.CarryTrophy(CampIndex), null, ref full))
                continue;
            UnitOrders.Replace(unit, UnitOrder.CarryTrophy(CampIndex));
            worker.DeliverToId = 0;
            worker.Phase = worker.Cargo > 0 ? WorkerPhase.Carrying : WorkerPhase.GoingToOre;
        }
        if (full)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
    }
}
