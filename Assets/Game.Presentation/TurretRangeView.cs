using Game.Sim;
using UnityEngine;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // turret.md UI: the range on the ground around a turret being placed and around the selected
    // turret. Range counts from the footprint's edge, so the ring is the footprint grown by the
    // range: straight sides and rounded corners.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(PlacementController))]
    public sealed class TurretRangeView : MonoBehaviour
    {
        private const int CornerSegments = 10;

        [SerializeField] private Color _color = new Color(0.95f, 0.85f, 0.35f);

        private SimRunner? _runner;
        private SelectionController? _selection;
        private PlacementController? _placement;
        private GroundDashes? _dashes;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _placement = GetComponent<PlacementController>();
            _dashes = new GroundDashes(transform, "TurretRange", _color, 0.1f);
        }

        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            _dashes!.Begin();
            if (simulation != null)
            {
                SimState state = simulation.State;
                if (_placement!.Footprint is RectInt rect && _placement.BuildingId != null
                    && state.Data.TryGetBuilding(_placement.BuildingId, out BuildingStats? placing) && placing!.Damage > 0)
                {
                    Draw(state, rect, placing.AttackRangeMilli);
                }
                Building? selected = state.FindBuilding(_selection!.SelectedBuildingId);
                if (selected != null && !selected.IsDead && selected.Turret != null)
                    Draw(state, new RectInt(selected.CellX, selected.CellY, selected.Width, selected.Height), selected.Stats.AttackRangeMilli);
            }
            _dashes.End();
        }

        // heights.md: a turret reaches farther at whatever stands lower. Where there is lower ground
        // within that reach, a second, sparser ring shows it.
        private void Draw(SimState state, RectInt rect, int rangeMilli)
        {
            Ring(rect, rangeMilli, 0.6f);
            MapState map = state.Map;
            int bonus = state.Rules.HighGroundRangeBonusMilli;
            if (!map.HasLevels || bonus <= 0 || !map.InBounds(rect.xMin, rect.yMin))
                return;
            int level = map.LevelAt(rect.xMin, rect.yMin);
            int reach = (rangeMilli + bonus) / Grid.MilliPerCell + 1;
            for (int y = rect.yMin - reach; y < rect.yMax + reach; y++)
            {
                for (int x = rect.xMin - reach; x < rect.xMax + reach; x++)
                {
                    if (map.InBounds(x, y) && map.LevelAt(x, y) < level)
                    {
                        Ring(rect, rangeMilli + bonus, 0.25f);
                        return;
                    }
                }
            }
        }

        // Four straight sides plus a quarter circle at each corner, as dashes on the ground.
        private void Ring(RectInt rect, int rangeMilli, float fill)
        {
            float r = rangeMilli / (float)Grid.MilliPerCell;
            float x0 = rect.xMin, x1 = rect.xMax, z0 = rect.yMin, z1 = rect.yMax;
            _dashes!.Line(new Vector3(x0, 0f, z0 - r), new Vector3(x1, 0f, z0 - r), 1f, fill);
            _dashes.Line(new Vector3(x1 + r, 0f, z0), new Vector3(x1 + r, 0f, z1), 1f, fill);
            _dashes.Line(new Vector3(x1, 0f, z1 + r), new Vector3(x0, 0f, z1 + r), 1f, fill);
            _dashes.Line(new Vector3(x0 - r, 0f, z1), new Vector3(x0 - r, 0f, z0), 1f, fill);
            Vector3[] corners = { new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0) };
            for (int c = 0; c < 4; c++)
            {
                // Corner c sweeps from -90° + 90°·c, a quarter turn counter-clockwise.
                float start = -90f + 90f * c;
                const float step = 90f / CornerSegments;
                for (int i = 0; i < CornerSegments; i++)
                {
                    float a = (start + (i + 0.5f - fill * 0.5f) * step) * Mathf.Deg2Rad;
                    float b = (start + (i + 0.5f + fill * 0.5f) * step) * Mathf.Deg2Rad;
                    _dashes.Dash(corners[c] + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r,
                                 corners[c] + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * r);
                }
            }
        }

        private void OnDestroy()
        {
            _dashes?.Destroy();
        }
    }
}
