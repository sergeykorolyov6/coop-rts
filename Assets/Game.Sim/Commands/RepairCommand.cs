using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// Right-click with workers on an own or ally building: help build it if unfinished
// (a frozen or ally site, drill.md), otherwise repair it (construction.md).
public sealed class RepairCommand : ICommand
{
    public const int Id = 5;

    private readonly int[] _unitIds;

    public RepairCommand(int playerId, int tick, int[] unitIds, int buildingId, bool queued = false)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        BuildingId = buildingId;
        Queued = queued;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public int BuildingId { get; }

    // order-queue.md: given with Shift — joins the queue of a busy unit.
    public bool Queued { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(BuildingId);
        writer.Write(Queued);
    }

    public static RepairCommand Read(int playerId, int tick, BinaryReader reader) =>
        new RepairCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32(), reader.ReadBoolean());

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

        bool full = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            Worker? worker = unit.Worker;
            if (worker == null)
                continue;
            if (target.IsComplete)
            {
                if (!UnitOrders.Defer(unit, Queued, UnitOrder.Repair(target.Id), null, ref full))
                    UnitOrders.Replace(unit, UnitOrder.Repair(target.Id));
            }
            else
            {
                var plan = new PlannedBuild(target.Stats.Id, target.CellX, target.CellY, target.Direction) { ExistingSiteId = target.Id };
                if (UnitOrders.Defer(unit, Queued, UnitOrder.Build(), new[] { plan }, ref full))
                    continue;
                UnitOrders.Replace(unit, UnitOrder.Build());
                worker._plans.Add(plan);
            }
        }
        if (full)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
    }
}
