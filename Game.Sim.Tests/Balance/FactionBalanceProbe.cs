using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// factions.md, balance: armies of the same price against the Legion's late waves on open ground,
// with and without micro. Not a test of a rule — a probe to run by hand after changing numbers:
//   dotnet test Game.Sim.Tests --filter FactionBalanceProbe
// It prints a table; the asserts only keep the outline the design asks for.
public class FactionBalanceProbe
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Player = 1;
    private const int Legion = 9;

    private static readonly (string Id, int Count)[] Blue = { ("heavy", 2), ("infantry", 2), ("shooter", 4) };       // 240 + 40
    private static readonly (string Id, int Count)[] Red = { ("rl_lancer", 2), ("rl_runner", 5), ("rl_striker", 4) }; // 240 + 20
    // Half the price: where the fight is close and the faction's play decides it.
    private static readonly (string Id, int Count)[] BlueHalf = { ("heavy", 1), ("infantry", 1), ("shooter", 2) };       // 120 + 20
    private static readonly (string Id, int Count)[] RedHalf = { ("rl_lancer", 1), ("rl_runner", 3), ("rl_striker", 2) }; // 126 + 10
    private static readonly (string Id, int Count)[] Wave4 = { ("legion_raider", 2), ("legion_gunner", 4), ("legion_brute", 1) };
    private static readonly (string Id, int Count)[] Wave6 = { ("legion_raider", 3), ("legion_gunner", 5), ("legion_brute", 2) };

    [Test, Explicit("balance probe, run by hand")]
    public void Probe()
    {
        var rows = new List<string>();
        foreach ((string name, var wave) in new[] { ("волна 4", Wave4), ("волна 6", Wave6) })
        {
            rows.Add($"{name}: Синие Стены                 {Fight("blue_walls", Blue, wave, micro: false)}");
            rows.Add($"{name}: Красные Молнии без микро    {Fight("red_lightning", Red, wave, micro: false)}");
            rows.Add($"{name}: Красные Молнии с микро      {Fight("red_lightning", Red, wave, micro: true)}");
        }
        rows.Add($"полцены, волна 6: Синие Стены         {Fight("blue_walls", BlueHalf, Wave6, micro: false)}");
        rows.Add($"полцены, волна 6: Молнии без микро    {Fight("red_lightning", RedHalf, Wave6, micro: false)}");
        rows.Add($"полцены, волна 6: Молнии с микро      {Fight("red_lightning", RedHalf, Wave6, micro: true)}");
        rows.Add($"полцены, волна 4: Синие Стены         {Fight("blue_walls", BlueHalf, Wave4, micro: false)}");
        rows.Add($"полцены, волна 4: Молнии без микро    {Fight("red_lightning", RedHalf, Wave4, micro: false)}");
        rows.Add($"полцены, волна 4: Молнии с микро      {Fight("red_lightning", RedHalf, Wave4, micro: true)}");
        foreach (string r in rows)
            TestContext.Progress.WriteLine(r);
    }

    // Returns "won/lost, left: price of the survivors, time".
    internal static string Fight(string faction, (string Id, int Count)[] army, (string Id, int Count)[] wave, bool micro)
    {
        var map = new MapSetup(40, 24);
        var sim = new Simulation(new MatchSetup(7, RepoData.Get(), map, 2, new[] { Player, Legion, 10 },
            new[] { new RelationSetup(Player, Legion, Relation.Enemy) }, creepPlayerId: 10,
            factions: new[] { new PlayerFaction(Player, faction), new PlayerFaction(Legion, "black_legion") }));
        List<Unit> mine = Line(sim, Player, army, 8);
        List<Unit> theirs = Line(sim, Legion, wave, 30);
        foreach (Unit u in mine)
            u.Order = UnitOrder.AttackMoveTo(Center(32), u.Y);
        foreach (Unit u in theirs)
            u.Order = UnitOrder.AttackMoveTo(Center(6), u.Y);

        int t = 0;
        for (; t < 120 * Second; t++)
        {
            if (micro)
                Micro(sim, mine);
            sim.Tick();
            if (mine.All(u => u.IsDead) || theirs.All(u => u.IsDead))
                break;
        }
        bool won = theirs.All(u => u.IsDead);
        int left = (won ? mine : theirs).Where(u => !u.IsDead).Sum(u => (u.Stats.CostPrimary + u.Stats.CostSecondary) * u.Hp / u.MaxHp);
        return $"{(won ? "победа  " : "поражение")}, осталось на {left,3} ед., {t / Second,3} с";
    }

    // What a player who knows the faction does: Overdrive into the fight, Blink a wounded striker
    // back, pull back the wounded so they heal, and strike again (first strike).
    private static void Micro(Simulation sim, List<Unit> mine)
    {
        SimState s = sim.State;
        foreach (Unit u in mine)
        {
            if (u.IsDead)
                continue;
            Unit? enemy = s.Units.Where(e => !e.IsDead && e.OwnerId == Legion)
                .OrderBy(e => Movement.Distance(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (enemy == null)
                return;
            long d = Movement.Distance(u.X, u.Y, enemy.X, enemy.Y);
            bool hurt = u.Hp * 100 / u.MaxHp < 35;
            for (int i = 0; i < u.Stats.Abilities.Count; i++)
            {
                if (u.AbilityCooldown(i) > 0)
                    continue;
                AbilityStats a = s.Data.Ability(u.Stats.Abilities[i]);
                if (a.Kind == AbilityKind.Boost && d < Cells(5) && !hurt)
                    sim.Enqueue(new UseAbilityCommand(Player, s.Tick, new[] { u.Id }, a.Id, 0, 0));
                if (a.Kind == AbilityKind.Blink && hurt)
                    sim.Enqueue(new UseAbilityCommand(Player, s.Tick, new[] { u.Id }, a.Id, u.X - (int)((enemy.X - u.X) * Cells(6) / System.Math.Max(1, d)), u.Y));
            }
            if (hurt && u.Order.Kind != UnitOrderKind.Move)
                sim.Enqueue(new MoveCommand(Player, s.Tick, new[] { u.Id }, Center(2), u.Y));
            else if (!hurt && u.Order.Kind == UnitOrderKind.Idle)
                u.Order = UnitOrder.AttackMoveTo(Center(32), u.Y);
        }
    }

    private static List<Unit> Line(Simulation sim, int owner, (string Id, int Count)[] army, int x)
    {
        var units = new List<Unit>();
        int y = 4;
        foreach ((string id, int count) in army)
        {
            for (int i = 0; i < count; i++)
                units.Add(Spawn(sim, owner, id, x + (y / 20), 2 + (y++ % 20)));
        }
        return units;
    }
}
