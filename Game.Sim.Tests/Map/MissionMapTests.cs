using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

// The M2 mission map in Assets/Data/maps/m2.json (from Docs/Design/maps/m2.txt), mission.md.
public class MissionMapTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Minute = 60 * Second;
    private const int Legion = Setups.EnemyAi;
    private static readonly int[] Four = { 1, 2, 3, 4 };

    private static Simulation Match(int players, int seed = 42)
    {
        int[] ids = Four.Take(players).ToArray();
        MapSetup map = MapLoader.Parse(File.ReadAllText(Path.Combine(RepoData.RepoRoot(), "Assets", "Data", "maps", "m2.json")), ids);
        var all = new List<int>(ids) { Legion, Setups.Creeps };
        var relations = new List<RelationSetup>();
        foreach (int a in ids)
        {
            foreach (int b in ids)
            {
                if (a < b)
                    relations.Add(new RelationSetup(a, b, Relation.Ally));
            }
            relations.Add(new RelationSetup(a, Legion, Relation.Enemy));
        }
        return new Simulation(new MatchSetup(seed, RepoData.Get(), map, 2, all, relations, Setups.Creeps, Legion));
    }

    [Test]
    public void GWT6_Solo_PlayerStart_AndTheLegionsBaseFromTheMap()
    {
        Simulation sim = Match(1);
        SimState s = sim.State;
        Assert.That((s.Map.Width, s.Map.Height), Is.EqualTo((80, 80)));
        Assert.That(s.TeamPlayerIds, Is.EqualTo(new[] { 1 }));
        Assert.That(s.FindHeadquarters(1), Is.Not.Null);
        Assert.That(s.UnitsOf(1).Count(u => u.IsBuilder), Is.EqualTo(1));
        Assert.That(s.GetPlayer(1).Wallet.Get(Resources.Primary), Is.EqualTo(100));

        LegionAi ai = s.Legion!;
        Assert.That(ai.Slots.All(slot => s.FindBuilding(slot.BuildingId)?.IsComplete == true), Is.True);
        Assert.That(ai.Slots.Count(slot => slot.Setup.BuildingTypeId == "turret"), Is.EqualTo(3));
        Assert.That(ai.Slots.Count(slot => slot.Setup.BuildingTypeId == "legion_factory"), Is.EqualTo(2));
        Assert.That(s.CreepCamps, Has.Count.EqualTo(3));
    }

    [Test]
    public void LegionDrillsAndBelts_BringBothResources()
    {
        Simulation sim = Match(1);
        sim.Run(Minute);
        Wallet wallet = sim.State.GetPlayer(Legion).Wallet;
        // 4 primary drills × 0,5 ед./с and one second-resource drill: about 120 and 30 a minute.
        Assert.That(wallet.Earned(Resources.Primary), Is.InRange(100, 125));
        Assert.That(wallet.Earned(Resources.Secondary), Is.InRange(20, 30));
    }

    [Test]
    public void FourPlayers_StrongerLegion_AllStartsTaken()
    {
        Simulation sim = Match(4);
        Assert.That(sim.State.Legion!.Stats.Players, Is.EqualTo(4));
        Assert.That(Four.All(p => sim.State.FindHeadquarters(p) != null), Is.True);
        Assert.That(sim.State.GetPlayer(Legion).Wallet.IncomePercent, Is.EqualTo(300));
    }

    [Test]
    public void FirstWave_ReachesThePlayersBase()
    {
        Simulation sim = Match(1);
        Building hq = sim.State.FindHeadquarters(1)!;
        var launched = new List<WaveLaunched>();
        sim.RunUntil(s =>
        {
            launched.AddRange(s.Events.OfType<WaveLaunched>());
            return hq.Hp < hq.MaxHp;
        }, 5 * Minute);
        Assert.That(launched.Single().Wave, Is.EqualTo(1));
        Assert.That(launched[0].UnitCount, Is.EqualTo(2));
    }

    [Test]
    public void APlayerWhoDoesNothing_LosesWithinFifteenMinutes()
    {
        Simulation sim = Match(1);
        sim.RunUntil(s => s.State.Outcome != MissionOutcome.InProgress, 15 * Minute);
        Assert.That(sim.State.Outcome, Is.EqualTo(MissionOutcome.Defeat));
    }

    [Test]
    public void Determinism_FifteenMinutes_TwoPlayers_SameHashEveryTick()
    {
        var hashes = new List<ulong>();
        Match(2).Run(15 * Minute, s => hashes.Add(s.StateHash));
        int i = 0;
        Match(2).Run(15 * Minute, s => Assert.That(s.StateHash, Is.EqualTo(hashes[i++])));
    }
}
