using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class SimStateTests
{
    [Test]
    public void NewState_FromSetup()
    {
        var state = new SimState(Setups.TwoAlliesAndEnemy());

        Assert.That(state.Tick, Is.Zero);
        Assert.That(state.Map.Width, Is.EqualTo(64));
        Assert.That(state.Map.Height, Is.EqualTo(64));
        Assert.That(state.ResourceTypeCount, Is.EqualTo(2));
        Assert.That(state.Players, Has.Count.EqualTo(3));
        Assert.That(state.Players[0].Id, Is.EqualTo(1));
        Assert.That(state.Players[2].Id, Is.EqualTo(9));
        Assert.That(state.Players[1].Wallet.ResourceTypeCount, Is.EqualTo(2));
    }

    [Test]
    public void GetPlayer_Unknown_Throws()
    {
        var state = new SimState(Setups.Solo());
        Assert.That(state.HasPlayer(1), Is.True);
        Assert.That(state.HasPlayer(2), Is.False);
        Assert.Throws<ArgumentException>(() => state.GetPlayer(2));
    }
    [Test]
    public void BuildingAt_FindsTheBuildingByAnyCellOfItsFootprint_AndNothingOffTheMap()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(32, 32).Start(Setups.PlayerA, 10, 10)));
        SimState state = sim.State;
        Building hq = state.FindHeadquarters(Setups.PlayerA)!;

        Assert.That(state.BuildingAt(hq.CellX, hq.CellY), Is.SameAs(hq));
        Assert.That(state.BuildingAt(hq.CellX + hq.Width - 1, hq.CellY + hq.Height - 1), Is.SameAs(hq));
        Assert.That(state.BuildingAt(hq.CellX + hq.Width, hq.CellY), Is.Null);
        Assert.That(state.BuildingAt(-1, 0), Is.Null);
        Assert.That(state.BuildingAt(0, 32), Is.Null);
    }
}
