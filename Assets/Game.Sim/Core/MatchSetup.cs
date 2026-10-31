using System;
using System.Collections.Generic;

namespace Game.Sim;

// Everything needed to start a match. Together with the command log it fully
// defines the match: replays and saves are built on that.
public sealed class MatchSetup
{
    private readonly List<int> _playerIds;
    private readonly List<RelationSetup> _relations;

    public MatchSetup(
        int seed,
        int mapWidth,
        int mapHeight,
        int resourceTypeCount,
        IEnumerable<int> playerIds,
        IEnumerable<RelationSetup> relations)
    {
        if (mapWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapWidth), mapWidth, "Must be positive.");
        if (mapHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapHeight), mapHeight, "Must be positive.");
        if (resourceTypeCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(resourceTypeCount), resourceTypeCount, "Must be positive.");

        _playerIds = new List<int>(playerIds);
        if (_playerIds.Count == 0)
            throw new ArgumentException("A match needs at least one player.", nameof(playerIds));

        // Sorted ids give every machine the same player order.
        _playerIds.Sort();
        for (int i = 0; i < _playerIds.Count; i++)
        {
            if (_playerIds[i] < 0)
                throw new ArgumentException($"Player id {_playerIds[i]} is negative.", nameof(playerIds));
            if (i > 0 && _playerIds[i] == _playerIds[i - 1])
                throw new ArgumentException($"Player id {_playerIds[i]} is duplicated.", nameof(playerIds));
        }

        _relations = new List<RelationSetup>(relations);
        foreach (RelationSetup relation in _relations)
        {
            if (_playerIds.BinarySearch(relation.PlayerA) < 0 || _playerIds.BinarySearch(relation.PlayerB) < 0)
                throw new ArgumentException($"Relation {relation.PlayerA}-{relation.PlayerB} names an unknown player.", nameof(relations));
            if (relation.PlayerA == relation.PlayerB)
                throw new ArgumentException($"Relation of player {relation.PlayerA} with itself.", nameof(relations));
        }

        Seed = seed;
        MapWidth = mapWidth;
        MapHeight = mapHeight;
        ResourceTypeCount = resourceTypeCount;
    }

    public int Seed { get; }
    public int MapWidth { get; }
    public int MapHeight { get; }
    public int ResourceTypeCount { get; }

    // Ascending.
    public IReadOnlyList<int> PlayerIds => _playerIds;

    public IReadOnlyList<RelationSetup> Relations => _relations;
}
