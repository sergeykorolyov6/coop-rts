using NUnit.Framework;

namespace Game.Sim.Tests;

public class GridTests
{
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
}
