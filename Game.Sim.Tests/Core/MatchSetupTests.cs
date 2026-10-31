using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class MatchSetupTests
{
    [Test]
    public void PlayerIds_AreSorted()
    {
        var setup = new MatchSetup(1, 8, 8, 2, new[] { 9, 2, 1 }, Array.Empty<RelationSetup>());
        Assert.That(setup.PlayerIds, Is.EqualTo(new[] { 1, 2, 9 }));
    }

    [Test]
    public void InvalidPlayers_Throw()
    {
        Assert.Throws<ArgumentException>(() => new MatchSetup(1, 8, 8, 2, Array.Empty<int>(), Array.Empty<RelationSetup>()));
        Assert.Throws<ArgumentException>(() => new MatchSetup(1, 8, 8, 2, new[] { 1, 1 }, Array.Empty<RelationSetup>()));
        Assert.Throws<ArgumentException>(() => new MatchSetup(1, 8, 8, 2, new[] { -1 }, Array.Empty<RelationSetup>()));
    }

    [Test]
    public void InvalidRelations_Throw()
    {
        Assert.Throws<ArgumentException>(() =>
            new MatchSetup(1, 8, 8, 2, new[] { 1, 2 }, new[] { new RelationSetup(1, 3, Relation.Ally) }));
        Assert.Throws<ArgumentException>(() =>
            new MatchSetup(1, 8, 8, 2, new[] { 1, 2 }, new[] { new RelationSetup(1, 1, Relation.Enemy) }));
    }

    [Test]
    public void InvalidSizes_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchSetup(1, 0, 8, 2, new[] { 1 }, Array.Empty<RelationSetup>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchSetup(1, 8, 0, 2, new[] { 1 }, Array.Empty<RelationSetup>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchSetup(1, 8, 8, 0, new[] { 1 }, Array.Empty<RelationSetup>()));
    }
}
