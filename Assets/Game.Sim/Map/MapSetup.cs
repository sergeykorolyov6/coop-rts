using System;
using System.Collections.Generic;

namespace Game.Sim;

// The hand-made map before the match: terrain, ore, starts, creep camps.
// Built by the map loader or directly by tests; read-only once the match starts.
public sealed class MapSetup
{
    public const int NoOre = -1;

    private readonly bool[] _blocked;
    private readonly int[] _ore;
    private readonly List<PlayerStart> _starts = new List<PlayerStart>();
    private readonly List<CreepCampSetup> _camps = new List<CreepCampSetup>();

    public MapSetup(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Must be positive.");
        Width = width;
        Height = height;
        _blocked = new bool[width * height];
        _ore = new int[width * height];
        for (int i = 0; i < _ore.Length; i++)
            _ore[i] = NoOre;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<PlayerStart> Starts => _starts;
    public IReadOnlyList<CreepCampSetup> CreepCamps => _camps;

    public bool IsBlocked(int x, int y) => _blocked[Index(x, y)];

    // Resource type of the ore in the cell, or NoOre.
    public int OreAt(int x, int y) => _ore[Index(x, y)];

    public MapSetup Block(int x, int y)
    {
        _blocked[Index(x, y)] = true;
        return this;
    }

    public MapSetup Ore(int x, int y, int resourceType)
    {
        if (resourceType < 0)
            throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Must not be negative.");
        _ore[Index(x, y)] = resourceType;
        return this;
    }

    // Rectangle of ore, a convenience for tests and hand-made maps.
    public MapSetup OreRect(int x, int y, int w, int h, int resourceType)
    {
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
                Ore(x + dx, y + dy, resourceType);
        }
        return this;
    }

    // Headquarters of the player is placed with its bottom-left cell at (cellX, cellY).
    public MapSetup Start(int playerId, int cellX, int cellY)
    {
        foreach (PlayerStart s in _starts)
        {
            if (s.PlayerId == playerId)
                throw new ArgumentException($"Player {playerId} already has a start.", nameof(playerId));
        }
        _starts.Add(new PlayerStart(playerId, cellX, cellY));
        return this;
    }

    // creepCells: one cell per creep of the camp type, in the camp's creep order.
    public MapSetup CreepCamp(string campTypeId, int centerX, int centerY, params (int X, int Y)[] creepCells)
    {
        _camps.Add(new CreepCampSetup(campTypeId, centerX, centerY, creepCells));
        return this;
    }

    private int Index(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {y}) is outside the {Width}x{Height} map.");
        return y * Width + x;
    }
}
