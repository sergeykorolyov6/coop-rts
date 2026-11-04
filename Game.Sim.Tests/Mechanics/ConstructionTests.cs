using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/construction.md (and the build rules it shares with drill.md).
public class ConstructionTests
{
    // A at (2,2), B at (20,2); open ground between, all inside headquarters' vision.
    internal static Simulation Match(int width = 32, int height = 16)
    {
        var map = new MapSetup(width, height).Start(Setups.PlayerA, 2, 2).Start(Setups.PlayerB, 20, 2);
        return new Simulation(Setups.Coop(map));
    }

    internal static Unit Worker(Simulation sim, int player, int index = 0) => sim.State.UnitsOf(player)[index];

    internal static int Wallet(Simulation sim, int player, int type = Resources.Primary) =>
        sim.State.GetPlayer(player).Wallet.Get(type);

    internal static BuildCommand Build(Simulation sim, int player, string type, int x, int y, params Unit[] workers) =>
        new BuildCommand(player, sim.State.Tick, workers.Select(w => w.Id).ToArray(), type, new[] { new BuildSite(x, y, Direction.Right) });

    internal static RepairCommand Repair(Simulation sim, int player, Building target, params Unit[] workers) =>
        new RepairCommand(player, sim.State.Tick, workers.Select(w => w.Id).ToArray(), target.Id);

    internal static Building? SiteAt(Simulation sim, int x, int y) => sim.State.FindBuilding(sim.State.Map.BuildingAt(x, y));

