using System.Collections.Generic;
using Game.Net;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Short messages from sim events for the local player, under the wallet: "place taken",
    // "drill attacked", "site frozen", "camp cleared"... Each slides in, fades after a few seconds;
    // the same message again while shown counts up (×3) instead of stacking. Colour by weight:
    // info, warning, alarm (Hud.uss .hud-toast--*).
    // legion-ai.md: a wave of the Legion also gets a banner at the top centre — when it goes,
    // to whom, when it will be there, and what to do about it.
    // network.md: "Игрок N вышел", "Рассинхронизация" and, while the lockstep stands, "Ожидание игроков…".
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class NotificationFeed : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        [SerializeField] private float _seconds = 4f;
        [SerializeField] private int _maxShown = 6;
        [SerializeField] private float _bannerSeconds = 7f;

        private enum Weight { Info, Warning, Alarm }

        private readonly List<Entry> _entries = new List<Entry>();

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private VisualElement? _column;
        private VisualElement? _waiting;
        private VisualElement? _banner;
        private Label? _bannerTitle;
        private Label? _bannerText;
        private float _bannerUntil = -1f;
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
                Show($"Игрок {notice.Slot} вышел — его база остаётся", Weight.Alarm);
            else if (notice.Kind == NetNoticeKind.Desync)
                Show($"Рассинхронизация на {DisplayNames.Clock(notice.Tick)} — игры у вас разошлись", Weight.Alarm);
        }

        private void Start()
        {
            if (_hud?.Root == null)
                return;
            _column = new VisualElement { name = "Notifications", pickingMode = PickingMode.Ignore };
            // Below the wallet and, in a team, the allies' panel (Hud, coop-tools.md).
            int allies = _runner!.Simulation != null ? _runner.Simulation.State.TeamPlayerIds.Count - 1 : 0;
            _column.style.top = Hud.AlliesTop + (allies > 0 ? Hud.AlliesPanelHeight(allies) + 8 : 0);
            _hud.Root.Add(_column);

            _banner = HudKit.Plate("WaveBanner");
            _banner.pickingMode = PickingMode.Ignore;
            _banner.AddToClassList("hud-banner--hidden");
            VisualElement head = HudKit.Child(_banner, "hud-banner__head");
            HudKit.IconView(head, "ui_wave", "!", "hud-banner__icon");
            _bannerTitle = HudKit.Text(head, "", "hud-banner__title");
            _bannerText = HudKit.Text(_banner, "", "hud-banner__text");
            _hud.Root.Add(_banner);
        }

        private void Update()
        {
            ShowWaiting();
            if (_banner != null && _bannerUntil > 0f && Time.unscaledTime >= _bannerUntil)
            {
                _bannerUntil = -1f;
                _banner.AddToClassList("hud-banner--hidden");
            }
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - _entries[i].Shown;
                if (age >= _seconds)
                {
                    _entries[i].Plate.RemoveFromHierarchy();
                    _entries.RemoveAt(i);
                }
                else if (age > _seconds - 1f)
                {
                    _entries[i].Plate.style.opacity = _seconds - age;
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
                        Show(DisplayNames.Reject(rejected.Reason), Weight.Warning);
                        break;
                    case MiningStopped stopped when stopped.PlayerId == me:
                        Show("Клетку руды заняли — строитель остановился", Weight.Warning);
                        break;
                    case ConstructionFrozen frozen when frozen.OwnerId == me:
                        Show($"Стройка заморожена: {BuildingName(state, frozen.BuildingId)}", Weight.Warning);
                        break;
                    case ConstructionCompleted completed when completed.OwnerId == me:
                        Show($"Построено: {BuildingName(state, completed.BuildingId)}", Weight.Info);
                        break;
                    case UnitProduced produced when produced.OwnerId == me:
                        Unit? unit = state.FindUnit(produced.UnitId);
                        Show($"Готов: {(unit != null ? DisplayNames.Unit(unit.Stats.Id) : "юнит")}", Weight.Info);
                        break;
                    // drill.md: the alarm goes to the owner and the allies, so the Defender knows where to go.
                    case BuildingAttacked attacked when state.Relations.IsAlly(me, attacked.OwnerId):
                        string whose = attacked.OwnerId == me ? "" : $" союзника {attacked.OwnerId}";
                        Show($"Атакован{whose}: {BuildingName(state, attacked.BuildingId)}", Weight.Alarm);
                        break;
                    // demolish.md: the owner took it down — not an alarm.
                    case EntityDestroyed demolished when demolished.Demolished && demolished.OwnerId == me:
                        string refund = demolished.RefundPrimary + demolished.RefundSecondary > 0
                            ? $" (+{demolished.RefundPrimary}{(demolished.RefundSecondary > 0 ? $" + {demolished.RefundSecondary}" : "")})"
                            : "";
                        Show($"Снесено: {DisplayNames.Building(demolished.TypeId).ToLowerInvariant()}{refund}", Weight.Info);
                        break;
                    // mission.md: a lost headquarters can be rebuilt while the builder lives.
                    case EntityDestroyed destroyed when destroyed.IsBuilding && destroyed.OwnerId == me:
                        Show(IsHeadquarters(state, destroyed.TypeId) && state.FindHeadquarters(me) == null
                            ? "Штаб разрушен — отстройте его строителем"
                            : $"Разрушено: {DisplayNames.Building(destroyed.TypeId).ToLowerInvariant()}", Weight.Alarm);
                        break;
                    // alerts.md: own losses by name (the builder has its own message below).
                    case EntityDestroyed lost when !lost.IsBuilding && lost.OwnerId == me && !IsBuilder(state, lost.TypeId):
                        Show($"Погиб: {DisplayNames.Unit(lost.TypeId).ToLowerInvariant()}", Weight.Warning);
                        break;
                    // alerts.md: one line per kind and player at most every few seconds.
                    case UnitAttacked hit when state.Relations.IsAlly(me, hit.OwnerId):
                        ShowUnitAttacked(state, me, hit);
                        break;
                    case BuilderDied died when state.Relations.IsAlly(me, died.PlayerId):
                        Show(died.PlayerId == me
                            ? $"Строитель погиб — вернётся через {DisplayNames.Clock(died.RespawnTick - died.Tick)} у штаба"
                            : $"Строитель союзника {died.PlayerId} погиб", Weight.Alarm);
                        break;
                    case BuilderRespawned back when back.PlayerId == me:
                        Show("Строитель вернулся (Tab — выделить)", Weight.Info);
                        break;
                    case HeroDied heroDied when state.Relations.IsAlly(me, heroDied.PlayerId):
                        Show(heroDied.PlayerId == me
                            ? $"Герой погиб — вернётся через {DisplayNames.Clock(heroDied.RespawnTick - heroDied.Tick)}, уровень сохранится"
                            : $"Герой союзника {heroDied.PlayerId} погиб", Weight.Alarm);
                        break;
                    case HeroRespawned heroBack when heroBack.PlayerId == me:
                        Show("Герой вернулся (G — выделить)", Weight.Info);
                        break;
                    case HeroLevelUp level when level.PlayerId == me:
                        Show(state.GetPlayer(me).HeroPathId.Length == 0
                            ? $"Герой получил уровень {level.Level} — выберите путь (G, затем кнопка пути)"
                            : $"Герой получил уровень {level.Level}", Weight.Info);
                        break;
                    case BuildingCaptured captured when state.Relations.IsAlly(me, captured.PlayerId):
                        string who = captured.PlayerId == me ? "" : $" союзником {captured.PlayerId}";
                        Show($"Захвачено{who}: {BuildingName(state, captured.BuildingId)}", Weight.Info);
                        break;
                    case LighthouseLit lit when state.Relations.IsAlly(me, lit.PlayerId):
                        Show("Маяк светит: открыт обзор вокруг", Weight.Info);
                        break;
                    case CampAlerted alerted when Discovered(state, me, alerted.CampIndex):
                        Show("Лагерь крипов встревожен", Weight.Warning);
                        break;
                    // creep-camp.md: every player hears it.
                    case CampCleared cleared:
                        CreepCamp camp = state.CreepCamps[cleared.CampIndex];
                        Show(camp.Stats.TrophyPrimary + camp.Stats.TrophySecondary > 0
                            ? "Лагерь крипов зачищен — трофей ждёт строителя"
                            : $"Зачищено: {DisplayNames.Camp(camp.Stats.Id)}" + (GuardsACave(state, cleared.CampIndex) ? " — пещеру можно захватить" : ""), Weight.Info);
                        break;
                    // legion-ai.md: the whole team hears where the next wave goes.
                    case WaveIncoming incoming:
                        string toWhom = incoming.TargetPlayerId == me ? "к вам" : $"к союзнику {incoming.TargetPlayerId}";
                        // legion-ai.md, T5: on a big map the way is long — the warning says when it will be there.
                        string arrives = incoming.ArrivalTick > incoming.LaunchTick ? $", дойдёт ≈ через {DisplayNames.Clock(incoming.ArrivalTick - incoming.Tick)}" : "";
                        Show($"Волна {incoming.Wave} Легиона: выйдет через {DisplayNames.Clock(incoming.LaunchTick - incoming.Tick)}{arrives} — идёт {toWhom}", Weight.Alarm);
                        Banner($"ВОЛНА {incoming.Wave} ЛЕГИОНА",
                            $"Выйдет через {DisplayNames.Clock(incoming.LaunchTick - incoming.Tick)} и пойдёт {toWhom}{arrives}."
                            + (incoming.TargetPlayerId == me ? "\nСтавьте турели у штаба и буров, соберите армию (F2)." : "\nПомогите союзнику: армия — F2, ресурс — кнопками под кошельком."));
                        break;
                    case WaveLaunched launched:
                        string side = launched.TargetPlayerId == me ? "на вас" : $"на союзника {launched.TargetPlayerId}";
                        string eta = launched.ArrivalTick > launched.Tick ? $", дойдёт ≈ через {DisplayNames.Clock(launched.ArrivalTick - launched.Tick)}" : "";
                        Show($"Волна {launched.Wave} вышла {side}: {launched.UnitCount} юн.{eta}", Weight.Alarm);
                        Banner($"ВОЛНА {launched.Wave} ВЫШЛА", $"{launched.UnitCount} юн. идут {side}{eta}.");
                        break;
                    // coop-tools.md: the mark itself is on the minimap and on the ground.
                    case Pinged ping when ping.PlayerId != me && state.Relations.IsAlly(me, ping.PlayerId):
                        Show($"{DisplayNames.PlayerName(state, ping.PlayerId)} поставил метку на карте", Weight.Info);
                        break;
                    case ResourcesGiven given when given.FromPlayerId == me || given.ToPlayerId == me:
                        string what = given.ResourceType == Game.Sim.Resources.Secondary ? "второго ресурса" : "основного ресурса";
                        Show(given.FromPlayerId == me
                            ? $"Вы передали {given.Amount} {what}: {DisplayNames.PlayerName(state, given.ToPlayerId)}"
                            : $"{DisplayNames.PlayerName(state, given.FromPlayerId)} передал вам {given.Amount} {what}", Weight.Info);
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
            Show(text, Weight.Alarm);
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
                _waiting = HudKit.Plate("Waiting", "hud-plate--main");
                _waiting.pickingMode = PickingMode.Ignore;
                HudKit.Text(_waiting, "Ожидание игроков…", "hud-text");
                _hud.Root.Add(_waiting);
            }
            if (_waiting != null)
                _waiting.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Show(string text, Weight weight)
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
                entry.Plate.style.opacity = 1f;
                return;
            }

            VisualElement plate = HudKit.Plate("Toast", "hud-toast");
            plate.pickingMode = PickingMode.Ignore;
            plate.AddToClassList(weight == Weight.Alarm ? "hud-toast--alarm" : weight == Weight.Warning ? "hud-toast--warning" : "hud-toast--info");
            Label label = HudKit.Text(plate, text, "hud-toast__text");
            // Slides in: the class goes off a moment later and the transition of Hud.uss plays.
            plate.AddToClassList("hud-toast--entering");
            plate.schedule.Execute(() => plate.RemoveFromClassList("hud-toast--entering")).StartingIn(30);
            _column.Add(plate);
            _entries.Add(new Entry(text, plate, label, Time.unscaledTime));
            if (_entries.Count > _maxShown)
            {
                _entries[0].Plate.RemoveFromHierarchy();
                _entries.RemoveAt(0);
            }
        }

        private void Banner(string title, string text)
        {
            if (_banner == null)
                return;
            _bannerTitle!.text = title;
            _bannerText!.text = text;
            _banner.BringToFront();
            _banner.RemoveFromClassList("hud-banner--hidden");
            _bannerUntil = Time.unscaledTime + _bannerSeconds;
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
            public Entry(string text, VisualElement plate, Label label, float shown)
            {
                Text = text;
                Plate = plate;
                Label = label;
                Shown = shown;
            }

            public string Text { get; }
            public VisualElement Plate { get; }
            public Label Label { get; }
            public float Shown { get; set; }
            public int Count { get; set; } = 1;
        }
    }
}
