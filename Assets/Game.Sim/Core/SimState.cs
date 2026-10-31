using System;
using System.Collections.Generic;

namespace Game.Sim;

// The whole mutable match state. Readable by everyone; mutated only by the
// simulation's tick through internal members.
public sealed class SimState
{
    private readonly List<Player> _players;

    internal SimState(MatchSetup setup)
    {
        Grid = new Grid(setup.MapWidth, setup.MapHeight);
        Rng = new DeterministicRng(setup.Seed);
        ResourceTypeCount = setup.ResourceTypeCount;

        _players = new List<Player>(setup.PlayerIds.Count);
        foreach (int id in setup.PlayerIds)
            _players.Add(new Player(id, setup.ResourceTypeCount));

        Relations = new Relations(setup.PlayerIds);
        foreach (RelationSetup relation in setup.Relations)
            Relations.Set(relation.PlayerA, relation.PlayerB, relation.Relation);
    }

    // The tick that will be executed next.
    public int Tick { get; internal set; }

    public Grid Grid { get; }
    public int ResourceTypeCount { get; }

    // Sorted by id.
    public IReadOnlyList<Player> Players => _players;

    public Relations Relations { get; }

    // Internal: anything outside the sim drawing from it would desync the match.
    internal DeterministicRng Rng { get; }

    public bool HasPlayer(int playerId) => FindPlayer(playerId) != null;

    public Player GetPlayer(int playerId) =>
        FindPlayer(playerId) ?? throw new ArgumentException($"Unknown player {playerId}.", nameof(playerId));

    private Player? FindPlayer(int playerId)
    {
        foreach (Player player in _players)
        {
            if (player.Id == playerId)
                return player;
        }
        return null;
    }
}
