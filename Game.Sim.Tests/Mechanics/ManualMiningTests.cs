using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/manual-mining.md
public class ManualMiningTests
{
    // HQ of A at (2,2) (4x4), of B at (20,2); ore at x = 10..11, y = 2..3, and second-resource ore at (10,8).
    private static Simulation Match()
    {
        var map = new MapSetup(32, 16)
            .Start(Setups.PlayerA, 2, 2)
            .Start(Setups.PlayerB, 20, 2)
            .OreRect(10, 2, 2, 2, Resources.Primary)
            .Ore(10, 8, Resources.Secondary);
        var sim = new Simulation(Setups.Coop(map));
        // The ore is between the bases, inside the headquarters' vision: already discovered.
        return sim;
    }

    private static Unit FirstWorker(Simulation sim, int player) => sim.State.UnitsOf(player).First();

    private static int Wallet(Simulation sim, int player, int type = Resources.Primary) =>
        sim.State.GetPlayer(player).Wallet.Get(type);

    private static MineCommand Mine(Simulation sim, int player, int x, int y, params Unit[] workers) =>
        new MineCommand(player, sim.State.Tick, workers.Select(w => w.Id).ToArray(), x, y);

    [Test]
    public void GWT1_DigsFiveInTenSeconds_CarriesHome_AndComesBack()
    {
        var sim = Match();
        Unit worker = FirstWorker(sim, Setups.PlayerA);
        int start = Wallet(sim, Setups.PlayerA);
        sim.Order(Mine(sim, Setups.PlayerA, 10, 2, worker));

        sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Digging);
        int digTicks = sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Carrying);
        Assert.That(digTicks, Is.EqualTo(10 * Simulation.TicksPerSecond));
        Assert.That(worker.Worker!.Cargo, Is.EqualTo(5));
        Assert.That(worker.Worker.CargoType, Is.EqualTo(Resources.Primary));

        sim.RunUntil(s => Wallet(s, Setups.PlayerA) == start + 5);
        Assert.That(worker.Worker.Cargo, Is.EqualTo(0));
        Assert.That(worker.Worker.Phase, Is.EqualTo(WorkerPhase.GoingToOre));

        sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Digging);
        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Mine));
    }

    [Test]
    public void GWT2_DeliveredToAllyHeadquarters_CreditsTheAlly()
    {
        var sim = Match();
        Unit worker = FirstWorker(sim, Setups.PlayerA);
        int a = Wallet(sim, Setups.PlayerA), b = Wallet(sim, Setups.PlayerB);
        sim.Order(Mine(sim, Setups.PlayerA, 10, 2, worker));
        sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Carrying);

        Building hqB = sim.State.FindHeadquarters(Setups.PlayerB)!;
        sim.Order(new DeliverCargoCommand(Setups.PlayerA, sim.State.Tick, new[] { worker.Id }, hqB.Id));
        sim.RunUntil(s => Wallet(s, Setups.PlayerB) == b + 5);

        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(a));
        // And it keeps feeding the ally on the next trips.
        sim.RunUntil(s => Wallet(s, Setups.PlayerB) == b + 10);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(a));
    }

    [Test]
    public void GWT3_OreUnderADrill_OrderRejected()
    {
        var sim = Match();
        sim.State.PlaceBuilding(Setups.PlayerB, sim.State.Data.Building("drill"), 10, 2, Direction.Right, complete: true);
        Unit worker = FirstWorker(sim, Setups.PlayerA);

        sim.Order(Mine(sim, Setups.PlayerA, 10, 2, worker));
        sim.Tick();

        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        var rejected = sim.Events.OfType<CommandRejected>().Single();
        Assert.That(rejected.PlayerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(rejected.Reason, Is.EqualTo(RejectReason.OreCellTaken));
    }

    [Test]
    public void GWT4_SecondResource_IsCarriedAndCreditedAsSecond()
    {
        var sim = Match();
        Unit worker = FirstWorker(sim, Setups.PlayerA);
        sim.Order(Mine(sim, Setups.PlayerA, 10, 8, worker));
        sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Carrying);
        Assert.That(worker.Worker!.CargoType, Is.EqualTo(Resources.Secondary));

        int primary = Wallet(sim, Setups.PlayerA);
        sim.RunUntil(s => Wallet(s, Setups.PlayerA, Resources.Secondary) == 5);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(primary));
    }

    [Test]
    public void Edge_SiteStartedOnTheCellWhileDigging_CarriesWhatItHas_ThenStops()
    {
        var sim = Match();
        Unit worker = FirstWorker(sim, Setups.PlayerA);
        sim.Order(Mine(sim, Setups.PlayerA, 10, 2, worker));
        sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Digging);
        sim.Run(5 * Simulation.TicksPerSecond);  // half a load
        int dug = worker.Worker!.Cargo;
        Assert.That(dug, Is.InRange(2, 3));

        int start = Wallet(sim, Setups.PlayerA);
        sim.State.PlaceBuilding(Setups.PlayerB, sim.State.Data.Building("drill"), 10, 2, Direction.Right, complete: false);
        var stopped = sim.RunCollecting<MiningStopped>(2);
        Assert.That(stopped.Single().WorkerId, Is.EqualTo(worker.Id));

        sim.RunUntil(s => Wallet(s, Setups.PlayerA) == start + dug);
        sim.Run(5);
        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        Assert.That(worker.Worker.Cargo, Is.EqualTo(0));
    }

    [Test]
    public void Edge_WorkerKilledWithCargo_CargoIsLost()
    {
        var sim = Match();
        Unit worker = FirstWorker(sim, Setups.PlayerA);
        int start = Wallet(sim, Setups.PlayerA);
        sim.Order(Mine(sim, Setups.PlayerA, 10, 2, worker));
        sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Carrying);

        worker.Hp = 0;
        worker.IsDead = true;
        sim.Run(20 * Simulation.TicksPerSecond);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(start));
        Assert.That(sim.State.FindUnit(worker.Id), Is.Null);
    }

    [Test]
    public void Edge_TwoPlayersDigTheSameCell_EachGetsTheirOwnLoad()
    {
        var sim = Match();
        Unit a = FirstWorker(sim, Setups.PlayerA), b = FirstWorker(sim, Setups.PlayerB);
        int walletA = Wallet(sim, Setups.PlayerA), walletB = Wallet(sim, Setups.PlayerB);
        sim.Order(Mine(sim, Setups.PlayerA, 11, 3, a));
        sim.Order(Mine(sim, Setups.PlayerB, 11, 3, b));

        sim.RunUntil(s => Wallet(s, Setups.PlayerA) >= walletA + 5 && Wallet(s, Setups.PlayerB) >= walletB + 5);
    }

    [Test]
    public void Edge_WorkerWithCargoGetsABuildOrder_KeepsTheCargo()
    {
        var sim = Match();
        Unit worker = FirstWorker(sim, Setups.PlayerA);
        sim.Order(Mine(sim, Setups.PlayerA, 10, 2, worker));
        sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Carrying);

        sim.Order(sim.Move(Setups.PlayerA, Center(8), Center(10), worker));
        sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Idle);
        Assert.That(worker.Worker!.Cargo, Is.EqualTo(5));

        // Back to mining: it first hands in the full load it still carries.
        int start = Wallet(sim, Setups.PlayerA);
        sim.Order(Mine(sim, Setups.PlayerA, 10, 2, worker));
        sim.Tick();
        Assert.That(worker.Worker.Phase, Is.EqualTo(WorkerPhase.Carrying));
        sim.RunUntil(s => Wallet(s, Setups.PlayerA) == start + 5);
    }

    [Test]
    public void Fail_HeadquartersDestroyedWhileCarrying_StopsWithTheCargo()
    {
        var sim = Match();
        Unit worker = FirstWorker(sim, Setups.PlayerA);
        sim.Order(Mine(sim, Setups.PlayerA, 10, 2, worker));
        sim.RunUntil(s => worker.Worker!.Phase == WorkerPhase.Carrying);

        Building hq = sim.State.FindHeadquarters(Setups.PlayerA)!;
        hq.Hp = 0;
        hq.IsDead = true;
        sim.Run(3);

        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        Assert.That(worker.Worker!.Cargo, Is.EqualTo(5));
    }

    [Test]
    public void Fail_CannotDeliverToEnemyHeadquarters()
    {
        var map = new MapSetup(32, 16).Start(Setups.PlayerA, 2, 2).Start(Setups.EnemyAi, 20, 2);
        var sim = new Simulation(Setups.Coop(map));
        Unit worker = FirstWorker(sim, Setups.PlayerA);
        Building enemyHq = sim.State.FindHeadquarters(Setups.EnemyAi)!;

        sim.Order(new DeliverCargoCommand(Setups.PlayerA, sim.State.Tick, new[] { worker.Id }, enemyHq.Id));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
        Assert.That(worker.Worker!.DeliverToId, Is.EqualTo(0));
    }

    [Test]
    public void Fail_OreNotDiscovered_OrderRejected()
    {
        var map = new MapSetup(64, 16).Start(Setups.PlayerA, 2, 2).Ore(60, 8, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        sim.Order(Mine(sim, Setups.PlayerA, 60, 8, FirstWorker(sim, Setups.PlayerA)));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotDiscovered));
    }

    [Test]
    public void Commands_RoundTripThroughTheCodec()
    {
        var codec = GameCommands.CreateCodec();
        var log = codec.ReadLog(codec.WriteLog(new ICommand[]
        {
            new MineCommand(1, 3, new[] { 4 }, 10, 2),
            new DeliverCargoCommand(2, 5, new[] { 6, 7 }, 9),
        }));
        var mine = (MineCommand)log[0];
        var deliver = (DeliverCargoCommand)log[1];
        Assert.That((mine.CellX, mine.CellY, mine.UnitIds[0]), Is.EqualTo((10, 2, 4)));
        Assert.That((deliver.HeadquartersId, deliver.UnitIds.Count), Is.EqualTo((9, 2)));
    }
}
