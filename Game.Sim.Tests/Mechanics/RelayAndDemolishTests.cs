using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;
using static Game.Sim.Tests.ConstructionTests;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/relay-and-demolish.md (U4): a belt laid over an own belt turns it;
// a demolition frame takes down everything own inside.
public class RelayAndDemolishTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB;

    // A at (2,2) (headquarters over cells 2–5), B at (20,2); a scout of A over (10,8) so all is discovered.
    private static Simulation Field()
    {
        var map = new MapSetup(32, 16).Start(A, 2, 2).Start(B, 26, 2).OreRect(14, 10, 4, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Spawn(sim, A, "scout", 10, 8);
        sim.Tick();
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string type, int x, int y, Direction dir = Direction.Right, bool complete = true) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, dir, complete);

    private static Unit Builder(Simulation sim, int player = A) => sim.State.FindBuilder(player)!;

    private static void Near(Unit unit, int cx, int cy)
    {
        unit.X = Center(cx);
        unit.Y = Center(cy);
    }

    private static BuildCommand Belt(Simulation sim, Unit builder, Direction dir, params (int X, int Y)[] cells) =>
        new BuildCommand(builder.OwnerId, sim.State.Tick, new[] { builder.Id }, "conveyor",
            cells.Select(c => new BuildSite(c.X, c.Y, dir)).ToArray());

    private static DemolishAreaCommand Frame(Simulation sim, int x0, int y0, int x1, int y1, int player = A) =>
        new DemolishAreaCommand(player, sim.State.Tick, x0, y0, x1, y1);

    private static List<CommandRejected> Rejections(Simulation sim, int ticks) => sim.RunCollecting<CommandRejected>(ticks);

    // ---------- U4.1: relaying a belt ----------

    [Test]
    public void GWT1_OwnBeltWithItem_RelaidUp_TurnsInPlace_ItemStays_Free_SameId()
    {
        var sim = Field();
        Building belt = Place(sim, A, "conveyor", 5, 8);
        belt.Conveyor!.ItemType = Resources.Primary;
        belt.Conveyor.ItemPos = 400;
        Unit builder = Builder(sim);
        Near(builder, 4, 9);
        int wallet = Wallet(sim, A);

        sim.Order(Belt(sim, builder, Direction.Up, (5, 8)));
        sim.Tick();

        Assert.That(belt.Direction, Is.EqualTo(Direction.Up));
        Assert.That(SiteAt(sim, 5, 8)!.Id, Is.EqualTo(belt.Id));
        // Kept, not reset: in the same tick, after the builder, the belt moves it on by one step.
        Assert.That((belt.Conveyor.ItemType, belt.Conveyor.ItemPos), Is.EqualTo((Resources.Primary, 400 + sim.State.Rules.ConveyorItemSpeedMilliPerTick)));
        Assert.That(Wallet(sim, A), Is.EqualTo(wallet));
    }

    [Test]
    public void GWT2_LineIntoHeadquarters_RelaidLeftWholly_ItemsGoLeft()
    {
        var sim = Field();
        // (8..12, 8) to the right into a storage at (13, 7); relaid to the left, into a storage at (6, 7).
        Building right = Place(sim, A, "storage", 13, 7);
        Building left = Place(sim, A, "storage", 6, 7);
        Building[] line = Enumerable.Range(8, 5).Select(x => Place(sim, A, "conveyor", x, 8)).ToArray();
        line[2].Conveyor!.ItemType = Resources.Primary;
        Unit builder = Builder(sim);
        Near(builder, 10, 10);

        sim.Order(Belt(sim, builder, Direction.Left, (8, 8), (9, 8), (10, 8), (11, 8), (12, 8)));
        sim.Tick();
        Assert.That(line.Select(b => b.Direction), Is.All.EqualTo(Direction.Left));

        sim.Run(60);
        Assert.That(left.Storage!.Primary, Is.EqualTo(1));
        Assert.That(right.Storage!.Primary, Is.EqualTo(0));
    }

    [Test]
    public void GWT3_SameDirection_NothingChanges_NoRejection_BuilderIdle()
    {
        var sim = Field();
        Building belt = Place(sim, A, "conveyor", 5, 8);
        Unit builder = Builder(sim);
        Near(builder, 4, 9);

        sim.Order(Belt(sim, builder, Direction.Right, (5, 8)));
        var rejected = Rejections(sim, 2);

        Assert.That(rejected, Is.Empty);
        Assert.That(belt.Direction, Is.EqualTo(Direction.Right));
        Assert.That(builder.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        Assert.That(builder.Worker!.Plans, Is.Empty);
    }

    [Test]
    public void GWT4_AllyBelt_PlaceTaken()
    {
        var sim = Field();
        Building belt = Place(sim, B, "conveyor", 5, 8);
        Unit builder = Builder(sim);
        Near(builder, 4, 9);

        sim.Order(Belt(sim, builder, Direction.Up, (5, 8)));
        var rejected = Rejections(sim, 1);

        Assert.That(rejected.Select(r => r.Reason), Is.EqualTo(new[] { RejectReason.PlaceTaken }));
        Assert.That(belt.Direction, Is.EqualTo(Direction.Right));
    }

    [Test]
    public void GWT5_OwnDrill_PlaceTaken()
    {
        var sim = Field();
        Place(sim, A, "drill", 14, 10);
        Unit builder = Builder(sim);
        Near(builder, 13, 9);

        sim.Order(Belt(sim, builder, Direction.Up, (14, 10)));
        var rejected = Rejections(sim, 1);

        Assert.That(rejected.Select(r => r.Reason), Is.EqualTo(new[] { RejectReason.PlaceTaken }));
    }

    [Test]
    public void GWT6_BuilderFarAway_TurnsOnlyOnceTheCellIsInRange()
    {
        var sim = Field();
        Building belt = Place(sim, A, "conveyor", 15, 8);
        Unit builder = Builder(sim);
        Near(builder, 4, 8);

        sim.Order(Belt(sim, builder, Direction.Up, (15, 8)));
        sim.Tick();
        Assert.That(belt.Direction, Is.EqualTo(Direction.Right));

        sim.RunUntil(s => belt.Direction == Direction.Up, 200);
        Assert.That(Movement.DistanceToRectangle(builder, 15, 8, 1, 1), Is.LessThanOrEqualTo(sim.State.Rules.BuildRangeMilli));
    }

    // ---------- U4.2: demolition frame ----------

    [Test]
    public void GWT7_FrameOverBeltsDrillStorage_AllyTurretStays()
    {
        var sim = Field();
        Building[] belts = Enumerable.Range(2, 6).Select(x => Place(sim, A, "conveyor", x, 9)).ToArray();
        Building drill = Place(sim, A, "drill", 14, 10);
        Building storage = Place(sim, A, "storage", 10, 11);
        Building turret = Place(sim, B, "turret", 12, 12);
        int wallet = Wallet(sim, A);

        sim.Order(Frame(sim, 2, 9, 17, 13));
        sim.Run(4);
        Assert.That(belts.All(b => sim.State.FindBuilding(b.Id) == null), Is.True, "belts go in 3 ticks");
        Assert.That(sim.State.FindBuilding(drill.Id), Is.Not.Null);

        int drillTicks = sim.RunUntil(s => s.State.FindBuilding(drill.Id) == null, 200) + 4;
        Assert.That(drillTicks, Is.EqualTo(50));
        Assert.That(Wallet(sim, A), Is.EqualTo(wallet + 15));
        int storageTicks = sim.RunUntil(s => s.State.FindBuilding(storage.Id) == null, 200) + drillTicks;
        Assert.That(storageTicks, Is.EqualTo(75));
        Assert.That(Wallet(sim, A), Is.EqualTo(wallet + 35));
        Assert.That(turret.IsDead, Is.False);
        Assert.That(turret.DemolishTicksLeft, Is.EqualTo(0));
    }

    [Test]
    public void GWT8_FrameOverHeadquartersAndTurret_TurretGoes_HeadquartersStays()
    {
        var sim = Field();
        Building hq = sim.State.FindHeadquarters(A)!;
        Building turret = Place(sim, A, "turret", 7, 2);

        sim.Order(Frame(sim, 0, 0, 10, 6));
        sim.Tick();

        Assert.That(turret.DemolishTicksLeft, Is.GreaterThan(0));
        Assert.That(hq.DemolishTicksLeft, Is.EqualTo(0));
    }

    [Test]
    public void GWT9_DrillAlreadyGoing_NotCancelled_NotRestarted()
    {
        var sim = Field();
        Building drill = Place(sim, A, "drill", 14, 10);
        drill.DemolishTicksLeft = 10;

        sim.Order(Frame(sim, 14, 10, 15, 11));
        int ticks = sim.RunUntil(s => s.State.FindBuilding(drill.Id) == null, 100);

        Assert.That(ticks, Is.EqualTo(10));
    }

    [Test]
    public void GWT10_ThreeSilhouettes_TwoInTheFrame_Dropped_BuilderGoesOnWithTheThird()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Near(builder, 8, 13);
        SetEmpty(sim);
        sim.Order(new BuildCommand(A, sim.State.Tick, new[] { builder.Id }, "storage",
            new[] { new BuildSite(10, 10, Direction.Right), new BuildSite(20, 10, Direction.Right), new BuildSite(10, 12, Direction.Right) }));
        sim.Tick();

        sim.Order(Frame(sim, 9, 9, 12, 14));
        sim.Tick();

        Assert.That(builder.Worker!.Plans.Select(p => (p.CellX, p.CellY)), Is.EqualTo(new[] { (20, 10) }));
        Assert.That(builder.Order.Kind, Is.EqualTo(UnitOrderKind.Build));
    }

    // No money: the silhouettes stay silhouettes (builder-beam.md), none is started.
    private static void SetEmpty(Simulation sim)
    {
        Wallet wallet = sim.State.GetPlayer(A).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary));
    }

    [Test]
    public void GWT11_QueuedOrderOfTwo_BothInTheFrame_RemovedFromTheQueue()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Near(builder, 4, 12);
        sim.Order(sim.Move(A, Center(4), Center(14), builder));
        sim.Order(new BuildCommand(A, sim.State.Tick, new[] { builder.Id }, "conveyor",
            new[] { new BuildSite(10, 10, Direction.Right), new BuildSite(11, 10, Direction.Right) }, queued: true));
        sim.Order(new MoveCommand(A, sim.State.Tick, new[] { builder.Id }, Center(6), Center(14), queued: true));
        sim.Tick();
        Assert.That(builder.OrderQueue, Has.Count.EqualTo(2));

        sim.Order(Frame(sim, 9, 9, 12, 11));
        sim.Tick();

        Assert.That(builder.OrderQueue.Select(o => o.Order.Kind), Is.EqualTo(new[] { UnitOrderKind.Move }));
    }

    [Test]
    public void GWT12_FrameTouchesOneCellOfTheDrill_DrillGoes()
    {
        var sim = Field();
        Building drill = Place(sim, A, "drill", 14, 10);

        sim.Order(Frame(sim, 15, 11, 18, 14));
        sim.Tick();

        Assert.That(drill.DemolishTicksLeft, Is.GreaterThan(0));
    }

    // ---------- Edge cases ----------

    [Test]
    public void Edge_RelayingABeltBeingDemolished_TurnsAndStillGoes()
    {
        var sim = Field();
        Building belt = Place(sim, A, "conveyor", 5, 8);
        Unit builder = Builder(sim);
        Near(builder, 4, 9);
        sim.Order(new DemolishCommand(A, sim.State.Tick, belt.Id));
        sim.Order(Belt(sim, builder, Direction.Up, (5, 8)));
        sim.Tick();

        Assert.That(belt.Direction, Is.EqualTo(Direction.Up));
        sim.Run(3);
        Assert.That(sim.State.FindBuilding(belt.Id), Is.Null);
    }

    [Test]
    public void Edge_BeltTurnedHeadOnIntoItsNeighbour_BothStand()
    {
        var sim = Field();
        Building back = Place(sim, A, "conveyor", 8, 8);
        Building front = Place(sim, A, "conveyor", 9, 8);
        front.Conveyor!.ItemType = Resources.Primary;
        Unit builder = Builder(sim);
        Near(builder, 8, 10);

        sim.Order(Belt(sim, builder, Direction.Left, (9, 8)));
        sim.Run(60);

        Assert.That(front.Direction, Is.EqualTo(Direction.Left));
        Assert.That(front.Conveyor.HasItem, Is.True);
        Assert.That(front.Conveyor.ItemPos, Is.EqualTo(Conveyor.CellLength));
        Assert.That(back.Conveyor!.HasItem, Is.False);
    }

    [Test]
    public void Edge_StartedSiteInTheFrame_IsABuilding_DemolishedWithHalfBack()
    {
        var sim = Field();
        Building site = Place(sim, A, "storage", 10, 10, complete: false);
        int wallet = Wallet(sim, A);

        sim.Order(Frame(sim, 10, 10, 10, 10));
        sim.RunUntil(s => s.State.FindBuilding(site.Id) == null, 200);

        Assert.That(Wallet(sim, A), Is.EqualTo(wallet + 20));
    }

    [Test]
    public void Edge_AllySilhouetteInMyFrame_Untouched()
    {
        var sim = Field();
        Unit ally = Builder(sim, B);
        Near(ally, 24, 13);
        Wallet bWallet = sim.State.GetPlayer(B).Wallet;
        bWallet.TrySpend(Resources.Primary, bWallet.Get(Resources.Primary));
        sim.Order(new BuildCommand(B, sim.State.Tick, new[] { ally.Id }, "storage", new[] { new BuildSite(10, 10, Direction.Right) }));
        sim.Tick();

        sim.Order(Frame(sim, 9, 9, 12, 12));
        sim.Tick();

        Assert.That(ally.Worker!.Plans, Has.Count.EqualTo(1));
    }

    [Test]
    public void Edge_FrameWhollyOffTheMap_Nothing()
    {
        var sim = Field();
        Building belt = Place(sim, A, "conveyor", 31, 15);

        sim.Order(Frame(sim, 40, 20, 50, 30));
        var rejected = Rejections(sim, 1);

        Assert.That(rejected, Is.Empty);
        Assert.That(belt.DemolishTicksLeft, Is.EqualTo(0));
    }

    [Test]
    public void Edge_Scavengers_RefundTheWholePrice_AsWithOneDemolition()
    {
        Simulation sim = ScavengerAndHeroTests.Match();
        int brown = Setups.PlayerA;
        Building drill = sim.State.PlaceBuilding(brown, sim.State.Data.Building("drill"), 20, 2, Direction.Right, complete: true);
        int before = sim.State.GetPlayer(brown).Wallet.Get(Resources.Primary);

        sim.Order(new DemolishAreaCommand(brown, sim.State.Tick, 20, 2, 21, 3));
        sim.RunUntil(s => s.State.FindBuilding(drill.Id) == null, 200);

        Assert.That(sim.State.GetPlayer(brown).Wallet.Get(Resources.Primary) - before, Is.EqualTo(30));
    }

    [Test]
    public void Edge_CapturedNeutralDrill_DemolishedAsOwn()
    {
        var sim = Field();
        Building drill = Place(sim, sim.State.NeutralPlayerId, "drill", 14, 10);
        drill.OwnerId = A;  // what a capture does (neutral-buildings.md)

        sim.Order(Frame(sim, 14, 10, 15, 11));
        sim.Tick();

        Assert.That(drill.DemolishTicksLeft, Is.GreaterThan(0));
    }

    [Test]
    public void Command_RoundTripsThroughTheCodec()
    {
        CommandCodec codec = GameCommands.CreateCodec();
        byte[] encoded = codec.Encode(new DemolishAreaCommand(2, 7, 3, 4, 9, 11));

        var read = (DemolishAreaCommand)codec.ReadAs(encoded, 2, 7);

        Assert.That((DemolishAreaCommand.Id, read.X0, read.Y0, read.X1, read.Y1), Is.EqualTo((18, 3, 4, 9, 11)));
    }

    [Test]
    public void Frame_GivenFromTheOtherCorner_SameResult()
    {
        var sim = Field();
        Building storage = Place(sim, A, "storage", 10, 11);

        sim.Order(Frame(sim, 12, 12, 9, 10));
        sim.Tick();

        Assert.That(storage.DemolishTicksLeft, Is.GreaterThan(0));
    }
}
