using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class IntMathTests
{
    [TestCase(0L, 0L)]
    [TestCase(1L, 1L)]
    [TestCase(2L, 1L)]
    [TestCase(3L, 1L)]
    [TestCase(4L, 2L)]
    [TestCase(15L, 3L)]
    [TestCase(16L, 4L)]
    [TestCase(17L, 4L)]
    [TestCase(999_999L, 999L)]
    [TestCase(1_000_000L, 1000L)]
    [TestCase(long.MaxValue, 3_037_000_499L)]
    public void Sqrt_ReturnsFloor(long value, long expected)
    {
        Assert.That(IntMath.Sqrt(value), Is.EqualTo(expected));
    }

    [Test]
    public void Sqrt_AroundEverySquare_IsFloor()
    {
        for (long root = 1; root < 5000; root++)
        {
            long square = root * root;
            Assert.That(IntMath.Sqrt(square), Is.EqualTo(root));
            Assert.That(IntMath.Sqrt(square - 1), Is.EqualTo(root - 1));
            Assert.That(IntMath.Sqrt(square + 1), Is.EqualTo(root));
        }
    }

    [Test]
    public void Sqrt_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntMath.Sqrt(-1));
    }

    [TestCase(7, 2, 3)]
    [TestCase(-7, 2, -4)]
    [TestCase(7, -2, -4)]
    [TestCase(-7, -2, 3)]
    [TestCase(-1, 1000, -1)]
    [TestCase(-1000, 1000, -1)]
    [TestCase(-1001, 1000, -2)]
    [TestCase(0, 1000, 0)]
    [TestCase(999, 1000, 0)]
    public void FloorDiv_RoundsTowardNegativeInfinity(int dividend, int divisor, int expected)
    {
        Assert.That(IntMath.FloorDiv(dividend, divisor), Is.EqualTo(expected));
    }
}
