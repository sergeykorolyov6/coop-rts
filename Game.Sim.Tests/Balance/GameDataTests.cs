using System;
using System.IO;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class GameDataTests
{
    private const string Units = @"[
      {""id"": ""worker"", ""name_ru"": ""Рабочий"", ""faction"": ""blue_walls"", ""hp"": 60, ""armor"": 0,
       ""damage"": 4, ""damage_vs_buildings"": 4, ""attack_range_mc"": 500, ""attack_cooldown_ticks"": 20,
       ""move_speed_mc_per_tick"": 120, ""vision_mc"": 5000, ""cost_primary"": 10, ""cost_secondary"": 0,
       ""build_time_ticks"": 240, ""produced_at"": ""factory"", ""size_mc"": 500, ""tags"": [""worker"", ""melee""], ""abilities"": [], ""projectile"": """", ""description"": """"},
      {""id"": ""creep_small"", ""name_ru"": ""Малый крип"", ""faction"": ""grey_creatures"", ""hp"": 80, ""armor"": 0,
       ""damage"": 6, ""damage_vs_buildings"": 6, ""attack_range_mc"": 500, ""attack_cooldown_ticks"": 20,
       ""move_speed_mc_per_tick"": 110, ""vision_mc"": 5000, ""cost_primary"": 0, ""cost_secondary"": 0,
       ""build_time_ticks"": 0, ""produced_at"": """", ""size_mc"": 600, ""tags"": [""melee"", ""creep""], ""abilities"": [], ""projectile"": """", ""description"": """"}
    ]";

    private const string Buildings = @"[
      {""id"": ""factory"", ""name_ru"": ""Завод"", ""faction"": ""blue_walls"", ""hp"": 1200, ""armor"": 1,
       ""size_w"": 3, ""size_h"": 3, ""cost_primary"": 50, ""cost_secondary"": 0, ""build_time_ticks"": 700,
       ""max_builders"": 4, ""vision_mc"": 7000, ""damage"": 0, ""attack_range_mc"": 0,
       ""attack_cooldown_ticks"": 0, ""behavior"": """", ""produces"": [""worker""], ""supply"": ""conveyor"", ""invulnerable"": 0, ""placement"": """", ""ammo"": 0, ""boost_pct"": 0, ""projectile"": """", ""description"": """"}
    ]";

    private const string Camps = @"[
      {""id"": ""small_camp"", ""name_ru"": ""Малый лагерь"", ""creeps"": [""creep_small"", ""creep_small""],
       ""aggro_radius_mc"": 5000, ""leash_radius_mc"": 8000, ""regen_pct_per_s"": 5, ""calm_delay_ticks"": 100,
       ""trophy_primary"": 30, ""trophy_secondary"": 0, ""trophy_per_trip"": 5}
    ]";

    internal const string Rules = @"{
      ""bridge_range"": 4, ""construction_start_hp_pct"": 10, ""conveyor_item_spacing_mc"": 1000,
      ""conveyor_item_speed_mc_per_tick"": 200, ""drill_buffer"": 10, ""drill_rate_per_ore_cell_ticks_per_unit"": 160,
      ""interact_range_mc"": 1000, ""manual_cargo"": 5, ""manual_mining_rate_ticks_per_unit"": 40,
      ""repair_cost_pct"": 50, ""start_primary"": 100, ""start_secondary"": 0,
      ""storage_capacity"": 100, ""factory_queue"": 5, ""attack_alarm_cooldown_ticks"": 100,
      ""ai_think_interval_ticks"": 20, ""ai_wave_repeat_ticks"": 2400, ""ai_wave_warning_ticks"": 400,
      ""ai_defense_calm_ticks"": 200, ""ai_rebuild_safe_radius_mc"": 8000,
      ""auto_repair_radius_mc"": 8000, ""factory_stock"": 150,
      ""capture_time_ticks"": 200, ""capture_radius_mc"": 2000, ""lighthouse_radius_mc"": 2000,
      ""demolish_time_pct"": 25,
      ""build_speed_pct"": 150
    }";

    [Test]
    public void Rules_AreRead()
    {
        GameRules rules = GameData.Parse(Units, Buildings, Camps, Rules).Rules;
        Assert.That(rules.DrillTicksPerUnitPerOreCell, Is.EqualTo(160));
        Assert.That(rules.ConveyorItemSpeedMilliPerTick, Is.EqualTo(200));
        Assert.That(rules.DemolishTimePercent, Is.EqualTo(25));
    }

    [Test]
    public void UnknownRule_IsAnError()
    {
        string rules = Rules.Replace("\"storage_capacity\": 100", "\"storage_capacity\": 100, \"magic\": 1");
        var ex = Assert.Throws<FormatException>(() => GameData.Parse(Units, Buildings, Camps, rules));
        Assert.That(ex!.Message, Does.Contain("magic"));
    }

    [Test]
    public void MissingRule_IsAnError()
    {
        string rules = Rules.Replace("\"bridge_range\": 4,", "");
        var ex = Assert.Throws<FormatException>(() => GameData.Parse(Units, Buildings, Camps, rules));
        Assert.That(ex!.Message, Does.Contain("bridge_range"));
    }

    [Test]
    public void Parse_ReadsEveryField()
    {
        GameData data = GameData.Parse(Units, Buildings, Camps, Rules);

        UnitStats worker = data.Unit("worker");
        Assert.That(worker.Hp, Is.EqualTo(60));
        Assert.That(worker.MoveSpeedMilliPerTick, Is.EqualTo(120));
        Assert.That(worker.AttackCooldownTicks, Is.EqualTo(20));
        Assert.That(worker.ProducedAt, Is.EqualTo("factory"));
        Assert.That(worker.HasTag("worker"), Is.True);
        Assert.That(worker.HasTag("creep"), Is.False);

        BuildingStats factory = data.Building("factory");
        Assert.That(factory.SizeW, Is.EqualTo(3));
        Assert.That(factory.BuildTimeTicks, Is.EqualTo(700));
        Assert.That(factory.MaxBuilders, Is.EqualTo(4));
        Assert.That(factory.Produces, Is.EqualTo(new[] { "worker" }));
        Assert.That(factory.SuppliedByConveyor, Is.True);
        Assert.That(factory.Invulnerable, Is.False);

        CreepCampStats camp = data.CreepCamp("small_camp");
        Assert.That(camp.Creeps, Is.EqualTo(new[] { "creep_small", "creep_small" }));
        Assert.That(camp.AggroRadiusMilli, Is.EqualTo(5000));
        Assert.That(camp.CalmDelayTicks, Is.EqualTo(100));
        Assert.That(camp.TrophyPrimary, Is.EqualTo(30));
    }

    [Test]
    public void Lists_AreSortedById()
    {
        GameData data = GameData.Parse(Units, Buildings, Camps, Rules);
        Assert.That(data.Units[0].Id, Is.EqualTo("creep_small"));
        Assert.That(data.Units[1].Id, Is.EqualTo("worker"));
    }

    [Test]
    public void UnknownId_Throws()
    {
        GameData data = GameData.Parse(Units, Buildings, Camps, Rules);
        Assert.Throws<ArgumentException>(() => data.Unit("tank"));
        Assert.That(data.TryGetUnit("tank", out _), Is.False);
    }

    [Test]
    public void DuplicateId_IsAnError()
    {
        string units = "[" + Units.Trim().Trim('[', ']') + "," + Units.Trim().Trim('[', ']') + "]";
        var ex = Assert.Throws<FormatException>(() => GameData.Parse(units, Buildings, Camps, Rules));
        Assert.That(ex!.Message, Does.Contain("duplicate id"));
    }

    // Cross-references catch a typo in the design tables before a match starts.
    [Test]
    public void FactoryProducingUnknownUnit_IsAnError()
    {
        string buildings = Buildings.Replace("[\"worker\"]", "[\"workr\"]");
        var ex = Assert.Throws<FormatException>(() => GameData.Parse(Units, buildings, Camps, Rules));
        Assert.That(ex!.Message, Does.Contain("workr"));
    }

    [Test]
    public void UnitProducedAtUnknownBuilding_IsAnError()
    {
        string units = Units.Replace("\"produced_at\": \"factory\"", "\"produced_at\": \"barracks\"");
        var ex = Assert.Throws<FormatException>(() => GameData.Parse(units, Buildings, Camps, Rules));
        Assert.That(ex!.Message, Does.Contain("barracks"));
    }

    [Test]
    public void CampWithUnknownCreep_IsAnError()
    {
        string camps = Camps.Replace("[\"creep_small\", \"creep_small\"]", "[\"creep_tiny\"]");
        var ex = Assert.Throws<FormatException>(() => GameData.Parse(Units, Buildings, camps, Rules));
        Assert.That(ex!.Message, Does.Contain("creep_tiny"));
    }

    [Test]
    public void NonPositiveHp_IsAnError()
    {
        string units = Units.Replace("\"hp\": 60", "\"hp\": 0");
        Assert.Throws<FormatException>(() => GameData.Parse(units, Buildings, Camps, Rules));
    }

    // The json actually shipped in Assets/Data, generated by Tools/csv2json.py.
    [Test]
    public void RepoData_LoadsAndMatchesTheSpecs()
    {
        GameData data = LoadRepoData();

        // drill.md: 10 s with one builder = 200 units of work, up to 3 builders, 30 units, 300 HP.
        BuildingStats drill = data.Building("drill");
        Assert.That(drill.BuildTimeTicks, Is.EqualTo(200));
        Assert.That(drill.MaxBuilders, Is.EqualTo(3));
        Assert.That(drill.CostPrimary, Is.EqualTo(30));
        Assert.That(drill.Hp, Is.EqualTo(300));
        Assert.That(drill.SizeW, Is.EqualTo(2));
        Assert.That(drill.SizeH, Is.EqualTo(2));

        // units.csv: 3 cells/s = 150 milli-cells per tick; 0.8 s = 16 ticks.
        Assert.That(data.Unit("builder").MoveSpeedMilliPerTick, Is.EqualTo(150));
        Assert.That(data.Unit("builder").ProducedAt, Is.EqualTo(""));
        Assert.That(data.Building("factory").Produces, Does.Contain("repairer").And.Not.Contain("builder"));
        Assert.That(data.Building("cave").Invulnerable, Is.True);
        Assert.That(data.Unit("infantry").AttackCooldownTicks, Is.EqualTo(16));

        // creep-camp.md: M1 camp «Стражи».
        CreepCampStats guard = data.CreepCamp("guard_camp");
        Assert.That(guard.Creeps, Is.EqualTo(new[] { "creep_big", "creep_small" }));
        Assert.That(guard.AggroRadiusMilli, Is.EqualTo(5000));
        Assert.That(guard.LeashRadiusMilli, Is.EqualTo(7000));
        Assert.That(guard.TrophyPrimary, Is.EqualTo(60));

        // drill.md appendix: 1 unit per 160 ticks per ore cell; conveyors.md: 200 milli-cells per tick.
        Assert.That(data.Rules.DrillTicksPerUnitPerOreCell, Is.EqualTo(160));
        Assert.That(data.Rules.ManualMiningTicksPerUnit, Is.EqualTo(40));
        Assert.That(data.Rules.ConveyorItemSpeedMilliPerTick, Is.EqualTo(200));
        Assert.That(data.Rules.StartPrimary, Is.EqualTo(100));
    }

    private static GameData LoadRepoData() => RepoData.Load();
}
