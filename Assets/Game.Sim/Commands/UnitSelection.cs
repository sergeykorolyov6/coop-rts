using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// The selected units a command applies to: written as a count and ids,
// resolved at execution time to the player's own living units, ascending, no repeats.
internal static class UnitSelection
{
    public const int MaxUnits = 256;

    public static void Write(BinaryWriter writer, int[] unitIds)
    {
        writer.Write(unitIds.Length);
        foreach (int id in unitIds)
            writer.Write(id);
    }

    public static int[] Read(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MaxUnits)
            throw new InvalidDataException($"Selection of {count} units.");
        var ids = new int[count];
        for (int i = 0; i < count; i++)
            ids[i] = reader.ReadInt32();
        return ids;
    }

    // Units of someone else, dead or unknown ids are skipped silently: the selection was
    // made on the issuing machine some ticks ago, and units may have died since.
    public static List<Unit> Resolve(SimState state, int playerId, int[] unitIds)
    {
        var sorted = new List<int>(unitIds);
        sorted.Sort();
        var result = new List<Unit>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            if (i > 0 && sorted[i] == sorted[i - 1])
                continue;
            Unit? unit = state.FindUnit(sorted[i]);
            if (unit != null && !unit.IsDead && unit.OwnerId == playerId)
                result.Add(unit);
        }
        return result;
    }
}
