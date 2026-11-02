using System;
using System.Collections.Generic;

namespace Game.Sim;

// Assets/Data/maps/*.json (made by Tools/csv2json.py from Docs/Design/maps/*.txt) → MapSetup.
public static class MapLoader
{
    // playerIdsBySlot[i] plays start slot i + 1; slots beyond the list stay empty
    // (a 4-player map works for 2).
    public static MapSetup Parse(string json, IReadOnlyList<int> playerIdsBySlot)
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
                    case '#': map.Block(x, y); break;
                    case 'o': map.Ore(x, y, Resources.Primary); break;
                    case 's': map.Ore(x, y, Resources.Secondary); break;
                    case '.': break;
                    default:
                        if (row[x] < '1' || row[x] > '9')
                            throw new FormatException($"Map cell ({x}, {y}) has unknown '{row[x]}'.");
                        break;
                }
            }
        }

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
        return map;
    }
}
