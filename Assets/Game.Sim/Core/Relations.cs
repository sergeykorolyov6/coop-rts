using System;
using System.Collections.Generic;

namespace Game.Sim;

// Symmetric relation matrix over the match's players. Pairs not set are Neutral;
// a player counts as an ally of itself so "own or allied" checks need no special case.
public sealed class Relations
{
    private readonly IReadOnlyList<int> _playerIds;
    private readonly Relation[] _matrix;

    internal Relations(IReadOnlyList<int> sortedPlayerIds)
    {
        _playerIds = sortedPlayerIds;
        _matrix = new Relation[sortedPlayerIds.Count * sortedPlayerIds.Count];
        for (int i = 0; i < sortedPlayerIds.Count; i++)
            _matrix[i * sortedPlayerIds.Count + i] = Relation.Ally;
    }

    public int PlayerCount => _playerIds.Count;

    public Relation Get(int playerA, int playerB) => _matrix[Cell(IndexOf(playerA), IndexOf(playerB))];

    public bool IsAlly(int playerA, int playerB) => Get(playerA, playerB) == Relation.Ally;

    public bool IsEnemy(int playerA, int playerB) => Get(playerA, playerB) == Relation.Enemy;

    // By player index, for hashing in a fixed order.
    internal Relation GetByIndex(int indexA, int indexB) => _matrix[Cell(indexA, indexB)];

    internal void Set(int playerA, int playerB, Relation relation)
    {
        int a = IndexOf(playerA);
        int b = IndexOf(playerB);
        if (a == b)
            throw new ArgumentException($"Cannot change relation of player {playerA} with itself.");
        _matrix[Cell(a, b)] = relation;
        _matrix[Cell(b, a)] = relation;
    }

    private int Cell(int indexA, int indexB) => indexA * _playerIds.Count + indexB;

    private int IndexOf(int playerId)
    {
        for (int i = 0; i < _playerIds.Count; i++)
        {
            if (_playerIds[i] == playerId)
                return i;
        }
        throw new ArgumentException($"Unknown player {playerId}.", nameof(playerId));
    }
}
