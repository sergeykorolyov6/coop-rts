using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // rebuild-ghosts.md UI: what a destroyed building left — flat tiles in the owner's colour washed
    // out to grey, crossed out, with the arrow of a belt or bridge; unlike a silhouette of a planned
    // building (PlannedSiteView), which is the owner's colour darkened. Own and allies' ghosts, where
    // the local player has discovered the map.
    [RequireComponent(typeof(SimRunner), typeof(EntityView), typeof(LocalPlayer))]
    public sealed class GhostView : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float _washOut = 0.6f;
        [SerializeField] private Color _grey = new Color(0.55f, 0.55f, 0.58f);

        private SimRunner? _runner;
        private EntityView? _entities;
        private LocalPlayer? _localPlayer;
        private GroundMarks? _marks;
        private int _lastTick = -1, _lastPlayer;

        // The ghost lying over the cell, own or an ally's, or null.
        public static Ghost? At(SimState state, int playerId, int x, int y)
        {
            foreach (Ghost g in state.Ghosts)
            {
                if (state.Relations.IsAlly(playerId, g.OwnerId) && state.Data.TryGetBuilding(g.BuildingTypeId, out BuildingStats? stats)
                    && Game.Sim.Grid.Touches(g.CellX, g.CellY, stats!.SizeW, stats.SizeH, x, y, x, y))
                    return g;
            }
            return null;
        }

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _localPlayer = GetComponent<LocalPlayer>();
            _marks = new GroundMarks(transform, "Ghosts");
        }

        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || (simulation.State.Tick == _lastTick && _localPlayer!.PlayerId == _lastPlayer))
                return;
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            _lastTick = state.Tick;
            _lastPlayer = playerId;

            _marks!.Begin();
            foreach (Ghost g in state.Ghosts)
            {
                if (!state.Relations.IsAlly(playerId, g.OwnerId) || !state.Data.TryGetBuilding(g.BuildingTypeId, out BuildingStats? stats)
                    || !state.Fog.IsDiscovered(playerId, g.CellX, g.CellY))
                    continue;
                Color color = Color.Lerp(_entities!.OwnerColor(state, g.OwnerId), _grey, _washOut);
                for (int y = g.CellY; y < g.CellY + stats!.SizeH; y++)
                    for (int x = g.CellX; x < g.CellX + stats.SizeW; x++)
                        _marks.Tile(x, y, color, 0.8f, 0.02f, 0.015f);
                Cross(g.CellX + stats.SizeW * 0.5f, g.CellY + stats.SizeH * 0.5f, Mathf.Min(stats.SizeW, stats.SizeH), Color.Lerp(color, Color.black, 0.35f));
                if (stats.IsBelt)
                    _marks.Arrow(g.CellX, g.CellY, g.Direction, Color.Lerp(color, Color.white, 0.5f), new Vector3(0.14f, 0.04f, 0.4f), 0.05f);
            }
            _marks.End();
        }

        // Two thin bars across the footprint: "destroyed".
        private void Cross(float cx, float cz, float size, Color color)
        {
            for (int i = 0; i < 2; i++)
            {
                Transform bar = _marks!.Take(color);
                bar.localRotation = Quaternion.Euler(0f, i == 0 ? 45f : -45f, 0f);
                bar.localScale = new Vector3(0.08f, 0.03f, size * 1.1f);
                bar.localPosition = Ground.Lift(new Vector3(cx, 0.04f, cz));
            }
        }

        private void OnDestroy() => _marks?.Destroy();
    }
}
