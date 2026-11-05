using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Minimap in the bottom-right corner: the fog-shaded ground (as MapView draws it), units and
    // buildings in their owners' colours (others only where the fog shows them), creep camps once
    // discovered (creep-camp.md), a trophy marker, a blinking ping where an own or ally building
    // is attacked (drill.md, combat.md), and the camera's view. Click or drag moves the camera.
    [RequireComponent(typeof(SimRunner), typeof(MapView), typeof(EntityView))]
    public sealed class Minimap : MonoBehaviour
    {
        // The panel around the map: the army button sits on top of it (SelectionController), as in SC2.
        public VisualElement? Frame { get; private set; }

        private const int PixelsPerCell = 3;
        private const float PingSeconds = 3f;

        [SerializeField] private Hud? _hud;
        [SerializeField] private RtsCamera? _camera;
        [SerializeField] private LocalPlayer? _localPlayer;
        [SerializeField] private float _size = 220f;
        [SerializeField] private float _redrawSeconds = 0.1f;
        [SerializeField] private Color _campColor = new Color(0.60f, 0.60f, 0.60f);
        [SerializeField] private Color _trophyColor = new Color(0.95f, 0.80f, 0.30f);
        [SerializeField] private Color _pingColor = new Color(1.00f, 0.25f, 0.20f);
        [Tooltip("Width of the camera-view frame in panel pixels.")]
        [SerializeField] private float _viewLineWidth = 1.5f;
        [SerializeField] private Color _viewColor = Color.white;

        private readonly List<Ping> _pings = new List<Ping>();

        private SimRunner? _runner;
        private MapView? _map;
        private EntityView? _entities;
        private VisualElement? _view;
        private VisualElement? _viewFrame;
        // Where the screen's corners hit the ground, in cells; drawn as a vector line over the texture.
        private readonly Vector2[] _viewCorners = new Vector2[4];
        private bool _hasView;
        private Texture2D? _texture;
        private Color32[] _pixels = new Color32[0];
        private int _width, _height;
        private float _nextRedraw;
        private bool _dragging;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _map = GetComponent<MapView>();
            _entities = GetComponent<EntityView>();
        }

        private void OnEnable() => _runner!.TickEvents += OnTickEvents;

        private void OnDisable() => _runner!.TickEvents -= OnTickEvents;

        private void Start()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_hud?.Root == null || simulation == null)
                return;
            _width = simulation.State.Map.Width * PixelsPerCell;
            _height = simulation.State.Map.Height * PixelsPerCell;
            _pixels = new Color32[_width * _height];
            _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Minimap",
            };

            var frame = new VisualElement { name = "Minimap" };
            Frame = frame;
            Hud.Panelise(frame);
            frame.style.position = Position.Absolute;
            frame.style.right = 12;
            frame.style.bottom = 12;
            frame.style.paddingLeft = frame.style.paddingRight = frame.style.paddingTop = frame.style.paddingBottom = 4;
            _view = new VisualElement();
            float aspect = simulation.State.Map.Height / (float)simulation.State.Map.Width;
            _view.style.width = _size;
            _view.style.height = _size * aspect;
            _view.style.backgroundImage = Background.FromTexture2D(_texture);
            _view.style.overflow = Overflow.Hidden;
            // The camera's view is a vector line over the texture: a 1-pixel line drawn into the
            // texture loses rows when the texture is scaled down to the panel and breaks into dashes.
            _viewFrame = new VisualElement { pickingMode = PickingMode.Ignore };
            _viewFrame.style.position = Position.Absolute;
            _viewFrame.style.left = _viewFrame.style.top = _viewFrame.style.right = _viewFrame.style.bottom = 0;
            _viewFrame.generateVisualContent += DrawViewFrame;
            _view.Add(_viewFrame);
            _view.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _view.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _view.RegisterCallback<PointerUpEvent>(OnPointerUp);
            frame.Add(_view);
            _hud.Root.Add(frame);
            // The bottom bar stops left of the minimap: frame margin + view + padding + a gap.
            if (_hud.BottomBar != null)
                _hud.BottomBar.style.right = 12 + _size + 8 + 12;
        }

        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _texture == null)
                return;
            // The view frame follows the camera every frame; the texture only every _redrawSeconds.
            UpdateCameraView();
            if (Time.unscaledTime < _nextRedraw)
                return;
            _nextRedraw = Time.unscaledTime + _redrawSeconds;
            Redraw(simulation.State);
        }

        private void Redraw(SimState state)
        {
            int playerId = _localPlayer != null ? _localPlayer.PlayerId : 1;
            int cellsW = state.Map.Width, cellsH = state.Map.Height;
            for (int y = 0; y < cellsH; y++)
                for (int x = 0; x < cellsW; x++)
                    FillCell(x, y, _map!.ShadedCell(x, y));

            foreach (Building building in state.Buildings)
            {
                if (building.IsDead || !Visibility.BuildingShown(state, playerId, building))
                    continue;
                Color32 color = _entities!.OwnerColor(state, building.OwnerId);
                for (int y = building.CellY; y < building.CellY + building.Height; y++)
                    for (int x = building.CellX; x < building.CellX + building.Width; x++)
                        FillCell(x, y, color);
            }

            foreach (CreepCamp camp in state.CreepCamps)
            {
                if (!state.Fog.IsDiscovered(playerId, camp.CenterX, camp.CenterY))
                    continue;
                int px = camp.CenterX * PixelsPerCell + 1, py = camp.CenterY * PixelsPerCell + 1;
                if (camp.State != CreepCampState.Cleared)
                    Square(px, py, 3, _campColor);
                else if (camp.TrophyPrimary + camp.TrophySecondary > 0)
                    Square(px, py, 2, _trophyColor);
            }

            foreach (Unit unit in state.Units)
            {
                if (unit.IsDead || !Visibility.UnitShown(state, playerId, unit))
                    continue;
                Color32 color = _entities!.OwnerColor(state, unit.OwnerId);
                int px = unit.X * PixelsPerCell / Grid.MilliPerCell, py = unit.Y * PixelsPerCell / Grid.MilliPerCell;
                Square(px, py, 1, color);
            }

            for (int i = _pings.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - _pings[i].Born;
                if (age >= PingSeconds)
                {
                    _pings.RemoveAt(i);
                    continue;
                }
                // Blinks twice a second so it catches the eye.
                if ((int)(age * 4f) % 2 == 0)
                    Ring(_pings[i].X, _pings[i].Y, 4, _pingColor);
            }

            _texture!.SetPixels32(_pixels);
            _texture.Apply();
        }

        private void OnTickEvents(IReadOnlyList<SimEvent> events)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _localPlayer == null)
                return;
            foreach (SimEvent e in events)
            {
                // legion-ai.md: where the coming wave starts from.
                if (e is WaveIncoming && simulation.State.Legion != null)
                {
                    LegionAi legion = simulation.State.Legion;
                    _pings.Add(new Ping(legion.RallyX * PixelsPerCell + PixelsPerCell / 2, legion.RallyY * PixelsPerCell + PixelsPerCell / 2, Time.unscaledTime));
                    continue;
                }
                // legion-ai.md: the wave is out — mark what it goes for, so the team knows where to defend.
                if (e is WaveLaunched launched)
                {
                    Building? target = simulation.State.FindBuilding(launched.TargetId);
                    if (target != null)
                        _pings.Add(new Ping((target.CellX * 2 + target.Width) * PixelsPerCell / 2, (target.CellY * 2 + target.Height) * PixelsPerCell / 2, Time.unscaledTime));
                    continue;
                }
                // alerts.md: units under attack get the same mark as buildings.
                if (e is UnitAttacked hit && simulation.State.Relations.IsAlly(_localPlayer.PlayerId, hit.OwnerId))
                {
                    Unit? unit = simulation.State.FindUnit(hit.UnitId);
                    if (unit != null)
                        _pings.Add(new Ping(unit.X * PixelsPerCell / Grid.MilliPerCell, unit.Y * PixelsPerCell / Grid.MilliPerCell, Time.unscaledTime));
                    continue;
                }
                if (e is BuildingAttacked attacked && simulation.State.Relations.IsAlly(_localPlayer.PlayerId, attacked.OwnerId))
                {
                    Building? building = simulation.State.FindBuilding(attacked.BuildingId);
                    if (building == null)
                        continue;
                    int px = (building.CellX * 2 + building.Width) * PixelsPerCell / 2;
                    int py = (building.CellY * 2 + building.Height) * PixelsPerCell / 2;
                    _pings.Add(new Ping(px, py, Time.unscaledTime));
                }
            }
        }

        // The ground the screen shows: where its corners hit the ground, in cells.
        private void UpdateCameraView()
        {
            if (_camera == null || _viewFrame == null)
                return;
            bool had = _hasView;
            _hasView = true;
            for (int i = 0; i < 4; i++)
            {
                var screen = new Vector2(i == 1 || i == 2 ? Screen.width : 0, i >= 2 ? Screen.height : 0);
                if (!_camera.GroundPoint(screen, out Vector3 ground))
                {
                    _hasView = false;
                    break;
                }
                _viewCorners[i] = new Vector2(ground.x, ground.z);
            }
            if (_hasView || had)
                _viewFrame.MarkDirtyRepaint();
        }

        // Cells to the element: the top-left is the map's north-west corner, y grows down on the panel.
        private void DrawViewFrame(MeshGenerationContext context)
        {
            Simulation? simulation = _runner!.Simulation;
            if (!_hasView || simulation == null || _viewFrame == null)
                return;
            Rect rect = _viewFrame.contentRect;
            float sx = rect.width / simulation.State.Map.Width, sy = rect.height / simulation.State.Map.Height;
            Painter2D painter = context.painter2D;
            painter.strokeColor = _viewColor;
            painter.lineWidth = _viewLineWidth;
            painter.lineJoin = LineJoin.Miter;
            painter.BeginPath();
            for (int i = 0; i < 4; i++)
            {
                var point = new Vector2(_viewCorners[i].x * sx, rect.height - _viewCorners[i].y * sy);
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;
            _dragging = true;
            _view!.CapturePointer(evt.pointerId);
            FocusAt(evt.localPosition);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_dragging)
                FocusAt(evt.localPosition);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            _dragging = false;
            _view!.ReleasePointer(evt.pointerId);
        }

        // The element's top-left is the map's north-west corner: y grows down on the panel, up on the map.
        private void FocusAt(Vector2 local)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _camera == null || _view == null)
                return;
            Rect rect = _view.contentRect;
            float x = Mathf.Clamp01(local.x / rect.width) * simulation.State.Map.Width;
            float y = (1f - Mathf.Clamp01(local.y / rect.height)) * simulation.State.Map.Height;
            _camera.Focus(new Vector3(x, 0f, y));
        }

        private void FillCell(int cellX, int cellY, Color32 color)
        {
            int x0 = cellX * PixelsPerCell, y0 = cellY * PixelsPerCell;
            for (int y = y0; y < y0 + PixelsPerCell; y++)
                for (int x = x0; x < x0 + PixelsPerCell; x++)
                    _pixels[y * _width + x] = color;
        }

        private void Square(int cx, int cy, int radius, Color32 color)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                    Set(x, y, color);
        }

        private void Ring(int cx, int cy, int radius, Color32 color)
        {
            for (int d = -radius; d <= radius; d++)
            {
                Set(cx + d, cy - radius, color);
                Set(cx + d, cy + radius, color);
                Set(cx - radius, cy + d, color);
                Set(cx + radius, cy + d, color);
            }
        }

        private void Set(int x, int y, Color32 color)
        {
            if (x >= 0 && y >= 0 && x < _width && y < _height)
                _pixels[y * _width + x] = color;
        }

        private void OnDestroy()
        {
            if (_texture != null)
                Destroy(_texture);
        }

        private readonly struct Ping
        {
            public Ping(int x, int y, float born)
            {
                X = x;
                Y = y;
                Born = born;
            }

            public int X { get; }
            public int Y { get; }
            public float Born { get; }
        }
    }
}
