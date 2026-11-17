using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/smart-line.md (U3): how two points and a bend side become the cells of a
// line — belts, skips, relays, bridges over obstacles, a sticking end. LinePlanner only reads the state.
public class SmartLineTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB;

    // 24×12; headquarters of A at (16,8), of B at (2,8) — away from rows 1–5 where the lines go;
    // a scout of A over (6,3) discovers them.
    private static Simulation Field(System.Action<MapSetup>? terrain = null)
    {
        var map = new MapSetup(24, 12).Start(A, 16, 8).Start(B, 2, 8);
        terrain?.Invoke(map);
        var sim = new Simulation(Setups.Coop(map));
        Spawn(sim, A, "scout", 6, 3);
        sim.Tick();
        return sim;
    }

    private static Simulation Rocks(params (int X, int Y)[] cells) => Field(map =>
    {
        foreach ((int x, int y) in cells)
            map.Block(x, y);
    });

    private static Building Place(Simulation sim, int owner, string type, int x, int y, Direction dir = Direction.Right) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, dir, complete: true);

    private static LinePlan Plan(Simulation sim, int x0, int y0, int x1, int y1, bool horizontalFirst = true, string type = "conveyor",
        Direction single = Direction.Right) =>
        LinePlanner.Plan(sim.State, A, type, x0, y0, x1, y1, horizontalFirst, single);

    private static (int, int, Direction, LineCellKind)[] Cells(LinePlan plan) =>
        plan.Cells.Select(c => (c.X, c.Y, c.Direction, c.Kind)).ToArray();

    private static LineCell At(LinePlan plan, int x, int y) => plan.Cells.Single(c => c.X == x && c.Y == y);

    // ---------- shape ----------

    [Test]
    public void GWT1_Straight_SixNewBelts_Right_CostSix()
    {
        var sim = Field();
        LinePlan plan = Plan(sim, 2, 2, 7, 2);

        Assert.That(Cells(plan), Is.EqualTo(Enumerable.Range(2, 6).Select(x => (x, 2, Direction.Right, LineCellKind.Build)).ToArray()));
        Assert.That(plan.CostPrimary, Is.EqualTo(6));
    }

    [Test]
    public void GWT2_HorizontalFirst_BendAtTheCorner_EightCells()
    {
        var sim = Field();
        LinePlan plan = Plan(sim, 2, 2, 6, 5, horizontalFirst: true);

        var expected = Enumerable.Range(2, 4).Select(x => (x, 2, Direction.Right, LineCellKind.Build))
            .Concat(Enumerable.Range(2, 4).Select(y => (6, y, Direction.Up, LineCellKind.Build))).ToArray();
        Assert.That(Cells(plan), Is.EqualTo(expected));
    }

    [Test]
    public void GWT3_VerticalFirst_BendOnTheOtherSide()
    {
        var sim = Field();
        LinePlan plan = Plan(sim, 2, 2, 6, 5, horizontalFirst: false);

        var expected = Enumerable.Range(2, 3).Select(y => (2, y, Direction.Up, LineCellKind.Build))
            .Concat(Enumerable.Range(2, 5).Select(x => (x, 5, Direction.Right, LineCellKind.Build))).ToArray();
        Assert.That(Cells(plan), Is.EqualTo(expected));
    }

    // ---------- bridges ----------

    [Test]
    public void GWT4_TwoRocks_BridgedOver_CostFifteen()
    {
        var sim = Rocks((4, 2), (5, 2));
        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(plan.Cells.Select(c => c.Kind), Is.EqualTo(new[]
        {
            LineCellKind.Build, LineCellKind.BridgeIn, LineCellKind.UnderBridge, LineCellKind.UnderBridge,
            LineCellKind.BridgeOut, LineCellKind.Build, LineCellKind.Build,
        }));
        Assert.That(plan.Cells.Select(c => c.X), Is.EqualTo(Enumerable.Range(2, 7)));
        Assert.That(plan.Cells.All(c => c.Direction == Direction.Right), Is.True);
        Assert.That(plan.CostPrimary, Is.EqualTo(3 + 2 * 6));
        Assert.That(plan.BridgeTypeId, Is.EqualTo("conveyor_bridge"));
    }

    [Test]
    public void GWT5_FourRocks_TooFar_OrderBreaksBeforeTheSpan()
    {
        var sim = Rocks((4, 2), (5, 2), (6, 2), (7, 2));
        LinePlan plan = Plan(sim, 2, 2, 9, 2);

        Assert.That(plan.Cells.Where(c => c.Kind == LineCellKind.Build).Select(c => c.X), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(plan.Cells.Where(c => c.Kind == LineCellKind.Broken).Select(c => c.X), Is.EqualTo(new[] { 4, 5, 6, 7, 8, 9 }));
        Assert.That((plan.Stop, plan.BrokenSpan), Is.EqualTo((LineStop.BridgeTooFar, 4)));
        Assert.That(plan.CostPrimary, Is.EqualTo(2));
    }

    [Test]
    public void GWT6_AllyBeltAcross_BridgedOver()
    {
        var sim = Field();
        for (int y = 1; y <= 3; y++)
            Place(sim, B, "conveyor", 5, y, Direction.Up);
        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(At(plan, 4, 2).Kind, Is.EqualTo(LineCellKind.BridgeIn));
        Assert.That(At(plan, 5, 2).Kind, Is.EqualTo(LineCellKind.UnderBridge));
        Assert.That(At(plan, 6, 2).Kind, Is.EqualTo(LineCellKind.BridgeOut));
    }

    [Test]
    public void GWT7_OwnBeltAcrossInTheMiddle_IsAnObstacle_BridgedOver()
    {
        var sim = Field();
        for (int y = 1; y <= 3; y++)
            Place(sim, A, "conveyor", 5, y, Direction.Up);
        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(At(plan, 4, 2).Kind, Is.EqualTo(LineCellKind.BridgeIn));
        Assert.That(At(plan, 6, 2).Kind, Is.EqualTo(LineCellKind.BridgeOut));
    }

    // ---------- the end ----------

    [Test]
    public void GWT8_EndsOnOwnBeltAcross_StopsBeforeIt_LooksIntoIt()
    {
        var sim = Field();
        for (int y = 1; y <= 3; y++)
            Place(sim, A, "conveyor", 5, y, Direction.Up);
        LinePlan plan = Plan(sim, 2, 2, 5, 2);

        Assert.That(Cells(plan), Is.EqualTo(Enumerable.Range(2, 3).Select(x => (x, 2, Direction.Right, LineCellKind.Build)).ToArray()));
        Assert.That(plan.MergesIntoBelt, Is.True);
    }

    [Test]
    public void GWT9_OwnLineDrawnBackwards_FiveRelays_Free()
    {
        var sim = Field();
        for (int x = 2; x <= 6; x++)
            Place(sim, A, "conveyor", x, 2);
        LinePlan plan = Plan(sim, 6, 2, 2, 2);

        Assert.That(plan.Cells.Select(c => (c.X, c.Direction, c.Kind)),
            Is.EqualTo(new[] { 6, 5, 4, 3, 2 }.Select(x => (x, Direction.Left, LineCellKind.Relay))));
        Assert.That(plan.CostPrimary, Is.EqualTo(0));
    }

    [Test]
    public void GWT10_OwnBeltAlongTheWay_Skipped_RestNew_CostFour()
    {
        var sim = Field();
        for (int x = 2; x <= 4; x++)
            Place(sim, A, "conveyor", x, 2);
        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(plan.Cells.Select(c => c.Kind), Is.EqualTo(
            Enumerable.Repeat(LineCellKind.Skip, 3).Concat(Enumerable.Repeat(LineCellKind.Build, 4))));
        Assert.That(plan.CostPrimary, Is.EqualTo(4));
    }

    [Test]
    public void GWT11_IntoOwnHeadquarters_ObstaclesAtTheEndDropped_LastLooksIntoIt()
    {
        var sim = Field();
        Building hq = Place(sim, A, "headquarters", 8, 1);
        LinePlan plan = Plan(sim, 2, 2, 9, 2);

        Assert.That(plan.Cells.Select(c => c.X), Is.EqualTo(Enumerable.Range(2, 6)));
        Assert.That(plan.Cells.Last().Direction, Is.EqualTo(Direction.Right));
        Assert.That(plan.StuckToId, Is.EqualTo(hq.Id));
    }

    [Test]
    public void GWT12_StorageBeside_LastTurnsIntoIt()
    {
        var sim = Field();
        Building storage = Place(sim, A, "storage", 5, 3);
        LinePlan plan = Plan(sim, 2, 2, 5, 2);

        Assert.That(Cells(plan), Is.EqualTo(new[]
        {
            (2, 2, Direction.Right, LineCellKind.Build), (3, 2, Direction.Right, LineCellKind.Build),
            (4, 2, Direction.Right, LineCellKind.Build), (5, 2, Direction.Up, LineCellKind.Build),
        }));
        Assert.That(plan.StuckToId, Is.EqualTo(storage.Id));
    }

    // ---------- heights ----------

    [Test]
    public void GWT13_CliffWithoutRamp_BridgeEndsOnBothSides()
    {
        var sim = Field(map =>
        {
            for (int y = 0; y < 12; y++)
                for (int x = 5; x < 24; x++)
                    map.Level(x, y, 1);
        });
        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(plan.Cells.Select(c => (c.X, c.Kind)), Is.EqualTo(new[]
        {
            (2, LineCellKind.Build), (3, LineCellKind.Build), (4, LineCellKind.BridgeIn), (5, LineCellKind.BridgeOut),
            (6, LineCellKind.Build), (7, LineCellKind.Build), (8, LineCellKind.Build),
        }));
    }

    [Test]
    public void GWT14_RampBetweenLevels_SevenBelts_NoBridge()
    {
        var sim = Field(map =>
        {
            for (int y = 0; y < 12; y++)
                for (int x = 6; x < 24; x++)
                    map.Level(x, y, 1);
            map.Level(5, 2, 0, ramp: true);
        });
        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(plan.Cells.Select(c => c.Kind), Is.EqualTo(Enumerable.Repeat(LineCellKind.Build, 7)));
    }

    // ---------- fail and walls ----------

    [Test]
    public void GWT15_RockOnTheFirstCell_NothingOrdered()
    {
        var sim = Rocks((2, 2));
        LinePlan plan = Plan(sim, 2, 2, 6, 2);

        Assert.That(plan.Stop, Is.EqualTo(LineStop.FirstCell));
        Assert.That(plan.Cells.All(c => c.Kind == LineCellKind.Broken), Is.True);
        Assert.That(plan.CostPrimary, Is.EqualTo(0));
    }

    [Test]
    public void GWT16_WallLine_SkipsTheRock_GoesOn()
    {
        var sim = Rocks((4, 2));
        LinePlan plan = Plan(sim, 2, 2, 6, 2, type: "wall");

        Assert.That(plan.Cells.Where(c => c.Kind == LineCellKind.Build).Select(c => c.X), Is.EqualTo(new[] { 2, 3, 5, 6 }));
        Assert.That(At(plan, 4, 2).Kind, Is.EqualTo(LineCellKind.Skip));
    }

    // ---------- Edge cases ----------

    [Test]
    public void Edge_BendOnAnObstacle_NoBridge_OrderUpToTheSpan()
    {
        var sim = Rocks((6, 2));
        LinePlan plan = Plan(sim, 2, 2, 6, 5, horizontalFirst: true);

        Assert.That(plan.Cells.Where(c => c.Kind == LineCellKind.Build).Select(c => (c.X, c.Y)),
            Is.EqualTo(new[] { (2, 2), (3, 2), (4, 2), (5, 2) }));
        Assert.That(At(plan, 6, 2).Kind, Is.EqualTo(LineCellKind.Broken));
    }

    [Test]
    public void Edge_CellBeforeTheSpanIsAnExistingBelt_NoBridge()
    {
        var sim = Rocks((4, 2));
        Place(sim, A, "conveyor", 3, 2);
        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(At(plan, 3, 2).Kind, Is.EqualTo(LineCellKind.Skip));
        Assert.That(plan.Cells.Where(c => c.X >= 4).All(c => c.Kind == LineCellKind.Broken), Is.True);
    }

    [Test]
    public void Edge_LongerThan256_CutAtThe256th()
    {
        var map = new MapSetup(300, 8).Start(A, 290, 4).Start(B, 280, 4);
        var sim = new Simulation(Setups.Coop(map));
        LinePlan plan = LinePlanner.Plan(sim.State, A, "conveyor", 1, 1, 299, 1, true, Direction.Right);

        Assert.That(plan.Cells, Has.Count.EqualTo(BuildCommand.MaxSites));
    }

    [Test]
    public void Edge_UndiscoveredCellInTheMiddle_Obstacle_BridgedWhenTheEndsAreDiscovered()
    {
        var sim = Field();
        MapState map = sim.State.Map;
        sim.State.Fog.DiscoveredByIndex(0)[2 * map.Width + 5] = false;
        Assert.That(sim.State.Fog.IsDiscovered(A, 5, 2), Is.False);

        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(At(plan, 4, 2).Kind, Is.EqualTo(LineCellKind.BridgeIn));
        Assert.That(At(plan, 5, 2).Kind, Is.EqualTo(LineCellKind.UnderBridge));
        Assert.That(At(plan, 6, 2).Kind, Is.EqualTo(LineCellKind.BridgeOut));
    }

    [Test]
    public void Edge_TwoSpansThroughOneFreeCell_SecondBridgeImpossible()
    {
        var sim = Rocks((4, 2), (6, 2));
        LinePlan plan = Plan(sim, 2, 2, 9, 2);

        Assert.That(At(plan, 3, 2).Kind, Is.EqualTo(LineCellKind.BridgeIn));
        Assert.That(At(plan, 5, 2).Kind, Is.EqualTo(LineCellKind.BridgeOut));
        Assert.That(plan.Cells.Where(c => c.X >= 6).All(c => c.Kind == LineCellKind.Broken), Is.True);
    }

    [Test]
    public void Edge_TwoSpansWithTwoFreeCellsBetween_TwoBridges()
    {
        var sim = Rocks((4, 2), (7, 2));
        LinePlan plan = Plan(sim, 2, 2, 10, 2);

        Assert.That(plan.Cells.Select(c => (c.X, c.Kind)), Is.EqualTo(new[]
        {
            (2, LineCellKind.Build), (3, LineCellKind.BridgeIn), (4, LineCellKind.UnderBridge), (5, LineCellKind.BridgeOut),
            (6, LineCellKind.BridgeIn), (7, LineCellKind.UnderBridge), (8, LineCellKind.BridgeOut),
            (9, LineCellKind.Build), (10, LineCellKind.Build),
        }));
    }

    [Test]
    public void Edge_BridgeExitOnTheBend_NoBridge()
    {
        // The output end gives only straight on along the bridge: a bend right after it would stand.
        var sim = Rocks((4, 2), (5, 2));
        LinePlan plan = Plan(sim, 2, 2, 6, 5, horizontalFirst: true);

        Assert.That(At(plan, 3, 2).Kind, Is.EqualTo(LineCellKind.Build));
        Assert.That(plan.Cells.Where(c => c.X >= 4).All(c => c.Kind == LineCellKind.Broken), Is.True);
    }

    [Test]
    public void Edge_CliffWithAnOwnBeltOnTheFarSide_StopsAndSaysWhy()
    {
        // x ≥ 5 is a plateau without a ramp; an own belt already lies on (5, 2), along the path:
        // a bridge end cannot stand on it, so the order stops at the cliff — and the plan says so.
        var sim = Field(map =>
        {
            for (int y = 0; y < 12; y++)
                for (int x = 5; x < 24; x++)
                    map.Level(x, y, 1);
        });
        Place(sim, A, "conveyor", 5, 2);
        LinePlan plan = Plan(sim, 2, 2, 8, 2);

        Assert.That(plan.Stop, Is.EqualTo(LineStop.NoBridge));
        Assert.That(plan.Cells.Where(c => c.Kind == LineCellKind.Build).Select(c => c.X), Is.EqualTo(new[] { 2, 3, 4 }));
        Assert.That(plan.Cells.Where(c => c.X >= 5).All(c => c.Kind == LineCellKind.Broken), Is.True);
    }

    [Test]
    public void Edge_PathAlongAnExistingLine_EndingBesideAStorage_TheLineIsNotTurnedIntoIt()
    {
        // The belt (2..8, 2) runs on past x = 5; drawing over its first cells must not cut it there.
        var sim = Field();
        for (int x = 2; x <= 8; x++)
            Place(sim, A, "conveyor", x, 2);
        Place(sim, A, "storage", 5, 3);
        LinePlan plan = Plan(sim, 2, 2, 5, 2);

        Assert.That(plan.Cells.All(c => c.Kind == LineCellKind.Skip && c.Direction == Direction.Right), Is.True);
        Assert.That(plan.StuckToId, Is.EqualTo(0));
        Assert.That(plan.HasOrders, Is.False);
    }

    [Test]
    public void Edge_EndOneCellBeforeAnOwnBelt_LooksIntoIt_ReportedAsMerge()
    {
        var sim = Field();
        for (int y = 1; y <= 3; y++)
            Place(sim, A, "conveyor", 5, y, Direction.Up);
        LinePlan plan = Plan(sim, 2, 2, 4, 2);

        Assert.That(plan.Cells.Select(c => c.X), Is.EqualTo(new[] { 2, 3, 4 }));
        Assert.That(plan.MergesIntoBelt, Is.True);
    }

    [Test]
    public void Edge_EndFacingAnOwnBeltHeadOn_IsNoMerge()
    {
        var sim = Field();
        Place(sim, A, "conveyor", 5, 2, Direction.Left);
        LinePlan plan = Plan(sim, 2, 2, 4, 2);

        Assert.That(plan.MergesIntoBelt, Is.False);
    }

    [Test]
    public void Edge_SingleCell_DirectionByR_NoSticking()
    {
        var sim = Field();
        Place(sim, A, "storage", 3, 2);
        LinePlan plan = Plan(sim, 2, 2, 2, 2, single: Direction.Down);

        Assert.That(Cells(plan), Is.EqualTo(new[] { (2, 2, Direction.Down, LineCellKind.Build) }));
        Assert.That(plan.StuckToId, Is.EqualTo(0));
    }

    [Test]
    public void Edge_ReceiverAheadAndOnTheLeft_AheadWins()
    {
        var sim = Field();
        Building ahead = Place(sim, A, "storage", 6, 1);
        Place(sim, A, "storage", 4, 3);
        LinePlan plan = Plan(sim, 2, 2, 5, 2);

        Assert.That(plan.Cells.Last().Direction, Is.EqualTo(Direction.Right));
        Assert.That(plan.StuckToId, Is.EqualTo(ahead.Id));
    }

    [Test]
    public void Edge_ReceiversOnTheLeftAndOnTheRight_LeftWins()
    {
        var sim = Field();
        Building left = Place(sim, A, "storage", 5, 3);
        Place(sim, A, "storage", 5, 0);
        LinePlan plan = Plan(sim, 2, 2, 5, 2);

        Assert.That(plan.Cells.Last().Direction, Is.EqualTo(Direction.Up));
        Assert.That(plan.StuckToId, Is.EqualTo(left.Id));
    }

    [Test]
    public void Edge_WholePathAlreadyThere_NothingToOrder()
    {
        var sim = Field();
        for (int x = 2; x <= 5; x++)
            Place(sim, A, "conveyor", x, 2);
        LinePlan plan = Plan(sim, 2, 2, 5, 2);

        Assert.That(plan.Cells.All(c => c.Kind == LineCellKind.Skip), Is.True);
        Assert.That(plan.HasOrders, Is.False);
    }

    [Test]
    public void Edge_BusyQueueFull_TheRestOfTheLineRejected()
    {
        var sim = Field();
        Unit builder = sim.State.FindBuilder(A)!;
        sim.Order(sim.Move(A, Center(10), Center(6), builder));
        for (int i = 0; i < UnitOrders.MaxQueue; i++)
            sim.Order(new MoveCommand(A, sim.State.Tick, new[] { builder.Id }, Center(10), Center(6), queued: true));
        sim.Tick();
        Assert.That(builder.OrderQueue, Has.Count.EqualTo(UnitOrders.MaxQueue));

        sim.Order(new BuildCommand(A, sim.State.Tick, new[] { builder.Id }, "conveyor", new[] { new BuildSite(2, 2, Direction.Right) }, queued: true));
        var rejected = sim.RunCollecting<CommandRejected>(1);

        Assert.That(rejected.Select(r => r.Reason), Is.EqualTo(new[] { RejectReason.NotAllowed }));
    }

    [Test]
    public void Plan_OnlyReadsTheState()
    {
        var sim = Rocks((4, 2));
        var hasher = new StateHasher();
        ulong before = hasher.Compute(sim.State);
        Plan(sim, 2, 2, 8, 5);
        Plan(sim, 2, 2, 8, 2, type: "wall");
        Assert.That(hasher.Compute(sim.State), Is.EqualTo(before));
    }
}
