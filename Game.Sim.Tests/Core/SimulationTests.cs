using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class SimulationTests
{
    [Test]
    public void Tick_AdvancesTickCounter()
    {
        var sim = new Simulation(Setups.Solo());
        for (int i = 0; i < 5; i++)
            sim.Tick();

        Assert.That(sim.State.Tick, Is.EqualTo(5));
    }

    [Test]
    public void Command_ExecutesOnItsTick_NotBefore()
    {
        var sim = new Simulation(Setups.Solo());
        sim.Enqueue(new GrantCommand(1, 3, Setups.Primary, 100));

        for (int i = 0; i < 3; i++)
            sim.Tick();
        Assert.That(sim.State.GetPlayer(1).Wallet.Get(Setups.Primary), Is.Zero);

        sim.Tick();
        Assert.That(sim.State.GetPlayer(1).Wallet.Get(Setups.Primary), Is.EqualTo(100));
    }

    [Test]
    public void Command_ForPastTick_Throws()
    {
        var sim = new Simulation(Setups.Solo());
        sim.Tick();

        Assert.Throws<ArgumentException>(() => sim.Enqueue(new GrantCommand(1, 0, Setups.Primary, 1)));
    }

    [Test]
    public void Command_FromUnknownPlayer_Throws()
    {
        var sim = new Simulation(Setups.Solo());

        Assert.Throws<ArgumentException>(() => sim.Enqueue(new GrantCommand(5, 0, Setups.Primary, 1)));
    }

    [Test]
    public void SameTick_ExecutesByPlayerId_ThenByIssueOrder()
    {
        var sim = new Simulation(Setups.TwoAlliesAndEnemy());
        sim.Enqueue(new MarkCommand(2, 0, "b1"));
        sim.Enqueue(new MarkCommand(9, 0, "e1"));
        sim.Enqueue(new MarkCommand(1, 0, "a1"));
        sim.Enqueue(new MarkCommand(2, 0, "b2"));
        sim.Enqueue(new MarkCommand(1, 0, "a2"));

        sim.Tick();

        var labels = sim.Events.Cast<MarkEvent>().Select(e => e.Label);
        Assert.That(labels, Is.EqualTo(new[] { "a1", "a2", "b1", "b2", "e1" }));
    }

    [Test]
    public void Events_AreOnlyThoseOfTheLastTick()
    {
        var sim = new Simulation(Setups.Solo());
        sim.Enqueue(new MarkCommand(1, 0, "first"));
        sim.Enqueue(new MarkCommand(1, 2, "third"));

        sim.Tick();
        Assert.That(sim.Events.Cast<MarkEvent>().Select(e => e.ToString()), Is.EqualTo(new[] { "0:1:first" }));

        sim.Tick();
        Assert.That(sim.Events, Is.Empty);

        sim.Tick();
        Assert.That(sim.Events.Cast<MarkEvent>().Select(e => e.ToString()), Is.EqualTo(new[] { "2:1:third" }));
    }
}
