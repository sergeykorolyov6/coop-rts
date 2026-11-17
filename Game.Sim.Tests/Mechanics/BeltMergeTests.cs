using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// Docs/Design/mechanics/belt-merge.md (U2): a belt cell takes from every own belt pointing into it —
// behind, left and right — in turn, round the circle after the one that entered last.
public class BeltMergeTests
{
    private const int A = Setups.PlayerA, B = Setups.PlayerB;
    private const int Second = Simulation.TicksPerSecond;

    // Headquarters of A over (2..5, 2..5). The main line M runs left along y = 4 from x = 11 to x = 6
    // into the headquarters; the merge cell is (9,4). Its left side is (9,3) (a line going up from
    // y = 0), its right side (9,5) (a line going down from y = 8); its back is (10,4), part of M.
    private const int MergeX = 9, MergeY = 4;

    private static MatchSetup Setup(System.Action<MapSetup>? terrain = null)
    {
        var map = new MapSetup(24, 16).Start(A, 2, 2).Start(B, 18, 10);
        terrain?.Invoke(map);
        return Setups.Coop(map);
    }

    private static Building Belt(Simulation sim, int x, int y, Direction dir, int owner = A, bool complete = true) =>
        sim.State.PlaceBuilding(owner, sim.State.Data.Building("conveyor"), x, y, dir, complete);

    private static Building[] MainLine(Simulation sim, int fromX = 11) =>
        Enumerable.Range(6, fromX - 5).Reverse().Select(x => Belt(sim, x, MergeY, Direction.Left)).ToArray();

    private static Building[] LeftLine(Simulation sim, int owner = A) =>
        Enumerable.Range(0, 4).Select(y => Belt(sim, MergeX, y, Direction.Up, owner)).ToArray();

    private static Building[] RightLine(Simulation sim) =>
        Enumerable.Range(5, 4).Reverse().Select(y => Belt(sim, MergeX, y, Direction.Down)).ToArray();

    private static Building At(Simulation sim, int x, int y) => sim.State.FindBuilding(sim.State.Map.BuildingAt(x, y))!;

    private static void Fill(params Building[] cells)
    {
        foreach (Building c in cells)
        {
            c.Conveyor!.ItemType = Resources.Primary;
            c.Conveyor.ItemPos = 0;
        }
    }

    // A source that never runs dry: the line's first cell is refilled whenever it is empty.
    private sealed class Source
    {
        private readonly Building _tail;
        public int Given;

        public Source(Building tail) => _tail = tail;

        public void Feed()
        {
            if (_tail.IsDead || _tail.Conveyor!.HasItem)
                return;
            _tail.Conveyor.ItemType = Resources.Primary;
            _tail.Conveyor.ItemPos = 0;
            Given++;
        }
    }

    private static void Run(Simulation sim, int ticks, params Source[] sources)
    {
        for (int i = 0; i < ticks; i++)
        {
            foreach (Source s in sources)
                s.Feed();
            sim.Tick();
        }
    }

    private static int Wallet(Simulation sim) => sim.State.GetPlayer(A).Wallet.Get(Resources.Primary);

    // Line 2 of the spec: the main line and the left line, both full, both fed for good.
    private static (Simulation Sim, Source Main, Source Side) TwoFullInputs(MatchSetup? setup = null)
    {
        var sim = new Simulation(setup ?? Setup());
        Building[] main = MainLine(sim);
        Building[] side = LeftLine(sim);
        Fill(main);
        Fill(side);
        return (sim, new Source(main[0]), new Source(side[0]));
    }

    // ---------- Given / When / Then ----------

    [Test]
    public void GWT1_SideLineFromADrill_IntoAnEmptyMainLine_Flows()
    {
        var sim = new Simulation(Setup(map => map.OreRect(10, 0, 2, 2, Resources.Primary)));
        MainLine(sim);
        Building[] side = LeftLine(sim);
        sim.State.PlaceBuilding(A, sim.State.Data.Building("drill"), 10, 0, Direction.Right, complete: true);
        int before = Wallet(sim);

        sim.Run(20 * Second);

        Assert.That(Wallet(sim) - before, Is.GreaterThan(0));
        Assert.That(side.Count(c => c.Conveyor!.HasItem && c.Conveyor.ItemPos == Conveyor.CellLength), Is.EqualTo(0), "nothing stands on the side line");
    }

