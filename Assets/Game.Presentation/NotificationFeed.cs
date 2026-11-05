using System.Collections.Generic;
using Game.Net;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Short messages from sim events for the local player, under the wallet: "place taken",
    // "drill attacked", "site frozen", "camp cleared"... Each fades after a few seconds; the same
    // message again while shown counts up (×3) instead of stacking.
    // network.md: "Игрок N вышел", "Рассинхронизация" and, while the lockstep stands, "Ожидание игроков…".
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class NotificationFeed : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        [SerializeField] private float _seconds = 4f;
        [SerializeField] private int _maxShown = 6;
        [SerializeField] private Color _infoColor = new Color(0.90f, 0.92f, 0.88f);
        [SerializeField] private Color _warningColor = new Color(0.98f, 0.82f, 0.35f);
        [SerializeField] private Color _alarmColor = new Color(1.00f, 0.45f, 0.40f);

        private readonly List<Entry> _entries = new List<Entry>();

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private VisualElement? _column;
        private Label? _waiting;
        private float _waitingSince = -1f;

        // A short stall is normal jitter; say something only when it lasts.
        private const float WaitingNoticeSeconds = 0.5f;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void OnEnable()
        {
            _runner!.TickEvents += OnTickEvents;
            _runner.NetNoticed += OnNetNotice;
        }

        private void OnDisable()
        {
            _runner!.TickEvents -= OnTickEvents;
            _runner.NetNoticed -= OnNetNotice;
        }

        private void OnNetNotice(NetNotice notice)
        {
            if (_runner!.Simulation == null)
                return;
            if (notice.Kind == NetNoticeKind.PlayerLeft)
                Show($"Игрок {notice.Slot} вышел — его база остаётся", _alarmColor);
            else if (notice.Kind == NetNoticeKind.Desync)
                Show($"Рассинхронизация на {DisplayNames.Clock(notice.Tick)} — игры у вас разошлись", _alarmColor);
        }

        private void Start()
        {
            if (_hud?.Root == null)
                return;
            _column = new VisualElement { name = "Notifications", pickingMode = PickingMode.Ignore };
            _column.style.position = Position.Absolute;
            _column.style.left = 12;
            _column.style.top = 44;
            _column.style.alignItems = Align.FlexStart;
            _hud.Root.Add(_column);
        }

        private void Update()
        {
            ShowWaiting();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - _entries[i].Shown;
                if (age >= _seconds)
                {
                    _entries[i].Label.RemoveFromHierarchy();
                    _entries.RemoveAt(i);
                }
                else if (age > _seconds - 1f)
                {
                    _entries[i].Label.style.opacity = _seconds - age;
                }
            }
        }

        private void OnTickEvents(IReadOnlyList<SimEvent> events)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null)
                return;
            SimState state = simulation.State;
            int me = _localPlayer!.PlayerId;

            foreach (SimEvent e in events)
            {
                switch (e)
                {
                    case CommandRejected rejected when rejected.PlayerId == me:
                        Show(DisplayNames.Reject(rejected.Reason), _warningColor);
                        break;
                    case MiningStopped stopped when stopped.PlayerId == me:
                        Show("Клетку руды заняли — строитель остановился", _warningColor);
                        break;
                    case ConstructionFrozen frozen when frozen.OwnerId == me:
                        Show($"Стройка заморожена: {BuildingName(state, frozen.BuildingId)}", _warningColor);
                        break;
                    case ConstructionCompleted completed when completed.OwnerId == me:
                        Show($"Построено: {BuildingName(state, completed.BuildingId)}", _infoColor);
                        break;
                    case UnitProduced produced when produced.OwnerId == me:
                        Unit? unit = state.FindUnit(produced.UnitId);
                        Show($"Готов: {(unit != null ? DisplayNames.Unit(unit.Stats.Id) : "юнит")}", _infoColor);
                        break;
                    // drill.md: the alarm goes to the owner and the allies, so the Defender knows where to go.
                    case BuildingAttacked attacked when state.Relations.IsAlly(me, attacked.OwnerId):
                        string whose = attacked.OwnerId == me ? "" : $" союзника {attacked.OwnerId}";
                        Show($"Атакован{whose}: {BuildingName(state, attacked.BuildingId)}", _alarmColor);
                        break;
                    // demolish.md: the owner took it down — not an alarm.
                    case EntityDestroyed demolished when demolished.Demolished && demolished.OwnerId == me:
                        string refund = demolished.RefundPrimary + demolished.RefundSecondary > 0
                            ? $" (+{demolished.RefundPrimary}{(demolished.RefundSecondary > 0 ? $" + {demolished.RefundSecondary}" : "")})"
                            : "";
                        Show($"Снесено: {DisplayNames.Building(demolished.TypeId).ToLowerInvariant()}{refund}", _infoColor);
                        break;
                    // mission.md: a lost headquarters can be rebuilt while the builder lives.
                    case EntityDestroyed destroyed when destroyed.IsBuilding && destroyed.OwnerId == me:
                        Show(IsHeadquarters(state, destroyed.TypeId) && state.FindHeadquarters(me) == null
                            ? "Штаб разрушен — отстройте его строителем"
                            : $"Разрушено: {DisplayNames.Building(destroyed.TypeId).ToLowerInvariant()}", _alarmColor);
                        break;
                    // alerts.md: own losses by name (the builder has its own message below).
                    case EntityDestroyed lost when !lost.IsBuilding && lost.OwnerId == me && !IsBuilder(state, lost.TypeId):
                        Show($"Погиб: {DisplayNames.Unit(lost.TypeId).ToLowerInvariant()}", _warningColor);
                        break;
                    // alerts.md: one line per kind and player at most every few seconds.
                    case UnitAttacked hit when state.Relations.IsAlly(me, hit.OwnerId):
                        ShowUnitAttacked(state, me, hit);
                        break;
                    case BuilderDied died when state.Relations.IsAlly(me, died.PlayerId):
                        Show(died.PlayerId == me
                            ? $"Строитель погиб — вернётся через {DisplayNames.Clock(died.RespawnTick - died.Tick)} у штаба"
                            : $"Строитель союзника {died.PlayerId} погиб", _alarmColor);
                        break;
                    case BuilderRespawned back when back.PlayerId == me:
                        Show("Строитель вернулся (Tab — выделить)", _infoColor);
                        break;
                    case HeroDied heroDied when state.Relations.IsAlly(me, heroDied.PlayerId):
                        Show(heroDied.PlayerId == me
                            ? $"Герой погиб — вернётся через {DisplayNames.Clock(heroDied.RespawnTick - heroDied.Tick)}, уровень сохранится"
                            : $"Герой союзника {heroDied.PlayerId} погиб", _alarmColor);
                        break;
                    case HeroRespawned heroBack when heroBack.PlayerId == me:
                        Show("Герой вернулся (G — выделить)", _infoColor);
                        break;
                    case HeroLevelUp level when level.PlayerId == me:
                        Show(state.GetPlayer(me).HeroPathId.Length == 0
                            ? $"Герой получил уровень {level.Level} — выберите путь (G, затем кнопка пути)"
                            : $"Герой получил уровень {level.Level}", _infoColor);
                        break;
                    case BuildingCaptured captured when state.Relations.IsAlly(me, captured.PlayerId):
                        string who = captured.PlayerId == me ? "" : $" союзником {captured.PlayerId}";
                        Show($"Захвачено{who}: {BuildingName(state, captured.BuildingId)}", _infoColor);
                        break;
                    case LighthouseLit lit when state.Relations.IsAlly(me, lit.PlayerId):
                        Show("Маяк светит: открыт обзор вокруг", _infoColor);
                        break;
                    case CampAlerted alerted when Discovered(state, me, alerted.CampIndex):
                        Show("Лагерь крипов встревожен", _warningColor);
                        break;
                    // creep-camp.md: every player hears it.
                    case CampCleared cleared:
                        CreepCamp camp = state.CreepCamps[cleared.CampIndex];
                        Show(camp.Stats.TrophyPrimary + camp.Stats.TrophySecondary > 0
                            ? "Лагерь крипов зачищен — трофей ждёт строителя"
                            : $"Зачищено: {DisplayNames.Camp(camp.Stats.Id)}" + (GuardsACave(state, cleared.CampIndex) ? " — пещеру можно захватить" : ""), _infoColor);
                        break;
                    // legion-ai.md: the whole team hears where the next wave goes.
                    case WaveIncoming incoming:
                        string toWhom = incoming.TargetPlayerId == me ? "к вам" : $"к союзнику {incoming.TargetPlayerId}";
                        Show($"Волна {incoming.Wave} Легиона через {DisplayNames.Clock(incoming.LaunchTick - incoming.Tick)} — идёт {toWhom}", _alarmColor);
                        break;
                    case WaveLaunched launched:
                        string side = launched.TargetPlayerId == me ? "на вас" : $"на союзника {launched.TargetPlayerId}";
                        Show($"Волна {launched.Wave} вышла {side}: {launched.UnitCount} юн.", _alarmColor);
                        break;

                }
            }
        }

        // Last time each kind of unit alarm was shown: builder, repairer, army; mine and allies' apart.
        private readonly float[] _unitAlarmShown = { -100f, -100f, -100f, -100f, -100f, -100f };
        [SerializeField] private float _unitAlarmSeconds = 5f;

        private void ShowUnitAttacked(SimState state, int me, UnitAttacked hit)
        {
            Unit? unit = state.FindUnit(hit.UnitId);
            if (unit == null)
                return;
            int kind = unit.IsBuilder ? 0 : unit.IsRepairer ? 1 : 2;
            int slot = kind + (hit.OwnerId == me ? 0 : 3);
            if (Time.unscaledTime - _unitAlarmShown[slot] < _unitAlarmSeconds)
                return;
            _unitAlarmShown[slot] = Time.unscaledTime;
            string whose = hit.OwnerId == me ? "" : $" союзника {hit.OwnerId}";
            string text = kind switch
            {
                0 => $"Строитель{whose} атакован!",
                1 => $"Ремонтник{whose} атакован",
                _ => hit.OwnerId == me ? "Ваши войска атакованы" : $"Войска союзника {hit.OwnerId} атакованы",
            };
            Show(text, _alarmColor);
        }

        private static bool IsBuilder(SimState state, string unitId) =>
            state.Data.TryGetUnit(unitId, out UnitStats? stats) && stats!.HasTag(UnitTags.Builder);

        private static bool IsHeadquarters(SimState state, string buildingId) =>
            state.Data.TryGetBuilding(buildingId, out BuildingStats? stats) && stats!.Behavior == BuildingBehavior.Headquarters;

        private static bool GuardsACave(SimState state, int campIndex)
        {
            foreach (Building b in state.Buildings)
            {
                if (b.GuardCampIndex == campIndex && b.OwnerId == state.NeutralPlayerId)
                    return true;
            }
            return false;
        }

        private void ShowWaiting()
        {
            if (_hud?.Root == null)
                return;
            bool waiting = _runner!.NetWaiting;
            if (!waiting)
                _waitingSince = -1f;
            else if (_waitingSince < 0f)
                _waitingSince = Time.unscaledTime;
            bool show = waiting && Time.unscaledTime - _waitingSince >= WaitingNoticeSeconds;
            if (show && _waiting == null)
            {
                _waiting = new Label("Ожидание игроков…") { pickingMode = PickingMode.Ignore };
                Hud.Panelise(_waiting);
                _waiting.style.position = Position.Absolute;
                _waiting.style.top = 90;
                _waiting.style.left = Length.Percent(50);
                _waiting.style.translate = new Translate(Length.Percent(-50), 0);
                _waiting.style.fontSize = 20;
                _waiting.style.color = _warningColor;
                _hud.Root.Add(_waiting);
            }
            if (_waiting != null)
                _waiting.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Show(string text, Color color)
        {
            if (_column == null)
                return;
            foreach (Entry entry in _entries)
            {
                if (entry.Text != text)
                    continue;
                entry.Count++;
                entry.Shown = Time.unscaledTime;
                entry.Label.text = $"{text}  ×{entry.Count}";
                entry.Label.style.opacity = 1f;
                return;
            }

            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            Hud.Panelise(label);
            label.style.color = color;
            label.style.marginBottom = 4;
            _column.Add(label);
            _entries.Add(new Entry(text, label, Time.unscaledTime));
            if (_entries.Count > _maxShown)
            {
                _entries[0].Label.RemoveFromHierarchy();
                _entries.RemoveAt(0);
            }
        }

        private static string BuildingName(SimState state, int buildingId)
        {
            Building? building = state.FindBuilding(buildingId);
            return building != null ? DisplayNames.Building(building.Stats.Id).ToLowerInvariant() : "здание";
        }

        private static bool Discovered(SimState state, int playerId, int campIndex)
        {
            CreepCamp camp = state.CreepCamps[campIndex];
            return state.Fog.IsDiscovered(playerId, camp.CenterX, camp.CenterY);
        }

        private sealed class Entry
        {
            public Entry(string text, Label label, float shown)
            {
                Text = text;
                Label = label;
                Shown = shown;
            }

            public string Text { get; }
            public Label Label { get; }
            public float Shown { get; set; }
            public int Count { get; set; } = 1;
        }
    }
}
