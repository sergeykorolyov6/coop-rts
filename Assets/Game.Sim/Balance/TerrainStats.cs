namespace Game.Sim;

// One row of terrain.csv (plan-terrain-and-factions.md, 3.2): a kind of ground or rock.
// A map cell names its kind by the symbol. What digging does to rock comes with mechanics/terrain.md.
public sealed class TerrainStats
{
    internal TerrainStats(JsonNode row)
    {
        Id = row.GetString("id");
        NameRu = row.GetString("name_ru");
        string symbol = row.GetString("symbol");
        if (symbol.Length != 1)
            throw new System.FormatException($"terrain \"{Id}\": symbol must be one character, got \"{symbol}\".");
        Symbol = symbol[0];
        Passable = row.GetInt("passable") != 0;
        Destructible = row.GetInt("destructible") != 0;
        Hp = row.GetInt("hp");
        string ore = row.GetString("ore");
        OreType = ore == "primary" ? Resources.Primary : ore == "secondary" ? Resources.Secondary : MapSetup.NoOre;
        if (ore.Length > 0 && OreType == MapSetup.NoOre)
            throw new System.FormatException($"terrain \"{Id}\": ore is \"{ore}\", expected primary, secondary or nothing.");
        if (Passable && Destructible)
            throw new System.FormatException($"terrain \"{Id}\": passable ground cannot be destructible.");
        if (Destructible != (Hp > 0))
            throw new System.FormatException($"terrain \"{Id}\": destructible rock has hp, nothing else does (hp {Hp}).");
        if (!Destructible && OreType != MapSetup.NoOre)
            throw new System.FormatException($"terrain \"{Id}\": only destructible rock has ore inside.");
    }

    public string Id { get; }

    // For the UI only.
    public string NameRu { get; }

    // The character of this terrain in a map's rows.
    public char Symbol { get; }

    // Units walk here and buildings stand here.
    public bool Passable { get; }

    // terrain.md: a digger breaks it; Hp is how much digging that takes.
    public bool Destructible { get; }
    public int Hp { get; }

    // The ore the cell holds once dug out, MapSetup.NoOre for none.
    public int OreType { get; }
}