    [Test]
    public void GWT2_TwoFullInputs_FourPerSecondIntoTheHeadquarters_SplitEvenly()
    {
        (Simulation sim, Source main, Source side) = TwoFullInputs();
        int before = Wallet(sim);

        Run(sim, 60 * Second, main, side);

        Assert.That(Wallet(sim) - before, Is.InRange(236, 244));
        Assert.That(System.Math.Abs(main.Given - side.Given), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void GWT3_ThreeFullInputs_AThirdEach()
    {
        var sim = new Simulation(Setup());
        Building[] main = MainLine(sim);
        Building[] left = LeftLine(sim);
        Building[] right = RightLine(sim);
        Fill(main);
        Fill(left);
        Fill(right);
        var sources = new[] { new Source(main[0]), new Source(left[0]), new Source(right[0]) };

        Run(sim, 60 * Second, sources);

        int max = sources.Max(s => s.Given), min = sources.Min(s => s.Given);
        Assert.That(max - min, Is.LessThanOrEqualTo(1));
        Assert.That(min, Is.GreaterThan(60));
    }

    [Test]
    public void GWT4_OnlyTheSideInputHasItems_ItGivesFourPerSecond()
    {
        var sim = new Simulation(Setup());
        MainLine(sim);
        Building[] side = LeftLine(sim);
        Fill(side);
        var source = new Source(side[0]);
        int before = Wallet(sim);

        Run(sim, 60 * Second, source);

        Assert.That(Wallet(sim) - before, Is.InRange(236, 244));
    }

    [Test]
    public void GWT5_HeadOn_TheItemStandsAtTheEndOfA()
    {
        var sim = new Simulation(Setup());
        Building[] a = Enumerable.Range(5, 3).Select(x => Belt(sim, x, 10, Direction.Right)).ToArray();
        Building b = Belt(sim, 8, 10, Direction.Left);
        Fill(a[0]);

        sim.Run(3 * Second);

        Assert.That(a[2].Conveyor!.HasItem, Is.True);
        Assert.That(a[2].Conveyor.ItemPos, Is.EqualTo(Conveyor.CellLength));
        Assert.That(b.Conveyor!.HasItem, Is.False);
    }

    [Test]
    public void GWT6_AllyBeltIntoMySide_Stands()
    {
        var sim = new Simulation(Setup());
        Building[] main = MainLine(sim);
        Building[] ally = LeftLine(sim, B);
        Fill(ally[0]);

        sim.Run(3 * Second);

        Assert.That(ally[3].Conveyor!.HasItem, Is.True);
        Assert.That(ally[3].Conveyor.ItemPos, Is.EqualTo(Conveyor.CellLength));
        Assert.That(main.All(c => !c.Conveyor!.HasItem), Is.True);
    }

    [Test]
    public void GWT7_SideLineBelowTheCliff_Stands()
    {
        // y ≥ 4 is the plateau: the main line is up, the side line below, no ramp.
        var sim = new Simulation(Setup(map =>
        {
            for (int y = 4; y < 16; y++)
                for (int x = 0; x < 24; x++)
                    map.Level(x, y, 1);
        }));
        Building[] main = MainLine(sim);
        Building[] side = LeftLine(sim);
        Fill(side[0]);

        sim.Run(3 * Second);

        Assert.That(side[3].Conveyor!.HasItem, Is.True);
        Assert.That(side[3].Conveyor.ItemPos, Is.EqualTo(Conveyor.CellLength));
        Assert.That(At(sim, MergeX, MergeY).Conveyor!.HasItem, Is.False);
    }

    [Test]
    public void GWT8_MergeCellDestroyed_BothInputsJamBeforeTheHole_TheRestDelivers()
    {
        (Simulation sim, Source main, Source side) = TwoFullInputs();
        Run(sim, 10 * Second, main, side);
        Building merge = At(sim, MergeX, MergeY);
        Building back = At(sim, MergeX + 1, MergeY), left = At(sim, MergeX, MergeY - 1);
        Building[] after = Enumerable.Range(6, 3).Select(x => At(sim, x, MergeY)).ToArray();

        Kill(merge);
        Run(sim, 5 * Second, main, side);

        Assert.That(sim.State.FindBuilding(merge.Id), Is.Null);
        Assert.That(back.Conveyor!.ItemPos, Is.EqualTo(Conveyor.CellLength));
        Assert.That(left.Conveyor!.ItemPos, Is.EqualTo(Conveyor.CellLength));
        Assert.That(after.All(c => !c.Conveyor!.HasItem), Is.True);
    }

    [Test]
    public void GWT9_SameSeed_SameHashEveryTick()
    {
        List<ulong> Play()
        {
            (Simulation sim, Source main, Source side) = TwoFullInputs();
            var hashes = new List<ulong>();
            for (int i = 0; i < 60 * Second; i++)
            {
                main.Feed();
                side.Feed();
                sim.Tick();
                hashes.Add(sim.StateHash);
            }
            return hashes;
        }

        Assert.That(Play(), Is.EqualTo(Play()));
    }

    [Test]
    public void GWT10_SnapshotAtThirtySeconds_RestoredGoesStepForStep()
    {
        MatchSetup setup = Setup();
        (Simulation sim, Source main, Source side) = TwoFullInputs(setup);
        Run(sim, 30 * Second, main, side);
        Building merge = At(sim, MergeX, MergeY);
        Assert.That(merge.Conveyor!.LastFeeder, Is.Not.EqualTo(0));

        Simulation restored = Simulation.FromSnapshot(setup, sim.WriteSnapshot(), sim.CommandLog);
        Assert.That(At(restored, MergeX, MergeY).Conveyor!.LastFeeder, Is.EqualTo(merge.Conveyor.LastFeeder));
        var mainR = new Source(At(restored, 11, MergeY));
        var sideR = new Source(At(restored, MergeX, 0));
        var source = new Source(At(sim, 11, MergeY));
        var sideS = new Source(At(sim, MergeX, 0));
        for (int i = 0; i < 30 * Second; i++)
        {
            source.Feed();
            sideS.Feed();
            mainR.Feed();
            sideR.Feed();
            sim.Tick();
            restored.Tick();
            Assert.That(restored.StateHash, Is.EqualTo(sim.StateHash), $"tick {sim.State.Tick}");
        }
    }

    // ---------- Edge cases ----------

    private static (Simulation Sim, Building Merge, Building Back, Building Left, Building Right) ThreeReady()
    {
        var sim = new Simulation(Setup());
        MainLine(sim);
        LeftLine(sim);
        RightLine(sim);
        Building merge = At(sim, MergeX, MergeY), back = At(sim, MergeX + 1, MergeY);
        Building left = At(sim, MergeX, MergeY - 1), right = At(sim, MergeX, MergeY + 1);
        return (sim, merge, back, left, right);
    }

    private static void Ready(params Building[] cells)
    {
        foreach (Building c in cells)
        {
            c.Conveyor!.ItemType = Resources.Primary;
            c.Conveyor.ItemPos = Conveyor.CellLength;
        }
    }

    [Test]
    public void Edge_NobodyYet_TwoReady_BackFirst_ThenLeft()
    {
        (Simulation sim, Building merge, Building back, Building left, _) = ThreeReady();
        Ready(back, left);

        sim.Tick();
        Assert.That(merge.Conveyor!.LastFeeder, Is.EqualTo(Conveyor.FromBack));
        Assert.That(back.Conveyor!.HasItem, Is.False);
        Assert.That(left.Conveyor!.HasItem, Is.True);

        sim.RunUntil(s => !left.Conveyor.HasItem, 40);
        Assert.That(merge.Conveyor.LastFeeder, Is.EqualTo(Conveyor.FromLeft));
    }

    [Test]
    public void Edge_LeftAndRightReady_LeftWasLast_RightGoes()
    {
        (Simulation sim, Building merge, _, Building left, Building right) = ThreeReady();
        Ready(left, right);
        merge.Conveyor!.LastFeeder = Conveyor.FromLeft;

        sim.Tick();

        Assert.That(merge.Conveyor.LastFeeder, Is.EqualTo(Conveyor.FromRight));
        Assert.That(right.Conveyor!.HasItem, Is.False);
        Assert.That(left.Conveyor!.HasItem, Is.True);
    }

    [Test]
    public void Edge_FullRingWithASideLine_SideWaits_NoHang()
    {
        var sim = new Simulation(Setup());
        Building[] ring =
        {
            Belt(sim, 14, 10, Direction.Right), Belt(sim, 15, 10, Direction.Up),
            Belt(sim, 15, 11, Direction.Left), Belt(sim, 14, 11, Direction.Down),
        };
        Building side = Belt(sim, 13, 10, Direction.Right);
        Fill(ring);
        Fill(side);

        sim.Run(10 * Second);

        Assert.That(ring.All(c => c.Conveyor!.HasItem), Is.True);
        Assert.That(side.Conveyor!.HasItem, Is.True);
    }

    [Test]
    public void Edge_BridgeOutputIntoAFullMergedLine_WaitsButLosesNothing()
    {
        var sim = new Simulation(Setup());
        Building[] main = MainLine(sim);
        Fill(main);
        var source = new Source(main[0]);
        // A bridge from (7,8) down to (7,5): its output gives forward, into the side of (7,4).
        Building input = sim.State.PlaceBuilding(A, sim.State.Data.Building("conveyor_bridge"), 7, 8, Direction.Down, complete: true);
        sim.State.PlaceBuilding(A, sim.State.Data.Building("conveyor_bridge"), 7, 5, Direction.Down, complete: true);
        Assert.That(input.Bridge!.LinkedToId, Is.Not.EqualTo(0));
        Building feeder = Belt(sim, 7, 9, Direction.Down);
        var bridgeSource = new Source(feeder);
        int before = Wallet(sim);

        Run(sim, 20 * Second, source, bridgeSource);
        int carried = Wallet(sim) - before;
        int onBelts = main.Count(c => c.Conveyor!.HasItem) + (feeder.Conveyor!.HasItem ? 1 : 0) + input.Bridge._items.Count;
        int startedOnMain = 6;

        Assert.That(carried + onBelts, Is.EqualTo(startedOnMain + source.Given + bridgeSource.Given));
    }

    [Test]
    public void Edge_DrillBesideTheMergeCell_PutsFirst_BeforeTheBelts()
    {
        // A short line (13,10) → (12,10) going left; the drill over (11..12, 11..12) touches only (12,10).
        var sim = new Simulation(Setup(map => map.OreRect(11, 11, 2, 2, Resources.Primary)));
        Building cell = Belt(sim, 12, 10, Direction.Left);
        Building back = Belt(sim, 13, 10, Direction.Left);
        Building drill = sim.State.PlaceBuilding(A, sim.State.Data.Building("drill"), 11, 11, Direction.Right, complete: true);
        drill.Drill!.Buffer = 1;
        Ready(back);

        sim.Tick();

        Assert.That(drill.Drill.Buffer, Is.EqualTo(0));
        Assert.That(cell.Conveyor!.HasItem, Is.True);
        Assert.That(cell.Conveyor.LastFeeder, Is.EqualTo(Conveyor.FromNobody));
        Assert.That(back.Conveyor!.ItemPos, Is.EqualTo(Conveyor.CellLength), "the belt waits: the cell took the drill's unit");
    }

    [Test]
    public void Edge_UnfinishedSideBelt_IsNoInput()
    {
        (Simulation sim, Building merge, Building back, _, _) = ThreeReady();
        Building left = At(sim, MergeX, MergeY - 1);
        left.IsDead = true;
        sim.Tick();
        Building unfinished = Belt(sim, MergeX, MergeY - 1, Direction.Up, complete: false);
        Ready(back);

        sim.Tick();

        Assert.That(unfinished.IsComplete, Is.False);
        Assert.That(merge.Conveyor!.LastFeeder, Is.EqualTo(Conveyor.FromBack));
        Assert.That(ConveyorInputs(sim, merge), Is.EqualTo(2));
    }

    private static int ConveyorInputs(Simulation sim, Building belt)
    {
        (bool back, bool left, bool right) = PlacementPreview.BeltInputs(sim.State, belt);
        return (back ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);
    }

    [Test]
    public void Edge_MergeCellRelaid_WhoEnteredLastIsReset()
    {
        (Simulation sim, Building merge, _, _, _) = ThreeReady();
        merge.Conveyor!.LastFeeder = Conveyor.FromLeft;
        Unit builder = sim.State.FindBuilder(A)!;
        builder.X = Center(MergeX - 1);
        builder.Y = Center(MergeY + 2);

        sim.Order(new BuildCommand(A, sim.State.Tick, new[] { builder.Id }, "conveyor", new[] { new BuildSite(MergeX, MergeY, Direction.Down) }));
        sim.Tick();

        Assert.That(merge.Direction, Is.EqualTo(Direction.Down));
        Assert.That(merge.Conveyor.LastFeeder, Is.EqualTo(Conveyor.FromNobody));
    }

    [Test]
    public void Inputs_CountedForTheUi()
    {
        (Simulation sim, Building merge, _, _, _) = ThreeReady();
        Assert.That(PlacementPreview.BeltInputs(sim.State, merge), Is.EqualTo((true, true, true)));
        Assert.That(PlacementPreview.BeltInputs(sim.State, At(sim, 6, MergeY)), Is.EqualTo((true, false, false)));
        // A corner: the last cell of the left line is fed from behind only; a cell fed from one side only.
        Assert.That(PlacementPreview.BeltInputs(sim.State, At(sim, MergeX, MergeY - 1)), Is.EqualTo((true, false, false)));
        Kill(At(sim, MergeX + 1, MergeY));
        Kill(At(sim, MergeX, MergeY + 1));
        sim.Tick();
        Assert.That(PlacementPreview.BeltInputs(sim.State, merge), Is.EqualTo((false, true, false)));
    }
}
