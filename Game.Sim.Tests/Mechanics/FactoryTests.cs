using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.ConstructionTests;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/factory.md
public class FactoryTests
{
    private static (Simulation, Building) WithFactory(bool complete = true)
    {
        var sim = Match();
        Building factory = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("factory"), 10, 6, Direction.Right, complete);
        return (sim, factory);
    }

    private static ProduceCommand Produce(Simulation sim, int player, Building factory, string unit) =>
        new ProduceCommand(player, sim.State.Tick, factory.Id, unit);

    private static int UnitsOfType(Simulation sim, int player, string type) =>
        sim.State.UnitsOf(player).Count(u => u.Stats.Id == type);

    [Test]
    public void GWT1_InfantryQueued_PaidAtOnce_ReadyAfterItsBuildTime_NextToTheFactory()
    {
        var (sim, factory) = WithFactory();
        int start = Wallet(sim, Setups.PlayerA);
        sim.Order(Produce(sim, Setups.PlayerA, factory, "infantry"));
        sim.Tick();
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(start - 30));

        int ticks = sim.RunUntil(s => UnitsOfType(s, Setups.PlayerA, "infantry") == 1) + 1;
        Assert.That(ticks, Is.EqualTo(25 * Simulation.TicksPerSecond));
        Unit infantry = sim.State.UnitsOf(Setups.PlayerA).Single(u => u.Stats.Id == "infantry");
        Assert.That(Movement.DistanceToRectangle(infantry, 10, 6, 3, 3), Is.LessThanOrEqualTo(1000));
        Assert.That(infantry.OwnerId, Is.EqualTo(Setups.PlayerA));
    }

    [Test]
    public void GWT2_QueueIsProducedInOrder()
    {
        var (sim, factory) = WithFactory();
        sim.Order(Produce(sim, Setups.PlayerA, factory, "worker"));
        sim.Order(Produce(sim, Setups.PlayerA, factory, "infantry"));
        int workers = UnitsOfType(sim, Setups.PlayerA, "worker");

        int first = sim.RunUntil(s => UnitsOfType(s, Setups.PlayerA, "worker") == workers + 1);
        Assert.That(first, Is.EqualTo(12 * Simulation.TicksPerSecond));
        Assert.That(UnitsOfType(sim, Setups.PlayerA, "infantry"), Is.EqualTo(0));
        int second = sim.RunUntil(s => UnitsOfType(s, Setups.PlayerA, "infantry") == 1);
        Assert.That(second, Is.EqualTo(25 * Simulation.TicksPerSecond));
        Assert.That(factory.Factory!.Queue, Is.Empty);
    }

    [Test]
    public void GWT3_NotEnoughResources_Rejected_NothingSpent()
    {
        var (sim, factory) = WithFactory();
        var wallet = sim.State.GetPlayer(Setups.PlayerA).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary) - 20);
        sim.Order(Produce(sim, Setups.PlayerA, factory, "infantry"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotEnoughResources));
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(20));
    }

    [Test]
    public void Edge_QueueOfFive_SixthRejected()
    {
        var (sim, factory) = WithFactory();
        sim.State.GetPlayer(Setups.PlayerA).Wallet.Add(Resources.Primary, 500);
        for (int i = 0; i < 6; i++)
            sim.Order(Produce(sim, Setups.PlayerA, factory, "worker"));
        sim.Tick();
        Assert.That(factory.Factory!.Queue, Has.Count.EqualTo(5));
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.CannotProduce));
    }

    [Test]
    public void Edge_CancelTheUnitInProgress_FullRefund_ProgressLost()
    {
        var (sim, factory) = WithFactory();
        int start = Wallet(sim, Setups.PlayerA);
        sim.Order(Produce(sim, Setups.PlayerA, factory, "infantry"));
        sim.Run(100);
        sim.Order(new CancelProductionCommand(Setups.PlayerA, sim.State.Tick, factory.Id));
        sim.Tick();
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(start));
        Assert.That(factory.Factory!.Queue, Is.Empty);
        Assert.That(factory.Factory.Progress, Is.EqualTo(0));
        sim.Run(600);
        Assert.That(UnitsOfType(sim, Setups.PlayerA, "infantry"), Is.EqualTo(0));
    }

    [Test]
    public void Edge_AllyCannotUseMyFactory()
    {
        var (sim, factory) = WithFactory();
        sim.Order(Produce(sim, Setups.PlayerB, factory, "worker"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
    }

    [Test]
    public void Edge_UnfinishedFactory_Rejected()
    {
        var (sim, factory) = WithFactory(complete: false);
        sim.Order(Produce(sim, Setups.PlayerA, factory, "worker"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.CannotProduce));
    }

    [Test]
    public void Edge_UnitNotInProducesList_Rejected()
    {
        var (sim, factory) = WithFactory();
        sim.Order(Produce(sim, Setups.PlayerA, factory, "creep_small"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.CannotProduce));
    }

    [Test]
    public void Commands_RoundTripThroughTheCodec()
    {
        var codec = GameCommands.CreateCodec();
        var log = codec.ReadLog(codec.WriteLog(new ICommand[]
        {
            new ProduceCommand(1, 2, 33, "infantry"),
            new CancelProductionCommand(1, 3, 33),
        }));
        Assert.That(((ProduceCommand)log[0]).UnitTypeId, Is.EqualTo("infantry"));
        Assert.That(((CancelProductionCommand)log[1]).FactoryId, Is.EqualTo(33));
    }
}
