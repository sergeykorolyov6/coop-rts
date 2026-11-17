using System.Collections.Generic;
using Game.Net;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Placing a building chosen in the build menu; every silhouette goes to the local player's
    // builder, whatever is selected (build-mode.md).
    // One building: a silhouette follows the cursor, green / red / yellow (inside a live creep
    // camp's aggro radius, creep-camp.md), a drill shows its ore cells and rate (drill.md); click places.
    // Conveyor and wall: press and drag a line from cell to cell, arrows show the direction of every
    // belt cell, the tip shows the total cost (conveyors.md). Bridge (the separate tool): first end,
    // then second end on a straight line within range. R rotates a single cell or bridge.
    // A drill: a click places one, a press dragged to another cell lays drills over the frame
    // (DrillFill, drill-fill.md).
    // The mode stays on for the next silhouette until right click or Esc (or a headquarters that can
    // be only one); a building joins the builder's build, or replaces what else it does — Shift
    // always queues it after everything ordered (order-queue.md).
    // Lines (belts, walls) are planned by LinePlanner (smart-line.md): one bend, bridges over what is
    // in the way, the end sticking to a receiver; belt cells over own belts show blue — they are turned,
    // not built (relay-and-demolish.md). The demolition mode drags a frame and takes down everything
    // own inside (DemolishAreaCommand).
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class PlacementController : MonoBehaviour
    {
        private enum Mode { None, Single, Line, Bridge, Demolish }

        [SerializeField] private RtsCamera? _camera;
        [SerializeField] private Hud? _hud;
        [SerializeField] private Color _okColor = new Color(0.30f, 0.85f, 0.40f);
        [Tooltip("Drill footprint cells that are ore.")]
        [SerializeField] private Color _oreColor = new Color(0.65f, 1.00f, 0.55f);
        [SerializeField] private Color _blockedColor = new Color(0.90f, 0.25f, 0.20f);
        [SerializeField] private Color _warningColor = new Color(0.95f, 0.80f, 0.20f);
        [SerializeField] private Color _spanColor = new Color(0.55f, 0.65f, 0.80f);
        [Tooltip("Belt cells over own belts: turned, free (relay-and-demolish.md).")]
        [SerializeField] private Color _relayColor = new Color(0.35f, 0.75f, 1.00f);
        [SerializeField] private Color _demolishColor = new Color(0.95f, 0.20f, 0.15f);
        [Tooltip("Line cells already there (smart-line.md): nothing is ordered on them.")]
        [SerializeField] private Color _skipColor = new Color(0.55f, 0.55f, 0.55f);

        private readonly List<(string TypeId, BuildSite[] Sites)> _orders = new List<(string, BuildSite[])>();
        private readonly List<BuildSite> _segment = new List<BuildSite>();

        // A replacing order is on its way through the input delay (network.md): until the builder
        // shows it, further silhouettes join it instead of replacing it.
        private static readonly int PendingTicks = LockstepSettings.TurnTicks * (LockstepSettings.DelayTurns + 2);

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private GroundMarks? _marks;

        private Mode _mode;
        private BuildingStats? _stats;
        private Direction _rotation = Direction.Right;
        // A press that is held: a line, a frame of drills or of demolition grows from _pressCell.
        private bool _dragging;
        private Vector2Int _pressCell;
        private Vector2Int? _bridgeStart;
        // smart-line.md: which axis the line takes first (R flips it while dragging).
        private bool _axisChosen;
        private bool _horizontalFirst = true;
        // drill-fill.md: the drill a plain click would place, and whether the press became a frame.
        private BuildSite _pressSite;
        private bool _filling;
        private int _endedFrame = -1;
        private int _replaceSentTick = -1;

        public bool IsActive => _mode != Mode.None;

        // Also true on the frame the mode ended: the click that placed or cancelled must not
        // select or order anything.
        public bool IsBusy => IsActive || _endedFrame == Time.frameCount;

        public string? BuildingId => _stats?.Id;

        // relay-and-demolish.md: the demolition frame is being chosen.
        public bool IsDemolishing => _mode == Mode.Demolish;

        // Cells the single-building silhouette covers this frame, null when there is none
        // (turret.md: the range ring is drawn around it).
        public RectInt? Footprint { get; private set; }

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
            _marks = new GroundMarks(transform, "Placement");
            // Another player may be of another faction.
            _localPlayer.Changed += End;
        }

        public void Begin(string buildingId)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _runner.IsReplay || !simulation.State.Data.TryGetBuilding(buildingId, out BuildingStats? stats))
                return;
            _stats = stats;
            _mode = stats!.LinePlacement ? Mode.Line
                : stats.Behavior == BuildingBehavior.Bridge ? Mode.Bridge
                : Mode.Single;
            ResetPress();
        }

        public void Cancel() => End();

        // relay-and-demolish.md: Delete without an own building selected, or the menu's button.
        public void BeginDemolish()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _runner.IsReplay)
                return;
            _stats = null;
            _mode = Mode.Demolish;
            ResetPress();
        }

        private void ResetPress()
        {
            _dragging = false;
            _filling = false;
            _bridgeStart = null;
        }

        private void End()
        {
            if (_mode == Mode.None)
                return;
            _mode = Mode.None;
            _stats = null;
            Footprint = null;
            ResetPress();
            _endedFrame = Time.frameCount;
            _marks!.Clear();
            _hud?.ShowTip(null, Vector2.zero);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_mode == Mode.None || simulation == null || _camera == null)
                return;
            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;
            if (mouse == null || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || mouse.rightButton.wasPressedThisFrame)
            {
                End();
                return;
            }
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                // smart-line.md: while a line is dragged R puts the bend on the other side.
                if (_mode == Mode.Line && _dragging)
                {
                    _horizontalFirst = !_horizontalFirst;
                    _axisChosen = true;
                }
                else
                    _rotation = (Direction)(((int)_rotation + 1) % 4);
            }

            Vector2 pointer = mouse.position.ReadValue();
            _marks!.Begin();
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
                    case Mode.Demolish: tip = UpdateDemolish(simulation, ground, mouse, overUi); break;
                }
            }
            if (_mode == Mode.None)
                return;
            // build-mode.md, fail cases: the silhouette shows, the click sends nothing.
            if (tip != null && _mode != Mode.Demolish && simulation.State.FindBuilder(_localPlayer!.PlayerId) == null)
                tip += "\n" + BuilderAway(simulation.State, _localPlayer.PlayerId);
            _marks.End();
            _hud?.ShowTip(tip, pointer);
        }

        private string UpdateSingle(Simulation simulation, Vector3 ground, Mouse mouse, bool overUi)
        {
            SimState state = simulation.State;
            BuildingStats stats = _stats!;
            int playerId = _localPlayer!.PlayerId;
            if (stats.Behavior == BuildingBehavior.Drill)
            {
                string? fill = UpdateDrillFill(simulation, ground, mouse, overUi);
                if (fill != null)
                    return fill;
            }
            // The cursor is the footprint's centre.
            int x = Mathf.FloorToInt(ground.x - (stats.SizeW - 1) * 0.5f);
            int y = Mathf.FloorToInt(ground.z - (stats.SizeH - 1) * 0.5f);
            Footprint = new RectInt(x, y, stats.SizeW, stats.SizeH);

            RejectReason? reason = PlacementPreview.Check(state, playerId, stats.Id, x, y, _rotation);
            bool nearCamp = reason == null && InCampAggro(state, playerId, x, y, stats.SizeW, stats.SizeH);
            Color color = reason != null ? _blockedColor : nearCamp ? _warningColor : _okColor;
            bool drill = stats.Behavior == BuildingBehavior.Drill;
            // heights.md: on uneven ground only the cells that spoil the site are red — a ramp, or a
            // level other than the one most of the site stands on; the rest warn.
            bool uneven = reason == RejectReason.UnevenGround || reason == RejectReason.OnRamp;
            int siteLevel = uneven ? CommonLevel(state.Map, x, y, stats.SizeW, stats.SizeH) : 0;
            for (int cy = y; cy < y + stats.SizeH; cy++)
            {
                for (int cx = x; cx < x + stats.SizeW; cx++)
                {
                    bool ore = drill && reason == null && state.Map.InBounds(cx, cy) && state.Map.OreAt(cx, cy) != MapSetup.NoOre;
                    Color tile = ore ? _oreColor : color;
                    if (uneven && state.Map.InBounds(cx, cy))
                        tile = state.Map.IsRamp(cx, cy) || state.Map.LevelAt(cx, cy) != siteLevel ? _blockedColor : _warningColor;
                    PlaceTile(cx, cy, tile);
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
            if (reason == null)
                tip += Shortfall(state, playerId, stats.Id, 1);
            if (nearCamp)
                tip += "\nРядом лагерь крипов: нападут";
            if (drill)
                tip += "\nКлик — один бур · протяните — буры рамкой";

            // A drill waits for the release: the press may become a frame (UpdateDrillFill).
            if (mouse.leftButton.wasPressedThisFrame && !overUi && reason == null && !drill)
            {
                Order(simulation, _stats!.Id, new[] { new BuildSite(x, y, _rotation) });
            }
            return tip;
        }

        // drill-fill.md: the press remembers the click's drill; once the cursor leaves the press cell
        // with the button held, the frame takes over (null is returned until then: the single silhouette
        // shows). Released — the click's drill or every drill of the frame, in one order.
        private string? UpdateDrillFill(Simulation simulation, Vector3 ground, Mouse mouse, bool overUi)
        {
            SimState state = simulation.State;
            BuildingStats stats = _stats!;
            int playerId = _localPlayer!.PlayerId;
            Vector2Int cursor = CursorCell(state, ground);
            if (mouse.leftButton.wasPressedThisFrame && !overUi)
            {
                _dragging = true;
                _filling = false;
                _pressCell = cursor;
                int px = Mathf.FloorToInt(ground.x - (stats.SizeW - 1) * 0.5f), py = Mathf.FloorToInt(ground.z - (stats.SizeH - 1) * 0.5f);
                _pressSite = new BuildSite(px, py, _rotation);
            }
            if (!_dragging)
                return null;
            if (cursor != _pressCell)
                _filling = true;

            if (!mouse.leftButton.isPressed)
            {
                _dragging = false;
                if (_filling)
                {
                    DrillFillPlan released = DrillFill.Plan(state, playerId, stats.Id, _pressCell.x, _pressCell.y, cursor.x, cursor.y);
                    if (released.Sites.Count > 0)
                    {
                        var sites = new BuildSite[released.Sites.Count];
                        for (int i = 0; i < sites.Length; i++)
                            sites[i] = new BuildSite(released.Sites[i].X, released.Sites[i].Y, _rotation);
                        Order(simulation, stats.Id, sites);
                    }
                    _filling = false;
                    return null;
                }
                if (PlacementPreview.Check(state, playerId, stats.Id, _pressSite.CellX, _pressSite.CellY, _rotation) == null)
                    Order(simulation, stats.Id, new[] { _pressSite });
                return null;
            }
            if (!_filling)
                return null;

            _marks!.Frame(FrameTo(cursor), _spanColor, 0.35f, TileThickness, TileLift);

            DrillFillPlan plan = DrillFill.Plan(state, playerId, stats.Id, _pressCell.x, _pressCell.y, cursor.x, cursor.y);
            bool nearCamp = false;
            foreach (DrillFillCell site in plan.Sites)
            {
                bool camp = InCampAggro(state, playerId, site.X, site.Y, stats.SizeW, stats.SizeH);
                nearCamp |= camp;
                for (int cy = site.Y; cy < site.Y + stats.SizeH; cy++)
                    for (int cx = site.X; cx < site.X + stats.SizeW; cx++)
                        PlaceTile(cx, cy, state.Map.OreAt(cx, cy) != MapSetup.NoOre ? _oreColor : camp ? _warningColor : _okColor);
            }
            // Cells left out: too little ore — yellow; the place is not free — red.
            foreach (DrillFillCell skipped in plan.Skipped)
            {
                Color color = skipped.Kind == DrillFillCellKind.Poor ? _warningColor : _blockedColor;
                for (int cy = skipped.Y; cy < skipped.Y + stats.SizeH; cy++)
                    for (int cx = skipped.X; cx < skipped.X + stats.SizeW; cx++)
                        PlaceTile(cx, cy, color, 0.5f);
            }

            if (plan.Sites.Count == 0)
                return "В рамке нет места для бура";
            int count = plan.Sites.Count;
            string cost = stats.CostSecondary > 0 ? $"{count * stats.CostPrimary} + {count * stats.CostSecondary} втор." : $"{count * stats.CostPrimary}";
            string tip = $"Буров: {count} · руды под ними: {plan.OreTotal} · добыча ≈ {DisplayNames.PerSecond(plan.OreTotal, state.Rules.DrillTicksPerUnitPerOreCell)} · цена {cost}";
            Wallet wallet = state.GetPlayer(playerId).Wallet;
            int affordable = Mathf.Min(
                stats.CostPrimary > 0 ? wallet.Get(Game.Sim.Resources.Primary) / stats.CostPrimary : count,
                stats.CostSecondary > 0 ? wallet.Secondary / stats.CostSecondary : count);
            if (affordable < count)
                tip += $"\nСейчас хватает на {affordable} — остальные по мере дохода, начиная с самых богатых";
            if (plan.Skipped.Count > 0)
                tip += "\nЖёлтые — мало руды (кликом можно поставить), красные — место занято";
            if (nearCamp)
                tip += "\nРядом лагерь крипов: нападут";
            return tip;
        }

        // smart-line.md: from the press cell to the cursor with one bend (LinePlanner): new cells green,
        // already there grey, relaid blue, bridges where obstacles or a cliff are in the way, red where
        // the order stops; the end turns to a receiver beside it. Released — one order per stretch of
        // belt and per bridge, in the order of the path.
        private string UpdateLine(Simulation simulation, Vector3 ground, Mouse mouse, bool overUi)
        {
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            BuildingStats stats = _stats!;
            Vector2Int cursor = CursorCell(state, ground);
            if (!_dragging)
            {
                _pressCell = cursor;
                _axisChosen = false;
                if (mouse.leftButton.wasPressedThisFrame && !overUi)
                    _dragging = true;
            }
            else if (!_axisChosen && cursor != _pressCell)
            {
                // The axis the cursor first left the press cell by; diagonally — horizontal.
                Vector2Int delta = cursor - _pressCell;
                _horizontalFirst = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
                _axisChosen = true;
            }

            LinePlan plan = LinePlanner.Plan(state, playerId, stats.Id, _pressCell.x, _pressCell.y, cursor.x, cursor.y, _horizontalFirst, _rotation);
            bool belt = stats.Behavior == BuildingBehavior.Conveyor;
            int built = 0, relaid = 0, bridges = 0;
            foreach (LineCell cell in plan.Cells)
            {
                switch (cell.Kind)
                {
                    case LineCellKind.Build: PlaceTile(cell.X, cell.Y, _okColor); built++; break;
                    case LineCellKind.Skip: PlaceTile(cell.X, cell.Y, _skipColor, 0.6f); break;
                    case LineCellKind.Relay: PlaceTile(cell.X, cell.Y, _relayColor); relaid++; break;
                    case LineCellKind.BridgeIn: PlaceTile(cell.X, cell.Y, _okColor); bridges++; break;
                    case LineCellKind.BridgeOut: PlaceTile(cell.X, cell.Y, _okColor); break;
                    case LineCellKind.UnderBridge: PlaceTile(cell.X, cell.Y, _spanColor, 0.4f); break;
                    case LineCellKind.Broken: PlaceTile(cell.X, cell.Y, _blockedColor); break;
                }
                if (belt && cell.Kind != LineCellKind.UnderBridge && cell.Kind != LineCellKind.Broken)
                    PlaceArrow(cell.X, cell.Y, cell.Direction);
            }

            string cost = plan.CostSecondary > 0 ? $"{plan.CostPrimary} + {plan.CostSecondary} втор." : $"{plan.CostPrimary}";
            string tip = belt ? $"Лента: {built} · мостов: {bridges} · цена {cost}" : $"{DisplayNames.Building(stats.Id)}: {built} · цена {cost}";
            if (relaid > 0)
                tip += $"\nПереложить: {relaid} — бесплатно, голубые клетки";
            int affordable = Affordable(state, playerId, plan);
            if (affordable >= 0)
                tip += $"\nСейчас хватает на {affordable} — остальное по мере дохода";
            switch (plan.Stop)
            {
                case LineStop.FirstCell:
                    RejectReason? reason = PlacementPreview.Check(state, playerId, stats.Id, _pressCell.x, _pressCell.y, _rotation);
                    tip += "\n" + (reason != null ? DisplayNames.Reject(reason.Value, stats.Id) : "Отсюда линию не начать");
                    break;
                case LineStop.BridgeTooFar:
                    tip += $"\nМост не достаёт: пролёт {plan.BrokenSpan} кл., дальность {state.Rules.BridgeRange - 1}";
                    break;
                case LineStop.NoBridge:
                    tip += "\nДальше не провести: моста здесь не поставить (изгиб, своя лента или занятый конец)";
                    break;
                default:
                    if (!plan.HasOrders)
                        tip += belt ? "\nЛента уже есть" : "\nСтроить здесь нечего";
                    break;
            }
            if (plan.StuckToId != 0 && state.FindBuilding(plan.StuckToId) is Building stuck)
                tip += $"\nКонец прилипнет: {DisplayNames.Building(stuck.Stats.Id).ToLowerInvariant()}";
            if (plan.MergesIntoBelt)
                tip += "\nКонец входит в свою ленту — врежется в линию";
            tip += _dragging ? "\nR — изгиб с другой стороны"
                : belt ? "\nЗажмите и тяните · R — поворот\nРесурс едет по стрелкам: конец ленты направьте в штаб" : "\nЗажмите и тяните";

            if (_dragging && !mouse.leftButton.isPressed)
            {
                if (!plan.Blocked && plan.HasOrders)
                    Order(simulation, Orders(plan));
                else
                    _dragging = false;
            }
            return tip;
        }

        // How many of the plan's orders the wallet pays for now, in the order of the path;
        // -1 when it pays for all.
        private static int Affordable(SimState state, int playerId, LinePlan plan)
        {
            if (!state.Data.TryGetBuilding(plan.BuildingTypeId, out BuildingStats? line))
                return -1;
            BuildingStats? bridge = null;
            if (plan.BridgeTypeId != null)
                state.Data.TryGetBuilding(plan.BridgeTypeId, out bridge);
            Wallet wallet = state.GetPlayer(playerId).Wallet;
            int primary = wallet.Get(Game.Sim.Resources.Primary), secondary = wallet.Secondary, count = 0;
            if (plan.CostPrimary <= primary && plan.CostSecondary <= secondary)
                return -1;
            foreach (LineCell cell in plan.Cells)
            {
                BuildingStats? stats = cell.Kind == LineCellKind.Build ? line
                    : cell.Kind == LineCellKind.BridgeIn || cell.Kind == LineCellKind.BridgeOut ? bridge : null;
                if (stats == null)
                    continue;
                primary -= stats.CostPrimary;
                secondary -= stats.CostSecondary;
                if (primary < 0 || secondary < 0)
                    break;
                count++;
            }
            return count;
        }

        // Stretches of new and relaid cells as one order each, every bridge as its own, path order.
        private List<(string TypeId, BuildSite[] Sites)> Orders(LinePlan plan)
        {
            _orders.Clear();
            _segment.Clear();
            BuildSite? input = null;
            foreach (LineCell cell in plan.Cells)
            {
                var site = new BuildSite(cell.X, cell.Y, cell.Direction);
                switch (cell.Kind)
                {
                    case LineCellKind.Build:
                    case LineCellKind.Relay:
                        _segment.Add(site);
                        continue;
                    case LineCellKind.BridgeIn:
                        input = site;
                        break;
                    case LineCellKind.BridgeOut:
                        Flush(plan.BuildingTypeId);
                        if (input != null && plan.BridgeTypeId != null)
                            _orders.Add((plan.BridgeTypeId, new[] { input.Value, site }));
                        input = null;
                        continue;
                }
                Flush(plan.BuildingTypeId);
            }
            Flush(plan.BuildingTypeId);
            return _orders;
        }

        private void Flush(string typeId)
        {
            if (_segment.Count == 0)
                return;
            _orders.Add((typeId, _segment.ToArray()));
            _segment.Clear();
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

            string text = $"Мост — 2 × {_stats.CostPrimary} = {2 * _stats.CostPrimary}\nВторой конец" + Shortfall(state, playerId, id, 2);
            if (!inRange)
                text += $"\nНа одной прямой, не дальше {range} клеток";
            else if (end == null && !state.Map.LevelsJoin(start.x, start.y, cursor.x, cursor.y))
                text += "\nМост перенесёт ресурс через обрыв";
            else if (end != null)
                text += "\n" + DisplayNames.Reject(end.Value, id);

            if (mouse.leftButton.wasPressedThisFrame && !overUi && ok)
            {
                Order(simulation, id, new[] { new BuildSite(start.x, start.y, direction), new BuildSite(cursor.x, cursor.y, direction) });
            }
            return text;
        }

        // relay-and-demolish.md: the frame from the press to the cursor; on release everything own in it
        // starts going. The frame's border is red, what will go is covered red.
        private string UpdateDemolish(Simulation simulation, Vector3 ground, Mouse mouse, bool overUi)
        {
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            Vector2Int cursor = CursorCell(state, ground);
            if (!_dragging)
            {
                _pressCell = cursor;
                if (mouse.leftButton.wasPressedThisFrame && !overUi)
                    _dragging = true;
            }
            RectInt frame = FrameTo(cursor);
            int minX = frame.xMin, minY = frame.yMin, maxX = frame.xMax - 1, maxY = frame.yMax - 1;
            _marks!.Frame(frame, _demolishColor, 0.5f, TileThickness, TileLift);

            int belts = 0, others = 0, refundPrimary = 0, refundSecondary = 0;
            bool headquarters = false;
            FactionStats? faction = state.GetPlayer(playerId).Faction;
            foreach (Building b in state.Buildings)
            {
                // The same question the command will ask (DemolishAreaCommand.Takes).
                if (!DemolishAreaCommand.Takes(b, playerId, minX, minY, maxX, maxY))
                {
                    headquarters |= !b.IsDead && b.OwnerId == playerId && b.Stats.Behavior == BuildingBehavior.Headquarters
                        && Grid.Touches(b.CellX, b.CellY, b.Width, b.Height, minX, minY, maxX, maxY);
                    continue;
                }
                float top = PrimitiveModels.BuildingHeight(b.Stats) + 0.05f;
                for (int y = b.CellY; y < b.CellY + b.Height; y++)
                    for (int x = b.CellX; x < b.CellX + b.Width; x++)
                        PlaceTile(x, y, _demolishColor, 0.95f, top);
                if (b.Stats.IsBelt)
                    belts++;
                else
                    others++;
                (int primary, int secondary) = DemolishSystem.Refund(faction, b.Stats);
                refundPrimary += primary;
                refundSecondary += secondary;
            }

            string tip = belts + others == 0 ? "Снос: протяните рамку по своим зданиям"
                : $"Снести: лент {belts}, зданий {others} · вернётся {refundPrimary}{(refundSecondary > 0 ? $" + {refundSecondary} втор." : "")}";
            if (headquarters)
                tip += "\nШтаб рамкой не сносится — только кнопкой на его панели";
            tip += "\nСилуэты и призраки разрушенного в рамке тоже снимутся · ПКМ / Esc — закончить";

            if (_dragging && !mouse.leftButton.isPressed)
            {
                _runner!.Submit(new DemolishAreaCommand(playerId, state.Tick, minX, minY, maxX, maxY));
                _dragging = false;
            }
            return tip;
        }

        // The level most cells of the footprint stand on (ties: the lower one).
        private static int CommonLevel(MapState map, int x, int y, int w, int h)
        {
            var counts = new int[MapSetup.MaxLevel + 1];
            for (int cy = y; cy < y + h; cy++)
            {
                for (int cx = x; cx < x + w; cx++)
                {
                    if (map.InBounds(cx, cy) && !map.IsRamp(cx, cy))
                        counts[map.LevelAt(cx, cy)]++;
                }
            }
            int best = 0;
            for (int level = 1; level < counts.Length; level++)
            {
                if (counts[level] > counts[best])
                    best = level;
            }
            return best;
        }

        // Lines and bridges stay on the map even when the mouse leaves it.
        private static Vector2Int CursorCell(SimState state, Vector3 ground) =>
            new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(ground.x), 0, state.Map.Width - 1),
                Mathf.Clamp(Mathf.FloorToInt(ground.z), 0, state.Map.Height - 1));

        // The cells between the press and the cursor, both included.
        private RectInt FrameTo(Vector2Int cursor)
        {
            int minX = Mathf.Min(_pressCell.x, cursor.x), minY = Mathf.Min(_pressCell.y, cursor.y);
            return new RectInt(minX, minY, Mathf.Abs(cursor.x - _pressCell.x) + 1, Mathf.Abs(cursor.y - _pressCell.y) + 1);
        }

        private static Direction DirectionOf(Vector2Int delta)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                return delta.x >= 0 ? Direction.Right : Direction.Left;
            return delta.y > 0 ? Direction.Up : Direction.Down;
        }

        // build-mode.md: who gets an order from the build menu, and how. The local player's builder;
        // the order joins its build (or starts at once when it is idle), replaces whatever else it does,
        // and with Shift goes after everything ordered. Null when there is no builder. Asked once per
        // order sent: a replacing order is remembered for the input delay.
        public Unit? BuilderForOrder(SimState state, out bool queued)
        {
            queued = false;
            Unit? builder = state.FindBuilder(_localPlayer!.PlayerId);
            if (builder == null)
                return null;
            UnitOrderKind kind = builder.Order.Kind;
            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            bool pending = _replaceSentTick >= 0 && state.Tick - _replaceSentTick <= PendingTicks
                && kind != UnitOrderKind.Build;
            queued = shift || pending || kind == UnitOrderKind.Build || kind == UnitOrderKind.Idle;
            if (!queued)
                _replaceSentTick = state.Tick;
            else if (!pending)
                _replaceSentTick = -1;
            return builder;
        }

        // The silhouette goes to the builder; the mode stays for the next one.
        private void Order(Simulation simulation, string typeId, BuildSite[] sites)
        {
            _orders.Clear();
            _orders.Add((typeId, sites));
            Order(simulation, _orders);
        }

        // smart-line.md: a line goes as several orders on one tick; the first by the builder's rule,
        // the rest after it (Queued), so they are built in the order of the path.
        private void Order(Simulation simulation, List<(string TypeId, BuildSite[] Sites)> orders)
        {
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            bool queued = false;
            Unit? builder = orders.Count > 0 ? BuilderForOrder(state, out queued) : null;
            if (builder != null)
            {
                for (int i = 0; i < orders.Count; i++)
                    _runner!.Submit(new BuildCommand(playerId, state.Tick, new[] { builder.Id }, orders[i].TypeId, orders[i].Sites, queued || i > 0));
            }
            ResetPress();
            // There can be one headquarters only: nothing more to place.
            if (_stats!.Behavior == BuildingBehavior.Headquarters && state.GetPlayer(playerId).Faction?.SingleHeadquarters == true)
                End();
        }

        // The dead builder comes back at the headquarters (builder.md); without one it does not.
        private static string BuilderAway(SimState state, int playerId)
        {
            int ticks = state.GetPlayer(playerId).BuilderRespawnTick - state.Tick;
            if (ticks <= 0)
                return "Строителя нет — некому строить";
            int seconds = (ticks + Simulation.TicksPerSecond - 1) / Simulation.TicksPerSecond;
            return $"Строитель вернётся через {seconds} с";
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

        // builder-beam.md: money is no reason to refuse — the silhouette waits; the tip says how much is missing.
        private static string Shortfall(SimState state, int playerId, string buildingId, int count)
        {
            (int primary, int secondary) = PlacementPreview.Shortfall(state, playerId, buildingId);
            if (count > 1 && state.Data.TryGetBuilding(buildingId, out BuildingStats? stats))
            {
                Wallet wallet = state.GetPlayer(playerId).Wallet;
                primary = Mathf.Max(0, count * stats!.CostPrimary - wallet.Get(Game.Sim.Resources.Primary));
                secondary = Mathf.Max(0, count * stats.CostSecondary - wallet.Secondary);
            }
            if (primary == 0 && secondary == 0)
                return "";
            string missing = secondary > 0 ? (primary > 0 ? $"{primary} + {secondary} втор." : $"{secondary} втор.") : $"{primary}";
            return $"\nНе хватает {missing}: построится, когда накопится";
        }

        private static string Cost(BuildingStats stats) =>
            stats.CostSecondary > 0 ? $"{stats.CostPrimary} + {stats.CostSecondary}" : $"{stats.CostPrimary}";

        private const float TileThickness = 0.04f, TileLift = 0.03f;

        // lift: over a building's top, so the mark is seen on it (relay-and-demolish.md: what goes is covered red).
        private void PlaceTile(int x, int y, Color color, float size = 0.9f, float lift = TileLift) =>
            _marks!.Tile(x, y, color, size, TileThickness, lift);

        // A bar from the cell centre towards its exit side.
        private void PlaceArrow(int x, int y, Direction direction) =>
            _marks!.Arrow(x, y, direction, Color.white, new Vector3(0.18f, 0.06f, 0.45f), 0.07f);

        private void OnDestroy()
        {
            if (_localPlayer != null)
                _localPlayer.Changed -= End;
            _marks?.Destroy();
        }
    }
}
