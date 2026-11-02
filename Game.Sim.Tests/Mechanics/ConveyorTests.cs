using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;
using static Game.Sim.Tests.ConstructionTests;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/conveyors.md
public class ConveyorTests
{
    // HQ A at (2,2)–(5,5), HQ B at (2,10)–(5,13). Ore at x = 14..15, y = 2..3.
    private static Simulation Match()
    {
        var map = new MapSetup(32, 18)
            .Start(Setups.PlayerA, 2, 2)
            .Start(Setups.PlayerB, 2, 10)
            .OreRect(14, 2, 2, 2, Resources.Primary);
        return new Simulation(Setups.Coop(map));
    }

    private static Building Place(Simulation sim, int owner, string type, int x, int y, Direction dir = Direction.Right) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, dir, complete: true);

    // A straight line of finished conveyor cells from (x0, y) going left to x1 (x0 > x1).
    private static Building[] LineLeft(Simulation sim, int owner, int x0, int x1, int y) =>
        Enumerable.Range(0, x0 - x1 + 1).Select(i => Place(sim, owner, "conveyor", x0 - i, y, Direction.Left)).ToArray();

    private static int ItemsOn(params Building[] cells) => cells.Count(c => c.Conveyor!.ItemType != Conveyor.Empty);

    [Test]
    public void GWT1_DraggedLineOfFive_EachCellPaidWhenStarted_BuiltInAboutTwoAndAHalfSeconds()
    {
        var sim = Match();
        Unit worker = Worker(sim, Setups.PlayerA);
        var wallet = sim.State.GetPlayer(Setups.PlayerA).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary) - 10);
        worker.X = Center(6); worker.Y = Center(7);

        var sites = Enumerable.Range(7, 5).Select(x => new BuildSite(x, 7, Direction.Right)).ToArray();
        sim.Order(new BuildCommand(Setups.PlayerA, sim.State.Tick, new[] { worker.Id }, "conveyor", sites));
        sim.RunUntil(s => SiteAt(s, 7, 7) != null);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(9), "only the first cell is paid so far");

        int ticks = sim.RunUntil(s => Enumerable.Range(7, 5).All(x => SiteAt(s, x, 7) is { IsComplete: true }));
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(5));
        // 5 × 0.5 s of work plus short steps between cells.
        Assert.That(ticks + 1, Is.InRange(50, 80));
    }

    [Test]
    public void GWT2_DrillIntoLineIntoHeadquarters_FirstUnitInAboutASecond_BufferEmpties()
    {
        var sim = Match();
        // Line y = 4 from x = 13 left to x = 6, into HQ A (its right edge is x = 5).
        Building drill = Place(sim, Setups.PlayerA, "drill", 14, 3);
        LineLeft(sim, Setups.PlayerA, 13, 6, 4);
        drill.Drill!.Buffer = 10;
        int start = Wallet(sim, Setups.PlayerA);

        int first = sim.RunUntil(s => Wallet(s, Setups.PlayerA) > start);
        // 8 cells at 4 cells/s = 2 s; a 4-cell line (the spec's example) would take 1 s.
        Assert.That(first, Is.InRange(40, 45));
        sim.Run(10 * Simulation.TicksPerSecond - first);
        Assert.That(Wallet(sim, Setups.PlayerA) - start, Is.GreaterThan(10));
        Assert.That(drill.Drill.Buffer, Is.LessThan(2));
    }

    [Test]
    public void GWT3_OwnLineIntoAllyHeadquarters_CreditsTheAlly()
    {
        var sim = Match();
        // Drill of A at (6,14)… use a line into HQ B from the right: y = 11, x = 12..6.
        Building drill = Place(sim, Setups.PlayerA, "drill", 13, 11);
        drill.Drill!.Buffer = 10;
        LineLeft(sim, Setups.PlayerA, 12, 6, 11);
        int a = Wallet(sim, Setups.PlayerA), b = Wallet(sim, Setups.PlayerB);
        sim.Run(200);
        Assert.That(Wallet(sim, Setups.PlayerB), Is.GreaterThan(b));
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(a));
    }

    [Test]
    public void GWT4_LineWithNoReceiver_FillsOnePerCell_DrillStops()
    {
        var sim = Match();
        Building drill = Place(sim, Setups.PlayerA, "drill", 14, 2);
        // Ends at x = 9 facing (8,3): empty ground.
        var line = LineLeft(sim, Setups.PlayerA, 13, 9, 3);
        sim.Run(60 * Simulation.TicksPerSecond);
        Assert.That(ItemsOn(line), Is.EqualTo(line.Length));
        Assert.That(drill.Drill!.IsStopped, Is.True);
        Assert.That(drill.Drill.Buffer, Is.EqualTo(10));
    }

    [Test]
    public void GWT5_ThirdCellOfAFullLineDestroyed_ItsUnitLost_FrontDelivers_BackJams()
    {
        var sim = Match();
        // Six cells x = 11..6 on y = 4 into HQ A; cell 1 is x = 11.
        var line = LineLeft(sim, Setups.PlayerA, 11, 6, 4);
        foreach (Building c in line)
        {
            c.Conveyor!.ItemType = Resources.Primary;
            c.Conveyor.ItemPos = 0;
        }
        line[2].Hp = 0;
        line[2].IsDead = true;
        int start = Wallet(sim, Setups.PlayerA);
        sim.Run(100);

        Assert.That(Wallet(sim, Setups.PlayerA) - start, Is.EqualTo(3), "cells 4–6 deliver");
        Assert.That(ItemsOn(line[0], line[1]), Is.EqualTo(2), "cells 1–2 jam at the gap");
        Assert.That(line[1].Conveyor!.ItemPos, Is.EqualTo(Conveyor.CellLength));
    }

    [Test]
    public void GWT6_OwnLineIntoAllyLine_StopsAsAtAWall()
    {
        var sim = Match();
        var a = LineLeft(sim, Setups.PlayerA, 12, 10, 7);
        var b = LineLeft(sim, Setups.PlayerB, 9, 6, 7);
        a[0].Conveyor!.ItemType = Resources.Primary;
        sim.Run(100);
        Assert.That(a[2].Conveyor!.ItemType, Is.EqualTo(Resources.Primary));
        Assert.That(a[2].Conveyor.ItemPos, Is.EqualTo(Conveyor.CellLength));
        Assert.That(ItemsOn(b), Is.EqualTo(0));
    }

    [Test]
    public void GWT7_BridgeOverAnAllyLine_CarriesOwnResourceToHeadquarters_NoMixing()
    {
        var sim = Match();
        // B's line runs up the column x = 7 (y = 1..8), between A's line and HQ A.
        for (int y = 1; y <= 8; y++)
            Place(sim, Setups.PlayerB, "conveyor", 7, y, Direction.Up);
        // A: line x = 12..9 on y = 4, bridge (9 → 6) … input at x = 8? The input must not be on B's line:
        // input end at (8,4), output end at (6,4) facing the HQ; 2 cells apart, B's (7,4) underneath.
        var line = LineLeft(sim, Setups.PlayerA, 12, 9, 4);
        Building input = Place(sim, Setups.PlayerA, "conveyor_bridge", 8, 4, Direction.Left);
        Building output = Place(sim, Setups.PlayerA, "conveyor_bridge", 6, 4, Direction.Left);
        Assert.That(input.Bridge!.LinkedToId, Is.EqualTo(output.Id));
        Assert.That(output.Bridge!.IsOutputEnd, Is.True);

        line[0].Conveyor!.ItemType = Resources.Secondary;  // A's unit, distinguishable
        sim.State.FindBuilding(sim.State.Map.BuildingAt(7, 1))!.Conveyor!.ItemType = Resources.Primary;  // B's unit
        int a2 = Wallet(sim, Setups.PlayerA, Resources.Secondary);
        sim.Run(100);

        Assert.That(Wallet(sim, Setups.PlayerA, Resources.Secondary), Is.EqualTo(a2 + 1));
        // B's unit went up its own line to the end (y = 8) and stays there.
        Assert.That(sim.State.FindBuilding(sim.State.Map.BuildingAt(7, 8))!.Conveyor!.ItemType, Is.EqualTo(Resources.Primary));
    }

    [Test]
    public void Edge_OtherPlayerTriesToRebuildMyConveyor_Rejected()
    {
        var sim = Match();
        Place(sim, Setups.PlayerA, "conveyor", 8, 7, Direction.Left);
        sim.Order(new BuildCommand(Setups.PlayerB, sim.State.Tick, new[] { Worker(sim, Setups.PlayerB).Id }, "conveyor",
            new[] { new BuildSite(8, 7, Direction.Up) }));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.PlaceTaken));
        Assert.That(SiteAt(sim, 8, 7)!.Direction, Is.EqualTo(Direction.Left));
    }

    [Test]
    public void Edge_AllyHelpsBuildMyConveyor_ItStaysMine_AllyPaysNothing()
    {
        var sim = Match();
        Building site = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("conveyor"), 8, 7, Direction.Left, complete: false);
        int b = Wallet(sim, Setups.PlayerB);
        sim.Order(Repair(sim, Setups.PlayerB, site, Worker(sim, Setups.PlayerB)));
        sim.RunUntil(s => site.IsComplete);
        Assert.That(site.OwnerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(Wallet(sim, Setups.PlayerB), Is.EqualTo(b));
    }

    [Test]
    public void Edge_TwoDrillsOfferToOneCellInOneTick_LowerIdFirst()
    {
        var map = new MapSetup(32, 18).Start(Setups.PlayerA, 2, 2).OreRect(10, 8, 2, 5, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Building low = Place(sim, Setups.PlayerA, "drill", 10, 8);    // below the cell
        Building high = Place(sim, Setups.PlayerA, "drill", 10, 11);  // above the cell
        Building cell = Place(sim, Setups.PlayerA, "conveyor", 10, 10, Direction.Left);
        low.Drill!.Buffer = 1;
        high.Drill!.Buffer = 1;
        sim.Tick();
        Assert.That(low.Drill.Buffer, Is.EqualTo(0));
        Assert.That(high.Drill.Buffer, Is.EqualTo(1));
        Assert.That(cell.Conveyor!.ItemType, Is.Not.EqualTo(Conveyor.Empty));
    }

    [Test]
    public void Edge_DrillNextToTwoConveyors_AlternatesBetweenThem()
    {
        var sim = Match();
        Building drill = Place(sim, Setups.PlayerA, "drill", 14, 2);
        Building left = Place(sim, Setups.PlayerA, "conveyor", 13, 2, Direction.Down);
        Building right = Place(sim, Setups.PlayerA, "conveyor", 16, 2, Direction.Down);
        int toLeft = 0, toRight = 0;
        drill.Drill!.Buffer = 10;
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            if (left.Conveyor!.ItemType != Conveyor.Empty && left.Conveyor.ItemPos <= 200) toLeft++;
            if (right.Conveyor!.ItemType != Conveyor.Empty && right.Conveyor.ItemPos <= 200) toRight++;
        }
        Assert.That(toLeft, Is.GreaterThan(0));
        Assert.That(toRight, Is.GreaterThan(0));
    }

    [Test]
    public void Edge_LineIntoEnemyHeadquarters_StopsAsAtAWall()
    {
        var map = new MapSetup(32, 18).Start(Setups.PlayerA, 20, 2).Start(Setups.EnemyAi, 2, 2);
        var sim = new Simulation(Setups.Coop(map));
        var line = LineLeft(sim, Setups.PlayerA, 8, 6, 3);
        line[0].Conveyor!.ItemType = Resources.Primary;
        sim.Run(100);
        Assert.That(line[2].Conveyor!.ItemType, Is.EqualTo(Resources.Primary));
        Assert.That(Wallet(sim, Setups.EnemyAi), Is.EqualTo(100));
    }

    [Test]
    public void Edge_SecondBridgeEndOutOfRange_CannotBePlaced()
    {
        var sim = Match();
        Unit worker = Worker(sim, Setups.PlayerA);
        var far = new[] { new BuildSite(12, 7, Direction.Left), new BuildSite(7, 7, Direction.Left) };  // 5 apart > 4
        sim.Order(new BuildCommand(Setups.PlayerA, sim.State.Tick, new[] { worker.Id }, "conveyor_bridge", far));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));

        var crooked = new[] { new BuildSite(12, 7, Direction.Left), new BuildSite(10, 8, Direction.Left) };
        sim.Order(new BuildCommand(Setups.PlayerA, sim.State.Tick, new[] { worker.Id }, "conveyor_bridge", crooked));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
    }

    [Test]
    public void Edge_OneBridgeEndDestroyed_UnitsInFlightLost_OtherEndIdleUntilRebuilt()
    {
        var sim = Match();
        Building input = Place(sim, Setups.PlayerA, "conveyor_bridge", 10, 4, Direction.Left);
        Building output = Place(sim, Setups.PlayerA, "conveyor_bridge", 6, 4, Direction.Left);
        var feed = LineLeft(sim, Setups.PlayerA, 13, 11, 4);
        feed[0].Conveyor!.ItemType = Resources.Primary;
        sim.RunUntil(s => input.Bridge!.ItemsInFlight > 0);
        int start = Wallet(sim, Setups.PlayerA);

        output.Hp = 0;
        output.IsDead = true;
        sim.Tick();
        Assert.That(input.Bridge!.ItemsInFlight, Is.EqualTo(0));
        Assert.That(input.Bridge.LinkedToId, Is.EqualTo(0));

        feed[0].Conveyor!.ItemType = Resources.Primary;
        sim.Run(100);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(start));
        Assert.That(feed[2].Conveyor!.ItemPos, Is.EqualTo(Conveyor.CellLength), "waits at the idle input end");

        Building rebuilt = Place(sim, Setups.PlayerA, "conveyor_bridge", 6, 4, Direction.Left);
        Assert.That(input.Bridge.LinkedToId, Is.EqualTo(rebuilt.Id));
        sim.Run(100);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(start + 1));
    }

    [Test]
    public void Edge_TwoLinesIntoOneCellFromTheSides_LowerIdConnected_OtherStops()
    {
        var sim = Match();
        Building target = Place(sim, Setups.PlayerA, "conveyor", 10, 8, Direction.Left);
        Building fromBelow = Place(sim, Setups.PlayerA, "conveyor", 10, 7, Direction.Up);
        Building fromAbove = Place(sim, Setups.PlayerA, "conveyor", 10, 9, Direction.Down);
        fromBelow.Conveyor!.ItemType = Resources.Primary;
        fromAbove.Conveyor!.ItemType = Resources.Secondary;
        sim.Run(60);
        Assert.That(fromBelow.Id, Is.LessThan(fromAbove.Id));
        Assert.That(fromBelow.Conveyor.ItemType, Is.EqualTo(Conveyor.Empty));
        Assert.That(fromAbove.Conveyor.ItemType, Is.EqualTo(Resources.Secondary));
        Assert.That(fromAbove.Conveyor.ItemPos, Is.EqualTo(Conveyor.CellLength));
    }

    [Test]
    public void Throughput_IsFourUnitsPerSecond()
    {
        var sim = Match();
        // Storage at (14..15, 4..5) feeds a line y = 5 from x = 13 into HQ A (right edge x = 5).
        Building storage = Place(sim, Setups.PlayerA, "storage", 14, 4);
        storage.Storage!.Primary = 100;
        LineLeft(sim, Setups.PlayerA, 13, 6, 5);
        int start = Wallet(sim, Setups.PlayerA);
        sim.RunUntil(s => Wallet(s, Setups.PlayerA) > start);
        int w0 = Wallet(sim, Setups.PlayerA);
        sim.Run(5 * Simulation.TicksPerSecond);
        Assert.That(Wallet(sim, Setups.PlayerA) - w0, Is.InRange(19, 21));
    }

    [Test]
    public void Storage_MergesTwoLines_AndHoldsUpToCapacity()
    {
        var sim = Match();
        Building storage = Place(sim, Setups.PlayerA, "storage", 10, 8);
        // Two lines into the storage from the right (y = 8) and from above (x = 10).
        var right = LineLeft(sim, Setups.PlayerA, 13, 12, 8);
        Building top = Place(sim, Setups.PlayerA, "conveyor", 10, 10, Direction.Down);
        right[0].Conveyor!.ItemType = Resources.Primary;
        top.Conveyor!.ItemType = Resources.Secondary;
        sim.Run(40);
        Assert.That(storage.Storage!.Primary, Is.EqualTo(1));
        Assert.That(storage.Storage.Secondary, Is.EqualTo(1));

        // Full: lines stop in front of it.
        storage.Storage.Primary = 100 - storage.Storage.Secondary;
        top.Conveyor.ItemType = Resources.Secondary;
        sim.Run(40);
        Assert.That(top.Conveyor.ItemType, Is.EqualTo(Resources.Secondary));
        Assert.That(storage.Storage.Total, Is.EqualTo(100));
    }

    [Test]
    public void HeadquartersRebuiltOnTheSamePlace_LineDeliversAgain()
    {
        var sim = Match();
        var line = LineLeft(sim, Setups.PlayerA, 8, 6, 3);
        Building hq = sim.State.FindHeadquarters(Setups.PlayerA)!;
        hq.Hp = 0;
        hq.IsDead = true;
        line[0].Conveyor!.ItemType = Resources.Primary;
        sim.Run(60);
        Assert.That(line[2].Conveyor!.ItemPos, Is.EqualTo(Conveyor.CellLength));

        int start = Wallet(sim, Setups.PlayerA);
        Place(sim, Setups.PlayerA, "headquarters", 2, 2);
        sim.Run(5);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(start + 1));
    }

    [Test]
    public void Determinism_FullEconomy_SameHashesTwice()
    {
        Assert.That(Economy(), Is.EqualTo(Economy()));
    }

    private static ulong[] Economy()
    {
        var sim = Match();
        Place(sim, Setups.PlayerA, "drill", 14, 3);
        LineLeft(sim, Setups.PlayerA, 13, 6, 4);
        Unit w = Worker(sim, Setups.PlayerB);
        sim.Order(new MineCommand(Setups.PlayerB, 1, new[] { w.Id }, 14, 2));
        var hashes = new ulong[600];
        for (int i = 0; i < hashes.Length; i++)
        {
            sim.Tick();
            hashes[i] = sim.StateHash;
        }
        return hashes;
    }
}
