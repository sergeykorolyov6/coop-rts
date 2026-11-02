using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

// construction.md, "Стартовые условия прототипа" (answer 7): per player a headquarters,
// 3 workers and 100 of the primary resource — taken from rules.csv, not hard-coded.
public class StartConditionsTests
{
    private static MapSetup TwoStarts() =>
        new MapSetup(32, 32).Start(Setups.PlayerA, 2, 2).Start(Setups.PlayerB, 20, 20);

    [Test]
    public void EachPlayer_GetsHeadquartersWorkersAndResources()
    {
        var state = new SimState(Setups.Coop(TwoStarts()));
        GameRules rules = state.Rules;

        foreach (int player in new[] { Setups.PlayerA, Setups.PlayerB })
        {
            Building? hq = state.FindHeadquarters(player);
            Assert.That(hq, Is.Not.Null);
            Assert.That(hq!.IsComplete, Is.True);
            Assert.That(hq.Hp, Is.EqualTo(hq.MaxHp));

            var workers = state.Units.Where(u => u.OwnerId == player).ToList();
            Assert.That(workers, Has.Count.EqualTo(rules.StartWorkers));
            Assert.That(workers.All(u => u.Worker != null), Is.True);

            Assert.That(state.GetPlayer(player).Wallet.Get(Resources.Primary), Is.EqualTo(rules.StartPrimary));
            Assert.That(state.GetPlayer(player).Wallet.Get(Resources.Secondary), Is.EqualTo(rules.StartSecondary));
        }

        // Players without a start (enemy AI, creeps) get nothing.
        Assert.That(state.FindHeadquarters(Setups.EnemyAi), Is.Null);
        Assert.That(state.GetPlayer(Setups.EnemyAi).Wallet.Get(Resources.Primary), Is.EqualTo(0));
        Assert.That(rules.StartWorkers, Is.EqualTo(3));
        Assert.That(rules.StartPrimary, Is.EqualTo(100));
    }

    [Test]
    public void Headquarters_OccupiesItsFootprint_AndBlocksWalking()
    {
        var state = new SimState(Setups.Coop(TwoStarts()));
        Building hq = state.FindHeadquarters(Setups.PlayerA)!;

        Assert.That(hq.Width, Is.EqualTo(4));
        for (int y = 2; y < 6; y++)
        {
            for (int x = 2; x < 6; x++)
            {
                Assert.That(state.Map.BuildingAt(x, y), Is.EqualTo(hq.Id));
                Assert.That(state.Map.IsPassable(x, y), Is.False);
            }
        }
        Assert.That(state.Map.BuildingAt(6, 2), Is.EqualTo(0));
    }

    [Test]
    public void Workers_StandOnFreeCellsNextToHeadquarters()
    {
        var state = new SimState(Setups.Coop(TwoStarts()));
        var workers = state.Units.Where(u => u.OwnerId == Setups.PlayerA).ToList();

        // Bottom row of the ring around the 4x4 footprint at (2, 2), left to right.
        Assert.That(workers.Select(u => (u.CellX, u.CellY)), Is.EqualTo(new[] { (1, 1), (2, 1), (3, 1) }));
        Assert.That(workers[0].X, Is.EqualTo(1500));
        Assert.That(workers[0].Y, Is.EqualTo(1500));
    }

    [Test]
    public void EntityIds_AreUniqueAndAscending()
    {
        var state = new SimState(Setups.Coop(TwoStarts()));
        var ids = state.Buildings.Select(b => b.Id).Concat(state.Units.Select(u => u.Id)).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
        Assert.That(state.Units.Select(u => u.Id), Is.Ordered);
        Assert.That(state.NextEntityId, Is.GreaterThan(ids.Max()));
    }

    [Test]
    public void SameSetup_SameInitialHash_DifferentStart_DifferentHash()
    {
        var a = new Simulation(Setups.Coop(TwoStarts()));
        var b = new Simulation(Setups.Coop(TwoStarts()));
        var c = new Simulation(Setups.Coop(new MapSetup(32, 32).Start(Setups.PlayerA, 3, 2).Start(Setups.PlayerB, 20, 20)));
        Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
        Assert.That(c.StateHash, Is.Not.EqualTo(a.StateHash));
    }

    [Test]
    public void StartForUnknownPlayer_IsRejected()
    {
        var map = new MapSetup(16, 16).Start(7, 2, 2);
        Assert.Throws<ArgumentException>(() => Setups.Coop(map));
    }

    [Test]
    public void CampWithWrongNumberOfCreepCells_IsRejected()
    {
        var map = new MapSetup(16, 16).CreepCamp("guard_camp", 8, 8, (8, 8));
        Assert.Throws<ArgumentException>(() => Setups.Coop(map));
    }
}
