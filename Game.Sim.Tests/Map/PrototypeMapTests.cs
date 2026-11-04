using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

// The M1 map in Assets/Data/maps/prototype.json (from Docs/Design/maps/prototype.txt).
// PLAN.md: ~64×64, 2 starts, 4–5 deposits of 12–40 ore cells, a creep camp guarding one.
public class PrototypeMapTests
{
    private static MapSetup Load(params int[] players) =>
        MapLoader.Parse(File.ReadAllText(Path.Combine(RepoData.RepoRoot(), "Assets", "Data", "maps", "prototype.json")), players);

    [Test]
    public void Map_MatchesThePlan()
    {
        var state = new SimState(Setups.Coop(Load(Setups.PlayerA, Setups.PlayerB)));
        Assert.That((state.Map.Width, state.Map.Height), Is.EqualTo((64, 64)));
        Assert.That(state.Map.Deposits.Count, Is.InRange(4, 5));
        Assert.That(state.Map.Deposits.All(d => d.Cells.Count >= 12 && d.Cells.Count <= 40), Is.True);
        Assert.That(state.Map.Deposits.Any(d => d.ResourceType == Resources.Secondary), Is.True);
        Assert.That(state.FindHeadquarters(Setups.PlayerA), Is.Not.Null);
        Assert.That(state.FindHeadquarters(Setups.PlayerB), Is.Not.Null);
        Assert.That(state.CreepCamps, Has.Count.EqualTo(1));
        Assert.That(state.CreepCamps[0].Stats.Id, Is.EqualTo("guard_camp"));
    }

    [Test]
    public void CampGuardsTheRichDeposit()
    {
        var state = new SimState(Setups.Coop(Load(Setups.PlayerA, Setups.PlayerB)));
        CreepCamp camp = state.CreepCamps[0];
        Deposit rich = state.Map.Deposits.OrderByDescending(d => d.Cells.Count).First();
        long r = camp.Stats.AggroRadiusMilli;
        bool partlyInRadius = rich.Cells.Any(c =>
        {
            long dx = Grid.CellCenter(c % 64) - camp.CenterMilliX, dy = Grid.CellCenter(c / 64) - camp.CenterMilliY;
            return dx * dx + dy * dy <= r * r;
        });
        Assert.That(partlyInRadius, Is.True, "creep-camp.md, assumption 4");
    }

    [Test]
    public void SoloLoad_LeavesTheSecondStartEmpty()
    {
        var state = new SimState(Setups.Coop(Load(Setups.PlayerA)));
        Assert.That(state.FindHeadquarters(Setups.PlayerA), Is.Not.Null);
        Assert.That(state.FindHeadquarters(Setups.PlayerB), Is.Null);
    }

    [Test]
    public void EveryDepositIsReachableFromBothStarts()
    {
        var state = new SimState(Setups.Coop(Load(Setups.PlayerA, Setups.PlayerB)));
        foreach (int player in new[] { Setups.PlayerA, Setups.PlayerB })
        {
            Unit worker = state.UnitsOf(player).First();
            foreach (Deposit d in state.Map.Deposits)
            {
                FlowField field = state.Paths.ToCell(state.Map, d.Cells[0] % 64, d.Cells[0] / 64);
                Assert.That(field.DistanceAt(worker.CellX, worker.CellY), Is.Not.EqualTo(FlowField.Unreachable));
            }
        }
    }

    [Test]
    public void FifteenMinutesOfEconomy_IsDeterministic()
    {
        Assert.That(Play(), Is.EqualTo(Play()));
    }

    // A's builder puts a drill on its deposit, then mines by hand; B's builds a factory, then mines.
    private static ulong[] Play()
    {
        var sim = new Simulation(Setups.Coop(Load(Setups.PlayerA, Setups.PlayerB)));
        var a = sim.State.UnitsOf(Setups.PlayerA);
        var b = sim.State.UnitsOf(Setups.PlayerB);
        sim.Order(new BuildCommand(Setups.PlayerA, 0, new[] { a[0].Id }, "drill", new[] { new BuildSite(16, 6, Direction.Right) }));
        sim.Order(new MineCommand(Setups.PlayerA, 0, new[] { a[0].Id }, 14, 4, queued: true));
        sim.Order(new BuildCommand(Setups.PlayerB, 0, new[] { b[0].Id }, "factory", new[] { new BuildSite(11, 52, Direction.Right) }));
        sim.Order(new MineCommand(Setups.PlayerB, 0, new[] { b[0].Id }, 14, 56, queued: true));
        var hashes = new System.Collections.Generic.List<ulong>();
        for (int t = 0; t < 15 * 60 * Simulation.TicksPerSecond; t++)
        {
            if (t == 1200)
            {
                Building? factory = sim.State.Buildings.FirstOrDefault(x => x.Stats.Id == "factory" && x.IsComplete);
                if (factory != null)
                    sim.Order(new ProduceCommand(Setups.PlayerB, sim.State.Tick, factory.Id, "infantry"));
            }
            sim.Tick();
            if (t % 20 == 0)
                hashes.Add(sim.StateHash);
        }
        Assert.That(sim.State.GetPlayer(Setups.PlayerA).Wallet.Get(Resources.Primary), Is.GreaterThan(100));
        return hashes.ToArray();
    }
}
