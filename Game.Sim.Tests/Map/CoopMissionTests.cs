using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using static Game.Sim.Tests.SimExtensions;

namespace Game.Sim.Tests;

// The co-op mission (mission.md, section M2+): missions.json row "coop", maps/coop.json.
public class CoopMissionTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Minute = 60 * Second;

    internal static MissionCatalog Catalog() =>
        MissionCatalog.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "Data", "missions.json")));

    internal static string MapJson(string map) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "Data", "maps", map + ".json"));

    private static string RepoRoot() => RepoData.RepoRoot();

    internal static Simulation Launch(string missionId, int seed = 42)
    {
        MissionStats mission = Catalog().Find(missionId)!;
        return new Simulation(MissionLauncher.CreateSetup(RepoData.Get(), mission, MapJson(mission.Map), seed));
    }

    private static Unit Boss(Simulation sim) => sim.State.Units.Single(u => u.Stats.HasTag(UnitTags.Boss));

    [Test]
    public void Catalog_HasBothMissions()
    {
        MissionCatalog catalog = Catalog();
        Assert.That(catalog.Missions.Select(m => m.Id), Is.EquivalentTo(new[] { "m2", "m2_red", "coop" }));
        Assert.That(catalog.Find("m2")!.Goal, Is.EqualTo(MissionGoal.DestroyLegion));
        Assert.That(catalog.Find("coop")!.Goal, Is.EqualTo(MissionGoal.KillBoss));
        Assert.That(catalog.Find("coop")!.HasLegion, Is.False);
        // factions.md: Red Lightning plays red, Blue Walls blue.
        Assert.That(catalog.Find("m2_red")!.Colors, Is.EqualTo(new[] { "red" }));
        Assert.That(catalog.Find("coop")!.Colors, Is.EqualTo(new[] { "blue", "red" }));
    }

    [Test]
    public void C1_Start_TwoPlayersWithBuilders_NoLegion_OneBoss_BigMap()
    {
        Simulation sim = Launch("coop");
        SimState s = sim.State;
        Assert.That((s.Map.Width, s.Map.Height), Is.EqualTo((128, 128)));
        Assert.That(s.TeamPlayerIds, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(s.Legion, Is.Null);
        Assert.That(s.Goal, Is.EqualTo(MissionGoal.KillBoss));
        foreach (int p in s.TeamPlayerIds)
        {
            Assert.That(s.FindHeadquarters(p), Is.Not.Null);
            Assert.That(s.UnitsOf(p).Single().IsBuilder, Is.True);
        }
        Assert.That(s.Units.Count(u => u.Stats.HasTag(UnitTags.Boss)), Is.EqualTo(1));
        Assert.That(s.CreepCamps.Count, Is.GreaterThanOrEqualTo(8));
        Assert.That(s.Buildings.Count(b => b.OwnerId == MissionLauncher.NeutralPlayerId && b.Stats.Id == "drill"), Is.EqualTo(3));
        Assert.That(s.Buildings.Count(b => b.Stats.Id == "lighthouse"), Is.EqualTo(2));
        Building cave = s.Buildings.Single(b => b.Stats.Id == "cave");
        Assert.That(s.CreepCamps[cave.GuardCampIndex].Stats.Id, Is.EqualTo("cave_guard"));
    }

    [Test]
    public void TheLairAndEveryNeutralBuilding_AreReachableFromTheStarts()
    {
        Simulation sim = Launch("coop");
        SimState s = sim.State;
        Unit builder = s.UnitsOf(1).Single();
        Unit boss = Boss(sim);
        FlowField toBoss = s.Paths.ToCell(s.Map, boss.CellX, boss.CellY);
        Assert.That(toBoss.DistanceAt(builder.CellX, builder.CellY), Is.Not.EqualTo(FlowField.Unreachable));
        foreach (Building b in s.Buildings.Where(b => b.OwnerId == MissionLauncher.NeutralPlayerId))
        {
            (int x, int y) = s.FreeCellsAround(b.CellX, b.CellY, b.Width, b.Height, 1)[0];
            FlowField field = s.Paths.ToCell(s.Map, x, y);
            Assert.That(field.DistanceAt(builder.CellX, builder.CellY), Is.Not.EqualTo(FlowField.Unreachable), b.Stats.Id);
        }
    }

    [Test]
    public void C2_OtherCreepsDead_BossAlive_GoesOn()
    {
        Simulation sim = Launch("coop");
        foreach (Unit u in sim.State.Units.Where(u => u.OwnerId == MissionLauncher.CreepPlayerId && !u.Stats.HasTag(UnitTags.Boss)).ToList())
            Kill(u);
        sim.Run(2);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test]
    public void C3_BossKilled_Victory()
    {
        Simulation sim = Launch("coop");
        Kill(Boss(sim));
        var ended = sim.RunCollecting<MissionEnded>(3);
        Assert.That(ended.Single().Outcome, Is.EqualTo(MissionOutcome.Victory));
    }

    [Test]
    public void C4_NoFinishedHeadquarters_LastBuilderDies_Defeat()
    {
        Simulation sim = Launch("coop");
        foreach (int p in sim.State.TeamPlayerIds)
            Kill(sim.State.FindHeadquarters(p)!);
        sim.Tick();
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
        foreach (Unit u in sim.State.Units.Where(u => u.IsBuilder).ToList())
            Kill(u);
        var ended = sim.RunCollecting<MissionEnded>(2);
        Assert.That(ended.Single().Outcome, Is.EqualTo(MissionOutcome.Defeat));
    }

    [Test]
    public void C5_NoHeadquartersOfA_ItsBuilderAlive_GoesOn()
    {
        Simulation sim = Launch("coop");
        Kill(sim.State.FindHeadquarters(1)!);
        Kill(sim.State.FindHeadquarters(2)!);
        sim.Run(60);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test]
    public void IdleTeam_IsNotAttackedByCreeps_InTenMinutes()
    {
        // Camps stand away from the starts: a passive team is not punished by creeps (only the boss is the goal).
        Simulation sim = Launch("coop");
        var attacked = sim.RunCollecting<BuildingAttacked>(10 * Minute);
        Assert.That(attacked, Is.Empty);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.InProgress));
    }

    [Test]
    public void Determinism_FiveMinutes_SameHashEveryTick()
    {
        var hashes = new List<ulong>();
        Launch("coop").Run(5 * Minute, s => hashes.Add(s.StateHash));
        int i = 0;
        Launch("coop").Run(5 * Minute, s => Assert.That(s.StateHash, Is.EqualTo(hashes[i++])));
    }
}
