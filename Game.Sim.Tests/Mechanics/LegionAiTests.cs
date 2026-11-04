using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/legion-ai.md
public class LegionAiTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Legion = Setups.EnemyAi;

    // Slot indexes in LegionMap.
    private const int SlotHq = 0, SlotFactory1 = 1, SlotFactory2 = 2, SlotDrill = 3, SlotTurret = 7;

    // 48×36: player A's headquarters at (2,2) with a drill at (10,2), B at (2,24) when two play.
    // Legion: HQ (38,24), factories (32,26) and (32,30), a drill next to the HQ, a drill with a
    // two-cell belt into the HQ, a turret at (44,18) away from the army's paths; rally at (28,22).
    private static MapSetup LegionMap(bool twoPlayers = false)
    {
        var map = new MapSetup(48, 36)
            .OreRect(10, 2, 2, 2, Resources.Primary)
            .OreRect(14, 2, 2, 2, Resources.Primary)
            .OreRect(42, 24, 2, 2, Resources.Primary)
            .OreRect(39, 30, 2, 2, Resources.Primary)
            .Start(Setups.PlayerA, 2, 2)
            .LegionSlot("headquarters", 38, 24)
            .LegionSlot("legion_factory", 32, 26)
            .LegionSlot("legion_factory", 32, 30)
            .LegionSlot("drill", 42, 24)
            .LegionSlot("drill", 39, 30)
            .LegionLine(40, 29, 40, 28)
            .LegionSlot("turret", 44, 18)
            .Rally(28, 22);
        if (twoPlayers)
            map.Start(Setups.PlayerB, 2, 24);
        return map;
    }

    private static Simulation Match(bool twoPlayers = false, MapSetup? map = null) =>
        new Simulation(Setups.WithLegion(map ?? LegionMap(twoPlayers)));

    private static LegionAi Ai(Simulation sim) => sim.State.Legion!;

    private static Building SlotBuilding(Simulation sim, int slot) => sim.State.FindBuilding(Ai(sim).Slots[slot].BuildingId)!;

    private static Building PlayerDrill(Simulation sim, int owner, int x, int y) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building("drill"), x, y, Direction.Right, complete: true);

    private static List<T> RunCollectingUntil<T>(Simulation sim, int tick) where T : SimEvent
    {
        var result = new List<T>();
        while (sim.State.Tick < tick)
        {
            sim.Tick();
            result.AddRange(sim.Events.OfType<T>());
        }
        return result;
    }

    [Test]
    public void GWT1_Start_EverySlotBuilt_ThreeWorkers_AndTheSoloPurse()
    {
        var sim = Match();
        LegionAi ai = Ai(sim);
        Assert.That(ai.Slots, Has.Count.EqualTo(8));
        foreach (BaseSlot slot in ai.Slots)
        {
            Building b = sim.State.FindBuilding(slot.BuildingId)!;
            Assert.That(b.OwnerId, Is.EqualTo(Legion));
            Assert.That(b.IsComplete, Is.True);
            Assert.That(b.Stats.Id, Is.EqualTo(slot.Setup.BuildingTypeId));
        }
        Assert.That(sim.State.UnitsOf(Legion).Count(u => u.Stats.Id == "legion_worker"), Is.EqualTo(3));
        Wallet wallet = sim.State.GetPlayer(Legion).Wallet;
        Assert.That(wallet.Get(Resources.Primary), Is.EqualTo(100));
        Assert.That(wallet.Get(Resources.Secondary), Is.EqualTo(20));
        Assert.That(ai.Stats.Players, Is.EqualTo(1));
    }

    [Test]
    public void GWT2_Solo_WarningAtTwoForty_ForThePlayer()
    {
        var sim = Match();
        sim.Tick();
        Assert.That(Ai(sim).NextWaveTick, Is.EqualTo(180 * Second), "the HUD's timer");
        List<WaveIncoming> warnings = RunCollectingUntil<WaveIncoming>(sim, 160 * Second + 1);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0].Tick, Is.EqualTo(160 * Second));
        Assert.That(warnings[0].Wave, Is.EqualTo(1));
        Assert.That(warnings[0].TargetPlayerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(warnings[0].LaunchTick, Is.EqualTo(180 * Second));
    }

    [Test]
    public void GWT3_AtThree_TwoRaiders_AttackMoveToThePlayersDrill()
    {
        var sim = Match();
        Building drill = PlayerDrill(sim, Setups.PlayerA, 10, 2);
        List<WaveLaunched> launched = RunCollectingUntil<WaveLaunched>(sim, 180 * Second + 1);

        Assert.That(launched, Has.Count.EqualTo(1));
        Assert.That(launched[0].Tick, Is.EqualTo(180 * Second));
        Assert.That(launched[0].TargetId, Is.EqualTo(drill.Id));
        Assert.That(launched[0].UnitCount, Is.EqualTo(2));
        Wave wave = Ai(sim).Waves.Single();
        foreach (int id in wave.UnitIds)
        {
            Unit u = sim.State.FindUnit(id)!;
            Assert.That(u.Stats.Id, Is.EqualTo("legion_raider"));
            Assert.That(u.Order.Kind, Is.EqualTo(UnitOrderKind.AttackMove));
        }
    }

    [Test]
    public void GWT4_TwoPlayers_WavesTakeTurns_AndGrowBySeventyPercent()
    {
        var sim = Match(twoPlayers: true);
        PlayerDrill(sim, Setups.PlayerA, 10, 2);
        // Wave 2 at 5:00 × 90 % = 4:30.
        List<WaveLaunched> launched = RunCollectingUntil<WaveLaunched>(sim, 270 * Second + 1);

        Assert.That(launched.Select(w => w.TargetPlayerId), Is.EqualTo(new[] { Setups.PlayerA, Setups.PlayerB }));
        Assert.That(launched[0].Tick, Is.EqualTo(180 * Second * 90 / 100));
        Assert.That(launched[0].UnitCount, Is.EqualTo(3)); // 2 raiders × 1,7 = 3,4 → 3
        Assert.That(Ai(sim).Stats.Players, Is.EqualTo(2));
    }

    [Test]
    public void GWT5_DestroyedDrill_RebuiltOnItsSlot_ThirtySecondsLater()
    {
        var sim = Match();
        sim.Run(60 * Second);
        Kill(SlotBuilding(sim, SlotDrill));
        sim.Run(Second);
        int destroyed = Ai(sim).Slots[SlotDrill].DestroyedTick;
        Assert.That(Ai(sim).Slots[SlotDrill].BuildingId, Is.EqualTo(0));

        sim.Run(destroyed + 29 * Second - sim.State.Tick);
        Assert.That(Ai(sim).Slots[SlotDrill].WorkerId, Is.EqualTo(0));

        sim.RunUntil(s => Ai(s).Slots[SlotDrill].BuildingId != 0 && SlotBuilding(s, SlotDrill).IsComplete, 60 * Second);
        Building rebuilt = SlotBuilding(sim, SlotDrill);
        Assert.That((rebuilt.CellX, rebuilt.CellY, rebuilt.OwnerId), Is.EqualTo((42, 24, Legion)));
    }

    [Test]
    public void GWT6_EnemyNearTheSlot_NoRebuild_UntilItLeaves()
    {
        var sim = Match();
        Kill(SlotBuilding(sim, SlotTurret));
        // A's worker stands 4 cells from the turret's slot (44..45, 18..19) and does not fight.
        Unit watcher = Spawn(sim, Setups.PlayerA, "builder", 44, 14);
        sim.Run(40 * Second);
        Assert.That(Ai(sim).Slots[SlotTurret].WorkerId, Is.EqualTo(0));
        Assert.That(Ai(sim).Slots[SlotTurret].BuildingId, Is.EqualTo(0));

        watcher.X = Center(20);
        sim.Run(2 * Second);
        Assert.That(Ai(sim).Slots[SlotTurret].WorkerId, Is.Not.EqualTo(0));
    }

    [Test]
    public void GWT7_FewerWorkersThanTheLimit_AWorkerIsQueued()
    {
        var sim = Match();
        Kill(sim.State.UnitsOf(Legion).First(u => u.Worker != null));
        sim.Run(Second + 1);
        IEnumerable<string> queued = sim.State.Buildings.Where(b => b.OwnerId == Legion && b.Factory != null).SelectMany(b => b.Factory!.Queue);
        Assert.That(queued, Does.Contain("legion_worker"));
    }

    [Test]
    public void GWT8_NothingWaiting_EachFactoryQueuesOneUnitOfTheNextWave()
    {
        var sim = Match();
        sim.Tick();
        List<Building> factories = sim.State.Buildings.Where(b => b.OwnerId == Legion && b.Factory != null).ToList();
        Assert.That(factories, Has.Count.EqualTo(2));
        foreach (Building f in factories)
            Assert.That(f.Factory!.Queue, Is.EqualTo(new[] { "legion_raider" }));

        sim.Run(5 * Second);
        foreach (Building f in factories)
            Assert.That(f.Factory!.Queue.Count, Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void GWT9_BaseAttacked_WaitingUnitsDefend_ThenGoBackToTheRally()
    {
        var sim = Match();
        Building turret = SlotBuilding(sim, SlotTurret);
        var gunners = new[] { Spawn(sim, Legion, "legion_gunner", 28, 22), Spawn(sim, Legion, "legion_gunner", 28, 21), Spawn(sim, Legion, "legion_gunner", 29, 22) };
        // A siege unit out of the turret's reach (8 > 6) shells it.
        Unit siege = Spawn(sim, Setups.PlayerA, "siege", 44, 9);
        sim.Order(new AttackCommand(Setups.PlayerA, 0, new[] { siege.Id }, turret.Id));
        sim.RunUntil(s => turret.Hp < turret.MaxHp, 20 * Second);
        sim.Run(2 * Second);
        Assert.That(Ai(sim).DefendBuildingId, Is.EqualTo(turret.Id));
        Assert.That(gunners.All(g => g.Order.Kind == UnitOrderKind.AttackMove && Movement.Distance(g.Order.X, g.Order.Y, siege.X, siege.Y) <= Cells(3)), Is.True);

        sim.RunUntil(s => siege.IsDead, 60 * Second);
        sim.RunUntil(s => Ai(s).DefendBuildingId == 0, 20 * Second);
        sim.RunUntil(s => gunners.Where(g => !g.IsDead).All(g => Movement.Distance(g.X, g.Y, Center(28), Center(22)) <= Cells(3)), 60 * Second);
    }

    [Test]
    public void GWT10_AttackMove_FightsWhatItMeets_ThenWalksOn()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(40, 12)));
        Unit gunner = Spawn(sim, Legion, "legion_gunner", 2, 5);
        Unit worker = Spawn(sim, Setups.PlayerA, "builder", 15, 5);
        UnitOrders.Replace(gunner, UnitOrder.AttackMoveTo(Center(35), Center(5)));
        sim.RunUntil(s => gunner.TargetId == worker.Id, 20 * Second);
        sim.RunUntil(s => worker.IsDead, 30 * Second);
        Assert.That(gunner.Order.Kind, Is.EqualTo(UnitOrderKind.AttackMove));
        sim.RunUntil(s => gunner.Order.Kind == UnitOrderKind.Idle, 30 * Second);
        Assert.That(gunner.CellX, Is.EqualTo(35));
    }

    [Test]
    public void GWT11_TwoPlayers_TheLegionsHeadquartersCreditsHundredSeventyPercent()
    {
        var sim = Match(twoPlayers: true);
        Wallet wallet = sim.State.GetPlayer(Legion).Wallet;
        int before = wallet.Get(Resources.Primary);
        wallet.Credit(Resources.Primary, 10);
        Assert.That(wallet.Get(Resources.Primary) - before, Is.EqualTo(17));
    }

    [Test]
    public void Edge_FractionalIncome_RemainderCarriesOver()
    {
        var sim = Match(twoPlayers: true);
        Wallet wallet = sim.State.GetPlayer(Legion).Wallet;
        int before = wallet.Get(Resources.Primary);
        wallet.Credit(Resources.Primary, 1);
        wallet.Credit(Resources.Primary, 1);
        wallet.Credit(Resources.Primary, 1);
        // 1,7 × 3 = 5,1 → 5 credited, 0,1 kept.
        Assert.That(wallet.Get(Resources.Primary) - before, Is.EqualTo(5));
        Assert.That(wallet.IncomeRemainder(Resources.Primary), Is.EqualTo(10));
    }

    [Test]
    public void Edge_TargetDestroyedOnTheWay_NextDrillOfTheSamePlayer()
    {
        var sim = Match();
        Building near = PlayerDrill(sim, Setups.PlayerA, 14, 2);
        Building far = PlayerDrill(sim, Setups.PlayerA, 10, 2);
        RunCollectingUntil<WaveLaunched>(sim, 180 * Second + 1);
        Wave wave = Ai(sim).Waves.Single();
        Assert.That(wave.TargetId, Is.EqualTo(near.Id));
        Kill(near);
        sim.Run(2 * Second);
        Assert.That(wave.TargetId, Is.EqualTo(far.Id));
    }

    [Test]
    public void Edge_PlayerWithNothingLeft_IsSkipped()
    {
        var sim = Match(twoPlayers: true);
        foreach (Unit u in sim.State.UnitsOf(Setups.PlayerB))
            Kill(u);
        Kill(sim.State.FindHeadquarters(Setups.PlayerB)!);
        // builder.md: B's builder would come back at A's headquarters; here it never does.
        sim.State.GetPlayer(Setups.PlayerB).BuilderRespawnTick = int.MaxValue;
        List<WaveLaunched> launched = RunCollectingUntil<WaveLaunched>(sim, 270 * Second + 1);
        Assert.That(launched.Select(w => w.TargetPlayerId), Is.EqualTo(new[] { Setups.PlayerA, Setups.PlayerA }));
    }

    [Test]
    public void Edge_NobodyReadyAtWaveTime_NoWave_TheNumberStillGrows()
    {
        var sim = Match();
        Kill(SlotBuilding(sim, SlotFactory1));
        Kill(SlotBuilding(sim, SlotFactory2));
        Kill(SlotBuilding(sim, SlotDrill));
        Kill(SlotBuilding(sim, SlotDrill + 1));
        // No money and no income: nothing gets rebuilt or produced.
        sim.State.GetPlayer(Legion).Wallet.TrySpend(Resources.Primary, 100);
        List<WaveLaunched> launched = RunCollectingUntil<WaveLaunched>(sim, 180 * Second + 1);
        Assert.That(launched, Is.Empty);
        Assert.That(Ai(sim).NextWave, Is.EqualTo(2));
    }

    [Test]
    public void Edge_ArmyLimitReached_NothingQueued()
    {
        var sim = Match();
        for (int i = 0; i < 12; i++)
            Spawn(sim, Legion, "legion_gunner", 20 + i % 6, 14 + i / 6);
        sim.Tick();
        IEnumerable<string> queued = sim.State.Buildings.Where(b => b.OwnerId == Legion && b.Factory != null).SelectMany(b => b.Factory!.Queue);
        Assert.That(queued, Is.Empty);
    }

    [Test]
    public void Edge_SlotTakenByAPlayersBuilding_NotRebuilt()
    {
        var sim = Match();
        Kill(SlotBuilding(sim, SlotTurret));
        sim.Tick();
        sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("storage"), 44, 18, Direction.Right, complete: true);
        sim.Run(45 * Second);
        Assert.That(Ai(sim).Slots[SlotTurret].BuildingId, Is.EqualTo(0));
    }

    [Test]
    public void Edge_TwoSlotsDestroyedInOneTick_RebuiltInSlotOrder_AWorkerEach()
    {
        var sim = Match();
        Kill(SlotBuilding(sim, SlotDrill));
        Kill(SlotBuilding(sim, SlotTurret));
        sim.RunUntil(s => Ai(s).Slots[SlotTurret].WorkerId != 0, 40 * Second);
        int drillWorker = Ai(sim).Slots[SlotDrill].WorkerId, turretWorker = Ai(sim).Slots[SlotTurret].WorkerId;
        Assert.That(drillWorker, Is.Not.EqualTo(0));
        Assert.That(drillWorker, Is.LessThan(turretWorker));
    }

    [Test]
    public void Edge_UnitsDefendingWhenTheWaveLeaves_GoWithIt()
    {
        var sim = Match();
        PlayerDrill(sim, Setups.PlayerA, 10, 2);
        sim.Run(170 * Second);
        List<Unit> raiders = sim.State.UnitsOf(Legion).Where(u => u.Stats.Id == "legion_raider").ToList();
        Assert.That(raiders, Has.Count.GreaterThanOrEqualTo(2));
        foreach (Unit r in raiders)
            UnitOrders.Replace(r, UnitOrder.AttackMoveTo(Center(40), Center(20)));
        RunCollectingUntil<WaveLaunched>(sim, 180 * Second + 1);
        Assert.That(Ai(sim).Waves.Single().UnitIds, Is.SubsetOf(raiders.Select(r => r.Id)));
    }

    [Test]
    public void Edge_PlayerCannotBuildTheLegionsFactory()
    {
        var sim = Match();
        Unit worker = sim.State.UnitsOf(Setups.PlayerA).First(u => u.Worker != null);
        sim.Order(new BuildCommand(Setups.PlayerA, 0, new[] { worker.Id }, "legion_factory", new[] { new BuildSite(8, 8, Direction.Right) }));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
    }

    [Test]
    public void Determinism_TenMinutesWithTheLegion_SameHashEveryTick()
    {
        var hashes = new List<ulong>();
        var a = Match(twoPlayers: true);
        PlayerDrill(a, Setups.PlayerA, 10, 2);
        a.Run(600 * Second, s => hashes.Add(s.StateHash));
        var b = Match(twoPlayers: true);
        PlayerDrill(b, Setups.PlayerA, 10, 2);
        int i = 0;
        b.Run(600 * Second, s => Assert.That(s.StateHash, Is.EqualTo(hashes[i++])));
    }
}
