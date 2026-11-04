using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/creep-camp.md
public class CreepCampTests
{
    // Camp «Стражи» centered at (20,8): big creep at (20,8), small at (21,8). HQ A at (2,2), HQ B at (30,1),
    // both well outside the 5-cell aggro radius.
    private const int CX = 20, CY = 8;

    private static Simulation Match(int width = 44, int height = 18, MapSetup? map = null)
    {
        map ??= new MapSetup(width, height);
        map.Start(Setups.PlayerA, 2, 2).Start(Setups.PlayerB, 30, 1)
           .CreepCamp("guard_camp", CX, CY, (CX, CY), (CX + 1, CY));
        return new Simulation(Setups.Coop(map));
    }

    private static CreepCamp Camp(Simulation sim) => sim.State.CreepCamps[0];

    private static Unit Big(Simulation sim) => sim.State.FindUnit(Camp(sim).CreepIds[0])!;
    private static Unit Small(Simulation sim) => sim.State.FindUnit(Camp(sim).CreepIds[1])!;

    private static Unit Spawn(Simulation sim, int owner, string type, int cx, int cy) =>
        sim.State.SpawnUnit(owner, sim.State.Data.Unit(type), Center(cx), Center(cy));

    private static void Kill(Entity e)
    {
        e.Hp = 0;
        e.IsDead = true;
    }

    [Test]
    public void Camp_SpawnsItsCreeps_OwnedByTheCreepPlayer_Calm()
    {
        var sim = Match();
        Assert.That(Big(sim).Stats.Id, Is.EqualTo("creep_big"));
        Assert.That(Small(sim).Stats.Id, Is.EqualTo("creep_small"));
        Assert.That(Big(sim).OwnerId, Is.EqualTo(Setups.Creeps));
        Assert.That(Camp(sim).State, Is.EqualTo(CreepCampState.Calm));
    }

    [Test]
    public void GWT1_InfantryComesWithinFiveCells_CampAlerted_BothCreepsTargetIt()
    {
        var sim = Match();
        Unit infantry = Spawn(sim, Setups.PlayerA, "infantry", CX - 7, CY);
        sim.Run(5);
        Assert.That(Camp(sim).State, Is.EqualTo(CreepCampState.Calm));

        sim.Order(sim.Move(Setups.PlayerA, Center(CX - 4), Center(CY), infantry));
        sim.RunUntil(s => Movement.Distance(infantry.X, infantry.Y, Center(CX), Center(CY)) <= Cells(5));
        var alerted = sim.RunCollecting<CampAlerted>(1);
        Assert.That(alerted, Has.Count.EqualTo(1));
        Assert.That(Camp(sim).State, Is.EqualTo(CreepCampState.Alerted));
        Assert.That(Big(sim).TargetId, Is.EqualTo(infantry.Id));
        Assert.That(Small(sim).TargetId, Is.EqualTo(infantry.Id));
    }

