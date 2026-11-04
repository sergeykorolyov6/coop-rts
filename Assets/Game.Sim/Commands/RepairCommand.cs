using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// Right-click with builders or repairers on an own or ally building or unit: builders help build
// an unfinished site (a frozen or ally one, drill.md); otherwise it is repaired (construction.md,
// builder.md). The id field keeps its old name: the serialized format does not change.
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
        // BuildingId may name a unit too: builders and repairers heal own and ally units (builder.md).
        Entity? target = state.FindEntity(BuildingId);
        if (target == null || target.IsDead || !state.Relations.IsAlly(PlayerId, target.OwnerId)
            || (target is Building tb && tb.Stats.Invulnerable))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }
        var site = target as Building;
        bool unfinished = site != null && !site.IsComplete;
        if (!unfinished && target.Hp >= target.MaxHp)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NothingToRepair));
            return;
        }

        bool full = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            if (!unit.CanRepair || unit == target)
                continue;
            Worker? worker = unit.Worker;
            if (!unfinished)
            {
                if (!UnitOrders.Defer(unit, Queued, UnitOrder.Repair(target.Id), null, ref full))
                    UnitOrders.Replace(unit, UnitOrder.Repair(target.Id));
            }
            else if (worker != null)
            {
                // Only builders build; a repairer does not help an unfinished site.
                var plan = new PlannedBuild(site!.Stats.Id, site.CellX, site.CellY, site.Direction) { ExistingSiteId = site.Id };
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
