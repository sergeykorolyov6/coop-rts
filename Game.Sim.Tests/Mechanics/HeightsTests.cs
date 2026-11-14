using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/heights.md
public class HeightsTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int A = Setups.PlayerA, B = Setups.PlayerB, Enemy = Setups.EnemyAi;

    // 24×16: x < 12 is level 0, x ≥ 12 the plateau of level 1, a cliff between them along x = 12.
    // A ramp of level 0, two cells wide, at (11, 13) and (11, 14) — ten cells from the action at y ≈ 3.
    private static MapSetup Cliff(bool ramp = true)
    {
        var map = new MapSetup(24, 16);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 12; x < 24; x++)
                map.Level(x, y, 1);
        }
        if (ramp)
            map.Level(11, 13, 0, ramp: true).Level(11, 14, 0, ramp: true);
        return map;
    }

    // Columns from x on get the given levels ('a'… — a ramp), the last one runs to the right edge.
    private static MapSetup Steps(int from, string levels)
    {
        var map = new MapSetup(24, 8);
        for (int y = 0; y < 8; y++)
        {
            for (int x = from; x < 24; x++)
            {
                char c = levels[System.Math.Min(x - from, levels.Length - 1)];
                if (c >= 'a')
                    map.Level(x, y, c - 'a', ramp: true);
                else
                    map.Level(x, y, c - '0');
            }
        }
        return map;
    }

    private static Simulation Sim(MapSetup map) => new Simulation(Setups.Coop(map));

    private static Simulation SimOf(MapSetup map, string factionA) =>
        new Simulation(new MatchSetup(42, RepoData.Get(), map, 2, new[] { A, B, Enemy, Setups.Creeps },
            new[] { new RelationSetup(A, B, Relation.Ally), new RelationSetup(A, Enemy, Relation.Enemy), new RelationSetup(B, Enemy, Relation.Enemy) },
            creepPlayerId: Setups.Creeps, factions: new[] { new PlayerFaction(A, factionA) }));

    private static Building Place(Simulation sim, int owner, string type, int x, int y, Direction dir = Direction.Right)
    {
        Building b = sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, dir, complete: true);
        sim.State.Fog.Update(sim.State);
        return b;
    }

    private static bool Seen(Simulation sim, int player, int x, int y) => sim.State.Fog.IsVisible(player, x, y);

    private static List<HitLanded> Hits(Simulation sim, int ticks, Entity by, Entity on) =>
        sim.RunCollecting<HitLanded>(ticks).Where(h => h.AttackerId == by.Id && h.TargetId == on.Id).ToList();

    private static void Attack(Simulation sim, Unit unit, Entity target) =>
        sim.Order(new AttackCommand(unit.OwnerId, sim.State.Tick, new[] { unit.Id }, target.Id));

    // A unit that never fights back: an enemy repairer.
    private static Unit Dummy(Simulation sim, int cx, int cy) => Spawn(sim, Enemy, "repairer", cx, cy);

    // ---------- walking ----------

    [Test]
    public void GWT1_PointJustAboveTheCliff_WalksRoundByTheRamp_NeverStepsOverTheCliff()
    {
        var sim = Sim(Cliff());
        MapState map = sim.State.Map;
        Unit unit = Spawn(sim, A, "infantry", 10, 3);
        sim.Order(sim.Move(A, Center(12), Center(3), unit));
        (int x, int y) = (unit.CellX, unit.CellY);
        bool viaRamp = false;
        sim.RunUntil(s =>
        {
            if ((unit.CellX, unit.CellY) != (x, y))
                Assert.That(map.CanStep(x, y, unit.CellX, unit.CellY), Is.True, $"step ({x}, {y}) → ({unit.CellX}, {unit.CellY})");
            (x, y) = (unit.CellX, unit.CellY);
            viaRamp |= map.IsRamp(x, y);
            return unit.Order.Kind == UnitOrderKind.Idle;
        }, 60 * Second);
        Assert.That((unit.CellX, unit.CellY), Is.EqualTo((12, 3)));
        Assert.That(viaRamp, Is.True);
    }

    [Test]
    public void GWT2_PlateauWithoutARamp_GoesToTheNearestCellBelow_AndStops()
    {
        var sim = Sim(Cliff(ramp: false));
        Unit unit = Spawn(sim, A, "infantry", 6, 3);
        sim.Order(sim.Move(A, Center(14), Center(3), unit));
        sim.RunUntil(s => unit.Order.Kind == UnitOrderKind.Idle, 30 * Second);
        Assert.That(unit.CellX, Is.EqualTo(11), "as close as the cliff lets it");
        Assert.That(sim.State.Map.LevelAt(unit.CellX, unit.CellY), Is.EqualTo(0));
    }

    [Test]
    public void GWT3_RampOfLevelOne_DoesNotLetInFromLevelZero()
    {
        // x < 8: level 0, x = 8: a ramp between 1 and 2, x ≥ 9: level 2.
        var sim = Sim(Steps(8, "b2"));
        Assert.That(sim.State.Map.CanStep(7, 3, 8, 3), Is.False);
        Assert.That(sim.State.Map.CanStep(8, 3, 9, 3), Is.True);
        Unit unit = Spawn(sim, A, "infantry", 5, 3);
        sim.Order(sim.Move(A, Center(12), Center(3), unit));
        sim.RunUntil(s => unit.Order.Kind == UnitOrderKind.Idle, 30 * Second);
        Assert.That(unit.CellX, Is.EqualTo(7));
    }

    [Test]
    public void GWT4_TwoRampsInARow_ClimbTwoLevels()
    {
        var sim = Sim(Steps(8, "ab2"));
        Unit unit = Spawn(sim, A, "infantry", 5, 3);
        sim.Order(sim.Move(A, Center(12), Center(3), unit));
        sim.RunUntil(s => unit.Order.Kind == UnitOrderKind.Idle, 30 * Second);
        Assert.That((unit.CellX, sim.State.Map.LevelAt(unit.CellX, unit.CellY)), Is.EqualTo((12, 2)));
    }

    [Test]
    public void GWT5_CrowdPushesTowardsTheCliff_ThePushIsDropped()
    {
        var sim = Sim(Cliff());
        Unit edge = Spawn(sim, A, "infantry", 12, 3);
        Unit pusher = Spawn(sim, A, "infantry", 12, 3);
        // Overlapping, the first one a hair from the edge: half the overlap would carry it into x = 11.
        edge.X = 12100;
        pusher.X = 12300;
        sim.Tick();
        Assert.That(edge.CellX, Is.EqualTo(12));
        Assert.That(pusher.X, Is.GreaterThan(12300), "the other way the push works");
    }

    [Test]
    public void GWT6_Diagonal_NeverCutsTheCornerOfACliff()
    {
        MapState map = Sim(Cliff()).State.Map;
        Assert.That(map.CanStep(11, 3, 12, 4), Is.False, "low → high diagonally");
        Assert.That(map.CanStep(10, 12, 11, 13), Is.True, "onto the ramp from below: both ways round are low ground");
        // From the ramp to the plateau cell beside its end: one way round is the cliff (11, 12) → (12, 12).
        Assert.That(map.CanStep(11, 13, 12, 12), Is.False);
        Assert.That(map.CanStep(11, 13, 12, 13), Is.True, "straight up the ramp");
    }

    // ---------- sight ----------

    [Test]
    public void GWT7_FromBelow_ThePlateauIsNeitherSeenNorDiscovered()
    {
        var sim = Sim(Cliff());
        Spawn(sim, A, "scout", 8, 3);
        Assert.That(Seen(sim, A, 10, 3), Is.True);
        Assert.That(Seen(sim, A, 12, 3) || Seen(sim, A, 14, 3), Is.False);
        Assert.That(sim.State.Fog.IsDiscovered(A, 12, 3), Is.False);
    }

    [Test]
    public void GWT8_FromThePlateau_BothLevelsAreSeen()
    {
        var sim = Sim(Cliff());
        Spawn(sim, A, "scout", 15, 3);
        Assert.That(Seen(sim, A, 13, 3) && Seen(sim, A, 9, 3), Is.True);
    }

    [Test]
    public void GWT9_OnTheRamp_EyesAreAtTheUpperLevel()
    {
        var sim = Sim(Cliff());
        Spawn(sim, A, "scout", 11, 13);
        Assert.That(Seen(sim, A, 13, 13), Is.True);
    }

    [Test]
    public void GWT10_FromBelow_TheRampItselfIsSeen()
    {
        var sim = Sim(Cliff());
        Spawn(sim, A, "scout", 8, 13);
        Assert.That(Seen(sim, A, 11, 13), Is.True);
        Assert.That(Seen(sim, A, 12, 13), Is.False);
    }

    // ---------- fighting ----------

    [Test]
    public void GWT11_EnemyOnThePlateauUnseen_IdleUnitBelowPicksNoTarget()
    {
        var sim = Sim(Cliff());
        Unit infantry = Spawn(sim, A, "infantry", 10, 3);
        Dummy(sim, 13, 3);
        sim.Run(Second, s => Assert.That(infantry.TargetId, Is.EqualTo(0)));
    }

    [Test]
    public void GWT12_WhoeverStruck_IsSeenByTheVictimsTeamForTwoSeconds()
    {
        var sim = Sim(Cliff());
        Unit infantry = Spawn(sim, A, "infantry", 10, 3);
        Unit gunner = Spawn(sim, Enemy, "legion_gunner", 13, 3);
        Assert.That(Seen(sim, A, 13, 3), Is.False);
        sim.RunUntil(s => s.Events.OfType<HitLanded>().Any(h => h.AttackerId == gunner.Id));
        Assert.That(Seen(sim, A, 13, 3), Is.True);
        Assert.That(Seen(sim, A, 14, 3), Is.False, "its own cell only");

        // Out of the gunner's sight, so no second shot renews it.
        infantry.X = Center(2);
        sim.Run(39);
        Assert.That(Seen(sim, A, 13, 3), Is.True, "40 ticks with the tick of the blow");
        sim.Tick();
        Assert.That(Seen(sim, A, 13, 3), Is.False);
    }

    [Test]
    public void GWT13_RevealedShooter_IsAnsweredByARangedUnitBelow()
    {
        var sim = Sim(Cliff());
        Spawn(sim, A, "infantry", 10, 3);
        Unit shooter = Spawn(sim, A, "shooter", 9, 4);
        Unit gunner = Spawn(sim, Enemy, "legion_gunner", 13, 3);
        Assert.That(Hits(sim, 3 * Second, shooter, gunner), Is.Not.Empty);
    }

    [Test]
    public void GWT14_ShooterAbove_ReachesOneCellFarther()
    {
        var sim = Sim(Cliff());
        Unit shooter = Spawn(sim, A, "shooter", 12, 3);
        Unit target = Dummy(sim, 7, 3);
        // 4.85 cells edge to edge: beyond the range of 4, within 4 + 1.
        target.X = shooter.X - 4850 - shooter.Radius - target.Radius;
        Assert.That(Combat.EdgeDistance(shooter, target), Is.InRange(4001, 5000));
        sim.State.Fog.Update(sim.State);
        int x = shooter.X;
        Attack(sim, shooter, target);
        Assert.That(Hits(sim, Second, shooter, target), Is.Not.Empty);
        Assert.That(shooter.X, Is.EqualTo(x), "did not have to come closer");
    }

    [Test]
    public void GWT15_ShooterBelow_GetsNoBonus_ComesWithinItsOwnRange()
    {
        var sim = Sim(Cliff());
        Spawn(sim, A, "scout", 16, 6);   // the team sees the plateau
        Unit shooter = Spawn(sim, A, "shooter", 7, 3);
        Unit target = Dummy(sim, 12, 3);
        shooter.X = target.X - 4850 - shooter.Radius - target.Radius;
        int x = shooter.X;
        Attack(sim, shooter, target);
        sim.RunUntil(s => s.Events.OfType<HitLanded>().Any(h => h.AttackerId == shooter.Id));
        Assert.That(shooter.X, Is.GreaterThan(x));
        Assert.That(Combat.EdgeDistance(shooter, target), Is.LessThanOrEqualTo(4000));
    }

    [Test]
    public void GWT16_SameLevel_NoBonus()
    {
        var sim = Sim(Cliff());
        Unit shooter = Spawn(sim, A, "shooter", 8, 3);
        Unit target = Dummy(sim, 3, 3);
        target.X = shooter.X - 4850 - shooter.Radius - target.Radius;
        int x = shooter.X;
        Attack(sim, shooter, target);
        sim.RunUntil(s => s.Events.OfType<HitLanded>().Any(h => h.AttackerId == shooter.Id));
        Assert.That(shooter.X, Is.LessThan(x), "had to step closer");
    }

    [Test]
    public void GWT17_TurretOnThePlateau_ReachesSevenCells()
    {
        var sim = Sim(Cliff());
        Building turret = Place(sim, A, "turret", 12, 2);
        Unit target = Dummy(sim, 5, 3);
        target.X = 12000 - 6800 - target.Radius;
        target.Y = 3000;
        Assert.That(TurretSystem.EdgeDistance(turret, target), Is.InRange(6001, 7000));
        sim.State.Fog.Update(sim.State);
        Assert.That(Hits(sim, 2 * Second, turret, target), Is.Not.Empty);
    }

    [Test]
    public void GWT18_TurretNeverFiresIntoTheFog_EvenWithinReach()
    {
        var sim = Sim(Cliff());
        Building turret = Place(sim, A, "turret", 8, 2);
        Unit target = Dummy(sim, 13, 3);
        Assert.That(TurretSystem.EdgeDistance(turret, target), Is.LessThan(6000));
        Assert.That(Hits(sim, 3 * Second, turret, target), Is.Empty, "the plateau is above the turret's eyes");
    }

    [Test]
    public void GWT19_MeleeNeverStrikesAcrossTheCliff_WalksRoundAndStrikesOnThePlateau()
    {
        var sim = Sim(Cliff());
        MapState map = sim.State.Map;
        Spawn(sim, A, "scout", 16, 6);
        Unit infantry = Spawn(sim, A, "infantry", 11, 3);
        Unit target = Dummy(sim, 12, 3);
        Attack(sim, infantry, target);
        bool struck = false;
        sim.RunUntil(s =>
        {
            if (s.Events.OfType<HitLanded>().Any(h => h.AttackerId == infantry.Id))
            {
                struck = true;
                Assert.That(map.LevelsJoin(infantry.CellX, infantry.CellY, target.CellX, target.CellY), Is.True);
                Assert.That(map.LevelAt(infantry.CellX, infantry.CellY), Is.EqualTo(1));
            }
            return struck;
        }, 60 * Second);
    }

    [Test]
    public void GWT20_MeleeOnTheRamp_StrikesTheEnemyAtItsTop()
    {
        var sim = Sim(Cliff());
        Unit infantry = Spawn(sim, A, "infantry", 11, 13);
        Unit target = Dummy(sim, 12, 13);
        infantry.X = 11800;
        target.X = 12300;
        Attack(sim, infantry, target);
        Assert.That(Hits(sim, Second, infantry, target), Is.Not.Empty);
        Assert.That(infantry.CellX, Is.EqualTo(11));
    }

    // Camp below at (8, 4) with creeps at (8, 4) and (9, 4); aggro radius 5.
    private static MapSetup CampBelow() => Cliff().CreepCamp("small_camp", 8, 4, (8, 4), (9, 4));

    [Test]
    public void GWT21_CampBelow_DoesNotRiseAtABuildingOnThePlateau()
    {
        var sim = Sim(CampBelow());
        Place(sim, A, "wall", 12, 4);
        sim.Run(5 * Second, s => Assert.That(s.State.CreepCamps[0].State, Is.EqualTo(CreepCampState.Calm)));

        // The same building below, as close: the camp rises — so the cliff was the reason.
        Place(sim, A, "wall", 11, 6);
        sim.Run(2);
        Assert.That(sim.State.CreepCamps[0].State, Is.EqualTo(CreepCampState.Alerted));
    }

    [Test]
    public void GWT22_TurretOnThePlateauStrikesACreep_TheCampRisesAndGoesForIt()
    {
        var sim = Sim(CampBelow());
        Building turret = Place(sim, A, "turret", 12, 3);
        CreepCamp camp = sim.State.CreepCamps[0];
        sim.RunUntil(s => camp.State == CreepCampState.Alerted, 3 * Second);
        sim.RunUntil(s => camp.CreepIds.Select(id => s.State.FindUnit(id)).Any(c => c != null && c.TargetId == turret.Id), 3 * Second);
    }

    // ---------- work, spawn, capture ----------

    [Test]
    public void GWT23_BuilderBelowTheCliff_BuildsFromThePlateau()
    {
        var sim = Sim(Cliff().Start(A, 2, 2));
        MapState map = sim.State.Map;
        Spawn(sim, A, "scout", 15, 3);   // the site must be discovered
        Unit builder = sim.State.FindBuilder(A)!;
        builder.X = Center(11);
        builder.Y = Center(3);
        sim.Order(new BuildCommand(A, sim.State.Tick, new[] { builder.Id }, "wall", new[] { new BuildSite(12, 3, Direction.Right) }));
        sim.RunUntil(s => map.BuildingAt(12, 3) != 0, 60 * Second);
        Assert.That(map.LevelsJoin(builder.CellX, builder.CellY, 12, 3), Is.True);
        Assert.That(map.LevelAt(builder.CellX, builder.CellY) == 1 || map.IsRamp(builder.CellX, builder.CellY), Is.True);
    }

    [Test]
    public void GWT24_UnitsAppearBesideTheBuildingOnItsOwnLevel()
    {
        var sim = Sim(Cliff());
        Place(sim, A, "factory", 12, 5);
        List<(int X, int Y)> cells = sim.State.FreeCellsAround(12, 5, 3, 3, 8);
        Assert.That(cells[0], Is.EqualTo((12, 4)), "(11, 4) comes first in the ring but lies under the cliff");
        Assert.That(cells.All(c => sim.State.Map.LevelAt(c.X, c.Y) == 1), Is.True);
    }

    [Test]
    public void GWT25_CaptureOnlyFromTheBuildingsOwnLevel()
    {
        // Only players with a start on the map capture (neutral-buildings.md).
        var map = Cliff().Start(A, 2, 9).OreRect(12, 4, 2, 2, Resources.Primary).Neutral("drill", 12, 4);
        var sim = new Simulation(Setups.WithNeutral(map));
        Building drill = sim.State.Buildings.Single(b => b.Stats.Id == "drill");
        Spawn(sim, A, "infantry", 10, 4);
        sim.Run(11 * Second);
        Assert.That((drill.OwnerId, drill.CaptureProgress), Is.EqualTo((Setups.Neutral, 0)));

        Spawn(sim, A, "infantry", 15, 4);
        sim.Run(10 * Second + 1);
        Assert.That(drill.OwnerId, Is.EqualTo(A));
    }

    // ---------- building and belts ----------

    private static Simulation Scouted()
    {
        var sim = Sim(Cliff().Start(A, 2, 2));
        Spawn(sim, A, "scout", 10, 3);
        Spawn(sim, A, "scout", 14, 3);
        Spawn(sim, A, "scout", 13, 13);
        return sim;
    }

    [Test]
    public void GWT26_FootprintOnTwoLevels_IsRejected_NothingPaid()
    {
        var sim = Scouted();
        SimState s = sim.State;
        Assert.That(Construction.CheckPlacement(s, A, s.Data.Building("turret"), 11, 2, Direction.Right), Is.EqualTo(RejectReason.UnevenGround));
        Assert.That(Construction.CheckPlacement(s, A, s.Data.Building("turret"), 13, 2, Direction.Right), Is.Null);

        int before = s.GetPlayer(A).Wallet.Get(Resources.Primary);
        sim.Order(new BuildCommand(A, s.Tick, new[] { s.FindBuilder(A)!.Id }, "factory", new[] { new BuildSite(10, 2, Direction.Right) }));
        Assert.That(sim.RunCollecting<CommandRejected>(1).Single().Reason, Is.EqualTo(RejectReason.UnevenGround));
        Assert.That(s.GetPlayer(A).Wallet.Get(Resources.Primary), Is.EqualTo(before));
    }

    [Test]
    public void GWT27_FootprintOnARamp_IsRejected()
    {
        var sim = Scouted();
        SimState s = sim.State;
        Assert.That(Construction.CheckPlacement(s, A, s.Data.Building("turret"), 10, 13, Direction.Right), Is.EqualTo(RejectReason.OnRamp));
        Assert.That(Construction.CheckPlacement(s, A, s.Data.Building("wall"), 11, 13, Direction.Right), Is.EqualTo(RejectReason.OnRamp));
    }

    private static Building[] LineRight(Simulation sim, int x0, int x1, int y) =>
        Enumerable.Range(x0, x1 - x0 + 1).Select(x => Place(sim, A, "conveyor", x, y)).ToArray();

    private static void Load(Building belt)
    {
        belt.Conveyor!.ItemType = Resources.Primary;
        belt.Conveyor.ItemPos = 0;
    }

    [Test]
    public void GWT28_BeltUpTheRamp_CarriesToThePlateau()
    {
        var sim = Scouted();
        Assert.That(Construction.CheckPlacement(sim.State, A, sim.State.Data.Building("conveyor"), 11, 13, Direction.Right), Is.Null);
        Building[] line = LineRight(sim, 9, 13, 13);
        Load(line[0]);
        sim.RunUntil(s => line[4].Conveyor!.HasItem, 5 * Second);
    }

    [Test]
    public void GWT29_BeltAtTheFootOfTheCliff_DoesNotPassUp_TheUnitWaitsAtTheEdge()
    {
        var sim = Sim(Cliff());
        Building[] line = LineRight(sim, 9, 13, 3);
        Load(line[0]);
        sim.Run(5 * Second);
        Assert.That(line[2].Conveyor!.HasItem, Is.True, "(11, 3), the last cell below");
        Assert.That(line[3].Conveyor!.HasItem || line[4].Conveyor!.HasItem, Is.False);
    }

    [Test]
    public void GWT30_BridgeCarriesOverTheCliff()
    {
        var sim = Sim(Cliff());
        Building feed = Place(sim, A, "conveyor", 9, 3);
        Building input = Place(sim, A, "conveyor_bridge", 10, 3);
        Building output = Place(sim, A, "conveyor_bridge", 13, 3);
        Building after = Place(sim, A, "conveyor", 14, 3);
        Assert.That(input.Bridge!.LinkedToId, Is.EqualTo(output.Id));
        Load(feed);
        sim.RunUntil(s => after.Conveyor!.HasItem, 10 * Second);
    }

    [Test]
    public void GWT31_DrillOnThePlateau_DoesNotGiveToABeltBelow()
    {
        var sim = Sim(Cliff().OreRect(12, 3, 2, 2, Resources.Primary));
        Building drill = Place(sim, A, "drill", 12, 3);
        Building below = Place(sim, A, "conveyor", 11, 3, Direction.Left);
        drill.Drill!.Buffer = 10;
        sim.Run(3 * Second);
        Assert.That((below.Conveyor!.HasItem, drill.Drill.Buffer), Is.EqualTo((false, 10)));

        Building beside = Place(sim, A, "conveyor", 14, 3);
        sim.RunUntil(s => beside.Conveyor!.HasItem, 3 * Second);
    }

    // ---------- abilities ----------

    private static CommandRejected? Blink(Simulation sim, Unit striker, int cx, int cy)
    {
        sim.Order(new UseAbilityCommand(A, sim.State.Tick, new[] { striker.Id }, "rl_blink", Center(cx), Center(cy)));
        return sim.RunCollecting<CommandRejected>(1).SingleOrDefault();
    }

    [Test]
    public void GWT32_BlinkToASeenPointOnThePlateau_CrossesTheCliff()
    {
        var sim = Sim(Cliff());
        Spawn(sim, A, "scout", 16, 6);
        Unit striker = Spawn(sim, A, "rl_striker", 9, 3);
        Assert.That(Blink(sim, striker, 13, 3), Is.Null);
        Assert.That((striker.CellX, striker.CellY), Is.EqualTo((13, 3)));
        Assert.That(striker.AbilityCooldown(0), Is.InRange(8 * Second - 1, 8 * Second));
    }

    [Test]
    public void GWT33_BlinkToAnUnseenPoint_IsRejected_NoCooldown()
    {
        var sim = Sim(Cliff());
        Unit striker = Spawn(sim, A, "rl_striker", 9, 3);
        Assert.That(Blink(sim, striker, 13, 3)!.Reason, Is.EqualTo(RejectReason.NoVision));
        Assert.That((striker.CellX, striker.AbilityCooldown(0)), Is.EqualTo((9, 0)));
    }

    [Test]
    public void GWT34_PointAbilityIntoTheUnseen_IsRejected()
    {
        var sim = SimOf(Cliff(), "yellow_star");
        Unit hero = Spawn(sim, A, "ys_hero", 9, 3);
        Player player = sim.State.GetPlayer(A);
        player.HeroPathId = "ys_mage";
        player.HeroLevel = 3;
        sim.Order(new UseAbilityCommand(A, sim.State.Tick, new[] { hero.Id }, "ys_starfall", Center(14), Center(3)));
        Assert.That(sim.RunCollecting<CommandRejected>(1).Single().Reason, Is.EqualTo(RejectReason.NoVision));

        Spawn(sim, A, "scout", 16, 6);
        sim.Order(new UseAbilityCommand(A, sim.State.Tick, new[] { hero.Id }, "ys_starfall", Center(14), Center(3)));
        Assert.That(sim.RunCollecting<AbilityUsed>(1), Is.Not.Empty);
    }

    [Test]
    public void GWT35_Aura_WorksAcrossLevels()
    {
        var sim = SimOf(Cliff(), "yellow_star");
        Spawn(sim, A, "ys_hero", 13, 3);
        Unit archer = Spawn(sim, A, "ys_archer", 9, 3);
        Assert.That(HeroSystem.DamageBonusPercent(sim.State, archer), Is.EqualTo(20));
    }

    [Test]
    public void GWT36_MapWithoutLevels_NothingChanges()
    {
        var sim = Sim(new MapSetup(24, 16));
        MapState map = sim.State.Map;
        Assert.That(map.HasLevels, Is.False);
        Assert.That(map.CanStep(11, 3, 12, 4) && map.LevelsJoin(0, 0, 23, 15), Is.True);
        Spawn(sim, A, "scout", 8, 3);
        Assert.That(Seen(sim, A, 14, 3), Is.True);
    }

    // ---------- edge cases ----------

    [Test]
    public void Edge_OnTheRamp_LevelAdjacentToBoth_NoBonusEitherWay()
    {
        var sim = Sim(Cliff());
        Unit onRamp = Spawn(sim, A, "shooter", 11, 13);
        Unit below = Dummy(sim, 10, 13), above = Dummy(sim, 12, 13);
        MapState map = sim.State.Map;
        Assert.That(Sight.LevelAdjacent(map, onRamp, below) && Sight.LevelAdjacent(map, onRamp, above), Is.True);
        Assert.That(Sight.HighGroundBonus(sim.State, onRamp, below), Is.EqualTo(0));
        Assert.That(Sight.HighGroundBonus(sim.State, onRamp, above), Is.EqualTo(0));
    }

    [Test]
    public void Edge_ShooterOnTheRamp_TargetBelow_StandLevelsAreEqual()
    {
        var sim = Sim(Cliff());
        Unit onRamp = Spawn(sim, A, "shooter", 11, 13);
        Assert.That(Sight.StandLevel(sim.State.Map, onRamp), Is.EqualTo(0));
        Assert.That(Sight.EyeLevel(sim.State.Map, onRamp), Is.EqualTo(1));
    }

    [Test]
    public void Edge_TargetOnTheRamp_ShooterOnThePlateau_GetsTheBonus()
    {
        var sim = Sim(Cliff());
        Unit shooter = Spawn(sim, A, "shooter", 14, 13);
        Unit onRamp = Dummy(sim, 11, 13);
        Assert.That(Sight.HighGroundBonus(sim.State, shooter, onRamp), Is.EqualTo(1000));
    }

    private static (Simulation Sim, Unit Gunner) Revealed()
    {
        var sim = Sim(Cliff());
        Unit victim = Spawn(sim, A, "infantry", 10, 3);
        Unit gunner = Spawn(sim, Enemy, "legion_gunner", 13, 3);
        sim.RunUntil(s => s.Events.OfType<HitLanded>().Any(h => h.AttackerId == gunner.Id));
        victim.X = Center(2);
        return (sim, gunner);
    }

    [Test]
    public void Edge_RevealedUnitMoves_TheRevealFollowsIt()
    {
        (Simulation sim, Unit gunner) = Revealed();
        gunner.X = Center(15);
        gunner.Y = Center(6);
        sim.Tick();
        Assert.That((Seen(sim, A, 15, 6), Seen(sim, A, 13, 3)), Is.EqualTo((true, false)));
    }

    [Test]
    public void Edge_StruckTwoPlayers_RevealedToTheLast_AlliesShareIt()
    {
        var sim = Sim(Cliff());
        Unit a = Spawn(sim, A, "infantry", 10, 3), b = Spawn(sim, B, "infantry", 10, 5);
        Unit gunner = Spawn(sim, Enemy, "repairer", 13, 3);
        sim.State.PendingHits.Add(new PendingHit(gunner.Id, a.Id, 1));
        sim.Tick();
        sim.State.PendingHits.Add(new PendingHit(gunner.Id, b.Id, 1));
        sim.Tick();
        Assert.That(gunner.RevealedToPlayerId, Is.EqualTo(B));
        Assert.That(Seen(sim, A, 13, 3) && Seen(sim, B, 13, 3), Is.True);
    }

    [Test]
    public void Edge_AttackerAlreadySeen_NothingChanges()
    {
        var sim = Sim(Cliff());
        Spawn(sim, A, "infantry", 10, 3);
        Unit gunner = Spawn(sim, Enemy, "legion_gunner", 8, 3);
        Assert.That(Seen(sim, A, 8, 3), Is.True);
        sim.Run(Second);
        Assert.That(Seen(sim, A, gunner.CellX, gunner.CellY), Is.True);
    }

    [Test]
    public void Edge_Reveal_IsPartOfTheState_SameFightSameHashes()
    {
        ulong[] Run()
        {
            (Simulation sim, _) = Revealed();
            var hashes = new ulong[60];
            for (int i = 0; i < hashes.Length; i++)
            {
                sim.Tick();
                hashes[i] = sim.StateHash;
            }
            return hashes;
        }
        Assert.That(Run(), Is.EqualTo(Run()));

        (Simulation one, Unit gunner) = Revealed();
        ulong before = new StateHasher().Compute(one.State);
        gunner.RevealedUntilTick++;
        Assert.That(new StateHasher().Compute(one.State), Is.Not.EqualTo(before));
    }

    [Test]
    public void Edge_BuildingOnThePlateau_WithinRadiusFromBelow_IsNotSeenNorAttacked()
    {
        var sim = Sim(Cliff());
        Building hq = Place(sim, Enemy, "legion_hq", 12, 2);
        Unit shooter = Spawn(sim, A, "shooter", 10, 3);
        Assert.That(Sight.IsVisibleTo(sim.State, A, hq), Is.False);
        sim.Run(Second, s => Assert.That(shooter.TargetId, Is.EqualTo(0)));
    }

    [Test]
    public void Edge_TurretReachesWhatItDoesNotSee_FiresOnceAnAllySees()
    {
        var sim = Sim(Cliff());
        Building turret = Place(sim, A, "turret", 8, 2);
        Unit target = Dummy(sim, 13, 3);
        Assert.That(Hits(sim, 2 * Second, turret, target), Is.Empty);
        Spawn(sim, B, "scout", 16, 6);
        Assert.That(Hits(sim, 2 * Second, turret, target), Is.Not.Empty);
    }

    [Test]
    public void Edge_AttackOrder_TargetGoesOutOfSight_TheUnitKeepsGoingForIt()
    {
        var sim = Sim(Cliff());
        Unit spotter = Spawn(sim, A, "scout", 16, 6);
        Unit shooter = Spawn(sim, A, "shooter", 4, 3);
        Unit target = Dummy(sim, 14, 3);
        Attack(sim, shooter, target);
        sim.Run(5);
        Kill(spotter);
        int x = shooter.X;
        sim.Run(2 * Second);
        Assert.That(shooter.Order.Kind, Is.EqualTo(UnitOrderKind.Attack));
        Assert.That(shooter.X, Is.Not.EqualTo(x));
    }

    [Test]
    public void Edge_AttackMoveToAPlateauWithoutARamp_StopsBelow()
    {
        var sim = Sim(Cliff(ramp: false));
        Unit raider = Spawn(sim, Enemy, "legion_raider", 6, 3);
        raider.Order = UnitOrder.AttackMoveTo(Center(14), Center(3));
        sim.RunUntil(s => raider.Order.Kind == UnitOrderKind.Idle, 30 * Second);
        Assert.That(sim.State.Map.LevelAt(raider.CellX, raider.CellY), Is.EqualTo(0));
    }

    [Test]
    public void Edge_CargoToAHeadquartersOnThePlateau_IsDeliveredFromItsLevel()
    {
        var sim = Sim(Cliff().Start(A, 12, 2));
        MapState map = sim.State.Map;
        Unit builder = sim.State.FindBuilder(A)!;
        builder.X = Center(11);
        builder.Y = Center(3);
        builder.Worker!.Cargo = 5;
        builder.Worker.CargoType = Resources.Primary;
        builder.Order = UnitOrder.ReturnCargo();
        sim.RunUntil(s => s.Events.OfType<CargoDelivered>().Any(), 60 * Second);
        Assert.That(map.LevelsJoin(builder.CellX, builder.CellY, 12, 2), Is.True);
    }

    [Test]
    public void Edge_BeltOnTheRamp_IsWalkable()
    {
        var sim = Sim(Cliff());
        Place(sim, A, "conveyor", 11, 13);
        Assert.That(sim.State.Map.CanStep(10, 13, 11, 13) && sim.State.Map.CanStep(11, 13, 12, 13), Is.True);
    }

    [Test]
    public void Edge_BridgeEndOnTheRamp_IsAllowed()
    {
        var sim = Scouted();
        Assert.That(Construction.CheckPlacement(sim.State, A, sim.State.Data.Building("conveyor_bridge"), 11, 13, Direction.Right), Is.Null);
    }

    [Test]
    public void Edge_BlinkWithNothingSeenOnTheLine_DoesNotMove_NoCooldown()
    {
        var sim = Sim(Cliff());
        Unit striker = Spawn(sim, A, "rl_striker", 11, 3);
        // Six cells into the unseen plateau: every landing spot on the line is out of sight.
        Assert.That(Blink(sim, striker, 20, 3)!.Reason, Is.EqualTo(RejectReason.NoVision));
        Assert.That((striker.CellX, striker.AbilityCooldown(0)), Is.EqualTo((11, 0)));
    }

    [Test]
    public void Edge_UnitOnAPillar_StaysPut()
    {
        var map = new MapSetup(16, 8).Level(8, 3, 3);
        var sim = Sim(map);
        Unit unit = Spawn(sim, A, "infantry", 8, 3);
        sim.Order(sim.Move(A, Center(2), Center(3), unit));
        sim.Run(3 * Second);
        Assert.That((unit.CellX, unit.CellY, unit.Order.Kind), Is.EqualTo((8, 3, UnitOrderKind.Idle)));
    }

    [Test]
    public void Edge_RockOnThePlateau_IsAboveTheEyesFromBelow()
    {
        var sim = Sim(Cliff().Block(13, 3));
        Spawn(sim, A, "scout", 9, 3);
        Assert.That(Seen(sim, A, 13, 3), Is.False);
        Spawn(sim, A, "scout", 16, 3);
        Assert.That(Seen(sim, A, 13, 3), Is.True);
    }

    // The hand-made test map of the stage: it loads with its levels, and the Legion's first wave
    // finds its way down the ramp.
    [Test]
    public void HeightsMission_Loads_TheLegionOnThePlateauReachesTheBasesByTheRamp()
    {
        Simulation sim = RepoData.Launch("heights");
        SimState s = sim.State;
        Assert.That(s.Map.HasLevels, Is.True);
        Building citadel = s.FindHeadquarters(MissionLauncher.LegionPlayerId)!;
        Assert.That(s.Map.LevelAt(citadel.CellX, citadel.CellY), Is.EqualTo(1));
        FlowField toCitadel = s.Paths.ToRectangle(s.Map, citadel.CellX, citadel.CellY, citadel.Width, citadel.Height);
        foreach (int p in s.TeamPlayerIds)
            Assert.That(toCitadel.DistanceAt(s.FindBuilder(p)!.CellX, s.FindBuilder(p)!.CellY), Is.Not.EqualTo(FlowField.Unreachable));
        Assert.That(s.Fog.IsDiscovered(1, citadel.CellX, citadel.CellY), Is.False, "the plateau is hidden from below");

        // Same seed, same commands (none): the same hashes with heights in play.
        Simulation twin = RepoData.Launch("heights");
        for (int i = 0; i < 6 * 60 * Simulation.TicksPerSecond; i++)
        {
            sim.Tick();
            twin.Tick();
            Assert.That(sim.StateHash, Is.EqualTo(twin.StateHash), $"tick {i}");
        }
        Assert.That(s.Buildings.Any(b => s.IsTeamPlayer(b.OwnerId) && b.LastDamagedTick > 0)
                    || s.Units.Any(u => u.OwnerId == MissionLauncher.LegionPlayerId && s.Map.LevelAt(u.CellX, u.CellY) == 0),
            Is.True, "by six minutes a wave has come down from the plateau");
    }
}
