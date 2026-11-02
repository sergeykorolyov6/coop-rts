using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/order-queue.md
public class OrderQueueTests
{
    // HQ of A at (2,2); ore 2×2 at (10,2) and (10,6), inside the headquarters' vision.
    private static Simulation Match()
    {
        var map = new MapSetup(32, 16)
            .Start(Setups.PlayerA, 2, 2)
            .OreRect(10, 2, 2, 2, Resources.Primary)
            .OreRect(10, 6, 2, 2, Resources.Primary);
        return new Simulation(Setups.Coop(map));
    }

    private static Unit Worker(Simulation sim) => sim.State.UnitsOf(Setups.PlayerA).First(u => u.Worker != null);

    private static int[] Ids(params Unit[] units) => units.Select(u => u.Id).ToArray();

    private static MoveCommand Move(Simulation sim, int cx, int cy, bool queued, params Unit[] units) =>
        new MoveCommand(Setups.PlayerA, sim.State.Tick, Ids(units), Center(cx), Center(cy), queued);

    private static BuildCommand Build(Simulation sim, int x, int y, bool queued, params Unit[] units) =>
        new BuildCommand(Setups.PlayerA, sim.State.Tick, Ids(units), "drill", new[] { new BuildSite(x, y, Direction.Right) }, queued);

    private static bool Near(Unit unit, int cx, int cy) =>
        System.Math.Abs(unit.X - Center(cx)) <= Cells(1) && System.Math.Abs(unit.Y - Center(cy)) <= Cells(1);

