using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class RelationsTests
{
    [Test]
    public void SetupRelations_AreApplied_Symmetrically()
    {
        var relations = new SimState(Setups.TwoAlliesAndEnemy()).Relations;

        Assert.That(relations.Get(1, 2), Is.EqualTo(Relation.Ally));
        Assert.That(relations.Get(2, 1), Is.EqualTo(Relation.Ally));
        Assert.That(relations.Get(9, 1), Is.EqualTo(Relation.Enemy));
        Assert.That(relations.IsEnemy(2, 9), Is.True);
    }

    [Test]
    public void UnsetPair_IsNeutral()
    {
        var setup = new MatchSetup(1, 8, 8, 2, new[] { 1, 2 }, Array.Empty<RelationSetup>());
        var relations = new SimState(setup).Relations;

        Assert.That(relations.Get(1, 2), Is.EqualTo(Relation.Neutral));
    }

    [Test]
    public void Player_IsAllyOfItself()
    {
        var relations = new SimState(Setups.TwoAlliesAndEnemy()).Relations;

        Assert.That(relations.IsAlly(9, 9), Is.True);
        Assert.Throws<ArgumentException>(() => relations.Set(1, 1, Relation.Enemy));
    }

    [Test]
    public void Set_ChangesBothDirections()
    {
        var relations = new SimState(Setups.TwoAlliesAndEnemy()).Relations;
        relations.Set(2, 1, Relation.Neutral);

        Assert.That(relations.Get(1, 2), Is.EqualTo(Relation.Neutral));
    }

    [Test]
    public void UnknownPlayer_Throws()
    {
        var relations = new SimState(Setups.TwoAlliesAndEnemy()).Relations;
        Assert.Throws<ArgumentException>(() => relations.Get(1, 5));
    }
}
