using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Answer 3: fog of war is shared by the whole team, as in SC2 Co-op.
// drill.md: a deposit goes Hidden → Discovered.
public class FogOfWarTests
{
    [Test]
    public void AtStart_AreaAroundHeadquartersIsDiscovered_FarAwayIsNot()
    {
        var state = new SimState(Setups.Coop(new MapSetup(64, 64).Start(Setups.PlayerA, 2, 2)));
        Assert.That(state.Fog.IsDiscovered(Setups.PlayerA, 4, 4), Is.True);
        Assert.That(state.Fog.IsVisible(Setups.PlayerA, 10, 4), Is.True);  // HQ vision 10 cells
        Assert.That(state.Fog.IsDiscovered(Setups.PlayerA, 40, 40), Is.False);
    }

    [Test]
    public void AllySeesWhatMyUnitSees_EnemyDoesNot()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(64, 64)));
        sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("scout"), Center(40), Center(40));
        sim.Tick();

        Assert.That(sim.State.Fog.IsVisible(Setups.PlayerA, 45, 40), Is.True);
        Assert.That(sim.State.Fog.IsVisible(Setups.PlayerB, 45, 40), Is.True);
        Assert.That(sim.State.Fog.IsDiscovered(Setups.PlayerB, 45, 40), Is.True);
        Assert.That(sim.State.Fog.IsVisible(Setups.EnemyAi, 45, 40), Is.False);
    }

    [Test]
    public void CellStaysDiscoveredAfterTheUnitLeaves_ButIsNoLongerVisible()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(64, 16)));
        Unit scout = sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("scout"), Center(5), Center(8));
        sim.Tick();
        Assert.That(sim.State.Fog.IsVisible(Setups.PlayerA, 5, 8), Is.True);

        sim.Order(sim.Move(Setups.PlayerA, Center(60), Center(8), scout));
        sim.RunUntil(s => scout.Order.Kind == UnitOrderKind.Idle);

        Assert.That(sim.State.Fog.IsVisible(Setups.PlayerA, 5, 8), Is.False);
        Assert.That(sim.State.Fog.IsDiscovered(Setups.PlayerA, 5, 8), Is.True);
    }

    [Test]
    public void Deposit_GoesFromHiddenToDiscovered()
    {
        var map = new MapSetup(64, 16).OreRect(50, 6, 4, 4, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Deposit deposit = sim.State.Map.Deposits[0];
        Unit scout = sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("scout"), Center(5), Center(8));
        sim.Tick();
        Assert.That(sim.State.Fog.IsDepositDiscovered(Setups.PlayerA, deposit), Is.False);

        sim.Order(sim.Move(Setups.PlayerA, Center(40), Center(8), scout));
        sim.RunUntil(s => s.State.Fog.IsDepositDiscovered(Setups.PlayerA, deposit));
        Assert.That(sim.State.Fog.IsDepositDiscovered(Setups.PlayerB, deposit), Is.True);
    }

    [Test]
    public void Discovery_ChangesTheStateHash()
    {
        // Two runs that differ only in what was scouted must not hash the same.
        var a = new Simulation(Setups.Coop(new MapSetup(32, 8)));
        var b = new Simulation(Setups.Coop(new MapSetup(32, 8)));
        a.State.SpawnUnit(Setups.PlayerA, a.State.Data.Unit("builder"), Center(2), Center(4));
        b.State.SpawnUnit(Setups.PlayerA, b.State.Data.Unit("builder"), Center(2), Center(4));
        a.Order(a.Move(Setups.PlayerA, Center(30), Center(4), a.State.Units[0]));
        b.Order(b.Move(Setups.PlayerA, Center(30), Center(4), b.State.Units[0]));
        a.RunUntil(s => s.State.Units[0].Order.Kind == UnitOrderKind.Idle);
        b.RunUntil(s => s.State.Units[0].Order.Kind == UnitOrderKind.Idle);
        Assert.That(b.StateHash, Is.EqualTo(a.StateHash));

        // Back at the same spot, but having seen the far end: discovered differs.
        var c = new Simulation(Setups.Coop(new MapSetup(32, 8)));
        c.State.SpawnUnit(Setups.PlayerA, c.State.Data.Unit("builder"), Center(30), Center(4));
        c.Tick();
        Assert.That(c.State.Fog.IsDiscovered(Setups.PlayerA, 2, 4), Is.False);
        Assert.That(a.State.Fog.IsDiscovered(Setups.PlayerA, 2, 4), Is.True);
    }
}
