using System;
using System.Collections.Generic;

namespace Game.Sim;

// Everything needed to start a match. Together with the command log it fully
// defines the match: replays and saves are built on that.
public sealed class MatchSetup
{
    public const int NoPlayer = -1;

    private readonly List<int> _playerIds;
    private readonly List<RelationSetup> _relations;

    // A real match: balance data, hand-made map with starts and camps.
    // creepPlayerId owns the creeps (Grey Creatures); it must be one of playerIds when the map has camps.
    public MatchSetup(
        int seed,
        GameData data,
        MapSetup map,
        int resourceTypeCount,
        IEnumerable<int> playerIds,
        IEnumerable<RelationSetup> relations,
        int creepPlayerId = NoPlayer)
        : this(seed, map.Width, map.Height, resourceTypeCount, playerIds, relations)
    {
        Data = data;
        Map = map;
        CreepPlayerId = creepPlayerId;

        foreach (PlayerStart start in map.Starts)
        {
            if (_playerIds.BinarySearch(start.PlayerId) < 0)
                throw new ArgumentException($"Map start for unknown player {start.PlayerId}.", nameof(map));
            if (start.PlayerId == creepPlayerId)
                throw new ArgumentException("The creep player cannot have a start.", nameof(map));
        }
        if (map.CreepCamps.Count > 0 && _playerIds.BinarySearch(creepPlayerId) < 0)
            throw new ArgumentException("The map has creep camps but no creep player.", nameof(creepPlayerId));
        foreach (CreepCampSetup camp in map.CreepCamps)
        {
            CreepCampStats stats = data.CreepCamp(camp.CampTypeId);
            if (camp.CreepCells.Count != stats.Creeps.Count)
                throw new ArgumentException($"Camp \"{camp.CampTypeId}\" at ({camp.CenterX}, {camp.CenterY}) needs {stats.Creeps.Count} creep cells, has {camp.CreepCells.Count}.", nameof(map));
        }
    }

    // An empty map without balance data: enough for the core tick loop.
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
        Data = GameData.Empty;
        Map = new MapSetup(mapWidth, mapHeight);
        CreepPlayerId = NoPlayer;
    }

    public GameData Data { get; }
    public MapSetup Map { get; }
    public int CreepPlayerId { get; }

    public int Seed { get; }
    public int MapWidth { get; }
    public int MapHeight { get; }
    public int ResourceTypeCount { get; }

    // Ascending.
    public IReadOnlyList<int> PlayerIds => _playerIds;

    public IReadOnlyList<RelationSetup> Relations => _relations;
}
