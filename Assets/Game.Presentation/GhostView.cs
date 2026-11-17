using System.Collections.Generic;
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

        private readonly List<Transform> _tiles = new List<Transform>();
        private readonly List<Transform> _marks = new List<Transform>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private LocalPlayer? _localPlayer;
        private Transform? _root;
        private Material? _baseMaterial;
        private int _lastTick = -1, _lastPlayer;
        private int _tilesUsed, _marksUsed;

        // The ghost lying over the cell, own or an ally's, or null.
        public static Ghost? At(SimState state, int playerId, int x, int y)
        {
            foreach (Ghost g in state.Ghosts)
            {
                if (!state.Relations.IsAlly(playerId, g.OwnerId) || !state.Data.TryGetBuilding(g.BuildingTypeId, out BuildingStats? stats))
                    continue;
                if (x >= g.CellX && x < g.CellX + stats!.SizeW && y >= g.CellY && y < g.CellY + stats.SizeH)
                    return g;
            }
            return null;
        }

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _localPlayer = GetComponent<LocalPlayer>();
            _root = new GameObject("Ghosts").transform;
            _root.SetParent(transform, worldPositionStays: false);
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
            _tilesUsed = _marksUsed = 0;

            foreach (Ghost g in state.Ghosts)
            {
                if (!state.Relations.IsAlly(playerId, g.OwnerId) || !state.Data.TryGetBuilding(g.BuildingTypeId, out BuildingStats? stats)
                    || !state.Fog.IsDiscovered(playerId, g.CellX, g.CellY))
                    continue;
                Color color = Color.Lerp(_entities!.OwnerColor(state, g.OwnerId), _grey, _washOut);
                for (int y = g.CellY; y < g.CellY + stats!.SizeH; y++)
                    for (int x = g.CellX; x < g.CellX + stats.SizeW; x++)
                        Tile(x, y, color);
                Cross(g.CellX + stats.SizeW * 0.5f, g.CellY + stats.SizeH * 0.5f, Mathf.Min(stats.SizeW, stats.SizeH), Color.Lerp(color, Color.black, 0.35f));
                if (stats.IsBelt)
                    Arrow(g.CellX, g.CellY, g.Direction, Color.Lerp(color, Color.white, 0.5f));
            }

            for (int i = _tilesUsed; i < _tiles.Count; i++)
                _tiles[i].gameObject.SetActive(false);
            for (int i = _marksUsed; i < _marks.Count; i++)
                _marks[i].gameObject.SetActive(false);
        }

        private void Tile(int x, int y, Color color)
        {
            Transform tile = Pooled(_tiles, ref _tilesUsed, "GhostTile");
            tile.localScale = new Vector3(0.8f, 0.02f, 0.8f);
            tile.localRotation = Quaternion.identity;
            tile.localPosition = Ground.Lift(new Vector3(x + 0.5f, 0.015f, y + 0.5f));
            tile.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
        }

        // Two thin bars across the footprint: "destroyed".
        private void Cross(float cx, float cz, float size, Color color)
        {
            for (int i = 0; i < 2; i++)
            {
                Transform bar = Pooled(_marks, ref _marksUsed, "GhostCross");
                bar.localRotation = Quaternion.Euler(0f, i == 0 ? 45f : -45f, 0f);
                bar.localScale = new Vector3(0.08f, 0.03f, size * 1.1f);
                bar.localPosition = Ground.Lift(new Vector3(cx, 0.04f, cz));
                bar.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            }
        }

        private void Arrow(int x, int y, Direction direction, Color color)
        {
            Transform arrow = Pooled(_marks, ref _marksUsed, "GhostArrow");
            float yaw = direction switch { Direction.Right => 90f, Direction.Up => 0f, Direction.Left => 270f, _ => 180f };
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            arrow.localRotation = rotation;
            arrow.localScale = new Vector3(0.14f, 0.04f, 0.4f);
            arrow.localPosition = Ground.Lift(new Vector3(x + 0.5f, 0.05f, y + 0.5f)) + rotation * new Vector3(0f, 0f, 0.16f);
            arrow.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
        }

        private Transform Pooled(List<Transform> pool, ref int used, string name)
        {
            if (used < pool.Count)
            {
                Transform existing = pool[used++];
                existing.gameObject.SetActive(true);
                existing.name = name;
                return existing;
            }
            GameObject go = Primitives.Create(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            _baseMaterial ??= go.GetComponent<MeshRenderer>().sharedMaterial;
            go.transform.SetParent(_root, worldPositionStays: false);
            pool.Add(go.transform);
            used++;
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
