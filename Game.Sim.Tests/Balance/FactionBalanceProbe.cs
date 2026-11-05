using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// factions.md, balance: armies of the same price against the Legion's late waves on open ground,
// with and without micro, and turrets for the same money against the same waves. Not a test of a rule — a probe to run by hand after changing numbers:
//   dotnet test Game.Sim.Tests --filter FactionBalanceProbe
// It prints a table; the asserts only keep the outline the design asks for.
public class FactionBalanceProbe
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Player = 1;
    private const int Legion = 9;

    private static readonly (string Id, int Count)[] Blue = { ("heavy", 2), ("infantry", 2), ("shooter", 4) };       // 240 + 40
    private static readonly (string Id, int Count)[] Red = { ("rl_lancer", 2), ("rl_runner", 5), ("rl_striker", 4) }; // 240 + 20
    private static readonly (string Id, int Count)[] Brown = { ("bs_scrapper", 8), ("bs_shotgun", 10) };                // 236
    // The hero is free but stands in for a strong unit: the army gets the budget less ≈ 70 (a heavy's price).
    private static readonly (string Id, int Count)[] Yellow = { ("ys_squire", 3), ("ys_archer", 5), ("ys_hero", 1) };   // 194 + the hero
    private static readonly (string Id, int Count)[] BrownHalf = { ("bs_scrapper", 4), ("bs_shotgun", 5) };             // 118
    private static readonly (string Id, int Count)[] YellowHalf = { ("ys_squire", 1), ("ys_archer", 2), ("ys_hero", 1) }; // 72 + the hero

    // Half the price: where the fight is close and the faction's play decides it.
    private static readonly (string Id, int Count)[] BlueHalf = { ("heavy", 1), ("infantry", 1), ("shooter", 2) };       // 120 + 20
    private static readonly (string Id, int Count)[] RedHalf = { ("rl_lancer", 1), ("rl_runner", 3), ("rl_striker", 2) }; // 126 + 10
    private static readonly (string Id, int Count)[] Wave4 = { ("legion_raider", 2), ("legion_gunner", 4), ("legion_brute", 1) };
    private static readonly (string Id, int Count)[] Wave6 = { ("legion_raider", 3), ("legion_gunner", 5), ("legion_brute", 2) };

    private static readonly (string Name, string Id)[] Paths = { ("Воин", "ys_warrior"), ("Маг", "ys_mage"), ("Покровитель", "ys_support") };

    [Test, Explicit("balance probe, run by hand")]
    public void Probe()
    {
        var rows = new List<string>();
        foreach ((string name, var wave) in new[] { ("волна 4", Wave4), ("волна 6", Wave6) })
        {
            rows.Add($"{name}, ≈ 270 ед.: Синие Стены          {Fight("blue_walls", Blue, wave, micro: false)}");
            rows.Add($"{name}, ≈ 270 ед.: Молнии без микро     {Fight("red_lightning", Red, wave, micro: false)}");
            rows.Add($"{name}, ≈ 270 ед.: Молнии с микро       {Fight("red_lightning", Red, wave, micro: true)}");
            rows.Add($"{name}, ≈ 270 ед.: Мусорщики            {Fight("brown_scavengers", Brown, wave, micro: false)}");
            rows.Add($"{name}, ≈ 270 ед.: Жёлтая Звезда, ур. 1 {Fight("yellow_star", Yellow, wave, micro: false)}");
            foreach ((string pathName, string path) in Paths)
                rows.Add($"{name}, ≈ 270 ед.: Жёлтая, ур. 3, {pathName,-11} {Fight("yellow_star", Yellow, wave, micro: true, heroLevel: 3, heroPath: path)}");
        }
        foreach ((string name, var wave) in new[] { ("волна 4", Wave4), ("волна 6", Wave6) })
        {
            rows.Add($"{name}, ≈ 135 ед.: Синие Стены          {Fight("blue_walls", BlueHalf, wave, micro: false)}");
            rows.Add($"{name}, ≈ 135 ед.: Молнии без микро     {Fight("red_lightning", RedHalf, wave, micro: false)}");
            rows.Add($"{name}, ≈ 135 ед.: Молнии с микро       {Fight("red_lightning", RedHalf, wave, micro: true)}");
            rows.Add($"{name}, ≈ 135 ед.: Мусорщики            {Fight("brown_scavengers", BrownHalf, wave, micro: false)}");
            rows.Add($"{name}, ≈ 135 ед.: Жёлтая Звезда, ур. 1 {Fight("yellow_star", YellowHalf, wave, micro: false)}");
            foreach ((string pathName, string path) in Paths)
                rows.Add($"{name}, ≈ 135 ед.: Жёлтая, ур. 3, {pathName,-11} {Fight("yellow_star", YellowHalf, wave, micro: true, heroLevel: 3, heroPath: path)}");
        }
        // Defence: 100 resources of the faction's turrets in a row; the wave comes at them.
        foreach ((string name, var wave) in new[] { ("волна 4", Wave4), ("волна 6", Wave6) })
        {
            foreach ((string faction, string turret, int count) in new[] { ("blue_walls", "turret", 2), ("red_lightning", "rl_tesla", 3), ("brown_scavengers", "bs_scrap_gun", 5), ("yellow_star", "ys_tower", 2) })
                rows.Add($"{name}, оборона на ≈ 100 ед.: {faction,-17} {count} × {turret,-13} {Defend(faction, turret, count, wave)}");
        }
        foreach (string r in rows)
            TestContext.Progress.WriteLine(r);
    }

    // Returns "won/lost, left: price of the survivors, time".
    internal static string Fight(string faction, (string Id, int Count)[] army, (string Id, int Count)[] wave, bool micro, int heroLevel = 1, string heroPath = "")
    {
        var map = new MapSetup(40, 24);
        var sim = new Simulation(new MatchSetup(7, RepoData.Get(), map, 2, new[] { Player, Legion, 10 },
            new[] { new RelationSetup(Player, Legion, Relation.Enemy) }, creepPlayerId: 10,
            factions: new[] { new PlayerFaction(Player, faction), new PlayerFaction(Legion, "black_legion") }));
        List<Unit> mine = Line(sim, Player, army, 8);
        List<Unit> theirs = Line(sim, Legion, wave, 30);
        Unit? hero = mine.FirstOrDefault(u => u.IsHero);
        if (hero != null && heroPath.Length > 0)
            sim.State.GetPlayer(Player).HeroPathId = heroPath;
        if (hero != null && heroLevel > 1)
        {
            sim.State.GetPlayer(Player).HeroLevel = heroLevel;
            HeroSystem.ApplyLevel(sim.State, hero, heroLevel);
            hero.Hp = hero.MaxHp;
        }
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
        string heroText = hero == null ? "" : hero.IsDead ? ", герой погиб" : $", герой {hero.Hp}/{hero.MaxHp}";
        return $"{(won ? "победа  " : "поражение")}, осталось на {left,3} ед., {t / Second,3} с{heroText}";
    }

    // The wave walks at a row of turrets with full ammo; returns what is left of the turrets.
    internal static string Defend(string faction, string turret, int count, (string Id, int Count)[] wave)
    {
        var map = new MapSetup(40, 24);
        var sim = new Simulation(new MatchSetup(7, RepoData.Get(), map, 2, new[] { Player, Legion, 10 },
            new[] { new RelationSetup(Player, Legion, Relation.Enemy) }, creepPlayerId: 10,
            factions: new[] { new PlayerFaction(Player, faction), new PlayerFaction(Legion, "black_legion") }));
        var turrets = new List<Building>();
        for (int i = 0; i < count; i++)
        {
            Building t = sim.State.PlaceBuilding(Player, sim.State.Data.Building(turret), 10, 4 + i * 3, Direction.Right, complete: true);
            t.Stock?.Add(Resources.Primary, 150);
            turrets.Add(t);
        }
        List<Unit> theirs = Line(sim, Legion, wave, 30);
        foreach (Unit u in theirs)
            u.Order = UnitOrder.AttackMoveTo(Center(2), u.Y);
        int t0 = 0;
        for (; t0 < 120 * Second; t0++)
        {
            sim.Tick();
            if (turrets.All(b => b.IsDead) || theirs.All(u => u.IsDead))
                break;
        }
        bool held = theirs.All(u => u.IsDead);
        int hp = turrets.Where(b => !b.IsDead).Sum(b => b.Hp * 100 / b.MaxHp) / count;
        return $"{(held ? "отбились" : "сломаны")}, у турелей {hp,3} % прочности, {t0 / Second,3} с";
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
                // Hero spells: at the nearest enemy when it is in reach, buffs and heals once the fight is on.
                bool reach = d <= a.RangeMilli + a.RadiusMilli;
                if ((a.Kind == AbilityKind.DamageArea || a.Kind == AbilityKind.Strike) && (a.AtPoint ? reach : d < a.RadiusMilli))
                    sim.Enqueue(new UseAbilityCommand(Player, s.Tick, new[] { u.Id }, a.Id, enemy.X, enemy.Y));
                if ((a.Kind == AbilityKind.BoostArea || a.Kind == AbilityKind.HealArea) && d < Cells(6))
                    sim.Enqueue(new UseAbilityCommand(Player, s.Tick, new[] { u.Id }, a.Id, u.X, u.Y));
                if (a.Kind == AbilityKind.Blink && a.Damage > 0 && reach && !hurt)
                    sim.Enqueue(new UseAbilityCommand(Player, s.Tick, new[] { u.Id }, a.Id, enemy.X, enemy.Y));
                if (a.Kind == AbilityKind.Blink && a.Damage == 0 && hurt)
                    sim.Enqueue(new UseAbilityCommand(Player, s.Tick, new[] { u.Id }, a.Id, u.X - (int)((enemy.X - u.X) * Cells(6) / System.Math.Max(1, d)), u.Y));
            }
            // Pulling the wounded back pays off only for factions that heal by themselves.
            bool heals = (s.GetPlayer(Player).Faction?.UnitRegenPercentPerSecond ?? 0) > 0;
            if (hurt && heals && u.Order.Kind != UnitOrderKind.Move)
                sim.Enqueue(new MoveCommand(Player, s.Tick, new[] { u.Id }, Center(2), u.Y));
            else if ((!hurt || !heals) && u.Order.Kind == UnitOrderKind.Idle)
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
