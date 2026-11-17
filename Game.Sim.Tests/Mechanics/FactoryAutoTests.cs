using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/factory-auto.md (U6): a factory repeats its queue, paying each repeat from
// its stock; a building with a stock passes on what it does not take along the belts leading away.
public class FactoryAutoTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB;
    private const int Second = Simulation.TicksPerSecond;

    // Headquarters of A over (2..5, 2..5); the factory over (10..12, 6..8): its input belt at (9,7)
    // looks right into it, the output belt (13,7) → (14,7) looks away, into a storage at (15,7).
    private static Simulation Field(System.Action<MapSetup>? terrain = null)
    {
        var map = new MapSetup(32, 16).Start(A, 2, 2).Start(B, 26, 2);
        terrain?.Invoke(map);
        return new Simulation(Setups.Coop(map));
    }

    private static Building Place(Simulation sim, int owner, string type, int x, int y, Direction dir = Direction.Right) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, dir, complete: true);

    private static Building Factory(Simulation sim, int owner = A) => Place(sim, owner, "factory", 10, 6);

    private static int Stock(Building b, int type = Resources.Primary) => b.Stock!.Get(type);

    private static void SetStock(Building b, int primary, int secondary = 0)
    {
        Wallet stock = b.Stock!;
        stock.TrySpend(Resources.Primary, stock.Get(Resources.Primary));
        stock.TrySpend(Resources.Secondary, stock.Get(Resources.Secondary));
        stock.Add(Resources.Primary, primary);
        stock.Add(Resources.Secondary, secondary);
    }

    private static ProduceCommand Produce(Simulation sim, Building f, string unit, int player = A) =>
        new ProduceCommand(player, sim.State.Tick, f.Id, unit);

    private static SetRepeatCommand Repeat(Simulation sim, Building f, bool on, int player = A) =>
        new SetRepeatCommand(player, sim.State.Tick, f.Id, on);

    private static (string Unit, bool Paid)[] Queue(Building f) =>
        f.Factory!.Queue.Select((u, i) => (u, f.Factory.Paid[i])).ToArray();

    // A unit at the front edge of a belt cell: it moves on (into what is ahead) this tick.
    private static void Ready(Building belt, int type = Resources.Primary)
    {
        belt.Conveyor!.ItemType = type;
        belt.Conveyor.ItemPos = Conveyor.CellLength;
    }

    // ---------- U6.1: repeat ----------

    [Test]
    public void GWT1_RepeatOn_InfantryDone_QueuedAgainUnpaid_PaidNextTurn()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 60);
        sim.Order(Repeat(sim, f, true));
        sim.Order(Produce(sim, f, "infantry"));
        sim.Tick();
        Assert.That(Stock(f), Is.EqualTo(30));

        sim.RunUntil(s => s.Events.OfType<UnitProduced>().Any(), 30 * Second);
        Assert.That(Queue(f), Is.EqualTo(new[] { ("infantry", false) }));
        sim.Tick();

        Assert.That(Queue(f), Is.EqualTo(new[] { ("infantry", true) }));
        Assert.That(Stock(f), Is.EqualTo(0));
        Assert.That(f.Factory!.Progress, Is.GreaterThan(0));
    }

    [Test]
    public void GWT2_NothingInStock_TheRepeatWaitsUnpaid_NoProgress()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 30);
        sim.Order(Repeat(sim, f, true));
        sim.Order(Produce(sim, f, "infantry"));
        sim.RunUntil(s => s.Events.OfType<UnitProduced>().Any(), 30 * Second);

        sim.Run(10 * Second);

        Assert.That(Queue(f), Is.EqualTo(new[] { ("infantry", false) }));
        Assert.That(f.Factory!.Progress, Is.EqualTo(0));
    }

    [Test]
    public void GWT3_TheStockReachesThePrice_PaidOnTheFactorysNextTurn_ProductionGoes()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 30);
        sim.Order(Repeat(sim, f, true));
        sim.Order(Produce(sim, f, "infantry"));
        sim.RunUntil(s => s.Events.OfType<UnitProduced>().Any(), 30 * Second);
        sim.Run(Second);

        // Belts move after the factory in a tick: the 30th unit lands, the factory pays on its next turn.
        Building input = Place(sim, A, "conveyor", 9, 7);
        SetStock(f, 29);
        Ready(input);
        sim.Tick();
        Assert.That(Stock(f), Is.EqualTo(30));
        sim.Tick();

        Assert.That(Stock(f), Is.EqualTo(0));
        Assert.That(Queue(f), Is.EqualTo(new[] { ("infantry", true) }));
        Assert.That(f.Factory!.Progress, Is.EqualTo(1));
    }

    [Test]
    public void GWT4_InfantryAndShooter_StockKeptFull_OutInTurn()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 150);
        sim.Order(Repeat(sim, f, true));
        sim.Order(Produce(sim, f, "infantry"));
        sim.Order(Produce(sim, f, "shooter"));
        var made = new List<string>();
        sim.Run(3 * 60 * Second, s =>
        {
            SetStock(f, 150);
            foreach (UnitProduced e in s.Events.OfType<UnitProduced>())
                made.Add(s.State.FindUnit(e.UnitId)!.Stats.Id);
        });

        Assert.That(made.Count, Is.GreaterThanOrEqualTo(6));
        for (int i = 0; i < made.Count; i++)
            Assert.That(made[i], Is.EqualTo(i % 2 == 0 ? "infantry" : "shooter"), $"unit {i}");
    }

    [Test]
    public void GWT5_RepeatOff_UnpaidDropped_PaidStays_StockUntouched()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 100);
        sim.Order(Produce(sim, f, "shooter"));
        sim.Tick();
        f.Factory!.Enqueue("infantry", paid: false);
        int stock = Stock(f);

        sim.Order(Repeat(sim, f, false));
        sim.Tick();

        Assert.That(Queue(f), Is.EqualTo(new[] { ("shooter", true) }));
        Assert.That(Stock(f), Is.EqualTo(stock));
    }

    [Test]
    public void GWT6_CancelTheLastUnpaid_NothingBack()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 100);
        sim.Order(Produce(sim, f, "shooter"));
        sim.Tick();
        f.Factory!.Enqueue("infantry", paid: false);
        int stock = Stock(f);

        sim.Order(new CancelProductionCommand(A, sim.State.Tick, f.Id));
        sim.Tick();

        Assert.That(Queue(f), Is.EqualTo(new[] { ("shooter", true) }));
        Assert.That(Stock(f), Is.EqualTo(stock));
    }

    [Test]
    public void GWT7_CancelTheLastPaid_PriceBackToTheStock()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 100);
        sim.Order(Produce(sim, f, "shooter"));
        sim.Order(Produce(sim, f, "infantry"));
        sim.Tick();
        int stock = Stock(f);

        sim.Order(new CancelProductionCommand(A, sim.State.Tick, f.Id));
        sim.Tick();

        Assert.That(Queue(f), Is.EqualTo(new[] { ("shooter", true) }));
        Assert.That(Stock(f), Is.EqualTo(stock + 30));
    }

    [Test]
    public void GWT8_AllysFactory_RepeatRejected()
    {
        var sim = Field();
        Building f = Factory(sim, B);

        sim.Order(Repeat(sim, f, true));
        var rejected = sim.RunCollecting<CommandRejected>(1);

        Assert.That(rejected.Select(r => r.Reason), Is.EqualTo(new[] { RejectReason.NotAllowed }));
        Assert.That(f.Factory!.Repeat, Is.False);
    }

    [Test]
    public void GWT9_RepeatOnEmptyQueue_NothingMade()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 150);
        sim.Order(Repeat(sim, f, true));

        var made = sim.RunCollecting<UnitProduced>(60 * Second);

        Assert.That(made, Is.Empty);
        Assert.That(f.Factory!.Queue, Is.Empty);
    }

    [Test]
    public void GWT10_FullQueueOfFive_FirstDone_FiveAgain()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 150);
        sim.Order(Repeat(sim, f, true));
        for (int i = 0; i < 5; i++)
            sim.Order(Produce(sim, f, "scout"));
        sim.RunUntil(s => s.Events.OfType<UnitProduced>().Any(), 30 * Second);

        Assert.That(f.Factory!.Queue, Has.Count.EqualTo(5));
        Assert.That(f.Factory.Paid.Last(), Is.False);
    }

    // ---------- U6.2: passing on ----------

    private static (Simulation Sim, Building Factory, Building Input, Building[] Output, Building Storage) Line(System.Action<MapSetup>? terrain = null)
    {
        var sim = Field(terrain);
        Building f = Factory(sim);
        Building input = Place(sim, A, "conveyor", 9, 7);
        Building[] output = { Place(sim, A, "conveyor", 13, 7), Place(sim, A, "conveyor", 14, 7) };
        Building storage = Place(sim, A, "storage", 15, 7);
        return (sim, f, input, output, storage);
    }

    [Test]
    public void P1_FullStock_TheUnitGoesOnToTheOutputBelt_ThenToTheStorage()
    {
        (Simulation sim, Building f, Building input, Building[] output, Building storage) = Line();
        SetStock(f, 150);
        Ready(input);

        sim.Tick();
        Assert.That(output[0].Conveyor!.HasItem, Is.True);
        Assert.That(Stock(f), Is.EqualTo(150));

        sim.Run(2 * Second);
        Assert.That(storage.Storage!.Primary, Is.EqualTo(1));
    }

    [Test]
    public void P2_OneShortOfFull_FirstToTheStock_SecondOn()
    {
        (Simulation sim, Building f, Building input, Building[] output, _) = Line();
        SetStock(f, 149);
        Ready(input);
        sim.Tick();
        Assert.That(Stock(f), Is.EqualTo(150));
        Assert.That(output[0].Conveyor!.HasItem, Is.False);

        Ready(input);
        sim.Tick();
        Assert.That(output[0].Conveyor.HasItem, Is.True);
    }

    [Test]
    public void P3_PrimaryFull_SecondaryEmpty_SecondaryKept_PrimaryPassedOn()
    {
        (Simulation sim, Building f, Building input, Building[] output, _) = Line();
        SetStock(f, 150, 0);
        Ready(input, Resources.Secondary);
        sim.Tick();
        Assert.That(Stock(f, Resources.Secondary), Is.EqualTo(1));
        Assert.That(output[0].Conveyor!.HasItem, Is.False);

        Ready(input, Resources.Primary);
        sim.Tick();
        Assert.That(output[0].Conveyor.ItemType, Is.EqualTo(Resources.Primary));
    }

    [Test]
    public void P4_NoOutput_FullStock_TheInputStands()
    {
        var sim = Field();
        Building f = Factory(sim);
        Building input = Place(sim, A, "conveyor", 9, 7);
        SetStock(f, 150);
        Ready(input);

        sim.Run(Second);

        Assert.That(input.Conveyor!.HasItem, Is.True);
        Assert.That(input.Conveyor.ItemPos, Is.EqualTo(Conveyor.CellLength));
    }

    private static (Simulation Sim, Building Input, Building[] Right, Building[] Up) TwoOutputs(MatchSetup? setup = null)
    {
        var sim = new Simulation(setup ?? Setups.Coop(new MapSetup(32, 16).Start(A, 2, 2).Start(B, 26, 2)));
        Building f = Factory(sim);
        SetStock(f, 150, 150);
        Building input = Place(sim, A, "conveyor", 9, 7);
        Building[] right = Enumerable.Range(13, 4).Select(x => Place(sim, A, "conveyor", x, 7)).ToArray();
        Building[] up = Enumerable.Range(9, 4).Select(y => Place(sim, A, "conveyor", 11, y, Direction.Up)).ToArray();
        return (sim, input, right, up);
    }

    private static int Items(Building[] line) => line.Count(c => c.Conveyor!.HasItem);

    [Test]
    public void P5_TwoOutputs_FullStock_FourUnits_TwoEach()
    {
        (Simulation sim, Building input, Building[] right, Building[] up) = TwoOutputs();
        for (int i = 0; i < 4; i++)
        {
            Ready(input);
            sim.Run(6);
        }

        Assert.That((Items(right), Items(up)), Is.EqualTo((2, 2)));
    }

    [Test]
    public void P6_AmmoTurret_TakesOnlyPrimary_SecondaryPassedOn()
    {
        Simulation sim = ScavengerAndHeroTests.Match();
        int brown = Setups.PlayerA;
        Building gun = sim.State.PlaceBuilding(brown, sim.State.Data.Building("bs_scrap_gun"), 10, 6, Direction.Right, complete: true);
        Building input = sim.State.PlaceBuilding(brown, sim.State.Data.Building("conveyor"), 9, 6, Direction.Right, complete: true);
        Building output = sim.State.PlaceBuilding(brown, sim.State.Data.Building("conveyor"), 12, 6, Direction.Right, complete: true);
        Ready(input, Resources.Secondary);

        sim.Tick();

        Assert.That(output.Conveyor!.ItemType, Is.EqualTo(Resources.Secondary));
        Assert.That(gun.Stock!.Get(Resources.Secondary), Is.EqualTo(0));
    }

    [Test]
    public void P7_AmmoTurretWithoutOutput_SecondaryKept()
    {
        Simulation sim = ScavengerAndHeroTests.Match();
        int brown = Setups.PlayerA;
        Building gun = sim.State.PlaceBuilding(brown, sim.State.Data.Building("bs_scrap_gun"), 10, 6, Direction.Right, complete: true);
        Building input = sim.State.PlaceBuilding(brown, sim.State.Data.Building("conveyor"), 9, 6, Direction.Right, complete: true);
        Ready(input, Resources.Secondary);

        sim.Tick();

        Assert.That(gun.Stock!.Get(Resources.Secondary), Is.EqualTo(1));
    }

    [Test]
    public void P8_AllysOutputBelt_TakesThePassedUnit()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 150);
        Building input = Place(sim, A, "conveyor", 9, 7);
        Building ally = Place(sim, B, "conveyor", 13, 7);
        Ready(input);

        sim.Tick();

        Assert.That(ally.Conveyor!.HasItem, Is.True);
    }

    [Test]
    public void P9_TwoFactoriesInARow_FirstFills_ThenSecond_ThenTheHeadquarters()
    {
        // Left along y = 3: source (20..18) → factory 1 (15..17, 2..4) → (14,3) → factory 2 (11..13) → (6..10, 3) → headquarters.
        var sim = Field();
        Building[] feed = Enumerable.Range(18, 3).Reverse().Select(x => Place(sim, A, "conveyor", x, 3, Direction.Left)).ToArray();
        Building f1 = Place(sim, A, "factory", 15, 2);
        Place(sim, A, "conveyor", 14, 3, Direction.Left);
        Building f2 = Place(sim, A, "factory", 11, 2);
        for (int x = 10; x >= 6; x--)
            Place(sim, A, "conveyor", x, 3, Direction.Left);
        int wallet = sim.State.GetPlayer(A).Wallet.Get(Resources.Primary);
        bool secondBeforeFirst = false, hqBeforeSecond = false;

        for (int i = 0; i < 2 * 60 * Second; i++)
        {
            Conveyor tail = feed[0].Conveyor!;
            if (!tail.HasItem)
            {
                tail.ItemType = Resources.Primary;
                tail.ItemPos = 0;
            }
            sim.Tick();
            secondBeforeFirst |= Stock(f2) > 0 && Stock(f1) < 150;
            hqBeforeSecond |= sim.State.GetPlayer(A).Wallet.Get(Resources.Primary) > wallet && Stock(f2) < 150;
        }

        Assert.That(Stock(f1), Is.EqualTo(150));
        Assert.That(Stock(f2), Is.EqualTo(150));
        Assert.That(sim.State.GetPlayer(A).Wallet.Get(Resources.Primary), Is.GreaterThan(wallet));
        Assert.That(secondBeforeFirst, Is.False);
        Assert.That(hqBeforeSecond, Is.False);
    }

    [Test]
    public void P10_SnapshotMidway_RestoredGoesStepForStep()
    {
        MatchSetup setup = Setups.Coop(new MapSetup(32, 16).Start(A, 2, 2).Start(B, 26, 2));
        (Simulation sim, Building input, _, _) = TwoOutputs(setup);
        Ready(input);
        sim.Run(6);
        Ready(input);
        sim.Run(3);

        Simulation restored = Simulation.FromSnapshot(setup, sim.WriteSnapshot(), sim.CommandLog);
        Building inputR = restored.State.FindBuilding(input.Id)!;
        for (int i = 0; i < 2; i++)
        {
            Ready(input);
            Ready(inputR);
            for (int t = 0; t < 6; t++)
            {
                sim.Tick();
                restored.Tick();
                Assert.That(restored.StateHash, Is.EqualTo(sim.StateHash));
            }
        }
    }

    // ---------- Edge cases ----------

    [Test]
    public void Edge_RepeatingFactoryDestroyed_ItsQueueGoes()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 150);
        sim.Order(Repeat(sim, f, true));
        sim.Order(Produce(sim, f, "scout"));
        sim.Tick();

        Kill(f);
        var made = sim.RunCollecting<UnitProduced>(30 * Second);

        Assert.That(made, Is.Empty);
        Assert.That(sim.State.FindBuilding(f.Id), Is.Null);
    }

    [Test]
    public void Edge_RepeatOn_FiveQueued_OneMoreRejected()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 150);
        sim.Order(Repeat(sim, f, true));
        for (int i = 0; i < 5; i++)
            sim.Order(Produce(sim, f, "scout"));
        sim.Tick();

        sim.Order(Produce(sim, f, "scout"));
        var rejected = sim.RunCollecting<CommandRejected>(1);

        Assert.That(rejected.Select(r => r.Reason), Is.EqualTo(new[] { RejectReason.CannotProduce }));
    }

    [Test]
    public void Edge_UnpaidFirst_PlayersOrderThatTickPaysFirst_TheRepeatWaits()
    {
        var sim = Field();
        Building f = Factory(sim);
        f.Factory!.Enqueue("infantry", paid: false);
        SetStock(f, 30);

        sim.Order(Produce(sim, f, "infantry"));
        sim.Tick();

        Assert.That(Stock(f), Is.EqualTo(0));
        Assert.That(Queue(f), Is.EqualTo(new[] { ("infantry", false), ("infantry", true) }));
        Assert.That(f.Factory.Progress, Is.EqualTo(0));
    }

    [Test]
    public void Edge_Cave_RepeatsAndPassesOn()
    {
        var sim = Field();
        Building cave = Place(sim, A, "cave", 10, 6);
        SetStock(cave, 150, 150);
        UnitStats beast = sim.State.Data.Unit("cave_beast");
        sim.Order(Repeat(sim, cave, true));
        sim.Order(Produce(sim, cave, "cave_beast"));
        sim.RunUntil(s => s.Events.OfType<UnitProduced>().Any(), beast.BuildTimeTicks + 10);
        Assert.That(cave.Factory!.Queue, Is.EqualTo(new[] { "cave_beast" }));

        // Repeat off: the factory would pay the repeat from the stock and so take the unit itself.
        sim.Order(Repeat(sim, cave, false));
        sim.Tick();
        SetStock(cave, 150, 150);
        Building input = Place(sim, A, "conveyor", 9, 7);
        Building output = Place(sim, A, "conveyor", 13, 7);
        Ready(input);
        sim.Tick();
        Assert.That(output.Conveyor!.HasItem, Is.True);
    }

    [Test]
    public void Edge_DrillAgainstAFullFactory_GivesThroughItToTheOutputBelt()
    {
        var sim = Field(map => map.OreRect(8, 6, 2, 2, Resources.Primary));
        Building f = Factory(sim);
        SetStock(f, 150);
        Building drill = Place(sim, A, "drill", 8, 6);
        drill.Drill!.Buffer = 1;
        Building output = Place(sim, A, "conveyor", 13, 7);

        sim.Tick();

        Assert.That(drill.Drill.Buffer, Is.EqualTo(0));
        Assert.That(output.Conveyor!.HasItem, Is.True);
    }

    [Test]
    public void Edge_OnlyNeighbourIsTheInputBelt_NoOutput()
    {
        var sim = Field();
        Building f = Factory(sim);
        SetStock(f, 150);
        Building input = Place(sim, A, "conveyor", 9, 7);
        Ready(input);

        sim.Tick();

        Assert.That(input.Conveyor!.HasItem, Is.True);
        Assert.That(Stock(f), Is.EqualTo(150));
    }

    [Test]
    public void Edge_OutputBeltOverACliff_IsNoOutput()
    {
        // x ≥ 13 is a plateau with no ramp: the belt at (13,7) is up there.
        (Simulation sim, Building f, Building input, Building[] output, _) = Line(map =>
        {
            for (int y = 0; y < 16; y++)
                for (int x = 13; x < 32; x++)
                    map.Level(x, y, 1);
        });
        SetStock(f, 150);
        Ready(input);

        sim.Tick();

        Assert.That(output[0].Conveyor!.HasItem, Is.False);
        Assert.That(input.Conveyor!.ItemPos, Is.EqualTo(Conveyor.CellLength));
    }

    // ---------- what the UI draws beside a stock: who feeds it, where the surplus goes ----------

    private static (int X, int Y, Direction Flow, bool Feeds)[] Links(Simulation sim, Building b)
    {
        var links = new List<(Building Link, Direction Flow, bool Feeds)>();
        PlacementPreview.StockLinks(sim.State, b, links);
        return links.Select(l => (l.Link.CellX, l.Link.CellY, l.Flow, l.Feeds)).ToArray();
    }

    [Test]
    public void Links_InputBeltFeeds_BeltsLookingAwayOrPassingByTakeTheSurplus()
    {
        (Simulation sim, Building f, _, _, _) = Line();
        Place(sim, A, "conveyor", 11, 9, Direction.Left);   // passes by along the top edge
        Place(sim, B, "conveyor", 11, 5, Direction.Down);   // an ally's belt out of the bottom edge

        Assert.That(Links(sim, f), Is.EquivalentTo(new[]
        {
            (9, 7, Direction.Right, true),
            (13, 7, Direction.Right, false),
            (11, 9, Direction.Left, false),
            (11, 5, Direction.Down, false),
        }));
    }

    [Test]
    public void Links_AllysBeltLookingIn_Feeds_UnfinishedOrBeyondACliff_IsNoLink()
    {
        // x ≥ 13 is a plateau with no ramp: the belt at (13,7) is up there.
        var sim = Field(map =>
        {
            for (int y = 0; y < 16; y++)
                for (int x = 13; x < 32; x++)
                    map.Level(x, y, 1);
        });
        Building f = Factory(sim);
        Place(sim, B, "conveyor", 9, 7);
        Place(sim, A, "conveyor", 13, 7);
        sim.State.PlaceBuilding(A, sim.State.Data.Building("conveyor"), 11, 9, Direction.Up, complete: false);

        Assert.That(Links(sim, f), Is.EqualTo(new[] { (9, 7, Direction.Right, true) }));
    }

    [Test]
    public void Links_BridgeOutputFeeds_BridgeInputTakesTheSurplus_FlowByTheirPositions()
    {
        var sim = Field();
        Building f = Factory(sim);
        // In from the left over a gap: input (6,7) → output (9,7), which hands on into the factory.
        Place(sim, A, "conveyor_bridge", 6, 7, Direction.Up);
        Place(sim, A, "conveyor_bridge", 9, 7, Direction.Up);
        // Out to the right: input (13,7) → output (16,7).
        Place(sim, A, "conveyor_bridge", 13, 7, Direction.Up);
        Place(sim, A, "conveyor_bridge", 16, 7, Direction.Up);
        // An unlinked bridge end beside the factory is no link at all.
        Place(sim, A, "conveyor_bridge", 11, 9, Direction.Up);

        Assert.That(Links(sim, f), Is.EquivalentTo(new[]
        {
            (9, 7, Direction.Right, true),
            (13, 7, Direction.Right, false),
        }));
    }

    [Test]
    public void Links_NoStock_None()
    {
        var sim = Field();
        Building storage = Place(sim, A, "storage", 10, 6);
        Place(sim, A, "conveyor", 9, 6);

        Assert.That(Links(sim, storage), Is.Empty);
    }

    [Test]
    public void SetRepeat_RoundTripsThroughTheCodec()
    {
        CommandCodec codec = GameCommands.CreateCodec();
        byte[] encoded = codec.Encode(new SetRepeatCommand(2, 4, 77, true));

        var read = (SetRepeatCommand)codec.ReadAs(encoded, 2, 4);

        Assert.That((SetRepeatCommand.Id, read.FactoryId, read.On), Is.EqualTo((19, 77, true)));
    }
}
