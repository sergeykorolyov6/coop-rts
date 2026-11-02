using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/combat.md
public class CombatTests
{
    private static Simulation Empty(int w = 32, int h = 16) => new Simulation(Setups.Coop(new MapSetup(w, h)));

    private static Unit Spawn(Simulation sim, int owner, string type, int cx, int cy) =>
        sim.State.SpawnUnit(owner, sim.State.Data.Unit(type), Center(cx), Center(cy));

    private static AttackCommand Attack(Simulation sim, int player, Entity target, params Unit[] units) =>
        new AttackCommand(player, sim.State.Tick, units.Select(u => u.Id).ToArray(), target.Id);

    [Test]
    public void GWT1_InfantryKillsSmallCreep_InSevenHits_EveryPointEightSeconds()
    {
        var sim = Empty();
        Unit infantry = Spawn(sim, Setups.PlayerA, "infantry", 5, 5);
        // An enemy AI unit with the creep's stats: the creep's own behaviour is creep-camp.md.
        Unit target = Spawn(sim, Setups.EnemyAi, "creep_small", 6, 5);
        sim.Order(Attack(sim, Setups.PlayerA, target, infantry));

        var hpSeen = new System.Collections.Generic.List<int>();
        int ticks = sim.RunUntil(s =>
        {
            if (!target.IsDead && (hpSeen.Count == 0 || hpSeen[^1] != target.Hp))
                hpSeen.Add(target.Hp);
            return s.State.FindUnit(target.Id) == null;
        });

        Assert.That(hpSeen.Take(3), Is.EqualTo(new[] { 68, 56, 44 }));
        // 7 hits, 16 ticks apart: the last lands 6 × 16 ticks after the first.
        Assert.That(ticks, Is.InRange(6 * 16 + 1, 6 * 16 + 10));
    }

    [Test]
    public void GWT2_IdleInfantry_AttacksTheNearestEnemyInSight_ByItself()
    {
        var sim = Empty();
        Unit infantry = Spawn(sim, Setups.PlayerA, "infantry", 5, 5);
        Unit near = Spawn(sim, Setups.EnemyAi, "legion_raider", 9, 5);
        Spawn(sim, Setups.EnemyAi, "legion_raider", 9, 8);  // farther
        sim.Run(5);
        Assert.That(infantry.TargetId, Is.EqualTo(near.Id));
        sim.RunUntil(s => near.Hp < near.MaxHp);
    }

    [Test]
    public void GWT3_EnemyHitsADrill_OwnerAndAllyAlarmed_NotAgainWithinFiveSeconds()
    {
        var map = new MapSetup(32, 16).OreRect(10, 6, 2, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Building drill = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), 10, 6, Direction.Right, complete: true);
        Unit gunner = Spawn(sim, Setups.EnemyAi, "legion_gunner", 14, 6);
        sim.Order(Attack(sim, Setups.EnemyAi, drill, gunner));

        var alarms = sim.RunCollecting<BuildingAttacked>(5 * Simulation.TicksPerSecond);
        Assert.That(alarms, Has.Count.EqualTo(1));
        Assert.That(alarms[0].BuildingId, Is.EqualTo(drill.Id));
        Assert.That(alarms[0].OwnerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(drill.Hp, Is.LessThan(drill.MaxHp));
        // Allies read it through relations: the event names the owner, the UI shows it to the team.
        var more = sim.RunCollecting<BuildingAttacked>(3 * Simulation.TicksPerSecond);
        Assert.That(more, Has.Count.EqualTo(1));
    }

    [Test]
    public void Edge_DamageBelowArmor_DealsOne()
    {
        var sim = Empty();
        Unit worker = Spawn(sim, Setups.PlayerA, "worker", 5, 5);          // damage 4
        Unit brute = Spawn(sim, Setups.EnemyAi, "legion_brute", 6, 5);     // armor 2 → 2
        Unit dummy = Spawn(sim, Setups.EnemyAi, "creep_big", 5, 8);        // armor 2
        sim.Order(Attack(sim, Setups.PlayerA, brute, worker));
        sim.RunUntil(s => brute.Hp < brute.MaxHp || worker.IsDead);
        Assert.That(brute.MaxHp - brute.Hp, Is.EqualTo(2));
        Assert.That(DamageRules.Damage(1, 5), Is.EqualTo(1));
    }

