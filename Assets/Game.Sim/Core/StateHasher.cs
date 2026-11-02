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
        _writer.Write(u.AttackCooldown);
        _writer.Write(u.TargetId);
        _writer.Write(u.CampIndex);
        _writer.Write(u.HomeX);
        _writer.Write(u.HomeY);

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
        {
            _writer.Write(plan.BuildingTypeId);
            _writer.Write(plan.CellX);
            _writer.Write(plan.CellY);
            _writer.Write((byte)plan.Direction);
            _writer.Write(plan.ExistingSiteId);
        }
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
    }
}
