using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // Ground of the map in the Game view: one texel per cell — terrain, rock and ore of both
    // resources; checkerboard so cells read without gizmos. Shaded by the local player's fog:
    // never seen — dark, nothing of the ground shows; seen before — dimmed; seen now — full.
    // Same layout as SimRunner's gizmo grid: cell (x, y) → world XZ [x, x+1) × [y, y+1).
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class MapView : MonoBehaviour
    {
        [SerializeField] private Color _cellColorA = new Color(0.32f, 0.36f, 0.30f);
        [SerializeField] private Color _cellColorB = new Color(0.28f, 0.32f, 0.26f);
        [SerializeField] private Color _blockedColor = new Color(0.15f, 0.14f, 0.13f);
        [SerializeField] private Color _primaryOreColor = new Color(0.80f, 0.55f, 0.25f);
        [SerializeField] private Color _secondaryOreColor = new Color(0.35f, 0.70f, 0.85f);
        [Tooltip("Ore cells keep a faint checker so single cells stay countable.")]
        [SerializeField, Range(0f, 0.2f)] private float _oreCheckerShade = 0.06f;
        [Header("Heights")]
        [Tooltip("How much lighter each level is than the one below.")]
        [SerializeField, Range(0f, 0.2f)] private float _levelBrightening = 0.07f;
        [SerializeField, Range(0f, 0.6f)] private float _cliffShadow = 0.35f;
        [SerializeField] private Color _rampColor = new Color(0.62f, 0.56f, 0.40f);
        [Header("Fog of war")]
        [SerializeField] private Color _undiscoveredColor = new Color(0.05f, 0.05f, 0.06f);
        [Tooltip("Brightness of discovered cells no one of the team sees right now.")]
        [SerializeField, Range(0f, 1f)] private float _rememberedBrightness = 0.5f;

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private Texture2D? _texture;
        private Material? _material;
        private Mesh? _mesh;
        private Color32[] _baseColors = new Color32[0];
        private Color32[] _shaded = new Color32[0];
        private int _shadedTick = -1;
        private int _shadedPlayer = -1;

        // Fog-shaded colour of a cell as on the ground this frame; the minimap draws the same.
        public Color32 ShadedCell(int x, int y) => _shaded[y * (_texture != null ? _texture.width : 1) + x];

        // Start, not Awake: SimRunner creates the Simulation in its Awake.
        private void Start()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
            var simulation = _runner.Simulation;
            if (simulation == null)
                return;

            MapState map = simulation.State.Map;
            int width = map.Width;
            int height = map.Height;
            _baseColors = new Color32[width * height];
            _shaded = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    _baseColors[y * width + x] = CellColor(map, x, y);

            _texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "MapCells",
            };
            Shade(simulation.State);

            // URP Lit from Primitives: its main texture is the cell picture. The mesh is the ground
            // in steps (heights.md): a quad per cell at its level, a slope on a ramp, a wall at a cliff.
            var ground = Primitives.Create(PrimitiveType.Quad);
            ground.name = "Ground";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(transform, worldPositionStays: false);
            // Slightly below zero so the gizmo grid is not hidden in the Scene view.
            ground.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            _mesh = BuildMesh(map);
            ground.GetComponent<MeshFilter>().sharedMesh = _mesh;

            var groundRenderer = ground.GetComponent<MeshRenderer>();
            _material = groundRenderer.material;
            _material.mainTexture = _texture;
        }

        // The fog changes only on ticks; 64×64 cells per tick is nothing to repaint.
        private void LateUpdate()
        {
            Simulation? simulation = _runner != null ? _runner.Simulation : null;
            if (simulation == null || _texture == null)
                return;
            if (simulation.State.Tick != _shadedTick || _localPlayer!.PlayerId != _shadedPlayer)
                Shade(simulation.State);
        }

        // Every cell has its own four vertices (so a cliff is a clean step) and takes its colour from
        // its own texel; a wall takes the texel of the upper cell, so the fog hides it with the plateau.
        private static Mesh BuildMesh(MapState map)
        {
            int width = map.Width, height = map.Height;
            var vertices = new System.Collections.Generic.List<Vector3>(width * height * 4);
            var uvs = new System.Collections.Generic.List<Vector2>(width * height * 4);
            var triangles = new System.Collections.Generic.List<int>(width * height * 6);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv)
            {
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uvs.Add(uv); uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }

            // A wall on one side of a cell, seen from outside: left to right along the edge, where
            // this cell stands higher than the neighbour across it.
            void Wall(int x, int y, int nx, int ny, int lx, int ly, int rx, int ry, Vector2 uv)
            {
                if (!map.InBounds(nx, ny))
                    return;
                // Corner offsets of the same two points as the neighbour names them.
                int nlx = lx - (nx - x), nly = ly - (ny - y), nrx = rx - (nx - x), nry = ry - (ny - y);
                float topLeft = Ground.CornerHeight(map, x, y, lx, ly), topRight = Ground.CornerHeight(map, x, y, rx, ry);
                float bottomLeft = Ground.CornerHeight(map, nx, ny, nlx, nly), bottomRight = Ground.CornerHeight(map, nx, ny, nrx, nry);
                if (topLeft <= bottomLeft + 0.001f && topRight <= bottomRight + 0.001f)
                    return;
                bottomLeft = Mathf.Min(bottomLeft, topLeft);
                bottomRight = Mathf.Min(bottomRight, topRight);
                Quad(new Vector3(x + lx, topLeft, y + ly), new Vector3(x + rx, topRight, y + ry),
                    new Vector3(x + rx, bottomRight, y + ry), new Vector3(x + lx, bottomLeft, y + ly), uv);
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var uv = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
                    // Clockwise seen from above: (0,0) → (0,1) → (1,1) → (1,0).
                    Quad(new Vector3(x, Ground.CornerHeight(map, x, y, 0, 0), y), new Vector3(x, Ground.CornerHeight(map, x, y, 0, 1), y + 1),
                        new Vector3(x + 1, Ground.CornerHeight(map, x, y, 1, 1), y + 1), new Vector3(x + 1, Ground.CornerHeight(map, x, y, 1, 0), y), uv);
                    if (!map.HasLevels)
                        continue;
                    Wall(x, y, x + 1, y, 1, 0, 1, 1, uv);   // east, seen from +X: left is the low z
                    Wall(x, y, x - 1, y, 0, 1, 0, 0, uv);   // west
                    Wall(x, y, x, y + 1, 1, 1, 0, 1, uv);   // north
                    Wall(x, y, x, y - 1, 0, 0, 1, 0, uv);   // south
                }
            }

            var mesh = new Mesh { name = "Ground", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Shade(SimState state)
        {
            int playerId = _localPlayer!.PlayerId;
            int width = state.Map.Width, height = state.Map.Height;
            Color32 undiscovered = _undiscoveredColor;
            byte remembered = (byte)(_rememberedBrightness * 255f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (!state.Fog.IsDiscovered(playerId, x, y))
                        _shaded[i] = undiscovered;
                    else if (state.Fog.IsVisible(playerId, x, y))
                        _shaded[i] = _baseColors[i];
                    else
                    {
                        Color32 c = _baseColors[i];
                        _shaded[i] = new Color32((byte)(c.r * remembered / 255), (byte)(c.g * remembered / 255), (byte)(c.b * remembered / 255), 255);
                    }
                }
            }
            _texture!.SetPixels32(_shaded);
            _texture.Apply();
            _shadedTick = state.Tick;
            _shadedPlayer = playerId;
        }

        // heights.md: higher ground is lighter, the foot of a cliff lies in its shadow and a ramp is
        // striped across — levels read from straight above and on the minimap, where walls do not show.
        private Color CellColor(MapState map, int x, int y)
        {
            Color color = FlatColor(map, x, y);
            if (!map.HasLevels)
                return color;
            int level = map.LevelAt(x, y);
            color = Color.Lerp(color, Color.white, level * _levelBrightening);
            if (map.IsRamp(x, y))
                return Color.Lerp(color, _rampColor, 0.45f);
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (map.InBounds(nx, ny) && map.LevelAt(nx, ny) > level && !map.LevelsJoin(x, y, nx, ny))
                    return Color.Lerp(color, Color.black, _cliffShadow);
            }
            return color;
        }

        private Color FlatColor(MapState map, int x, int y)
        {
            bool even = (x + y) % 2 == 0;
            if (map.IsBlocked(x, y))
                return _blockedColor;
            int ore = map.OreAt(x, y);
            if (ore == MapSetup.NoOre)
                return even ? _cellColorA : _cellColorB;
            Color color = ore == Game.Sim.Resources.Primary ? _primaryOreColor : _secondaryOreColor;
            return even ? color : Color.Lerp(color, Color.black, _oreCheckerShade);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
            if (_mesh != null)
                Destroy(_mesh);
            if (_texture != null)
                Destroy(_texture);
        }
    }
}