    [Test]
    public void Edge_TwoUnitsKillEachOtherInOneTick_BothDie()
    {
        var sim = Empty();
        Unit a = Spawn(sim, Setups.PlayerA, "infantry", 5, 5);
        Unit b = Spawn(sim, Setups.EnemyAi, "infantry", 6, 5);
        a.Hp = 5;
        b.Hp = 5;
        sim.Order(Attack(sim, Setups.PlayerA, b, a));
        sim.Order(Attack(sim, Setups.EnemyAi, a, b));
        sim.RunUntil(s => a.IsDead || b.IsDead);
        Assert.That(a.IsDead && b.IsDead, Is.True);
    }

    [Test]
    public void Edge_IdleUnitDropsATargetThatLeftItsSight()
    {
        var sim = Empty(64, 16);
        Unit infantry = Spawn(sim, Setups.PlayerA, "infantry", 5, 5);
        Unit scout = Spawn(sim, Setups.EnemyAi, "scout", 9, 5);
        sim.Run(3);
        Assert.That(infantry.TargetId, Is.EqualTo(scout.Id));
        sim.Order(sim.Move(Setups.EnemyAi, Center(60), Center(5), scout));
        sim.Run(120);
        Assert.That(infantry.TargetId, Is.EqualTo(0));
        Assert.That(infantry.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void Edge_IdleWorker_DoesNotAttackByItself()
    {
        var sim = Empty();
        Unit worker = Spawn(sim, Setups.PlayerA, "worker", 5, 5);
        Unit enemy = Spawn(sim, Setups.EnemyAi, "legion_worker", 6, 5);
        sim.Run(60);
        Assert.That(enemy.Hp, Is.EqualTo(enemy.MaxHp));
        Assert.That(worker.TargetId, Is.EqualTo(0));
    }

    [Test]
    public void Fail_AttackingAnAlly_Rejected()
    {
        var sim = Empty();
        Unit a = Spawn(sim, Setups.PlayerA, "infantry", 5, 5);
        Unit b = Spawn(sim, Setups.PlayerB, "infantry", 6, 5);
        sim.Order(Attack(sim, Setups.PlayerA, b, a));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
        sim.Run(40);
        Assert.That(b.Hp, Is.EqualTo(b.MaxHp));
    }

    [Test]
    public void DestroyedBuilding_FreesItsCells_AndRaisesAnEvent()
    {
        var sim = Empty();
        Building conveyor = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("conveyor"), 8, 5, Direction.Left, complete: true);
        Unit raider = Spawn(sim, Setups.EnemyAi, "legion_raider", 9, 5);
        sim.Order(Attack(sim, Setups.EnemyAi, conveyor, raider));
        var died = sim.RunCollecting<EntityDestroyed>(20 * Simulation.TicksPerSecond);
        Assert.That(died.Select(d => d.EntityId), Does.Contain(conveyor.Id));
        Assert.That(sim.State.Map.BuildingAt(8, 5), Is.EqualTo(0));
    }

    [Test]
    public void AttackCommand_RoundTripsThroughTheCodec()
    {
        var codec = GameCommands.CreateCodec();
        var read = (AttackCommand)codec.ReadLog(codec.WriteLog(new ICommand[] { new AttackCommand(1, 2, new[] { 3 }, 4) }))[0];
        Assert.That((read.TargetId, read.UnitIds[0]), Is.EqualTo((4, 3)));
    }

    // combat.md UI: "видно удар" — every applied hit is an event with attacker, target and damage.
    [Test]
    public void Event_HitLanded_OnePerAppliedHit_WithDamage()
    {
        var sim = Empty();
        Unit infantry = Spawn(sim, Setups.PlayerA, "infantry", 5, 5);
        Unit target = Spawn(sim, Setups.EnemyAi, "creep_small", 6, 5);
        sim.Order(Attack(sim, Setups.PlayerA, target, infantry));

        var hits = new System.Collections.Generic.List<HitLanded>();
        sim.RunUntil(s =>
        {
            hits.AddRange(s.Events.OfType<HitLanded>().Where(h => h.AttackerId == infantry.Id));
            return hits.Count >= 2;
        });

        Assert.That(hits[0].TargetId, Is.EqualTo(target.Id));
        Assert.That(hits[0].Damage, Is.EqualTo(12));
        Assert.That(target.Hp, Is.EqualTo(target.MaxHp - 24));
    }
}
