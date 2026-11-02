using System;
using System.IO;
using System.Text;

namespace Game.Sim;

// Serializes the key state fields in a fixed order and hashes them with XXH64.
// Compared every tick to catch desyncs in tests and over the network.
// Every new piece of state must be added here, or desyncs in it go unnoticed.
internal sealed class StateHasher
{
    private readonly MemoryStream _buffer = new MemoryStream();
    private readonly BinaryWriter _writer;

    public StateHasher()
    {
        _writer = new BinaryWriter(_buffer, Encoding.UTF8, leaveOpen: true);
    }

    public ulong Compute(SimState state)
    {
        _buffer.SetLength(0);

        _writer.Write(state.Tick);
        _writer.Write(state.Rng.State);
        _writer.Write(state.Grid.Width);
        _writer.Write(state.Grid.Height);
        _writer.Write(state.ResourceTypeCount);

        _writer.Write(state.Players.Count);
        foreach (Player player in state.Players)
        {
            _writer.Write(player.Id);
            for (int type = 0; type < player.Wallet.ResourceTypeCount; type++)
                _writer.Write(player.Wallet.Get(type));
        }

        for (int a = 0; a < state.Relations.PlayerCount; a++)
        {
            for (int b = 0; b < state.Relations.PlayerCount; b++)
                _writer.Write((byte)state.Relations.GetByIndex(a, b));
        }

        _writer.Write(state.NextEntityId);
        _writer.Write(state.Units.Count);
        foreach (Unit unit in state.Units)
            WriteUnit(unit);
        _writer.Write(state.Buildings.Count);
        foreach (Building building in state.Buildings)
            WriteBuilding(building);
        foreach (CreepCamp camp in state.CreepCamps)
        {
            _writer.Write((byte)camp.State);
            _writer.Write(camp.LastDamageTick);
            _writer.Write(camp.TrophyPrimary);
            _writer.Write(camp.TrophySecondary);
            _writer.Write(camp.Attackers.Count);
            foreach (int id in camp.Attackers)
                _writer.Write(id);
        }

        // Discovered cells per player as bits; visibility itself follows from positions.
        for (int p = 0; p < state.Players.Count; p++)
        {
            bool[] discovered = state.Fog.DiscoveredByIndex(p);
            for (int c = 0; c < discovered.Length; c += 8)
            {
                byte bits = 0;
                for (int k = 0; k < 8 && c + k < discovered.Length; k++)
                {
                    if (discovered[c + k])
                        bits |= (byte)(1 << k);
                }
                _writer.Write(bits);
            }
        }

        _writer.Flush();
        return XxHash64.Compute(new ReadOnlySpan<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length));
    }

    private void WriteEntity(Entity e)
    {
        _writer.Write(e.Id);
        _writer.Write(e.OwnerId);
        _writer.Write(e.Hp);
        _writer.Write(e.IsDead);
        _writer.Write(e.LastDamagedTick);
        _writer.Write(e.LastAttackerId);
    }

    private void WriteUnit(Unit u)
    {
        WriteEntity(u);
        _writer.Write(u.Stats.Id);
        _writer.Write(u.X);
        _writer.Write(u.Y);
        _writer.Write((byte)u.Order.Kind);
        _writer.Write(u.Order.X);
        _writer.Write(u.Order.Y);
        _writer.Write(u.Order.TargetId);
        _writer.Write(u.OrderQueue.Count);
        foreach (QueuedOrder queued in u.OrderQueue)
        {
            _writer.Write((byte)queued.Order.Kind);
            _writer.Write(queued.Order.X);
            _writer.Write(queued.Order.Y);
            _writer.Write(queued.Order.TargetId);
            _writer.Write(queued.Plans.Count);
            foreach (PlannedBuild plan in queued.Plans)
                WritePlan(plan);
        }
        _writer.Write(u.AttackCooldown);
        _writer.Write(u.TargetId);
        _writer.Write(u.CampIndex);
        _writer.Write(u.HomeX);
        _writer.Write(u.HomeY);
        _writer.Write(u.RegenAccumulator);

        Worker? w = u.Worker;
        if (w == null)
            return;
        _writer.Write(w.CargoType);
        _writer.Write(w.Cargo);
        _writer.Write(w.MiningAccumulator);
        _writer.Write(w.DeliverToId);
        _writer.Write((byte)w.Phase);
        _writer.Write(w.RepairCostAccumulator);
        _writer.Write(w.RepairCostAccumulatorSecondary);
        _writer.Write(w.RepairHpAccumulator);
        _writer.Write(w.CurrentPlanKey);
        _writer.Write(w.Plans.Count);
        foreach (PlannedBuild plan in w.Plans)
            WritePlan(plan);
    }

    private void WritePlan(PlannedBuild plan)
    {
        _writer.Write(plan.BuildingTypeId);
        _writer.Write(plan.CellX);
        _writer.Write(plan.CellY);
        _writer.Write((byte)plan.Direction);
        _writer.Write(plan.ExistingSiteId);
    }

    private void WriteBuilding(Building b)
    {
        WriteEntity(b);
        _writer.Write(b.Stats.Id);
        _writer.Write(b.CellX);
        _writer.Write(b.CellY);
        _writer.Write((byte)b.Direction);
        _writer.Write((byte)b.State);
        _writer.Write(b.BuildProgress);
        _writer.Write(b.BuildersLastTick);
        if (b.Drill != null)
        {
            _writer.Write(b.Drill.Buffer);
            _writer.Write(b.Drill.Accumulator);
            _writer.Write(b.Drill.OutputCursor);
            _writer.Write(b.Drill.IsStopped);
        }
        if (b.Conveyor != null)
        {
            _writer.Write(b.Conveyor.ItemType);
            _writer.Write(b.Conveyor.ItemPos);
        }
        if (b.Bridge != null)
        {
            _writer.Write(b.Bridge.LinkedToId);
            _writer.Write(b.Bridge.LinkedFromId);
            _writer.Write(b.Bridge.IsOutputEnd);
            _writer.Write(b.Bridge._items.Count);
            foreach (BridgeItem item in b.Bridge._items)
            {
                _writer.Write(item.Type);
                _writer.Write(item.Pos);
            }
        }
        _writer.Write(b.LastAlarmTick);
        if (b.Factory != null)
        {
            _writer.Write(b.Factory.Progress);
            _writer.Write(b.Factory.Queue.Count);
            foreach (string unit in b.Factory.Queue)
                _writer.Write(unit);
        }
        if (b.Storage != null)
        {
            _writer.Write(b.Storage.Primary);
            _writer.Write(b.Storage.Secondary);
            _writer.Write(b.Storage.OutputCursor);
        }
    }
}
