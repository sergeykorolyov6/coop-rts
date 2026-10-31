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
        Assert.That(state.Grid.Width, Is.EqualTo(64));
        Assert.That(state.Grid.Height, Is.EqualTo(64));
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
}
