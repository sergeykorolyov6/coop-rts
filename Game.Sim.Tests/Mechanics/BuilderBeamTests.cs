using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;
using static Game.Sim.Tests.ConstructionTests;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/builder-beam.md (U7): the silhouette waits for resources, the builder works
// from build_range, and builds a line on the move.
public class BuilderBeamTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB;

    // A at (2,2) (headquarters 4×4 over cells 2–5), B at (20,2); ore 2×2 at (13,6); a scout of A
    // over (12,8) so every site below is discovered.
    private static Simulation Field()
    {
        var map = new MapSetup(32, 16).Start(A, 2, 2).Start(B, 26, 2).OreRect(13, 6, 2, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Spawn(sim, A, "scout", 12, 8);
        sim.Tick();
        return sim;
    }

    private static Unit Builder(Simulation sim, int player = A) => sim.State.FindBuilder(player)!;

    private static void Put(Unit unit, int milliX, int milliY)
    {
        unit.X = milliX;
        unit.Y = milliY;
    }

    private static void SetWallet(Simulation sim, int player, int primary, int secondary = 0)
    {
        Wallet wallet = sim.State.GetPlayer(player).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary));
        wallet.TrySpend(Resources.Secondary, wallet.Get(Resources.Secondary));
        wallet.Add(Resources.Primary, primary);
        wallet.Add(Resources.Secondary, secondary);
    }

    private static BuildCommand Line(Simulation sim, Unit worker, int x0, int y, int count) =>
        new BuildCommand(worker.OwnerId, sim.State.Tick, new[] { worker.Id }, "conveyor",
            Enumerable.Range(x0, count).Select(x => new BuildSite(x, y, Direction.Right)).ToArray());

    private static long Reach(Unit unit, int x, int y, int w, int h) => Movement.DistanceToRectangle(unit, x, y, w, h);

    private static int Range(Simulation sim) => sim.State.Rules.BuildRangeMilli;

    // ---------- U7.1: the silhouette waits for resources ----------

    [Test]
    public void GWT1_NoMoney_OrderAccepted_BuilderWaitsInRange_OneWaitingEvent_NoSite()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Put(builder, Center(3), Center(7));
        SetWallet(sim, A, 0);

        sim.Order(Build(sim, A, "drill", 13, 6, builder));
        var rejected = new List<CommandRejected>();
        var waiting = new List<ConstructionWaiting>();
        sim.Run(200, s =>
        {
            rejected.AddRange(s.Events.OfType<CommandRejected>());
            waiting.AddRange(s.Events.OfType<ConstructionWaiting>());
        });

        Assert.That(rejected, Is.Empty);
        Assert.That(waiting, Has.Count.EqualTo(1));
        Assert.That((waiting[0].PlayerId, waiting[0].UnitId, waiting[0].BuildingTypeId, waiting[0].CellX, waiting[0].CellY),
            Is.EqualTo((A, builder.Id, "drill", 13, 6)));
        Assert.That(builder.Order.Kind, Is.EqualTo(UnitOrderKind.Build));
        Assert.That(builder.Worker!.WaitingForResources, Is.True);
        Assert.That((builder.Worker.CurrentPlan?.CellX, builder.Worker.CurrentPlan?.CellY), Is.EqualTo(((int?)13, (int?)6)));
        Assert.That(SiteAt(sim, 13, 6), Is.Null);
        // It walked only into build range, not up to the site.
        long reach = Reach(builder, 13, 6, 2, 2);
        Assert.That(reach, Is.LessThanOrEqualTo(Range(sim)));
        Assert.That(reach, Is.GreaterThan(Range(sim) - Cells(1)));
    }

    [Test]
    public void GWT2_WaitingBuilder_MoneyArrives_PaysAndStartsTheSameTurn()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Put(builder, Center(3), Center(7));
        SetWallet(sim, A, 0);
        sim.Order(Build(sim, A, "drill", 13, 6, builder));
        sim.RunUntil(s => builder.Worker!.WaitingForResources, 200);

        SetWallet(sim, A, 30);
        sim.Tick();

        Assert.That(Wallet(sim, A), Is.EqualTo(0));
        Assert.That(SiteAt(sim, 13, 6), Is.Not.Null);
        Assert.That(builder.Worker!.WaitingForResources, Is.False);
    }

    [Test]
    public void GWT3_LineOfFive_MoneyForTwo_TwoBuilt_WaitsAtTheThird()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Put(builder, Center(6), Center(10));
        SetWallet(sim, A, 2);

        sim.Order(Line(sim, builder, 7, 10, 5));
        sim.Run(5 * Simulation.TicksPerSecond);

        Assert.That(SiteAt(sim, 7, 10)?.IsComplete, Is.True);
        Assert.That(SiteAt(sim, 8, 10)?.IsComplete, Is.True);
        Assert.That(SiteAt(sim, 9, 10), Is.Null);
        Assert.That(builder.Worker!.WaitingForResources, Is.True);
        Assert.That(builder.Worker.Plans.Select(p => p.CellX), Is.EqualTo(new[] { 9, 10, 11 }));
    }

    [Test]
    public void GWT4_WaitingAtTheThird_OneMore_BuildsIt_ThenWaitsAgain()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Put(builder, Center(6), Center(10));
        SetWallet(sim, A, 2);
        sim.Order(Line(sim, builder, 7, 10, 5));
        sim.Run(5 * Simulation.TicksPerSecond);

        SetWallet(sim, A, 1);
        sim.Tick();
        Assert.That(SiteAt(sim, 9, 10), Is.Not.Null);
        var waiting = new List<ConstructionWaiting>();
        sim.Run(2 * Simulation.TicksPerSecond, s => waiting.AddRange(s.Events.OfType<ConstructionWaiting>()));

        Assert.That(SiteAt(sim, 9, 10)!.IsComplete, Is.True);
        Assert.That(SiteAt(sim, 10, 10), Is.Null);
        Assert.That(waiting.Select(w => w.CellX), Is.EqualTo(new[] { 10 }));
        Assert.That(builder.Worker!.WaitingForResources, Is.True);
    }

    // ---------- U7.2: build range ----------

    [Test]
    public void GWT5_FourCellsFromTheEdge_BuildsWithoutMoving_OnItsFirstTurn()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        SetWallet(sim, A, 100, 100);
        Put(builder, Cells(12) - Cells(4), Center(6));
        int x = builder.X, y = builder.Y;

        sim.Order(Build(sim, A, "turret", 12, 6, builder));
        sim.Tick();

        Assert.That(SiteAt(sim, 12, 6), Is.Not.Null);
        Assert.That((builder.X, builder.Y), Is.EqualTo((x, y)));
    }

    [Test]
    public void GWT6_SevenCellsAway_WalksUntilFourOrLess_BuildsFromThere()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        SetWallet(sim, A, 100, 100);
        Put(builder, Cells(12) - Cells(7), Center(6));

        sim.Order(Build(sim, A, "turret", 12, 6, builder));
        sim.RunUntil(s => SiteAt(s, 12, 6) != null, 200);
        long reach = Reach(builder, 12, 6, 2, 2);
        Assert.That(reach, Is.LessThanOrEqualTo(Range(sim)));
        Assert.That(reach, Is.GreaterThan(Range(sim) - Cells(1)));

        int x = builder.X, y = builder.Y;
        sim.Run(20);
        Assert.That((builder.X, builder.Y), Is.EqualTo((x, y)), "builds from there");
        Assert.That(SiteAt(sim, 12, 6)!.BuildersLastTick, Is.EqualTo(1));
    }

    [Test]
    public void GWT7_BelowTheCliffTwoCellsAway_GoesUpTheRamp_NeverBuildsFromBelow()
    {
        // x < 12 is level 0, x ≥ 12 the plateau of level 1; a ramp of level 0 at (11,13)–(11,14).
        var map = new MapSetup(24, 16).Start(A, 2, 2);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 12; x < 24; x++)
                map.Level(x, y, 1);
        }
        map.Level(11, 13, 0, ramp: true).Level(11, 14, 0, ramp: true);
        var sim = new Simulation(Setups.Coop(map));
        Spawn(sim, A, "scout", 15, 3);
        sim.Tick();
        Unit builder = Builder(sim);
        SetWallet(sim, A, 100, 100);
        Put(builder, Center(10), Center(3));

        sim.Order(Build(sim, A, "turret", 12, 3, builder));
        sim.RunUntil(s => s.State.Map.BuildingAt(12, 3) != 0, 60 * Simulation.TicksPerSecond);

        MapState m = sim.State.Map;
        Assert.That(m.LevelsJoin(builder.CellX, builder.CellY, 12, 3), Is.True);
        Assert.That(m.LevelAt(builder.CellX, builder.CellY) == 1 || m.IsRamp(builder.CellX, builder.CellY), Is.True);
    }

    [Test]
    public void GWT8_DamagedOwnDrill_FourCellsAway_RepairsWithoutApproaching()
    {
        var sim = Field();
        Building drill = sim.State.PlaceBuilding(A, sim.State.Data.Building("drill"), 13, 6, Direction.Right, complete: true).NoSelfRepair();
        drill.Hp = 150;
        Unit builder = Builder(sim);
        Put(builder, Cells(13) - Cells(4), Center(6));
        int x = builder.X, y = builder.Y;

        sim.Order(Repair(sim, A, drill, builder));
        sim.Run(40);

        Assert.That(drill.Hp, Is.GreaterThan(150));
        Assert.That((builder.X, builder.Y), Is.EqualTo((x, y)));
    }

    [Test]
    public void GWT9_WoundedAllyInfantry_FourCellsAway_ApproachesToOneCell_AsBefore()
    {
        var sim = Field();
        Unit patient = Spawn(sim, B, "infantry", 12, 12);
        patient.Hp = patient.MaxHp / 2;
        int wounded = patient.Hp;
        Unit builder = Builder(sim);
        Put(builder, patient.X - Cells(4) - builder.Radius - patient.Radius, patient.Y);

        sim.Order(new RepairCommand(A, sim.State.Tick, new[] { builder.Id }, patient.Id));
        sim.RunUntil(s => patient.Hp > wounded, 200);

        long gap = Movement.Distance(builder.X, builder.Y, patient.X, patient.Y) - builder.Radius - patient.Radius;
        Assert.That(gap, Is.LessThanOrEqualTo(sim.State.Rules.InteractRangeMilli));
    }

    // ---------- U7.3: building on the move ----------

    [Test]
    public void GWT10_StraightLineOfTen_DoneWithin80Ticks_BuilderMovedAtLeastFiveCells()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        SetWallet(sim, A, 100);
        Put(builder, Center(5), Center(10));
        int startX = builder.X;

        sim.Order(Line(sim, builder, 6, 10, 10));
        sim.Run(80);

        for (int x = 6; x < 16; x++)
            Assert.That(SiteAt(sim, x, 10)?.IsComplete, Is.True, $"cell {x}");
        Assert.That(builder.X - startX, Is.GreaterThanOrEqualTo(Cells(5)));
    }

    [Test]
    public void GWT11_WaitingBuilder_ToldToMove_Goes_SilhouetteDropped()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Put(builder, Center(3), Center(7));
        SetWallet(sim, A, 0);
        sim.Order(Build(sim, A, "drill", 13, 6, builder));
        sim.RunUntil(s => builder.Worker!.WaitingForResources, 200);

        sim.Order(sim.Move(A, Center(4), Center(12), builder));
        sim.Tick();

        Assert.That(builder.Order.Kind, Is.EqualTo(UnitOrderKind.Move));
        Assert.That(builder.Worker!.Plans, Is.Empty);
        Assert.That(builder.Worker.WaitingForResources, Is.False);
    }

    [Test]
    public void GWT12_WaitingBuilder_AllyBuildsThere_PlaceTaken_SilhouetteDropped()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Put(builder, Center(3), Center(7));
        SetWallet(sim, A, 0);
        sim.Order(Build(sim, A, "drill", 13, 6, builder));
        sim.RunUntil(s => builder.Worker!.WaitingForResources, 200);

        sim.State.PlaceBuilding(B, sim.State.Data.Building("storage"), 13, 6, Direction.Right, complete: true);
        var rejected = sim.RunCollecting<CommandRejected>(2);

        Assert.That(rejected.Select(r => (r.Reason, r.UnitId)), Is.EqualTo(new[] { (RejectReason.PlaceTaken, builder.Id) }));
        Assert.That(builder.Worker!.Plans, Is.Empty);
        Assert.That(builder.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
        Assert.That(builder.Worker.WaitingForResources, Is.False);
    }

    // ---------- Edge cases ----------

    [Test]
    public void Edge_QueuedSilhouette_NoMoney_Waits_AndTheRestOfTheQueueWaits()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        Put(builder, Center(3), Center(7));
        SetWallet(sim, A, 0);
        sim.Order(sim.Move(A, Center(6), Center(7), builder));
        sim.Order(new BuildCommand(A, sim.State.Tick, new[] { builder.Id }, "drill", new[] { new BuildSite(13, 6, Direction.Right) }, queued: true));
        sim.Order(new MoveCommand(A, sim.State.Tick, new[] { builder.Id }, Center(4), Center(12), queued: true));
        sim.Run(300);

        Assert.That(builder.Order.Kind, Is.EqualTo(UnitOrderKind.Build));
        Assert.That(builder.Worker!.WaitingForResources, Is.True);
        Assert.That(builder.OrderQueue.Select(o => o.Order.Kind), Is.EqualTo(new[] { UnitOrderKind.Move }));
    }

    [Test]
    public void Edge_OwnerWaits_AllyHelpsOnlyOnceStarted_AndNeverPays()
    {
        var sim = Field();
        Unit owner = Builder(sim, A);
        Unit ally = Builder(sim, B);
        Put(owner, Center(10), Center(10));
        Put(ally, Center(16), Center(10));
        SetWallet(sim, A, 0);
        SetWallet(sim, B, 0);
        sim.Order(Build(sim, A, "drill", 13, 6, owner));
        sim.RunUntil(s => owner.Worker!.WaitingForResources, 200);
        Assert.That(SiteAt(sim, 13, 6), Is.Null, "nothing for the ally to help with yet");

        SetWallet(sim, A, 30);
        sim.Tick();
        Building site = SiteAt(sim, 13, 6)!;
        sim.Order(Repair(sim, B, site, ally));
        sim.RunUntil(s => site.BuildersLastTick == 2, 200);

        Assert.That(ally.Worker!.WaitingForResources, Is.False);
        Assert.That(Wallet(sim, B), Is.EqualTo(0));
        sim.RunUntil(s => site.IsComplete, 400);
    }

    [Test]
    public void Edge_BuilderStandsOnTheSite_StartsAndIsPushedOut()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        SetWallet(sim, A, 100);
        Put(builder, Cells(14), Cells(7));

        sim.Order(Build(sim, A, "drill", 13, 6, builder));
        sim.Tick();
        Assert.That(SiteAt(sim, 13, 6), Is.Not.Null);
        sim.Run(20);
        Assert.That(SiteAt(sim, 13, 6)!.OccupiesCell(builder.CellX, builder.CellY), Is.False);
    }

    [Test]
    public void Edge_SameCommandsAndSeed_SameHashEveryTick_WaitingIncluded()
    {
        List<ulong> Play()
        {
            var sim = Field();
            Unit builder = Builder(sim);
            Put(builder, Center(6), Center(10));
            SetWallet(sim, A, 3);
            sim.Order(Line(sim, builder, 7, 10, 6));
            var hashes = new List<ulong>();
            sim.Run(200, s => hashes.Add(s.StateHash));
            return hashes;
        }

        Assert.That(Play(), Is.EqualTo(Play()));
    }

    [Test]
    public void Edge_WaitingFlag_IsInTheHash()
    {
        var sim = Field();
        Unit builder = Builder(sim);
        var hasher = new StateHasher();
        ulong before = hasher.Compute(sim.State);
        builder.Worker!.WaitingForResources = true;
        Assert.That(hasher.Compute(sim.State), Is.Not.EqualTo(before));
    }

    [Test]
    public void Edge_LineUpTheRamp_BuiltOnTheMove_AsOnFlatGround()
    {
        // Columns: x < 9 level 0, x = 9 a ramp of level 0, x ≥ 10 level 1.
        var map = new MapSetup(24, 16).Start(A, 2, 2);
        for (int y = 0; y < 16; y++)
        {
            map.Level(9, y, 0, ramp: true);
            for (int x = 10; x < 24; x++)
                map.Level(x, y, 1);
        }
        var sim = new Simulation(Setups.Coop(map));
        Spawn(sim, A, "scout", 12, 8);
        sim.Tick();
        Unit builder = Builder(sim);
        SetWallet(sim, A, 100);
        Put(builder, Center(5), Center(10));

        sim.Order(Line(sim, builder, 6, 10, 10));
        sim.Run(80);

        for (int x = 6; x < 16; x++)
            Assert.That(SiteAt(sim, x, 10)?.IsComplete, Is.True, $"cell {x}");
    }

    // ---------- the preview tells place from money ----------

    [Test]
    public void Preview_NoMoney_PlaceIsFine_ShortfallIsReported()
    {
        var sim = Field();
        SetWallet(sim, A, 10, 3);

        Assert.That(PlacementPreview.Check(sim.State, A, "turret", 12, 6, Direction.Right), Is.Null);
        Assert.That(PlacementPreview.Shortfall(sim.State, A, "turret"), Is.EqualTo((30, 7)));
        SetWallet(sim, A, 100, 100);
        Assert.That(PlacementPreview.Shortfall(sim.State, A, "turret"), Is.EqualTo((0, 0)));
    }
}
