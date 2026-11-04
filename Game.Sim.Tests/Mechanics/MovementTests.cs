using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// PLAN.md M1: movement by click, flow field pathfinding, simple avoidance. No spec of its own:
// these tests pin the behaviour the mechanics rely on.
public class MovementTests
{
    private static (Simulation Sim, Unit Unit) OneWorker(MapSetup map, int cellX, int cellY)
    {
        var sim = new Simulation(Setups.Coop(map));
        Unit unit = sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("legion_worker"), Center(cellX), Center(cellY));
        return (sim, unit);
    }

    [Test]
    public void StraightLine_TakesDistanceOverSpeed()
    {
        var (sim, worker) = OneWorker(new MapSetup(16, 16), 2, 5);
        sim.Order(sim.Move(Setups.PlayerA, Center(8), Center(5), worker));

        // 6 cells at 2.4 cells/s (120 milli-cells per tick) = 50 ticks.
        int ticks = sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Idle);
        Assert.That(ticks, Is.EqualTo(50));
        Assert.That((worker.X, worker.Y), Is.EqualTo((Center(8), Center(5))));
    }

    [Test]
    public void WalksAroundAWall_NeverStandingInIt()
    {
        var map = new MapSetup(16, 16);
        for (int y = 0; y < 12; y++)
            map.Block(8, y);
        var (sim, worker) = OneWorker(map, 4, 4);
        sim.Order(sim.Move(Setups.PlayerA, Center(12), Center(4), worker));

        int ticks = sim.RunUntil(s =>
        {
            Assert.That(s.State.Map.IsPassable(worker.CellX, worker.CellY), Is.True, $"in a wall at {worker.CellX},{worker.CellY}");
            return worker.Order.Kind == UnitOrderKind.Idle;
        });
        Assert.That((worker.CellX, worker.CellY), Is.EqualTo((12, 4)));
        // Around the end of the wall at y = 12 and back: much longer than the 8 cells straight.
        Assert.That(ticks, Is.GreaterThan(8 * 1000 / 120 * 2));
    }

    [Test]
    public void DoesNotCutABlockedCorner()
    {
        // Diagonal (4,4) → (5,5) squeezes between two blocked cells.
        var map = new MapSetup(10, 10).Block(5, 4).Block(4, 5);
        var (sim, worker) = OneWorker(map, 4, 4);
        sim.Order(sim.Move(Setups.PlayerA, Center(5), Center(5), worker));

        var visited = new List<(int, int)>();
        sim.RunUntil(s =>
        {
            visited.Add((worker.CellX, worker.CellY));
            return worker.Order.Kind == UnitOrderKind.Idle;
        });
        Assert.That(visited.Distinct(), Has.Some.Matches<(int X, int Y)>(c => c.X == 3 || c.Y == 3),
            "must go around the blocked pair, not through it");
    }

    [Test]
    public void UnreachableTarget_StopsNextToIt()
    {
        // Target cell (8,8) is walled in on all sides.
        var map = new MapSetup(16, 16);
        for (int x = 7; x <= 9; x++)
            for (int y = 7; y <= 9; y++)
                if (x != 8 || y != 8)
                    map.Block(x, y);
        var (sim, worker) = OneWorker(map, 2, 8);
        sim.Order(sim.Move(Setups.PlayerA, Center(8), Center(8), worker));

        sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Idle);
        // The nearest passable ring around the target is two cells out.
        Assert.That(Math.Max(Math.Abs(worker.CellX - 8), Math.Abs(worker.CellY - 8)), Is.EqualTo(2));
    }

    [Test]
    public void CommandForSomeoneElsesUnit_IsIgnored()
    {
        var (sim, worker) = OneWorker(new MapSetup(16, 16), 2, 2);
        sim.Order(sim.Move(Setups.PlayerB, Center(10), Center(10), worker));
        sim.Run(20);
        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        Assert.That((worker.CellX, worker.CellY), Is.EqualTo((2, 2)));
    }

    [Test]
    public void GroupMovedToOnePoint_SpreadsOut()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(16, 16)));
        UnitStats stats = sim.State.Data.Unit("infantry");
        var group = Enumerable.Range(0, 4)
            .Select(i => sim.State.SpawnUnit(Setups.PlayerA, stats, Center(2 + i), Center(2)))
            .ToArray();
        sim.Order(sim.Move(Setups.PlayerA, Center(10), Center(10), group));

        sim.Run(400);
        for (int i = 0; i < group.Length; i++)
        {
            for (int j = i + 1; j < group.Length; j++)
            {
                long d = Movement.Distance(group[i].X, group[i].Y, group[j].X, group[j].Y);
                Assert.That(d, Is.GreaterThanOrEqualTo(group[i].Radius + group[j].Radius - 2));
            }
            Assert.That(Movement.Distance(group[i].X, group[i].Y, Center(10), Center(10)), Is.LessThan(Cells(2)));
        }
    }

    [Test]
    public void IdleOwnUnitOnTheWay_DoesNotStopTheMover()
    {
        var (sim, worker) = OneWorker(new MapSetup(20, 8), 1, 4);
        sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("infantry"), Center(8), Center(4));
        sim.Order(sim.Move(Setups.PlayerA, Center(16), Center(4), worker));
        sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Idle);
        Assert.That(worker.CellX, Is.EqualTo(16));
    }

    [Test]
    public void TwoUnitsHeadOn_PassEachOther()
    {
        var sim = new Simulation(Setups.Coop(new MapSetup(20, 8)));
        Unit a = sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("infantry"), Center(2), Center(4));
        Unit b = sim.State.SpawnUnit(Setups.PlayerB, sim.State.Data.Unit("infantry"), Center(16), Center(4));
        sim.Order(sim.Move(Setups.PlayerA, Center(16), Center(4), a));
        sim.Order(sim.Move(Setups.PlayerB, Center(2), Center(4), b));
        sim.RunUntil(s => a.Order.Kind == UnitOrderKind.Idle && b.Order.Kind == UnitOrderKind.Idle, 600);
        Assert.That(a.CellX, Is.EqualTo(16));
        Assert.That(b.CellX, Is.EqualTo(2));
    }

    [Test]
    public void UnitUnderANewBuilding_IsPushedOut()
    {
        var (sim, worker) = OneWorker(new MapSetup(16, 16), 6, 6);
        sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("factory"), 5, 5, Direction.Right, complete: true);
        sim.Tick();
        Assert.That(sim.State.Map.IsPassable(worker.CellX, worker.CellY), Is.True);
        Assert.That(Math.Max(Math.Abs(worker.CellX - 6), Math.Abs(worker.CellY - 6)), Is.LessThanOrEqualTo(2));
    }

    [Test]
    public void UnitsWalkOverConveyors()
    {
        var map = new MapSetup(16, 16);
        var (sim, worker) = OneWorker(map, 2, 4);
        for (int y = 0; y < 16; y++)
            sim.State.PlaceBuilding(Setups.PlayerB, sim.State.Data.Building("conveyor"), 6, y, Direction.Up, complete: true);
        sim.Order(sim.Move(Setups.PlayerA, Center(10), Center(4), worker));
        int ticks = sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Idle);
        Assert.That(ticks, Is.EqualTo(8 * 1000 / 120 + 1));
    }

    [Test]
    public void MoveCommand_RoundTripsThroughTheCodec()
    {
        var codec = GameCommands.CreateCodec();
        var command = new MoveCommand(1, 7, new[] { 3, 5 }, 1500, -20);
        var read = (MoveCommand)codec.ReadLog(codec.WriteLog(new ICommand[] { command }))[0];
        Assert.That(read.PlayerId, Is.EqualTo(1));
        Assert.That(read.Tick, Is.EqualTo(7));
        Assert.That(read.UnitIds, Is.EqualTo(new[] { 3, 5 }));
        Assert.That((read.X, read.Y), Is.EqualTo((1500, -20)));
    }

    [Test]
    public void Determinism_SameMovesSameHashes_TwiceInARow()
    {
        Assert.That(RunScript(), Is.EqualTo(RunScript()));
    }

    private static List<ulong> RunScript()
    {
        var map = new MapSetup(32, 32).Start(Setups.PlayerA, 2, 2).Start(Setups.PlayerB, 20, 20);
        for (int y = 5; y < 25; y++)
            map.Block(14, y);
        var sim = new Simulation(Setups.Coop(map));
        var a = sim.State.UnitsOf(Setups.PlayerA).ToArray();
        var b = sim.State.UnitsOf(Setups.PlayerB).ToArray();
        sim.Order(sim.Move(Setups.PlayerA, Center(25), Center(10), a));
        sim.Order(sim.Move(Setups.PlayerB, Center(3), Center(25), b));
        var hashes = new List<ulong>();
        sim.Run(300, s =>
        {
            hashes.Add(s.StateHash);
            if (s.State.Tick == 100)
                s.Order(s.Move(Setups.PlayerA, Center(16), Center(16), a));
        });
        Assert.That(hashes.Distinct().Count(), Is.GreaterThan(100));
        return hashes;
    }
}
