using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/demolish.md
public class DemolishTests
{
    private static Simulation Match() => ConstructionTests.Match();

    private static Building Place(Simulation sim, int owner, string type, int x, int y, bool complete = true) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, Direction.Right, complete);

    private static int Wallet(Simulation sim, int type = Resources.Primary) =>
        sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(type);

    private static DemolishCommand Demolish(Simulation sim, Building b, int player = Setups.PlayerA) =>
        new DemolishCommand(player, sim.State.Tick, b.Id);

    [Test]
    public void GWT1_Drill_GoneAfterTwoAndAHalfSeconds_HalfThePriceBack_CellsFree()
    {
        var sim = Match();
        Building drill = Place(sim, Setups.PlayerA, "drill", 12, 6);
        int before = Wallet(sim);
        sim.Order(Demolish(sim, drill));
        int ticks = sim.RunUntil(s => s.State.FindBuilding(drill.Id) == null);
        Assert.That(ticks, Is.EqualTo(50));
        Assert.That(Wallet(sim), Is.EqualTo(before + 15));
        Assert.That(sim.State.Map.BuildingAt(12, 6), Is.EqualTo(0));
    }

    [Test]
    public void GWT2_Turret_RefundsBothResources()
    {
        var sim = Match();
        Building turret = Place(sim, Setups.PlayerA, "turret", 12, 6);
        int primary = Wallet(sim), secondary = Wallet(sim, Resources.Secondary);
        sim.Order(Demolish(sim, turret));
        var destroyed = sim.RunCollecting<EntityDestroyed>(5 * Simulation.TicksPerSecond);
        Assert.That(destroyed.Single().Demolished, Is.True);
        Assert.That(destroyed.Single().TypeId, Is.EqualTo("turret"));
        Assert.That(Wallet(sim), Is.EqualTo(primary + 20));
        Assert.That(Wallet(sim, Resources.Secondary), Is.EqualTo(secondary + 5));
    }

    [Test]
    public void GWT3_Conveyor_GoneInThreeTicks_NothingBack()
    {
        var sim = Match();
        Building belt = Place(sim, Setups.PlayerA, "conveyor", 12, 6);
        int before = Wallet(sim);
        sim.Order(Demolish(sim, belt));
        sim.Run(2);
        Assert.That(sim.State.FindBuilding(belt.Id), Is.Not.Null);
        sim.Tick();
        Assert.That(sim.State.FindBuilding(belt.Id), Is.Null);
        Assert.That(Wallet(sim), Is.EqualTo(before));
    }

    [Test]
    public void GWT4_SecondCommand_CancelsTheDemolition()
    {
        var sim = Match();
        Building factory = Place(sim, Setups.PlayerA, "factory", 12, 6);
        int before = Wallet(sim);
        sim.Order(Demolish(sim, factory));
        sim.Run(20);
        Assert.That(factory.DemolishTicksLeft, Is.GreaterThan(0));
        sim.Order(Demolish(sim, factory));
        sim.Run(400);
        Assert.That(sim.State.FindBuilding(factory.Id), Is.Not.Null);
        Assert.That(factory.DemolishTicksLeft, Is.EqualTo(0));
        Assert.That(Wallet(sim), Is.EqualTo(before));
    }

    [Test]
    public void GWT5_AllysBuilding_Rejected()
    {
        var sim = Match();
        Building drill = Place(sim, Setups.PlayerB, "drill", 12, 6);
        sim.Order(Demolish(sim, drill));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
        Assert.That(drill.DemolishTicksLeft, Is.EqualTo(0));
    }

    [Test]
    public void Edge_DestroyedWhileDemolishing_NoRefund()
    {
        var sim = Match();
        Building drill = Place(sim, Setups.PlayerA, "drill", 12, 6);
        int before = Wallet(sim);
        sim.Order(Demolish(sim, drill));
        sim.Run(10);
        drill.Hp = 0;
        drill.IsDead = true;
        sim.Run(60);
        Assert.That(Wallet(sim), Is.EqualTo(before));
    }

    [Test]
    public void Edge_UnfinishedDrill_SameTimeAndRefund()
    {
        var sim = Match();
        Building site = Place(sim, Setups.PlayerA, "drill", 12, 6, complete: false);
        int before = Wallet(sim);
        sim.Order(Demolish(sim, site));
        int ticks = sim.RunUntil(s => s.State.FindBuilding(site.Id) == null);
        Assert.That(ticks, Is.EqualTo(50));
        Assert.That(Wallet(sim), Is.EqualTo(before + 15));
    }

    [Test]
    public void Edge_Bridge_NothingBack_StorageHalf()
    {
        // Every price in buildings.csv is even today; rounding down is the integer division itself.
        var sim = Match();
        Building bridge = Place(sim, Setups.PlayerA, "conveyor_bridge", 12, 6);
        Building storage = Place(sim, Setups.PlayerA, "storage", 14, 6);
        int before = Wallet(sim);
        sim.Order(Demolish(sim, bridge));
        sim.Order(Demolish(sim, storage));
        sim.Run(5 * Simulation.TicksPerSecond);
        Assert.That(Wallet(sim), Is.EqualTo(before + 20));
    }

    [Test]
    public void Edge_InvulnerablePointOfInterest_Rejected()
    {
        var sim = Match();
        Building cave = Place(sim, Setups.PlayerA, "cave", 12, 6);
        sim.Order(Demolish(sim, cave));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
    }

    [Test]
    public void Edge_DemolishedHeadquarters_BuilderStillLives_MissionRules()
    {
        var sim = Match();
        Building hq = sim.State.FindHeadquarters(Setups.PlayerA)!;
        sim.Order(Demolish(sim, hq));
        int ticks = sim.RunUntil(s => s.State.FindHeadquarters(Setups.PlayerA) == null);
        Assert.That(ticks, Is.EqualTo(15 * Simulation.TicksPerSecond));
        Assert.That(sim.State.UnitsOf(Setups.PlayerA).Count(u => u.IsBuilder), Is.EqualTo(1));
    }
}
