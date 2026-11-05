using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/factions.md — Brown Scavengers and Yellow Star (rows 11–20).
public class ScavengerAndHeroTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Brown = Setups.PlayerA;
    private const int Yellow = Setups.PlayerB;
    private const int Legion = Setups.EnemyAi;

    // Scavengers at (2,2), Yellow Star at (2,14); ore at (20..23, 2..3) and (30, 2).
    internal static Simulation Match()
    {
        var map = new MapSetup(48, 24).Start(Brown, 2, 2).Start(Yellow, 2, 14)
            .OreRect(20, 2, 4, 2, Resources.Primary).Ore(30, 2, Resources.Primary).Ore(34, 2, Resources.Primary);
        return new Simulation(new MatchSetup(42, RepoData.Get(), map, 2, new[] { Brown, Yellow, Legion, Setups.Creeps },
            new[]
            {
                new RelationSetup(Brown, Yellow, Relation.Ally),
                new RelationSetup(Brown, Legion, Relation.Enemy),
                new RelationSetup(Yellow, Legion, Relation.Enemy),
            },
            creepPlayerId: Setups.Creeps,
            factions: new[]
            {
                new PlayerFaction(Brown, "brown_scavengers"), new PlayerFaction(Yellow, "yellow_star"),
                new PlayerFaction(Legion, "black_legion"),
            }));
    }

    private static Building Place(Simulation sim, int owner, string type, int x, int y) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, Direction.Right, complete: true);

    internal static Unit Hero(Simulation sim) => sim.State.FindHero(Yellow)!;

    [Test]
    public void GWT11_SmallDrillOnOneCell_OneUnitInEightSeconds()
    {
        var sim = Match();
        Building drill = Place(sim, Brown, "bs_drill_small", 30, 2);
        sim.Run(8 * Second);
        Assert.That(drill.Drill!.Buffer, Is.EqualTo(1));
    }

    [Test]
    public void GWT12_DrillNextToALubricator_MinesTwiceAsFast()
    {
        var sim = Match();
        Building plain = Place(sim, Brown, "bs_drill_small", 30, 2);
        Building boosted = Place(sim, Brown, "bs_drill_small", 34, 2);
        Place(sim, Brown, "bs_pump", 35, 2);
        sim.Run(32 * Second);
        Assert.That(plain.Drill!.Buffer, Is.EqualTo(4));
        Assert.That(boosted.Drill!.Buffer, Is.EqualTo(8));
    }

    [Test]
    public void GWT13_ScrapGun_FiresOnlyWithAmmo_OneUnitAShot()
    {
        var sim = Match();
        Building gun = Place(sim, Brown, "bs_scrap_gun", 20, 10);
        Unit target = Spawn(sim, Legion, "legion_brute", 25, 11);
        target.Order = UnitOrder.Idle;
        var hits = sim.RunCollecting<HitLanded>(2 * Second).Where(h => h.AttackerId == gun.Id).ToList();
        Assert.That(hits, Is.Empty);
        gun.Stock!.Add(Resources.Primary, 3);
        hits = sim.RunCollecting<HitLanded>(5 * Second).Where(h => h.AttackerId == gun.Id).ToList();
        Assert.That(hits, Has.Count.EqualTo(3));
        Assert.That(gun.Stock.Get(Resources.Primary), Is.EqualTo(0));
    }

    [Test]
    public void GWT14_ScavengerDemolition_RefundsTheWholePrice()
    {
        var sim = Match();
        Building drill = Place(sim, Brown, "drill", 20, 2);
        int before = sim.State.GetPlayer(Brown).Wallet.Get(Resources.Primary);
        sim.Order(new DemolishCommand(Brown, sim.State.Tick, drill.Id));
        sim.RunUntil(s => s.State.FindBuilding(drill.Id) == null);
        Assert.That(sim.State.GetPlayer(Brown).Wallet.Get(Resources.Primary) - before, Is.EqualTo(30));
    }

    [Test]
    public void GWT15_YellowMission_BuilderAndLevelOneHero()
    {
        Simulation sim = RepoData.Launch("m2_yellow");
        Assert.That(sim.State.FindBuilder(1)!.Stats.Id, Is.EqualTo("ys_builder"));
        Unit hero = sim.State.FindHero(1)!;
        Assert.That(hero.Stats.Id, Is.EqualTo("ys_hero"));
        Assert.That(sim.State.GetPlayer(1).HeroLevel, Is.EqualTo(1));
        Assert.That(RepoData.Launch("m2_brown").State.FindHeadquarters(1)!.Stats.Id, Is.EqualTo("bs_hq"));
    }

    [Test]
    public void GWT16_EnemyDiesNearTheHero_ItGainsTwentyPercentOfItsHp()
    {
        var sim = Match();
        Unit hero = Hero(sim);
        Unit gunner = Spawn(sim, Legion, "legion_gunner", hero.CellX + 4, hero.CellY);
        Kill(gunner);
        gunner.IsDead = false;
        gunner.Hp = 1;
        sim.State.PendingHits.Add(new PendingHit(hero.Id, gunner.Id, 5));
        Combat.ApplyHits(sim.State, new System.Collections.Generic.List<SimEvent>());
        Assert.That(sim.State.GetPlayer(Yellow).HeroXp, Is.EqualTo(18));
    }

    [Test]
    public void GWT17_HundredXp_LevelTwo_MoreHpAndDamage()
    {
        var sim = Match();
        Unit hero = Hero(sim);
        var events = new System.Collections.Generic.List<SimEvent>();
        GainXp(sim, hero, 100, events);
        Assert.That(sim.State.GetPlayer(Yellow).HeroLevel, Is.EqualTo(2));
        Assert.That(events.OfType<HeroLevelUp>().Single().Level, Is.EqualTo(2));
        Assert.That(hero.MaxHp, Is.EqualTo(483));
        Assert.That(hero.Hp, Is.EqualTo(483), "a full hero stays full");
        Assert.That(HeroSystem.DamageBonusPercent(sim.State, hero), Is.EqualTo(15));
    }

    [Test]
    public void GWT18_LevelTwoHeroDies_ComesBackInThirtySeconds_StillLevelTwo_FullHp()
    {
        var sim = Match();
        GainXp(sim, Hero(sim), 100, new System.Collections.Generic.List<SimEvent>());
        Kill(Hero(sim));
        sim.Tick();
        Assert.That(sim.Events.OfType<HeroDied>().Single().RespawnTick, Is.EqualTo(sim.State.Tick - 1 + 30 * Second));
        sim.RunUntil(s => s.State.FindHero(Yellow) != null, 31 * Second);
        Unit back = Hero(sim);
        Assert.That(sim.State.GetPlayer(Yellow).HeroLevel, Is.EqualTo(2));
        Assert.That((back.Hp, back.MaxHp), Is.EqualTo((483, 483)));
    }

    [Test]
    public void GWT20_ArcherNearTheHero_HitsHarder_FarFromIt_Normal()
    {
        var sim = Match();
        Unit hero = Hero(sim);
        hero.X = Center(20);
        hero.Y = Center(14);
        Unit near = Spawn(sim, Yellow, "ys_archer", 26, 14);
        Unit far = Spawn(sim, Yellow, "ys_archer", 28, 14);
        Assert.That(HeroSystem.DamageBonusPercent(sim.State, near), Is.EqualTo(20));
        Assert.That(HeroSystem.DamageBonusPercent(sim.State, far), Is.EqualTo(0));
        Unit target = Spawn(sim, Legion, "legion_raider", 30, 14);
        sim.Order(new AttackCommand(Yellow, sim.State.Tick, new[] { near.Id }, target.Id));
        var hit = sim.RunCollecting<HitLanded>(2 * Second).First(h => h.AttackerId == near.Id);
        Assert.That(hit.Damage, Is.EqualTo(10));
    }

    [Test]
    public void Edge_TwoLubricators_StillTwice()
    {
        var sim = Match();
        Building drill = Place(sim, Brown, "bs_drill_small", 34, 2);
        Place(sim, Brown, "bs_pump", 35, 2);
        Place(sim, Brown, "bs_pump", 33, 2);
        sim.Run(32 * Second);
        Assert.That(drill.Drill!.Buffer, Is.EqualTo(8));
    }

    [Test]
    public void Edge_NoXpForBuildings_XpForCreeps()
    {
        var sim = Match();
        Unit hero = Hero(sim);
        Building drill = Place(sim, Legion, "drill", hero.CellX + 3, hero.CellY - 2);
        drill.Hp = 1;
        sim.State.PendingHits.Add(new PendingHit(hero.Id, drill.Id, 5));
        Combat.ApplyHits(sim.State, new System.Collections.Generic.List<SimEvent>());
        Assert.That(sim.State.GetPlayer(Yellow).HeroXp, Is.EqualTo(0));
        Unit creep = Spawn(sim, Setups.Creeps, "creep_big", hero.CellX + 3, hero.CellY);
        creep.Hp = 1;
        sim.State.PendingHits.Add(new PendingHit(hero.Id, creep.Id, 5));
        Combat.ApplyHits(sim.State, new System.Collections.Generic.List<SimEvent>());
        Assert.That(sim.State.GetPlayer(Yellow).HeroXp, Is.EqualTo(100));
    }

    [Test]
    public void Edge_HeroDead_NoAura()
    {
        var sim = Match();
        Unit archer = Spawn(sim, Yellow, "ys_archer", 4, 12);
        Assert.That(HeroSystem.DamageBonusPercent(sim.State, archer), Is.EqualTo(20));
        Kill(Hero(sim));
        Assert.That(HeroSystem.DamageBonusPercent(sim.State, archer), Is.EqualTo(0));
    }

    [Test]
    public void Edge_HeroCannotBeProduced_OnlyOne()
    {
        var sim = Match();
        Assert.That(sim.State.Data.Building("ys_barracks").Produces, Does.Not.Contain("ys_hero"));
        Assert.That(sim.State.UnitsOf(Yellow).Count(u => u.IsHero), Is.EqualTo(1));
    }

    [Test]
    public void Edge_AmmoTurretWithoutStock_IsALoadError()
    {
        GameData data = RepoData.Get();
        Assert.That(data.Building("bs_scrap_gun").SuppliedByConveyor, Is.True);
        Assert.That(data.Building("bs_scrap_gun").Ammo, Is.EqualTo(1));
    }

    [Test]
    public void Determinism_HeroAndScavengers_SameHashEveryTick()
    {
        ulong[] Play()
        {
            var sim = Match();
            Place(sim, Brown, "bs_drill_small", 30, 2);
            Place(sim, Brown, "bs_pump", 31, 2);
            Unit hero = Hero(sim);
            for (int i = 0; i < 6; i++)
                Spawn(sim, Legion, "legion_gunner", hero.CellX + 6 + i % 2, hero.CellY + i / 2);
            var hashes = new System.Collections.Generic.List<ulong>();
            sim.Run(40 * Second, s => hashes.Add(s.StateHash));
            return hashes.ToArray();
        }
        Assert.That(Play(), Is.EqualTo(Play()));
    }

    // As if enemies with that much HP × 5 died next to the hero.
    internal static void GainXp(Simulation sim, Unit hero, int xp, System.Collections.Generic.List<SimEvent> events)
    {
        while (sim.State.GetPlayer(Yellow).HeroXp < xp)
        {
            Unit victim = Spawn(sim, Legion, "creep_big", hero.CellX + 2, hero.CellY);
            victim.Hp = 0;
            victim.IsDead = true;
            HeroSystem.OnUnitDied(sim.State, victim, events);
        }
    }
}
