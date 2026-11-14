using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Sim;

// save-load.md, T5: the whole mutable state of a match between two ticks, so a save loads at once
// instead of replaying its command log. The map's layout, the balance data and the players come
// from the MatchSetup: a snapshot is read into the state a fresh Simulation of that setup starts
// with, and replaces everything a match changes. Entities are written by id of their kind, so a
// change of balance numbers does not break a snapshot; a kind that no longer exists does.
// Every new piece of state goes here as well as into StateHasher; SnapshotTests keep a restored
// match in step with the original, which catches what was forgotten.
internal static class Snapshot
{
    private const int Magic = 0x50414E53; // "SNAP"
    private const int Version = 1;

    public static byte[] Write(SimState state)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteHeader(w, state);

            w.Write(state.Tick);
            w.Write(state.Rng.State);
            w.Write(state.NextEntityId);
            w.Write((byte)state.Outcome);

            foreach (Player p in state.Players)
            {
                w.Write(p.BuilderRespawnTick);
                w.Write(p.HeroRespawnTick);
                w.Write(p.HeroXp);
                w.Write(p.HeroLevel);
                w.Write(p.HeroPathId);
                p.Wallet.WriteSnapshot(w);
            }

            state.Map.WriteSnapshot(w);

            w.Write(state.Buildings.Count);
            foreach (Building b in state.Buildings)
                WriteBuilding(w, b);
            w.Write(state.Units.Count);
            foreach (Unit u in state.Units)
                WriteUnit(w, u);

            foreach (CreepCamp camp in state.CreepCamps)
            {
                w.Write((byte)camp.State);
                w.Write(camp.LastDamageTick);
                w.Write(camp.TrophyPrimary);
                w.Write(camp.TrophySecondary);
                WriteIds(w, camp._attackers);
            }

            foreach (LegionAi ai in state.Legions)
            {
                w.Write(ai.NextWave);
                w.Write(ai.NextWaveTargetPlayerId);
                w.Write(ai.NextWaveWarned);
                w.Write(ai.NextWaveTick);
                w.Write(ai.NextWaveArrivalTick);
                w.Write(ai.DefendBuildingId);
                w.Write(ai._slots.Count);
                foreach (BaseSlot slot in ai._slots)
                {
                    w.Write(slot.BuildingId);
                    w.Write(slot.DestroyedTick);
                    w.Write(slot.WorkerId);
                }
                w.Write(ai._waves.Count);
                foreach (Wave wave in ai._waves)
                {
                    w.Write(wave.Number);
                    w.Write(wave.Target);
                    w.Write(wave.TargetPlayerId);
                    w.Write(wave.TargetId);
                    WriteIds(w, wave._unitIds);
                }
            }

