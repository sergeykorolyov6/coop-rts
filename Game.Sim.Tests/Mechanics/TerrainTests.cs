using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/terrain.md
public class TerrainTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int A = Setups.PlayerA, B = Setups.PlayerB, Enemy = Setups.EnemyAi;

    private static TerrainStats Kind(char symbol) => RepoData.Get().TerrainBySymbol(symbol)!;

    private static MapSetup Rock(MapSetup map, char symbol, params (int X, int Y)[] cells)
    {
        foreach ((int x, int y) in cells)
            map.Terrain(x, y, Kind(symbol));
        return map;
    }

    // A full-height column of rock at x.
    private static MapSetup Column(MapSetup map, char symbol, int x)
    {
        for (int y = 0; y < map.Height; y++)
            map.Terrain(x, y, Kind(symbol));
        return map;
    }

    private static Simulation Sim(MapSetup map) => new Simulation(Setups.Coop(map));

    private static void Dig(Simulation sim, int x, int y, bool queued = false, params Unit[] units) =>
        sim.Order(new DigCommand(units[0].OwnerId, sim.State.Tick, units.Select(u => u.Id).ToArray(), x, y, queued));

    private static RejectReason? Rejected(Simulation sim) =>
        sim.RunCollecting<CommandRejected>(1).Select(r => (RejectReason?)r.Reason).SingleOrDefault();

    private static List<(int X, int Y)> Tunnel(Unit miner) =>
        new[] { (miner.Order.X, miner.Order.Y) }.Concat(miner.OrderQueue.Select(q => (q.Order.X, q.Order.Y))).ToList();

    // ---------- digging ----------

    [Test]
    public void GWT1_SoftRock_DugInFiveSeconds_BecomesGround()
    {
        var sim = Sim(Rock(new MapSetup(16, 8), '%', (6, 3)));
        Unit miner = Spawn(sim, A, "miner", 5, 3);
        Dig(sim, 6, 3, units: miner);
        var destroyed = new List<RockDestroyed>();
        int ticks = sim.RunUntil(s => { destroyed.AddRange(s.Events.OfType<RockDestroyed>()); return destroyed.Count > 0; });
        Assert.That(ticks, Is.EqualTo(100));
        Assert.That((destroyed.Single().CellX, destroyed.Single().CellY, destroyed.Single().TerrainId), Is.EqualTo((6, 3, "soft_rock")));
        Assert.That(sim.State.Map.IsPassable(6, 3) && !sim.State.Map.IsOpaque(6, 3), Is.True);
        Assert.That(miner.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void GWT2_TwoMiners_DigTogether_TwiceAsFast_OneEvent()
    {
        var sim = Sim(Rock(new MapSetup(16, 8), '%', (6, 3)));
        Unit a = Spawn(sim, A, "miner", 5, 3), b = Spawn(sim, A, "miner", 6, 2);
        Dig(sim, 6, 3, units: new[] { a, b });
        var destroyed = new List<RockDestroyed>();
        int ticks = sim.RunUntil(s => { destroyed.AddRange(s.Events.OfType<RockDestroyed>()); return !s.State.Map.IsDestructibleRock(6, 3); });
        Assert.That(ticks, Is.EqualTo(50));
        sim.Run(5, s => destroyed.AddRange(s.Events.OfType<RockDestroyed>()));
        Assert.That(destroyed, Has.Count.EqualTo(1));
    }

    [Test]
    public void GWT3_HardRock_TwentySeconds()
    {
        var sim = Sim(Rock(new MapSetup(16, 8), '@', (6, 3)));
        Unit miner = Spawn(sim, A, "miner", 5, 3);
        Dig(sim, 6, 3, units: miner);
        Assert.That(sim.RunUntil(s => !s.State.Map.IsDestructibleRock(6, 3), 30 * Second), Is.EqualTo(400));
    }

    [Test]
    public void GWT4_OreRock_LeavesOreOfTheNeighbouringDeposit_ADrillFitsOnIt()
    {
        var map = Rock(new MapSetup(16, 8).Start(A, 0, 4).OreRect(8, 3, 2, 2, Resources.Primary), 'R', (7, 3));
        var sim = Sim(map);
        MapState m = sim.State.Map;
        Unit miner = Spawn(sim, A, "miner", 6, 3);
        Dig(sim, 7, 3, units: miner);
        sim.RunUntil(s => !m.IsDestructibleRock(7, 3), 20 * Second);
        Assert.That(m.OreAt(7, 3), Is.EqualTo(Resources.Primary));
        Assert.That(m.DepositAt(7, 3), Is.EqualTo(m.DepositAt(8, 3)));
        Assert.That(m.Deposits[m.DepositAt(8, 3) - 1].Cells, Has.Member(3 * 16 + 7));
        Assert.That(Construction.CheckPlacement(sim.State, A, sim.State.Data.Building("drill"), 7, 3, Direction.Right), Is.Null);
    }

    [Test]
    public void GWT5_CrystalRock_FarFromOre_NewDepositOfTheSecondResource()
    {
        var sim = Sim(Rock(new MapSetup(16, 8).OreRect(12, 5, 2, 2, Resources.Primary), 'C', (6, 3)));
        MapState m = sim.State.Map;
        int before = m.Deposits.Count;
        Unit miner = Spawn(sim, A, "miner", 5, 3);
        Dig(sim, 6, 3, units: miner);
        sim.RunUntil(s => !m.IsDestructibleRock(6, 3), 20 * Second);
        Assert.That(m.OreAt(6, 3), Is.EqualTo(Resources.Secondary));
        Assert.That(m.Deposits, Has.Count.EqualTo(before + 1));
        Assert.That(m.DepositAt(6, 3), Is.EqualTo(before + 1));
        Assert.That(m.Deposits[before].ResourceType, Is.EqualTo(Resources.Secondary));
    }

    [Test]
    public void GWT6_WallThreeThick_DugNearestFirst_ThenWalkable()
    {
        var map = Column(Column(Column(new MapSetup(16, 8), '%', 6), '%', 7), '%', 8);
        var sim = Sim(map);
        Spawn(sim, B, "scout", 11, 3);   // the far side is seen by an ally
        Unit miner = Spawn(sim, A, "miner", 4, 3);
        Dig(sim, 8, 3, units: miner);
        sim.Tick();
        Assert.That(Tunnel(miner), Is.EqualTo(new[] { (6, 3), (7, 3), (8, 3) }));
        var order = new List<(int, int)>();
        sim.RunUntil(s =>
        {
            order.AddRange(s.Events.OfType<RockDestroyed>().Select(e => (e.CellX, e.CellY)));
            return order.Count == 3;
        }, 30 * Second);
        Assert.That(order, Is.EqualTo(new[] { (6, 3), (7, 3), (8, 3) }));
        sim.Order(sim.Move(A, Center(11), Center(3), miner));
        sim.RunUntil(s => miner.Order.Kind == UnitOrderKind.Idle, 10 * Second);
        Assert.That(miner.CellX, Is.EqualTo(11));
    }

    // A sealed room x 9..11, y 4..6 inside eternal rock; two ways in: one hard cell from the west at
    // (8, 5), or three soft cells in a column under it at (10, 1..3). The target is soft rock inside, at (11, 5).
    private static MapSetup Room()
    {
        var map = new MapSetup(16, 12);
        for (int x = 8; x <= 12; x++)
        {
            Rock(map, '#', (x, 3), (x, 7));
        }
        for (int y = 3; y <= 7; y++)
            Rock(map, '#', (8, y), (12, y));
        Rock(map, '#', (9, 1), (9, 2), (11, 1), (11, 2));
        Rock(map, '@', (8, 5));
        Rock(map, '%', (10, 1), (10, 2), (10, 3), (11, 5));
        return map;
    }

    [Test]
    public void GWT7_FewestRockCells_NotTheFastest_NotTheNearest()
    {
        var sim = Sim(Room());
        Spawn(sim, B, "scout", 10, 5);   // stands inside the room: sees the target
        Unit miner = Spawn(sim, A, "miner", 10, 0);
        Dig(sim, 11, 5, units: miner);
        sim.Tick();
        Assert.That(Tunnel(miner), Is.EqualTo(new[] { (8, 5), (11, 5) }), "one hard cell, not three soft ones under the miner's feet");
    }

    [Test]
    public void GWT8_SoftRockWalledInByEternalRock_CannotDig()
    {
        // An island: ground (5, 5) and soft rock (6, 5) inside a ring of eternal rock; an ally's
        // scout on the island sees the rock, the miner outside cannot get through.
        var map = new MapSetup(16, 12);
        for (int x = 4; x <= 7; x++)
            Rock(map, '#', (x, 4), (x, 6));
        Rock(map, '#', (4, 5), (7, 5));
        Rock(map, '%', (6, 5));
        var sim = Sim(map);
        Spawn(sim, B, "scout", 5, 5);
        Unit miner = Spawn(sim, A, "miner", 1, 1);
        Dig(sim, 6, 5, units: miner);
        Assert.That(Rejected(sim), Is.EqualTo(RejectReason.CannotDig));
        Assert.That(miner.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void GWT9_EternalRock_NothingToDig()
    {
        var sim = Sim(Rock(new MapSetup(16, 8), '#', (6, 3)));
        Unit miner = Spawn(sim, A, "miner", 5, 3);
        Dig(sim, 6, 3, units: miner);
        Assert.That(Rejected(sim), Is.EqualTo(RejectReason.NothingToDig));
    }

    [Test]
    public void GWT10_OnlyABuilderSelected_NoDigger()
    {
        var sim = Sim(Rock(new MapSetup(16, 8), '%', (6, 3)));
        Unit builder = Spawn(sim, A, "builder", 5, 3);
        Dig(sim, 6, 3, units: builder);
        Assert.That(Rejected(sim), Is.EqualTo(RejectReason.NoDigger));
        Assert.That(sim.State.Map.RockHp(6, 3), Is.EqualTo(100));
    }

    [Test]
    public void GWT11_UndiscoveredRock_NotDiscovered()
    {
        var sim = Sim(Rock(new MapSetup(48, 8), '%', (40, 3)));
        Unit miner = Spawn(sim, A, "miner", 2, 3);
        Dig(sim, 40, 3, units: miner);
        Assert.That(Rejected(sim), Is.EqualTo(RejectReason.NotDiscovered));
    }

    [Test]
    public void GWT12_WithShift_FirstWalksThenDigs()
    {
        var sim = Sim(Rock(new MapSetup(16, 8), '%', (6, 3)));
        Unit miner = Spawn(sim, A, "miner", 5, 3);
        sim.Order(sim.Move(A, Center(1), Center(3), miner));
        sim.Tick();
        Dig(sim, 6, 3, queued: true, miner);
        sim.Tick();
        Assert.That(miner.Order.Kind, Is.EqualTo(UnitOrderKind.Move));
        Assert.That(miner.OrderQueue.Single().Order.Kind, Is.EqualTo(UnitOrderKind.Dig));
        sim.RunUntil(s => miner.Order.Kind == UnitOrderKind.Dig, 10 * Second);
        Assert.That(miner.CellX, Is.EqualTo(1), "reached the point first");
        sim.RunUntil(s => !s.State.Map.IsDestructibleRock(6, 3), 20 * Second);
    }

    [Test]
    public void GWT13_ABreachOpens_AWalkerOnTheDetourTakesIt()
    {
        // A soft wall at x = 8 with a gap only at the top: the detour is about 60 cells.
        var map = new MapSetup(16, 32);
        for (int y = 0; y < 31; y++)
            Rock(map, '%', (8, y));
        var sim = Sim(map);
        Unit infantry = Spawn(sim, A, "infantry", 4, 2);
        Unit miner = Spawn(sim, A, "miner", 7, 2);
        sim.Order(sim.Move(A, Center(12), Center(2), infantry));
        Dig(sim, 8, 2, units: miner);
        int ticks = sim.RunUntil(s => infantry.Order.Kind == UnitOrderKind.Idle, 60 * Second);
        Assert.That(ticks, Is.LessThan(400), "the detour alone takes over 600 ticks");
        Assert.That(infantry.CellX, Is.EqualTo(12));
    }

    // ---------- sight ----------

    [Test]
    public void GWT14_WallBlocksSight_TheWallItselfIsSeen()
    {
        var sim = Sim(Column(new MapSetup(16, 8), '%', 6));
        Spawn(sim, A, "scout", 3, 3);
        FogOfWar fog = sim.State.Fog;
        Assert.That(fog.IsVisible(A, 5, 3) && fog.IsVisible(A, 6, 3), Is.True);
        Assert.That(fog.IsVisible(A, 7, 3) || fog.IsVisible(A, 9, 3), Is.False);
    }

    [Test]
    public void GWT15_WallDugOut_SeenThroughTheBreach()
    {
        var sim = Sim(Column(new MapSetup(16, 8), '%', 6));
        Spawn(sim, A, "scout", 3, 3);
        sim.State.Map.DamageRock(6, 3, 1000);
        sim.Tick();
        Assert.That(sim.State.Fog.IsVisible(A, 9, 3), Is.True);
    }

    [Test]
    public void GWT16_ACornerHidesOnlyWhatIsBehindIt()
    {
        var sim = Sim(Rock(new MapSetup(16, 10), '%', (6, 4)));
        Spawn(sim, A, "scout", 4, 4);
        FogOfWar fog = sim.State.Fog;
        Assert.That(fog.IsVisible(A, 6, 4), Is.True, "the rock");
        Assert.That(fog.IsVisible(A, 7, 4) || fog.IsVisible(A, 8, 4), Is.False, "straight behind it");
        Assert.That(fog.IsVisible(A, 6, 5) && fog.IsVisible(A, 8, 6) && fog.IsVisible(A, 7, 3), Is.True, "beside it");
    }

    [Test]
    public void GWT17_CampBehindAWall_DoesNotSeeThroughIt_RisesOnceItIsDug()
    {
        var map = Column(new MapSetup(16, 10), '%', 8).CreepCamp("small_camp", 10, 4, (10, 4), (11, 4));
        var sim = Sim(map);
        Place(sim, A, "wall", 6, 4);    // three cells from the camp's centre, behind the rock
        sim.Run(2 * Second, s => Assert.That(s.State.CreepCamps[0].State, Is.EqualTo(CreepCampState.Calm)));
        sim.State.Map.DamageRock(8, 4, 1000);
        sim.Run(2);
        Assert.That(sim.State.CreepCamps[0].State, Is.EqualTo(CreepCampState.Alerted));
    }

    [Test]
    public void GWT18_TurretDoesNotFireThroughEternalRock()
    {
        var sim = Sim(Column(new MapSetup(16, 10), '#', 6));
        Building turret = Place(sim, A, "turret", 2, 3);
        Unit target = Spawn(sim, Enemy, "repairer", 8, 4);
        Assert.That(TurretSystem.EdgeDistance(turret, target), Is.LessThan(turret.Stats.AttackRangeMilli));
        Assert.That(sim.RunCollecting<HitLanded>(3 * Second).Where(h => h.AttackerId == turret.Id), Is.Empty);
    }

    [Test]
    public void GWT19_RockOnThePlateau_DugFromThePlateau()
    {
        var map = new MapSetup(24, 16);
        for (int y = 0; y < 16; y++)
            for (int x = 12; x < 24; x++)
                map.Level(x, y, 1);
        map.Level(11, 13, 0, ramp: true).Level(11, 14, 0, ramp: true);
        Rock(map, '%', (12, 3));
        var sim = Sim(map);
        MapState m = sim.State.Map;
        Spawn(sim, A, "scout", 15, 3);
        Unit miner = Spawn(sim, A, "miner", 11, 3);
        Dig(sim, 12, 3, units: miner);
        sim.RunUntil(s => !m.IsDestructibleRock(12, 3), 60 * Second);
        Assert.That(m.LevelsJoin(miner.CellX, miner.CellY, 12, 3), Is.True);
        Assert.That(m.LevelAt(miner.CellX, miner.CellY) == 1 || m.IsRamp(miner.CellX, miner.CellY), Is.True);
    }

    [Test]
    public void GWT20_TheLegionDoesNotDig_StopsAtTheWall()
    {
        var sim = Sim(Column(new MapSetup(16, 8), '%', 8));
        Unit raider = Spawn(sim, Enemy, "legion_raider", 12, 3);
        raider.Order = UnitOrder.AttackMoveTo(Center(3), Center(3));
        sim.Run(20 * Second);
        Assert.That(raider.CellX, Is.GreaterThan(8));
        Assert.That(Enumerable.Range(0, 8).All(y => sim.State.Map.RockHp(8, y) == 100), Is.True);
    }

    [Test]
    public void GWT21_EveryFactionsFactory_ProducesTheMiner()
    {
        GameData data = RepoData.Get();
        foreach (string factory in new[] { "factory", "rl_factory", "bs_factory", "ys_barracks" })
            Assert.That(data.Building(factory).Produces, Has.Member("miner"), factory);
        UnitStats miner = data.Unit("miner");
        Assert.That((miner.CostPrimary, miner.CostSecondary, miner.BuildTimeTicks, miner.DigPerTick), Is.EqualTo((30, 0, 15 * Second, 1)));
        Assert.That(miner.IsDigger && !Combat.CanAttack(new Unit(1, A, miner, 0, 0)), Is.True);
    }

    [Test]
    public void GWT22_SameDiggingTwice_SameHashes_AndRockHpIsInTheHash()
    {
        ulong[] Run()
        {
            var sim = Sim(Column(Column(new MapSetup(16, 8), '%', 6), 'R', 7));
            Unit miner = Spawn(sim, A, "miner", 4, 3);
            Spawn(sim, B, "scout", 10, 3);
            Dig(sim, 7, 3, units: miner);
            var hashes = new ulong[20 * Second];
            for (int i = 0; i < hashes.Length; i++)
            {
                sim.Tick();
                hashes[i] = sim.StateHash;
            }
            return hashes;
        }
        Assert.That(Run(), Is.EqualTo(Run()));

        var one = Sim(Rock(new MapSetup(16, 8), '%', (6, 3)));
        ulong before = new StateHasher().Compute(one.State);
        one.State.Map.DamageRock(6, 3, 1);
        Assert.That(new StateHasher().Compute(one.State), Is.Not.EqualTo(before));
    }

    // ---------- edge cases ----------

    [Test]
    public void Edge_CellDugByAnother_TheMinerMovesOnToTheNext()
    {
        var sim = Sim(Column(Column(new MapSetup(16, 8), '%', 6), '%', 7));
        Spawn(sim, B, "scout", 10, 3);
        Unit miner = Spawn(sim, A, "miner", 2, 3);
        Dig(sim, 7, 3, units: miner);
        sim.Tick();
        Assert.That((miner.Order.X, miner.Order.Y), Is.EqualTo((6, 3)));
        sim.State.Map.DamageRock(6, 3, 1000);
        sim.Run(2);
        Assert.That((miner.Order.Kind, miner.Order.X, miner.Order.Y), Is.EqualTo((UnitOrderKind.Dig, 7, 3)));
    }

    [Test]
    public void Edge_MinerDies_TheRockKeepsItsDamage()
    {
        var sim = Sim(Rock(new MapSetup(16, 8), '%', (6, 3)));
        Unit miner = Spawn(sim, A, "miner", 5, 3);
        Dig(sim, 6, 3, units: miner);
        sim.Run(40);
        Kill(miner);
        sim.Run(5 * Second);
        Assert.That(sim.State.Map.RockHp(6, 3), Is.EqualTo(60));
    }

    [Test]
    public void Edge_TunnelLongerThan32_DigsTheFirst32()
    {
        var map = new MapSetup(48, 4);
        for (int x = 4; x < 44; x++)
            for (int y = 0; y < 4; y++)
                Rock(map, '%', (x, y));
        var sim = Sim(map);
        Spawn(sim, B, "scout", 46, 1);
        sim.State.Fog.Update(sim.State);
        Unit miner = Spawn(sim, A, "miner", 2, 1);
        Assert.That(sim.State.Fog.IsDiscovered(A, 43, 1), Is.True, "the far end is seen by the ally");
        Dig(sim, 43, 1, units: miner);
        sim.Tick();
        List<(int X, int Y)> tunnel = Tunnel(miner);
        Assert.That(tunnel, Has.Count.EqualTo(32));
        Assert.That((tunnel[0].X, tunnel[31].X), Is.EqualTo((4, 35)));
    }

    [Test]
    public void Edge_OreRockTouchingTwoDeposits_JoinsTheLowerId_NoMerge()
    {
        var map = Rock(new MapSetup(16, 8).OreRect(4, 3, 2, 1, Resources.Primary).OreRect(7, 3, 2, 1, Resources.Primary), 'R', (6, 3));
        var sim = Sim(map);
        MapState m = sim.State.Map;
        int left = m.DepositAt(4, 3), right = m.DepositAt(7, 3);
        Assert.That(left, Is.LessThan(right));
        m.DamageRock(6, 3, 1000);
        Assert.That(m.DepositAt(6, 3), Is.EqualTo(left));
        Assert.That(m.DepositAt(7, 3), Is.EqualTo(right), "the two deposits stay apart");
    }

    [Test]
    public void Edge_NoDiagonalTunnel_ARockCellReachableOnlyByItsCorner_CannotDig()
    {
        var map = new MapSetup(16, 10);
        Rock(map, '%', (6, 3), (7, 4));
        Rock(map, '#', (7, 3), (6, 4), (8, 4), (7, 5));
        var sim = Sim(map);
        Spawn(sim, B, "scout", 6, 5);
        Unit miner = Spawn(sim, A, "miner", 5, 3);
        Dig(sim, 7, 4, units: miner);
        Assert.That(Rejected(sim), Is.EqualTo(RejectReason.CannotDig));
    }

    [Test]
    public void Edge_GroupFromTwoSides_EachDigsItsOwnTunnel()
    {
        var sim = Sim(Column(Column(Column(new MapSetup(16, 8), '%', 6), '%', 7), '%', 8));
        Unit west = Spawn(sim, A, "miner", 4, 3), east = Spawn(sim, A, "miner", 10, 3);
        // The west face is what the west miner sees; the east one tunnels all the way through to it.
        Dig(sim, 6, 3, units: new[] { west, east });
        sim.Tick();
        Assert.That(Tunnel(west), Is.EqualTo(new[] { (6, 3) }));
        Assert.That(Tunnel(east), Is.EqualTo(new[] { (8, 3), (7, 3), (6, 3) }));
    }

    [Test]
    public void Edge_FromThePlateau_RockStillBlocksSightDownward()
    {
        var map = new MapSetup(24, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 12; x < 24; x++)
                map.Level(x, y, 1);
        Rock(map, '#', (12, 3));
        var sim = Sim(map);
        Spawn(sim, A, "scout", 14, 3);
        Assert.That(sim.State.Fog.IsVisible(A, 10, 3), Is.False);
        Assert.That(sim.State.Fog.IsVisible(A, 10, 5), Is.True);
    }

    private static Building Place(Simulation sim, int owner, string type, int x, int y)
    {
        Building b = sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, Direction.Right, complete: true);
        sim.State.Fog.Update(sim.State);
        return b;
    }
}
