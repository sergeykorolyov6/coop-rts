using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class GridTests
{
    [Test]
    public void InBounds_CoversExactlyTheMap()
    {
        var grid = new Grid(64, 32);
        Assert.That(grid.InBounds(0, 0), Is.True);
        Assert.That(grid.InBounds(63, 31), Is.True);
        Assert.That(grid.InBounds(64, 0), Is.False);
        Assert.That(grid.InBounds(0, 32), Is.False);
        Assert.That(grid.InBounds(-1, 0), Is.False);
        Assert.That(grid.InBounds(0, -1), Is.False);
    }

    [TestCase(0, 0)]
    [TestCase(999, 0)]
    [TestCase(1000, 1)]
    [TestCase(1500, 1)]
    [TestCase(-1, -1)]
    [TestCase(-1000, -1)]
    [TestCase(-1001, -2)]
    public void MilliToCell_FloorsToContainingCell(int milli, int cell)
    {
        Assert.That(Grid.MilliToCell(milli), Is.EqualTo(cell));
    }

    [Test]
    public void CellToMilli_IsCellOrigin()
    {
        Assert.That(Grid.CellToMilli(3), Is.EqualTo(3000));
        Assert.That(Grid.MilliToCell(Grid.CellToMilli(-5)), Is.EqualTo(-5));
    }

    [Test]
    public void NonPositiveSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Grid(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Grid(10, -1));
    }
}
