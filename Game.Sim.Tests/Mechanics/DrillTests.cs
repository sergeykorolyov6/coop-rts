using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;
using static Game.Sim.Tests.ConstructionTests;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/drill.md
public class DrillTests
{
    // A at (2,2), B at (20,2); a 4x4 deposit at (10,6) and a 1x2 strip at (10,12).
    private static Simulation Match()
    {
        var map = new MapSetup(32, 16)
            .Start(Setups.PlayerA, 2, 2)
            .Start(Setups.PlayerB, 20, 2)
            .OreRect(10, 6, 4, 4, Resources.Primary);
        return new Simulation(Setups.Coop(map));
    }

    private static Building Drill(Simulation sim, int player, int x, int y) =>
        sim.State.PlaceBuilding(player, sim.State.Data.Building("drill"), x, y, Direction.Right, complete: true);

    [Test]
    public void GWT1_WorkerBuildsDrillOnOre_PaysAtStart_TenSeconds_ThenMinesHalfPerSecond()
    {
        var sim = Match();
        Unit worker = Worker(sim, Setups.PlayerA);
        int start = Wallet(sim, Setups.PlayerA);
        sim.Order(Build(sim, Setups.PlayerA, "drill", 10, 6, worker));

        sim.Tick();
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(start), "paid only when the worker starts");
        sim.RunUntil(s => SiteAt(s, 10, 6) != null);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(start - 30));

        Building drill = SiteAt(sim, 10, 6)!;
        int buildTicks = sim.RunUntil(s => drill.IsComplete) + 1;
        Assert.That(buildTicks, Is.EqualTo(10 * Simulation.TicksPerSecond));
        sim.Run(2);
        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));

        // 0.5 units/s on 4 ore cells: one unit per 40 ticks into the buffer.
        int t = sim.RunUntil(s => drill.Drill!.Buffer == 1);
        int t2 = sim.RunUntil(s => drill.Drill!.Buffer == 2);
        Assert.That(t2, Is.EqualTo(40));
        Assert.That(t, Is.LessThanOrEqualTo(40));
        Assert.That(drill.Drill!.OreCells, Is.EqualTo(4));
    }

    [Test]
    public void GWT2_AllyJoinsHalfWay_RestTakesHalfTime_DrillStaysTheOwners_AllyPaysNothing()
    {
        var sim = Match();
        Unit a = Worker(sim, Setups.PlayerA);
        sim.Order(Build(sim, Setups.PlayerA, "drill", 10, 6, a));
        sim.RunUntil(s => SiteAt(s, 10, 6) != null);
        Building drill = SiteAt(sim, 10, 6)!;

        // Bring B's worker next to the site first, so walking does not blur the timing.
        Unit b = Worker(sim, Setups.PlayerB);
        sim.Order(sim.Move(Setups.PlayerB, Center(12), Center(5), b));
        sim.RunUntil(s => drill.BuildProgress >= 100 && b.Order.Kind == UnitOrderKind.Idle);
        Assert.That(drill.BuildProgress, Is.EqualTo(100), "B must be in place before half way");

        int walletB = Wallet(sim, Setups.PlayerB);
        sim.Order(Repair(sim, Setups.PlayerB, drill, b));
        int rest = sim.RunUntil(s => drill.IsComplete);

        // 100 units of work left at 2 per tick = 50 ticks = 2.5 s (instead of 5 s); the order
        // lands on the next tick, which A works alone.
        Assert.That(rest, Is.InRange(50, 52));
        Assert.That(drill.OwnerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(Wallet(sim, Setups.PlayerB), Is.EqualTo(walletB));
    }

    [Test]
    public void GWT3_EnemyDrillOnAllCells_Destroyed_CellsFree_PlayerCanBuild()
    {
        var map = new MapSetup(32, 16).Start(Setups.PlayerA, 2, 2).OreRect(10, 6, 2, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Building enemy = Drill(sim, Setups.EnemyAi, 10, 6);
        Unit worker = Worker(sim, Setups.PlayerA);

        sim.Order(Build(sim, Setups.PlayerA, "drill", 10, 6, worker));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.PlaceTaken));

        enemy.Hp = 0;
        enemy.IsDead = true;
        sim.Tick();
        Assert.That(sim.State.Map.BuildingAt(10, 6), Is.EqualTo(0));

        sim.Order(Build(sim, Setups.PlayerA, "drill", 10, 6, worker));
        sim.RunUntil(s => SiteAt(s, 10, 6) is { OwnerId: Setups.PlayerA });
    }

    [Test]
    public void Edge_PartialOre_TwoCellsOfFour_QuarterUnitPerSecond()
    {
        var sim = Match();
        // x = 13..14: only x = 13 is ore → 2 ore cells of 4.
        Assert.That(DrillPreview.OreCellsUnder(sim.State.Map, 13, 6), Is.EqualTo(2));
        Building drill = Drill(sim, Setups.PlayerA, 13, 6);
        sim.RunUntil(s => drill.Drill!.Buffer == 1);
        int t = sim.RunUntil(s => drill.Drill!.Buffer == 2);
        Assert.That(t, Is.EqualTo(80));
    }

    [Test]
    public void Edge_TwoWorkersStartOnTheSameCellsInOneTick_LowerIdWins()
    {
        var sim = Match();
        Unit a = Worker(sim, Setups.PlayerA), b = Worker(sim, Setups.PlayerB);
        // Both in reach of the site already; their orders execute in the same tick.
        a.X = Center(9); a.Y = Center(7);
        b.X = Center(13); b.Y = Center(7);
        sim.Order(Build(sim, Setups.PlayerB, "drill", 11, 6, b));
        sim.Order(Build(sim, Setups.PlayerA, "drill", 10, 6, a));
        var rejected = sim.RunCollecting<CommandRejected>(3);

        Assert.That(a.Id, Is.LessThan(b.Id));
        Assert.That(SiteAt(sim, 10, 6)!.OwnerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(rejected.Single().UnitId, Is.EqualTo(b.Id));
        Assert.That(rejected.Single().Reason, Is.EqualTo(RejectReason.PlaceTaken));
    }

    [Test]
    public void Fail_NoLogistics_BufferFillsAndDrillStops()
    {
        var sim = Match();
        Building drill = Drill(sim, Setups.PlayerA, 10, 6);
        sim.Run(60 * Simulation.TicksPerSecond);
        Assert.That(drill.Drill!.Buffer, Is.EqualTo(10));
        Assert.That(drill.Drill.IsStopped, Is.True);
    }

    [Test]
    public void Fail_DrillDestroyed_BufferLost_CellsFreeForEveryone()
    {
        var sim = Match();
        Building drill = Drill(sim, Setups.PlayerA, 10, 6);
        sim.Run(200);
        Assert.That(drill.Drill!.Buffer, Is.GreaterThan(0));
        drill.Hp = 0;
        drill.IsDead = true;
        sim.Tick();
        for (int y = 6; y < 8; y++)
            for (int x = 10; x < 12; x++)
                Assert.That(sim.State.Map.IsBuildable(x, y), Is.True);
    }

    [Test]
    public void DrillNextToHeadquarters_HandsResourcesStraightIn_ToTheHeadquartersOwner()
    {
        // drill.md, assumption 3: the drill gives to any neighbouring building that accepts.
        var map = new MapSetup(32, 16).Start(Setups.PlayerB, 2, 2).OreRect(6, 2, 2, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Building drill = Drill(sim, Setups.PlayerA, 6, 2);  // A's drill touching B's HQ (x = 2..5)
        int a = Wallet(sim, Setups.PlayerA), b = Wallet(sim, Setups.PlayerB);

        sim.Run(400);
        Assert.That(Wallet(sim, Setups.PlayerB) - b, Is.EqualTo(10));  // 400 ticks / 40
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(a));
        Assert.That(drill.Drill!.Buffer, Is.EqualTo(0));
    }

    [Test]
    public void DrillNextToEnemyHeadquarters_GivesNothing()
    {
        var map = new MapSetup(32, 16).Start(Setups.EnemyAi, 2, 2).OreRect(6, 2, 2, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Building drill = Drill(sim, Setups.PlayerA, 6, 2);
        sim.Run(400);
        Assert.That(drill.Drill!.Buffer, Is.EqualTo(10));
        Assert.That(Wallet(sim, Setups.EnemyAi), Is.EqualTo(100));
    }

    [Test]
    public void UnfinishedDrill_DoesNotMine()
    {
        var sim = Match();
        Building site = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), 10, 6, Direction.Right, complete: false);
        sim.Run(200);
        Assert.That(site.Drill!.Buffer, Is.EqualTo(0));
    }
}
