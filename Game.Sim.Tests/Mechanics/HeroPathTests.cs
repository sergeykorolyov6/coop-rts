using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/factions.md — the hero's paths (rows 17a–17d, 19, 19a, 19b).
public class HeroPathTests
{
    private const int Yellow = Setups.PlayerB;
    private const int Brown = Setups.PlayerA;
    private const int Legion = Setups.EnemyAi;

    private static Simulation Match() => ScavengerAndHeroTests.Match();

    private static Unit Hero(Simulation sim) => ScavengerAndHeroTests.Hero(sim);

    private static void Path(Simulation sim, string path)
    {
        sim.Order(new ChooseHeroPathCommand(Yellow, sim.State.Tick, path));
        sim.Tick();
    }

    private static void ToLevel(Simulation sim, int level)
    {
        int xp = sim.State.Data.HeroLevels[level - 1].Xp;
        ScavengerAndHeroTests.GainXp(sim, Hero(sim), xp, new List<SimEvent>());
    }

    private static List<HitLanded> Use(Simulation sim, Unit hero, string ability, int cx, int cy)
    {
        sim.Order(new UseAbilityCommand(Yellow, sim.State.Tick, new[] { hero.Id }, ability, Center(cx), Center(cy)));
        return sim.RunCollecting<HitLanded>(1).Where(h => h.AttackerId == hero.Id).ToList();
    }

    private static void Place(Unit hero, int cx, int cy)
    {
        hero.X = Center(cx);
        hero.Y = Center(cy);
    }

