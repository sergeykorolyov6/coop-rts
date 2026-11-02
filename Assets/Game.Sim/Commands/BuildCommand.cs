using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// Place one building, or a whole dragged conveyor line (one site per cell), with the selected
// workers (construction.md, conveyors.md). Every site is paid when a worker starts it.
public sealed class BuildCommand : ICommand
{
    public const int Id = 4;
    public const int MaxSites = 256;

    private readonly int[] _unitIds;
    private readonly BuildSite[] _sites;

    public BuildCommand(int playerId, int tick, int[] unitIds, string buildingTypeId, BuildSite[] sites, bool queued = false)
    {
        PlayerId = playerId;
        Tick = tick;
        _unitIds = (int[])unitIds.Clone();
        BuildingTypeId = buildingTypeId;
        _sites = (BuildSite[])sites.Clone();
        Queued = queued;
    }

    public int PlayerId { get; }
    public int Tick { get; }
    public int TypeId => Id;
    public IReadOnlyList<int> UnitIds => _unitIds;
    public string BuildingTypeId { get; }
    public IReadOnlyList<BuildSite> Sites => _sites;

    // order-queue.md: given with Shift — joins the queue of a busy unit.
    public bool Queued { get; }

    public void WritePayload(BinaryWriter writer)
    {
        UnitSelection.Write(writer, _unitIds);
        writer.Write(BuildingTypeId);
        writer.Write(_sites.Length);
        foreach (BuildSite site in _sites)
        {
            writer.Write(site.CellX);
            writer.Write(site.CellY);
            writer.Write((byte)site.Direction);
        }
        writer.Write(Queued);
    }

    public static BuildCommand Read(int playerId, int tick, BinaryReader reader)
    {
        int[] units = UnitSelection.Read(reader);
        string type = reader.ReadString();
        int count = reader.ReadInt32();
        if (count < 0 || count > MaxSites)
            throw new InvalidDataException($"Build order with {count} sites.");
        var sites = new BuildSite[count];
        for (int i = 0; i < count; i++)
        {
            int x = reader.ReadInt32(), y = reader.ReadInt32();
            byte dir = reader.ReadByte();
            if (dir > (byte)Direction.Down)
                throw new InvalidDataException($"Bad direction {dir}.");
            sites[i] = new BuildSite(x, y, (Direction)dir);
        }
        return new BuildCommand(playerId, tick, units, type, sites, reader.ReadBoolean());
    }

    public void Execute(SimState state, List<SimEvent> events)
    {
        if (_sites.Length == 0 || !state.Data.TryGetBuilding(BuildingTypeId, out BuildingStats? stats))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }

        if (stats!.Id == BuildingKinds.ConveyorBridge && _sites.Length == 2
            && !ConveyorSystem.InBridgeRange(state, _sites[0].CellX, _sites[0].CellY, _sites[1].CellX, _sites[1].CellY))
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
            return;
        }

        // Checked against the first site only: later cells of a line are checked when
        // a worker gets to them (conveyors.md, fail cases).
        BuildSite first = _sites[0];
        RejectReason? reason = Construction.CheckPlacement(state, PlayerId, stats!, first.CellX, first.CellY, first.Direction)
            ?? Construction.CheckCanPay(state, PlayerId, stats!);
        if (reason != null)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, reason.Value));
            return;
        }

        bool full = false;
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            Worker? worker = unit.Worker;
            if (worker == null)
                continue;
            var plans = new PlannedBuild[_sites.Length];
            for (int i = 0; i < _sites.Length; i++)
                plans[i] = new PlannedBuild(BuildingTypeId, _sites[i].CellX, _sites[i].CellY, _sites[i].Direction);

            // Shift while building with nothing else queued: the silhouettes join the current
            // build, built in order (order-queue.md).
            if (Queued && unit.Order.Kind == UnitOrderKind.Build && unit._queue.Count == 0)
            {
                worker._plans.AddRange(plans);
                continue;
            }
            if (UnitOrders.Defer(unit, Queued, UnitOrder.Build(), plans, ref full))
                continue;
            UnitOrders.Replace(unit, UnitOrder.Build());
            worker._plans.AddRange(plans);
        }
        if (full)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
    }
}
