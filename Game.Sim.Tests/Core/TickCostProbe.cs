using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// plan-terrain-and-factions.md, stage T0: what a tick costs on a 256×256 map in a late game —
// 4 bases with belts, drills and turrets, ≈ 400 units marching across the map and fighting.
// Not a test of a rule — a probe to run by hand before and after work on scale:
//   dotnet test Game.Sim.Tests -c Release --filter TickCostProbe
// The scenario is fixed (own LCG, no clock in the sim), so two runs differ only by the machine.
public class TickCostProbe
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Size = 256;
    private const int Ticks = 5 * 60 * Second;
    private const int Enemy = 9;
    private const int Creeps = 10;

    private static readonly (int X, int Y)[] Bases = { (16, 16), (16, 136), (136, 16), (136, 136) };
    private static readonly (string Id, int Count)[] Army = { ("infantry", 25), ("shooter", 25), ("heavy", 10) };
    private static readonly (string Id, int Count)[] Raid = { ("legion_raider", 15), ("legion_gunner", 17), ("legion_brute", 8) };

    [Test, Explicit("performance probe, run by hand")]
    public void Probe()
    {
        // Run 1: every tick timed, hash included — what the game and the network pay.
        Simulation sim = Build(out int rocks, out List<ICommand> orders);
        foreach (ICommand c in orders)
            sim.Enqueue(c);
        int units = sim.State.Units.Count, buildings = sim.State.Buildings.Count;
        var ticks = new long[Ticks];
        var watch = new Stopwatch();
        for (int t = 0; t < Ticks; t++)
        {
            watch.Restart();
            sim.Tick();
            ticks[t] = watch.ElapsedTicks;
        }
        ulong hash = sim.StateHash;
        int unitsLeft = sim.State.Units.Count, buildingsLeft = sim.State.Buildings.Count;

        // Run 2: the same match without hashing the ticks in between — what loading a save pays.
        Simulation replay = Build(out _, out orders);
        foreach (ICommand c in orders)
            replay.Enqueue(c);
        watch.Restart();
        replay.FastForward(Ticks);
        double replayMs = watch.Elapsed.TotalMilliseconds;
        Assert.That(replay.StateHash, Is.EqualTo(hash), "the probe's scenario must be deterministic");

        double toMs = 1000.0 / Stopwatch.Frequency;
        long[] sorted = ticks.OrderBy(v => v).ToArray();
        var rows = new List<string>
        {
            $"Карта {Size}×{Size}, скал {rocks} ({rocks * 100 / (Size * Size)} %), юнитов {units} → {unitsLeft}, зданий {buildings} → {buildingsLeft}, {Ticks / Second} с игры",
            $"Тик с хешем:   среднее {ticks.Average() * toMs:F3} мс, медиана {sorted[Ticks / 2] * toMs:F3}, 99 % {sorted[Ticks * 99 / 100] * toMs:F3}, худший {sorted[Ticks - 1] * toMs:F3} мс (тик {Array.IndexOf(ticks, sorted[Ticks - 1])}), худший после первой секунды {ticks.Skip(Second).Max() * toMs:F3} мс",
            $"Тик без хеша:  среднее {replayMs / Ticks:F3} мс (загрузка сохранения: 45 мин игры ≈ {replayMs / Ticks * 45 * 60 * Second / 1000:F0} с)",
        };
        for (int minute = 0; minute < Ticks / (60 * Second); minute++)
        {
            var slice = new ArraySegment<long>(ticks, minute * 60 * Second, 60 * Second);
            rows.Add($"  минута {minute + 1}: среднее {slice.Average() * toMs:F3} мс, худший {slice.Max() * toMs:F3} мс");
        }
        foreach (string r in rows)
            TestContext.Progress.WriteLine(r);
    }

    // Where the tick goes: the same match stepped system by system, in Simulation.Step's order.
    // The final hash is checked against a real run, so a change in Step that is not mirrored here fails.
    [Test, Explicit("performance probe, run by hand")]
    public void Breakdown()
    {
        Simulation real = Build(out _, out List<ICommand> orders);
        foreach (ICommand c in orders)
            real.Enqueue(c);
        real.FastForward(Ticks);

        Simulation sim = Build(out _, out orders);
        SimState s = sim.State;
        var events = new List<SimEvent>();
        var names = new List<string>();
        var total = new List<long>();
        var worst = new List<long>();
        var watch = new Stopwatch();
        int step = 0;
        void Time(string name, Action action)
        {
            if (step == names.Count)
            {
                names.Add(name);
                total.Add(0);
                worst.Add(0);
            }
            watch.Restart();
            action();
            long e = watch.ElapsedTicks;
            total[step] += e;
            if (e > worst[step])
                worst[step] = e;
            step++;
        }

        int next = 0;
        orders = orders.OrderBy(c => c.Tick).ThenBy(c => c.PlayerId).ToList();
        for (int t = 0; t < Ticks; t++)
        {
            step = 0;
            events.Clear();
            Time("команды", () =>
            {
                for (; next < orders.Count && orders[next].Tick == t; next++)
                    orders[next].Execute(s, events);
            });
            Time("LegionAi", () => LegionAiSystem.Tick(s, events));
            Time("CreepCamp.Begin", () => CreepCampSystem.BeginTick(s, events));
            Time("UnitSystem", () => UnitSystem.Tick(s, events));
            Time("CreepCamp.End", () => CreepCampSystem.EndTick(s));
            Time("TurretSystem", () => TurretSystem.Tick(s));
            Time("Combat.ApplyHits", () => Combat.ApplyHits(s, events));
            Time("Regenerate", () => FactionTraits.Regenerate(s));
            Time("Demolish", () => DemolishSystem.Tick(s, events));
            Time("Builder", () => BuilderSystem.Tick(s, events));
            Time("Factory", () => FactorySystem.Tick(s, events));
            Time("Drill", () => DrillSystem.Tick(s));
            Time("Conveyor", () => ConveyorSystem.Tick(s));
            Time("Separate", () => Movement.Separate(s));
            Time("PushOut", () => Movement.PushOutOfBlockedCells(s));
            Time("Capture", () => CaptureSystem.Tick(s, events));
            Time("RemoveDead", () => s.RemoveDead());
            Time("MissionRules", () => MissionRules.Check(s, events));
            Time("Fog", () => s.Fog.Update(s));
            s.Tick = t + 1;
        }
        Assert.That(new StateHasher().Compute(s), Is.EqualTo(real.StateHash), "Breakdown no longer mirrors Simulation.Step");

        double toMs = 1000.0 / Stopwatch.Frequency;
        long sum = total.Sum();
        TestContext.Progress.WriteLine($"Всего {sum * toMs / Ticks:F3} мс на тик; полей пути построено {s.Paths.Built}");
        foreach (int i in Enumerable.Range(0, names.Count).OrderByDescending(i => total[i]))
            TestContext.Progress.WriteLine($"  {names[i],-18} {total[i] * toMs / Ticks,8:F3} мс  {total[i] * 100.0 / sum,5:F1} %  худший {worst[i] * toMs,8:F3} мс");
    }

    private static Simulation Build(out int rocks, out List<ICommand> orders)
    {
        orders = new List<ICommand>();
        var map = new MapSetup(Size, Size);
        rocks = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!IsClear(x, y) && Mix((uint)(y * Size + x)) % 100 < 12)
                {
                    map.Block(x, y);
                    rocks++;
                }
            }
        }

        var players = new List<int>();
        var relations = new List<RelationSetup>();
        var factions = new List<PlayerFaction> { new PlayerFaction(Enemy, "black_legion") };
        for (int p = 1; p <= Bases.Length; p++)
        {
            (int bx, int by) = Bases[p - 1];
            map.Start(p, bx, by);
            for (int row = 0; row < 6; row++)
                map.OreRect(bx + 60, by + 6 + 4 * row, 2, 2, Resources.Primary);
            for (int q = 1; q < p; q++)
                relations.Add(new RelationSetup(q, p, Relation.Ally));
            relations.Add(new RelationSetup(p, Enemy, Relation.Enemy));
            players.Add(p);
        }
        players.Add(Enemy);
        players.Add(Creeps);

        var sim = new Simulation(new MatchSetup(7, RepoData.Get(), map, 2, players, relations, creepPlayerId: Creeps, factions: factions));
        SimState s = sim.State;
        uint rng = 12345;
        for (int p = 1; p <= Bases.Length; p++)
        {
            (int bx, int by) = Bases[p - 1];
            // Six drills, each with a 60-cell belt towards the base, and a row of turrets behind them.
            for (int row = 0; row < 6; row++)
            {
                int y = by + 6 + 4 * row;
                s.PlaceBuilding(p, s.Data.Building("drill"), bx + 60, y, Direction.Right, complete: true);
                for (int x = bx + 59; x >= bx; x--)
                    s.PlaceBuilding(p, s.Data.Building("conveyor"), x, y, Direction.Left, complete: true);
            }
            for (int k = 0; k < 8; k++)
                s.PlaceBuilding(p, s.Data.Building("turret"), bx + 66, by + 4 * k, Direction.Right, complete: true);

            // Half the army marches to the enemy's side of the map, half stays and is moved around
            // the base every 10 s, as a player shuffling a defence would.
            List<Unit> army = Block(sim, p, Army, bx + 6, by - 6);
            for (int i = 0; i < army.Count; i += 2)
                army[i].Order = UnitOrder.AttackMoveTo(Center(236), Center(100 + 10 * p));
            int[] home = army.Where((u, i) => i % 2 == 1).Select(u => u.Id).ToArray();
            for (int t = 10 * Second; t < Ticks; t += 10 * Second)
            {
                rng = rng * 1664525 + 1013904223;
                int x = bx + 8 + (int)(rng >> 8) % 56, y = by - 6 + (int)(rng >> 20) % 4;
                orders.Add(new MoveCommand(p, t, home, Center(x), Center(y)));
            }

            // A raid on every base from the far edge: the longest paths the map has.
            foreach (Unit u in Block(sim, Enemy, Raid, 236, 96 + 10 * p - 4))
                u.Order = UnitOrder.AttackMoveTo(Center(bx + 30), Center(by + 16));
        }
        return sim;
    }

    // Units in rows of 20, one per cell.
    private static List<Unit> Block(Simulation sim, int owner, (string Id, int Count)[] army, int x, int y)
    {
        var units = new List<Unit>();
        foreach ((string id, int count) in army)
        {
            for (int i = 0; i < count; i++)
                units.Add(Spawn(sim, owner, id, x + units.Count % 20, y + units.Count / 20));
        }
        return units;
    }

    // No rocks on the bases and where the raids appear.
    private static bool IsClear(int x, int y)
    {
        foreach ((int bx, int by) in Bases)
        {
            if (x >= bx - 8 && x < bx + 72 && y >= by - 8 && y < by + 36)
                return true;
        }
        return x >= 230 && x < 262 && y >= 96 && y < 150;
    }

    private static uint Mix(uint v)
    {
        v ^= v >> 16;
        v *= 0x7feb352d;
        v ^= v >> 15;
        v *= 0x846ca68b;
        return v ^ (v >> 16);
    }
}
