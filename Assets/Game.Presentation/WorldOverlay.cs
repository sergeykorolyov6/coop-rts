using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // UI pinned to things on the map, drawn on the HUD panel:
    // - HP bar over damaged units and buildings (combat.md);
    // - build bar over unfinished buildings: progress, how many build, "frozen" (drill.md);
    // - buffer of own and ally drills, and "nowhere to give" when it is full and the drill stands (drill.md);
    // - "+5" rising over a headquarters on every delivery, in the carrier's colour (manual-mining.md);
    // - a red "!" over an alerted creep camp (creep-camp.md);
    // - capture progress over a neutral building, "guarded" while its camp lives (neutral-buildings.md);
    // - demolition countdown (demolish.md), a factory's stock (factory.md M2).
    // Only what the local player sees through the fog.
    [RequireComponent(typeof(SimRunner), typeof(EntityView), typeof(LocalPlayer))]
    public sealed class WorldOverlay : MonoBehaviour
    {
        private const float FloatSeconds = 1.6f;

        // A bar with its label under it, pixels: the hero's level bar sits this high over its HP bar.
        private const float LabelledBarHeight = 32f;

        [SerializeField] private Hud? _hud;
        [SerializeField] private Camera? _camera;
        [SerializeField] private Color _hpHigh = new Color(0.35f, 0.85f, 0.35f);
        [SerializeField] private Color _hpLow = new Color(0.90f, 0.25f, 0.20f);
        [SerializeField] private Color _buildColor = new Color(0.95f, 0.80f, 0.30f);
        [SerializeField] private Color _frozenColor = new Color(0.55f, 0.75f, 0.95f);
        [SerializeField] private Color _bufferColor = new Color(0.45f, 0.70f, 0.95f);
        [SerializeField] private Color _stoppedColor = new Color(1.00f, 0.55f, 0.25f);
        [SerializeField] private Color _captureColor = new Color(0.95f, 0.95f, 0.90f);
        [SerializeField] private Color _demolishColor = new Color(0.95f, 0.35f, 0.30f);
        [SerializeField] private Color _xpColor = new Color(0.98f, 0.85f, 0.30f);

        private readonly List<Bar> _bars = new List<Bar>();
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly List<Label> _alerts = new List<Label>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private LocalPlayer? _localPlayer;
        private VisualElement? _layer;
        private int _barsUsed;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void OnEnable() => _runner!.TickEvents += OnTickEvents;

        private void OnDisable() => _runner!.TickEvents -= OnTickEvents;

        private void Start()
        {
            if (_hud?.Root == null)
                return;
            // Under every panel: bars never cover buttons.
            _layer = new VisualElement { name = "WorldOverlay", pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = _layer.style.top = _layer.style.right = _layer.style.bottom = 0;
            _hud.Root.Insert(0, _layer);
        }

        // After EntityView has placed the units for this frame.
        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _layer?.panel == null || _camera == null)
                return;
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            _barsUsed = 0;

            foreach (Unit unit in state.Units)
            {
                if (unit.IsDead || unit.Hp >= unit.MaxHp || !Visibility.UnitShown(state, playerId, unit)
                    || !_entities!.TryGetUnitPosition(unit.Id, out Vector3 ground))
                    continue;
                float size = unit.Stats.SizeMilli / (float)Grid.MilliPerCell;
                ShowBar(ground + Vector3.up * (size * 1.4f + 0.2f), 34f, unit.Hp / (float)unit.MaxHp, HpColor(unit), null);
            }

            // factions.md: the hero's level and the way to the next one, over its head.
            foreach (Unit unit in state.Units)
            {
                if (unit.IsDead || !unit.IsHero || !Visibility.UnitShown(state, playerId, unit)
                    || !_entities!.TryGetUnitPosition(unit.Id, out Vector3 ground))
                    continue;
                Player owner = state.GetPlayer(unit.OwnerId);
                IReadOnlyList<HeroLevelStats> levels = state.Data.HeroLevels;
                int level = owner.HeroLevel;
                bool top = level >= levels.Count;
                float share = top ? 1f : Mathf.InverseLerp(levels[level - 1].Xp, levels[level].Xp, owner.HeroXp);
                float size = unit.Stats.SizeMilli / (float)Grid.MilliPerCell;
                // Anchored where the HP bar is and lifted in pixels: the label never covers the HP bar at any zoom.
                ShowBar(ground + Vector3.up * (size * 1.4f + 0.2f), 44f, share, _xpColor, top ? $"ур. {level} (макс.)" : $"ур. {level}", LabelledBarHeight);
            }

            foreach (Building building in state.Buildings)
            {
                if (building.IsDead || !Visibility.BuildingShown(state, playerId, building))
                    continue;
                var top = new Vector3(building.CellX + building.Width * 0.5f, 2.3f, building.CellY + building.Height * 0.5f);
                float width = 40f + 16f * building.Width;
                if (!building.IsComplete)
                {
                    bool frozen = building.State == BuildState.Frozen;
                    float progress = building.BuildWork > 0 ? building.BuildProgress / (float)building.BuildWork : 1f;
                    string label = frozen ? "заморожена" : $"строят: {building.BuildersLastTick}";
                    ShowBar(top, width, progress, frozen ? _frozenColor : _buildColor, label);
                }
                else if (building.Hp < building.MaxHp)
                {
                    ShowBar(top, width, building.Hp / (float)building.MaxHp, HpColor(building), null);
                }

                if (building.OwnerId == state.NeutralPlayerId && building.Stats.Behavior != BuildingBehavior.Lighthouse)
                {
                    if (state.IsGuarded(building))
                        ShowBar(top, width, 0f, _captureColor, "охраняется крипами");
                    else if (building.CaptureProgress > 0 && state.Rules.CaptureTimeTicks > 0)
                        ShowBar(top, width, building.CaptureProgress / (float)state.Rules.CaptureTimeTicks, _captureColor, $"захват: игрок {building.CapturePlayerId}");
                    continue;
                }
                if (building.DemolishTicksLeft > 0 && state.Relations.IsAlly(playerId, building.OwnerId))
                {
                    int total = DemolishSystem.DemolishTicks(state.Rules, building.Stats);
                    ShowBar(top + Vector3.up * 0.5f, width, building.DemolishTicksLeft / (float)total, _demolishColor, $"снос: {DisplayNames.Seconds(building.DemolishTicksLeft)}");
                }
                Wallet? stock = building.Stock;
                if (stock != null && building.IsComplete && building.OwnerId == playerId)
                {
                    int cap = System.Math.Max(1, state.Rules.FactoryStock);
                    int primary = stock.Get(Game.Sim.Resources.Primary);
                    // factions.md: an ammo turret shows its shells, a factory its stock of both resources.
                    string text = building.Stats.Ammo > 0
                        ? (primary < building.Stats.Ammo ? "нет боеприпасов — подведите ленту" : $"боеприпасы {primary}")
                        : $"склад {primary} · {stock.Secondary}";
                    ShowBar(top + Vector3.down * 0.9f, width, primary / (float)cap,
                        building.Stats.Ammo > 0 && primary < building.Stats.Ammo ? _stoppedColor : _bufferColor, text);
                }

                Drill? drill = building.Drill;
                if (drill != null && building.IsComplete && state.Relations.IsAlly(playerId, building.OwnerId))
                {
                    int capacity = state.Rules.DrillBuffer;
                    string label = drill.IsStopped ? "полон — некуда отдавать" : $"буфер {drill.Buffer}/{capacity}";
                    ShowBar(top + Vector3.down * 0.9f, width, drill.Buffer / (float)capacity, drill.IsStopped ? _stoppedColor : _bufferColor, label);
                }
            }

            for (int i = _barsUsed; i < _bars.Count; i++)
                _bars[i].Root.style.display = DisplayStyle.None;

            for (int i = 0; i < state.CreepCamps.Count; i++)
            {
                CreepCamp camp = state.CreepCamps[i];
                if (_alerts.Count <= i)
                    _alerts.Add(CreateAlert());
                var centre = new Vector3(camp.CenterMilliX / (float)Grid.MilliPerCell, 2.6f, camp.CenterMilliY / (float)Grid.MilliPerCell);
                bool alerted = camp.State == CreepCampState.Alerted && state.Fog.IsDiscovered(playerId, camp.CenterX, camp.CenterY)
                    && _camera.WorldToViewportPoint(centre).z > 0f;
                _alerts[i].style.display = alerted ? DisplayStyle.Flex : DisplayStyle.None;
                if (alerted)
                {
                    Place(_alerts[i], centre, 24f);
                    // Pulses so the eye finds it.
                    _alerts[i].style.opacity = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
                }
            }

            UpdateFloaters();
        }

        private void OnTickEvents(IReadOnlyList<SimEvent> events)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _layer == null)
                return;
            foreach (SimEvent e in events)
            {
                if (e is not CargoDelivered delivered)
                    continue;
                Building? hq = simulation.State.FindBuilding(delivered.HeadquartersId);
                if (hq == null || !simulation.State.Fog.IsVisible(_localPlayer!.PlayerId, hq.CellX, hq.CellY))
                    continue;
                var label = new Label($"+{delivered.Amount}") { pickingMode = PickingMode.Ignore };
                label.style.position = Position.Absolute;
                label.style.fontSize = 20;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = _entities!.OwnerColor(simulation.State, delivered.CarrierOwnerId);
                _layer.Add(label);
                var start = new Vector3(hq.CellX + hq.Width * 0.5f, 2.2f, hq.CellY + hq.Height * 0.5f);
                _floaters.Add(new Floater(label, start, Time.unscaledTime));
            }
        }

        private void UpdateFloaters()
        {
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                Floater floater = _floaters[i];
                float t = (Time.unscaledTime - floater.Born) / FloatSeconds;
                if (t >= 1f)
                {
                    floater.Label.RemoveFromHierarchy();
                    _floaters.RemoveAt(i);
                    continue;
                }
                Place(floater.Label, floater.Start + Vector3.up * t * 1.2f, 0f);
                floater.Label.style.opacity = 1f - t * t;
            }
        }

        private void ShowBar(Vector3 world, float width, float share, Color color, string? label, float pixelsUp = 0f)
        {
            if (_camera!.WorldToViewportPoint(world).z <= 0f)
                return;
            Bar bar = _barsUsed < _bars.Count ? _bars[_barsUsed] : CreateBar();
            _barsUsed++;
            bar.Root.style.display = DisplayStyle.Flex;
            bar.Root.style.width = width;
            bar.Fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100f);
            bar.Fill.style.backgroundColor = color;
            bar.Label.style.display = label != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (label != null && bar.Label.text != label)
                bar.Label.text = label;
            Place(bar.Root, world, width, pixelsUp);
        }

        // Centred horizontally over the world point, its top there or pixelsUp higher.
        private void Place(VisualElement element, Vector3 world, float width, float pixelsUp = 0f)
        {
            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(_layer!.panel, world, _camera!);
            element.style.left = p.x - width * 0.5f;
            element.style.top = p.y - pixelsUp;
        }

        private Label CreateAlert()
        {
            var label = new Label("!") { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.width = 24;
            label.style.fontSize = 34;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.color = new Color(1f, 0.25f, 0.2f);
            label.style.unityTextOutlineWidth = 0.4f;
            label.style.unityTextOutlineColor = Color.black;
            _layer!.Add(label);
            return label;
        }

        private Bar CreateBar()
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = 7;
            track.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            track.style.borderTopWidth = track.style.borderBottomWidth = track.style.borderLeftWidth = track.style.borderRightWidth = 1;
            track.style.borderTopColor = track.style.borderBottomColor = track.style.borderLeftColor = track.style.borderRightColor = new Color(0f, 0f, 0f, 0.9f);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100f);
            track.Add(fill);
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.fontSize = 15;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            // A dark outline keeps white text readable over the bright owner colours.
            label.style.unityTextOutlineWidth = 0.3f;
            label.style.unityTextOutlineColor = Color.black;
            label.style.color = Color.white;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.paddingTop = label.style.paddingBottom = 0;
            label.style.marginTop = label.style.marginBottom = 0;
            root.Add(track);
            root.Add(label);
            _layer!.Add(root);
            var bar = new Bar(root, fill, label);
            _bars.Add(bar);
            return bar;
        }

        private Color HpColor(Entity entity) => Color.Lerp(_hpLow, _hpHigh, entity.Hp / (float)entity.MaxHp);

        private sealed class Bar
        {
            public Bar(VisualElement root, VisualElement fill, Label label)
            {
                Root = root;
                Fill = fill;
                Label = label;
            }

            public VisualElement Root { get; }
            public VisualElement Fill { get; }
            public Label Label { get; }
        }

        private sealed class Floater
        {
            public Floater(Label label, Vector3 start, float born)
            {
                Label = label;
                Start = start;
                Born = born;
            }

            public Label Label { get; }
            public Vector3 Start { get; }
            public float Born { get; }
        }
    }
}
