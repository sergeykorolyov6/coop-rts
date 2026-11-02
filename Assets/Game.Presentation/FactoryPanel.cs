using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // factory.md UI: for a selected own factory — unit buttons with price and time (grey when the
    // wallet cannot pay or the queue is full), the queue with the first unit's progress, and
    // a cancel button that drops the last unit (full refund, in the sim).
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class FactoryPanel : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        [Tooltip("M1 units (PLAN.md): worker and infantry. The rest of the factory's list comes in M2.")]
        [SerializeField] private string[] _units = { "worker", "infantry" };

        private readonly List<Button> _buttons = new List<Button>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private VisualElement? _panel;
        private Label? _title;
        private Label? _queue;
        private VisualElement? _progressFill;
        private Button? _cancel;
        private string _shownQueue = "";

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void Start()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_hud?.BottomBar == null || simulation == null)
                return;

            _panel = new VisualElement { name = "FactoryPanel" };
            Hud.Panelise(_panel);
            _title = new Label("Завод");
            _panel.Add(_title);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            foreach (string unitId in _units)
            {
                if (!simulation.State.Data.TryGetUnit(unitId, out UnitStats? stats))
                    continue;
                string id = unitId;
                string price = stats!.CostSecondary > 0 ? $"{stats.CostPrimary} + {stats.CostSecondary}" : $"{stats.CostPrimary}";
                var button = new Button(() => Produce(id))
                {
                    text = $"{DisplayNames.Unit(id)}\n{price} · {DisplayNames.Seconds(stats.BuildTimeTicks)}",
                    userData = stats,
                };
                button.style.width = 110;
                button.style.height = 52;
                Hud.StyleButton(button);
                row.Add(button);
                _buttons.Add(button);
            }
            _cancel = new Button(Cancel) { text = "Отменить\nпоследний" };
            _cancel.style.width = 92;
            _cancel.style.height = 52;
            Hud.StyleButton(_cancel);
            row.Add(_cancel);
            _panel.Add(row);

            _queue = new Label();
            _panel.Add(_queue);
            var bar = new VisualElement();
            bar.style.height = 6;
            bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            _progressFill = new VisualElement();
            _progressFill.style.height = 6;
            _progressFill.style.backgroundColor = new Color(0.4f, 0.9f, 0.5f);
            bar.Add(_progressFill);
            _panel.Add(bar);

            _panel.style.display = DisplayStyle.None;
            _hud.BottomBar.Add(_panel);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_panel == null || simulation == null)
                return;
            Building? factory = SelectedFactory(simulation.State);
            _panel.style.display = factory != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (factory == null)
                return;

            SimState state = simulation.State;
            IReadOnlyList<string> queue = factory.Factory!.Queue;
            bool queueFull = queue.Count >= state.Rules.FactoryQueue;
            Wallet wallet = state.GetPlayer(_localPlayer!.PlayerId).Wallet;
            foreach (Button button in _buttons)
            {
                var stats = (UnitStats)button.userData;
                bool affordable = wallet.Get(Game.Sim.Resources.Primary) >= stats.CostPrimary
                    && (stats.CostSecondary == 0 || (wallet.ResourceTypeCount > Game.Sim.Resources.Secondary && wallet.Get(Game.Sim.Resources.Secondary) >= stats.CostSecondary));
                button.style.opacity = affordable && !queueFull ? 1f : 0.45f;
            }
            _cancel!.SetEnabled(queue.Count > 0);

            string text = queue.Count == 0 ? $"Очередь пуста (до {state.Rules.FactoryQueue})" : "Очередь: " + Names(queue) + $"  ({queue.Count}/{state.Rules.FactoryQueue})";
            if (text != _shownQueue)
            {
                _shownQueue = text;
                _queue!.text = text;
            }
            float share = queue.Count > 0 && state.Data.TryGetUnit(queue[0], out UnitStats? first) && first!.BuildTimeTicks > 0
                ? Mathf.Clamp01(factory.Factory.Progress / (float)first.BuildTimeTicks)
                : 0f;
            _progressFill!.style.width = Length.Percent(share * 100f);
            _title!.text = factory.IsComplete ? "Завод" : "Завод (строится)";
        }

        // A click with a full queue or an empty wallet still goes to the sim: it answers with
        // CommandRejected, which the notifications show (step 5).
        private void Produce(string unitId)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _selection!.SelectedBuildingId == 0)
                return;
            simulation.Enqueue(new ProduceCommand(_localPlayer!.PlayerId, simulation.State.Tick, _selection.SelectedBuildingId, unitId));
        }

        private void Cancel()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _selection!.SelectedBuildingId == 0)
                return;
            simulation.Enqueue(new CancelProductionCommand(_localPlayer!.PlayerId, simulation.State.Tick, _selection.SelectedBuildingId));
        }

        private Building? SelectedFactory(SimState state)
        {
            Building? building = state.FindBuilding(_selection!.SelectedBuildingId);
            return building != null && !building.IsDead && building.Factory != null ? building : null;
        }

        private static string Names(IReadOnlyList<string> queue)
        {
            var names = new string[queue.Count];
            for (int i = 0; i < queue.Count; i++)
                names[i] = DisplayNames.Unit(queue[i]);
            return string.Join(", ", names);
        }
    }
}
