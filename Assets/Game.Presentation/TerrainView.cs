using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // Relief on top of the flat ground texture (MapView): rocks on blocked cells, a few nuggets
    // in the resource's colour on ore cells. Variation comes from a hash of the cell, so the
    // look is the same every run. Follows the local player's fog like the ground: never seen —
    // hidden, seen before — dimmed, seen now — full. Nuggets hide under any building
    // (a drill or a conveyor sits on the ore).
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class TerrainView : MonoBehaviour
    {
        [SerializeField] private Color[] _rockColors =
        {
            new Color(0.36f, 0.34f, 0.31f),
            new Color(0.42f, 0.39f, 0.35f),
            new Color(0.30f, 0.29f, 0.28f),
        };
        [SerializeField] private Color _primaryOreColor = new Color(0.95f, 0.62f, 0.25f);
        [SerializeField] private Color _secondaryOreColor = new Color(0.40f, 0.80f, 0.95f);
        [SerializeField, Range(0f, 1f)] private float _rememberedBrightness = 0.5f;

        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private readonly List<Cell> _cells = new List<Cell>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private Transform? _root;
        private Material? _baseMaterial;
        private int _shownTick = -1;
        private int _shownPlayer = -1;

        // Start, not Awake: SimRunner creates the Simulation in its Awake.
        private void Start()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
            Simulation? simulation = _runner.Simulation;
            if (simulation == null)
                return;
            _root = new GameObject("Terrain").transform;
            _root.SetParent(transform, worldPositionStays: false);

            MapState map = simulation.State.Map;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (map.IsBlocked(x, y))
                        _cells.Add(Rocks(x, y));
                    else if (map.OreAt(x, y) != MapSetup.NoOre)
                        _cells.Add(Nuggets(x, y, map.OreAt(x, y)));
                }
            }
            Refresh(simulation.State);
        }

        private void LateUpdate()
        {
            Simulation? simulation = _runner != null ? _runner.Simulation : null;
            if (simulation == null || _root == null)
                return;
            if (simulation.State.Tick != _shownTick || _localPlayer!.PlayerId != _shownPlayer)
                Refresh(simulation.State);
        }

        private void Refresh(SimState state)
        {
            int playerId = _localPlayer!.PlayerId;
            foreach (Cell cell in _cells)
            {
                CellLook look = !state.Fog.IsDiscovered(playerId, cell.X, cell.Y) ? CellLook.Hidden
                    : state.Fog.IsVisible(playerId, cell.X, cell.Y) ? CellLook.Lit
                    : CellLook.Dim;
                if (cell.IsOre && state.Map.BuildingAt(cell.X, cell.Y) != 0)
                    look = CellLook.Hidden;
                if (look == cell.Look)
                    continue;
                cell.Look = look;
                cell.Root.gameObject.SetActive(look != CellLook.Hidden);
                if (look == CellLook.Hidden)
                    continue;
                for (int i = 0; i < cell.Renderers.Count; i++)
                {
                    Color color = look == CellLook.Lit ? cell.Colors[i] : Color.Lerp(Color.black, cell.Colors[i], _rememberedBrightness);
                    cell.Renderers[i].sharedMaterial = MaterialFor(color);
                }
            }
            _shownTick = state.Tick;
            _shownPlayer = playerId;
        }

        // A low rounded base sunk into the ground, a big tilted block breaking the silhouette and
        // a shard or two around it, sized and turned by the cell's hash: neighbouring blocked cells
        // merge into ridges. Neither pure cubes (read as crates) nor pure spheres (read as eggs).
        private Cell Rocks(int x, int y)
        {
            var cell = new Cell(x, y, isOre: false, CellRoot(x, y, "Rocks"));
            uint h = Hash(x, y);
            Color main = _rockColors[h % (uint)_rockColors.Length];
            float width = 1.05f + Unit(h, 2) * 0.25f;
            AddPart(cell, PrimitiveType.Sphere, Color.Lerp(main, Color.black, 0.15f), new Vector3(0f, 0.05f, 0f),
                new Vector3(width, 0.45f + Unit(h, 1) * 0.2f, width * 0.9f), Unit(h, 6) * 180f, 0f);

            float block = 0.5f + Unit(h, 3) * 0.3f;
            AddPart(cell, PrimitiveType.Cube, main, new Vector3(Unit(h, 4) * 0.2f - 0.1f, 0.25f + block * 0.3f, Unit(h, 5) * 0.2f - 0.1f),
                new Vector3(block, block * (0.8f + Unit(h, 7) * 0.7f), block * 0.85f), Unit(h, 8) * 360f, 18f + Unit(h, 9) * 20f);

            int shards = 1 + (int)(h >> 20) % 2;
            for (int i = 0; i < shards; i++)
            {
                float size = 0.18f + Unit(h, 10 + i) * 0.2f;
                float angle = Unit(h, 12 + i) * 360f;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, 0.32f + Unit(h, 14 + i) * 0.12f);
                Color shard = Color.Lerp(main, Color.white, 0.1f);
                AddPart(cell, PrimitiveType.Cube, shard, offset + new Vector3(0f, 0.12f + size * 0.3f, 0f),
                    new Vector3(size, size * (0.8f + Unit(h, 16 + i) * 0.6f), size * 0.8f), angle, 25f + Unit(h, 18 + i) * 20f);
            }
            return cell;
        }

        // Two or three low angular chunks: the cell reads as ore from any zoom.
        private Cell Nuggets(int x, int y, int resourceType)
        {
            var cell = new Cell(x, y, isOre: true, CellRoot(x, y, "Ore"));
            uint h = Hash(x, y);
            Color color = resourceType == Game.Sim.Resources.Primary ? _primaryOreColor : _secondaryOreColor;
            int count = 2 + (int)(h >> 24) % 2;
            for (int i = 0; i < count; i++)
            {
                float size = 0.14f + Unit(h, i + 1) * 0.12f;
                Vector3 offset = new Vector3(Unit(h, i + 4) * 0.6f - 0.3f, size * 0.35f, Unit(h, i + 7) * 0.6f - 0.3f);
                Color shade = Color.Lerp(color, Color.white, Unit(h, i + 10) * 0.25f);
                AddPart(cell, PrimitiveType.Cube, shade, offset, new Vector3(size, size * 0.8f, size), Unit(h, i + 13) * 90f, 35f);
            }
            return cell;
        }

        private Transform CellRoot(int x, int y, string name)
        {
            var root = new GameObject($"{name} {x},{y}").transform;
            root.SetParent(_root, worldPositionStays: false);
            root.localPosition = new Vector3(x + 0.5f, 0f, y + 0.5f);
            return root;
        }

        private void AddPart(Cell cell, PrimitiveType type, Color color, Vector3 position, Vector3 scale, float yaw, float tilt)
        {
            GameObject go = Primitives.Create(type);
            Destroy(go.GetComponent<Collider>());
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            _baseMaterial ??= renderer.sharedMaterial;
            renderer.sharedMaterial = MaterialFor(color);
            go.transform.SetParent(cell.Root, worldPositionStays: false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(tilt, yaw, tilt * 0.5f);
            go.transform.localScale = scale;
            cell.Renderers.Add(renderer);
            cell.Colors.Add(color);
        }

        private Material MaterialFor(Color color)
        {
            if (!_materials.TryGetValue(color, out Material? material))
            {
                material = new Material(_baseMaterial) { color = color };
                // Stone and ore are matte: the default Lit smoothness makes them look like plastic.
                if (material.HasProperty(SmoothnessId))
                    material.SetFloat(SmoothnessId, 0.08f);
                _materials.Add(color, material);
            }
            return material;
        }

        // Integer hash of the cell (Wang-style mixing): stable variation without a random generator.
        private static uint Hash(int x, int y)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h = (h ^ 61u) ^ (h >> 16);
            h *= 9u;
            h ^= h >> 4;
            h *= 0x27d4eb2du;
            h ^= h >> 15;
            return h;
        }

        // A value in 0..1 from the hash and a salt, one per property.
        private static float Unit(uint hash, int salt)
        {
            uint h = hash ^ (uint)(salt * 0x9E3779B9u);
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        private void OnDestroy()
        {
            foreach (Material material in _materials.Values)
                Destroy(material);
        }

        private enum CellLook { Unset, Hidden, Dim, Lit }

        private sealed class Cell
        {
            public Cell(int x, int y, bool isOre, Transform root)
            {
                X = x;
                Y = y;
                IsOre = isOre;
                Root = root;
            }

            public int X { get; }
            public int Y { get; }
            public bool IsOre { get; }
            public Transform Root { get; }
            public List<MeshRenderer> Renderers { get; } = new List<MeshRenderer>();
            public List<Color> Colors { get; } = new List<Color>();
            public CellLook Look { get; set; } = CellLook.Unset;
        }
    }
}
