using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/neutral-buildings.md
public class NeutralBuildingTests
{
    private const int Second = Simulation.TicksPerSecond;

    // HQ A (2,2), HQ B (2,20); neutral drill on ore at (20,4); cave (30,20) guarded by camp 0
    // around (34,24); lighthouse (12,30).
    private static Simulation Match()
    {
        var map = new MapSetup(48, 40)
            .Start(Setups.PlayerA, 2, 2)
            .Start(Setups.PlayerB, 2, 20)
            .OreRect(20, 4, 2, 2, Resources.Primary)
            .CreepCamp("cave_guard", 34, 24, (34, 24), (35, 24), (34, 25), (35, 25))
            .Neutral("drill", 20, 4)
            .Neutral("cave", 30, 20, guardCampIndex: 0)
            .Neutral("lighthouse", 12, 30);
        return new Simulation(Setups.WithNeutral(map));
    }

    private static Building At(Simulation sim, string type) => sim.State.Buildings.Single(b => b.Stats.Id == type);

    private static void ClearCamp(Simulation sim)
    {
        foreach (int id in sim.State.CreepCamps[0].CreepIds)
        {
            Unit creep = sim.State.FindUnit(id)!;
            creep.Hp = 0;
            creep.IsDead = true;
        }
    }

    [Test]
    public void GWT1_UnitStandsTenSeconds_DrillIsCaptured()
    {
        var sim = Match();
        Building drill = At(sim, "drill");
        Assert.That(drill.OwnerId, Is.EqualTo(Setups.Neutral));
        Spawn(sim, Setups.PlayerA, "infantry", 19, 4);
        var captured = sim.RunCollecting<BuildingCaptured>(10 * Second);
        Assert.That(captured.Single().PlayerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(captured.Single().Tick, Is.EqualTo(10 * Second - 1));
        Assert.That(drill.OwnerId, Is.EqualTo(Setups.PlayerA));
    }

    [Test]
    public void GWT2_UnitLeavesHalfway_ProgressDropsToZero_TenSecondsAgain()
    {
        var sim = Match();
        Building drill = At(sim, "drill");
        Unit u = Spawn(sim, Setups.PlayerA, "infantry", 19, 4);
        sim.Run(5 * Second);
        Assert.That(drill.CaptureProgress, Is.EqualTo(5 * Second));
        u.X = Center(10);
        sim.Tick();
        Assert.That(drill.CaptureProgress, Is.EqualTo(0));
        u.X = Center(19);
        u.Order = UnitOrder.Idle;
        int ticks = sim.RunUntil(s => drill.OwnerId == Setups.PlayerA);
        Assert.That(ticks, Is.EqualTo(10 * Second));
    }

    [Test]
    public void GWT3_GuardedCave_NotCapturedWhileItsCampLives()
    {
        var sim = Match();
        Building cave = At(sim, "cave");
        Spawn(sim, Setups.PlayerA, "heavy", 29, 19);
        // Keep the guards home: this checks the rule, not the fight.
        foreach (int id in sim.State.CreepCamps[0].CreepIds)
            sim.State.FindUnit(id)!.Hp = 100000;
        sim.Run(20 * Second);
        Assert.That(cave.OwnerId, Is.EqualTo(Setups.Neutral));
        Assert.That(cave.CaptureProgress, Is.EqualTo(0));
    }

    [Test]
    public void GWT4_CampCleared_CaveCaptured_ThenRecruitsFromItsStock()
    {
        var sim = Match();
        Building cave = At(sim, "cave");
        ClearCamp(sim);
        Spawn(sim, Setups.PlayerA, "infantry", 29, 19);
        sim.RunUntil(s => cave.OwnerId == Setups.PlayerA, 12 * Second);

        sim.Order(new ProduceCommand(Setups.PlayerA, sim.State.Tick, cave.Id, "cave_beast"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.FactoryStockLow));
        cave.Factory!.Stock!.Add(Resources.Primary, 60);
        cave.Factory.Stock.Add(Resources.Secondary, 30);
        sim.Order(new ProduceCommand(Setups.PlayerA, sim.State.Tick, cave.Id, "cave_beast"));
        sim.RunUntil(s => s.State.UnitsOf(Setups.PlayerA).Any(u => u.Stats.Id == "cave_beast"), 31 * Second);
    }

    [Test]
    public void GWT5_UnitByTheLighthouse_ItShinesForTheTeam()
    {
        var sim = Match();
        Building lighthouse = At(sim, "lighthouse");
        Assert.That(sim.State.Fog.IsVisible(Setups.PlayerB, 33, 31), Is.False);
        Unit u = Spawn(sim, Setups.PlayerA, "scout", 11, 30);
        sim.Tick();
        Assert.That(sim.Events.OfType<LighthouseLit>().Single().PlayerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(lighthouse.LitForPlayerId, Is.EqualTo(Setups.PlayerA));
        // 24 cells from its centre (13, 31): far beyond the scout's own 10.
        Assert.That(sim.State.Fog.IsVisible(Setups.PlayerA, 33, 31), Is.True);
        Assert.That(sim.State.Fog.IsVisible(Setups.PlayerB, 33, 31), Is.True);
    }

    [Test]
    public void GWT6_UnitLeaves_TheLighthouseGoesDark_CellsStayDiscovered()
    {
        var sim = Match();
        Building lighthouse = At(sim, "lighthouse");
        Unit u = Spawn(sim, Setups.PlayerA, "scout", 11, 30);
        sim.Tick();
        u.X = Center(30);
        u.Y = Center(10);
        sim.Tick();
        Assert.That(lighthouse.LitForPlayerId, Is.EqualTo(0));
        Assert.That(sim.State.Fog.IsVisible(Setups.PlayerA, 33, 31), Is.False);
        Assert.That(sim.State.Fog.IsDiscovered(Setups.PlayerA, 33, 31), Is.True);
    }

    [Test]
    public void Edge_TwoAllies_LowerUnitIdCaptures()
    {
        var sim = Match();
        Spawn(sim, Setups.PlayerB, "infantry", 19, 5);
        Spawn(sim, Setups.PlayerA, "infantry", 19, 4);
        sim.Run(10 * Second);
        Assert.That(At(sim, "drill").OwnerId, Is.EqualTo(Setups.PlayerB));
    }

    [Test]
    public void Edge_HostileUnitNearby_ProgressHolds()
    {
        var sim = Match();
        Building drill = At(sim, "drill");
        Spawn(sim, Setups.PlayerA, "heavy", 19, 4);
        sim.Run(3 * Second);
        Unit raider = Spawn(sim, Setups.EnemyAi, "legion_raider", 22, 6);
        raider.Hp = 100000;
        sim.Tick();
        int held = drill.CaptureProgress;
        sim.Run(Second);
        Assert.That(drill.CaptureProgress, Is.EqualTo(held));
        Assert.That(drill.OwnerId, Is.EqualTo(Setups.Neutral));
    }

    [Test]
    public void Edge_CreepsIgnoreNeutralBuildings()
    {
        var map = new MapSetup(32, 32)
            .Start(Setups.PlayerA, 2, 2)
            .OreRect(20, 20, 2, 2, Resources.Primary)
            .CreepCamp("small_camp", 22, 22, (22, 23), (23, 23))
            .Neutral("drill", 20, 20);
        var sim = new Simulation(Setups.WithNeutral(map));
        sim.Run(5 * Second);
        Assert.That(sim.State.CreepCamps[0].State, Is.EqualTo(CreepCampState.Calm));
        Assert.That(sim.State.Buildings.Single(b => b.Stats.Id == "drill").Hp, Is.EqualTo(300));
    }

    [Test]
    public void Edge_CapturedDrill_GivesToItsNewOwnersBelt()
    {
        var sim = Match();
        Building drill = At(sim, "drill");
        Spawn(sim, Setups.PlayerA, "infantry", 19, 3);
        sim.RunUntil(s => drill.OwnerId == Setups.PlayerA, 11 * Second);
        Building belt = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("conveyor"), 22, 4, Direction.Right, complete: true);
        sim.RunUntil(s => belt.Conveyor!.HasItem, 3 * Second);
    }

    [Test]
    public void Edge_LegionDoesNotCapture_NeutralCaveNotOrderable_BeltsDoNotFeedIt()
    {
        var sim = Match();
        Building drill = At(sim, "drill");
        Building cave = At(sim, "cave");
        Spawn(sim, Setups.EnemyAi, "legion_raider", 19, 4);
        sim.Order(new ProduceCommand(Setups.PlayerA, sim.State.Tick, cave.Id, "cave_beast"));
        sim.Run(11 * Second);
        Assert.That(drill.OwnerId, Is.EqualTo(Setups.Neutral));
        Assert.That(cave.OwnerId, Is.EqualTo(Setups.Neutral));
        Assert.That(cave.Factory!.Queue, Is.Empty);
    }

    [Test]
    public void Edge_CaveAndLighthouse_TakeNoDamage()
    {
        var sim = Match();
        Building cave = At(sim, "cave");
        ClearCamp(sim);
        Spawn(sim, Setups.PlayerA, "infantry", 29, 19);
        sim.RunUntil(s => cave.OwnerId == Setups.PlayerA, 12 * Second);
        Unit raider = Spawn(sim, Setups.EnemyAi, "legion_raider", 33, 20);
        sim.Order(new AttackCommand(Setups.EnemyAi, sim.State.Tick, new[] { raider.Id }, cave.Id));
        sim.Run(3 * Second);
        Assert.That(cave.Hp, Is.EqualTo(cave.MaxHp));
    }
}
