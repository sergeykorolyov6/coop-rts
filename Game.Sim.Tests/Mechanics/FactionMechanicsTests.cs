using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/factions.md
public class FactionMechanicsTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Red = Setups.PlayerB;
    private const int Blue = Setups.PlayerA;
    private const int Legion = Setups.EnemyAi;

    // Blue Walls at (2,2), Red Lightning at (2,12), the enemy AI plays the Black Legion's units.
    private static Simulation Match()
    {
        var map = new MapSetup(48, 24).Start(Blue, 2, 2).Start(Red, 2, 14);
        return new Simulation(new MatchSetup(42, RepoData.Get(), map, 2, new[] { Blue, Red, Legion, Setups.Creeps },
            new[]
            {
                new RelationSetup(Blue, Red, Relation.Ally),
                new RelationSetup(Blue, Legion, Relation.Enemy),
                new RelationSetup(Red, Legion, Relation.Enemy),
            },
            creepPlayerId: Setups.Creeps,
            factions: new[] { new PlayerFaction(Red, "red_lightning"), new PlayerFaction(Legion, "black_legion") }));
    }

    private static Building Place(Simulation sim, int owner, string type, int x, int y) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, Direction.Right, complete: true);

    private static UseAbilityCommand Use(Simulation sim, Unit unit, string ability, int cx = 0, int cy = 0) =>
        new UseAbilityCommand(unit.OwnerId, sim.State.Tick, new[] { unit.Id }, ability, Center(cx), Center(cy));

    [Test]
    public void GWT1_BlueBuilding_NotHitForTenSeconds_RepairsOnePercentASecond()
    {
        var sim = Match();
        Building turret = Place(sim, Blue, "turret", 20, 4);
        turret.Hp = 200;
        sim.Run(Second);
        Assert.That(turret.Hp, Is.EqualTo(204));
    }

    [Test]
    public void GWT2_BlueBuilding_HitRecently_WaitsBeforeRepairing()
    {
        var sim = Match();
        Building turret = Place(sim, Blue, "turret", 20, 4);
        turret.Hp = 200;
        turret.LastDamagedTick = sim.State.Tick - 5 * Second;
        sim.Run(Second);
        Assert.That(turret.Hp, Is.EqualTo(200));
        sim.Run(5 * Second);
        Assert.That(turret.Hp, Is.GreaterThan(200));
    }

    [Test]
    public void GWT3_RedUnit_NotHitForFourSeconds_HealsThreePercentASecond()
    {
        var sim = Match();
        Unit runner = Spawn(sim, Red, "rl_runner", 20, 18);
        runner.Hp = 35;
        sim.Run(Second);
        // 3 % of 70 = 2.1 HP a second, exactly accumulated.
        Assert.That(runner.Hp, Is.EqualTo(37));
        Unit infantry = Spawn(sim, Blue, "infantry", 30, 4);
        infantry.Hp = 100;
        sim.Run(Second);
        Assert.That(infantry.Hp, Is.EqualTo(100), "Blue Walls' units do not heal by themselves");
    }

    [Test]
    public void GWT4_RedFirstStrike_DoubleDamage_ThenNormal()
    {
        var sim = Match();
        Unit striker = Spawn(sim, Red, "rl_striker", 20, 18);
        Unit target = Spawn(sim, Legion, "legion_brute", 23, 18);
        sim.Order(new AttackCommand(Red, sim.State.Tick, new[] { striker.Id }, target.Id));
        var hits = sim.RunCollecting<HitLanded>(2 * Second).Where(h => h.AttackerId == striker.Id).ToList();
        // 10 × 2 − armor 2 = 18 first, then 10 − 2 = 8.
        Assert.That(hits.Select(h => h.Damage).Take(2), Is.EqualTo(new[] { 18, 8 }));
    }

    [Test]
    public void GWT5_Blink_ToAPointWithinRange_CooldownStarts()
    {
        var sim = Match();
        Unit striker = Spawn(sim, Red, "rl_striker", 20, 18);
        sim.Order(Use(sim, striker, "rl_blink", 24, 18));
        sim.Tick();
        Assert.That((striker.CellX, striker.CellY), Is.EqualTo((24, 18)));
        Assert.That(striker.AbilityCooldown(0), Is.EqualTo(8 * Second - 1));
        sim.Order(Use(sim, striker, "rl_blink", 20, 18));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.AbilityNotReady));
    }

    [Test]
    public void GWT6_Blink_TooFar_StopsAtTheRange()
    {
        var sim = Match();
        Unit striker = Spawn(sim, Red, "rl_striker", 20, 18);
        int x = striker.X;
        sim.Order(Use(sim, striker, "rl_blink", 30, 18));
        sim.Tick();
        Assert.That(striker.X - x, Is.EqualTo(Cells(6)));
    }

    [Test]
    public void GWT7_Overdrive_PaysTenPercent_FasterForFiveSeconds()
    {
        var sim = Match();
        Unit runner = Spawn(sim, Red, "rl_runner", 20, 18);
        int speed = runner.MoveSpeedMilliPerTick, cooldown = runner.AttackCooldownTicks;
        sim.Order(Use(sim, runner, "rl_overdrive"));
        sim.Tick();
        Assert.That(runner.Hp, Is.EqualTo(63));
        Assert.That(runner.MoveSpeedMilliPerTick, Is.EqualTo(speed * 3 / 2));
        Assert.That(runner.AttackCooldownTicks, Is.EqualTo(cooldown * 100 / 150));
        sim.Run(5 * Second);
        Assert.That(runner.MoveSpeedMilliPerTick, Is.EqualTo(speed));
    }

    [Test]
    public void GWT8_LegionKillsADrill_FrenzyAndBounty()
    {
        var sim = Match();
        Building drill = Place(sim, Blue, "drill", 30, 4);
        drill.Hp = 1;
        Unit raider = Spawn(sim, Legion, "legion_raider", 32, 5);
        int wallet = sim.State.GetPlayer(Legion).Wallet.Get(Resources.Primary);
        sim.Order(new AttackCommand(Legion, sim.State.Tick, new[] { raider.Id }, drill.Id));
        sim.RunUntil(s => drill.IsDead || s.State.FindBuilding(drill.Id) == null, 5 * Second);
        Assert.That(raider.BoostTicksLeft, Is.InRange(4 * Second - 2, 4 * Second));
        Assert.That(raider.BoostAttackPercent, Is.EqualTo(40));
        Assert.That(sim.State.GetPlayer(Legion).Wallet.Get(Resources.Primary) - wallet, Is.EqualTo(7));
    }

    [Test]
    public void GWT9_CoopMission_BlueWallsAndRedLightning()
    {
        Simulation sim = CoopMissionTests.Launch("coop");
        SimState s = sim.State;
        Assert.That(s.GetPlayer(1).Faction!.Id, Is.EqualTo("blue_walls"));
        Assert.That(s.GetPlayer(2).Faction!.Id, Is.EqualTo("red_lightning"));
        Assert.That(s.FindHeadquarters(2)!.Stats.Id, Is.EqualTo("rl_hq"));
        Assert.That(s.FindBuilder(2)!.Stats.Id, Is.EqualTo("rl_builder"));
        Assert.That(s.FindBuilder(1)!.Stats.Id, Is.EqualTo("builder"));
    }

    [Test]
    public void GWT10_LegionMission_ForRedLightning_SameLegionBase()
    {
        Simulation blue = CoopMissionTests.Launch("m2"), red = CoopMissionTests.Launch("m2_red");
        Assert.That(red.State.GetPlayer(1).Faction!.Id, Is.EqualTo("red_lightning"));
        Assert.That(red.State.GetPlayer(MissionLauncher.LegionPlayerId).Faction!.Id, Is.EqualTo("black_legion"));
        Assert.That(red.State.Buildings.Count(b => b.OwnerId == MissionLauncher.LegionPlayerId),
            Is.EqualTo(blue.State.Buildings.Count(b => b.OwnerId == MissionLauncher.LegionPlayerId)));
        Assert.That(red.State.FindHeadquarters(MissionLauncher.LegionPlayerId)!.Stats.Id, Is.EqualTo("legion_hq"));
    }

    [Test]
    public void Edge_BlinkIntoRock_LandsOnTheLastFreeCellOnTheLine()
    {
        var map = new MapSetup(48, 24).Start(Red, 2, 14);
        for (int y = 0; y < 24; y++)
            map.Block(25, y);
        var sim = new Simulation(new MatchSetup(1, RepoData.Get(), map, 2, new[] { Red, Setups.Creeps }, System.Array.Empty<RelationSetup>(),
            creepPlayerId: Setups.Creeps, factions: new[] { new PlayerFaction(Red, "red_lightning") }));
        Unit striker = Spawn(sim, Red, "rl_striker", 22, 18);
        sim.Order(Use(sim, striker, "rl_blink", 25, 18));
        sim.Tick();
        Assert.That(striker.CellX, Is.EqualTo(24));
    }

    [Test]
    public void Edge_OverdriveAtTheCostInHp_DoesNotStart()
    {
        var sim = Match();
        Unit runner = Spawn(sim, Red, "rl_runner", 20, 18);
        runner.Hp = 7;
        runner.NoSelfRepair();
        sim.Order(Use(sim, runner, "rl_overdrive"));
        sim.Tick();
        Assert.That(runner.Hp, Is.EqualTo(7));
        Assert.That(runner.BoostTicksLeft, Is.EqualTo(0));
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.AbilityNotReady));
    }

    [Test]
    public void Edge_UnitWithoutTheAbility_Rejected()
    {
        var sim = Match();
        Unit infantry = Spawn(sim, Blue, "infantry", 20, 4);
        sim.Order(Use(sim, infantry, "rl_blink", 24, 4));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.AbilityNotReady));
    }

    [Test]
    public void Edge_DemolishedOrUnfinished_NoSelfRepair_NoBountyForDemolition()
    {
        var sim = Match();
        Building site = sim.State.PlaceBuilding(Blue, sim.State.Data.Building("turret"), 20, 4, Direction.Right, complete: false);
        int siteHp = site.Hp;
        Building turret = Place(sim, Blue, "turret", 24, 4);
        turret.Hp = 200;
        sim.Order(new DemolishCommand(Blue, sim.State.Tick, turret.Id));
        sim.Run(Second);
        Assert.That(site.Hp, Is.EqualTo(siteHp));
        Assert.That(turret.Hp, Is.EqualTo(200));
    }

    [Test]
    public void Edge_FrenzyAgain_RefreshesTheTime()
    {
        var sim = Match();
        Unit raider = Spawn(sim, Legion, "legion_raider", 30, 10);
        AbilitySystem.ApplyBoost(raider, sim.State.Data.Ability("legion_frenzy"));
        sim.Run(Second);
        AbilitySystem.ApplyBoost(raider, sim.State.Data.Ability("legion_frenzy"));
        Assert.That(raider.BoostTicksLeft, Is.EqualTo(4 * Second));
    }

    [Test]
    public void Edge_RedBuilder_ComesBackInTenSeconds_RedBuildsOnlyItsList()
    {
        var sim = Match();
        Kill(sim.State.FindBuilder(Red)!);
        int ticks = sim.RunUntil(s => s.State.FindBuilder(Red) != null);
        // The first tick notices the death, ten seconds later it is back.
        Assert.That(ticks, Is.EqualTo(10 * Second + 1));
        Assert.That(sim.State.GetPlayer(Red).CanBuild("turret"), Is.False);
        Assert.That(sim.State.GetPlayer(Red).CanBuild("rl_tesla"), Is.True);
        Assert.That(sim.State.GetPlayer(Blue).CanBuild("wall"), Is.True);
        Assert.That(sim.State.GetPlayer(Red).CanBuild("wall"), Is.False);
    }

    [Test]
    public void Determinism_AbilitiesAndTraits_SameHashEveryTick()
    {
        ulong[] Play()
        {
            var sim = Match();
            Unit striker = Spawn(sim, Red, "rl_striker", 20, 18);
            Unit runner = Spawn(sim, Red, "rl_runner", 20, 17);
            Spawn(sim, Legion, "legion_gunner", 30, 18);
            var hashes = new System.Collections.Generic.List<ulong>();
            sim.Order(Use(sim, runner, "rl_overdrive"));
            sim.Order(Use(sim, striker, "rl_blink", 25, 18));
            sim.Run(30 * Second, s => hashes.Add(s.StateHash));
            return hashes.ToArray();
        }
        Assert.That(Play(), Is.EqualTo(Play()));
    }
}
