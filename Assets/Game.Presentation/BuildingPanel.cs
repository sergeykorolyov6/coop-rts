using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // demolish.md UI: for a selected own building — its name and HP, and "Снести (Del)" that turns
    // into "Отменить снос" with the time left while it goes. Points of interest cannot be demolished.
    // Someone else's building (Legion, neutral, ally) shows whose it is and its numbers, no button.
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
            _info.style.maxWidth = 340;
            _info.style.whiteSpace = WhiteSpace.Normal;
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
            SimState state = simulation.State;
            Building? b = state.FindBuilding(_selection!.SelectedBuildingId);
            if (b != null && b.IsDead)
                b = null;
            _panel.style.display = b != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (b == null)
                return;

            int me = _localPlayer!.PlayerId;
            bool own = b.OwnerId == me;
            _demolish!.style.display = own ? DisplayStyle.Flex : DisplayStyle.None;
            Keyboard? keyboard = Keyboard.current;
            if (own && keyboard != null && keyboard.deleteKey.wasPressedThisFrame)
                Demolish();

            string text = $"{DisplayNames.Building(b.Stats.Id)} · {b.Hp} / {b.MaxHp}";
            if (!own)
                text += $"\n{DisplayNames.Whose(state, me, b.OwnerId)}\n{b.Stats.Description}\n{StatsLine(b.Stats)}";
            else
                text += $"\n{b.Stats.Description}";
            bool can = !b.Stats.Invulnerable;
            string button = !can ? "Снести нельзя (точка интереса)"
                : b.DemolishTicksLeft > 0 ? $"Отменить снос ({DisplayNames.Seconds(b.DemolishTicksLeft)})"
                : $"Снести (Del){RefundText(state, b.OwnerId, b.Stats)}";
            string key = text + "|" + button;
            if (key == _shown)
                return;
            _shown = key;
            _info!.text = text;
            _demolish!.text = button;
            _demolish.SetEnabled(can);
        }

        // "броня 1 · урон 40 · дальность 7" — what an enemy building is worth knowing.
        private static string StatsLine(BuildingStats s)
        {
            string line = $"Броня {s.Armor}";
            if (s.Damage > 0)
                line += $" · урон {s.Damage} · дальность {DisplayNames.Number(s.AttackRangeMilli / (double)Game.Sim.Grid.MilliPerCell, "0.#")}";
            if (s.Produces.Count > 0)
            {
                var names = new string[s.Produces.Count];
                for (int i = 0; i < names.Length; i++)
                    names[i] = DisplayNames.Unit(s.Produces[i]);
                line += $"\nДелает: {string.Join(", ", names)}";
            }
            return line;
        }

        private static string RefundText(SimState state, int owner, BuildingStats stats)
        {
            (int primary, int secondary) = DemolishSystem.Refund(state.GetPlayer(owner).Faction, stats);
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
