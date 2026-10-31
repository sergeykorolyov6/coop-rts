using System;

namespace Game.Sim.Tests;

internal static class Setups
{
    public const int Primary = 0;
    public const int Secondary = 1;

    // Two allied players against the enemy AI (player 9): the usual co-op layout.
    public static MatchSetup TwoAlliesAndEnemy(int seed = 42) =>
        new MatchSetup(
            seed,
            mapWidth: 64,
            mapHeight: 64,
            resourceTypeCount: 2,
            playerIds: new[] { 1, 2, 9 },
            relations: new[]
            {
                new RelationSetup(1, 2, Relation.Ally),
                new RelationSetup(1, 9, Relation.Enemy),
                new RelationSetup(2, 9, Relation.Enemy),
            });

    public static MatchSetup Solo(int seed = 42) =>
        new MatchSetup(seed, 16, 16, 2, new[] { 1 }, Array.Empty<RelationSetup>());
}
