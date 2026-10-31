using System;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class XxHash64Tests
{
    // Expected values from the reference implementation (python-xxhash, xxh64).
    [Test]
    public void Empty()
    {
        Assert.That(XxHash64.Compute(ReadOnlySpan<byte>.Empty), Is.EqualTo(0xEF46DB3751D8E999UL));
    }

    [Test]
    public void Abc()
    {
        Assert.That(XxHash64.Compute(Encoding.ASCII.GetBytes("abc")), Is.EqualTo(0x44BC2CF5AD770999UL));
    }

    [Test]
    public void Abc_WithSeed()
    {
        Assert.That(XxHash64.Compute(Encoding.ASCII.GetBytes("abc"), 123), Is.EqualTo(0x2DF10692FE3004B9UL));
    }

    [Test]
    public void FortyBytes_OneStripePlusTail()
    {
        byte[] data = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        Assert.That(XxHash64.Compute(data), Is.EqualTo(0xF5DA40F1B11741E9UL));
    }

    [Test]
    public void HundredBytes_SeveralStripesAndAllTailSizes()
    {
        byte[] data = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        Assert.That(XxHash64.Compute(data), Is.EqualTo(0x6AC1E58032166597UL));
    }
}
