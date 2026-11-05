using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/builder.md
public class BuilderTests
{
    private const int Second = Simulation.TicksPerSecond;

    // A at (2,2), B at (20,2).
    private static Simulation Match() => ConstructionTests.Match();

    private static Unit Builder(Simulation sim, int player) => sim.State.UnitsOf(player).Single(u => u.IsBuilder);

    private static int Wallet(Simulation sim, int player, int type = Resources.Primary) =>
        sim.State.GetPlayer(player).Wallet.Get(type);

    [Test]
    public void GWT1_Start_OneBuilderPerPlayer_NoWorkers()
    {
        var sim = Match();
        foreach (int p in new[] { Setups.PlayerA, Setups.PlayerB })
        {
            Assert.That(sim.State.UnitsOf(p), Has.Count.EqualTo(1));
            Assert.That(Builder(sim, p).Worker, Is.Not.Null);
        }
        Assert.That(sim.State.Data.TryGetUnit("worker", out _), Is.False);
    }

    [Test]
    public void GWT2_BuilderDies_ComesBackNextToOwnHeadquartersAfter15s()
    {
        var sim = Match();
        Kill(Builder(sim, Setups.PlayerA));
        sim.Tick();
        BuilderDied died = sim.Events.OfType<BuilderDied>().Single();
        Assert.That(died.PlayerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(died.RespawnTick, Is.EqualTo(15 * Second));
        Assert.That(sim.State.UnitsOf(Setups.PlayerA), Is.Empty);

        int ticks = sim.RunUntil(s => s.Events.OfType<BuilderRespawned>().Any());
        Assert.That(sim.State.Tick - 1, Is.EqualTo(15 * Second));
        Assert.That(ticks, Is.EqualTo(15 * Second));
        Unit back = Builder(sim, Setups.PlayerA);
        Building hq = sim.State.FindHeadquarters(Setups.PlayerA)!;
        Assert.That(Movement.DistanceToBuilding(back.X, back.Y, hq), Is.LessThanOrEqualTo(1000));
        Assert.That(sim.Events.OfType<BuilderRespawned>().Single().HeadquartersId, Is.EqualTo(hq.Id));
    }

    [Test]
    public void GWT3_NoOwnHeadquarters_ComesBackAtTheAllysOne()
    {
        var sim = Match();
        Kill(sim.State.FindHeadquarters(Setups.PlayerA)!);
        Kill(Builder(sim, Setups.PlayerA));
        sim.RunUntil(s => s.Events.OfType<BuilderRespawned>().Any());
        Building allyHq = sim.State.FindHeadquarters(Setups.PlayerB)!;
        Unit back = Builder(sim, Setups.PlayerA);
        Assert.That(back.OwnerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(Movement.DistanceToBuilding(back.X, back.Y, allyHq), Is.LessThanOrEqualTo(1000));
    }

    [Test]
    public void GWT4_IdleRepairer_RepairsADamagedTurretByItself_PaidByItsOwner()
    {
        var sim = Match();
        Building turret = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("turret"), 12, 6, Direction.Right, complete: true);
        turret.Hp = 200;
        turret.NoSelfRepair();
        sim.State.GetPlayer(Setups.PlayerA).Wallet.Add(Resources.Secondary, 20);
        int primary = Wallet(sim, Setups.PlayerA), secondary = Wallet(sim, Setups.PlayerA, Resources.Secondary);
        Unit repairer = Spawn(sim, Setups.PlayerA, "repairer", 9, 9);

        sim.RunUntil(s => turret.Hp == turret.MaxHp, 60 * Second);
        // Full repair costs half of 40 + 10; half the HP was missing: ≈ 10 and 2–3.
        Assert.That(primary - Wallet(sim, Setups.PlayerA), Is.InRange(9, 10));
        Assert.That(secondary - Wallet(sim, Setups.PlayerA, Resources.Secondary), Is.InRange(2, 3));
        sim.Run(2);
        Assert.That(repairer.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void GWT5_Repairer_HealsAnAllysUnit_ItsOwnerPays()
    {
        var sim = Match();
        Unit infantry = Spawn(sim, Setups.PlayerB, "infantry", 14, 8);
        infantry.Hp = 120;
        Spawn(sim, Setups.PlayerA, "repairer", 10, 8);
        int a = Wallet(sim, Setups.PlayerA), b = Wallet(sim, Setups.PlayerB);

        sim.RunUntil(s => infantry.Hp == infantry.MaxHp, 60 * Second);
        // 100 of 220 HP at half of 30: ≈ 6.8 units, charged as it goes.
        Assert.That(a - Wallet(sim, Setups.PlayerA), Is.InRange(6, 7));
        Assert.That(Wallet(sim, Setups.PlayerB), Is.EqualTo(b));
    }

    [Test]
    public void GWT5b_RepairOfAUnit_TakesItsBuildTime()
    {
        var sim = Match();
        Unit infantry = Spawn(sim, Setups.PlayerA, "infantry", 11, 8);
        Spawn(sim, Setups.PlayerA, "repairer", 10, 8);
        infantry.Hp = 1;
        int ticks = sim.RunUntil(s => infantry.Hp == infantry.MaxHp, 60 * Second);
        // 219 HP over 25 s for 220: ≈ 24.9 s, plus the wait for the next scan.
        Assert.That(ticks, Is.InRange(25 * Second * 219 / 220, 25 * Second * 219 / 220 + 12));
    }

    [Test]
    public void GWT6_RepairerOnOrder_RepairsADrill()
    {
        var sim = Match();
        Building drill = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), 20, 10, Direction.Right, complete: true);
        drill.Hp = 150;
        drill.NoSelfRepair();
        Unit repairer = Spawn(sim, Setups.PlayerA, "repairer", 2, 12);
        sim.Order(ConstructionTests.Repair(sim, Setups.PlayerA, drill, repairer));
        sim.RunUntil(s => drill.Hp == drill.MaxHp, 60 * Second);
        Assert.That(repairer.Order.IsAuto, Is.False);
    }

    [Test]
    public void Edge_FactoryCannotProduceABuilder()
    {
        var sim = Match();
        Building factory = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("factory"), 10, 6, Direction.Right, complete: true);
        factory.Stock!.Add(Resources.Primary, 100);
        sim.Order(new ProduceCommand(Setups.PlayerA, sim.State.Tick, factory.Id, "builder"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.CannotProduce));
    }

