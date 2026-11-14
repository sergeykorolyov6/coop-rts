namespace Game.Sim;

// One row of terrain.csv (plan-terrain-and-factions.md, 3.2): a kind of ground or rock.
// A map cell names its kind by the symbol. What digging does to rock comes with mechanics/terrain.md.
public sealed class TerrainStats
{
    internal TerrainStats(JsonNode row)
    {
        Id = row.GetString("id");
        string symbol = row.GetString("symbol");
        if (symbol.Length != 1)
            throw new System.FormatException($"terrain \"{Id}\": symbol must be one character, got \"{symbol}\".");
        Symbol = symbol[0];
        Passable = row.GetInt("passable") != 0;
        Destructible = row.GetInt("destructible") != 0;
        if (Passable && Destructible)
            throw new System.FormatException($"terrain \"{Id}\": passable ground cannot be destructible.");
    }

    public string Id { get; }

    // The character of this terrain in a map's rows.
    public char Symbol { get; }

    // Units walk here and buildings stand here.
    public bool Passable { get; }

    // A digger can break it (stage T4); until then it is as solid as any rock.
    public bool Destructible { get; }
}
