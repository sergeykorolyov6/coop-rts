using System;
using System.Collections.Generic;

namespace Game.Sim;

// Assets/Data/missions.json (from Docs/Design/missions.csv): the missions the menu offers.
public sealed class MissionCatalog
{
    private readonly List<MissionStats> _missions;

    private MissionCatalog(List<MissionStats> missions)
    {
        _missions = missions;
    }

    // In file order (sorted by id by the converter).
    public IReadOnlyList<MissionStats> Missions => _missions;

    public static MissionCatalog Parse(string json)
    {
        JsonNode root;
        try
        {
            root = JsonParser.Parse(json);
        }
        catch (FormatException e)
        {
            throw new FormatException($"missions.json: {e.Message}", e);
        }
        if (root.Kind != JsonKind.Array)
            throw new FormatException("missions.json: expected a list of rows.");
        var list = new List<MissionStats>(root.Items.Count);
        foreach (JsonNode row in root.Items)
        {
            var m = new MissionStats(row);
            if (list.Exists(x => x.Id == m.Id))
                throw new FormatException($"missions.json: duplicate id \"{m.Id}\".");
            list.Add(m);
        }
        return new MissionCatalog(list);
    }

    public MissionStats? Find(string id)
    {
        foreach (MissionStats m in _missions)
        {
            if (m.Id == id)
                return m;
        }
        return null;
    }
}
