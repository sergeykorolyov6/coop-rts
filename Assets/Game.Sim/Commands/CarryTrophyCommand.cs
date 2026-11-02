using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// creep-camp.md: right-click with workers on the trophy of a cleared camp.
public sealed class CarryTrophyCommand : ICommand
{
    public const int Id = 9;

    private readonly int[] _unitIds;

    public CarryTrophyCommand(int playerId, int tick, int[] unitIds, int campIndex)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        CampIndex = campIndex;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public int CampIndex { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(CampIndex);
    }

    public static CarryTrophyCommand Read(int playerId, int tick, BinaryReader reader) =>
        new CarryTrophyCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32());

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
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            Worker? worker = unit.Worker;
            if (worker == null)
                continue;
            UnitOrders.Give(unit, UnitOrder.CarryTrophy(CampIndex));
            worker.DeliverToId = 0;
            worker.Phase = worker.Cargo > 0 ? WorkerPhase.Carrying : WorkerPhase.GoingToOre;
        }
    }
}
