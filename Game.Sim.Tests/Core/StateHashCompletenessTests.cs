using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// CLAUDE.md: every piece of state goes into StateHash, or a desync in it goes unnoticed.
// Each settable number, flag or enum of every object the state holds is changed in turn; the hash
// must change too. A new field that is not hashed fails here, unless it is listed as derived below.
// Not covered: the contents of lists (queues, plans, items) and wallet amounts — keep those in mind.
public class StateHashCompletenessTests
{
    // Not state: rebuilt every tick from hashed state, or only for the UI. Each entry says why.
    private static readonly string[] Derived =
    {
        "Unit.StepX", "Unit.StepY",                // this tick's own step, reset at the start of every tick
        "Building.WorkersThisTick",                // counted again every tick (BuildersLastTick is hashed)
        "LegionAi.NextWaveTick",                   // follows from NextWave, for the HUD timer
        "Unit.Order",                              // an immutable value: its fields are checked as UnitOrder
    };

    [Test]
    public void EverySettableField_IsInTheHash()
    {
        Simulation sim = RichMatch();
        var hasher = new StateHasher();
        ulong baseline = hasher.Compute(sim.State);
        var missing = new List<string>();
        var checkedNames = new SortedSet<string>(StringComparer.Ordinal);

        foreach (object owner in Objects(sim.State))
        {
            foreach (PropertyInfo p in owner.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string name = $"{owner.GetType().Name}.{p.Name}";
                if (p.SetMethod == null || p.GetIndexParameters().Length > 0 || Derived.Contains(name))
                    continue;
                object? before = p.GetValue(owner);
                object? changed = Changed(p.PropertyType, before);
                if (changed == null)
                    continue;
                checkedNames.Add(name);
                p.SetValue(owner, changed);
                if (hasher.Compute(sim.State) == baseline)
                    missing.Add(name);
                p.SetValue(owner, before);
            }
        }

        Assert.That(hasher.Compute(sim.State), Is.EqualTo(baseline), "the test must leave the state as it was");
        Assert.That(missing.Distinct(), Is.Empty, "not in StateHasher (or add to Derived with a reason)");
        // The test really reaches the components, not only the top-level objects.
        Assert.That(checkedNames, Is.SupersetOf(new[] { "Drill.Buffer", "Conveyor.ItemPos", "Factory.Progress", "Worker.Cargo", "Building.CaptureProgress", "Player.BuilderRespawnTick" }));
    }

    private static object? Changed(Type type, object? value)
    {
        if (type == typeof(int)) return (int)value! + 1;
        if (type == typeof(bool)) return !(bool)value!;
        if (type == typeof(ulong)) return (ulong)value! + 1;
        if (type == typeof(long)) return (long)value! + 1;
        if (type.IsEnum)
        {
            Array values = Enum.GetValues(type);
            int i = Array.IndexOf(values, value);
            return values.GetValue((i + 1) % values.Length);
        }
        return null;
    }

    // Everything mutable the state holds, one of each kind at least.
    private static IEnumerable<object> Objects(SimState state)
    {
        yield return state;
        foreach (Player p in state.Players)
            yield return p;
        foreach (Unit u in state.Units)
        {
            yield return u;
            if (u.Worker != null)
                yield return u.Worker;
        }
        foreach (Building b in state.Buildings)
        {
            yield return b;
            foreach (object? c in new object?[] { b.Drill, b.Conveyor, b.Bridge, b.Storage, b.Factory, b.Turret })
            {
                if (c != null)
                    yield return c;
            }
        }
        foreach (CreepCamp c in state.CreepCamps)
            yield return c;
        if (state.Legion != null)
        {
            yield return state.Legion;
            foreach (BaseSlot s in state.Legion.Slots)
                yield return s;
            foreach (Wave w in state.Legion.Waves)
                yield return w;
        }
    }

    // The M2 mission a few minutes in, plus one of each building and a worker with cargo.
    private static Simulation RichMatch()
    {
        MissionStats mission = RepoData.Catalog().Find("m2")!;
        var map = MapLoader.Parse(RepoData.MapJson("m2"), new[] { 1 })
            .Neutral("cave", 30, 40);
        var setup = new MatchSetup(1, RepoData.Get(), map, MissionLauncher.ResourceTypeCount,
            new[] { 1, MissionLauncher.LegionPlayerId, MissionLauncher.CreepPlayerId, MissionLauncher.NeutralPlayerId },
            new[] { new RelationSetup(1, MissionLauncher.LegionPlayerId, Relation.Enemy) },
            MissionLauncher.CreepPlayerId, MissionLauncher.LegionPlayerId, MissionLauncher.NeutralPlayerId, mission.Goal);
        var sim = new Simulation(setup);
        SimState s = sim.State;
        Building factory = s.PlaceBuilding(1, s.Data.Building("factory"), 10, 30, Direction.Right, complete: true);
        factory.Stock!.Add(Resources.Primary, 100);
        s.PlaceBuilding(1, s.Data.Building("storage"), 14, 30, Direction.Right, complete: true);
        s.PlaceBuilding(1, s.Data.Building("conveyor_bridge"), 17, 30, Direction.Right, complete: true);
        s.PlaceBuilding(1, s.Data.Building("conveyor"), 18, 30, Direction.Right, complete: true);
        s.PlaceBuilding(1, s.Data.Building("turret"), 20, 30, Direction.Right, complete: true);
        sim.Enqueue(new ProduceCommand(1, 0, factory.Id, "infantry"));
        sim.RunUntil(x => x.State.Legion!.Waves.Count > 0, 5 * 60 * Simulation.TicksPerSecond);
        Unit builder = s.FindBuilder(1)!;
        builder.Worker!.Cargo = 3;
        builder.Worker.CargoType = Resources.Primary;
        return sim;
    }
}