    [Test]
    public void GWT1_ShiftMoveAThenB_VisitsAThenB_EndsIdleAtB()
    {
        var sim = Match();
        Unit worker = Worker(sim);
        sim.Order(Move(sim, 12, 10, true, worker));
        sim.Order(Move(sim, 24, 10, true, worker));
        sim.Tick();
        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Move));
        Assert.That(worker.OrderQueue, Has.Count.EqualTo(1));

        sim.RunUntil(s => Near(worker, 12, 10));
        Assert.That(worker.X, Is.LessThan(Center(16)), "reached A before heading to B");
        sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Idle && worker.OrderQueue.Count == 0);
        Assert.That(Near(worker, 24, 10), Is.True);
    }

    [Test]
    public void GWT2_InfantryMovingToA_ShiftAttack_ReachesAFirst_ThenAttacks()
    {
        var sim = Match();
        Unit infantry = sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("infantry"), Center(4), Center(12));
        Unit enemy = sim.State.SpawnUnit(Setups.EnemyAi, sim.State.Data.Unit("legion_raider"), Center(30), Center(12));
        sim.Order(Move(sim, 14, 12, false, infantry));
        sim.Order(new AttackCommand(Setups.PlayerA, sim.State.Tick, Ids(infantry), enemy.Id, true));
        sim.Tick();
        Assert.That(infantry.Order.Kind, Is.EqualTo(UnitOrderKind.Move));

        sim.RunUntil(s => infantry.Order.Kind == UnitOrderKind.Attack);
        Assert.That(Near(infantry, 14, 12), Is.True, "started the attack at A");
        sim.RunUntil(s => enemy.Hp < enemy.MaxHp);
    }

    [Test]
    public void GWT3_BuildingDrill_ShiftSecondDrill_BuiltAfterFirst_EachPaidOnStart()
    {
        var sim = Match();
        Unit worker = Worker(sim);
        int start = sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(Resources.Primary);
        sim.Order(Build(sim, 10, 2, false, worker));
        sim.Order(Build(sim, 10, 6, true, worker));

        Building? first = null;
        sim.RunUntil(s => (first = s.State.Buildings.FirstOrDefault(b => b.CellY == 2 && b.Stats.Id == "drill")) != null);
        Assert.That(sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(Resources.Primary), Is.EqualTo(start - 30), "second not paid yet");

        sim.RunUntil(s => s.State.Buildings.Any(b => b.CellY == 6 && b.Stats.Id == "drill"));
        Assert.That(first!.IsComplete, Is.True, "second started only after the first was finished");
        Assert.That(sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(Resources.Primary), Is.EqualTo(start - 60));
        sim.RunUntil(s => s.State.Buildings.Count(b => b.Stats.Id == "drill" && b.IsComplete) == 2);
    }

    [Test]
    public void GWT4_QueueOfTwoPoints_MoveWithoutShift_ClearsQueue_GoesToC()
    {
        var sim = Match();
        Unit worker = Worker(sim);
        sim.Order(Move(sim, 12, 10, false, worker));
        sim.Order(Move(sim, 20, 10, true, worker));
        sim.Order(Move(sim, 24, 10, true, worker));
        sim.Tick();
        Assert.That(worker.OrderQueue, Has.Count.EqualTo(2));

        sim.Order(Move(sim, 6, 12, false, worker));
        sim.Tick();
        Assert.That(worker.OrderQueue, Is.Empty);
        Assert.That((worker.Order.Kind, worker.Order.X, worker.Order.Y), Is.EqualTo((UnitOrderKind.Move, Center(6), Center(12))));
    }

    [Test]
    public void GWT5_IdleWorker_ShiftMine_MinesAtOnce()
    {
        var sim = Match();
        Unit worker = Worker(sim);
        sim.Order(new MineCommand(Setups.PlayerA, sim.State.Tick, Ids(worker), 10, 2, true));
        sim.Tick();
        Assert.That((worker.Order.Kind, worker.Order.X, worker.Order.Y), Is.EqualTo((UnitOrderKind.Mine, 10, 2)));
        Assert.That(worker.OrderQueue, Is.Empty);
    }

    [Test]
    public void GWT6_QueuedRepair_AlreadyRepaired_Skipped_NextOrderRuns()
    {
        var sim = Match();
        Unit worker = Worker(sim);
        Building hq = sim.State.FindHeadquarters(Setups.PlayerA)!;
        hq.Hp = hq.MaxHp - 100;
        sim.Order(Move(sim, 20, 10, false, worker));
        sim.Order(new RepairCommand(Setups.PlayerA, sim.State.Tick, Ids(worker), hq.Id, true));
        sim.Order(Move(sim, 24, 12, true, worker));
        sim.Tick();
        Assert.That(worker.OrderQueue, Has.Count.EqualTo(2));

        hq.Hp = hq.MaxHp;  // someone else repaired it meanwhile
        sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Idle && worker.OrderQueue.Count == 0);
        Assert.That(Near(worker, 24, 12), Is.True);
    }

    [Test]
    public void Edge_QueueFull_33rdRejected_QueueUnchanged()
    {
        var sim = Match();
        Unit worker = Worker(sim);
        sim.Order(Move(sim, 30, 14, false, worker));
        for (int i = 0; i < UnitOrders.MaxQueue; i++)
            sim.Order(Move(sim, i % 30, 10, true, worker));
        sim.Tick();
        Assert.That(worker.OrderQueue, Has.Count.EqualTo(UnitOrders.MaxQueue));

        sim.Order(Move(sim, 5, 5, true, worker));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
        Assert.That(worker.OrderQueue, Has.Count.EqualTo(UnitOrders.MaxQueue));
    }

    [Test]
    public void Edge_ShiftAfterMine_WaitsUntilMiningStops()
    {
        var sim = Match();
        Unit worker = Worker(sim);
        sim.Order(new MineCommand(Setups.PlayerA, sim.State.Tick, Ids(worker), 10, 2));
        sim.Order(Move(sim, 24, 12, true, worker));
        sim.Run(400);
        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Mine), "mining never ends by itself");
        Assert.That(worker.OrderQueue, Has.Count.EqualTo(1));

        // The cell is taken by an enemy drill: mining stops, the queued move runs.
        sim.State.PlaceBuilding(Setups.EnemyAi, sim.State.Data.Building("drill"), 10, 2, Direction.Right, complete: true);
        sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Move && worker.Order.X == Center(24));
    }

    [Test]
    public void Edge_Determinism_QueuedFlag_RoundTripsInCommandLog()
    {
        var codec = GameCommands.CreateCodec();
        int[] units = { 7 };
        var log = new ICommand[]
        {
            new MoveCommand(1, 3, units, 100, 200, true),
            new MineCommand(1, 3, units, 4, 5, true),
            new AttackCommand(1, 3, units, 9, true),
            new RepairCommand(1, 3, units, 9, true),
            new BuildCommand(1, 3, units, "drill", new[] { new BuildSite(4, 5, Direction.Up) }, true),
            new CarryTrophyCommand(1, 3, units, 0, true),
            new MoveCommand(1, 3, units, 100, 200),
        };

        List<ICommand> restored = codec.ReadLog(codec.WriteLog(log));

        Assert.That(((MoveCommand)restored[0]).Queued, Is.True);
        Assert.That(((MineCommand)restored[1]).Queued, Is.True);
        Assert.That(((AttackCommand)restored[2]).Queued, Is.True);
        Assert.That(((RepairCommand)restored[3]).Queued, Is.True);
        Assert.That(((BuildCommand)restored[4]).Queued, Is.True);
        Assert.That(((CarryTrophyCommand)restored[5]).Queued, Is.True);
        Assert.That(((MoveCommand)restored[6]).Queued, Is.False);
    }

    [Test]
    public void Edge_Determinism_QueueInStateHash_SameCommandsSameHashes()
    {
        List<ulong> Run(bool secondQueued)
        {
            var sim = Match();
            Unit worker = Worker(sim);
            sim.Order(Move(sim, 12, 10, false, worker));
            sim.Order(Move(sim, 20, 10, secondQueued, worker));
            var hashes = new List<ulong>();
            sim.Run(200, s => hashes.Add(s.StateHash));
            return hashes;
        }

        Assert.That(Run(true), Is.EqualTo(Run(true)));
        Assert.That(Run(true)[0], Is.Not.EqualTo(Run(false)[0]), "the queue is part of the hash");
    }
}
