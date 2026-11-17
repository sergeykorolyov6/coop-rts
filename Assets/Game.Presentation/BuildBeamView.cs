using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // builder-beam.md UI: a beam from a builder to the building it builds or repairs from build_range,
    // so a builder standing four cells off is seen at work. Drawn for every builder the local
    // player sees; the beam is in the owner's colour, lightened.
    [RequireComponent(typeof(SimRunner), typeof(EntityView), typeof(LocalPlayer))]
    public sealed class BuildBeamView : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float _lighten = 0.45f;
        [SerializeField] private float _width = 0.06f;
        [Tooltip("Height of the beam's start over the builder's feet, cells.")]
        [SerializeField] private float _handHeight = 0.6f;

        private readonly List<Transform> _beams = new List<Transform>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private LocalPlayer? _localPlayer;
        private Transform? _root;
        private Material? _baseMaterial;
        private int _used;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _localPlayer = GetComponent<LocalPlayer>();
            _root = new GameObject("BuildBeams").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        // After EntityView has placed the units for this frame.
        private void LateUpdate()
        {
            _used = 0;
            Simulation? simulation = _runner!.Simulation;
            if (simulation != null)
            {
                SimState state = simulation.State;
                int playerId = _localPlayer!.PlayerId;
                foreach (Unit unit in state.Units)
                {
                    if (unit.IsDead || unit.Worker == null || !Visibility.UnitShown(state, playerId, unit))
                        continue;
                    Building? target = Target(state, unit);
                    if (target != null && _entities!.TryGetUnitPosition(unit.Id, out Vector3 from)
                        && _entities.TryGetEntity(target.Id, out Vector3 to, out _, out _))
                        Beam(from + Vector3.up * _handHeight, to, _entities.OwnerColor(state, unit.OwnerId));
                }
            }
            for (int i = _used; i < _beams.Count; i++)
                _beams[i].gameObject.SetActive(false);
        }

        // The site the builder works on now, or the building it repairs, when it is within build range.
        private static Building? Target(SimState state, Unit unit)
        {
            Building? target = null;
            if (unit.Order.Kind == UnitOrderKind.Build && !unit.Worker!.WaitingForResources)
            {
                PlannedBuild? plan = unit.Worker.CurrentPlan;
                if (plan != null && plan.ExistingSiteId != 0)
                    target = state.FindBuilding(plan.ExistingSiteId);
                if (target != null && target.IsComplete)
                    target = null;
            }
            else if (unit.Order.Kind == UnitOrderKind.Repair)
            {
                target = state.FindBuilding(unit.Order.TargetId);
            }
            if (target == null || target.IsDead)
                return null;
            // The sim's own measure: from the unit's centre to the nearest point of the footprint.
            float dx = Mathf.Max(0f, Mathf.Max(target.CellX - Ground.Cells(unit.X), Ground.Cells(unit.X) - (target.CellX + target.Width)));
            float dy = Mathf.Max(0f, Mathf.Max(target.CellY - Ground.Cells(unit.Y), Ground.Cells(unit.Y) - (target.CellY + target.Height)));
            return dx * dx + dy * dy <= Mathf.Pow(Ground.Cells(state.Rules.BuildRangeMilli), 2) ? target : null;
        }

        private void Beam(Vector3 from, Vector3 to, Color owner)
        {
            Transform beam = Pooled();
            Vector3 along = to - from;
            beam.position = (from + to) * 0.5f;
            beam.rotation = Quaternion.LookRotation(along.sqrMagnitude > 0.0001f ? along : Vector3.forward);
            beam.localScale = new Vector3(_width, _width, along.magnitude);
            beam.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(Color.Lerp(owner, Color.white, _lighten));
        }

        private Transform Pooled()
        {
            if (_used < _beams.Count)
            {
                Transform existing = _beams[_used++];
                existing.gameObject.SetActive(true);
                return existing;
            }
            GameObject go = Primitives.Create(PrimitiveType.Cube);
            go.name = "BuildBeam";
            Destroy(go.GetComponent<Collider>());
            _baseMaterial ??= go.GetComponent<MeshRenderer>().sharedMaterial;
            go.transform.SetParent(_root, worldPositionStays: false);
            _beams.Add(go.transform);
            _used++;
            return go.transform;
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

        private void OnDestroy()
        {
            foreach (Material material in _materials.Values)
                Destroy(material);
        }
    }
}
