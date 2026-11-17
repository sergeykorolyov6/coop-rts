using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/save-load.md: the state snapshot (T5, rows S)
public class SnapshotTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Minute = 60 * Second;

    private static readonly CommandCodec Codec = GameCommands.CreateCodec();

    private static Simulation Restored(Simulation sim, MatchSetup setup) =>
        Simulation.FromSnapshot(setup, sim.WriteSnapshot(), sim.CommandLog);

    private static void AssertInStep(Simulation a, Simulation b, int ticks, System.Action<Simulation>? orders = null)
    {
        Assert.That(b.State.Tick, Is.EqualTo(a.State.Tick));
        Assert.That(b.StateHash, Is.EqualTo(a.StateHash), "right after the restore");
        orders?.Invoke(a);
        orders?.Invoke(b);
        for (int t = 0; t < ticks; t++)
        {
            a.Tick();
            b.Tick();
            Assert.That(b.StateHash, Is.EqualTo(a.StateHash), $"tick {a.State.Tick}");
        }
    }

    // Веха U: a builder waiting for resources, a repeating factory with an unpaid place and a pass
    // cursor, ghosts, an ally's silhouette over one of them, a belt cell that remembers who entered
    // last — all of it comes back from a snapshot.
    [Test]
    public void ConvenienceMilestoneState_RestoresInStep()
    {
        const int a = Setups.PlayerA, b = Setups.PlayerB;
        MatchSetup setup = Setups.Coop(new MapSetup(32, 16).Start(a, 2, 2).Start(b, 26, 2));
        var sim = new Simulation(setup);
        SimState s = sim.State;
        Spawn(sim, a, "scout", 10, 9);
        Spawn(sim, b, "scout", 16, 10);
        sim.Tick();
        BuildingStats belt = s.Data.Building("conveyor");

        Building factory = s.PlaceBuilding(a, s.Data.Building("factory"), 8, 11, Direction.Right, complete: true);
        factory.Factory!.Repeat = true;
        factory.Factory.Enqueue("scout", paid: false);
        factory.PassCursor = 3;

        Building merge = s.PlaceBuilding(a, belt, 14, 8, Direction.Left, complete: true);
        s.PlaceBuilding(a, belt, 14, 7, Direction.Up, complete: true);
        merge.Conveyor!.LastFeeder = Conveyor.FromLeft;

        foreach (int x in new[] { 20, 22 })
        {
            Building ruined = s.PlaceBuilding(a, belt, x, 12, Direction.Up, complete: true);
            GhostRules.Leave(s, ruined);
            Kill(ruined);
        }
        s.RemoveDead();
        Unit allyBuilder = s.FindBuilder(b)!;
        sim.Enqueue(new RebuildCommand(b, s.Tick, new[] { allyBuilder.Id }, 20, 12, 20, 12, includeAllies: true));

        Wallet wallet = s.GetPlayer(a).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary));
        Unit builder = s.FindBuilder(a)!;
        sim.Enqueue(new BuildCommand(a, s.Tick, new[] { builder.Id }, "storage", new[] { new BuildSite(6, 8, Direction.Right) }));
        sim.Run(40);

        Assert.That(builder.Worker!.WaitingForResources, Is.True);
        Assert.That(allyBuilder.Worker!.Plans.Select(p => p.OwnerId), Is.EqualTo(new[] { a }));
        Assert.That(s.Ghosts, Has.Count.EqualTo(2));
        Assert.That(factory.Factory.Paid, Is.EqualTo(new[] { false }));

        Simulation restored = Restored(sim, setup);
        Building factoryR = restored.State.FindBuilding(factory.Id)!;
        Assert.That((factoryR.Factory!.Repeat, factoryR.PassCursor), Is.EqualTo((true, 3)));
        Assert.That(restored.State.FindBuilding(merge.Id)!.Conveyor!.LastFeeder, Is.EqualTo(Conveyor.FromLeft));
        // The ally's builder reaches the ghost and rebuilds it for its owner within these seconds.
        AssertInStep(sim, restored, 15 * Second);
        Assert.That(s.FindBuilding(s.Map.BuildingAt(20, 12))?.OwnerId, Is.EqualTo(a));
    }

    // The M2 mission three minutes in: one of every kind of building, a factory at work, belts with
    // cargo, a builder with a queue of orders, the Legion's wave on its way.
    private static Simulation RichM2(out MatchSetup setup)
    {
        setup = RepoData.Setup("m2", 7);
        var sim = new Simulation(setup);
        SimState s = sim.State;
        Unit builder = s.FindBuilder(1)!;
        Building factory = s.PlaceBuilding(1, s.Data.Building("factory"), 10, 30, Direction.Right, complete: true);
        factory.Stock!.Add(Resources.Primary, 150);
        s.PlaceBuilding(1, s.Data.Building("storage"), 14, 30, Direction.Right, complete: true);
        s.PlaceBuilding(1, s.Data.Building("conveyor_bridge"), 17, 30, Direction.Right, complete: true);
        s.PlaceBuilding(1, s.Data.Building("conveyor_bridge"), 20, 30, Direction.Right, complete: true);
        s.PlaceBuilding(1, s.Data.Building("conveyor"), 21, 30, Direction.Right, complete: true);
        s.PlaceBuilding(1, s.Data.Building("turret"), 24, 30, Direction.Right, complete: true);
        s.GetPlayer(1).Wallet.Add(Resources.Primary, 300);
        sim.Enqueue(new ProduceCommand(1, 0, factory.Id, "infantry"));
        sim.Enqueue(new ProduceCommand(1, 0, factory.Id, "miner"));
        sim.Enqueue(new SetRallyCommand(1, 0, factory.Id, true, Center(16), Center(26)));
        sim.Enqueue(new BuildCommand(1, 0, new[] { builder.Id }, "drill", new[] { new BuildSite(12, 19, Direction.Right) }));
        sim.Enqueue(new BuildCommand(1, 1, new[] { builder.Id }, "conveyor",
            new[] { new BuildSite(14, 19, Direction.Right), new BuildSite(15, 19, Direction.Right), new BuildSite(16, 19, Direction.Right) }, queued: true));
        sim.Enqueue(new MineCommand(1, 2, new[] { builder.Id }, 13, 22, queued: true));
        sim.RunUntil(x => x.State.Legion!.Waves.Count > 0, 5 * Minute);
        sim.Run(3 * Second);
        Assert.That(s.Legion!.Waves, Is.Not.Empty, "the wave is still on its way");
        return sim;
    }

    [Test]
    public void S1_SaveThenLoadFromTheSnapshot_SameTickHashAndTheWholeLog()
    {
        Simulation sim = RichM2(out MatchSetup setup);

        Simulation loaded = Restored(sim, setup);

        Assert.That(loaded.State.Tick, Is.EqualTo(sim.State.Tick));
        Assert.That(loaded.StateHash, Is.EqualTo(sim.StateHash));
        Assert.That(loaded.CommandLog, Has.Count.EqualTo(sim.CommandLog.Count));
        Assert.That(loaded.State.Units.Select(u => (u.Id, u.Stats.Id, u.X, u.Y, u.Hp)),
            Is.EqualTo(sim.State.Units.Select(u => (u.Id, u.Stats.Id, u.X, u.Y, u.Hp))));
        Assert.That(loaded.State.Buildings.Select(b => (b.Id, b.Stats.Id, b.CellX, b.CellY, b.State)),
            Is.EqualTo(sim.State.Buildings.Select(b => (b.Id, b.Stats.Id, b.CellX, b.CellY, b.State))));
        Assert.That(loaded.State.NextEntityId, Is.EqualTo(sim.State.NextEntityId));
    }

    [Test]
    public void S2_LoadedAndOriginal_StayInStepForTwoMinutes()
    {
        Simulation sim = RichM2(out MatchSetup setup);
        Simulation loaded = Restored(sim, setup);
        Unit builder = sim.State.FindBuilder(1)!;

        AssertInStep(sim, loaded, 2 * Minute, s =>
        {
            s.Enqueue(new MoveCommand(1, s.State.Tick, new[] { builder.Id }, Center(20), Center(20)));
            s.Enqueue(new BuildCommand(1, s.State.Tick + 10 * Second, new[] { builder.Id }, "turret", new[] { new BuildSite(22, 22, Direction.Right) }));
        });
    }

    [Test]
    public void S3_DugRockOreAndDeposits_ComeBackTheSame()
    {
        MatchSetup setup = RepoData.Setup("heights", 5);
        var sim = new Simulation(setup);
        // heights.txt: ore rock at (2,26) and (3,26), crystal rock at (2,27), soft rock at (20..23, 27..28).
        // A miner sees rock only from beside it: rock hides what lies behind (terrain.md).
        Unit miner = Spawn(sim, 1, "miner", 2, 25);
        Unit second = Spawn(sim, 1, "miner", 21, 26);
        sim.Enqueue(new DigCommand(1, 0, new[] { miner.Id }, 2, 26));
        sim.Enqueue(new DigCommand(1, 0, new[] { second.Id }, 21, 27));
        sim.RunUntil(s => s.State.Map.OreAt(2, 26) == Resources.Primary, 3 * Minute);
        sim.Enqueue(new DigCommand(1, sim.State.Tick, new[] { miner.Id }, 2, 27));
        sim.Run(3 * Second);
        Assert.That(sim.State.Map.RockHp(2, 27), Is.LessThan(sim.State.Data.TerrainBySymbol('C')!.Hp));
        Assert.That(sim.State.Map.RockHp(2, 27), Is.GreaterThan(0), "the next rock is only cracked: partial damage is in the snapshot too");

        Simulation loaded = Restored(sim, setup);

        MapState a = sim.State.Map, b = loaded.State.Map;
        Assert.That(b.RockChecksum, Is.EqualTo(a.RockChecksum));
        Assert.That(b.Deposits.Select(d => (d.Id, d.ResourceType, d.Cells.Count)), Is.EqualTo(a.Deposits.Select(d => (d.Id, d.ResourceType, d.Cells.Count))));
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                Assert.That((b.RockHp(x, y), b.IsBlocked(x, y), b.OreAt(x, y), b.DepositAt(x, y), b.TerrainAt(x, y)?.Id, b.BuildingAt(x, y), b.IsPassable(x, y)),
                    Is.EqualTo((a.RockHp(x, y), a.IsBlocked(x, y), a.OreAt(x, y), a.DepositAt(x, y), a.TerrainAt(x, y)?.Id, a.BuildingAt(x, y), a.IsPassable(x, y))), $"cell ({x}, {y})");
                Assert.That(loaded.State.Fog.IsVisible(1, x, y), Is.EqualTo(sim.State.Fog.IsVisible(1, x, y)), $"sight of ({x}, {y})");
                Assert.That(loaded.State.Fog.IsDiscovered(1, x, y), Is.EqualTo(sim.State.Fog.IsDiscovered(1, x, y)), $"discovered ({x}, {y})");
            }
        }
        AssertInStep(sim, loaded, Minute);
    }

    [Test]
    public void S4_LobbyFactions_HeroPathAndXp_AlertedCamp_StayInStep()
    {
        MatchSetup Setup() => RepoData.Setup("coop", 3, new[] { "yellow_star", "brown_scavengers" });
        var sim = new Simulation(Setup());
        Unit hero = sim.State.FindHero(1)!;
        CreepCamp camp = sim.State.CreepCamps.OrderBy(c => Movement.Distance(hero.X, hero.Y, c.CenterMilliX, c.CenterMilliY)).First();
        sim.Enqueue(new ChooseHeroPathCommand(1, 5, "ys_warrior"));
        sim.Enqueue(new MoveCommand(1, 6, new[] { hero.Id }, camp.CenterMilliX, camp.CenterMilliY));
        sim.RunUntil(s => camp.State != CreepCampState.Calm, 3 * Minute);
        sim.Run(4 * Second);

        Simulation loaded = Restored(sim, Setup());

        Assert.That(loaded.State.GetPlayer(1).HeroPathId, Is.EqualTo("ys_warrior"));
        Assert.That(loaded.State.GetPlayer(2).Faction!.Id, Is.EqualTo("brown_scavengers"));
        Assert.That(loaded.State.CreepCamps[camp.Index].State, Is.EqualTo(camp.State));
        Assert.That(loaded.State.CreepCamps[camp.Index].Attackers, Is.EqualTo(camp.Attackers));
        AssertInStep(sim, loaded, Minute);
        Assert.That(loaded.State.GetPlayer(1).HeroXp, Is.EqualTo(sim.State.GetPlayer(1).HeroXp));
    }

    // Two minutes of a player who builds, mines and walks: only commands, so a replay gives the same match.
    private static Simulation Played(int seed = 7)
    {
        var sim = new Simulation(RepoData.Setup("m2", seed));
        Unit builder = sim.State.FindBuilder(1)!;
        sim.Enqueue(new BuildCommand(1, 0, new[] { builder.Id }, "drill", new[] { new BuildSite(12, 19, Direction.Right) }));
        sim.Enqueue(new MineCommand(1, 0, new[] { builder.Id }, 13, 20, queued: true));
        sim.Run(2 * Minute);
        return sim;
    }

    [Test]
    public void S5_BrokenSnapshot_LoadsByReplayingTheLog()
    {
        Simulation sim = Played();
        SaveGame good = SaveGame.Read(SaveGame.Capture(sim, "m2", 7, Codec).Write());
        Assert.That(good.HasSnapshot, Is.True);
        Assert.That(good.TryRestoreSnapshot(RepoData.Setup("m2", 7), Codec)!.StateHash, Is.EqualTo(sim.StateHash));

        byte[] broken = good.Snapshot.ToArray();
        for (int i = broken.Length / 2; i < broken.Length / 2 + 64; i++)
            broken[i] ^= 0x5A;
        var save = new SaveGame(good.MissionId, good.Seed, good.Tick, good.StateHash, good.CommandLog, good.Factions, broken);
        var cut = new SaveGame(good.MissionId, good.Seed, good.Tick, good.StateHash, good.CommandLog, good.Factions, good.Snapshot.Take(100).ToArray());

        foreach (SaveGame bad in new[] { save, cut })
        {
            Assert.That(bad.TryRestoreSnapshot(RepoData.Setup("m2", 7), Codec), Is.Null);
            Simulation loaded = bad.Restore(RepoData.Setup("m2", 7), Codec);
            Assert.That(loaded.StateHash, Is.EqualTo(sim.StateHash));
            Assert.That(loaded.State.Tick, Is.EqualTo(sim.State.Tick));
        }
    }

    [Test]
    public void S6_FormatTwoFile_HasNoSnapshot_LoadsByReplay()
    {
        Simulation sim = Played();
        byte[] log = Codec.WriteLog(sim.CommandLog);
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("COOPSAVE"));
            w.Write(2);
            w.Write("m2");
            w.Write(7);
            w.Write(1);
            w.Write("blue_walls");
            w.Write(sim.State.Tick);
            w.Write(sim.StateHash);
            w.Write(log.Length);
            w.Write(log);
        }

        SaveGame save = SaveGame.Read(stream.ToArray());

        Assert.That(save.HasSnapshot, Is.False);
        Assert.That(save.TryRestoreSnapshot(RepoData.Setup("m2", 7), Codec), Is.Null);
        Assert.That(save.Restore(RepoData.Setup("m2", 7, save.FactionsOrNull), Codec).StateHash, Is.EqualTo(sim.StateHash));
    }

    [Test]
    public void S7_GameLoadedFromASnapshot_StillRecordsAReplayFromTickZero()
    {
        Simulation sim = Played();
        SaveGame save = SaveGame.Read(SaveGame.Capture(sim, "m2", 7, Codec).Write());
        Simulation loaded = save.TryRestoreSnapshot(RepoData.Setup("m2", 7), Codec)!;
        Unit builder = loaded.State.FindBuilder(1)!;
        loaded.Enqueue(new MoveCommand(1, loaded.State.Tick, new[] { builder.Id }, Center(20), Center(20)));
        loaded.Run(Minute);

        SaveGame record = SaveGame.Read(SaveGame.Capture(loaded, "m2", 7, Codec, snapshot: false).Write());
        Simulation replay = record.BeginReplay(RepoData.Setup("m2", 7), Codec);
        Assert.That(replay.State.Tick, Is.Zero);
        replay.FastForward(record.Tick);

        Assert.That(record.Matches(replay), Is.True);
    }

    [Test]
    public void S8_CommandForAFutureTick_RunsOnItsTickAfterTheLoad()
    {
        MatchSetup setup = RepoData.Setup("m2", 7);
        var sim = new Simulation(setup);
        sim.Run(5 * Second);
        Unit builder = sim.State.FindBuilder(1)!;
        int startX = builder.X;
        sim.Enqueue(new MoveCommand(1, sim.State.Tick + 2 * Second, new[] { builder.Id }, builder.X + Cells(5), builder.Y));

        Simulation loaded = Restored(sim, setup);
        loaded.Run(2 * Second);
        Assert.That(loaded.State.FindBuilder(1)!.X, Is.EqualTo(startX), "not before its tick");
        loaded.Run(3 * Second);
        Assert.That(loaded.State.FindBuilder(1)!.X, Is.GreaterThan(startX + Cells(2)));

        sim.Run(5 * Second);
        Assert.That(loaded.StateHash, Is.EqualTo(sim.StateHash));
        Assert.That(loaded.CommandLog, Has.Count.EqualTo(sim.CommandLog.Count));
    }

    [Test]
    public void S9_SnapshotOfAnotherMissionOrSeed_IsAFormatError()
    {
        Simulation sim = Played();
        byte[] snapshot = sim.WriteSnapshot();

        Assert.Throws<InvalidDataException>(() => Simulation.FromSnapshot(RepoData.Setup("coop", 7), snapshot, sim.CommandLog));
        Assert.Throws<InvalidDataException>(() => Simulation.FromSnapshot(RepoData.Setup("m2", 8), snapshot, sim.CommandLog));
        Assert.Throws<InvalidDataException>(() => Simulation.FromSnapshot(RepoData.Setup("m2_red", 7), snapshot, sim.CommandLog),
            "the same map with another faction is another match");
        Assert.Throws<InvalidDataException>(() => Simulation.FromSnapshot(RepoData.Setup("m2", 7), new byte[] { 1, 2, 3 }, sim.CommandLog));
    }

    [Test]
    public void Edge_UnitTheDataNoLongerHas_SnapshotDoesNotFit()
    {
        Simulation sim = Played();
        byte[] snapshot = sim.WriteSnapshot();
        // The builder's type id, renamed in place to one units.json does not have.
        byte[] name = System.Text.Encoding.UTF8.GetBytes("builder");
        int at = Enumerable.Range(0, snapshot.Length - name.Length).First(i => name.Select((c, k) => snapshot[i + k] == c).All(same => same));
        snapshot[at] = (byte)'x';

        Assert.Throws<InvalidDataException>(() => Simulation.FromSnapshot(RepoData.Setup("m2", 7), snapshot, sim.CommandLog));
    }

    [Test]
    public void Edge_Replay_IsWrittenWithoutASnapshot_ASaveWithOne()
    {
        Simulation sim = Played();

        SaveGame replay = SaveGame.Read(SaveGame.Capture(sim, "m2", 7, Codec, snapshot: false).Write());
        SaveGame save = SaveGame.Read(SaveGame.Capture(sim, "m2", 7, Codec).Write());

        Assert.That(replay.HasSnapshot, Is.False);
        Assert.That(save.HasSnapshot, Is.True);
        Assert.That(save.Snapshot.Length, Is.LessThan(200_000), "an 80×80 match two minutes in");
    }

    [Test]
    public void Edge_SnapshotTwice_SameBytes_AndASnapshotOfTheRestoredMatchToo()
    {
        Simulation sim = RichM2(out MatchSetup setup);

        byte[] first = sim.WriteSnapshot();
        Assert.That(sim.WriteSnapshot(), Is.EqualTo(first));
        Assert.That(Restored(sim, setup).WriteSnapshot(), Is.EqualTo(first));
    }

    // Every mission of the game half a minute in: all the maps, factions, camps and neutral buildings.
    [TestCaseSource(nameof(MissionIds))]
    public void EveryMission_RestoresAndStaysInStep(string missionId)
    {
        var sim = new Simulation(RepoData.Setup(missionId, 11));
        foreach (int player in sim.State.TeamPlayerIds)
        {
            Unit builder = sim.State.FindBuilder(player)!;
            sim.Enqueue(new MoveCommand(player, 0, new[] { builder.Id }, builder.X + Cells(6), builder.Y + Cells(3)));
        }
        sim.Run(30 * Second);

        Simulation loaded = Restored(sim, RepoData.Setup(missionId, 11));

        AssertInStep(sim, loaded, 20 * Second);
    }

    private static IEnumerable<string> MissionIds() => RepoData.Catalog().Missions.Select(m => m.Id);
}
