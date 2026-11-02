using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Placing a building chosen in the build menu, with the selected workers.
    // One building: a silhouette follows the cursor, green / red / yellow (inside a live creep
    // camp's aggro radius, creep-camp.md), a drill shows its ore cells and rate (drill.md); click places.
    // Conveyor: press and drag a line, it turns where the mouse turns, arrows show the direction
    // of every cell, the tip shows the total cost (conveyors.md). Bridge: first end, then second
    // end on a straight line within range. R rotates a single conveyor cell or bridge.
    // Right click or Esc cancels. Placing ends the mode; with Shift the building joins the workers'
    // queue and the mode stays for the next one (order-queue.md).
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class PlacementController : MonoBehaviour
    {
        private enum Mode { None, Single, Line, Bridge }

        [SerializeField] private RtsCamera? _camera;
        [SerializeField] private Hud? _hud;
        [SerializeField] private Color _okColor = new Color(0.30f, 0.85f, 0.40f);
        [Tooltip("Drill footprint cells that are ore.")]
        [SerializeField] private Color _oreColor = new Color(0.65f, 1.00f, 0.55f);
        [SerializeField] private Color _blockedColor = new Color(0.90f, 0.25f, 0.20f);
        [SerializeField] private Color _warningColor = new Color(0.95f, 0.80f, 0.20f);
        [SerializeField] private Color _spanColor = new Color(0.55f, 0.65f, 0.80f);

        private readonly List<Vector2Int> _path = new List<Vector2Int>();
        private readonly List<int> _workers = new List<int>();
        private readonly List<Transform> _tiles = new List<Transform>();
        private readonly List<Transform> _arrows = new List<Transform>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private Transform? _root;
        private Material? _baseMaterial;

        private Mode _mode;
        private BuildingStats? _stats;
        private Direction _rotation = Direction.Right;
        private bool _dragging;
        private Vector2Int? _bridgeStart;
        private int _endedFrame = -1;
        private int _tilesUsed, _arrowsUsed;

        public bool IsActive => _mode != Mode.None;

        // Also true on the frame the mode ended: the click that placed or cancelled must not
        // select or order anything.
        public bool IsBusy => IsActive || _endedFrame == Time.frameCount;

        public string? BuildingId => _stats?.Id;

        // Cells the single-building silhouette covers this frame, null when there is none
        // (turret.md: the range ring is drawn around it).
        public RectInt? Footprint { get; private set; }

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
            _root = new GameObject("Placement").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        public void Begin(string buildingId)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || !simulation.State.Data.TryGetBuilding(buildingId, out BuildingStats? stats) || !CollectWorkers(simulation.State))
                return;
            _stats = stats;
            _mode = buildingId == BuildingKinds.Conveyor ? Mode.Line
                : buildingId == BuildingKinds.ConveyorBridge ? Mode.Bridge
                : Mode.Single;
            _dragging = false;
            _bridgeStart = null;
            _path.Clear();
        }

        public void Cancel() => End();

        private void End()
        {
            if (_mode == Mode.None)
                return;
            _mode = Mode.None;
            _stats = null;
            Footprint = null;
            _dragging = false;
            _bridgeStart = null;
            _endedFrame = Time.frameCount;
            HideAll();
            _hud?.ShowTip(null, Vector2.zero);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_mode == Mode.None || simulation == null || _camera == null)
                return;
            SimState state = simulation.State;
            if (!CollectWorkers(state))
            {
                End();
                return;
            }

            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;
            if (mouse == null || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || mouse.rightButton.wasPressedThisFrame)
            {
                End();
                return;
            }
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                _rotation = (Direction)(((int)_rotation + 1) % 4);

            Vector2 pointer = mouse.position.ReadValue();
            _tilesUsed = _arrowsUsed = 0;
            Footprint = null;
            string? tip = null;
            if (_camera.GroundPoint(pointer, out Vector3 ground))
            {
                bool overUi = _hud != null && _hud.IsPointerOverUi(pointer);
                switch (_mode)
                {
                    case Mode.Single: tip = UpdateSingle(simulation, ground, mouse, overUi); break;
                    case Mode.Line: tip = UpdateLine(simulation, ground, mouse, overUi); break;
                    case Mode.Bridge: tip = UpdateBridge(simulation, ground, mouse, overUi); break;
                }
            }
            if (_mode == Mode.None)
                return;
            HideUnused();
            _hud?.ShowTip(tip, pointer);
        }

        private string UpdateSingle(Simulation simulation, Vector3 ground, Mouse mouse, bool overUi)
        {
            SimState state = simulation.State;
            BuildingStats stats = _stats!;
            int playerId = _localPlayer!.PlayerId;
            // The cursor is the footprint's centre.
            int x = Mathf.FloorToInt(ground.x - (stats.SizeW - 1) * 0.5f);
            int y = Mathf.FloorToInt(ground.z - (stats.SizeH - 1) * 0.5f);
            Footprint = new RectInt(x, y, stats.SizeW, stats.SizeH);

            RejectReason? reason = PlacementPreview.Check(state, playerId, stats.Id, x, y, _rotation);
            bool nearCamp = reason == null && InCampAggro(state, playerId, x, y, stats.SizeW, stats.SizeH);
            Color color = reason != null ? _blockedColor : nearCamp ? _warningColor : _okColor;
            bool drill = stats.Id == BuildingKinds.Drill;
            for (int cy = y; cy < y + stats.SizeH; cy++)
            {
                for (int cx = x; cx < x + stats.SizeW; cx++)
                {
                    bool ore = drill && reason == null && state.Map.InBounds(cx, cy) && state.Map.OreAt(cx, cy) != MapSetup.NoOre;
                    PlaceTile(cx, cy, ore ? _oreColor : color);
                }
            }

            string tip = $"{DisplayNames.Building(stats.Id)} — {Cost(stats)}";
            if (reason != null)
                tip += "\n" + DisplayNames.Reject(reason.Value, stats.Id);
            else if (drill)
            {
                int cells = DrillPreview.OreCellsUnder(state.Map, x, y);
                tip += $"\nРуды под буром: {cells} из 4 · {DisplayNames.PerSecond(cells, state.Rules.DrillTicksPerUnitPerOreCell)}";
                tip += "\nОтдаёт в соседнюю ленту, склад или штаб; в кошелёк зачисляет только штаб";
            }
            if (nearCamp)
                tip += "\nРядом лагерь крипов: нападут";

            if (mouse.leftButton.wasPressedThisFrame && !overUi && reason == null)
            {
                Order(simulation, new[] { new BuildSite(x, y, _rotation) });
            }
            return tip;
        }

        private string UpdateLine(Simulation simulation, Vector3 ground, Mouse mouse, bool overUi)
        {
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            Vector2Int cursor = CursorCell(state, ground);

            if (!_dragging)
            {
                _path.Clear();
                _path.Add(cursor);
                if (mouse.leftButton.wasPressedThisFrame && !overUi)
                    _dragging = true;
            }
            else
            {
                Extend(cursor);
            }

            int count = _path.Count;
            RejectReason? first = null;
            for (int i = 0; i < count; i++)
            {
                Direction direction = LineDirection(i);
                RejectReason? reason = PlacementPreview.Check(state, playerId, _stats!.Id, _path[i].x, _path[i].y, direction);
                if (i == 0)
                    first = reason;
                PlaceTile(_path[i].x, _path[i].y, reason == null ? _okColor : _blockedColor);
                PlaceArrow(_path[i].x, _path[i].y, direction);
            }

            int total = count * _stats!.CostPrimary;
            int wallet = state.GetPlayer(playerId).Wallet.Get(Game.Sim.Resources.Primary);
            string tip = $"Лента: {count} × {_stats.CostPrimary} = {total}";
            if (total > wallet)
                tip += $"\nСейчас хватает на {wallet / Mathf.Max(1, _stats.CostPrimary)} — остальное по мере дохода";
            if (first != null)
                tip += "\n" + DisplayNames.Reject(first.Value, _stats.Id);
            if (!_dragging)
                tip += "\nЗажмите и тяните · R — поворот\nРесурс едет по стрелкам: конец ленты направьте в штаб";

            if (_dragging && !mouse.leftButton.isPressed)
            {
                if (first == null)
                {
                    var sites = new BuildSite[count];
                    for (int i = 0; i < count; i++)
                        sites[i] = new BuildSite(_path[i].x, _path[i].y, LineDirection(i));
                    Order(simulation, sites);
                }
                else
                {
                    // The first cell decides (BuildCommand checks only it): start over.
                    _dragging = false;
                }
            }
            return tip;
        }

        private string UpdateBridge(Simulation simulation, Vector3 ground, Mouse mouse, bool overUi)
        {
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            string id = _stats!.Id;
            Vector2Int cursor = CursorCell(state, ground);
            int range = state.Rules.BridgeRange;

            if (_bridgeStart == null)
            {
                RejectReason? reason = PlacementPreview.Check(state, playerId, id, cursor.x, cursor.y, _rotation);
                PlaceTile(cursor.x, cursor.y, reason == null ? _okColor : _blockedColor);
                PlaceArrow(cursor.x, cursor.y, _rotation);
                if (mouse.leftButton.wasPressedThisFrame && !overUi && reason == null)
                    _bridgeStart = cursor;
                string tip = $"Мост — {Cost(_stats)} за конец · дальность {range}\nПервый конец";
                return reason != null ? tip + "\n" + DisplayNames.Reject(reason.Value, id) : tip;
            }

            Vector2Int start = _bridgeStart.Value;
            bool inRange = PlacementPreview.InBridgeRange(state, start.x, start.y, cursor.x, cursor.y);
            Direction direction = inRange ? DirectionOf(cursor - start) : _rotation;
            RejectReason? end = PlacementPreview.Check(state, playerId, id, cursor.x, cursor.y, direction);
            bool ok = inRange && end == null;

            PlaceTile(start.x, start.y, _okColor);
            PlaceArrow(start.x, start.y, direction);
            if (inRange)
            {
                Vector2Int step = cursor - start;
                step = new Vector2Int(System.Math.Sign(step.x), System.Math.Sign(step.y));
                for (Vector2Int c = start + step; c != cursor; c += step)
                    PlaceTile(c.x, c.y, _spanColor, 0.4f);
            }
            PlaceTile(cursor.x, cursor.y, ok ? _okColor : _blockedColor);
            PlaceArrow(cursor.x, cursor.y, direction);

            string text = $"Мост — 2 × {_stats.CostPrimary} = {2 * _stats.CostPrimary}\nВторой конец";
            if (!inRange)
                text += $"\nНа одной прямой, не дальше {range} клеток";
            else if (end != null)
                text += "\n" + DisplayNames.Reject(end.Value, id);

            if (mouse.leftButton.wasPressedThisFrame && !overUi && ok)
            {
                Order(simulation, new[] { new BuildSite(start.x, start.y, direction), new BuildSite(cursor.x, cursor.y, direction) });
            }
            return text;
        }

        // Lines and bridges stay on the map even when the mouse leaves it.
        private static Vector2Int CursorCell(SimState state, Vector3 ground) =>
            new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(ground.x), 0, state.Map.Width - 1),
                Mathf.Clamp(Mathf.FloorToInt(ground.z), 0, state.Map.Height - 1));

        // The line follows the mouse cell by cell: back over its own cells it shortens, otherwise
        // it walks towards the cursor along the longer axis first, so a turn appears where the mouse turned.
        private void Extend(Vector2Int cursor)
        {
            int index = _path.IndexOf(cursor);
            if (index >= 0)
            {
                _path.RemoveRange(index + 1, _path.Count - index - 1);
                return;
            }
            while (_path[_path.Count - 1] != cursor && _path.Count < BuildCommand.MaxSites)
            {
                Vector2Int last = _path[_path.Count - 1];
                Vector2Int delta = cursor - last;
                Vector2Int next = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                    ? last + new Vector2Int(System.Math.Sign(delta.x), 0)
                    : last + new Vector2Int(0, System.Math.Sign(delta.y));
                int seen = _path.IndexOf(next);
                if (seen >= 0)
                {
                    _path.RemoveRange(seen + 1, _path.Count - seen - 1);
                    continue;
                }
                _path.Add(next);
            }
        }

        // Each cell looks at the next one; the last keeps the direction of the drag
        // (a single cell uses the R rotation).
        private Direction LineDirection(int i)
        {
            if (_path.Count == 1)
                return _rotation;
            return i < _path.Count - 1 ? DirectionOf(_path[i + 1] - _path[i]) : DirectionOf(_path[i] - _path[i - 1]);
        }

        private static Direction DirectionOf(Vector2Int delta)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                return delta.x >= 0 ? Direction.Right : Direction.Left;
            return delta.y > 0 ? Direction.Up : Direction.Down;
        }

        // Shift queues the building and keeps the mode for the next one; otherwise placing ends it.
        private void Order(Simulation simulation, BuildSite[] sites)
        {
            bool queued = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            simulation.Enqueue(new BuildCommand(_localPlayer!.PlayerId, simulation.State.Tick, _workers.ToArray(), _stats!.Id, sites, queued));
            if (!queued)
            {
                End();
                return;
            }
            _dragging = false;
            _bridgeStart = null;
            _path.Clear();
        }

        private bool CollectWorkers(SimState state)
        {
            _workers.Clear();
            foreach (int id in _selection!.Selected)
            {
                Unit? unit = state.FindUnit(id);
                if (unit != null && unit.Worker != null)
                    _workers.Add(id);
            }
            return _workers.Count > 0;
        }

        // creep-camp.md: placing inside a live camp's aggro radius is allowed, with a warning.
        // Distance to the nearest point of the footprint, as the sim measures it for buildings.
        private static bool InCampAggro(SimState state, int playerId, int x, int y, int w, int h)
        {
            foreach (CreepCamp camp in state.CreepCamps)
            {
                if (camp.State == CreepCampState.Cleared || !state.Fog.IsDiscovered(playerId, camp.CenterX, camp.CenterY))
                    continue;
                long dx = camp.CenterMilliX - Mathf.Clamp(camp.CenterMilliX, Grid.CellToMilli(x), Grid.CellToMilli(x + w));
                long dy = camp.CenterMilliY - Mathf.Clamp(camp.CenterMilliY, Grid.CellToMilli(y), Grid.CellToMilli(y + h));
                long r = camp.Stats.AggroRadiusMilli;
                if (dx * dx + dy * dy <= r * r)
                    return true;
            }
            return false;
        }

        private static string Cost(BuildingStats stats) =>
            stats.CostSecondary > 0 ? $"{stats.CostPrimary} + {stats.CostSecondary}" : $"{stats.CostPrimary}";

        private void PlaceTile(int x, int y, Color color, float size = 0.9f)
        {
            Transform tile = Pooled(_tiles, ref _tilesUsed, "GhostTile");
            tile.localScale = new Vector3(size, 0.04f, size);
            tile.localPosition = new Vector3(x + 0.5f, 0.03f, y + 0.5f);
            tile.localRotation = Quaternion.identity;
            tile.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
        }

        // A bar from the cell centre towards its exit side.
        private void PlaceArrow(int x, int y, Direction direction)
        {
            Transform arrow = Pooled(_arrows, ref _arrowsUsed, "GhostArrow");
            float yaw = direction switch { Direction.Right => 90f, Direction.Up => 0f, Direction.Left => 270f, _ => 180f };
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            arrow.localRotation = rotation;
            arrow.localScale = new Vector3(0.18f, 0.06f, 0.45f);
            arrow.localPosition = new Vector3(x + 0.5f, 0.07f, y + 0.5f) + rotation * new Vector3(0f, 0f, 0.18f);
            arrow.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(Color.white);
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

        private void HideUnused()
        {
            for (int i = _tilesUsed; i < _tiles.Count; i++)
                _tiles[i].gameObject.SetActive(false);
            for (int i = _arrowsUsed; i < _arrows.Count; i++)
                _arrows[i].gameObject.SetActive(false);
        }

        private void HideAll()
        {
            _tilesUsed = _arrowsUsed = 0;
            HideUnused();
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
