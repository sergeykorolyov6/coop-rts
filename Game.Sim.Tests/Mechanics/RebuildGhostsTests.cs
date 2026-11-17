using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/rebuild-ghosts.md (U5): a building destroyed by damage leaves a ghost;
// the builder rebuilds own ghosts with one order, an ally's at its own cost, repairers by themselves.
public class RebuildGhostsTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB, Enemy = Setups.EnemyAi;
    private const int Second = Simulation.TicksPerSecond;

    // Headquarters of A over (2..5, 2..5), of B over (26..29, 2..5); scouts of A and B light rows 6–14.
    private static Simulation Field(System.Action<MapSetup>? terrain = null)
    {
        var map = new MapSetup(32, 16).Start(A, 2, 2).Start(B, 26, 2);
        terrain?.Invoke(map);
        var sim = new Simulation(Setups.Coop(map));
        Spawn(sim, A, "scout", 8, 12);
        Spawn(sim, B, "scout", 20, 12);
        sim.Tick();
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string type, int x, int y, Direction dir = Direction.Right, bool complete = true) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building(type), x, y, dir, complete);

    // What combat does when a building falls: the same rule, without waiting for the blows.
    private static Building Ruin(Simulation sim, int owner, string type, int x, int y, Direction dir = Direction.Right)
    {
        Building b = Place(sim, owner, type, x, y, dir);
        GhostRules.Leave(sim.State, b);
        Kill(b);
        sim.State.RemoveDead();
        return b;
    }

    private static void SetWallet(Simulation sim, int player, int primary, int secondary = 0)
    {
        Wallet wallet = sim.State.GetPlayer(player).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary));
        wallet.TrySpend(Resources.Secondary, wallet.Get(Resources.Secondary));
        wallet.Add(Resources.Primary, primary);
        wallet.Add(Resources.Secondary, secondary);
    }

    private static int Wallet(Simulation sim, int player) => sim.State.GetPlayer(player).Wallet.Get(Resources.Primary);

    private static List<Ghost> GhostsOf(Simulation sim, int owner) => sim.State.Ghosts.Where(g => g.OwnerId == owner).ToList();

    private static RebuildCommand RebuildAll(Simulation sim, Unit builder, bool allies = false) =>
        new RebuildCommand(builder.OwnerId, sim.State.Tick, new[] { builder.Id }, 0, 0, sim.State.Map.Width - 1, sim.State.Map.Height - 1, allies);

    private static RebuildCommand RebuildOne(Simulation sim, Unit builder, int x, int y) =>
        new RebuildCommand(builder.OwnerId, sim.State.Tick, new[] { builder.Id }, x, y, x, y, includeAllies: true);

    private static Unit Builder(Simulation sim, int player, int cx, int cy)
    {
        Unit b = sim.State.FindBuilder(player)!;
        b.X = Center(cx);
        b.Y = Center(cy);
        return b;
    }

    private static Building? At(Simulation sim, int x, int y) => sim.State.FindBuilding(sim.State.Map.BuildingAt(x, y));

    // ---------- U5.1 ----------

    [Test]
    public void GWT1_BeltDestroyedByARaider_LeavesAGhost_CellFree()
    {
        var sim = Field();
        Building belt = Place(sim, A, "conveyor", 10, 9, Direction.Up);
        // A brute: the scouts nearby would shoot a raider down before it broke the belt.
        Unit raider = Spawn(sim, Enemy, "legion_brute", 11, 9);
        sim.Order(new AttackCommand(Enemy, sim.State.Tick, new[] { raider.Id }, belt.Id));
        sim.RunUntil(s => belt.IsDead, 30 * Second);
        sim.Tick();

        Ghost ghost = GhostsOf(sim, A).Single();
        Assert.That((ghost.BuildingTypeId, ghost.CellX, ghost.CellY, ghost.Direction), Is.EqualTo(("conveyor", 10, 9, Direction.Up)));
        Assert.That(sim.State.Map.BuildingAt(10, 9), Is.EqualTo(0));
        Assert.That(sim.State.Map.IsPassable(10, 9), Is.True);
    }

    [Test]
    public void GWT2_OwnerDemolishes_NoGhost()
    {
        var sim = Field();
        Building drill = Place(sim, A, "drill", 10, 9);
        sim.Order(new DemolishCommand(A, sim.State.Tick, drill.Id));
        sim.RunUntil(s => s.State.FindBuilding(drill.Id) == null, 200);

        Assert.That(sim.State.Ghosts, Is.Empty);
    }

    [Test]
    public void GWT3_UnfinishedTurretDestroyed_LeavesAGhost()
    {
        var sim = Field();
        Building site = Place(sim, A, "turret", 10, 9, complete: false);
        Unit raider = Spawn(sim, Enemy, "legion_raider", 12, 9);
        sim.Order(new AttackCommand(Enemy, sim.State.Tick, new[] { raider.Id }, site.Id));
        sim.RunUntil(s => site.IsDead, 60 * Second);

        Assert.That(GhostsOf(sim, A).Select(g => g.BuildingTypeId), Is.EqualTo(new[] { "turret" }));
    }

    [Test]
    public void GWT4_LegionFactoryDestroyed_NoGhost()
    {
        var sim = Field();
        Building factory = Place(sim, Enemy, "legion_factory", 10, 9);
        GhostRules.Leave(sim.State, factory);

        Assert.That(sim.State.Ghosts, Is.Empty);
    }

    [Test]
    public void GWT5_AllyStartsBuildingOnTheGhost_GhostGone()
    {
        var sim = Field();
        Ruin(sim, A, "conveyor", 10, 9);
        Place(sim, B, "storage", 10, 9, complete: false);

        Assert.That(sim.State.Ghosts, Is.Empty);
    }

    [Test]
    public void GWT6_ThreeBeltGhosts_RebuildAll_InOrderNearestNext_EachGhostGoesWithItsStart_ThreePaid()
    {
        var sim = Field();
        for (int x = 7; x >= 5; x--)
            Ruin(sim, A, "conveyor", x, 9);
        Unit builder = Builder(sim, A, 3, 9);
        int wallet = Wallet(sim, A);

        sim.Order(RebuildAll(sim, builder));
        sim.Tick();
        Assert.That(builder.Worker!.Plans.Select(p => (p.CellX, p.CellY)).Take(3), Is.EqualTo(new[] { (5, 9), (6, 9), (7, 9) }));

        sim.RunUntil(s => At(s, 5, 9) != null, 200);
        Assert.That(GhostsOf(sim, A).Select(g => g.CellX), Is.EqualTo(new[] { 6, 7 }).Or.EquivalentTo(new[] { 7, 6 }));
        sim.RunUntil(s => Enumerable.Range(5, 3).All(x => At(s, x, 9)?.IsComplete == true), 400);

        Assert.That(sim.State.Ghosts, Is.Empty);
        Assert.That(wallet - Wallet(sim, A), Is.EqualTo(3));
    }

    [Test]
    public void GWT7_BothBridgeEndsDestroyed_Rebuilt_AndLinked()
    {
        var sim = Field();
        Ruin(sim, A, "conveyor_bridge", 10, 9, Direction.Right);
        Ruin(sim, A, "conveyor_bridge", 13, 9, Direction.Right);
        Unit builder = Builder(sim, A, 9, 10);

        sim.Order(RebuildAll(sim, builder));
        sim.RunUntil(s => At(s, 10, 9)?.IsComplete == true && At(s, 13, 9)?.IsComplete == true, 600);

        Assert.That(At(sim, 10, 9)!.Bridge!.LinkedToId, Is.EqualTo(At(sim, 13, 9)!.Id));
    }

    [Test]
    public void GWT8_RebuildAll_TakesOnlyOwnGhosts()
    {
        var sim = Field(map => map.OreRect(10, 9, 2, 2, Resources.Primary));
        Ruin(sim, A, "drill", 10, 9);
        Ruin(sim, A, "conveyor", 12, 9);
        Ruin(sim, B, "turret", 16, 9);
        Unit builder = Builder(sim, A, 9, 12);

        sim.Order(RebuildAll(sim, builder));
        sim.Tick();

        Assert.That(builder.Worker!.Plans.Select(p => p.BuildingTypeId), Is.EquivalentTo(new[] { "drill", "conveyor" }));
        Assert.That(GhostsOf(sim, B), Has.Count.EqualTo(1));
    }

    // ---------- U5.2 ----------

    [Test]
    public void GWT9_AllysBuilderRebuildsMyDrill_ItIsMine_TheAllyPays()
    {
        var sim = Field(map => map.OreRect(10, 9, 2, 2, Resources.Primary));
        Ruin(sim, A, "drill", 10, 9);
        SetWallet(sim, A, 0);
        SetWallet(sim, B, 100);
        Unit ally = Builder(sim, B, 13, 10);

        sim.Order(RebuildOne(sim, ally, 10, 9));
        sim.RunUntil(s => At(s, 10, 9)?.IsComplete == true, 600);

        Building drill = At(sim, 10, 9)!;
        Assert.That(drill.OwnerId, Is.EqualTo(A));
        Assert.That(Wallet(sim, B), Is.EqualTo(70));
        Assert.That(Wallet(sim, A), Is.EqualTo(0));
    }

    // ---------- U5.3 ----------

    private static Unit Repairer(Simulation sim, int owner, int cx, int cy) => Spawn(sim, owner, "repairer", cx, cy);

    [Test]
    public void GWT10_IdleRepairerNearby_RebuildsTheBelt_OwnerPaysOne()
    {
        var sim = Field();
        Ruin(sim, A, "conveyor", 10, 9);
        Repairer(sim, A, 15, 9);
        int wallet = Wallet(sim, A);

        sim.RunUntil(s => At(s, 10, 9)?.IsComplete == true, 30 * Second);

        Assert.That(At(sim, 10, 9)!.OwnerId, Is.EqualTo(A));
        Assert.That(wallet - Wallet(sim, A), Is.EqualTo(1));
        Assert.That(sim.State.Ghosts, Is.Empty);
    }

    [Test]
    public void GWT11_ARaiderSeenNearTheGhost_TheRepairerStays()
    {
        var sim = Field();
        // The raider 6 cells from the ghost, seen by the scout at (8,12); out of sight of the repairer,
        // the scouts and the headquarters, so it stands and nothing gets damaged.
        Ruin(sim, A, "conveyor", 10, 9);
        Unit repairer = Repairer(sim, A, 10, 13);
        Spawn(sim, Enemy, "legion_raider", 16, 7);
        sim.Tick();
        Assert.That(sim.State.Fog.IsVisible(A, 16, 7), Is.True);

        sim.Run(5 * Second);

        Assert.That(repairer.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        Assert.That(GhostsOf(sim, A), Has.Count.EqualTo(1));
    }

    [Test]
    public void GWT12_NoMoney_TheRepairerStays_NoEvents()
    {
        var sim = Field();
        Ruin(sim, A, "conveyor", 10, 9);
        Unit repairer = Repairer(sim, A, 15, 9);
        SetWallet(sim, A, 0);

        var rejected = sim.RunCollecting<CommandRejected>(5 * Second);

        Assert.That(repairer.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        Assert.That(rejected, Is.Empty);
        Assert.That(GhostsOf(sim, A), Has.Count.EqualTo(1));
    }

    [Test]
    public void GWT13_DamagedTurretAndAGhost_RepairFirst_ThenRebuild()
    {
        var sim = Field();
        Building turret = Place(sim, A, "turret", 13, 11).NoSelfRepair();
        turret.Hp = turret.MaxHp - 40;
        Ruin(sim, A, "conveyor", 10, 9);
        Unit repairer = Repairer(sim, A, 12, 9);
        SetWallet(sim, A, 100, 100);

        sim.RunUntil(s => repairer.Order.Kind != UnitOrderKind.Idle, 20);
        Assert.That(repairer.Order.Kind, Is.EqualTo(UnitOrderKind.Repair));
        sim.RunUntil(s => turret.Hp == turret.MaxHp, 60 * Second);
        Assert.That(GhostsOf(sim, A), Has.Count.EqualTo(1));

        sim.RunUntil(s => At(s, 10, 9)?.IsComplete == true, 30 * Second);
        Assert.That(sim.State.Ghosts, Is.Empty);
    }

    // ---------- limits, frame, snapshot ----------

    [Test]
    public void GWT14_AtTheLimit_ANewGhostPushesTheOldestOut()
    {
        var sim = Field();
        int limit = sim.State.Rules.GhostLimit;
        Assert.That(limit, Is.EqualTo(256));
        // Each on its own cell (rows 6–15): a building placed over a ghost would take it away.
        for (int i = 0; i < limit; i++)
            Ruin(sim, A, "conveyor", i % 32, 6 + i / 32);
        int oldest = GhostsOf(sim, A).Min(g => g.Id);

        Ruin(sim, A, "conveyor", limit % 32, 6 + limit / 32);

        Assert.That(GhostsOf(sim, A), Has.Count.EqualTo(limit));
        Assert.That(GhostsOf(sim, A).Any(g => g.Id == oldest), Is.False);
    }

    [Test]
    public void GWT15_DemolitionFrameOverOwnGhosts_TheyGo()
    {
        var sim = Field();
        Ruin(sim, A, "conveyor", 6, 7);
        Ruin(sim, A, "conveyor", 8, 7);
        Ruin(sim, A, "conveyor", 12, 7);
        Ruin(sim, B, "conveyor", 7, 7);

        sim.Order(new DemolishAreaCommand(A, sim.State.Tick, 2, 6, 9, 10));
        sim.Tick();

        Assert.That(GhostsOf(sim, A).Select(g => g.CellX), Is.EqualTo(new[] { 12 }));
        Assert.That(GhostsOf(sim, B), Has.Count.EqualTo(1));
    }

    [Test]
    public void GWT16_SnapshotWithGhosts_RestoredGoesStepForStep()
    {
        var map = new MapSetup(32, 16).Start(A, 2, 2).Start(B, 26, 2);
        MatchSetup setup = Setups.Coop(map);
        var sim = new Simulation(setup);
        Spawn(sim, A, "scout", 8, 12);
        sim.Tick();
        Ruin(sim, A, "conveyor", 10, 9);
        Ruin(sim, A, "conveyor", 11, 9);
        Repairer(sim, A, 14, 9);
        sim.Run(3);

        Simulation restored = Simulation.FromSnapshot(setup, sim.WriteSnapshot(), sim.CommandLog);
        Assert.That(restored.State.Ghosts.Select(g => (g.Id, g.CellX)), Is.EqualTo(sim.State.Ghosts.Select(g => (g.Id, g.CellX))));
        for (int i = 0; i < 200; i++)
        {
            sim.Tick();
            restored.Tick();
            Assert.That(restored.StateHash, Is.EqualTo(sim.StateHash), $"tick {sim.State.Tick}");
        }
    }

    [Test]
    public void Ghosts_AreInTheHash()
    {
        var sim = Field();
        var hasher = new StateHasher();
        ulong before = hasher.Compute(sim.State);
        Ruin(sim, A, "conveyor", 10, 9);
        Assert.That(hasher.Compute(sim.State), Is.Not.EqualTo(before));
    }

    // ---------- Edge cases ----------

    [Test]
    public void Edge_DestroyedWhileTheOwnerDemolishedIt_NoGhost()
    {
        var sim = Field();
        Building drill = Place(sim, A, "drill", 10, 9);
        drill.DemolishTicksLeft = 20;
        GhostRules.Leave(sim.State, drill);

        Assert.That(sim.State.Ghosts, Is.Empty);
    }

    [Test]
    public void Edge_CapturedNeutralDrill_LeavesAGhostForItsNewOwner()
    {
        var sim = Field();
        Building drill = Place(sim, sim.State.NeutralPlayerId, "drill", 10, 9);
        drill.OwnerId = A;
        GhostRules.Leave(sim.State, drill);

        Assert.That(GhostsOf(sim, A).Select(g => g.BuildingTypeId), Is.EqualTo(new[] { "drill" }));
    }

    [Test]
    public void Edge_MoreGhostsThanOneOrderTakes_First256_TheRestWait()
    {
        var sim = Field();
        // One player has at most ghost_limit (256) ghosts; with an ally's there can be more in one order.
        for (int i = 0; i < 300; i++)
            Ruin(sim, i < 200 ? A : B, "conveyor", i % 32, 6 + i / 32);
        Unit builder = Builder(sim, A, 6, 6);

        sim.Order(RebuildAll(sim, builder, allies: true));
        sim.Tick();

        Assert.That(builder.Worker!.Plans.Count, Is.EqualTo(BuildCommand.MaxSites));
        Assert.That(sim.State.Ghosts.Count, Is.GreaterThanOrEqualTo(300 - 2));
    }

    [Test]
    public void Edge_BuilderDiesWithRebuildSilhouettes_GhostsStay()
    {
        var sim = Field();
        Ruin(sim, A, "conveyor", 20, 12);
        Ruin(sim, A, "conveyor", 21, 12);
        Unit builder = Builder(sim, A, 6, 6);
        sim.Order(RebuildAll(sim, builder));
        sim.Tick();

        Kill(builder);
        sim.Run(2);

        Assert.That(GhostsOf(sim, A), Has.Count.EqualTo(2));
    }

    [Test]
    public void Edge_TwoRepairersOneDrillGhost_FirstStarts_SecondHelps()
    {
        var sim = Field(map => map.OreRect(10, 9, 2, 2, Resources.Primary));
        Ruin(sim, A, "drill", 10, 9);
        // At the edge of the search radius: both have picked the ghost before the first one starts it.
        Repairer(sim, A, 19, 9);
        Repairer(sim, A, 19, 11);
        SetWallet(sim, A, 100);

        sim.RunUntil(s => At(s, 10, 9)?.BuildersLastTick == 2, 30 * Second);

        Assert.That(At(sim, 10, 9)!.BuildersLastTick, Is.EqualTo(2));
        Assert.That(100 - Wallet(sim, A), Is.EqualTo(30));
    }

    [Test]
    public void Edge_ScavengerRepairbot_RebuildsToo()
    {
        Simulation sim = ScavengerAndHeroTests.Match();
        int brown = Setups.PlayerA;
        Building belt = sim.State.PlaceBuilding(brown, sim.State.Data.Building("conveyor"), 12, 3, Direction.Right, complete: true);
        GhostRules.Leave(sim.State, belt);
        Kill(belt);
        sim.State.RemoveDead();
        Spawn(sim, brown, "bs_repairbot", 15, 3);

        sim.RunUntil(s => s.State.Map.BuildingAt(12, 3) != 0 && s.State.FindBuilding(s.State.Map.BuildingAt(12, 3))!.IsComplete, 30 * Second);

        Assert.That(sim.State.Ghosts, Is.Empty);
    }

    [Test]
    public void Rebuild_RoundTripsThroughTheCodec()
    {
        CommandCodec codec = GameCommands.CreateCodec();
        byte[] encoded = codec.Encode(new RebuildCommand(1, 3, new[] { 4, 9 }, 1, 2, 30, 14, includeAllies: true, queued: true));

        var read = (RebuildCommand)codec.ReadAs(encoded, 1, 3);

        Assert.That((RebuildCommand.Id, read.X0, read.Y0, read.X1, read.Y1, read.IncludeAllies, read.Queued), Is.EqualTo((20, 1, 2, 30, 14, true, true)));
        Assert.That(read.UnitIds, Is.EqualTo(new[] { 4, 9 }));
    }
}
