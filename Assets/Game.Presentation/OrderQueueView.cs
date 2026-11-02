using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // order-queue.md UI: for the selected units, where their current order and the Shift-queued
    // ones lead — a marker per point (colour by kind) and a line through them in order, as in SC2.
    [RequireComponent(typeof(SimRunner), typeof(EntityView), typeof(SelectionController))]
    public sealed class OrderQueueView : MonoBehaviour
    {
        [SerializeField] private Color _moveColor = new Color(0.40f, 0.95f, 0.45f);
        [SerializeField] private Color _attackColor = new Color(1.00f, 0.30f, 0.25f);
        [SerializeField] private Color _workColor = new Color(0.98f, 0.80f, 0.30f);

        private readonly List<Transform> _markers = new List<Transform>();
        private readonly List<Transform> _segments = new List<Transform>();
        private readonly List<(Vector3 Point, Color Color)> _points = new List<(Vector3, Color)>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private SelectionController? _selection;
        private Transform? _root;
        private Material? _baseMaterial;
        private int _markersUsed, _segmentsUsed;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _selection = GetComponent<SelectionController>();
            _root = new GameObject("OrderQueue").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        // After EntityView has placed the units for this frame.
        private void LateUpdate()
        {
            _markersUsed = _segmentsUsed = 0;
            Simulation? simulation = _runner!.Simulation;
            if (simulation != null)
            {
                foreach (int id in _selection!.Selected)
                {
                    Unit? unit = simulation.State.FindUnit(id);
                    if (unit == null || !_entities!.TryGetUnitPosition(id, out Vector3 from))
                        continue;
                    _points.Clear();
                    AddOrder(simulation.State, unit.Order, unit.Worker?.Plans);
                    foreach (QueuedOrder queued in unit.OrderQueue)
                        AddOrder(simulation.State, queued.Order, queued.Plans);

                    foreach ((Vector3 point, Color color) in _points)
                    {
                        Segment(from, point, color);
                        Marker(point, color);
                        from = point;
                    }
                }
            }
            for (int i = _markersUsed; i < _markers.Count; i++)
                _markers[i].gameObject.SetActive(false);
            for (int i = _segmentsUsed; i < _segments.Count; i++)
                _segments[i].gameObject.SetActive(false);
        }

        private void AddOrder(SimState state, UnitOrder order, IReadOnlyList<PlannedBuild>? plans)
        {
            switch (order.Kind)
            {
                case UnitOrderKind.Move:
                    _points.Add((ToWorld(order.X, order.Y), _moveColor));
                    break;
                case UnitOrderKind.Attack:
                    if (_entities!.TryGetEntity(order.TargetId, out Vector3 target, out _, out _))
                        _points.Add((new Vector3(target.x, 0f, target.z), _attackColor));
                    break;
                case UnitOrderKind.Mine:
                    _points.Add((new Vector3(order.X + 0.5f, 0f, order.Y + 0.5f), _workColor));
                    break;
                case UnitOrderKind.Repair:
                    Building? building = state.FindBuilding(order.TargetId);
                    if (building != null)
                        _points.Add((new Vector3(building.CellX + building.Width * 0.5f, 0f, building.CellY + building.Height * 0.5f), _workColor));
                    break;
                case UnitOrderKind.CarryTrophy:
                    CreepCamp camp = state.CreepCamps[order.TargetId];
                    _points.Add((ToWorld(camp.CenterMilliX, camp.CenterMilliY), _workColor));
                    break;
                case UnitOrderKind.Build when plans != null:
                    foreach (PlannedBuild plan in plans)
                    {
                        if (state.Data.TryGetBuilding(plan.BuildingTypeId, out BuildingStats? stats))
                            _points.Add((new Vector3(plan.CellX + stats!.SizeW * 0.5f, 0f, plan.CellY + stats.SizeH * 0.5f), _workColor));
                    }
                    break;
            }
        }

        private void Marker(Vector3 point, Color color)
        {
            Transform marker = Take(_markers, ref _markersUsed, PrimitiveType.Cylinder, color);
            marker.position = point + Vector3.up * 0.04f;
            marker.localScale = new Vector3(0.35f, 0.02f, 0.35f);
            marker.rotation = Quaternion.identity;
        }

        private void Segment(Vector3 from, Vector3 to, Color color)
        {
            from.y = to.y = 0.035f;
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.01f)
                return;
            Transform segment = Take(_segments, ref _segmentsUsed, PrimitiveType.Cube, Color.Lerp(color, Color.black, 0.25f));
            segment.position = (from + to) * 0.5f;
            segment.rotation = Quaternion.LookRotation(delta);
            segment.localScale = new Vector3(0.05f, 0.01f, delta.magnitude);
        }

        private Transform Take(List<Transform> pool, ref int used, PrimitiveType type, Color color)
        {
            Transform item;
            if (used < pool.Count)
            {
                item = pool[used];
                item.gameObject.SetActive(true);
            }
            else
            {
                GameObject go = GameObject.CreatePrimitive(type);
                go.name = type == PrimitiveType.Cube ? "QueueLine" : "QueuePoint";
                Destroy(go.GetComponent<Collider>());
                _baseMaterial ??= go.GetComponent<MeshRenderer>().sharedMaterial;
                go.transform.SetParent(_root, worldPositionStays: false);
                item = go.transform;
                pool.Add(item);
            }
            used++;
            item.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            return item;
        }

        private Material MaterialFor(Color color)
        {
            if (!_materials.TryGetValue(color, out Material? material))
            {
                material = new Material(_baseMaterial) { color = color };
                _materials.Add(color, material);
            }
            return material;
        }

        private static Vector3 ToWorld(int milliX, int milliY) =>
            new Vector3(milliX / (float)Grid.MilliPerCell, 0f, milliY / (float)Grid.MilliPerCell);

        private void OnDestroy()
        {
            foreach (Material material in _materials.Values)
                Destroy(material);
        }
    }
}
