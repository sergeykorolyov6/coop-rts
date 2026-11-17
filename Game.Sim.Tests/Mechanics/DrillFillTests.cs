using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/drill-fill.md (U9): drills laid out over a frame as a grid, shifted so
// that the most ore ends up under them. DrillFill only reads the state.
public class DrillFillTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB;

    // 24×16; headquarters of A at (16,10), of B at (16,2); a scout of A over (5,5).
    private static Simulation Field(System.Action<MapSetup> terrain, string? factionA = null)
    {
        var map = new MapSetup(24, 16).Start(A, 16, 10).Start(B, 16, 2);
        terrain(map);
        MatchSetup setup = factionA == null ? Setups.Coop(map)
            : new MatchSetup(42, RepoData.Get(), map, 2, new[] { A, B, Setups.EnemyAi, Setups.Creeps },
                new[] { new RelationSetup(A, B, Relation.Ally), new RelationSetup(A, Setups.EnemyAi, Relation.Enemy), new RelationSetup(B, Setups.EnemyAi, Relation.Enemy) },
                creepPlayerId: Setups.Creeps, factions: new[] { new PlayerFaction(A, factionA) });
        var sim = new Simulation(setup);
        Spawn(sim, A, "scout", 5, 5);
        sim.Tick();
        return sim;
    }

    private static Simulation Ore(int x, int y, int w, int h) => Field(map => map.OreRect(x, y, w, h, Resources.Primary));

    private static DrillFillPlan Plan(Simulation sim, int x0, int y0, int x1, int y1, string type = "drill") =>
        DrillFill.Plan(sim.State, A, type, x0, y0, x1, y1);

    private static (int, int)[] Sites(DrillFillPlan plan) => plan.Sites.Select(s => (s.X, s.Y)).ToArray();

    [Test]
    public void GWT1_SixteenOreCells_FourDrills_FourOreEach()
    {
        var sim = Ore(4, 4, 4, 4);
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 7);

        Assert.That(Sites(plan), Is.EquivalentTo(new[] { (4, 4), (6, 4), (4, 6), (6, 6) }));
        Assert.That(plan.Sites.All(s => s.Ore == 4), Is.True);
    }

    [Test]
    public void GWT2_FrameWithAMargin_SameFourDrills_ShiftChosenByOreThenFewerDrills()
    {
        var sim = Ore(4, 4, 4, 4);
        DrillFillPlan plan = Plan(sim, 3, 3, 8, 8);

        Assert.That(Sites(plan), Is.EquivalentTo(new[] { (4, 4), (6, 4), (4, 6), (6, 6) }));
        Assert.That(plan.OreTotal, Is.EqualTo(16));
    }

    [Test]
    public void GWT3_TwelveOreCells_RichestFirst_ThenBottomUpLeftToRight()
    {
        var sim = Ore(4, 4, 3, 4);
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 7);

        Assert.That(plan.Sites.Select(s => (s.X, s.Y, s.Ore)), Is.EqualTo(new[] { (4, 4, 4), (4, 6, 4), (6, 4, 2), (6, 6, 2) }));
    }

    [Test]
    public void GWT4_CellWithOneOreOfFour_NotPlaced()
    {
        var sim = Field(map => map.OreRect(4, 4, 2, 2, Resources.Primary).Ore(6, 4, Resources.Primary));
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 5);

        Assert.That(Sites(plan), Is.EqualTo(new[] { (4, 4) }));
        Assert.That(plan.Skipped.Select(s => (s.X, s.Y, s.Kind)), Is.EqualTo(new[] { (6, 4, DrillFillCellKind.Poor) }));
    }

    [Test]
    public void GWT5_ForeignDrillOnTheOre_ThreeDrills()
    {
        var sim = Ore(4, 4, 4, 4);
        sim.State.PlaceBuilding(B, sim.State.Data.Building("drill"), 6, 6, Direction.Right, complete: true);
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 7);

        Assert.That(Sites(plan), Is.EquivalentTo(new[] { (4, 4), (6, 4), (4, 6) }));
        Assert.That(plan.Skipped.Select(s => (s.X, s.Y, s.Kind)), Is.EqualTo(new[] { (6, 6, DrillFillCellKind.Taken) }));
    }

    [Test]
    public void GWT6_SmallDrill_OnePerOreCell()
    {
        var sim = Field(map => map.OreRect(4, 4, 4, 4, Resources.Primary), "brown_scavengers");
        DrillFillPlan plan = Plan(sim, 4, 4, 5, 5, "bs_drill_small");

        Assert.That(Sites(plan), Is.EquivalentTo(new[] { (4, 4), (5, 4), (4, 5), (5, 5) }));
    }

    [Test]
    public void GWT7_NoOre_NoDrills()
    {
        var sim = Ore(12, 12, 2, 2);
        DrillFillPlan plan = Plan(sim, 2, 2, 7, 7);

        Assert.That(plan.Sites, Is.Empty);
        Assert.That(plan.Skipped, Is.Empty);
    }

    [Test]
    public void GWT8_HalfTheOreUndiscovered_TwoDrillsOnTheDiscoveredHalf()
    {
        var sim = Ore(4, 4, 4, 4);
        bool[] discovered = sim.State.Fog.DiscoveredByIndex(0);
        for (int y = 4; y <= 7; y++)
            for (int x = 6; x <= 7; x++)
                discovered[y * sim.State.Map.Width + x] = false;
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 7);

        Assert.That(Sites(plan), Is.EquivalentTo(new[] { (4, 4), (4, 6) }));
    }

    // ---------- Edge cases ----------

    [Test]
    public void Edge_FrameSmallerThanADrill_Nothing()
    {
        var sim = Ore(4, 4, 4, 4);
        DrillFillPlan plan = Plan(sim, 4, 4, 4, 4);

        Assert.That(plan.Sites, Is.Empty);
    }

    [Test]
    public void Edge_TwoKindsOfOre_EachCellByTheResourceItHasMostOf()
    {
        // Left half primary, right half secondary: each drill counts its own resource.
        var sim = Field(map => map.OreRect(4, 4, 2, 4, Resources.Primary).OreRect(6, 4, 2, 4, Resources.Secondary).Ore(6, 4, Resources.Primary));
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 7);

        Assert.That(plan.Sites.Select(s => (s.X, s.Y, s.Ore, s.ResourceType)), Is.EquivalentTo(new[]
        {
            (4, 4, 4, Resources.Primary), (4, 6, 4, Resources.Primary),
            (6, 4, 3, Resources.Secondary), (6, 6, 4, Resources.Secondary),
        }));
    }

    [Test]
    public void Edge_UnevenGroundUnderACell_Skipped()
    {
        var sim = Field(map =>
        {
            map.OreRect(4, 4, 4, 4, Resources.Primary);
            map.Level(7, 7, 1);
        });
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 7);

        Assert.That(Sites(plan), Is.EquivalentTo(new[] { (4, 4), (6, 4), (4, 6) }));
        Assert.That(plan.Skipped.Select(s => (s.X, s.Y, s.Kind)), Is.EqualTo(new[] { (6, 6, DrillFillCellKind.Taken) }));
    }

    [Test]
    public void Edge_FrameOverTheMapEdge_CutByTheMap()
    {
        var sim = Field(map => map.OreRect(0, 0, 4, 4, Resources.Primary));
        DrillFillPlan plan = Plan(sim, -5, -5, 3, 3);

        Assert.That(Sites(plan), Is.EquivalentTo(new[] { (0, 0), (2, 0), (0, 2), (2, 2) }));
    }

    [Test]
    public void Edge_MoneyForPartOnly_AllOrdered_RichestFirst()
    {
        var sim = Ore(4, 4, 3, 4);
        Wallet wallet = sim.State.GetPlayer(A).Wallet;
        wallet.TrySpend(Resources.Primary, wallet.Get(Resources.Primary) - 30);
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 7);

        Assert.That(plan.Sites, Has.Count.EqualTo(4));
        Assert.That(plan.Sites[0].Ore, Is.EqualTo(4));
    }

    [Test]
    public void Edge_AtMost256Drills()
    {
        var map = new MapSetup(80, 80).Start(A, 70, 70).Start(B, 74, 74).OreRect(0, 0, 66, 66, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        bool[] discovered = sim.State.Fog.DiscoveredByIndex(0);
        for (int i = 0; i < discovered.Length; i++)
            discovered[i] = true;
        DrillFillPlan plan = DrillFill.Plan(sim.State, A, "drill", 0, 0, 65, 65);

        Assert.That(plan.Sites, Has.Count.EqualTo(BuildCommand.MaxSites));
    }

    [Test]
    public void Order_AllSitesInOneBuildCommand_BuiltRichestFirst()
    {
        var sim = Ore(4, 4, 3, 4);
        Unit builder = sim.State.FindBuilder(A)!;
        DrillFillPlan plan = Plan(sim, 4, 4, 7, 7);
        sim.Order(new BuildCommand(A, sim.State.Tick, new[] { builder.Id }, "drill",
            plan.Sites.Select(s => new BuildSite(s.X, s.Y, Direction.Right)).ToArray()));
        sim.RunUntil(s => s.State.Map.BuildingAt(4, 4) != 0, 600);

        Assert.That(sim.State.Map.BuildingAt(6, 4), Is.EqualTo(0), "the richest drills come first");
        Assert.That(builder.Worker!.Plans.Select(p => (p.CellX, p.CellY)), Is.EqualTo(new[] { (4, 4), (4, 6), (6, 4), (6, 6) }));
    }

    [Test]
    public void Plan_OnlyReadsTheState()
    {
        var sim = Ore(4, 4, 4, 4);
        var hasher = new StateHasher();
        ulong before = hasher.Compute(sim.State);
        Plan(sim, 2, 2, 9, 9);
        Assert.That(hasher.Compute(sim.State), Is.EqualTo(before));
    }

    [Test]
    public void MinimumOre_ComesFromTheRules()
    {
        var sim = Ore(4, 4, 4, 4);
        Assert.That(sim.State.Rules.DrillFillMinOrePercent, Is.EqualTo(50));
    }
}