    [Test]
    public void GWT17a_NoPath_NoAbility()
    {
        var sim = Match();
        Unit hero = Hero(sim);
        sim.Order(new UseAbilityCommand(Yellow, sim.State.Tick, new[] { hero.Id }, "ys_heal", hero.X, hero.Y));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.AbilityNotReady));
    }

    [Test]
    public void GWT17b_Mage_BoltFromLevelOne_StarfallWaitsForLevelThree()
    {
        var sim = Match();
        Path(sim, "ys_mage");
        Unit hero = Hero(sim);
        Place(hero, 20, 14);
        Unit gunner = Spawn(sim, Legion, "legion_gunner", 26, 14);
        List<HitLanded> hits = Use(sim, hero, "ys_bolt", 26, 15);
        Assert.That(hits.Single().TargetId, Is.EqualTo(gunner.Id));
        Assert.That(hits.Single().Damage, Is.EqualTo(90));
        sim.Order(new UseAbilityCommand(Yellow, sim.State.Tick, new[] { hero.Id }, "ys_starfall", Center(26), Center(14)));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.AbilityNotReady));
        Assert.That(sim.State.Data.TryGetAbility("ys_heal", out AbilityStats? heal) && AbilitySystem.IsReady(sim.State, hero, heal!), Is.False,
            "another path's ability stays closed");
    }

    [Test]
    public void GWT17c_PathChosenOnce()
    {
        var sim = Match();
        Path(sim, "ys_mage");
        sim.Order(new ChooseHeroPathCommand(Yellow, sim.State.Tick, "ys_warrior"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.HeroPathChosen));
        Assert.That(sim.State.GetPlayer(Yellow).HeroPathId, Is.EqualTo("ys_mage"));
    }

    [Test]
    public void GWT17d_WarriorLevelThree_HpGrowsByThePathToo()
    {
        var sim = Match();
        Path(sim, "ys_warrior");
        ToLevel(sim, 3);
        Assert.That(Hero(sim).MaxHp, Is.EqualTo(630));
        // Damage: level 3 gives +30 %, the path +5 % × 2.
        Assert.That(HeroSystem.DamageBonusPercent(sim.State, Hero(sim)), Is.EqualTo(40));
    }

    [Test]
    public void GWT19_MageStarfall_EveryEnemyInTheArea_NotTheAlly()
    {
        var sim = Match();
        Path(sim, "ys_mage");
        ToLevel(sim, 3);
        Unit hero = Hero(sim);
        Place(hero, 20, 14);
        Unit[] enemies = { Spawn(sim, Legion, "legion_brute", 27, 14), Spawn(sim, Legion, "legion_brute", 26, 16), Spawn(sim, Legion, "legion_brute", 25, 14) };
        Unit ally = Spawn(sim, Brown, "bs_scrapper", 26, 14);
        Unit far = Spawn(sim, Legion, "legion_brute", 31, 14);
        List<HitLanded> hits = Use(sim, hero, "ys_starfall", 26, 14);
        // 70 − armor 2 each; the ally among them and the brute 5 cells away are not touched.
        Assert.That(hits.Where(h => h.Damage == 68).Select(h => h.TargetId), Is.EquivalentTo(enemies.Select(e => e.Id)));
        Assert.That(hits.Any(h => h.TargetId == ally.Id || h.TargetId == far.Id), Is.False);
    }

    [Test]
    public void GWT19a_SupporterHeal_OwnAndAllies_CappedAtMax_NotEnemies()
    {
        var sim = Match();
        Path(sim, "ys_support");
        Unit hero = Hero(sim);
        Place(hero, 20, 14);
        Unit squire = Spawn(sim, Yellow, "ys_squire", 24, 14).NoSelfRepair();
        Unit nearlyFull = Spawn(sim, Yellow, "ys_archer", 24, 15).NoSelfRepair();
        Unit ally = Spawn(sim, Brown, "bs_scrapper", 23, 14).NoSelfRepair();
        Unit enemy = Spawn(sim, Legion, "legion_brute", 26, 12).NoSelfRepair();
        squire.Hp = 50;
        nearlyFull.Hp = 70;
        ally.Hp = 10;
        enemy.Hp = 100;
        sim.Order(new UseAbilityCommand(Yellow, sim.State.Tick, new[] { hero.Id }, "ys_heal", Center(24), Center(14)));
        var used = sim.RunCollecting<AbilityUsed>(1);
        Assert.That((squire.Hp, nearlyFull.Hp, ally.Hp), Is.EqualTo((130, 80, 90)));
        Assert.That(enemy.Hp, Is.LessThanOrEqualTo(100));
        Assert.That(used.Single().AbilityId, Is.EqualTo("ys_heal"));
        Assert.That((used.Single().X, used.Single().Y), Is.EqualTo((Center(24), Center(14))));
    }

    [Test]
    public void GWT19b_WarriorCharge_LandsAndHitsAround()
    {
        var sim = Match();
        Path(sim, "ys_warrior");
        Unit hero = Hero(sim);
        Place(hero, 20, 14);
        Unit near = Spawn(sim, Legion, "legion_gunner", 25, 15);
        Unit far = Spawn(sim, Legion, "legion_gunner", 25, 18);
        List<HitLanded> hits = Use(sim, hero, "ys_charge", 25, 14);
        Assert.That((hero.CellX, hero.CellY), Is.EqualTo((25, 14)));
        Assert.That(hits.Where(h => h.TargetId == near.Id).Select(h => h.Damage), Does.Contain(40));
        Assert.That(hits.Any(h => h.TargetId == far.Id), Is.False);
    }

    [Test]
    public void Edge_PointTooFar_PulledInToTheRange()
    {
        var sim = Match();
        Path(sim, "ys_support");
        Unit hero = Hero(sim);
        Place(hero, 20, 14);
        sim.Order(new UseAbilityCommand(Yellow, sim.State.Tick, new[] { hero.Id }, "ys_heal", Center(40), Center(14)));
        AbilityUsed used = sim.RunCollecting<AbilityUsed>(1).Single();
        Assert.That(used.X - hero.X, Is.EqualTo(Cells(7)));
    }

    [Test]
    public void Edge_StrikeWithNobodyNear_NotUsed_NoCooldown()
    {
        var sim = Match();
        Path(sim, "ys_mage");
        Unit hero = Hero(sim);
        Place(hero, 20, 14);
        sim.Order(new UseAbilityCommand(Yellow, sim.State.Tick, new[] { hero.Id }, "ys_bolt", Center(26), Center(14)));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.AbilityNotReady));
        Assert.That(hero.AbilityCooldown(3), Is.EqualTo(0));
    }

    [Test]
    public void Edge_PathOfAnotherHero_OrNoHeroFaction_Rejected()
    {
        var sim = Match();
        sim.Order(new ChooseHeroPathCommand(Brown, sim.State.Tick, "ys_mage"));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
    }

    [Test]
    public void Edge_PathKeptAfterDeath()
    {
        var sim = Match();
        Path(sim, "ys_warrior");
        Kill(Hero(sim));
        sim.RunUntil(s => s.State.FindHero(Yellow) != null, 31 * Simulation.TicksPerSecond);
        Assert.That(sim.State.GetPlayer(Yellow).HeroPathId, Is.EqualTo("ys_warrior"));
    }
}
