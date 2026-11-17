using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // heights.md: how high the ground is at a point of the world. Levels are steps of LevelHeight,
    // a ramp is a slope between its level and the next. Everything that stands or is drawn on the
    // ground (units, buildings, marks, tiles) asks here; the ground mesh (MapView) is built from
    // the same corner heights, so things sit exactly on it.
    public static class Ground
    {
        public const float LevelHeight = 0.8f;

        // The map of the running match; set by SimRunner, null in the menu.
        public static MapState? Map { get; set; }

        // Height of the cell's corner (ox, oy ∈ {0, 1}) as this cell has it. A plain cell is flat.
        // A ramp's corner is up when a cell sharing that corner stands higher than the ramp's level.
        public static float CornerHeight(MapState map, int cx, int cy, int ox, int oy)
        {
            int level = map.LevelAt(cx, cy);
            if (!map.IsRamp(cx, cy))
                return level * LevelHeight;
            for (int dy = oy - 1; dy <= oy; dy++)
            {
                for (int dx = ox - 1; dx <= ox; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if ((dx != 0 || dy != 0) && map.InBounds(nx, ny) && map.LevelAt(nx, ny) > level)
                        return (level + 1) * LevelHeight;
                }
            }
            return level * LevelHeight;
        }

        // World height of the ground under the point (x, z).
        public static float Y(float x, float z)
        {
            MapState? map = Map;
            if (map == null || !map.HasLevels)
                return 0f;
            int cx = Mathf.Clamp(Mathf.FloorToInt(x), 0, map.Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(z), 0, map.Height - 1);
            if (!map.IsRamp(cx, cy))
                return map.LevelAt(cx, cy) * LevelHeight;
            float fx = Mathf.Clamp01(x - cx), fz = Mathf.Clamp01(z - cy);
            float low = Mathf.Lerp(CornerHeight(map, cx, cy, 0, 0), CornerHeight(map, cx, cy, 1, 0), fx);
            float high = Mathf.Lerp(CornerHeight(map, cx, cy, 0, 1), CornerHeight(map, cx, cy, 1, 1), fx);
            return Mathf.Lerp(low, high, fz);
        }

        // Sim positions are milli-cells; a cell is one unit of the world.
        public static float Cells(int milli) => milli / (float)Game.Sim.Grid.MilliPerCell;

        public static int Milli(float cells) => Mathf.RoundToInt(cells * Game.Sim.Grid.MilliPerCell);

        // A sim point on the ground, `above` it.
        public static Vector3 At(int milliX, int milliY, float above = 0f) => Lift(Flat(milliX, milliY, above));

        // A sim point at a fixed height, not lifted: for what follows the ground itself (GroundDashes).
        public static Vector3 Flat(int milliX, int milliY, float y = 0f) => new Vector3(Cells(milliX), y, Cells(milliY));

        // Which way a belt or bridge looks: Direction.Up is +Y in cells, which is +Z in the world.
        public static Quaternion Facing(Direction direction) => direction switch
        {
            Direction.Right => Quaternion.Euler(0f, 90f, 0f),
            Direction.Up => Quaternion.identity,
            Direction.Left => Quaternion.Euler(0f, 270f, 0f),
            _ => Quaternion.Euler(0f, 180f, 0f),
        };

        // The point lifted onto the ground: its own y becomes the height above the ground.
        public static Vector3 Lift(Vector3 point)
        {
            point.y += Y(point.x, point.z);
            return point;
        }
    }
}
