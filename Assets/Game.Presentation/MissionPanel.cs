using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // mission.md / legion-ai.md UI: the objective and the next wave's timer in the top right
    // corner (with a bar that runs out as the wave comes), and the victory or defeat screen when
    // the sim says the mission ended. For missions with a goal (missions.csv); free play shows nothing.
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class MissionPanel : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        [Tooltip("Seconds before a wave goes out when its timer turns red.")]
        [SerializeField] private float _nearSeconds = 30f;

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private VisualElement? _objective;
        private Label? _goal;
        private Label? _found;
        private VisualElement? _wave;
        private Label? _waveTitle;
        private Label? _waveTimer;
        private Label? _waveArrival;
        private HudKit.Bar? _waveBar;
        private Label? _clock;
        private VisualElement? _end;
        private string _shownFound = "";
        private string _shownWave = "";
        private string _shownClock = "";
        private int _endTick = -1;
        // The wave the timer counts to and when the count began: the bar is the share left.
        private int _waveTick = -1;
        private int _waveCountFrom;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void OnEnable() => _runner!.TickEvents += OnTickEvents;

        private void OnDisable() => _runner!.TickEvents -= OnTickEvents;

        private void Start()
        {
            if (_hud?.TopRight == null || _runner!.Simulation == null || _runner.Simulation.State.Goal == MissionGoal.None)
                return;
            SimState state = _runner.Simulation.State;

            _objective = HudKit.Plate("Objective", "hud-plate--main");
            _objective.pickingMode = PickingMode.Ignore;
            VisualElement head = HudKit.Child(_objective, "hud-objective__head");
            HudKit.IconView(head, "ui_target", "◎", "hud-objective__icon");
            HudKit.Text(head, "ЦЕЛЬ МИССИИ", "hud-caption");
            _goal = HudKit.Text(_objective, state.Goal == MissionGoal.KillBoss
                ? "Победите вожака Серых Существ — его логово на северо-востоке."
                : "Найдите и уничтожьте штабы и заводы Чёрного Легиона.", "hud-text");
            _found = HudKit.Text(_objective, "", "hud-dim");
            _found.AddToClassList("hud-objective__found");
            _found.style.display = DisplayStyle.None;

            _wave = HudKit.Child(_objective, "hud-wave");
            VisualElement waveHead = HudKit.Child(_wave, "hud-wave__head");
            HudKit.IconView(waveHead, "ui_wave", "!", "hud-wave__icon");
            _waveTitle = HudKit.Text(waveHead, "", "hud-wave__title");
            _waveTimer = HudKit.Text(waveHead, "", "hud-wave__timer");
            _waveBar = HudKit.AddBar(_wave, "hud-wave__bar");
            _waveArrival = HudKit.Text(_wave, "", "hud-dim");
            _wave.style.display = DisplayStyle.None;

            VisualElement footer = HudKit.Child(_objective, "hud-objective__footer");
            _clock = HudKit.Text(footer, "", "hud-dim");
            HudKit.Text(footer, "F1 — КАК ИГРАТЬ", "hud-dim").AddToClassList("hud-accent");
            _hud.TopRight.Insert(0, _objective);

            _end = new VisualElement { name = "MissionEnd", pickingMode = PickingMode.Ignore };
            _end.style.display = DisplayStyle.None;
            _hud.Root!.Add(_end);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_objective == null || simulation == null)
                return;
            SimState state = simulation.State;
            // legion-ai.md, T5: the nearest wave of all the bases (a dormant base has none).
            LegionAi? legion = null;
            foreach (LegionAi ai in state.Legions)
            {
                if (ai.NextWaveTick != int.MaxValue && (legion == null || ai.NextWaveTick < legion.NextWaveTick))
                    legion = ai;
            }

            // Only what the team has seen: the base hides in the fog until scouted.
            int seen = 0;
            int me = _localPlayer!.PlayerId;
            foreach (Building b in state.Buildings)
            {
                if (state.IsLegionPlayer(b.OwnerId) && !b.IsDead && (b.Stats.Behavior == BuildingBehavior.Headquarters || b.Factory != null)
                    && state.Fog.IsDiscovered(me, b.CellX, b.CellY))
                    seen++;
            }
            string found = seen > 0 ? $"Найдено и ещё стоит: {seen}" : "";
            if (state.Goal == MissionGoal.KillBoss && state.FindBoss() is Unit boss && state.Fog.IsVisible(me, boss.CellX, boss.CellY))
                found = $"Вожак: {boss.Hp} / {boss.MaxHp}";
            else if (seen == 0 && state.Goal == MissionGoal.DestroyLegion)
                found = "База Легиона ещё не найдена — пошлите разведчика.";
            if (found != _shownFound)
            {
                _shownFound = found;
                _found!.text = found;
                _found.style.display = found.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool waveShown = state.Outcome == MissionOutcome.InProgress && legion != null;
            _wave!.style.display = waveShown ? DisplayStyle.Flex : DisplayStyle.None;
            if (waveShown)
            {
                if (legion!.NextWaveTick != _waveTick)
                {
                    _waveTick = legion.NextWaveTick;
                    _waveCountFrom = state.Tick;
                }
                int left = legion.NextWaveTick - state.Tick;
                float share = _waveTick > _waveCountFrom ? left / (float)(_waveTick - _waveCountFrom) : 0f;
                _waveBar!.Set(share);
                _wave.EnableInClassList("hud-wave--near", left <= _nearSeconds * Simulation.TicksPerSecond);
                // Known once the wave is warned of: the way from its base at the pace of its slowest unit.
                string arrival = legion.NextWaveArrivalTick != int.MaxValue && legion.NextWaveArrivalTick > legion.NextWaveTick
                    ? $"Дойдёт до цели ≈ через {DisplayNames.Clock(legion.NextWaveArrivalTick - state.Tick)}"
                    : "Легион пришлёт отряд к одной из баз команды.";
                string wave = $"{legion.NextWave}|{DisplayNames.Clock(left)}|{arrival}";
                if (wave != _shownWave)
                {
                    _shownWave = wave;
                    _waveTitle!.text = $"ВОЛНА {legion.NextWave} ЧЕРЕЗ";
                    _waveTimer!.text = DisplayNames.Clock(left);
                    _waveArrival!.text = arrival;
                }
            }
            // The clock stops when the mission ends; the sim keeps ticking behind the banner.
            string clock = $"ВРЕМЯ {DisplayNames.Clock(_endTick >= 0 ? _endTick : state.Tick)}";
            if (clock != _shownClock)
            {
                _shownClock = clock;
                _clock!.text = clock;
            }
        }

        private void OnTickEvents(IReadOnlyList<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                if (e is not MissionEnded ended)
                    continue;
                _endTick = ended.Tick;
                if (_end == null)
                    continue;
                bool won = ended.Outcome == MissionOutcome.Victory;
                SimState? state = _runner!.Simulation?.State;
                string goal = state != null && state.Goal == MissionGoal.KillBoss ? "Вожак Серых Существ повержен" : "База Чёрного Легиона уничтожена";
                _end.Clear();
                VisualElement plate = HudKit.Plate("EndPlate", "hud-end", won ? "hud-end--victory" : "hud-end--defeat");
                plate.pickingMode = PickingMode.Ignore;
                HudKit.Text(plate, won ? "ПОБЕДА" : "ПОРАЖЕНИЕ", "hud-end__title");
                HudKit.Text(plate, won
                    ? $"{goal} за {DisplayNames.Clock(ended.Tick)}."
                    : $"У команды не осталось ни штаба, ни строителя ({DisplayNames.Clock(ended.Tick)}).", "hud-end__text");
                HudKit.Text(plate, "ESC — МЕНЮ · ЗАПИСЬ МАТЧА СОХРАНИТСЯ В РЕПЛЕЯХ", "hud-end__hint");
                _end.Add(plate);
                _end.style.display = DisplayStyle.Flex;
                _end.BringToFront();
            }
        }
    }
}
