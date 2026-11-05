using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/save-load.md
public class SaveGameTests
{
    private const int Second = Simulation.TicksPerSecond;

    private static MatchSetup Setup(string missionId, int seed)
    {
        MissionStats mission = CoopMissionTests.Catalog().Find(missionId)!;
        return MissionLauncher.CreateSetup(RepoData.Get(), mission, CoopMissionTests.MapJson(mission.Map), seed);
    }

    // Two minutes of a player who builds, mines and orders units around, on the M2 mission.
    private static Simulation Played(int seed = 7)
    {
        var sim = new Simulation(Setup("m2", seed));
        Unit builder = sim.State.UnitsOf(1).Single();
        Building hq = sim.State.FindHeadquarters(1)!;
        sim.Enqueue(new BuildCommand(1, 0, new[] { builder.Id }, "drill", new[] { new BuildSite(12, 19, Direction.Right) }));
        sim.Enqueue(new MineCommand(1, 0, new[] { builder.Id }, 13, 20, queued: true));
        sim.Run(60 * Second);
        sim.Enqueue(new MoveCommand(1, sim.State.Tick, new[] { builder.Id }, Grid.CellCenter(hq.CellX + 6), Grid.CellCenter(hq.CellY)));
        sim.Run(60 * Second);
        return sim;
    }

    [Test]
    public void GWT1_SaveThenLoad_SameTickAndStateHash()
    {
        Simulation sim = Played();
        CommandCodec codec = GameCommands.CreateCodec();
        byte[] file = SaveGame.Capture(sim, "m2", 7, codec).Write();

        SaveGame save = SaveGame.Read(file);
        Assert.That(save.MissionId, Is.EqualTo("m2"));
        Simulation loaded = save.Restore(Setup(save.MissionId, save.Seed), codec);
        Assert.That(loaded.State.Tick, Is.EqualTo(sim.State.Tick));
        Assert.That(loaded.StateHash, Is.EqualTo(sim.StateHash));
        Assert.That(loaded.CommandLog, Has.Count.EqualTo(sim.CommandLog.Count));
    }

    [Test]
    public void GWT2_LoadedAndOriginal_StayInStepWithTheSameCommands()
    {
        Simulation sim = Played();
        CommandCodec codec = GameCommands.CreateCodec();
        Simulation loaded = SaveGame.Read(SaveGame.Capture(sim, "m2", 7, codec).Write()).Restore(Setup("m2", 7), codec);
        Unit builder = sim.State.UnitsOf(1).Single(u => u.IsBuilder);
        foreach (Simulation s in new[] { sim, loaded })
            s.Enqueue(new MoveCommand(1, s.State.Tick, new[] { builder.Id }, Grid.CellCenter(20), Grid.CellCenter(20)));
        for (int t = 0; t < 60 * Second; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.That(loaded.StateHash, Is.EqualTo(sim.StateHash));
        }
    }

