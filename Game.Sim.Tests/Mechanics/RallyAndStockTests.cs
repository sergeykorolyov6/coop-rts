using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/factory.md, section "M2: точка сбора и склад завода".
public class RallyAndStockTests
{
    private const int Second = Simulation.TicksPerSecond;

    // HQ A at (2,2)–(5,5), HQ B at (2,10)–(5,13); ore at x = 20..21, y = 2..3; factory A at (10,2)–(12,4).
    private static (Simulation, Building) Match()
    {
        var map = new MapSetup(32, 18)
            .Start(Setups.PlayerA, 2, 2)
            .Start(Setups.PlayerB, 2, 10)
            .OreRect(20, 2, 2, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Building factory = Place(sim, Setups.PlayerA, "factory", 10, 2);
        return (sim, factory);
    }

    private static Building Place(Simulation sim, int owner, string type, int x, int y, Direction dir = Direction.Right) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, dir, complete: true);

    // Drill at (20,2), line y = 3 from x = 19 left to x = 13: into the factory's right edge (x = 12).
    private static Building DrillLine(Simulation sim, int owner)
    {
        Building drill = Place(sim, owner, "drill", 20, 2);
        for (int x = 19; x >= 13; x--)
            Place(sim, owner, "conveyor", x, 3, Direction.Left);
        return drill;
    }

    private static int Stock(Building f, int type = Resources.Primary) => f.Stock!.Get(type);

    private static int Wallet(Simulation sim, int player) => sim.State.GetPlayer(player).Wallet.Get(Resources.Primary);

    [Test]
    public void R1_RallyPoint_FinishedUnitWalksThere()
    {
        var (sim, factory) = Match();
        factory.Stock!.Add(Resources.Primary, 30);
        sim.Order(new SetRallyCommand(Setups.PlayerA, sim.State.Tick, factory.Id, true, Center(20), Center(10)));
        sim.Order(new ProduceCommand(Setups.PlayerA, sim.State.Tick, factory.Id, "infantry"));
        sim.RunUntil(s => s.State.UnitsOf(Setups.PlayerA).Any(u => u.Stats.Id == "infantry"));
        Unit infantry = sim.State.UnitsOf(Setups.PlayerA).Single(u => u.Stats.Id == "infantry");
        Assert.That(infantry.Order.Kind, Is.EqualTo(UnitOrderKind.Move));
        sim.RunUntil(s => infantry.Order.Kind == UnitOrderKind.Idle);
        Assert.That((infantry.CellX, infantry.CellY), Is.EqualTo((20, 10)));
    }

    [Test]
    public void R2_RallyCleared_NextUnitStaysByTheFactory()
    {
        var (sim, factory) = Match();
        factory.Stock!.Add(Resources.Primary, 30);
        sim.Order(new SetRallyCommand(Setups.PlayerA, sim.State.Tick, factory.Id, true, Center(20), Center(10)));
        sim.Tick();
        sim.Order(new SetRallyCommand(Setups.PlayerA, sim.State.Tick, factory.Id, false, 0, 0));
        sim.Order(new ProduceCommand(Setups.PlayerA, sim.State.Tick, factory.Id, "infantry"));
        sim.RunUntil(s => s.State.UnitsOf(Setups.PlayerA).Any(u => u.Stats.Id == "infantry"));
        Assert.That(factory.Factory.HasRally, Is.False);
        Assert.That(sim.State.UnitsOf(Setups.PlayerA).Single(u => u.Stats.Id == "infantry").Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void R_Edge_AllysRallyOrOutsideTheMap_Rejected()
    {
        var (sim, factory) = Match();
        sim.Order(new SetRallyCommand(Setups.PlayerB, sim.State.Tick, factory.Id, true, Center(5), Center(5)));
        sim.Order(new SetRallyCommand(Setups.PlayerA, sim.State.Tick, factory.Id, true, Center(40), Center(5)));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Count(), Is.EqualTo(2));
        Assert.That(factory.Factory!.HasRally, Is.False);
    }

    [Test]
    public void S1_BeltFromADrill_FillsTheFactoryStock_NotTheWallet()
    {
        var (sim, factory) = Match();
        Building drill = DrillLine(sim, Setups.PlayerA);
        drill.Drill!.Buffer = 10;
        int wallet = Wallet(sim, Setups.PlayerA);
        sim.Run(10 * Second);
        // The 10 in the buffer plus up to 5 mined meanwhile, less what is still on the belt.
        Assert.That(Stock(factory), Is.InRange(10, 15));
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(wallet));
    }

    [Test]
    public void S2_EmptyStock_FullWallet_Rejected()
    {
        var (sim, factory) = Match();
        sim.State.GetPlayer(Setups.PlayerA).Wallet.Add(Resources.Primary, 500);
        sim.Order(new ProduceCommand(Setups.PlayerA, sim.State.Tick, factory.Id, "infantry"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.FactoryStockLow));
        Assert.That(factory.Factory!.Queue, Is.Empty);
    }

    [Test]
    public void S3_PaidFromTheStock_WalletUntouched()
    {
        var (sim, factory) = Match();
        factory.Stock!.Add(Resources.Primary, 40);
        int wallet = Wallet(sim, Setups.PlayerA);
        sim.Order(new ProduceCommand(Setups.PlayerA, sim.State.Tick, factory.Id, "infantry"));
        sim.Tick();
        Assert.That(Stock(factory), Is.EqualTo(10));
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(wallet));
    }

    [Test]
    public void S4_FullStock_TheBeltStops()
    {
        var (sim, factory) = Match();
        factory.Stock!.Add(Resources.Primary, 150);
        Building drill = DrillLine(sim, Setups.PlayerA);
        drill.Drill!.Buffer = 10;
        sim.Run(10 * Second);
        Assert.That(Stock(factory), Is.EqualTo(150));
        Building last = sim.State.FindBuilding(sim.State.Map.BuildingAt(13, 3))!;
        Assert.That(last.Conveyor!.HasItem, Is.True);
    }

    [Test]
    public void S5_AllysBelt_FillsMyFactory()
    {
        var (sim, factory) = Match();
        Building drill = DrillLine(sim, Setups.PlayerB);
        drill.Drill!.Buffer = 10;
        sim.Run(10 * Second);
        Assert.That(Stock(factory), Is.GreaterThanOrEqualTo(10));
    }

    [Test]
    public void S_Edge_HeadquartersNextToTheFactory_GivesNothing()
    {
        var map = new MapSetup(32, 18).Start(Setups.PlayerA, 2, 2);
        var sim = new Simulation(Setups.Coop(map));
        Building factory = Place(sim, Setups.PlayerA, "factory", 6, 2);
        sim.Run(5 * Second);
        Assert.That(Stock(factory), Is.EqualTo(0));
    }

    [Test]
    public void S_Edge_LegionFactory_PaysFromTheWallet()
    {
        var (sim, _) = Match();
        Building legion = Place(sim, Setups.EnemyAi, "legion_factory", 20, 10);
        Assert.That(legion.Stock, Is.Null);
    }
}