            state.Fog.WriteSnapshot(w);
        }
        return stream.ToArray();
    }

    // state: as a new Simulation of the same MatchSetup made it. Throws InvalidDataException when
    // the snapshot is of another match, another version or damaged; the state is then unusable.
    public static void Read(SimState state, byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(stream, Encoding.UTF8);
            ReadHeader(r, state);

            int tick = r.ReadInt32();
            ulong rng = r.ReadUInt64();
            int nextEntityId = r.ReadInt32();
            var outcome = (MissionOutcome)r.ReadByte();

            foreach (Player p in state.Players)
            {
                p.BuilderRespawnTick = r.ReadInt32();
                p.HeroRespawnTick = r.ReadInt32();
                p.HeroXp = r.ReadInt32();
                p.HeroLevel = r.ReadInt32();
                p.HeroPathId = r.ReadString();
                p.Wallet.ReadSnapshot(r);
            }

            state.Map.ReadSnapshot(r);

            var buildings = new List<Building>();
            for (int n = Count(r, stream); n > 0; n--)
                buildings.Add(ReadBuilding(r, stream, state));
            var units = new List<Unit>();
            for (int n = Count(r, stream); n > 0; n--)
                units.Add(ReadUnit(r, stream, state));
            state.RestoreEntities(units, buildings, nextEntityId);

            foreach (CreepCamp camp in state.CreepCamps)
            {
                camp.State = (CreepCampState)r.ReadByte();
                camp.LastDamageTick = r.ReadInt32();
                camp.TrophyPrimary = r.ReadInt32();
                camp.TrophySecondary = r.ReadInt32();
                ReadIds(r, stream, camp._attackers);
            }

            foreach (LegionAi ai in state.Legions)
            {
                ai.NextWave = r.ReadInt32();
                ai.NextWaveTargetPlayerId = r.ReadInt32();
                ai.NextWaveWarned = r.ReadBoolean();
                ai.NextWaveTick = r.ReadInt32();
                ai.NextWaveArrivalTick = r.ReadInt32();
                ai.DefendBuildingId = r.ReadInt32();
                if (r.ReadInt32() != ai._slots.Count)
                    throw new InvalidDataException("Snapshot: another Legion base.");
                foreach (BaseSlot slot in ai._slots)
                {
                    slot.BuildingId = r.ReadInt32();
                    slot.DestroyedTick = r.ReadInt32();
                    slot.WorkerId = r.ReadInt32();
                }
                ai._waves.Clear();
                for (int n = Count(r, stream); n > 0; n--)
                {
                    int number = r.ReadInt32();
                    string target = r.ReadString();
                    int targetPlayerId = r.ReadInt32();
                    int targetId = r.ReadInt32();
                    var ids = new List<int>();
                    ReadIds(r, stream, ids);
                    ai._waves.Add(new Wave(number, target, targetPlayerId, ids) { TargetId = targetId });
                }
            }

            state.Fog.ReadSnapshot(r);
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Snapshot: trailing bytes.");

            state.Tick = tick;
            state.Rng.Restore(rng);
            state.Outcome = outcome;
        }
        catch (Exception e) when (!(e is InvalidDataException) && !(e is OutOfMemoryException))
        {
            // Truncated bytes, an id the data no longer has, a count that points nowhere.
            throw new InvalidDataException("The snapshot does not fit this match or this version of the game.", e);
        }
    }

    // What the snapshot must share with the match it is read into; anything else is another match.
    private static void WriteHeader(BinaryWriter w, SimState state)
    {
        w.Write(Magic);
        w.Write(Version);
        w.Write(state.Seed);
        w.Write(state.Map.Width);
        w.Write(state.Map.Height);
        w.Write(state.Map.LayoutChecksum);
        w.Write(state.ResourceTypeCount);
        w.Write(state.Players.Count);
        foreach (Player p in state.Players)
        {
            w.Write(p.Id);
            w.Write(p.Faction?.Id ?? "");
        }
        w.Write(state.CreepCamps.Count);
        w.Write(state.Legions.Count);
    }

    private static void ReadHeader(BinaryReader r, SimState state)
    {
        if (r.ReadInt32() != Magic)
            throw new InvalidDataException("Not a snapshot.");
        int version = r.ReadInt32();
        if (version != Version)
            throw new InvalidDataException($"Snapshot format {version}, expected {Version}.");
        bool same = r.ReadInt32() == state.Seed
            && r.ReadInt32() == state.Map.Width
            && r.ReadInt32() == state.Map.Height
            && r.ReadUInt64() == state.Map.LayoutChecksum
            && r.ReadInt32() == state.ResourceTypeCount
            && r.ReadInt32() == state.Players.Count;
        for (int i = 0; same && i < state.Players.Count; i++)
            same = r.ReadInt32() == state.Players[i].Id && r.ReadString() == (state.Players[i].Faction?.Id ?? "");
        same = same && r.ReadInt32() == state.CreepCamps.Count && r.ReadInt32() == state.Legions.Count;
        if (!same)
            throw new InvalidDataException("The snapshot is of another match: seed, map or players differ.");
    }

    private static void WriteEntity(BinaryWriter w, Entity e)
    {
        w.Write(e.Id);
        w.Write(e.OwnerId);
        w.Write(e.Hp);
        w.Write(e.MaxHp);
        w.Write(e.IsDead);
        w.Write(e.LastDamagedTick);
        w.Write(e.LastAttackerId);
        w.Write(e.RevealedUntilTick);
        w.Write(e.RevealedToPlayerId);
        w.Write(e.LastAlarmTick);
    }

    // After the id and the owner, which the constructor takes.
    private static void ReadEntity(BinaryReader r, Entity e)
    {
        e.Hp = r.ReadInt32();
        e.MaxHp = r.ReadInt32();
        e.IsDead = r.ReadBoolean();
        e.LastDamagedTick = r.ReadInt32();
        e.LastAttackerId = r.ReadInt32();
        e.RevealedUntilTick = r.ReadInt32();
        e.RevealedToPlayerId = r.ReadInt32();
        e.LastAlarmTick = r.ReadInt32();
    }

    private static void WriteBuilding(BinaryWriter w, Building b)
    {
        w.Write(b.Stats.Id);
        w.Write(b.CellX);
        w.Write(b.CellY);
        w.Write((byte)b.Direction);
        w.Write((byte)b.State);
        w.Write(b.BuildProgress);
        WriteEntity(w, b);
        w.Write(b.BuildersLastTick);
        w.Write(b.DemolishTicksLeft);
        w.Write(b.CaptureProgress);
        w.Write(b.CapturePlayerId);
        w.Write(b.GuardCampIndex);
        w.Write(b.LitForPlayerId);
        w.Write(b.RegenAccumulator);
        w.Write(b.BuildSpeedAccumulator);

        w.Write(b.Drill != null);
        if (b.Drill != null)
        {
            w.Write(b.Drill.ResourceType);
            w.Write(b.Drill.OreCells);
            w.Write(b.Drill.Buffer);
            w.Write(b.Drill.Accumulator);
            w.Write(b.Drill.OutputCursor);
            w.Write(b.Drill.IsStopped);
        }
        w.Write(b.Conveyor != null);
        if (b.Conveyor != null)
        {
            w.Write(b.Conveyor.ItemType);
            w.Write(b.Conveyor.ItemPos);
        }
        w.Write(b.Bridge != null);
        if (b.Bridge != null)
        {
            w.Write(b.Bridge.LinkedToId);
            w.Write(b.Bridge.LinkedFromId);
            w.Write(b.Bridge.IsOutputEnd);
            w.Write(b.Bridge._items.Count);
            foreach (BridgeItem item in b.Bridge._items)
            {
                w.Write(item.Type);
                w.Write(item.Pos);
            }
        }
        w.Write(b.Stock != null);
        b.Stock?.WriteSnapshot(w);
        w.Write(b.Factory != null);
        if (b.Factory != null)
        {
            w.Write(b.Factory.Progress);
            w.Write(b.Factory.HasRally);
            w.Write(b.Factory.RallyX);
            w.Write(b.Factory.RallyY);
            w.Write(b.Factory._queue.Count);
            foreach (string unit in b.Factory._queue)
                w.Write(unit);
        }
        w.Write(b.Turret != null);
        if (b.Turret != null)
        {
            w.Write(b.Turret.Cooldown);
            w.Write(b.Turret.TargetId);
        }
        w.Write(b.Storage != null);
        if (b.Storage != null)
        {
            w.Write(b.Storage.Primary);
            w.Write(b.Storage.Secondary);
            w.Write(b.Storage.OutputCursor);
        }
    }

    private static Building ReadBuilding(BinaryReader r, Stream stream, SimState state)
    {
        BuildingStats stats = state.Data.Building(r.ReadString());
        int cellX = r.ReadInt32(), cellY = r.ReadInt32();
        var direction = (Direction)r.ReadByte();
        var buildState = (BuildState)r.ReadByte();
        int progress = r.ReadInt32();
        int id = r.ReadInt32(), owner = r.ReadInt32();
        if (!state.HasPlayer(owner) || !state.Map.InBounds(cellX, cellY) || !state.Map.InBounds(cellX + stats.SizeW - 1, cellY + stats.SizeH - 1))
            throw new InvalidDataException($"Snapshot: building {id} of player {owner} at ({cellX}, {cellY}).");

        var b = new Building(id, owner, stats, cellX, cellY, direction, buildState == BuildState.Complete, state.Rules.ConstructionStartHpPercent);
        b.RestoreBuild(buildState, progress);
        ReadEntity(r, b);
        b.BuildersLastTick = r.ReadInt32();
        b.DemolishTicksLeft = r.ReadInt32();
        b.CaptureProgress = r.ReadInt32();
        b.CapturePlayerId = r.ReadInt32();
        b.GuardCampIndex = r.ReadInt32();
        b.LitForPlayerId = r.ReadInt32();
        b.RegenAccumulator = r.ReadInt32();
        b.BuildSpeedAccumulator = r.ReadInt32();

        // The parts its kind has by the current data; a bridge keeps the links it was saved with.
        BuildingComponents.Attach(state, b, link: false);
        if (r.ReadBoolean() != (b.Drill != null))
            throw Mismatch(b);
        if (b.Drill != null)
        {
            b.Drill = new Drill(r.ReadInt32(), r.ReadInt32());
            b.Drill.Buffer = r.ReadInt32();
            b.Drill.Accumulator = r.ReadInt32();
            b.Drill.OutputCursor = r.ReadInt32();
            b.Drill.IsStopped = r.ReadBoolean();
        }
        if (r.ReadBoolean() != (b.Conveyor != null))
            throw Mismatch(b);
        if (b.Conveyor != null)
        {
            b.Conveyor.ItemType = r.ReadInt32();
            b.Conveyor.ItemPos = r.ReadInt32();
        }
        if (r.ReadBoolean() != (b.Bridge != null))
            throw Mismatch(b);
        if (b.Bridge != null)
        {
            b.Bridge.LinkedToId = r.ReadInt32();
            b.Bridge.LinkedFromId = r.ReadInt32();
            b.Bridge.IsOutputEnd = r.ReadBoolean();
            for (int n = Count(r, stream); n > 0; n--)
                b.Bridge._items.Add(new BridgeItem(r.ReadInt32(), r.ReadInt32()));
        }
        if (r.ReadBoolean() != (b.Stock != null))
            throw Mismatch(b);
        b.Stock?.ReadSnapshot(r);
        if (r.ReadBoolean() != (b.Factory != null))
            throw Mismatch(b);
        if (b.Factory != null)
        {
            b.Factory.Progress = r.ReadInt32();
            b.Factory.HasRally = r.ReadBoolean();
            b.Factory.RallyX = r.ReadInt32();
            b.Factory.RallyY = r.ReadInt32();
            for (int n = Count(r, stream); n > 0; n--)
                b.Factory._queue.Add(state.Data.Unit(r.ReadString()).Id);
        }
        if (r.ReadBoolean() != (b.Turret != null))
            throw Mismatch(b);
        if (b.Turret != null)
        {
            b.Turret.Cooldown = r.ReadInt32();
            b.Turret.TargetId = r.ReadInt32();
        }
        if (r.ReadBoolean() != (b.Storage != null))
            throw Mismatch(b);
        if (b.Storage != null)
        {
            b.Storage.Primary = r.ReadInt32();
            b.Storage.Secondary = r.ReadInt32();
            b.Storage.OutputCursor = r.ReadInt32();
        }
        return b;
    }

    private static InvalidDataException Mismatch(Building b) =>
        new InvalidDataException($"Snapshot: \"{b.Stats.Id}\" is another kind of building in the current data.");

    private static void WriteUnit(BinaryWriter w, Unit u)
    {
        w.Write(u.Stats.Id);
        w.Write(u.X);
        w.Write(u.Y);
        WriteEntity(w, u);
        WriteOrder(w, u.Order);
        w.Write(u._queue.Count);
        foreach (QueuedOrder queued in u._queue)
        {
            WriteOrder(w, queued.Order);
            WritePlans(w, queued.Plans);
        }
        w.Write(u.AttackCooldown);
        w.Write(u.TargetId);
        w.Write(u.CampIndex);
        w.Write(u.HomeX);
        w.Write(u.HomeY);
        w.Write(u.RegenAccumulator);
        w.Write(u.RepairHpAccumulator);
        w.Write(u.RepairCostAccumulator);
        w.Write(u.RepairCostAccumulatorSecondary);
        w.Write(u.BoostTicksLeft);
        w.Write(u.BoostMovePercent);
        w.Write(u.BoostAttackPercent);
        w.Write(u.LastAttackTick);
        w.Write(u._abilityCooldowns.Length);
        foreach (int cooldown in u._abilityCooldowns)
            w.Write(cooldown);

        Worker? worker = u.Worker;
        w.Write(worker != null);
        if (worker == null)
            return;
        w.Write(worker.CargoType);
        w.Write(worker.Cargo);
        w.Write(worker.MiningAccumulator);
        w.Write(worker.DeliverToId);
        w.Write((byte)worker.Phase);
        w.Write(worker.CurrentPlanKey);
        WritePlans(w, worker._plans);
    }

    private static Unit ReadUnit(BinaryReader r, Stream stream, SimState state)
    {
        UnitStats stats = state.Data.Unit(r.ReadString());
        int x = r.ReadInt32(), y = r.ReadInt32();
        int id = r.ReadInt32(), owner = r.ReadInt32();
        if (!state.HasPlayer(owner))
            throw new InvalidDataException($"Snapshot: unit {id} of unknown player {owner}.");

        var u = new Unit(id, owner, stats, x, y);
        ReadEntity(r, u);
        u.Order = ReadOrder(r);
        for (int n = Count(r, stream); n > 0; n--)
        {
            UnitOrder order = ReadOrder(r);
            List<PlannedBuild> plans = ReadPlans(r, stream, state);
            u._queue.Add(new QueuedOrder(order, plans.Count > 0 ? plans.ToArray() : null));
        }
        u.AttackCooldown = r.ReadInt32();
        u.TargetId = r.ReadInt32();
        u.CampIndex = r.ReadInt32();
        u.HomeX = r.ReadInt32();
        u.HomeY = r.ReadInt32();
        u.RegenAccumulator = r.ReadInt32();
        u.RepairHpAccumulator = r.ReadInt32();
        u.RepairCostAccumulator = r.ReadInt32();
        u.RepairCostAccumulatorSecondary = r.ReadInt32();
        u.BoostTicksLeft = r.ReadInt32();
        u.BoostMovePercent = r.ReadInt32();
        u.BoostAttackPercent = r.ReadInt32();
        u.LastAttackTick = r.ReadInt32();
        if (r.ReadInt32() != u._abilityCooldowns.Length)
            throw new InvalidDataException($"Snapshot: \"{stats.Id}\" has other abilities in the current data.");
        for (int i = 0; i < u._abilityCooldowns.Length; i++)
            u._abilityCooldowns[i] = r.ReadInt32();
        if (u.CampIndex >= state.CreepCamps.Count)
            throw new InvalidDataException($"Snapshot: unit {id} of unknown camp {u.CampIndex}.");

        Worker? worker = u.Worker;
        if (r.ReadBoolean() != (worker != null))
            throw new InvalidDataException($"Snapshot: \"{stats.Id}\" is or is not a worker in the current data.");
        if (worker == null)
            return u;
        worker.CargoType = r.ReadInt32();
        worker.Cargo = r.ReadInt32();
        worker.MiningAccumulator = r.ReadInt32();
        worker.DeliverToId = r.ReadInt32();
        worker.Phase = (WorkerPhase)r.ReadByte();
        worker.CurrentPlanKey = r.ReadInt32();
        worker._plans.AddRange(ReadPlans(r, stream, state));
        return u;
    }

    private static void WriteOrder(BinaryWriter w, UnitOrder order)
    {
        w.Write((byte)order.Kind);
        w.Write(order.X);
        w.Write(order.Y);
        w.Write(order.TargetId);
        w.Write(order.IsAuto);
    }

    private static UnitOrder ReadOrder(BinaryReader r) =>
        UnitOrder.Restore((UnitOrderKind)r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadBoolean());

    private static void WritePlans(BinaryWriter w, IReadOnlyList<PlannedBuild> plans)
    {
        w.Write(plans.Count);
        foreach (PlannedBuild plan in plans)
        {
            w.Write(plan.BuildingTypeId);
            w.Write(plan.CellX);
            w.Write(plan.CellY);
            w.Write((byte)plan.Direction);
            w.Write(plan.ExistingSiteId);
        }
    }

    private static List<PlannedBuild> ReadPlans(BinaryReader r, Stream stream, SimState state)
    {
        var plans = new List<PlannedBuild>();
        for (int n = Count(r, stream); n > 0; n--)
        {
            string type = state.Data.Building(r.ReadString()).Id;
            var plan = new PlannedBuild(type, r.ReadInt32(), r.ReadInt32(), (Direction)r.ReadByte());
            plan.ExistingSiteId = r.ReadInt32();
            plans.Add(plan);
        }
        return plans;
    }

    private static void WriteIds(BinaryWriter w, List<int> ids)
    {
        w.Write(ids.Count);
        foreach (int id in ids)
            w.Write(id);
    }

    private static void ReadIds(BinaryReader r, Stream stream, List<int> ids)
    {
        ids.Clear();
        for (int n = Count(r, stream); n > 0; n--)
            ids.Add(r.ReadInt32());
    }

    // A count of items that each take at least a byte: damaged bytes cannot ask for a huge list.
    private static int Count(BinaryReader r, Stream stream)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > stream.Length - stream.Position)
            throw new InvalidDataException($"Snapshot: a list of {count} items.");
        return count;
    }
}
