using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // mission.md / legion-ai.md UI: the objective and the next wave's timer in the top right
    // corner, and the victory or defeat screen when the sim says the mission ended.
    // For missions with a goal (missions.csv); free play shows nothing.
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class MissionPanel : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        [SerializeField] private Color _victoryColor = new Color(0.55f, 0.95f, 0.55f);
        [SerializeField] private Color _defeatColor = new Color(1.00f, 0.45f, 0.40f);

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private Label? _objective;
        private Label? _end;
        private string _shown = "";
        private int _endTick = -1;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void OnEnable() => _runner!.TickEvents += OnTickEvents;

        private void OnDisable() => _runner!.TickEvents -= OnTickEvents;

        private void Start()
        {
            if (_hud?.Root == null || _runner!.Simulation == null || _runner.Simulation.State.Goal == MissionGoal.None)
                return;

            _objective = new Label { name = "Objective", pickingMode = PickingMode.Ignore };
            _objective.style.position = Position.Absolute;
            _objective.style.right = 12;
            _objective.style.top = 8;
            Hud.Panelise(_objective);
            _objective.style.unityTextAlign = TextAnchor.UpperRight;
            _hud.Root.Add(_objective);

            _end = new Label { name = "MissionEnd", pickingMode = PickingMode.Ignore };
            _end.style.position = Position.Absolute;
            // A banner across the screen: no transforms, so it centres on any resolution.
            _end.style.left = 0;
            _end.style.right = 0;
            _end.style.top = Length.Percent(35);
            Hud.Panelise(_end);
            _end.style.fontSize = 36;
            _end.style.paddingLeft = _end.style.paddingRight = 32;
            _end.style.paddingTop = _end.style.paddingBottom = 16;
            _end.style.unityTextAlign = TextAnchor.MiddleCenter;
            _end.style.display = DisplayStyle.None;
            _hud.Root.Add(_end);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_objective == null || simulation == null)
                return;
            SimState state = simulation.State;
            LegionAi? legion = state.Legion;

            // Only what the team has seen: the base hides in the fog until scouted.
            int seen = 0;
            int me = _localPlayer!.PlayerId;
            foreach (Building b in state.Buildings)
            {
                if (b.OwnerId == state.LegionPlayerId && !b.IsDead && (b.Stats.Behavior == BuildingBehavior.Headquarters || b.Factory != null)
                    && state.Fog.IsDiscovered(me, b.CellX, b.CellY))
                    seen++;
            }

            string text = state.Goal == MissionGoal.KillBoss
                ? "Цель: победите вожака Серых Существ (логово на северо-востоке)"
                : "Цель: уничтожьте штаб и заводы Чёрного Легиона";
            if (seen > 0)
                text += $"\nНайдено и ещё стоит: {seen}";
            if (state.Goal == MissionGoal.KillBoss && state.FindBoss() is Unit boss && state.Fog.IsVisible(me, boss.CellX, boss.CellY))
                text += $"\nВожак: {boss.Hp} / {boss.MaxHp}";
            if (state.Outcome == MissionOutcome.InProgress && legion != null && legion.NextWaveTick != int.MaxValue)
                text += $"\nВолна {legion.NextWave} через {DisplayNames.Clock(legion.NextWaveTick - state.Tick)}";
            // The clock stops when the mission ends; the sim keeps ticking behind the banner.
            text += $"\nВремя миссии: {DisplayNames.Clock(_endTick >= 0 ? _endTick : state.Tick)}";
            string keys = state.FindHero(me) != null ? "Tab — строитель · G — герой" : "Tab — строитель";
            // network.md: no player switching in a network game.
            text += state.TeamPlayerIds.Count > 1 && !_runner!.IsNetwork ? $"\n{keys} · F1–F4 — игрок · Esc — меню" : $"\n{keys} · Esc — меню";
            if (text != _shown)
            {
                _shown = text;
                _objective.text = text;
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
                _end.text = won
                    ? $"Победа!\n{goal} за {DisplayNames.Clock(ended.Tick)}\nEsc — меню"
                    : $"Поражение\nУ команды не осталось ни штаба, ни строителя ({DisplayNames.Clock(ended.Tick)})\nEsc — меню";
                _end.style.color = won ? _victoryColor : _defeatColor;
                _end.style.display = DisplayStyle.Flex;
            }
        }
    }
}
