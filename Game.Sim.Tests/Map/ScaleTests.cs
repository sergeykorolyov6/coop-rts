using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// plan-terrain-and-factions.md, stage T1: the shortcuts for big maps must answer exactly as the
// plain versions did — a path field searched only as far as asked, buildings found by bucket.
public class ScaleTests
{
    private static MapSetup WalledMap()
    {
        var map = new MapSetup(48, 48);
        for (int y = 0; y < 40; y++)
            map.Block(20, y);
        return map;
    }

    [Test]
    public void FlowField_AnswersTheSame_WhateverWasAskedFirst()
    {
        var a = new Simulation(Setups.Coop(WalledMap()));
        var b = new Simulation(Setups.Coop(WalledMap()));
        FlowField nearFirst = a.State.Paths.ToCell(a.State.Map, 5, 5);
        FlowField farFirst = b.State.Paths.ToCell(b.State.Map, 5, 5);

        Assert.That(nearFirst.DistanceAt(6, 5), Is.EqualTo(FlowField.StraightCost));
        // Behind the wall: around its top end at y = 40.
        int far = farFirst.DistanceAt(30, 5);
        Assert.That(far, Is.GreaterThan(70 * FlowField.StraightCost));

        for (int y = 0; y < 48; y++)
        {
            for (int x = 0; x < 48; x++)
                Assert.That(nearFirst.DistanceAt(x, y), Is.EqualTo(farFirst.DistanceAt(x, y)), $"cell ({x}, {y})");
        }
        Assert.That(nearFirst.DistanceAt(20, 10), Is.EqualTo(FlowField.Unreachable), "inside the wall");
    }

    [Test]
    public void FlowField_BeltDoesNotDropCachedPaths_SolidBuildingDoes()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(32, 32)));
        SimState s = sim.State;
        FlowField field = s.Paths.ToCell(s.Map, 5, 5);

        s.PlaceBuilding(Setups.PlayerA, s.Data.Building("conveyor"), 10, 10, Direction.Right, complete: true);
        Assert.That(s.Paths.ToCell(s.Map, 5, 5), Is.SameAs(field), "a belt is walkable: paths stay");

        s.PlaceBuilding(Setups.PlayerA, s.Data.Building("turret"), 12, 12, Direction.Right, complete: true);
        FlowField rebuilt = s.Paths.ToCell(s.Map, 5, 5);
        Assert.That(rebuilt, Is.Not.SameAs(field));
        Assert.That(rebuilt.DistanceAt(12, 12), Is.EqualTo(FlowField.Unreachable), "the turret's cell is closed now");
    }

    [Test]
    public void NearestEnemy_FindsBuildingsAcrossBuckets_NearestFirst_ThenLowerId()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(64, 64)));
        SimState s = sim.State;
        // Sight 6: the unit stands on a bucket corner (cell 16 = 2 × 8), targets lie in four buckets.
        Unit shooter = Spawn(sim, Setups.PlayerA, "shooter", 16, 16);
        long sight = shooter.Stats.VisionMilli;
        Assert.That(Combat.NearestEnemy(s, shooter, shooter.X, shooter.Y, sight), Is.Null);

        // Same distance on both sides: the lower id wins, though the other one's bucket is scanned first.
        Building right = s.PlaceBuilding(Setups.EnemyAi, s.Data.Building("conveyor"), 20, 16, Direction.Right, complete: true);
        Building left = s.PlaceBuilding(Setups.EnemyAi, s.Data.Building("conveyor"), 12, 16, Direction.Right, complete: true);
        Assert.That(right.Id, Is.LessThan(left.Id));
        Assert.That(Combat.NearestEnemy(s, shooter, shooter.X, shooter.Y, sight), Is.SameAs(right));

        // A nearer one in another bucket; then one out of sight changes nothing.
        Building near = s.PlaceBuilding(Setups.EnemyAi, s.Data.Building("conveyor"), 16, 14, Direction.Right, complete: true);
        s.PlaceBuilding(Setups.EnemyAi, s.Data.Building("conveyor"), 40, 40, Direction.Right, complete: true);
        Assert.That(Combat.NearestEnemy(s, shooter, shooter.X, shooter.Y, sight), Is.SameAs(near));

        // A 4×4 building reaching into sight from a bucket of its own corner.
        Kill(near);
        Kill(right);
        Kill(left);
        sim.Tick();
        Assert.That(s.Buildings.Count, Is.EqualTo(1), "the index must forget the removed ones");
        Building hq = s.PlaceBuilding(Setups.EnemyAi, s.Data.Building("legion_hq"), 21, 14, Direction.Right, complete: true);
        Assert.That(Combat.NearestEnemy(s, shooter, shooter.X, shooter.Y, sight), Is.SameAs(hq));
    }

    // The scale stand (Tools/make_bench_map.py): generated, so the test is what guards its sense.
    [Test]
    public void Bench256_Loads_FourBases_EveryStartReachesTheLegion()
    {
        Simulation sim = RepoData.Launch("bench256");
        SimState s = sim.State;
        Assert.That((s.Map.Width, s.Map.Height), Is.EqualTo((256, 256)));
        Assert.That(s.TeamPlayerIds, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(s.Legion, Is.Not.Null);
        Building citadel = s.FindHeadquarters(MissionLauncher.LegionPlayerId)!;
        FlowField toLegion = s.Paths.ToRectangle(s.Map, citadel.CellX, citadel.CellY, citadel.Width, citadel.Height);
        foreach (int p in s.TeamPlayerIds)
        {
            Unit builder = s.FindBuilder(p)!;
            Assert.That(toLegion.DistanceAt(builder.CellX, builder.CellY), Is.Not.EqualTo(FlowField.Unreachable), $"player {p}");
        }
        sim.Run(5 * Simulation.TicksPerSecond);
    }
}
