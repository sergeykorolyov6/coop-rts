using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // demolish.md UI: for a selected own building — its name and HP, and "Снести (Del)" that turns
    // into "Отменить снос" with the time left while it goes. Points of interest cannot be demolished.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class BuildingPanel : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private VisualElement? _panel;
        private Label? _info;
        private Button? _demolish;
        private string _shown = "";

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void Start()
        {
            if (_hud?.BottomBar == null || _runner!.Simulation == null)
                return;
            _panel = new VisualElement { name = "BuildingPanel" };
            Hud.Panelise(_panel);
            _panel.style.marginRight = 8;
            _info = new Label();
            _panel.Add(_info);
            _demolish = new Button(Demolish);
            Hud.StyleButton(_demolish);
            _demolish.style.height = 40;
            _panel.Add(_demolish);
            _panel.style.display = DisplayStyle.None;
            _hud.BottomBar.Insert(0, _panel);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_panel == null || simulation == null)
                return;
            Building? b = Selected(simulation.State);
            _panel.style.display = b != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (b == null)
                return;

            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null && keyboard.deleteKey.wasPressedThisFrame)
                Demolish();

            SimState state = simulation.State;
            string text = $"{DisplayNames.Building(b.Stats.Id)} · {b.Hp} / {b.MaxHp}";
            bool can = !b.Stats.Invulnerable;
            string button = !can ? "Снести нельзя (точка интереса)"
                : b.DemolishTicksLeft > 0 ? $"Отменить снос ({DisplayNames.Seconds(b.DemolishTicksLeft)})"
                : $"Снести (Del){RefundText(state, b.Stats)}";
            string key = text + "|" + button;
            if (key == _shown)
                return;
            _shown = key;
            _info!.text = text;
            _demolish!.text = button;
            _demolish.SetEnabled(can);
        }

        private static string RefundText(SimState state, BuildingStats stats)
        {
            (int primary, int secondary) = DemolishSystem.Refund(state.Rules, stats);
            if (primary + secondary == 0)
                return " — без возврата";
            return secondary > 0 ? $" — вернётся {primary} + {secondary}" : $" — вернётся {primary}";
        }

        private void Demolish()
        {
            Simulation? simulation = _runner!.Simulation;
            Building? b = simulation != null ? Selected(simulation.State) : null;
            if (b == null || b.Stats.Invulnerable)
                return;
            simulation!.Enqueue(new DemolishCommand(_localPlayer!.PlayerId, simulation.State.Tick, b.Id));
        }

        private Building? Selected(SimState state)
        {
            Building? b = state.FindBuilding(_selection!.SelectedBuildingId);
            return b != null && !b.IsDead && b.OwnerId == _localPlayer!.PlayerId ? b : null;
        }
    }
}
