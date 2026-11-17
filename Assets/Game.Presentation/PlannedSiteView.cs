using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // drill.md UI: the silhouette of a planned building stays on its place while the worker walks
    // there — flat tiles in the owner's colour, darkened; conveyor cells with their direction.
    // Every player's plans are drawn, Shift-queued ones included: allies see where help is needed.
    [RequireComponent(typeof(SimRunner), typeof(EntityView))]
    public sealed class PlannedSiteView : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float _darken = 0.45f;

        private readonly HashSet<long> _drawn = new HashSet<long>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private GroundMarks? _marks;
        private int _lastTick = -1;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _marks = new GroundMarks(transform, "PlannedSites");
        }

        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || simulation.State.Tick == _lastTick)
                return;
            SimState state = simulation.State;
            _lastTick = state.Tick;
            _marks!.Begin();
            _drawn.Clear();

            foreach (Unit unit in state.Units)
            {
                if (unit.IsDead || unit.Worker == null)
                    continue;
                foreach (PlannedBuild plan in unit.Worker.Plans)
                    Draw(state, unit, plan);
                foreach (QueuedOrder queued in unit.OrderQueue)
                    foreach (PlannedBuild plan in queued.Plans)
                        Draw(state, unit, plan);
            }

            _marks.End();
        }

        private void Draw(SimState state, Unit unit, PlannedBuild plan)
        {
            // A started site is a building already; several workers may share a plan.
            if (plan.ExistingSiteId != 0 || state.Map.BuildingAt(plan.CellX, plan.CellY) != 0
                || !_drawn.Add(((long)plan.CellX << 32) | (uint)plan.CellY)
                || !state.Data.TryGetBuilding(plan.BuildingTypeId, out BuildingStats? stats))
                return;
            Color color = Color.Lerp(_entities!.OwnerColor(state, unit.OwnerId), Color.black, _darken);
            for (int y = plan.CellY; y < plan.CellY + stats!.SizeH; y++)
                for (int x = plan.CellX; x < plan.CellX + stats.SizeW; x++)
                    _marks!.Tile(x, y, color, 0.86f, 0.03f, 0.02f);
            if (stats.IsBelt)
                _marks!.Arrow(plan.CellX, plan.CellY, plan.Direction, Color.Lerp(color, Color.white, 0.6f), new Vector3(0.16f, 0.05f, 0.42f), 0.05f);
        }

        private void OnDestroy() => _marks?.Destroy();
    }
}
