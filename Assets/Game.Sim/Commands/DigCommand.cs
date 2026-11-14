using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// terrain.md: right-click on destructible rock with miners — each digs a tunnel to the cell.
public sealed class DigCommand : ICommand
{
    public const int Id = 14;

    private readonly int[] _unitIds;

    public DigCommand(int playerId, int tick, int[] unitIds, int cellX, int cellY, bool queued = false)
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

    public static DigCommand Read(int playerId, int tick, BinaryReader reader) =>
        new DigCommand(playerId, tick, UnitSelection.Read(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBoolean());

    public void Execute(SimState state, List<SimEvent> events)
    {
        MapState map = state.Map;
        RejectReason? reason = null;
        List<Unit> miners = new List<Unit>();
        foreach (Unit unit in UnitSelection.Resolve(state, PlayerId, _unitIds))
        {
            if (unit.Stats.IsDigger)
                miners.Add(unit);
        }
        if (miners.Count == 0)
            reason = RejectReason.NoDigger;
        else if (!map.InBounds(CellX, CellY) || !state.Fog.IsDiscovered(PlayerId, CellX, CellY))
            reason = RejectReason.NotDiscovered;
        else if (!map.IsDestructibleRock(CellX, CellY))
            reason = RejectReason.NothingToDig;
        if (reason != null)
        {
            events.Add(new CommandRejected(state.Tick, PlayerId, reason.Value));
            return;
        }

        bool any = false, full = false;
        foreach (Unit unit in miners)
        {
            List<(int X, int Y)>? tunnel = Digging.PlanTunnel(state, unit, CellX, CellY);
            if (tunnel == null)
                continue;
            any = true;
            int i = 0;
            if (!Queued || (unit.Order.Kind == UnitOrderKind.Idle && unit._queue.Count == 0))
            {
                UnitOrders.Replace(unit, UnitOrder.Dig(tunnel[0].X, tunnel[0].Y));
                i = 1;
            }
            for (; i < tunnel.Count; i++)
            {
                if (unit._queue.Count >= UnitOrders.MaxQueue)
                {
                    full = true;
                    break;
                }
                unit._queue.Add(new QueuedOrder(UnitOrder.Dig(tunnel[i].X, tunnel[i].Y), null));
            }
        }
        if (!any)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.CannotDig));
        else if (full)
            events.Add(new CommandRejected(state.Tick, PlayerId, RejectReason.NotAllowed));
    }
}
