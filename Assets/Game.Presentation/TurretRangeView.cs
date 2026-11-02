using System.Collections.Generic;
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

        private readonly List<Transform> _segments = new List<Transform>();
        private SimRunner? _runner;
        private SelectionController? _selection;
        private PlacementController? _placement;
        private Transform? _root;
        private Material? _material;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _placement = GetComponent<PlacementController>();
            _root = new GameObject("TurretRange").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            int used = 0;
            if (simulation != null)
            {
                SimState state = simulation.State;
                if (_placement!.Footprint is RectInt rect && _placement.BuildingId != null
                    && state.Data.TryGetBuilding(_placement.BuildingId, out BuildingStats? placing) && placing!.Damage > 0)
                {
                    used = Draw(rect, placing.AttackRangeMilli, used);
                }
                Building? selected = state.FindBuilding(_selection!.SelectedBuildingId);
                if (selected != null && !selected.IsDead && selected.Turret != null)
                    used = Draw(new RectInt(selected.CellX, selected.CellY, selected.Width, selected.Height), selected.Stats.AttackRangeMilli, used);
            }
            for (int i = used; i < _segments.Count; i++)
                _segments[i].gameObject.SetActive(false);
        }

        // Four straight sides plus a quarter circle at each corner, as dashes on the ground.
        private int Draw(RectInt rect, int rangeMilli, int used)
        {
            float r = rangeMilli / (float)Grid.MilliPerCell;
            float x0 = rect.xMin, x1 = rect.xMax, z0 = rect.yMin, z1 = rect.yMax;
            used = Line(new Vector3(x0, 0f, z0 - r), new Vector3(x1, 0f, z0 - r), used);
            used = Line(new Vector3(x1 + r, 0f, z0), new Vector3(x1 + r, 0f, z1), used);
            used = Line(new Vector3(x1, 0f, z1 + r), new Vector3(x0, 0f, z1 + r), used);
            used = Line(new Vector3(x0 - r, 0f, z1), new Vector3(x0 - r, 0f, z0), used);
            Vector3[] corners = { new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0) };
            for (int c = 0; c < 4; c++)
            {
                // Corner c sweeps from -90° + 90°·c, a quarter turn counter-clockwise.
                float start = -90f + 90f * c;
                const float step = 90f / CornerSegments;
                for (int i = 0; i < CornerSegments; i++)
                {
                    float a = (start + (i + 0.2f) * step) * Mathf.Deg2Rad;
                    float b = (start + (i + 0.8f) * step) * Mathf.Deg2Rad;
                    used = Dash(corners[c] + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r,
                                corners[c] + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * r, used);
                }
            }
            return used;
        }

        private int Line(Vector3 from, Vector3 to, int used)
        {
            int dashes = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to)));
            for (int i = 0; i < dashes; i++)
                used = Dash(Vector3.Lerp(from, to, (i + 0.2f) / dashes), Vector3.Lerp(from, to, (i + 0.8f) / dashes), used);
            return used;
        }

        private int Dash(Vector3 from, Vector3 to, int used)
        {
            if (used == _segments.Count)
                _segments.Add(CreateSegment());
            Transform t = _segments[used];
            t.gameObject.SetActive(true);
            Vector3 mid = (from + to) * 0.5f;
            t.position = new Vector3(mid.x, 0.06f, mid.z);
            Vector3 along = to - from;
            t.rotation = Quaternion.LookRotation(along.sqrMagnitude > 0f ? along : Vector3.forward);
            t.localScale = new Vector3(0.1f, 0.03f, along.magnitude);
            return used + 1;
        }

        private Transform CreateSegment()
        {
            GameObject go = Primitives.Create(PrimitiveType.Cube);
            go.name = "Range";
            Destroy(go.GetComponent<Collider>());
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            _material ??= new Material(renderer.sharedMaterial) { color = _color };
            renderer.sharedMaterial = _material;
            go.transform.SetParent(_root, worldPositionStays: false);
            return go.transform;
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
