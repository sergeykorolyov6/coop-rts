using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/coop-tools.md
public class CoopToolsTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB, Legion = Setups.EnemyAi;

    // A and B with a headquarters each, against the Legion's base.
    private static Simulation Match()
    {
        var map = new MapSetup(48, 24)
            .Start(A, 2, 2)
            .Start(B, 2, 14)
            .LegionSlot("legion_hq", 40, 10)
            .Rally(34, 12);
        var sim = new Simulation(Setups.WithLegion(map));
        // Round numbers: A has 100 of the primary and 40 of the secondary, B 20 and 0.
        Wallet a = sim.State.GetPlayer(A).Wallet, b = sim.State.GetPlayer(B).Wallet;
        a.TrySpend(Resources.Primary, a.Get(Resources.Primary));
        b.TrySpend(Resources.Primary, b.Get(Resources.Primary));
        a.Add(Resources.Primary, 100);
        a.Add(Resources.Secondary, 40);
        b.Add(Resources.Primary, 20);
        return sim;
    }

    private static (int Primary, int Secondary) Purse(Simulation sim, int player) =>
        (sim.State.GetPlayer(player).Wallet.Get(Resources.Primary), sim.State.GetPlayer(player).Wallet.Get(Resources.Secondary));

    private static T? Only<T>(Simulation sim) where T : SimEvent => sim.RunCollecting<T>(1).SingleOrDefault();

    [Test]
    public void GWT1_Ping_RaisesTheEvent_AndChangesNoState()
    {
        Simulation with = Match(), without = Match();
        with.Order(new PingCommand(A, 0, 12500, 7500));
        Pinged? ping = Only<Pinged>(with);
        without.Tick();

        Assert.That(ping, Is.Not.Null);
        Assert.That((ping!.PlayerId, ping.X, ping.Y, ping.Tick), Is.EqualTo((A, 12500, 7500, 0)));
        Assert.That(with.StateHash, Is.EqualTo(without.StateHash));
    }

    [Test]
    public void GWT2_Give_MovesTheResourceFromWalletToWallet()
    {
        Simulation sim = Match();
        sim.Order(new GiveResourcesCommand(A, 0, B, Resources.Primary, 50));
        ResourcesGiven? given = Only<ResourcesGiven>(sim);

        Assert.That(Purse(sim, A).Primary, Is.EqualTo(50));
        Assert.That(Purse(sim, B).Primary, Is.EqualTo(70));
        Assert.That(given, Is.Not.Null);
        Assert.That((given!.FromPlayerId, given.ToPlayerId, given.ResourceType, given.Amount), Is.EqualTo((A, B, Resources.Primary, 50)));
    }

    [Test]
    public void GWT3_NotEnough_Rejected_WalletsUntouched()
    {
        Simulation sim = Match();
        sim.Order(new GiveResourcesCommand(B, 0, A, Resources.Primary, 50));
        CommandRejected? rejected = Only<CommandRejected>(sim);

        Assert.That((rejected!.PlayerId, rejected.Reason), Is.EqualTo((B, RejectReason.NotEnoughResources)));
        Assert.That((Purse(sim, A).Primary, Purse(sim, B).Primary), Is.EqualTo((100, 20)));
    }

    [Test]
    public void GWT4_ToTheLegion_Rejected()
    {
        Simulation sim = Match();
        int legionBefore = Purse(sim, Legion).Primary;
        sim.Order(new GiveResourcesCommand(A, 0, Legion, Resources.Primary, 10));
        var events = sim.RunCollecting<SimEvent>(1);

        Assert.That(events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
        Assert.That(events.OfType<ResourcesGiven>(), Is.Empty);
        Assert.That((Purse(sim, A).Primary, Purse(sim, Legion).Primary), Is.EqualTo((100, legionBefore)));
    }

    [Test]
    public void GWT5_SecondResource_LeavesThePrimaryAlone()
    {
        Simulation sim = Match();
        sim.Order(new GiveResourcesCommand(A, 0, B, Resources.Secondary, 40));
        sim.Tick();

        Assert.That(Purse(sim, A), Is.EqualTo((100, 0)));
        Assert.That(Purse(sim, B), Is.EqualTo((20, 40)));
    }

    [Test]
    public void GWT6_Gift_IsNotIncome()
    {
        Simulation sim = Match();
        sim.State.GetPlayer(B).Wallet.Credit(Resources.Primary, 10);
        sim.Order(new GiveResourcesCommand(A, 0, B, Resources.Primary, 50));
        sim.Tick();

        Assert.That(sim.State.GetPlayer(B).Wallet.Earned(Resources.Primary), Is.EqualTo(10));
        Assert.That(Purse(sim, B).Primary, Is.EqualTo(80));
    }

    [Test]
    public void GWT7_BothCommands_RoundTripThroughTheCodec()
    {
        CommandCodec codec = GameCommands.CreateCodec();
        var log = codec.ReadLog(codec.WriteLog(new ICommand[]
        {
            new PingCommand(A, 3, 12500, 7500),
            new GiveResourcesCommand(B, 4, A, Resources.Secondary, 25),
        }));

        var ping = (PingCommand)log[0];
        Assert.That((ping.PlayerId, ping.Tick, ping.X, ping.Y), Is.EqualTo((A, 3, 12500, 7500)));
        var give = (GiveResourcesCommand)log[1];
        Assert.That((give.PlayerId, give.Tick, give.ToPlayerId, give.ResourceType, give.Amount), Is.EqualTo((B, 4, A, Resources.Secondary, 25)));
    }

    [TestCase(A, Resources.Primary, 10, TestName = "Edge_GiveToOneself_Rejected")]
    [TestCase(B, Resources.Primary, 0, TestName = "Edge_ZeroAmount_Rejected")]
    [TestCase(B, Resources.Primary, -5, TestName = "Edge_NegativeAmount_Rejected")]
    [TestCase(B, 7, 10, TestName = "Edge_UnknownResource_Rejected")]
    [TestCase(77, Resources.Primary, 10, TestName = "Edge_UnknownPlayer_Rejected")]
    public void Edge_BadGift_Rejected(int to, int resourceType, int amount)
    {
        Simulation sim = Match();
        sim.Order(new GiveResourcesCommand(A, 0, to, resourceType, amount));
        var events = sim.RunCollecting<SimEvent>(1);

        Assert.That(events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
        Assert.That(events.OfType<ResourcesGiven>(), Is.Empty);
        Assert.That((Purse(sim, A), Purse(sim, B)), Is.EqualTo(((100, 40), (20, 0))));
    }

    [Test]
    public void Edge_PingByTheLegion_OrOutsideTheMap_Rejected()
    {
        Simulation sim = Match();
        sim.Order(new PingCommand(Legion, 0, 5000, 5000));
        sim.Order(new PingCommand(A, 0, -1, 5000));
        sim.Order(new PingCommand(A, 0, 5000, Cells(24)));
        var events = sim.RunCollecting<SimEvent>(1);

        Assert.That(events.OfType<Pinged>(), Is.Empty);
        Assert.That(events.OfType<CommandRejected>().Select(r => (r.PlayerId, r.Reason)),
            Is.EqualTo(new[] { (A, RejectReason.NotAllowed), (A, RejectReason.NotAllowed), (Legion, RejectReason.NotAllowed) }));
    }

    [Test]
    public void Edge_BothGiveInOneTick_ByPlayerOrder_EachAgainstTheWalletOfItsMoment()
    {
        Simulation sim = Match();
        // B has 20: its gift of 60 passes only because A's 50 arrives first (A before B).
        sim.Order(new GiveResourcesCommand(B, 0, A, Resources.Primary, 60));
        sim.Order(new GiveResourcesCommand(A, 0, B, Resources.Primary, 50));
        var given = sim.RunCollecting<ResourcesGiven>(1);

        Assert.That(given.Select(g => g.FromPlayerId), Is.EqualTo(new[] { A, B }));
        Assert.That((Purse(sim, A).Primary, Purse(sim, B).Primary), Is.EqualTo((110, 10)));
    }

    [Test]
    public void Edge_AllyWithNothingLeft_StillReceives()
    {
        Simulation sim = Match();
        foreach (Entity e in sim.State.Buildings.Where(b => b.OwnerId == B).Cast<Entity>().Concat(sim.State.UnitsOf(B)).ToList())
            Kill(e);
        sim.Tick();
        sim.Order(new GiveResourcesCommand(A, sim.State.Tick, B, Resources.Primary, 100));
        sim.Tick();

        Assert.That(Purse(sim, B).Primary, Is.EqualTo(120));
    }
}
