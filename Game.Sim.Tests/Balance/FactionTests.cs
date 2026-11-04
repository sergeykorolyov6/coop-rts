using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

using Node = System.Text.Json.Nodes.JsonNode;

// factions.csv and buildings.csv "behavior": a new faction is data — its builder, headquarters,
// buildings — and a building's behaviour comes from its row, not from its id.
public class FactionTests
{
    private static string Json(string name) => File.ReadAllText(Path.Combine(RepoData.RepoRoot(), "Assets", "Data", name));

    // The shipped data plus a second playable faction whose builder, headquarters and drill are
    // copies of Blue Walls' rows under new ids.
    private static GameData WithRedLightning(Action<JsonArray>? editFactions = null)
    {
        JsonArray units = Copy(Json("units.json"), "builder", "red_builder");
        JsonArray buildings = Copy(Json("buildings.json"), "headquarters", "red_hq");
        buildings = Copy(buildings.ToJsonString(), "drill", "red_drill");
        var factions = Node.Parse(Json("factions.json"))!.AsArray();
        factions.Add(new JsonObject
        {
            ["id"] = "test_faction", ["name_ru"] = "Тестовая фракция", ["role"] = "player",
            ["builder"] = "red_builder", ["headquarters"] = "red_hq",
            ["buildings"] = new JsonArray("red_drill", "conveyor"), ["single_headquarters"] = 1,
            ["builder_respawn_ticks"] = 200, ["unit_regen_pct_per_s"] = 0, ["building_regen_pct_per_s"] = 0,
            ["regen_delay_ticks"] = 0, ["first_strike_pct"] = 100, ["first_strike_delay_ticks"] = 0,
            ["on_kill_ability"] = "", ["bounty_pct"] = 0,
        });
        editFactions?.Invoke(factions);
        return GameData.Parse(units.ToJsonString(), buildings.ToJsonString(), Json("creep_camps.json"), Json("rules.json"),
            Json("legion.json"), Json("waves.json"), factions.ToJsonString(), Json("abilities.json"));
    }

    private static JsonArray Copy(string json, string id, string newId)
    {
        JsonArray rows = Node.Parse(json)!.AsArray();
        Node copy = rows.First(r => (string)r!["id"]! == id)!.DeepClone();
        copy["id"] = newId;
        rows.Add(copy);
        return rows;
    }

    private static MatchSetup Setup(GameData data, params PlayerFaction[] factions)
    {
        var map = new MapSetup(40, 16).Start(Setups.PlayerA, 2, 2).Start(Setups.PlayerB, 20, 2).OreRect(28, 4, 2, 2, Resources.Primary);
        return new MatchSetup(42, data, map, 2, new[] { Setups.PlayerA, Setups.PlayerB, Setups.EnemyAi, Setups.Creeps },
            new[] { new RelationSetup(Setups.PlayerA, Setups.PlayerB, Relation.Ally) },
            creepPlayerId: Setups.Creeps, factions: factions);
    }

    [Test]
    public void EveryPlayer_GetsTheFirstFactionOfItsRole()
    {
        var sim = new Simulation(Setups.WithLegion(new MapSetup(48, 36).Start(Setups.PlayerA, 2, 2)
            .LegionSlot("legion_hq", 38, 24).Rally(28, 22)));
        SimState s = sim.State;
        Assert.That(s.GetPlayer(Setups.PlayerA).Faction!.Id, Is.EqualTo("blue_walls"));
        Assert.That(s.GetPlayer(Setups.EnemyAi).Faction!.Id, Is.EqualTo("black_legion"));
        Assert.That(s.GetPlayer(Setups.Creeps).Faction!.Id, Is.EqualTo("grey_creatures"));
    }

    [Test]
    public void SecondFaction_StartsWithItsOwnHeadquartersAndBuilder_AndGetsThatBuilderBack()
    {
        var sim = new Simulation(Setup(WithRedLightning(), new PlayerFaction(Setups.PlayerB, "test_faction")));
        SimState s = sim.State;
        Assert.That(s.FindHeadquarters(Setups.PlayerB)!.Stats.Id, Is.EqualTo("red_hq"));
        Assert.That(s.FindBuilder(Setups.PlayerB)!.Stats.Id, Is.EqualTo("red_builder"));
        Assert.That(s.FindBuilder(Setups.PlayerA)!.Stats.Id, Is.EqualTo("builder"));

        Kill(s.FindBuilder(Setups.PlayerB)!);
        sim.RunUntil(x => x.State.FindBuilder(Setups.PlayerB) != null);
        Assert.That(s.FindBuilder(Setups.PlayerB)!.Stats.Id, Is.EqualTo("red_builder"));
    }

    [Test]
    public void SecondFaction_BuildsOnlyItsList_AndItsDrillMinesByItsBehaviour()
    {
        var sim = new Simulation(Setup(WithRedLightning(), new PlayerFaction(Setups.PlayerB, "test_faction")));
        SimState s = sim.State;
        Unit red = s.FindBuilder(Setups.PlayerB)!;
        sim.Order(new BuildCommand(Setups.PlayerB, s.Tick, new[] { red.Id }, "factory", new[] { new BuildSite(24, 8, Direction.Right) }));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));

        Unit blue = s.FindBuilder(Setups.PlayerA)!;
        sim.Order(new BuildCommand(Setups.PlayerA, s.Tick, new[] { blue.Id }, "red_drill", new[] { new BuildSite(28, 4, Direction.Right) }));
        sim.Tick();
        Assert.That(sim.Events.OfType<CommandRejected>().Single().Reason, Is.EqualTo(RejectReason.NotAllowed));

        sim.Order(new BuildCommand(Setups.PlayerB, s.Tick, new[] { red.Id }, "red_drill", new[] { new BuildSite(28, 4, Direction.Right) }));
        sim.RunUntil(x => x.State.Buildings.Any(b => b.Stats.Id == "red_drill" && b.IsComplete), 60 * Simulation.TicksPerSecond);
        Building drill = s.Buildings.Single(b => b.Stats.Id == "red_drill");
        Assert.That(drill.Drill, Is.Not.Null);
        sim.RunUntil(x => drill.Drill!.Buffer > 0, 20 * Simulation.TicksPerSecond);
    }

    [Test]
    public void BadFactionRows_AreLoadErrors()
    {
        Assert.Throws<FormatException>(() => WithRedLightning(f => f[f.Count - 1]!["builder"] = "infantry"));
        Assert.Throws<FormatException>(() => WithRedLightning(f => f[f.Count - 1]!["headquarters"] = "red_drill"));
        Assert.Throws<FormatException>(() => WithRedLightning(f => f[f.Count - 1]!["buildings"] = new JsonArray("nothing")));
    }

    [Test]
    public void UnknownFactionInTheSetup_IsAnError()
    {
        Assert.Throws<ArgumentException>(() => Setup(RepoData.Get(), new PlayerFaction(Setups.PlayerB, "yellow_star")));
    }

    [Test]
    public void BehaviourComesFromTheRow()
    {
        GameData data = RepoData.Get();
        Assert.That(data.Building("conveyor_bridge").Behavior, Is.EqualTo(BuildingBehavior.Bridge));
        Assert.That(data.Building("conveyor_bridge").IsBelt, Is.True);
        Assert.That(data.Building("factory").Behavior, Is.EqualTo(BuildingBehavior.None));
        Assert.That(data.Faction("black_legion").SingleHeadquarters, Is.False);
    }
}
