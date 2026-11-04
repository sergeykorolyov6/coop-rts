using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/alerts.md
public class AlertTests
{
    private static Simulation Empty() => new Simulation(Setups.Coop(new MapSetup(32, 16)));

    [Test]
    public void GWT1_UnitHitEverySecond_AlarmOncePerFiveSeconds()
    {
        var sim = Empty();
        Unit target = Spawn(sim, Setups.PlayerA, "repairer", 5, 5);
        Unit attacker = Spawn(sim, Setups.EnemyAi, "shooter", 8, 5);
        sim.Order(new AttackCommand(Setups.EnemyAi, sim.State.Tick, new[] { attacker.Id }, target.Id));
        // Only alarms for the target's owner count here.
        var alarms = sim.RunCollecting<UnitAttacked>(6 * Simulation.TicksPerSecond).Where(e => e.OwnerId == Setups.PlayerA).ToList();
        Assert.That(alarms, Has.Count.EqualTo(2));
        Assert.That(alarms.All(a => a.OwnerId == Setups.PlayerA && a.UnitId == target.Id && a.AttackerId == attacker.Id), Is.True);
        Assert.That(alarms[1].Tick - alarms[0].Tick, Is.EqualTo(5 * Simulation.TicksPerSecond));
    }

    [Test]
    public void GWT2_TwoUnitsHit_AnAlarmForEach()
    {
        var sim = Empty();
        Unit a = Spawn(sim, Setups.PlayerA, "repairer", 5, 5);
        Unit b = Spawn(sim, Setups.PlayerA, "repairer", 5, 9);
        Unit x = Spawn(sim, Setups.EnemyAi, "shooter", 8, 5);
        Unit y = Spawn(sim, Setups.EnemyAi, "shooter", 8, 9);
        sim.Order(new AttackCommand(Setups.EnemyAi, sim.State.Tick, new[] { x.Id }, a.Id));
        sim.Order(new AttackCommand(Setups.EnemyAi, sim.State.Tick, new[] { y.Id }, b.Id));
        var alarms = sim.RunCollecting<UnitAttacked>(Simulation.TicksPerSecond).Where(e => e.OwnerId == Setups.PlayerA).ToList();
        Assert.That(alarms.Select(e => e.UnitId).OrderBy(id => id), Is.EqualTo(new[] { a.Id, b.Id }));
    }
}