    [Test]
    public void GWT2_DrillSiteInRadius_LeftAlone_CreepsAttackIt_OwnerAlarmed()
    {
        var map = new MapSetup(44, 18).OreRect(CX - 5, CY - 1, 2, 2, Resources.Primary);
        var sim = Match(map: map);
        // Nearest cell of the site (x = 16) is 4 cells from the center.
        Building site = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), CX - 5, CY - 1, Direction.Right, complete: false);
        var alarms = sim.RunCollecting<BuildingAttacked>(5 * Simulation.TicksPerSecond);
        Assert.That(alarms.Select(a => a.BuildingId), Does.Contain(site.Id));
        Assert.That(alarms.First().OwnerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(site.Hp, Is.LessThan(site.MaxHp / 10 + 1));
    }

    [Test]
    public void GWT3_TargetRunsBeyondTheLeash_CreepsReturn_CalmAfterFiveSeconds_Regenerate()
    {
        var sim = Match(60);
        Unit infantry = Spawn(sim, Setups.PlayerA, "infantry", CX - 4, CY);
        Unit big = Big(sim);
        sim.RunUntil(s => big.Hp < big.MaxHp - 30);
        sim.Order(sim.Move(Setups.PlayerA, Center(2), Center(CY), infantry));

        sim.RunUntil(s => Camp(s).State == CreepCampState.Returning);
        Assert.That(Movement.Distance(infantry.X, infantry.Y, big.HomeX, big.HomeY), Is.GreaterThan(Cells(7)));
        sim.RunUntil(s => Camp(s).State == CreepCampState.Calm);
        Assert.That(sim.State.Tick - Camp(sim).LastDamageTick, Is.GreaterThanOrEqualTo(5 * Simulation.TicksPerSecond));

        int hp = big.Hp;
        sim.Run(Simulation.TicksPerSecond);
        // 5 % of 500 HP per second = 25 HP/s.
        Assert.That(big.Hp - hp, Is.EqualTo(System.Math.Min(25, big.MaxHp - hp)));
        sim.Run(30 * Simulation.TicksPerSecond);
        Assert.That(big.Hp, Is.EqualTo(big.MaxHp));
    }

    [Test]
    public void GWT4_ThreeInfantryClearTheCamp_TrophyAppears_NoRespawn()
    {
        var sim = Match();
        var squad = new[] { Spawn(sim, Setups.PlayerA, "infantry", CX - 3, CY - 1), Spawn(sim, Setups.PlayerA, "infantry", CX - 3, CY), Spawn(sim, Setups.PlayerA, "infantry", CX - 3, CY + 1) };
        sim.Order(new AttackCommand(Setups.PlayerA, sim.State.Tick, squad.Select(u => u.Id).ToArray(), Camp(sim).CreepIds[1]));

        var cleared = sim.RunCollecting<CampCleared>(60 * Simulation.TicksPerSecond);
        Assert.That(cleared, Has.Count.EqualTo(1));
        Assert.That(Camp(sim).State, Is.EqualTo(CreepCampState.Cleared));
        Assert.That(Camp(sim).TrophyPrimary, Is.EqualTo(60));
        Assert.That(squad.Count(u => !u.IsDead), Is.GreaterThanOrEqualTo(2), "3 infantry are enough (creep-camp.md)");
        Assert.That(sim.State.UnitsOf(Setups.Creeps), Is.Empty);
        sim.Run(120 * Simulation.TicksPerSecond);
        Assert.That(sim.State.UnitsOf(Setups.Creeps), Is.Empty);
    }

    [Test]
    public void GWT5_AllyCarriesTheTrophy_FivePerTrip_AllSixtyToItsOwnHeadquarters()
    {
        var sim = Match();
        Kill(Big(sim));
        Kill(Small(sim));
        sim.Tick();
        Unit worker = sim.State.UnitsOf(Setups.PlayerB).First(u => u.Worker != null);
        int a = sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(Resources.Primary);
        int b = sim.State.GetPlayer(Setups.PlayerB).Wallet.Get(Resources.Primary);

        sim.Order(new CarryTrophyCommand(Setups.PlayerB, sim.State.Tick, new[] { worker.Id }, 0));
        var deliveries = new System.Collections.Generic.List<CargoDelivered>();
        sim.RunUntil(s =>
        {
            deliveries.AddRange(s.Events.OfType<CargoDelivered>());
            return worker.Order.Kind == UnitOrderKind.Idle;
        }, 20 * 60 * 20);

        Assert.That(deliveries, Has.Count.EqualTo(12));
        Assert.That(deliveries.All(d => d.Amount == 5), Is.True);
        Assert.That(sim.State.GetPlayer(Setups.PlayerB).Wallet.Get(Resources.Primary), Is.EqualTo(b + 60));
        Assert.That(sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(Resources.Primary), Is.EqualTo(a));
        Assert.That(Camp(sim).TrophyPrimary, Is.EqualTo(0));
    }

    [Test]
    public void GWT6_TwoAlliedInfantryAtEqualDistance_LowerIdIsTheTarget()
    {
        var sim = Match();
        // Both 3 cells from the small creep at (21,8): (21,11) and (21,5).
        Unit first = Spawn(sim, Setups.PlayerA, "infantry", CX + 1, CY + 3);
        Unit second = Spawn(sim, Setups.PlayerB, "infantry", CX + 1, CY - 3);
        sim.Tick();
        sim.Tick();
        Assert.That(first.Id, Is.LessThan(second.Id));
        Assert.That(Small(sim).TargetId, Is.EqualTo(first.Id));
    }

    [Test]
    public void Edge_UnitAndDrillInRadius_CreepsHitTheUnitFirst()
    {
        var map = new MapSetup(44, 18).OreRect(CX - 4, CY + 2, 2, 2, Resources.Primary);
        var sim = Match(map: map);
        Building drill = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), CX - 4, CY + 2, Direction.Right, complete: true);
        Unit worker = Spawn(sim, Setups.PlayerA, "builder", CX - 3, CY - 2);
        sim.Run(3);
        Assert.That(Big(sim).TargetId, Is.EqualTo(worker.Id));
        sim.RunUntil(s => worker.IsDead);
        sim.RunUntil(s => drill.Hp < drill.MaxHp);
    }

    [Test]
    public void Edge_SiegeOutrangesTheLeash_CreepsCannotReachIt_NoRegenWhileShelled()
    {
        var sim = Match(60);
        Unit small = Small(sim);
        // Siege at the very end of its range (8 cells edge to edge) from the small creep's home.
        Unit siege = Spawn(sim, Setups.PlayerA, "siege", CX + 1, CY);
        siege.X = small.HomeX + Cells(8) + siege.Radius + small.Radius;
        sim.Order(new AttackCommand(Setups.PlayerA, sim.State.Tick, new[] { siege.Id }, small.Id));

        int maxDist = 0, lastHp = small.Hp;
        bool everCalmAfterHit = false, alerted = false;
        sim.Run(15 * Simulation.TicksPerSecond, s =>
        {
            if (small.IsDead)
                return;
            maxDist = System.Math.Max(maxDist, (int)Movement.Distance(small.X, small.Y, small.HomeX, small.HomeY));
            Assert.That(small.Hp, Is.LessThanOrEqualTo(lastHp), "no regeneration while shelled");
            lastHp = small.Hp;
            alerted |= Camp(s).State == CreepCampState.Alerted;
            everCalmAfterHit |= alerted && Camp(s).State == CreepCampState.Calm;
        });
        Assert.That(maxDist, Is.LessThanOrEqualTo(Cells(7)));
        Assert.That(everCalmAfterHit, Is.False);
        Assert.That(small.IsDead, Is.True);
        Assert.That(siege.Hp, Is.EqualTo(siege.MaxHp), "creeps never got to it");
    }

    [Test]
    public void Edge_ShooterHitsFromOutsideTheRadiusButInsideTheLeash_WholeCampRunsAtIt()
    {
        var sim = Match();
        // Heavy: range 4 edge to edge stops it 5.1 cells from the big creep — outside the radius.
        Unit shooter = Spawn(sim, Setups.PlayerA, "heavy", CX - 7, CY);
        sim.Order(new AttackCommand(Setups.PlayerA, sim.State.Tick, new[] { shooter.Id }, Camp(sim).CreepIds[0]));
        sim.RunUntil(s => Camp(s).State == CreepCampState.Alerted);
        Assert.That(Movement.Distance(shooter.X, shooter.Y, Center(CX), Center(CY)), Is.GreaterThan(Cells(5)));
        Assert.That(Big(sim).MaxHp - Big(sim).Hp, Is.GreaterThan(0), "woken by the blow, not by the radius");
        sim.Tick();
        Assert.That(Big(sim).TargetId, Is.EqualTo(shooter.Id));
        Assert.That(Small(sim).TargetId, Is.EqualTo(shooter.Id));
    }

    [Test]
    public void Edge_AAttacks_BStandsInRadius_EachCreepHitsTheNearestUnit()
    {
        var sim = Match();
        Unit a = Spawn(sim, Setups.PlayerA, "infantry", CX - 3, CY);       // near the big creep
        Unit b = Spawn(sim, Setups.PlayerB, "infantry", CX + 4, CY);       // near the small creep
        b.Order = UnitOrder.MoveTo(b.X, b.Y);  // standing, not fighting by itself this tick
        sim.Run(2);
        Assert.That(Big(sim).TargetId, Is.EqualTo(a.Id));
        Assert.That(Small(sim).TargetId, Is.EqualTo(b.Id));
    }

    [Test]
    public void Edge_LastCreepKilledByTwoPlayersInOneTick_TrophyStaysForAnyone()
    {
        var sim = Match();
        Kill(Big(sim));
        Unit small = Small(sim);
        small.Hp = 1;
        Unit a = Spawn(sim, Setups.PlayerA, "infantry", CX + 1, CY + 1);
        Unit b = Spawn(sim, Setups.PlayerB, "infantry", CX + 1, CY - 1);
        int wa = sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(Resources.Primary);
        sim.RunUntil(s => Camp(s).State == CreepCampState.Cleared);
        Assert.That(Camp(sim).TrophyPrimary, Is.EqualTo(60));
        Assert.That(sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(Resources.Primary), Is.EqualTo(wa));
    }

    [Test]
    public void Edge_TwoWorkersTakeTheLastUnitsInOneTick_LowerIdFirst_OtherLeavesEmptyHanded()
    {
        var sim = Match();
        Kill(Big(sim));
        Kill(Small(sim));
        sim.Tick();
        Camp(sim).TrophyPrimary = 5;
        Unit w1 = Spawn(sim, Setups.PlayerA, "builder", CX - 1, CY);
        Unit w2 = Spawn(sim, Setups.PlayerA, "builder", CX + 1, CY);
        sim.Order(new CarryTrophyCommand(Setups.PlayerA, sim.State.Tick, new[] { w2.Id, w1.Id }, 0));
        sim.Tick();
        Assert.That(w1.Worker!.Cargo, Is.EqualTo(5));
        Assert.That(w2.Worker!.Cargo, Is.EqualTo(0));
        Assert.That(w2.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void Edge_ConveyorThroughTheRadius_IsAttacked()
    {
        var sim = Match();
        Building belt = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("conveyor"), CX - 3, CY + 2, Direction.Left, complete: true);
        sim.RunUntil(s => belt.IsDead || belt.Hp < belt.MaxHp);
    }

    [Test]
    public void Edge_LegionSquadThroughTheRadius_IsAttackedToo()
    {
        var sim = Match();
        Unit raider = Spawn(sim, Setups.EnemyAi, "legion_gunner", CX - 3, CY);
        raider.Order = UnitOrder.MoveTo(raider.X, raider.Y);
        sim.Run(3);
        Assert.That(Big(sim).TargetId, Is.EqualTo(raider.Id));
    }

    [Test]
    public void Edge_TargetDies_CreepTakesTheNextOneWithoutGoingHome()
    {
        var sim = Match();
        Unit first = Spawn(sim, Setups.PlayerA, "builder", CX - 2, CY);
        Unit second = Spawn(sim, Setups.PlayerA, "builder", CX - 4, CY);
        sim.Run(2);
        Assert.That(Big(sim).TargetId, Is.EqualTo(first.Id));
        sim.RunUntil(s => first.IsDead);
        sim.Run(2);
        Assert.That(Big(sim).TargetId, Is.EqualTo(second.Id));
        Assert.That(Camp(sim).State, Is.EqualTo(CreepCampState.Alerted));
    }

    [Test]
    public void Edge_UndiscoveredCampEntered_AlertsAnyway_AndIsDiscovered()
    {
        var sim = Match();
        Unit stranger = Spawn(sim, Setups.EnemyAi, "legion_worker", CX - 4, CY);
        Assert.That(sim.State.Fog.IsDiscovered(Setups.EnemyAi, CX, CY), Is.False);
        sim.Run(2);
        Assert.That(Camp(sim).State, Is.EqualTo(CreepCampState.Alerted));
        Assert.That(sim.State.Fog.IsDiscovered(Setups.EnemyAi, CX, CY), Is.True);
    }

    [Test]
    public void CarryTrophyCommand_RoundTripsThroughTheCodec()
    {
        var codec = GameCommands.CreateCodec();
        var read = (CarryTrophyCommand)codec.ReadLog(codec.WriteLog(new ICommand[] { new CarryTrophyCommand(1, 2, new[] { 3 }, 4) }))[0];
        Assert.That((read.CampIndex, read.UnitIds[0]), Is.EqualTo((4, 3)));
    }

    [Test]
    public void Determinism_CampFight_SameHashesTwice()
    {
        Assert.That(Fight(), Is.EqualTo(Fight()));
    }

    private static ulong[] Fight()
    {
        var sim = Match();
        var squad = Enumerable.Range(0, 3).Select(i => Spawn(sim, Setups.PlayerA, "infantry", CX - 7, CY - 1 + i)).ToArray();
        sim.Order(sim.Move(Setups.PlayerA, Center(CX), Center(CY), squad));
        var hashes = new ulong[900];
        for (int i = 0; i < hashes.Length; i++)
        {
            sim.Tick();
            hashes[i] = sim.StateHash;
        }
        return hashes;
    }
}
