using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // Relief on top of the ground (MapView): a rock on every blocked cell, a pile of ore or a
    // cluster of crystals on every ore cell, and here and there a piece of scenery on plain ground.
    // Models and their textures lie in Assets/Resources/Terrain and are found by name:
    // rock_<terrain id>_<n>, ore_primary_<n>, ore_secondary_<n>, decor_<n>, n from 1 without gaps
    // (the texture is <model>_tex). A new kind of rock in terrain.csv gets its look by adding
    // files; without them it looks like plain rock. Models come from Tools/art/bake.py: the
    // footprint is one unit wide and centred, the bottom is at zero.
    // Which model stands on a cell, its turn and size come from a hash of the cell, so the map
    // looks the same every run. Follows the local player's fog like the ground: never seen —
    // hidden, seen before — dimmed, seen now — full. Ore and scenery hide under any building
    // (a drill or a conveyor sits on the ore).
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class TerrainView : MonoBehaviour
    {
        private const string Folder = "Terrain/";
        private const string PlainRock = "rock_rock";

        [SerializeField, Range(0f, 1f)] private float _rememberedBrightness = 0.5f;
        [Tooltip("Share of plain ground cells with a piece of scenery on them.")]
        [SerializeField, Range(0f, 0.2f)] private float _decorDensity = 0.02f;

        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private readonly List<Cell> _cells = new List<Cell>();
        private readonly Dictionary<string, Prop[]> _props = new Dictionary<string, Prop[]>();
        private readonly List<Material> _materials = new List<Material>();

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private Transform? _root;
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
            if (Props(PlainRock).Length == 0)
                Debug.LogError($"TerrainView: no models in Assets/Resources/{Folder} ({PlainRock}_1 …); the map has no rocks.");

            MapState map = simulation.State.Map;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (map.IsBlocked(x, y))
                        Rock(x, y, map.TerrainAt(x, y));
                    else if (map.OreAt(x, y) != MapSetup.NoOre)
                        Ore(x, y, map.OreAt(x, y));
                    else if (!map.IsRamp(x, y) && Unit(Hash(x, y), 40) < _decorDensity)
                        Decor(x, y);
                }
            }
            _rockChecksum = map.RockChecksum;
            Refresh(simulation.State);
        }

        // terrain.md: dug rock settles as it loses strength and disappears when dug out; ore found
        // under it gets its pile. Only when some rock changed (the checksum the sim keeps for its hash).
        private void UpdateRocks(MapState map)
        {
            if (map.RockChecksum == _rockChecksum)
                return;
            _rockChecksum = map.RockChecksum;
            int count = _cells.Count;
            for (int i = 0; i < count; i++)
            {
                Cell cell = _cells[i];
                if (cell.Gone || cell.MaxHp == 0)
                    continue;
                int hp = map.RockHp(cell.X, cell.Y);
                if (hp > 0)
                {
                    cell.Transform.localScale = Vector3.Scale(cell.Scale, new Vector3(1f, 0.45f + 0.55f * hp / cell.MaxHp, 1f));
                    continue;
                }
                cell.Gone = true;
                cell.Transform.gameObject.SetActive(false);
                int ore = map.OreAt(cell.X, cell.Y);
                if (ore != MapSetup.NoOre)
                    Ore(cell.X, cell.Y, ore);
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
                if (cell.HidesUnderBuilding && state.Map.BuildingAt(cell.X, cell.Y) != 0)
                    look = CellLook.Hidden;
                if (look == cell.Look)
                    continue;
                cell.Look = look;
                cell.Transform.gameObject.SetActive(look != CellLook.Hidden);
                if (look != CellLook.Hidden)
                    cell.Renderer.sharedMaterial = look == CellLook.Lit ? cell.Prop.Lit : cell.Prop.Dim;
            }
            _shownTick = state.Tick;
            _shownPlayer = playerId;
        }

        // Wider than the cell and sunk a little, turned by quarter turns with a wobble, taller or
        // lower by the hash: neighbouring blocked cells merge into one ridge, not a row of copies.
        private void Rock(int x, int y, TerrainStats? terrain)
        {
            Prop[] props = terrain != null ? Props("rock_" + terrain.Id) : Props(PlainRock);
            if (props.Length == 0)
                props = Props(PlainRock);
            if (props.Length == 0)
                return;
            uint h = Hash(x, y);
            float size = 1.15f + Unit(h, 2) * 0.25f;
            var scale = new Vector3(size, size * (0.85f + Unit(h, 3) * 0.4f), size);
            var offset = new Vector3((Unit(h, 4) - 0.5f) * 0.12f, -0.03f, (Unit(h, 5) - 0.5f) * 0.12f);
            float yaw = (h >> 8) % 4 * 90f + (Unit(h, 6) - 0.5f) * 24f;
            Cell cell = Place(x, y, "Rock", props[h % (uint)props.Length], offset, scale, yaw, hidesUnderBuilding: false);
            cell.MaxHp = terrain?.Hp ?? 0;
        }

        // Smaller than the cell: single cells of a deposit stay countable.
        private void Ore(int x, int y, int resourceType)
        {
            Prop[] props = Props(resourceType == Game.Sim.Resources.Primary ? "ore_primary" : "ore_secondary");
            if (props.Length == 0)
                return;
            uint h = Hash(x, y);
            float size = 0.6f + Unit(h, 2) * 0.2f;
            var offset = new Vector3((Unit(h, 4) - 0.5f) * 0.24f, -0.01f, (Unit(h, 5) - 0.5f) * 0.24f);
            Place(x, y, "Ore", props[h % (uint)props.Length], offset, Vector3.one * size, Unit(h, 6) * 360f, hidesUnderBuilding: true);
        }

        // Small and low, so it is not taken for an obstacle: units walk through it and buildings stand on it.
        private void Decor(int x, int y)
        {
            Prop[] props = Props("decor");
            if (props.Length == 0)
                return;
            uint h = Hash(x, y);
            float size = 0.55f + Unit(h, 2) * 0.3f;
            var offset = new Vector3((Unit(h, 4) - 0.5f) * 0.3f, -0.01f, (Unit(h, 5) - 0.5f) * 0.3f);
            Place(x, y, "Decor", props[(h >> 8) % (uint)props.Length], offset, Vector3.one * size, Unit(h, 6) * 360f, hidesUnderBuilding: true);
        }

        private Cell Place(int x, int y, string name, Prop prop, Vector3 offset, Vector3 scale, float yaw, bool hidesUnderBuilding)
        {
            var go = new GameObject($"{name} {x},{y}", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root, worldPositionStays: false);
            go.transform.localPosition = Ground.Lift(new Vector3(x + 0.5f, 0f, y + 0.5f) + offset);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = prop.Mesh;
            var cell = new Cell(x, y, go.transform, go.GetComponent<MeshRenderer>(), prop, scale, hidesUnderBuilding);
            _cells.Add(cell);
            return cell;
        }

        // Models of one kind: <prefix>_1, <prefix>_2 … up to the first that is missing.
        private Prop[] Props(string prefix)
        {
            if (_props.TryGetValue(prefix, out Prop[]? found))
                return found;
            var props = new List<Prop>();
            Material? lit = Primitives.Lit;
            for (int n = 1; lit != null; n++)
            {
                string path = $"{Folder}{prefix}_{n}";
                Mesh? mesh = UnityEngine.Resources.Load<Mesh>(path);
                Texture2D? texture = UnityEngine.Resources.Load<Texture2D>(path + "_tex");
                if (mesh == null || texture == null)
                    break;
                props.Add(new Prop(mesh, Textured(lit, texture, 1f), Textured(lit, texture, _rememberedBrightness)));
            }
            found = props.ToArray();
            _props.Add(prefix, found);
            return found;
        }

        private Material Textured(Material lit, Texture2D texture, float brightness)
        {
            var material = new Material(lit) { mainTexture = texture, color = new Color(brightness, brightness, brightness) };
            // Stone and ore are matte: the default Lit smoothness makes them look like plastic.
            if (material.HasProperty(SmoothnessId))
                material.SetFloat(SmoothnessId, 0.08f);
            _materials.Add(material);
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
            foreach (Material material in _materials)
                Destroy(material);
        }

        private enum CellLook { Unset, Hidden, Dim, Lit }

        // A model with its texture, as seen now and as remembered under the fog.
        private sealed class Prop
        {
            public Prop(Mesh mesh, Material lit, Material dim)
            {
                Mesh = mesh;
                Lit = lit;
                Dim = dim;
            }

            public Mesh Mesh { get; }
            public Material Lit { get; }
            public Material Dim { get; }
        }

        private sealed class Cell
        {
            public Cell(int x, int y, Transform transform, MeshRenderer renderer, Prop prop, Vector3 scale, bool hidesUnderBuilding)
            {
                X = x;
                Y = y;
                Transform = transform;
                Renderer = renderer;
                Prop = prop;
                Scale = scale;
                HidesUnderBuilding = hidesUnderBuilding;
            }

            public int X { get; }
            public int Y { get; }
            public Transform Transform { get; }
            public MeshRenderer Renderer { get; }
            public Prop Prop { get; }
            public Vector3 Scale { get; }
            public bool HidesUnderBuilding { get; }
            public CellLook Look { get; set; } = CellLook.Unset;

            // terrain.md: full strength of destructible rock (0 for anything else), and dug out.
            public int MaxHp { get; set; }
            public bool Gone { get; set; }
        }
    }
}
