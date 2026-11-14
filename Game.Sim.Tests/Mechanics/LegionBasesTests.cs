using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/legion-ai.md, "T5: несколько баз Легиона и время в пути"
public class LegionBasesTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int LegionA = MissionLauncher.LegionPlayerId;
    private static readonly int LegionB = MissionLauncher.LegionPlayerIdFor(1);

    // 64×48: players 1–4 down the west side; base 1 in the south-east, base 2 in the north-east.
    // Neither base has drills: the start purse pays for the first wave.
    private static MapSetup TwoBaseMap(int players)
    {
        var map = new MapSetup(64, 48)
            .LegionSlot("legion_hq", 50, 4)
            .LegionSlot("legion_factory", 44, 6)
            .Rally(40, 8)
            .LegionBase()
            .LegionSlot("legion_hq", 50, 38)
            .LegionSlot("legion_factory", 44, 40)
            .Rally(40, 36);
        (int X, int Y)[] starts = { (2, 2), (2, 40), (2, 14), (2, 26) };
        for (int i = 0; i < players; i++)
            map.Start(i + 1, starts[i].X, starts[i].Y);
        return map;
    }

    private static Simulation Match(int players, MapSetup? map = null)
    {
        var humans = Enumerable.Range(1, players).ToList();
        var relations = new List<RelationSetup> { new RelationSetup(LegionA, LegionB, Relation.Ally) };
        foreach (int a in humans)
        {
            relations.AddRange(humans.Where(b => a < b).Select(b => new RelationSetup(a, b, Relation.Ally)));
            relations.Add(new RelationSetup(a, LegionA, Relation.Enemy));
            relations.Add(new RelationSetup(a, LegionB, Relation.Enemy));
        }
        return new Simulation(new MatchSetup(42, RepoData.Get(), map ?? TwoBaseMap(players), 2,
            humans.Concat(new[] { LegionA, LegionB }), relations, legionPlayerIds: new[] { LegionA, LegionB }));
    }

    // One base; the player's headquarters at (2,8), so the nearest cell beside it is (6,10): 30 cells
    // west of the rally point along a straight row. wall: rock across the row, open only at the top.
    private static Simulation OneBase(bool wall = false, bool sealedHq = false)
    {
        var map = new MapSetup(64, 20)
            .Start(Setups.PlayerA, 2, 8)
            .LegionSlot("legion_hq", 54, 8)
            .LegionSlot("legion_factory", 48, 14)
            .Rally(36, 10);
        if (wall)
        {
            for (int y = 0; y < 19; y++)
                map.Block(20, y);
        }
        if (sealedHq)
        {
            for (int y = 0; y < 20; y++)
                map.Block(10, y);
        }
        return new Simulation(Setups.WithLegion(map));
    }

    private static List<T> CollectUntil<T>(Simulation sim, int tick) where T : SimEvent
    {
        var result = new List<T>();
        while (sim.State.Tick < tick)
        {
            sim.Tick();
            result.AddRange(sim.Events.OfType<T>());
        }
        return result;
    }

    private static int TravelTicks(Simulation sim, int cells, string slowest) =>
        (cells * Grid.MilliPerCell + sim.State.Data.Unit(slowest).MoveSpeedMilliPerTick - 1) / sim.State.Data.Unit(slowest).MoveSpeedMilliPerTick;

    [Test]
    public void T1_TwoBases_TwoLegionPlayers_EachWithItsOwnSlotsWorkersAndPurse()
    {
        SimState s = Match(2).State;

        Assert.That(s.Legions.Select(l => l.PlayerId), Is.EqualTo(new[] { LegionA, LegionB }));
        Assert.That(s.Relations.IsAlly(LegionA, LegionB), Is.True);
        Assert.That(s.IsLegionPlayer(LegionA) && s.IsLegionPlayer(LegionB) && !s.IsLegionPlayer(1), Is.True);
        foreach (LegionAi ai in s.Legions)
        {
            Assert.That(s.Relations.IsEnemy(ai.PlayerId, 1) && s.Relations.IsEnemy(ai.PlayerId, 2), Is.True);
            Assert.That(ai.Stats.Players, Is.EqualTo(1));
            Assert.That(ai.Slots, Has.Count.EqualTo(2));
            Assert.That(ai.Slots.All(slot => s.FindBuilding(slot.BuildingId)!.OwnerId == ai.PlayerId), Is.True);
            Assert.That(s.UnitsOf(ai.PlayerId).Count(u => u.Worker != null), Is.EqualTo(ai.Stats.StartWorkers));
            Assert.That(s.GetPlayer(ai.PlayerId).Wallet.Get(Resources.Primary), Is.EqualTo(ai.Stats.StartPrimary));
            Assert.That(s.GetPlayer(ai.PlayerId).Faction!.Id, Is.EqualTo("black_legion"));
        }
        Assert.That((s.Legions[0].RallyX, s.Legions[0].RallyY), Is.EqualTo((40, 8)));
        Assert.That((s.Legions[1].RallyX, s.Legions[1].RallyY), Is.EqualTo((40, 36)));
    }

    [Test]
    public void T2_FourPlayers_DealtToTheBasesInTurn_EachBaseTunedForItsTwo()
    {
        SimState s = Match(4).State;

        Assert.That(s.Legions[0].TargetPlayerIds, Is.EqualTo(new[] { 1, 3 }));
        Assert.That(s.Legions[1].TargetPlayerIds, Is.EqualTo(new[] { 2, 4 }));
        Assert.That(s.Legions.Select(l => l.Stats.Players), Is.EqualTo(new[] { 2, 2 }));
    }

    [Test]
    public void T3_TwoPlayers_EachBaseSendsItsWaveToItsOwnPlayer()
    {
        Simulation sim = Match(2);
        List<WaveLaunched> launched = CollectUntil<WaveLaunched>(sim, 180 * Second + 1);

        Assert.That(launched.Select(w => (w.LegionPlayerId, w.TargetPlayerId)),
            Is.EqualTo(new[] { (LegionA, 1), (LegionB, 2) }));
        Assert.That(launched.All(w => w.Wave == 1 && w.UnitCount == 2), Is.True);
    }

    [Test]
    public void T4_OnePlayer_TheSecondBaseSleeps_NoWarningNoWave_ItsRaidersWait()
    {
        Simulation sim = Match(1);
        var warnings = new List<WaveIncoming>();
        var launched = new List<WaveLaunched>();
        while (sim.State.Tick < 180 * Second + 1)
        {
            sim.Tick();
            warnings.AddRange(sim.Events.OfType<WaveIncoming>());
            launched.AddRange(sim.Events.OfType<WaveLaunched>());
        }

        Assert.That(warnings.Select(w => w.LegionPlayerId), Is.EqualTo(new[] { LegionA }));
        Assert.That(launched.Select(w => w.LegionPlayerId), Is.EqualTo(new[] { LegionA }));
        LegionAi asleep = sim.State.Legions[1];
        Assert.That(asleep.TargetPlayerIds, Is.Empty);
        Assert.That(asleep.Stats.Players, Is.EqualTo(1));
        Assert.That(asleep.NextWave, Is.EqualTo(1));
        Assert.That(asleep.NextWaveTick, Is.EqualTo(int.MaxValue), "no timer for a base that sends nothing");
        Assert.That(asleep.Waves, Is.Empty);
        List<Unit> raiders = sim.State.UnitsOf(LegionB).Where(u => u.Worker == null).ToList();
        Assert.That(raiders, Has.Count.EqualTo(2));
        Assert.That(raiders.All(u => Movement.Distance(u.X, u.Y, Center(40), Center(36)) <= Cells(6)), Is.True);
    }

    [Test]
    public void T5_Victory_OnlyWhenEveryBaseLostItsHeadquartersAndFactories()
    {
        Simulation sim = Match(2);
        foreach (Building b in sim.State.Buildings.Where(b => b.OwnerId == LegionA).ToList())
            Kill(b);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));

        foreach (Building b in sim.State.Buildings.Where(b => b.OwnerId == LegionB).ToList())
            Kill(b);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.Victory));
    }

    [Test]
    public void T6_Warning_NamesTheArrival_LaunchPlusPathOverTheSlowestSpeed()
    {
        Simulation sim = OneBase();
        WaveIncoming warning = CollectUntil<WaveIncoming>(sim, 160 * Second + 1).Single();

        Assert.That(warning.LaunchTick, Is.EqualTo(180 * Second));
        Assert.That(warning.ArrivalTick, Is.EqualTo(180 * Second + TravelTicks(sim, 30, "legion_raider")));
        Assert.That(sim.State.Legions[0].NextWaveArrivalTick, Is.EqualTo(warning.ArrivalTick), "the HUD's timer");

        WaveLaunched launched = CollectUntil<WaveLaunched>(sim, 180 * Second + 1).Single();
        Assert.That(launched.ArrivalTick, Is.EqualTo(warning.ArrivalTick));
    }

    [Test]
    public void T7_RockAcrossTheWay_TheArrivalIsLater()
    {
        int direct = CollectUntil<WaveIncoming>(OneBase(), 160 * Second + 1).Single().ArrivalTick;
        int around = CollectUntil<WaveIncoming>(OneBase(wall: true), 160 * Second + 1).Single().ArrivalTick;

        Assert.That(around, Is.GreaterThan(direct + 2 * Second));
    }

    [Test]
    public void T8_BaseUnderAttack_OnlyItsOwnUnitsDefend()
    {
        Simulation sim = Match(2);
        sim.Run(60 * Second);
        Building citadel = sim.State.FindHeadquarters(LegionB)!;
        Unit attacker = Spawn(sim, 2, "infantry", 30, 30);
        citadel.LastDamagedTick = sim.State.Tick;
        citadel.LastAttackerId = attacker.Id;
        sim.Run(Second + 1);

        Assert.That(sim.State.Legions[0].DefendBuildingId, Is.Zero);
        Assert.That(sim.State.Legions[1].DefendBuildingId, Is.EqualTo(citadel.Id));
        List<Unit> defenders = sim.State.UnitsOf(LegionB).Where(u => u.Worker == null).ToList();
        Assert.That(defenders, Is.Not.Empty);
        Assert.That(defenders.All(u => u.Order.Kind == UnitOrderKind.AttackMove), Is.True);
        Assert.That(sim.State.UnitsOf(LegionA).Where(u => u.Worker == null)
            .All(u => Movement.Distance(u.X, u.Y, Center(40), Center(8)) <= Cells(6)), Is.True);
    }

    [Test]
    public void Edge_BaseWhosePlayersAreGone_SendsItsWaveToAnyLivingPlayer()
    {
        Simulation sim = Match(2);
        // Player 2 has nothing when the wave leaves (the warning named it ten seconds ago): its builder
        // comes back at the ally's headquarters only builder_respawn later (builder.md).
        sim.Run(170 * Second);
        foreach (Entity e in sim.State.Buildings.Where(b => b.OwnerId == 2).Cast<Entity>().Concat(sim.State.UnitsOf(2)).ToList())
            Kill(e);
        Assert.That(sim.State.GetPlayer(2).Faction!.BuilderRespawnTicks, Is.GreaterThan(10 * Second));
        List<WaveLaunched> launched = CollectUntil<WaveLaunched>(sim, 180 * Second + 1);

        Assert.That(launched.Select(w => (w.LegionPlayerId, w.TargetPlayerId)),
            Is.EqualTo(new[] { (LegionA, 1), (LegionB, 1) }));
    }

    [Test]
    public void Edge_NoWayToTheTarget_ArrivalIsTheLaunch()
    {
        Simulation sim = OneBase(sealedHq: true);
        WaveIncoming warning = CollectUntil<WaveIncoming>(sim, 160 * Second + 1).Single();

        Assert.That(warning.ArrivalTick, Is.EqualTo(warning.LaunchTick));
    }

    [Test]
    public void Edge_MixedWave_TheSlowestUnitSetsTheArrival()
    {
        SimState s = OneBase().State;
        WaveStats withBrute = s.Data.Waves.First(w => w.Units.Any(u => u.UnitId == "legion_brute"));

        Assert.That(LegionAiSystem.SlowestSpeed(s, withBrute), Is.EqualTo(s.Data.Unit("legion_brute").MoveSpeedMilliPerTick));
        Assert.That(s.Data.Unit("legion_brute").MoveSpeedMilliPerTick, Is.LessThan(s.Data.Unit("legion_raider").MoveSpeedMilliPerTick));
    }

    [Test]
    public void Edge_OneBaseMap_WorksAsBefore_AllPlayersAreItsOwn()
    {
        var map = new MapSetup(64, 30)
            .Start(Setups.PlayerA, 2, 2)
            .Start(Setups.PlayerB, 2, 20)
            .LegionSlot("legion_hq", 54, 8)
            .LegionSlot("legion_factory", 48, 14)
            .Rally(40, 10);
        SimState s = new Simulation(Setups.WithLegion(map)).State;

        Assert.That(s.Legions, Has.Count.EqualTo(1));
        Assert.That(s.Legion, Is.SameAs(s.Legions[0]));
        Assert.That(s.Legion!.TargetPlayerIds, Is.EqualTo(new[] { Setups.PlayerA, Setups.PlayerB }));
        Assert.That(s.Legion.Stats.Players, Is.EqualTo(2));
    }

    [Test]
    public void MapJson_TwoBases_LoadAsTwoBases_OneBaseAsBefore()
    {
        const string two = @"{""width"": 12, ""height"": 4, ""rows_bottom_up"": [""............"", ""............"", ""............"", ""............""],
            ""starts"": [], ""camps"": [],
            ""legions"": [
              {""slots"": [{""building"": ""legion_turret"", ""x"": 1, ""y"": 1, ""dir"": ""right""}], ""rally"": [3, 3]},
              {""slots"": [{""building"": ""legion_turret"", ""x"": 8, ""y"": 1, ""dir"": ""up""}], ""rally"": [10, 3]}]}";
        MapSetup map = MapLoader.Parse(two, new int[0], RepoData.Get());

        Assert.That(map.LegionBases, Has.Count.EqualTo(2));
        Assert.That((map.LegionBases[1].Slots[0].CellX, map.LegionBases[1].Slots[0].Direction), Is.EqualTo((8, Direction.Up)));
        Assert.That((map.LegionBases[1].RallyX, map.LegionBases[1].RallyY), Is.EqualTo((10, 3)));

        MapSetup one = MapLoader.Parse(RepoData.MapJson("m2"), new[] { 1 }, RepoData.Get());
        Assert.That(one.LegionBases, Has.Count.EqualTo(1));
        Assert.That(one.LegionBases[0].HasRally, Is.True);
    }

    [Test]
    public void Determinism_TwoBases_FourPlayers_SameHashEveryTick()
    {
        var hashes = new List<ulong>();
        Match(4).Run(5 * 60 * Second, s => hashes.Add(s.StateHash));
        int i = 0;
        Match(4).Run(5 * 60 * Second, s => Assert.That(s.StateHash, Is.EqualTo(hashes[i++])));
    }
}