    [Test]
    public void GWT1_SecondHeadquarters_Rejected_NothingSpent()
    {
        var sim = Match();
        sim.State.GetPlayer(Setups.PlayerA).Wallet.Add(Resources.Primary, 500);
        int before = Wallet(sim, Setups.PlayerA);

        sim.Order(Build(sim, Setups.PlayerA, "headquarters", 8, 4, Worker(sim, Setups.PlayerA)));
        sim.Tick();

        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.OneHeadquartersOnly));
        sim.Run(200);
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(before));
        Assert.That(sim.State.Buildings.Count(b => b.OwnerId == Setups.PlayerA && b.Stats.Id == "headquarters"), Is.EqualTo(1));
    }

    [Test]
    public void GWT2_AllyRepairsDrill_PaysHalfPrice_OwnerPaysNothing()
    {
        var sim = Match();
        Building drill = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), 12, 6, Direction.Right, complete: true);
        drill.Hp = 150;
        int a = Wallet(sim, Setups.PlayerA), b = Wallet(sim, Setups.PlayerB);

        Unit worker = Worker(sim, Setups.PlayerB);
        sim.Order(Repair(sim, Setups.PlayerB, drill, worker));
        sim.RunUntil(s => drill.Hp == drill.MaxHp);

        // 150 HP × 30 / 600 = 7.5 → 7 whole units charged so far (construction.md, «Реализация»).
        Assert.That(b - Wallet(sim, Setups.PlayerB), Is.InRange(7, 8));
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(a));
        sim.Run(2);
        Assert.That(worker.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void GWT3_HeadquartersDestroyed_NewOneMayBeBuilt_EverythingElseStays()
    {
        var sim = Match();
        Building hq = sim.State.FindHeadquarters(Setups.PlayerA)!;
        Building factory = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("factory"), 10, 10, Direction.Right, complete: true);
        hq.Hp = 0;
        hq.IsDead = true;
        sim.Tick();
        int wallet = Wallet(sim, Setups.PlayerA);
        Assert.That(wallet, Is.GreaterThanOrEqualTo(100));

        sim.Order(Build(sim, Setups.PlayerA, "headquarters", 2, 2, Worker(sim, Setups.PlayerA)));
        sim.RunUntil(s => SiteAt(s, 2, 2) != null);

        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(wallet - 100));
        Assert.That(sim.State.FindBuilding(factory.Id), Is.Not.Null);
        Assert.That(sim.State.UnitsOf(Setups.PlayerA), Has.Count.EqualTo(1));
        Assert.That(sim.State.FindHeadquarters(Setups.PlayerA)!.IsComplete, Is.False);
    }

    [Test]
    public void Edge_RepairWithAFractionalPrice_IsChargedInWholeUnitsAsItGoes()
    {
        var sim = Match();
        Building drill = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("drill"), 12, 6, Direction.Right, complete: true);
        drill.Hp = 280;  // 20 HP missing = exactly 1 unit (30 × 20 = 600)
        int before = Wallet(sim, Setups.PlayerA);
        sim.Order(Repair(sim, Setups.PlayerA, drill, Worker(sim, Setups.PlayerA)));
        sim.RunUntil(s => drill.Hp == drill.MaxHp);
        Assert.That(before - Wallet(sim, Setups.PlayerA), Is.EqualTo(1));
    }

    [Test]
    public void Edge_RepairerRunsOutOfResources_RepairStops_KeepsWhatWasRestored()
    {
        var sim = Match();
        Building factory = sim.State.PlaceBuilding(Setups.PlayerB, sim.State.Data.Building("factory"), 12, 6, Direction.Right, complete: true);
        factory.Hp = 100;
        var wallet = sim.State.GetPlayer(Setups.PlayerA).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary) - 2);  // 2 units left

        Unit worker = Worker(sim, Setups.PlayerA);
        sim.Order(Repair(sim, Setups.PlayerA, factory, worker));
        sim.RunUntil(s => worker.Order.Kind == UnitOrderKind.Idle);

        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(0));
        // Factory: 50 units, 1200 HP, half price → 1 unit per 48 HP; 2 units buy 96 HP, the next one can't be paid.
        Assert.That(factory.Hp, Is.InRange(100 + 96, 100 + 96 + 47));
        Assert.That(factory.Hp, Is.LessThan(factory.MaxHp));
    }

    [Test]
    public void Fail_WorkerLeavesTheSite_ItFreezes_AllyFinishesIt_OwnerKeepsIt()
    {
        var sim = Match();
        Unit a = Worker(sim, Setups.PlayerA);
        sim.Order(Build(sim, Setups.PlayerA, "factory", 12, 6, a));
        sim.RunUntil(s => SiteAt(s, 12, 6) != null);
        Building site = SiteAt(sim, 12, 6)!;
        sim.Run(50);

        sim.Order(sim.Move(Setups.PlayerA, Center(8), Center(14), a));
        var frozen = sim.RunCollecting<ConstructionFrozen>(5);
        Assert.That(frozen.Single().BuildingId, Is.EqualTo(site.Id));
        Assert.That(site.State, Is.EqualTo(BuildState.Frozen));
        int progress = site.BuildProgress;
        sim.Run(40);
        Assert.That(site.BuildProgress, Is.EqualTo(progress));

        int b = Wallet(sim, Setups.PlayerB);
        sim.Order(Repair(sim, Setups.PlayerB, site, Worker(sim, Setups.PlayerB)));
        sim.RunUntil(s => site.IsComplete);
        Assert.That(site.OwnerId, Is.EqualTo(Setups.PlayerA));
        Assert.That(Wallet(sim, Setups.PlayerB), Is.EqualTo(b));
    }

    [Test]
    public void Fail_PlaceTakenWhileWalking_PlaceTakenEvent_NothingSpent()
    {
        var sim = Match();
        Unit a = Worker(sim, Setups.PlayerA);
        int before = Wallet(sim, Setups.PlayerA);
        sim.Order(Build(sim, Setups.PlayerA, "factory", 12, 6, a));
        sim.Tick();
        sim.State.PlaceBuilding(Setups.PlayerB, sim.State.Data.Building("conveyor"), 13, 7, Direction.Right, complete: true);

        var rejected = sim.RunCollecting<CommandRejected>(200);
        Assert.That(rejected.Single().Reason, Is.EqualTo(RejectReason.PlaceTaken));
        Assert.That(rejected.Single().UnitId, Is.EqualTo(a.Id));
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(before));
        Assert.That(a.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void Fail_ResourcesSpentWhileWalking_NotEnoughResources_NothingSpent()
    {
        var sim = Match();
        Unit a = Worker(sim, Setups.PlayerA);
        sim.Order(Build(sim, Setups.PlayerA, "factory", 12, 6, a));
        sim.Tick();
        var wallet = sim.State.GetPlayer(Setups.PlayerA).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary) - 10);

        var rejected = sim.RunCollecting<CommandRejected>(200);
        Assert.That(rejected.Single().Reason, Is.EqualTo(RejectReason.NotEnoughResources));
        Assert.That(Wallet(sim, Setups.PlayerA), Is.EqualTo(10));
        Assert.That(SiteAt(sim, 12, 6), Is.Null);
        // The silhouette stays until the player gives another order (conveyors.md, fail cases).
        Assert.That(a.Worker!.Plans, Has.Count.EqualTo(1));
    }

    [Test]
    public void Order_NotEnoughResources_RejectedRightAway()
    {
        var sim = Match();
        sim.State.GetPlayer(Setups.PlayerA).Wallet.TrySpend(Resources.Primary, 100);
        sim.Order(Build(sim, Setups.PlayerA, "factory", 12, 6, Worker(sim, Setups.PlayerA)));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotEnoughResources));
    }

    [Test]
    public void Order_OnUndiscoveredCells_Rejected()
    {
        var sim = Match(64, 16);
        sim.Order(Build(sim, Setups.PlayerA, "factory", 58, 10, Worker(sim, Setups.PlayerA)));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotDiscovered));
    }

    [Test]
    public void BuildTime_OneWorker_IsBuildTimeOfTheTable()
    {
        var sim = Match();
        Unit a = Worker(sim, Setups.PlayerA);
        sim.Order(Build(sim, Setups.PlayerA, "factory", 12, 6, a));
        sim.RunUntil(s => SiteAt(s, 12, 6) != null);
        Building site = SiteAt(sim, 12, 6)!;
        int ticks = sim.RunUntil(s => site.IsComplete);
        // Factory: 35 s = 700 units of work; the start tick already counts one.
        Assert.That(ticks + 1, Is.EqualTo(35 * Simulation.TicksPerSecond));
        Assert.That(site.Hp, Is.EqualTo(site.MaxHp));
        sim.Run(2);
        Assert.That(a.Order.Kind, Is.EqualTo(UnitOrderKind.Idle));
    }

    [Test]
    public void Site_StartsAtTenPercentHp_AndGrowsWithProgress()
    {
        var sim = Match();
        sim.Order(Build(sim, Setups.PlayerA, "factory", 12, 6, Worker(sim, Setups.PlayerA)));
        sim.RunUntil(s => SiteAt(s, 12, 6) != null);
        Building site = SiteAt(sim, 12, 6)!;
        Assert.That(site.Hp, Is.InRange(120, 122));  // 10 % of 1200, plus the first tick of work
        sim.RunUntil(s => site.BuildProgress >= 350);
        Assert.That(site.Hp, Is.InRange(655, 665));   // half way: 120 + 1080 / 2
    }

    [Test]
    public void MaxBuilders_ExtraWorkersDoNotSpeedItUp()
    {
        var map = new MapSetup(32, 16).Start(Setups.PlayerA, 2, 2).OreRect(10, 2, 2, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        // One builder per player in a match; extra ones here stand in for allies' builders.
        sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("builder"), Center(7), Center(2));
        sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("builder"), Center(7), Center(3));
        var a = sim.State.UnitsOf(Setups.PlayerA);
        var extra = sim.State.SpawnUnit(Setups.PlayerA, sim.State.Data.Unit("builder"), Center(7), Center(1));
        sim.State.GetPlayer(Setups.PlayerA).Wallet.Add(Resources.Primary, 100);
        sim.Order(Build(sim, Setups.PlayerA, "drill", 10, 2, a[0], a[1], a[2], extra));
        sim.RunUntil(s => SiteAt(s, 10, 2) != null);
        Building site = SiteAt(sim, 10, 2)!;
        sim.RunUntil(s => site.BuildersLastTick == 3);
        sim.Run(10);
        Assert.That(site.BuildersLastTick, Is.EqualTo(3));  // drill: max_builders = 3
        // The fourth one waits: the three lower ids took the slots first.
        Assert.That(extra.Worker!.CurrentPlanKey, Is.EqualTo(-1));
        sim.RunUntil(s => site.IsComplete);
        sim.Run(2);
        Assert.That(a.Concat(new[] { extra }).All(w => w.Order.Kind == UnitOrderKind.Idle), Is.True);
    }

    [Test]
    public void EnemyCannotHelpBuild()
    {
        var map = new MapSetup(32, 16).Start(Setups.PlayerA, 2, 2).Start(Setups.EnemyAi, 20, 2);
        var sim = new Simulation(Setups.Coop(map));
        Building site = sim.State.PlaceBuilding(Setups.PlayerA, sim.State.Data.Building("factory"), 12, 6, Direction.Right, complete: false);
        sim.Order(Repair(sim, Setups.EnemyAi, site, sim.State.UnitsOf(Setups.EnemyAi)[0]));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));
    }

    [Test]
    public void Commands_RoundTripThroughTheCodec()
    {
        var codec = GameCommands.CreateCodec();
        var log = codec.ReadLog(codec.WriteLog(new ICommand[]
        {
            new BuildCommand(1, 3, new[] { 4, 5 }, "conveyor", new[] { new BuildSite(1, 2, Direction.Up), new BuildSite(1, 3, Direction.Left) }),
            new RepairCommand(2, 4, new[] { 6 }, 77),
        }));
        var build = (BuildCommand)log[0];
        Assert.That(build.BuildingTypeId, Is.EqualTo("conveyor"));
        Assert.That(build.Sites.Select(s => (s.CellX, s.CellY, s.Direction)), Is.EqualTo(new[] { (1, 2, Direction.Up), (1, 3, Direction.Left) }));
        Assert.That(((RepairCommand)log[1]).BuildingId, Is.EqualTo(77));
    }
}
