using System;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/turret.md
public class TurretTests
{
    // Turret of A occupies cells (10..11, 5..6): its right edge is x = 12 cells.
    private const int TX = 10, TY = 5;
    private const int RightEdge = 12 * Grid.MilliPerCell;

    private static Simulation Empty(MapSetup? map = null) => new Simulation(Setups.Coop(map ?? new MapSetup(40, 20)));

    private static Building Turret(Simulation sim, int owner = Setups.PlayerA, bool complete = true, int x = TX, int y = TY) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building("turret"), x, y, Direction.Right, complete);

    private static Unit SpawnAt(Simulation sim, int owner, string type, int x, int y) =>
        sim.State.SpawnUnit(owner, sim.State.Data.Unit(type), x, y);

    // A unit whose edge is `cells` from the turret's right edge, on the turret's lower row.
    private static Unit AtRange(Simulation sim, int owner, string type, int milli)
    {
        Unit u = SpawnAt(sim, owner, type, 0, Center(TY));
        u.X = RightEdge + milli + u.Radius;
        return u;
    }

    [Test]
    public void GWT1_GunnerInRange_TwelveEverySecond_DiesOnTheEighthHit_SevenSecondsAfterTheFirst()
    {
        var sim = Empty();
        Building turret = Turret(sim);
        Unit gunner = AtRange(sim, Setups.EnemyAi, "legion_gunner", Cells(6));

        int first = -1, last = -1, hits = 0;
        sim.RunUntil(s =>
        {
            foreach (HitLanded hit in s.Events.OfType<HitLanded>().Where(h => h.AttackerId == turret.Id))
            {
                Assert.That(hit.Damage, Is.EqualTo(12));
                hits++;
                if (first < 0)
                    first = hit.Tick;
                last = hit.Tick;
            }
            return gunner.IsDead;
        });

        Assert.That(hits, Is.EqualTo(8));
        Assert.That(last - first, Is.EqualTo(7 * Simulation.TicksPerSecond));
    }

    [Test]
    public void GWT2_UnfinishedOrFrozenTurret_DoesNotShoot()
    {
        var sim = Empty();
        Building turret = Turret(sim, complete: false);
        Unit enemy = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(3));
        sim.Run(5 * Simulation.TicksPerSecond);
        Assert.That(turret.State, Is.EqualTo(BuildState.Frozen));
        Assert.That(enemy.Hp, Is.EqualTo(enemy.MaxHp));
    }

    [Test]
    public void GWT3_EnemySevenCellsAway_NotHit_AtSixCells_HitTheSameTick()
    {
        var sim = Empty();
        Turret(sim);
        Unit enemy = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(7));
        sim.Run(2 * Simulation.TicksPerSecond);
        Assert.That(enemy.Hp, Is.EqualTo(enemy.MaxHp));

        enemy.X = RightEdge + Cells(6) + enemy.Radius;
        sim.Tick();
        Assert.That(enemy.Hp, Is.EqualTo(enemy.MaxHp - 12));
    }

    [Test]
    public void GWT4_UnitBeforeBuilding_ThenTheBuilding()
    {
        var sim = Empty();
        Building turret = Turret(sim);
        Building storage = sim.State.PlaceBuilding(Setups.EnemyAi, sim.State.Data.Building("storage"), 14, 5, Direction.Right, complete: true);
        Unit worker = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(4));
        sim.Run(2);
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(worker.Id));
        sim.RunUntil(s => worker.IsDead);
        sim.Run(2);
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(storage.Id));
        sim.RunUntil(s => storage.Hp < storage.MaxHp, 2 * Simulation.TicksPerSecond);
    }

    [Test]
    public void GWT5_TwoEnemiesAtTheSameDistance_LowerIdFirst()
    {
        var sim = Empty();
        Building turret = Turret(sim);
        // Mirror images around the turret's middle row (y = 6 cells).
        Unit a = SpawnAt(sim, Setups.EnemyAi, "legion_worker", RightEdge + Cells(3), Cells(6) + Cells(2));
        Unit b = SpawnAt(sim, Setups.EnemyAi, "legion_worker", RightEdge + Cells(3), Cells(6) - Cells(2));
        sim.Run(2);
        Assert.That(a.Id, Is.LessThan(b.Id));
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(a.Id));
    }

    [Test]
    public void GWT6_KeepsItsTarget_WhenAnotherEnemyComesCloser()
    {
        var sim = Empty();
        Building turret = Turret(sim);
        Unit x = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(5));
        sim.Run(2);
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(x.Id));

        Unit y = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(1));
        sim.Run(3 * Simulation.TicksPerSecond);
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(x.Id));
        Assert.That(y.Hp, Is.EqualTo(y.MaxHp));
    }

    [Test]
    public void GWT7_TurretOfA_DefendsAllyBsBuilding_WithoutOrders()
    {
        var sim = Empty();
        Building turret = Turret(sim);
        Building storageB = sim.State.PlaceBuilding(Setups.PlayerB, sim.State.Data.Building("storage"), 13, 8, Direction.Right, complete: true);
        Unit raider = SpawnAt(sim, Setups.EnemyAi, "legion_raider", Center(20), Center(9));
        sim.Order(new AttackCommand(Setups.EnemyAi, 0, new[] { raider.Id }, storageB.Id));
        sim.RunUntil(s => raider.IsDead);
        Assert.That(storageB.IsDead, Is.False);
        Assert.That(turret.OwnerId, Is.EqualTo(Setups.PlayerA));
    }

    [Test]
    public void Edge_VisionShorterThanRangePlusHalfDiagonalPlusACell_DataRejected()
    {
        string dir = System.IO.Path.Combine(RepoData.RepoRoot(), "Assets", "Data");
        string buildings = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "buildings.json"));
        int at = buildings.IndexOf("\"id\": \"turret\"", StringComparison.Ordinal);
        int vision = buildings.IndexOf("\"vision_mc\": 9000", at, StringComparison.Ordinal);
        Assert.That(vision, Is.GreaterThan(at), "turret row with vision 9 cells");
        string broken = buildings.Substring(0, vision) + "\"vision_mc\": 8000" + buildings.Substring(vision + "\"vision_mc\": 9000".Length);

        var ex = Assert.Throws<FormatException>(() => GameData.Parse(
            System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "units.json")),
            broken,
            System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "creep_camps.json")),
            System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "rules.json"))));
        Assert.That(ex!.Message, Does.Contain("turret"));
    }

    [Test]
    public void Edge_TwoTurretsKillOneTargetInOneTick_BothShoot_TheExtraBlowIsLost()
    {
        var sim = Empty();
        Building t1 = Turret(sim);
        Building t2 = Turret(sim, y: TY + 3);
        Unit enemy = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(2));
        enemy.Hp = 12;
        sim.RunUntil(s => s.Events.OfType<HitLanded>().Any(h => h.TargetId == enemy.Id));
        Assert.That(enemy.IsDead, Is.True);
        // Both fired (both reload); the blow that found the target already dead is lost.
        Assert.That(t1.Turret!.Cooldown, Is.EqualTo(t1.Stats.AttackCooldownTicks));
        Assert.That(t2.Turret!.Cooldown, Is.EqualTo(t2.Stats.AttackCooldownTicks));
    }

    [Test]
    public void Edge_TargetLeavesRange_PicksAnother_NoneLeft_Waits()
    {
        var sim = Empty();
        Building turret = Turret(sim);
        Unit x = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(5));
        Unit y = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(4));
        y.Y = Center(TY) + Cells(1);
        sim.Run(2);
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(y.Id));

        y.X = RightEdge + Cells(9);
        sim.Run(2);
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(x.Id));

        x.X = RightEdge + Cells(9);
        sim.Run(2);
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(0));
    }

    [Test]
    public void Edge_FinishedThisTick_ShootsThisTick()
    {
        var sim = Empty();
        Building turret = Turret(sim, complete: false);
        Unit enemy = AtRange(sim, Setups.EnemyAi, "legion_worker", Cells(3));
        sim.Run(3);
        turret.AddWork(turret.BuildWork);
        sim.Tick();
        Assert.That(enemy.Hp, Is.EqualTo(enemy.MaxHp - 12));
    }

    [Test]
    public void Edge_AllyInRange_NotHit()
    {
        var sim = Empty();
        Building turret = Turret(sim);
        Unit ally = AtRange(sim, Setups.PlayerB, "builder", Cells(2));
        sim.Run(40);
        Assert.That(ally.Hp, Is.EqualTo(ally.MaxHp));
        Assert.That(turret.Turret!.TargetId, Is.EqualTo(0));
    }

    [Test]
    public void Edge_CreepInRange_Hit_TheCampWakesAndGoesForTheTurret()
    {
        // Camp centered at (24,6): the turret is 12 cells away, outside the 5-cell aggro radius;
        // one creep's home (17,6) is in the turret's range and within its leash of the turret.
        var map = new MapSetup(40, 20).CreepCamp("small_camp", 24, 6, (17, 6), (24, 7));
        var sim = Empty(map);
        Building turret = Turret(sim);
        Unit creep = sim.State.FindUnit(sim.State.CreepCamps[0].CreepIds[0])!;
        sim.RunUntil(s => creep.Hp < creep.MaxHp);
        sim.Tick();
        Assert.That(sim.State.CreepCamps[0].State, Is.EqualTo(CreepCampState.Alerted));
        sim.Run(10);
        Assert.That(creep.TargetId, Is.EqualTo(turret.Id));
    }
}
