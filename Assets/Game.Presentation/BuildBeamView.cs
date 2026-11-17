using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // builder-beam.md UI: a beam from a builder or repairer to the building it builds, rebuilds or
    // repairs from build_range, so one standing four cells off is seen at work. Drawn for every such
    // unit the local player sees; the beam is in the owner's colour, lightened.
    [RequireComponent(typeof(SimRunner), typeof(EntityView), typeof(LocalPlayer))]
    public sealed class BuildBeamView : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float _lighten = 0.45f;
        [SerializeField] private float _width = 0.06f;
        [Tooltip("Height of the beam's start over the unit's feet, cells.")]
        [SerializeField] private float _handHeight = 0.6f;

        private SimRunner? _runner;
        private EntityView? _entities;
        private LocalPlayer? _localPlayer;
        private GroundMarks? _beams;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _localPlayer = GetComponent<LocalPlayer>();
            _beams = new GroundMarks(transform, "BuildBeams");
        }

        // After EntityView has placed the units for this frame.
        private void LateUpdate()
        {
            _beams!.Begin();
            Simulation? simulation = _runner!.Simulation;
            if (simulation != null)
            {
                SimState state = simulation.State;
                int playerId = _localPlayer!.PlayerId;
                foreach (Unit unit in state.Units)
                {
                    if (unit.IsDead || !Visibility.UnitShown(state, playerId, unit))
                        continue;
                    Building? target = Target(state, unit);
                    if (target != null && _entities!.TryGetUnitPosition(unit.Id, out Vector3 from)
                        && _entities.TryGetEntity(target.Id, out Vector3 to, out _, out _))
                        Beam(from + Vector3.up * _handHeight, to, Color.Lerp(_entities.OwnerColor(state, unit.OwnerId), Color.white, _lighten));
                }
            }
            _beams.End();
        }

        // The building the unit works on from where it stands: the site of its current silhouette, the
        // ghost a repairer rebuilds, or the building it repairs — once the sim says it is in reach.
        private static Building? Target(SimState state, Unit unit)
        {
            Building? target = unit.Order.Kind switch
            {
                UnitOrderKind.Build => Unfinished(state.FindBuilding(unit.Worker?.CurrentPlan?.ExistingSiteId ?? 0)),
                UnitOrderKind.Rebuild => Unfinished(state.BuildingAt(unit.Order.X, unit.Order.Y)),
                UnitOrderKind.Repair => state.FindBuilding(unit.Order.TargetId),
                _ => null,
            };
            return target != null && !target.IsDead && PlacementPreview.InBuildRange(state, unit, target) ? target : null;
        }

        private static Building? Unfinished(Building? site) => site != null && !site.IsComplete ? site : null;

        private void Beam(Vector3 from, Vector3 to, Color color)
        {
            Transform beam = _beams!.Take(color);
            Vector3 along = to - from;
            beam.position = (from + to) * 0.5f;
            beam.rotation = Quaternion.LookRotation(along.sqrMagnitude > 0.0001f ? along : Vector3.forward);
            beam.localScale = new Vector3(_width, _width, along.magnitude);
        }

        private void OnDestroy() => _beams?.Destroy();
    }
}
