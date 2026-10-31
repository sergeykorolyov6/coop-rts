using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

// Mandatory by CLAUDE.md: same seed + same commands -> same StateHash on every tick.
public class DeterminismTests
{
    private const int Ticks = 200;

    [Test]
    public void SameSeedAndCommands_SameHashEveryTick_TwiceInARow()
    {
        var first = Run(Setups.TwoAlliesAndEnemy(seed: 42), Script());
        var second = Run(Setups.TwoAlliesAndEnemy(seed: 42), Script());

        Assert.That(second, Is.EqualTo(first));
        // A hash that never changes would pass the check above while proving nothing.
        Assert.That(first.Distinct().Count(), Is.EqualTo(first.Length));
    }

    [Test]
    public void ArrivalOrderBetweenPlayers_DoesNotChangeHashes()
    {
        var script = Script();
        // Same per-player order, but player 1 now arrives before player 2 on every tick
        // (the script issues player 2 first), as the network might deliver them.
        var reordered = script.Where(c => c.PlayerId == 1)
            .Concat(script.Where(c => c.PlayerId == 9))
            .Concat(script.Where(c => c.PlayerId == 2))
            .ToList();

        Assert.That(Run(Setups.TwoAlliesAndEnemy(), reordered), Is.EqualTo(Run(Setups.TwoAlliesAndEnemy(), script)));
    }

    [Test]
    public void ReplayFromSerializedLog_GivesSameHashes()
    {
        var codec = TestCommands.CreateCodec();
        var script = Script();
        var replayed = codec.ReadLog(codec.WriteLog(script));

        Assert.That(Run(Setups.TwoAlliesAndEnemy(), replayed), Is.EqualTo(Run(Setups.TwoAlliesAndEnemy(), script)));
    }

    [Test]
    public void DifferentSeed_DifferentHashes()
    {
        var a = Run(Setups.TwoAlliesAndEnemy(seed: 1), Script());
        var b = Run(Setups.TwoAlliesAndEnemy(seed: 2), Script());

        for (int tick = 0; tick < Ticks; tick++)
            Assert.That(b[tick], Is.Not.EqualTo(a[tick]), $"tick {tick}");
    }

    [Test]
    public void OneExtraResource_ChangesHashFromThatTickOn()
    {
        var script = Script();
        var withExtra = script.Append(new GrantCommand(2, 50, Setups.Secondary, 1)).ToList();

        var a = Run(Setups.TwoAlliesAndEnemy(), script);
        var b = Run(Setups.TwoAlliesAndEnemy(), withExtra);

        Assert.That(b.Take(50), Is.EqualTo(a.Take(50)));
        for (int tick = 50; tick < Ticks; tick++)
            Assert.That(b[tick], Is.Not.EqualTo(a[tick]), $"tick {tick}");
    }

    [Test]
    public void InitialHash_DependsOnSetupOnly()
    {
        Assert.That(new Simulation(Setups.TwoAlliesAndEnemy()).StateHash,
            Is.EqualTo(new Simulation(Setups.TwoAlliesAndEnemy()).StateHash));
        Assert.That(new Simulation(Setups.TwoAlliesAndEnemy()).StateHash,
            Is.Not.EqualTo(new Simulation(Setups.Solo()).StateHash));
    }

    // Both allies draw from the shared RNG on the same ticks, so execution order
    // between players decides who gets which number.
    private static List<ICommand> Script()
    {
        var commands = new List<ICommand>();
        for (int tick = 0; tick < Ticks; tick++)
        {
            commands.Add(new RandomGrantCommand(2, tick, 5));
            commands.Add(new RandomGrantCommand(1, tick, 5));
            if (tick % 10 == 0)
                commands.Add(new GrantCommand(1, tick, Setups.Secondary, 3));
            if (tick % 25 == 0)
                commands.Add(new MarkCommand(9, tick, "wave"));
        }
        return commands;
    }

    private static ulong[] Run(MatchSetup setup, IEnumerable<ICommand> commands)
    {
        var sim = new Simulation(setup);
        foreach (ICommand command in commands)
            sim.Enqueue(command);

        var hashes = new ulong[Ticks];
        for (int tick = 0; tick < Ticks; tick++)
        {
            sim.Tick();
            hashes[tick] = sim.StateHash;
        }
        return hashes;
    }
}