    [Test]
    public void Edge_TwoDamagedAtTheSameDistance_LowerIdFirst()
    {
        var sim = Match();
        Unit first = Spawn(sim, Setups.PlayerA, "infantry", 10, 10);
        Unit second = Spawn(sim, Setups.PlayerA, "infantry", 14, 10);
        first.Hp = second.Hp = 100;
        Unit repairer = Spawn(sim, Setups.PlayerA, "repairer", 12, 10);
        sim.RunUntil(s => repairer.Order.Kind == UnitOrderKind.Repair, 20);
        Assert.That(repairer.Order.TargetId, Is.EqualTo(first.Id));
    }

    [Test]
    public void Edge_EmptyWallet_RepairerTakesNothing_NoEvents()
    {
        var sim = Match();
        Building drill = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), 12, 6, Direction.Right, complete: true);
        drill.Hp = 100;
        drill.NoSelfRepair();
        var wallet = sim.State.GetPlayer(Setups.PlayerA).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary));
        Unit repairer = Spawn(sim, Setups.PlayerA, "repairer", 10, 9);
        var rejected = sim.RunCollecting<CommandRejected>(5 * Second);
        Assert.That(rejected, Is.Empty);
        Assert.That(repairer.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        Assert.That(drill.Hp, Is.EqualTo(100));
    }

    [Test]
    public void Edge_RepairerToldToMove_GoesThere_ThenRepairsAgainByItself()
    {
        var sim = Match();
        Unit repairer = Spawn(sim, Setups.PlayerA, "repairer", 10, 9);
        sim.Order(sim.Move(Setups.PlayerA, Center(16), Center(9), repairer));
        sim.RunUntil(s => repairer.Order.Kind == UnitOrderKind.Idle && repairer.CellX == 16);
        Building drill = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), 18, 9, Direction.Right, complete: true);
        drill.Hp = 250;
        drill.NoSelfRepair();
        sim.RunUntil(s => drill.Hp == drill.MaxHp, 30 * Second);
    }

    [Test]
    public void Edge_NoHeadquartersAnywhere_WaitsUntilTheAllyBuildsOne()
    {
        var sim = Match();
        sim.State.GetPlayer(Setups.PlayerB).Wallet.Add(Resources.Primary, 200);
        Kill(sim.State.FindHeadquarters(Setups.PlayerA)!);
        Kill(sim.State.FindHeadquarters(Setups.PlayerB)!);
        Kill(Builder(sim, Setups.PlayerA));
        sim.Run(20 * Second);
        Assert.That(sim.State.UnitsOf(Setups.PlayerA), Is.Empty);

        Unit b = Builder(sim, Setups.PlayerB);
        sim.Order(new BuildCommand(Setups.PlayerB, sim.State.Tick, new[] { b.Id }, "headquarters", new[] { new BuildSite(20, 8, Direction.Right) }));
        sim.RunUntil(s => s.State.FindHeadquarters(Setups.PlayerB)?.IsComplete == true, 90 * Second);
        sim.Tick();
        Assert.That(sim.State.UnitsOf(Setups.PlayerA).Count(u => u.IsBuilder), Is.EqualTo(1));
    }

    [Test]
    public void Edge_RepairerIgnoresABuildOrder()
    {
        var sim = Match();
        sim.State.GetPlayer(Setups.PlayerA).Wallet.Add(Resources.Primary, 100);
        Unit repairer = Spawn(sim, Setups.PlayerA, "repairer", 10, 9);
        sim.Order(new BuildCommand(Setups.PlayerA, sim.State.Tick, new[] { repairer.Id }, "drill", new[] { new BuildSite(12, 6, Direction.Right) }));
        sim.Run(10 * Second);
        Assert.That(sim.State.Buildings.Any(b => b.Stats.Id == "drill"), Is.False);
    }
}
