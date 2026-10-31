using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class DeterministicRngTests
{
    [Test]
    public void Seed42_MatchesPcg32ReferenceOutput()
    {
        // pcg32-demo from the reference C implementation: srandom(42, 54).
        var rng = new DeterministicRng(42);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };

        foreach (uint value in expected)
            Assert.That(rng.NextUInt(), Is.EqualTo(value));
    }

    [Test]
    public void SameSeed_SameSequence()
    {
        var a = new DeterministicRng(123);
        var b = new DeterministicRng(123);
        for (int i = 0; i < 1000; i++)
            Assert.That(a.NextInt(1, 7), Is.EqualTo(b.NextInt(1, 7)));
        Assert.That(a.State, Is.EqualTo(b.State));
    }

    [Test]
    public void DifferentSeeds_DifferentSequences()
    {
        var a = new DeterministicRng(1);
        var b = new DeterministicRng(2);
        Assert.That(a.NextUInt(), Is.Not.EqualTo(b.NextUInt()));
    }

    [Test]
    public void NextInt_StaysInRange_AndCoversIt()
    {
        var rng = new DeterministicRng(7);
        var seen = new bool[6];
        for (int i = 0; i < 1000; i++)
        {
            int value = rng.NextInt(-3, 3);
            Assert.That(value, Is.InRange(-3, 2));
            seen[value + 3] = true;
        }
        Assert.That(seen, Is.All.True);
    }

    [Test]
    public void NextInt_One_AlwaysZero()
    {
        var rng = new DeterministicRng(7);
        for (int i = 0; i < 100; i++)
            Assert.That(rng.NextInt(1), Is.Zero);
    }

    [Test]
    public void NextInt_InvalidBounds_Throw()
    {
        var rng = new DeterministicRng(7);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(int.MinValue, int.MaxValue));
    }
}
