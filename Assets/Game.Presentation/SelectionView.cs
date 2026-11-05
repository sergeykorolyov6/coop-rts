using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Placeholder selection marker until there is art: a flat disc under each selected unit,
    // a plate one cell wider than the footprint under a selected building; red under someone
    // else's unit or building picked for a look.
    [RequireComponent(typeof(SimRunner), typeof(EntityView), typeof(SelectionController))]
    [RequireComponent(typeof(LocalPlayer))]
    public sealed class SelectionView : MonoBehaviour
    {
        [SerializeField] private Color _color = new Color(0.4f, 1f, 0.4f);
        [Tooltip("Under someone else's unit picked for a look.")]
        [SerializeField] private Color _inspectColor = new Color(1f, 0.35f, 0.3f);
        [Tooltip("Disc diameter relative to the unit size.")]
        [SerializeField] private float _scale = 1.5f;

        private readonly List<Transform> _discs = new List<Transform>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private Material? _material;
        private Material? _inspectMaterial;
        private Transform? _root;
        private Transform? _plate;
        private Transform? _inspectDisc;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
            _root = new GameObject("Selection").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        // After EntityView.LateUpdate has placed the units for this frame.
        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            IReadOnlyList<int> selected = _selection!.Selected;
            int shown = 0;
            if (simulation != null)
            {
                foreach (int id in selected)
                {
                    Unit? unit = simulation.State.FindUnit(id);
                    if (unit == null || !_entities!.TryGetUnitPosition(id, out Vector3 position))
                        continue;
                    Transform disc = Disc(shown++);
                    float size = unit.Stats.SizeMilli / (float)Grid.MilliPerCell * _scale;
                    disc.localScale = new Vector3(size, 0.01f, size);
                    disc.position = position + Vector3.up * 0.02f;
                    disc.gameObject.SetActive(true);
                }
            }
            for (int i = shown; i < _discs.Count; i++)
                _discs[i].gameObject.SetActive(false);

            Building? building = simulation?.State.FindBuilding(_selection.SelectedBuildingId);
            if (building != null)
            {
                _plate ??= CreateMarker(PrimitiveType.Cube, "SelectionPlate");
                _plate.localScale = new Vector3(building.Width + 0.3f, 0.01f, building.Height + 0.3f);
                _plate.position = new Vector3(building.CellX + building.Width * 0.5f, 0.02f, building.CellY + building.Height * 0.5f);
                _plate.GetComponent<MeshRenderer>().sharedMaterial = building.OwnerId == _localPlayer!.PlayerId ? _material : InspectMaterial(_plate);
            }
            _plate?.gameObject.SetActive(building != null);

            Unit? inspected = simulation?.State.FindUnit(_selection.InspectedUnitId);
            Vector3 at = default;
            bool showInspected = inspected != null && _entities!.TryGetUnitPosition(inspected.Id, out at);
            if (showInspected)
            {
                if (_inspectDisc == null)
                {
                    _inspectDisc = CreateMarker(PrimitiveType.Cylinder, "InspectDisc");
                    _inspectDisc.GetComponent<MeshRenderer>().sharedMaterial = InspectMaterial(_inspectDisc);
                }
                float size = inspected!.Stats.SizeMilli / (float)Grid.MilliPerCell * _scale;
                _inspectDisc.localScale = new Vector3(size, 0.01f, size);
                _inspectDisc.position = at + Vector3.up * 0.02f;
            }
            _inspectDisc?.gameObject.SetActive(showInspected);
        }

        private Material InspectMaterial(Transform marker) =>
            _inspectMaterial ??= new Material(marker.GetComponent<MeshRenderer>().sharedMaterial) { color = _inspectColor };

        // Discs are pooled: selection changes every few seconds, no objects churn.
        private Transform Disc(int index)
        {
            if (index < _discs.Count)
                return _discs[index];
            Transform disc = CreateMarker(PrimitiveType.Cylinder, "SelectionDisc");
            _discs.Add(disc);
            return disc;
        }

        private Transform CreateMarker(PrimitiveType type, string name)
        {
            GameObject go = Primitives.Create(type);
            go.name = name;
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
            if (_inspectMaterial != null)
                Destroy(_inspectMaterial);
        }
    }
}
