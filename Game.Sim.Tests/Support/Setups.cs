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

    public const int PlayerA = 1;
    public const int PlayerB = 2;
    public const int EnemyAi = 9;
    public const int Creeps = 10;

    // The co-op layout with real balance data on a given map: A and B allied,
    // the enemy AI and the creeps hostile to everyone.
    public static MatchSetup Coop(MapSetup map, int seed = 42) =>
        new MatchSetup(
            seed,
            RepoData.Get(),
            map,
            resourceTypeCount: 2,
            playerIds: new[] { PlayerA, PlayerB, EnemyAi, Creeps },
            relations: new[]
            {
                new RelationSetup(PlayerA, PlayerB, Relation.Ally),
                new RelationSetup(PlayerA, EnemyAi, Relation.Enemy),
                new RelationSetup(PlayerB, EnemyAi, Relation.Enemy),
                new RelationSetup(PlayerA, Creeps, Relation.Enemy),
                new RelationSetup(PlayerB, Creeps, Relation.Enemy),
                new RelationSetup(EnemyAi, Creeps, Relation.Enemy),
            },
            creepPlayerId: Creeps);

    public static MatchSetup Solo(int seed = 42) =>
        new MatchSetup(seed, 16, 16, 2, new[] { 1 }, Array.Empty<RelationSetup>());
}
