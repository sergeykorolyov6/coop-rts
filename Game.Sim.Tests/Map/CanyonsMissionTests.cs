using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

// mission.md, the Canyons missions (T5, rows K; plan-terrain-and-factions.md, T5): the first big map — Docs/Design/maps/canyons.txt (192×192,
// 2–4 players, two Legion bases) — as the missions canyons2 and canyons4 launch it.
public class CanyonsMissionTests
{
    private const int Second = Simulation.TicksPerSecond;
    private const int Minute = 60 * Second;
    private static readonly int LegionWest = MissionLauncher.LegionPlayerIdFor(0), LegionEast = MissionLauncher.LegionPlayerIdFor(1);

    [Test]
    public void K1_TwoPlayers_BigMapWithHeights_TwoBases_EachAgainstItsOwnPlayer()
    {
        SimState s = RepoData.Launch("canyons2").State;

        Assert.That((s.Map.Width, s.Map.Height), Is.EqualTo((192, 192)));
        Assert.That(s.Map.HasLevels, Is.True);
        Assert.That(s.TeamPlayerIds, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(s.Legions.Select(l => l.PlayerId), Is.EqualTo(new[] { LegionWest, LegionEast }));
        Assert.That(s.Legions[0].TargetPlayerIds, Is.EqualTo(new[] { 1 }));
        Assert.That(s.Legions[1].TargetPlayerIds, Is.EqualTo(new[] { 2 }));
        // The middle plateaus: neighbours play together, each nearer to its own base.
        Building hq1 = s.FindHeadquarters(1)!, hq2 = s.FindHeadquarters(2)!;
        Assert.That(hq1.CellX, Is.LessThan(hq2.CellX));
        Assert.That(s.Legions[0].RallyX, Is.LessThan(s.Legions[1].RallyX));
        Assert.That(s.Map.LevelAt(hq1.CellX, hq1.CellY), Is.EqualTo(2), "bases stand on plateaus");
        Assert.That(s.Legions.All(l => s.Map.LevelAt(l.RallyX, l.RallyY) == 2), Is.True);
        Assert.That(s.Goal, Is.EqualTo(MissionGoal.DestroyLegion));
    }

    [Test]
    public void K2_FourPlayers_WestBaseAgainstOneAndThree_EastAgainstTwoAndFour()
    {
        SimState s = RepoData.Launch("canyons4").State;

        Assert.That(s.TeamPlayerIds, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(s.Legions[0].TargetPlayerIds, Is.EqualTo(new[] { 1, 3 }));
        Assert.That(s.Legions[1].TargetPlayerIds, Is.EqualTo(new[] { 2, 4 }));
        Assert.That(s.Legions.All(l => l.Stats.Players == 2), Is.True);
        // West base, west players; east base, east players.
        foreach (LegionAi ai in s.Legions)
        {
            foreach (int player in ai.TargetPlayerIds)
                Assert.That(s.FindHeadquarters(player)!.CellX < 96, Is.EqualTo(ai.RallyX < 96), $"player {player}");
        }
    }

    [Test]
    public void K3_TheMapIsWhatThePlanAsksFor_RockShare_PointsOfInterest_RichOreFarAway()
    {
        SimState s = RepoData.Launch("canyons4").State;
        MapState map = s.Map;
        int rock = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (map.IsBlocked(x, y))
                    rock++;
            }
        }
        Assert.That(rock * 100 / (map.Width * map.Height), Is.InRange(30, 40), "30–40 % of the cells are rock");

        int neutral = s.Buildings.Count(b => b.OwnerId == s.NeutralPlayerId);
        Assert.That(s.CreepCamps.Count + neutral, Is.InRange(20, 30), "20–30 points of interest besides the ore");
        Assert.That(s.Buildings.Count(b => b.Stats.Id == "cave" && s.IsGuarded(b)), Is.EqualTo(2));

        // The start deposits are small; the richest lie in the basin (level 0) and on the massif (level 3).
        var rich = map.Deposits.Where(d => d.Cells.Count >= 30).ToList();
        Assert.That(rich, Has.Count.GreaterThanOrEqualTo(4));
        Assert.That(rich.Any(d => map.LevelAt(d.Cells[0] % map.Width, d.Cells[0] / map.Width) == 0), Is.True);
        Assert.That(rich.Any(d => map.LevelAt(d.Cells[0] % map.Width, d.Cells[0] / map.Width) == 3), Is.True);
        foreach (int player in s.TeamPlayerIds)
        {
            Building hq = s.FindHeadquarters(player)!;
            Assert.That(rich.All(d => System.Math.Abs(d.Cells[0] / map.Width - hq.CellY) > 30), Is.True, $"player {player}: rich ore is far from the base");
        }
    }

    [Test]
    public void K4_FirstWaves_LeaveBothBases_ForTheirOwnPlayers_WithAnArrivalEstimate()
    {
        Simulation sim = RepoData.Launch("canyons2");
        var warnings = new List<WaveIncoming>();
        var launched = new List<WaveLaunched>();
        while (sim.State.Tick <= 3 * Minute)
        {
            sim.Tick();
            warnings.AddRange(sim.Events.OfType<WaveIncoming>());
            launched.AddRange(sim.Events.OfType<WaveLaunched>());
        }

        Assert.That(warnings.Select(w => (w.LegionPlayerId, w.TargetPlayerId)), Is.EqualTo(new[] { (LegionWest, 1), (LegionEast, 2) }));
        Assert.That(launched.Select(w => (w.LegionPlayerId, w.TargetPlayerId)), Is.EqualTo(new[] { (LegionWest, 1), (LegionEast, 2) }));
        foreach (WaveIncoming w in warnings)
        {
            // More than a hundred cells of ramps and canyons at a raider's pace: well over half a minute.
            Assert.That(w.ArrivalTick - w.LaunchTick, Is.InRange(35 * Second, 90 * Second));
        }
    }

    [Test]
    public void K5_FirstWave_ReallyArrives_AboutWhenTheWarningSaid()
    {
        Simulation sim = RepoData.Launch("canyons2");
        Building hq = sim.State.FindHeadquarters(1)!;
        int promised = 0;
        sim.RunUntil(s =>
        {
            foreach (WaveLaunched w in s.Events.OfType<WaveLaunched>().Where(w => w.TargetPlayerId == 1))
                promised = w.ArrivalTick;
            return hq.Hp < hq.MaxHp;
        }, 6 * Minute);

        // The estimate is to the edge of the building along cell centres; the raiders also crowd on the ramp.
        Assert.That(sim.State.Tick, Is.InRange(promised - 5 * Second, promised + 20 * Second));
    }

    [Test]
    public void Determinism_FiveMinutes_TwoPlayers_SameHashEveryTick()
    {
        var hashes = new List<ulong>();
        RepoData.Launch("canyons2", 9).Run(5 * Minute, s => hashes.Add(s.StateHash));
        int i = 0;
        RepoData.Launch("canyons2", 9).Run(5 * Minute, s => Assert.That(s.StateHash, Is.EqualTo(hashes[i++])));
    }
}
