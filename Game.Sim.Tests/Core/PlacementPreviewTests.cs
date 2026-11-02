using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

// What the build menu shows before the click must be what BuildCommand would answer
// (drill.md UI: red outline if the place is unavailable; conveyors.md: bridge range).
public class PlacementPreviewTests
{
    private static Simulation Match() => ConstructionTests.Match();

    private static RejectReason? ByCommand(Simulation sim, string type, int x, int y)
    {
        sim.Order(ConstructionTests.Build(sim, Setups.PlayerA, type, x, y, ConstructionTests.Worker(sim, Setups.PlayerA)));
        sim.Tick();
        return sim.Events.OfType<CommandRejected>().Select(e => (RejectReason?)e.Reason).SingleOrDefault();
    }

    [TestCase("factory", 8, 4)]
    [TestCase("headquarters", 8, 4)]
    [TestCase("factory", 2, 2)]
    [TestCase("factory", 31, 4)]
    [TestCase("no_such_building", 8, 4)]
    public void Check_SameAnswerAsBuildCommand(string type, int x, int y)
    {
        var sim = Match();
        RejectReason? preview = PlacementPreview.Check(sim.State, Setups.PlayerA, type, x, y, Direction.Right);
        Assert.That(preview, Is.EqualTo(ByCommand(sim, type, x, y)));
    }

    [Test]
    public void Check_FreePlace_Null()
    {
        var sim = Match();
        Assert.That(PlacementPreview.Check(sim.State, Setups.PlayerA, "factory", 8, 4, Direction.Right), Is.Null);
    }

    [Test]
    public void Check_NotEnoughResources_SameAsCommand()
    {
        var sim = Match();
        Wallet wallet = sim.State.GetPlayer(Setups.PlayerA).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary));

        Assert.That(PlacementPreview.Check(sim.State, Setups.PlayerA, "factory", 8, 4, Direction.Right), Is.EqualTo(RejectReason.NotEnoughResources));
        Assert.That(ByCommand(sim, "factory", 8, 4), Is.EqualTo(RejectReason.NotEnoughResources));
    }

    [Test]
    public void BridgeRange_StraightWithinRange_Only()
    {
        var sim = Match();
        int range = sim.State.Rules.BridgeRange;

        Assert.That(PlacementPreview.InBridgeRange(sim.State, 5, 8, 5 + range, 8), Is.True);
        Assert.That(PlacementPreview.InBridgeRange(sim.State, 5, 8, 5, 8 - range), Is.True);
        Assert.That(PlacementPreview.InBridgeRange(sim.State, 5, 8, 6 + range, 8), Is.False);
        Assert.That(PlacementPreview.InBridgeRange(sim.State, 5, 8, 7, 10), Is.False);
        Assert.That(PlacementPreview.InBridgeRange(sim.State, 5, 8, 5, 8), Is.False);
    }
}
