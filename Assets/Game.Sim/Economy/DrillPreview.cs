namespace Game.Sim;

// What the player sees before placing a drill: how many ore cells it would mine (drill.md, edge cases).
public static class DrillPreview
{
    public static int OreCellsUnder(MapState map, int cellX, int cellY) => Count(map, cellX, cellY, 2, 2, out _);

    // The resource with most ore cells in the footprint and their count.
    internal static int Count(MapState map, int x, int y, int w, int h, out int resourceType)
    {
        int primary = 0, secondary = 0;
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (!map.InBounds(cx, cy))
                    continue;
                int ore = map.OreAt(cx, cy);
                if (ore == Resources.Primary)
                    primary++;
                else if (ore == Resources.Secondary)
                    secondary++;
            }
        }
        resourceType = secondary > primary ? Resources.Secondary : Resources.Primary;
        return secondary > primary ? secondary : primary;
    }
}
