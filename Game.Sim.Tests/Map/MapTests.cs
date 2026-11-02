using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class MapTests
{
    [Test]
    public void Deposits_AreConnectedPatchesOfOneType_NumberedRowMajor()
    {
        var map = new MapSetup(16, 16)
            .OreRect(10, 2, 2, 2, Resources.Primary)   // later in row-major order
            .OreRect(1, 1, 3, 2, Resources.Primary)    // first
            .Ore(4, 1, Resources.Secondary);           // touches the first patch but is another type

        var state = new SimState(Setups.Coop(map));

        Assert.That(state.Map.Deposits, Has.Count.EqualTo(3));
        Assert.That(state.Map.Deposits[0].Cells, Has.Count.EqualTo(6));
        Assert.That(state.Map.Deposits[0].ResourceType, Is.EqualTo(Resources.Primary));
        Assert.That(state.Map.Deposits[1].ResourceType, Is.EqualTo(Resources.Secondary));
        Assert.That(state.Map.Deposits[2].Cells, Has.Count.EqualTo(4));
        Assert.That(state.Map.DepositAt(1, 1), Is.EqualTo(1));
        Assert.That(state.Map.DepositAt(4, 1), Is.EqualTo(2));
        Assert.That(state.Map.DepositAt(0, 0), Is.EqualTo(0));
    }

    [Test]
    public void BlockedCells_AreNotPassableOrBuildable()
    {
        var state = new SimState(Setups.Coop(new MapSetup(8, 8).Block(3, 3)));
        Assert.That(state.Map.IsPassable(3, 3), Is.False);
        Assert.That(state.Map.IsBuildable(3, 3), Is.False);
        Assert.That(state.Map.IsPassable(2, 3), Is.True);
        Assert.That(state.Map.IsPassable(-1, 0), Is.False);
    }

    [Test]
    public void MapSetup_RejectsCellsOutsideTheMap()
    {
        var map = new MapSetup(8, 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Block(8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Ore(0, -1, Resources.Primary));
    }
}
