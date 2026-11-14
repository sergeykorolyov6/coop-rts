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
        [Header("Rock kinds (terrain.md)")]
        [SerializeField] private Color _softRockColor = new Color(0.50f, 0.40f, 0.30f);
        [SerializeField] private Color _hardRockColor = new Color(0.40f, 0.43f, 0.52f);
        [Tooltip("Eternal rock: darker than anything a miner can dig.")]
        [SerializeField, Range(0f, 1f)] private float _eternalDarkening = 0.35f;
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
        private ulong _rockChecksum;
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
                        _cells.Add(Rocks(x, y, map.TerrainAt(x, y)));
                    else if (map.OreAt(x, y) != MapSetup.NoOre)
                        _cells.Add(Nuggets(x, y, map.OreAt(x, y)));
                }
            }
            _rockChecksum = map.RockChecksum;
            Refresh(simulation.State);
        }

        // terrain.md: dug rock settles as it loses strength and disappears when dug out; ore found
        // under it gets nuggets. Only when some rock changed (the checksum the sim keeps for its hash).
        private void UpdateRocks(MapState map)
        {
            if (map.RockChecksum == _rockChecksum)
                return;
            _rockChecksum = map.RockChecksum;
            int count = _cells.Count;
            for (int i = 0; i < count; i++)
            {
                Cell cell = _cells[i];
                if (cell.IsOre || cell.Gone || cell.MaxHp == 0)
                    continue;
                int hp = map.RockHp(cell.X, cell.Y);
                if (hp > 0)
                {
                    cell.Root.localScale = new Vector3(1f, 0.45f + 0.55f * hp / cell.MaxHp, 1f);
                    continue;
                }
                cell.Gone = true;
                cell.Root.gameObject.SetActive(false);
                cell.Root.localScale = Vector3.one;
                int ore = map.OreAt(cell.X, cell.Y);
                if (ore != MapSetup.NoOre)
                    _cells.Add(Nuggets(cell.X, cell.Y, ore));
            }
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
            UpdateRocks(state.Map);
            int playerId = _localPlayer!.PlayerId;
            foreach (Cell cell in _cells)
            {
                if (cell.Gone)
                    continue;
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
        private Cell Rocks(int x, int y, TerrainStats? terrain)
        {
            var cell = new Cell(x, y, isOre: false, CellRoot(x, y, "Rocks")) { MaxHp = terrain?.Hp ?? 0 };
            uint h = Hash(x, y);
            Color main = _rockColors[h % (uint)_rockColors.Length];
            // terrain.md: what can be dug reads apart from eternal rock, and soft from hard.
            if (terrain == null || !terrain.Destructible)
                main = Color.Lerp(main, Color.black, _eternalDarkening);
            else if (terrain.Id == "hard_rock")
                main = Color.Lerp(main, _hardRockColor, 0.7f);
            else
                main = Color.Lerp(main, _softRockColor, 0.7f);
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
            // Rock with ore inside shows specks of the resource's colour.
            if (terrain != null && terrain.OreType != MapSetup.NoOre)
            {
                Color speck = terrain.OreType == Game.Sim.Resources.Primary ? _primaryOreColor : _secondaryOreColor;
                for (int i = 0; i < 3; i++)
                {
                    float angle = Unit(h, 30 + i) * 360f;
                    Vector3 at = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0.35f + Unit(h, 33 + i) * 0.2f, 0.28f);
                    AddPart(cell, PrimitiveType.Cube, speck, at, Vector3.one * (0.12f + Unit(h, 36 + i) * 0.06f), angle, 30f);
                }
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
            root.localPosition = Ground.Lift(new Vector3(x + 0.5f, 0f, y + 0.5f));
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

            // terrain.md: full strength of destructible rock (0 for anything else), and dug out.
            public int MaxHp { get; set; }
            public bool Gone { get; set; }
        }
    }
}
