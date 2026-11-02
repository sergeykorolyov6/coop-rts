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
        [Header("Fog of war")]
        [SerializeField] private Color _undiscoveredColor = new Color(0.05f, 0.05f, 0.06f);
        [Tooltip("Brightness of discovered cells no one of the team sees right now.")]
        [SerializeField, Range(0f, 1f)] private float _rememberedBrightness = 0.5f;

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private Texture2D? _texture;
        private Material? _material;
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

            // URP Lit from Primitives: its main texture is the cell picture.
            var ground = Primitives.Create(PrimitiveType.Quad);
            ground.name = "Ground";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(transform, worldPositionStays: false);
            // Quad lies in XY facing -Z; +90° about X lays it on XZ facing up, local Y → world Z.
            // Slightly below zero so the gizmo grid is not hidden in the Scene view.
            ground.transform.localPosition = new Vector3(width * 0.5f, -0.01f, height * 0.5f);
            ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ground.transform.localScale = new Vector3(width, height, 1f);

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

        private Color CellColor(MapState map, int x, int y)
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
            if (_texture != null)
                Destroy(_texture);
        }
    }
}
