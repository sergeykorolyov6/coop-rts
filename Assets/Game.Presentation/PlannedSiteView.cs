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

        private readonly List<Transform> _tiles = new List<Transform>();
        private readonly List<Transform> _arrows = new List<Transform>();
        private readonly HashSet<long> _drawn = new HashSet<long>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private Transform? _root;
        private Material? _baseMaterial;
        private int _lastTick = -1;
        private int _tilesUsed, _arrowsUsed;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _root = new GameObject("PlannedSites").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || simulation.State.Tick == _lastTick)
                return;
            SimState state = simulation.State;
            _lastTick = state.Tick;
            _tilesUsed = _arrowsUsed = 0;
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

            for (int i = _tilesUsed; i < _tiles.Count; i++)
                _tiles[i].gameObject.SetActive(false);
            for (int i = _arrowsUsed; i < _arrows.Count; i++)
                _arrows[i].gameObject.SetActive(false);
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
                    Tile(x, y, color);
            if (stats.Id == BuildingKinds.Conveyor || stats.Id == BuildingKinds.ConveyorBridge)
                Arrow(plan.CellX, plan.CellY, plan.Direction, color);
        }

        private void Tile(int x, int y, Color color)
        {
            Transform tile = Pooled(_tiles, ref _tilesUsed, "PlannedTile");
            tile.localScale = new Vector3(0.86f, 0.03f, 0.86f);
            tile.localPosition = new Vector3(x + 0.5f, 0.02f, y + 0.5f);
            tile.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
        }

        private void Arrow(int x, int y, Direction direction, Color color)
        {
            Transform arrow = Pooled(_arrows, ref _arrowsUsed, "PlannedArrow");
            float yaw = direction switch { Direction.Right => 90f, Direction.Up => 0f, Direction.Left => 270f, _ => 180f };
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            arrow.localRotation = rotation;
            arrow.localScale = new Vector3(0.16f, 0.05f, 0.42f);
            arrow.localPosition = new Vector3(x + 0.5f, 0.05f, y + 0.5f) + rotation * new Vector3(0f, 0f, 0.18f);
            arrow.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(Color.Lerp(color, Color.white, 0.6f));
        }

        private Transform Pooled(List<Transform> pool, ref int used, string name)
        {
            if (used < pool.Count)
            {
                Transform existing = pool[used++];
                existing.gameObject.SetActive(true);
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
