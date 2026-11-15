using System;
using System.Collections.Generic;

namespace Game.Sim;

// Assets/Data/maps/*.json (made by Tools/csv2json.py from Docs/Design/maps/*.txt) → MapSetup.
public static class MapLoader
{
    private static Direction ParseDirection(string text)
    {
        switch (text)
        {
            case "right": return Direction.Right;
            case "up": return Direction.Up;
            case "left": return Direction.Left;
            case "down": return Direction.Down;
            default: throw new FormatException($"Unknown direction \"{text}\".");
        }
    }

    // playerIdsBySlot[i] plays start slot i + 1; slots beyond the list stay empty
    // (a 4-player map works for 2).
    // data: terrain.csv gives the meaning of the rock characters.
    public static MapSetup Parse(string json, IReadOnlyList<int> playerIdsBySlot, GameData data)
    {
        JsonNode root = JsonParser.Parse(json);
        int width = root.GetInt("width"), height = root.GetInt("height");
        var map = new MapSetup(width, height);

        List<string> rows = root.GetStringList("rows_bottom_up");
        if (rows.Count != height)
            throw new FormatException($"Map has {rows.Count} rows, expected {height}.");
        for (int y = 0; y < height; y++)
        {
            string row = rows[y];
            if (row.Length != width)
                throw new FormatException($"Map row {y} has {row.Length} cells, expected {width}.");
            for (int x = 0; x < width; x++)
            {
                switch (row[x])
                {
                    case 'o': map.Ore(x, y, Resources.Primary); break;
                    case 's': map.Ore(x, y, Resources.Secondary); break;
                    case '.': break;
                    default:
                        if (row[x] >= '1' && row[x] <= '9')
                            break;
                        TerrainStats? terrain = data.TerrainBySymbol(row[x]);
                        if (terrain == null)
                            throw new FormatException($"Map cell ({x}, {y}) has '{row[x]}', which is neither ore, a start nor a symbol of terrain.json.");
                        if (!terrain.Passable)
                            map.Terrain(x, y, terrain);
                        break;
                }
            }
        }

        JsonNode? levels = root.Find("levels_bottom_up");
        if (levels != null)
            ParseLevels(map, levels.Items);

        foreach (JsonNode start in root.Get("starts").Items)
        {
            int slot = start.GetInt("slot");
            if (slot >= 1 && slot <= playerIdsBySlot.Count)
                map.Start(playerIdsBySlot[slot - 1], start.GetInt("x"), start.GetInt("y"));
        }

        foreach (JsonNode camp in root.Get("camps").Items)
        {
            var cells = new List<(int X, int Y)>();
            foreach (JsonNode pair in camp.Get("creeps").Items)
            {
                if (pair.Items.Count != 2)
                    throw new FormatException("A creep cell is a pair [x, y].");
                cells.Add((pair.Items[0].Integer, pair.Items[1].Integer));
            }
            map.CreepCamp(camp.GetString("type"), camp.GetInt("x"), camp.GetInt("y"), cells.ToArray());
        }

        JsonNode? neutrals = root.Find("neutral");
        if (neutrals != null)
        {
            foreach (JsonNode n in neutrals.Items)
                map.Neutral(n.GetString("building"), n.GetInt("x"), n.GetInt("y"), n.Find("guard")?.Integer ?? -1);
        }

        // legion-ai.md, T5: "legion" is the map's only base, "legions" a list of several.
        JsonNode? legion = root.Find("legion");
        if (legion != null)
            ParseBase(map, legion);
        JsonNode? legions = root.Find("legions");
        for (int i = 0; legions != null && i < legions.Items.Count; i++)
        {
            if (i > 0)
                map.LegionBase();
            ParseBase(map, legions.Items[i]);
        }
        return map;
    }

    // A digit is the cell's level, a letter (a = 0, b = 1, …) a ramp from that level up.
    private static void ParseLevels(MapSetup map, IReadOnlyList<JsonNode> rows)
    {
        if (rows.Count != map.Height)
            throw new FormatException($"Map has {rows.Count} rows of levels, expected {map.Height}.");
        for (int y = 0; y < map.Height; y++)
        {
            string row = rows[y].Text;
            if (row.Length != map.Width)
                throw new FormatException($"Levels row {y} has {row.Length} cells, expected {map.Width}.");
            for (int x = 0; x < map.Width; x++)
            {
                char c = row[x];
                if (c >= '0' && c <= '0' + MapSetup.MaxLevel)
                    map.Level(x, y, c - '0');
                else if (c >= 'a' && c < 'a' + MapSetup.MaxLevel)
                    map.Level(x, y, c - 'a', ramp: true);
                else
                    throw new FormatException($"Levels cell ({x}, {y}) has unknown '{c}'.");
            }
        }
    }

    private static void ParseBase(MapSetup map, JsonNode legionBase)
    {
        foreach (JsonNode slot in legionBase.Get("slots").Items)
            map.LegionSlot(slot.GetString("building"), slot.GetInt("x"), slot.GetInt("y"), ParseDirection(slot.GetString("dir")));
        JsonNode rally = legionBase.Get("rally");
        if (rally.Items.Count != 2)
            throw new FormatException("The Legion's rally point is a pair [x, y].");
        map.Rally(rally.Items[0].Integer, rally.Items[1].Integer);
    }
}
