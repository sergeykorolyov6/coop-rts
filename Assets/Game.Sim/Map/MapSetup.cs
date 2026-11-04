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
    private readonly List<BaseSlotSetup> _legionSlots = new List<BaseSlotSetup>();
    private readonly List<NeutralBuildingSetup> _neutrals = new List<NeutralBuildingSetup>();

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

    // neutral-buildings.md: neutral drills, caves, lighthouses standing from the start.
    public IReadOnlyList<NeutralBuildingSetup> Neutrals => _neutrals;

    // legion-ai.md: the Legion's base in rebuild order, and where its army gathers.
    public IReadOnlyList<BaseSlotSetup> LegionSlots => _legionSlots;
    public bool HasRally { get; private set; }
    public int RallyX { get; private set; }
    public int RallyY { get; private set; }

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

    public MapSetup LegionSlot(string buildingTypeId, int cellX, int cellY, Direction direction = Direction.Right)
    {
        Index(cellX, cellY);
        _legionSlots.Add(new BaseSlotSetup(buildingTypeId, cellX, cellY, direction));
        return this;
    }

    // A straight conveyor line from (x1, y1) to (x2, y2), every cell facing the end.
    public MapSetup LegionLine(int x1, int y1, int x2, int y2)
    {
        if ((x1 != x2) == (y1 != y2))
            throw new ArgumentException("A conveyor line goes straight and has at least two cells.");
        int dx = System.Math.Sign(x2 - x1), dy = System.Math.Sign(y2 - y1);
        Direction d = dx > 0 ? Direction.Right : dx < 0 ? Direction.Left : dy > 0 ? Direction.Up : Direction.Down;
        int n = System.Math.Max(System.Math.Abs(x2 - x1), System.Math.Abs(y2 - y1)) + 1;
        for (int i = 0; i < n; i++)
            LegionSlot(BuildingKinds.Conveyor, x1 + dx * i, y1 + dy * i, d);
        return this;
    }

    // guardCampIndex: the camp (index in CreepCamps) that must be cleared before capture, -1 for none.
    public MapSetup Neutral(string buildingTypeId, int cellX, int cellY, int guardCampIndex = -1)
    {
        Index(cellX, cellY);
        _neutrals.Add(new NeutralBuildingSetup(buildingTypeId, cellX, cellY, guardCampIndex));
        return this;
    }

    public MapSetup Rally(int cellX, int cellY)
    {
        Index(cellX, cellY);
        HasRally = true;
        RallyX = cellX;
        RallyY = cellY;
        return this;
    }

    private int Index(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {y}) is outside the {Width}x{Height} map.");
        return y * Width + x;
    }
}