    [Test]
    public void GWT3_BrokenFile_IsAFormatError()
    {
        Simulation sim = Played();
        byte[] file = SaveGame.Capture(sim, "m2", 7, GameCommands.CreateCodec()).Write();
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(file.Take(file.Length - 3).ToArray()));
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(new byte[] { 1, 2, 3 }));
        SaveGame good = SaveGame.Read(file);
        var wrongHash = new SaveGame(good.MissionId, good.Seed, good.Tick, good.StateHash ^ 1, good.CommandLog);
        Assert.Throws<InvalidDataException>(() => wrongHash.Restore(Setup("m2", 7), GameCommands.CreateCodec()));
    }

    private static MatchSetup Setup(string missionId, int seed, System.Collections.Generic.IReadOnlyList<string>? factions)
    {
        MissionStats mission = CoopMissionTests.Catalog().Find(missionId)!;
        return MissionLauncher.CreateSetup(RepoData.Get(), mission, CoopMissionTests.MapJson(mission.Map), seed, factions);
    }

    [Test]
    public void GWT4_LobbyFactions_SavedAndRestored()
    {
        var factions = new[] { "yellow_star", "brown_scavengers" };
        var sim = new Simulation(Setup("coop", 3, factions));
        sim.Enqueue(new ChooseHeroPathCommand(1, 5, "ys_mage"));
        sim.Run(30 * Second);
        CommandCodec codec = GameCommands.CreateCodec();

        SaveGame save = SaveGame.Read(SaveGame.Capture(sim, "coop", 3, codec).Write());
        Assert.That(save.Factions, Is.EqualTo(factions));
        Simulation loaded = save.Restore(Setup(save.MissionId, save.Seed, save.FactionsOrNull), codec);
        Assert.That(loaded.StateHash, Is.EqualTo(sim.StateHash));
        Assert.That(loaded.State.GetPlayer(2).Faction!.Id, Is.EqualTo("brown_scavengers"));
        Assert.That(loaded.State.GetPlayer(1).HeroPathId, Is.EqualTo("ys_mage"));
    }

    [Test]
    public void GWT5_FormatOneFile_Loads_FactionsFromMission()
    {
        Simulation sim = Played();
        CommandCodec codec = GameCommands.CreateCodec();
        byte[] log = codec.WriteLog(sim.CommandLog);
        // Format 1 as it was written before factions: no faction list after the seed.
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("COOPSAVE"));
            w.Write(1);
            w.Write("m2");
            w.Write(7);
            w.Write(sim.State.Tick);
            w.Write(sim.StateHash);
            w.Write(log.Length);
            w.Write(log);
        }

        SaveGame save = SaveGame.Read(stream.ToArray());
        Assert.That(save.Factions, Is.Empty);
        Assert.That(save.FactionsOrNull, Is.Null);
        Assert.That(save.Restore(Setup("m2", 7, save.FactionsOrNull), codec).StateHash, Is.EqualTo(sim.StateHash));
    }

    [Test]
    public void GWT6_Replay_SameHashEveryTick_AndMatchesAtTheEnd()
    {
        var original = new Simulation(Setup("m2", 7));
        Unit builder = original.State.UnitsOf(1).Single();
        original.Enqueue(new BuildCommand(1, 0, new[] { builder.Id }, "drill", new[] { new BuildSite(12, 19, Direction.Right) }));
        var hashes = new System.Collections.Generic.List<ulong>();
        for (int t = 0; t < 40 * Second; t++)
        {
            if (t == 20 * Second)
                original.Enqueue(new MoveCommand(1, t, new[] { builder.Id }, builder.X + 3000, builder.Y));
            original.Tick();
            hashes.Add(original.StateHash);
        }
        CommandCodec codec = GameCommands.CreateCodec();
        SaveGame record = SaveGame.Read(SaveGame.Capture(original, "m2", 7, codec).Write());

        Simulation replay = record.BeginReplay(Setup("m2", 7), codec);
        Assert.That(replay.State.Tick, Is.Zero);
        for (int t = 0; t < hashes.Count; t++)
        {
            Assert.That(record.Matches(replay), Is.False);
            replay.Tick();
            Assert.That(replay.StateHash, Is.EqualTo(hashes[t]), $"tick {t + 1}");
        }
        Assert.That(record.Matches(replay), Is.True);
    }

    [Test]
    public void GWT7_ReplayOnOtherData_DoesNotMatchAtTheEnd()
    {
        Simulation sim = Played();
        CommandCodec codec = GameCommands.CreateCodec();
        SaveGame record = SaveGame.Capture(sim, "m2", 7, codec);

        Simulation replay = record.BeginReplay(Setup("m2", 8), codec);
        replay.FastForward(record.Tick);
        Assert.That(record.Matches(replay), Is.False);
    }

    [Test]
    public void Edge_LoadInParts_SameAsAtOnce()
    {
        Simulation sim = Played();
        CommandCodec codec = GameCommands.CreateCodec();
        SaveGame save = SaveGame.Capture(sim, "m2", 7, codec);

        Simulation parts = save.BeginReplay(Setup("m2", 7), codec);
        while (parts.State.Tick < save.Tick)
            parts.FastForward(System.Math.Min(save.Tick, parts.State.Tick + 333));
        Assert.That(save.Matches(parts), Is.True);
    }

    [Test]
    public void Edge_AnotherSeed_DoesNotMatchTheHash()
    {
        Simulation sim = Played();
        SaveGame save = SaveGame.Capture(sim, "m2", 7, GameCommands.CreateCodec());
        Assert.Throws<InvalidDataException>(() => save.Restore(Setup("m2", 8), GameCommands.CreateCodec()));
    }

    [Test]
    public void Edge_NewCommands_RoundTripThroughTheCodec()
    {
        CommandCodec codec = GameCommands.CreateCodec();
        var log = codec.ReadLog(codec.WriteLog(new ICommand[]
        {
            new SetRallyCommand(1, 2, 33, true, 4500, 6500),
            new DemolishCommand(1, 3, 34),
        }));
        var rally = (SetRallyCommand)log[0];
        Assert.That((rally.FactoryId, rally.HasRally, rally.X, rally.Y), Is.EqualTo((33, true, 4500, 6500)));
        Assert.That(((DemolishCommand)log[1]).BuildingId, Is.EqualTo(34));
    }
}
