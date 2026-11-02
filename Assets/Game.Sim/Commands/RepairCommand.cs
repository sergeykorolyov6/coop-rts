using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// Right-click with workers on an own or ally building: help build it if unfinished
// (a frozen or ally site, drill.md), otherwise repair it (construction.md).
public sealed class RepairCommand : ICommand
{
    public const int Id = 5;

    private readonly int[] _unitIds;

    public RepairCommand(int playerId, int tick, int[] unitIds, int buildingId)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        BuildingId = buildingId;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public int BuildingId { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(BuildingId);
    }

    public static RepairCommand Read(int playerId, int tick, BinaryReader reader) =>
        new RepairCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32());

    public void Execute(SimState state, List<SimEvent> events)
    {
        Building? target = state.FindBuilding(BuildingId);
        if (target == null || target.IsDead || !state.Relations.IsAlly(PlayerId, target.OwnerId))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        if (target.IsComplete && target.Hp >= target.MaxHp)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NothingToRepair));
            return;
        }

        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            Worker? worker = unit.Worker;
            if (worker == null)
                continue;
            if (target.IsComplete)
            {
                UnitOrders.Give(unit, UnitOrder.Repair(target.Id));
            }
            else
            {
                UnitOrders.Give(unit, UnitOrder.Build());
                var plan = new PlannedBuild(target.Stats.Id, target.CellX, target.CellY, target.Direction) { ExistingSiteId = target.Id };
                worker._plans.Add(plan);
            }
        }
    }
}
