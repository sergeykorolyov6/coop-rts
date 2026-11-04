using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/mission.md
public class MissionTests
{
    // A at (2,2), B at (2,20); the Legion: HQ (38,24), two factories, a rally point.
    private static Simulation Match()
    {
        var map = new MapSetup(48, 36)
            .Start(Setups.PlayerA, 2, 2)
            .Start(Setups.PlayerB, 2, 20)
            .LegionSlot("headquarters", 38, 24)
            .LegionSlot("legion_factory", 32, 26)
            .LegionSlot("legion_factory", 32, 30)
            .Rally(28, 22);
        return new Simulation(Setups.WithLegion(map));
    }

    private static List<Building> Legion(Simulation sim) =>
        sim.State.Buildings.Where(b => b.OwnerId == Setups.EnemyAi).ToList();

    private static void Wipe(Simulation sim, int player, bool keepWorker = false)
    {
        foreach (Building b in sim.State.Buildings.Where(b => b.OwnerId == player))
            Kill(b);
        foreach (Unit u in sim.State.UnitsOf(player).Skip(keepWorker ? 1 : 0))
            Kill(u);
    }

    [Test]
    public void GWT1_BothFactoriesDestroyed_HeadquartersStands_MissionGoesOn()
    {
        var sim = Match();
        foreach (Building f in Legion(sim).Where(b => b.Factory != null))
            Kill(f);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test]
    public void GWT2_LastHeadquartersDestroyed_Victory_Once()
    {
        var sim = Match();
        foreach (Building f in Legion(sim).Where(b => b.Factory != null))
            Kill(f);
        sim.Tick();
        Kill(Legion(sim).Single(b => b.Stats.Id == "headquarters"));
        var ended = sim.RunCollecting<MissionEnded>(40);
        Assert.That(ended, Has.Count.EqualTo(1));
        Assert.That(ended[0].Outcome, Is.EqualTo(MissionOutcome.Victory));
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.Victory));
    }

    [Test]
    public void GWT3_OnePlayerWipedOut_TheAllyStands_MissionGoesOn()
    {
        var sim = Match();
        Wipe(sim, Setups.PlayerA);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test]
    public void GWT4_NoHeadquarters_LastBuilderKilled_Defeat()
    {
        var sim = Match();
        Wipe(sim, Setups.PlayerA);
        Wipe(sim, Setups.PlayerB, keepWorker: true);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));

        Kill(sim.State.UnitsOf(Setups.PlayerB).Single());
        var ended = sim.RunCollecting<MissionEnded>(2);
        Assert.That(ended.Single().Outcome, Is.EqualTo(MissionOutcome.Defeat));
    }

    [Test]
    public void GWT5_HeadquartersLost_TheBuilderRebuildsIt_ASiteWithoutABuilderDoesNotCount()
    {
        // mission.md M2+: a finished headquarters or a living builder keeps the team in the game.
        var sim = Match();
        Wipe(sim, Setups.PlayerB);
        Kill(sim.State.FindHeadquarters(Setups.PlayerA)!);
        sim.Tick();
        Unit builder = sim.State.UnitsOf(Setups.PlayerA).First(u => u.IsBuilder);
        sim.Order(new BuildCommand(Setups.PlayerA, sim.State.Tick, new[] { builder.Id }, "headquarters", new[] { new BuildSite(2, 8, Direction.Right) }));
        sim.RunUntil(s => s.State.FindHeadquarters(Setups.PlayerA) != null, 60 * Simulation.TicksPerSecond);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
        foreach (Unit u in sim.State.UnitsOf(Setups.PlayerA))
            Kill(u);
        sim.Run(2);
        Assert.That(sim.State.FindHeadquarters(Setups.PlayerA)!.IsComplete, Is.False);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.Defeat));
    }

    [Test]
    public void GWT5b_FinishedHeadquarters_BuilderDead_MissionGoesOn()
    {
        var sim = Match();
        Wipe(sim, Setups.PlayerB);
        foreach (Unit u in sim.State.UnitsOf(Setups.PlayerA))
            Kill(u);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test]
    public void Edge_LastLegionHeadquartersAndLastWorkerInOneTick_Victory()
    {
        var sim = Match();
        foreach (Building f in Legion(sim).Where(b => b.Factory != null))
            Kill(f);
        Wipe(sim, Setups.PlayerA);
        Wipe(sim, Setups.PlayerB, keepWorker: true);
        sim.Tick();
        Kill(Legion(sim).Single(b => b.Stats.Id == "headquarters"));
        Kill(sim.State.UnitsOf(Setups.PlayerB).Single());
        sim.Tick();
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.Victory));
    }

    [Test]
    public void Edge_FactorySiteOfTheLegionCounts_NoVictoryWhileItStands()
    {
        var sim = Match();
        Building hq = Legion(sim).Single(b => b.Stats.Id == "headquarters");
        foreach (Building f in Legion(sim).Where(b => b.Factory != null))
            Kill(f);
        sim.Tick();
        sim.State.PlaceBuilding(Setups.EnemyAi, sim.State.Data.Building("legion_factory"), 32, 26, Direction.Right, complete: false);
        Kill(hq);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test]
    public void Edge_AfterTheEnd_TheOutcomeStays()
    {
        var sim = Match();
        foreach (Building b in Legion(sim))
            Kill(b);
        sim.Tick();
        Wipe(sim, Setups.PlayerA);
        Wipe(sim, Setups.PlayerB);
        var ended = sim.RunCollecting<MissionEnded>(40);
        Assert.That(ended, Is.Empty);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.Victory));
    }

    [Test]
    public void Edge_MapWithoutTheLegion_NoOutcome()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(20, 20).Start(Setups.PlayerA, 2, 2)));
        Wipe(sim, Setups.PlayerA);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }
}
