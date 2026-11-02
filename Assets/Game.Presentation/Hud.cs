using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Root of the in-game UI (UI Toolkit, built in code until there is a designer for it):
    // the wallet bar on top, a bottom bar that panels fill, and a tip that follows the cursor.
    // Mouse input on the map asks IsPointerOverUi first so a button click never also orders units.
    [RequireComponent(typeof(UIDocument))]
    public sealed class Hud : MonoBehaviour
    {
        [SerializeField] private SimRunner? _runner;
        [SerializeField] private LocalPlayer? _localPlayer;

        private Label? _wallet;
        private Label? _tip;
        private int _shownPrimary = -1, _shownSecondary = -1, _shownPlayer = -1;

        public VisualElement? Root { get; private set; }
        public VisualElement? BottomBar { get; private set; }

        private void Awake()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            Root = root;

            _wallet = new Label { name = "Wallet", pickingMode = PickingMode.Ignore };
            _wallet.style.position = Position.Absolute;
            _wallet.style.left = 12;
            _wallet.style.top = 8;
            Panelise(_wallet);
            root.Add(_wallet);

            BottomBar = new VisualElement { name = "BottomBar", pickingMode = PickingMode.Ignore };
            BottomBar.style.position = Position.Absolute;
            BottomBar.style.left = 12;
            BottomBar.style.bottom = 12;
            BottomBar.style.flexDirection = FlexDirection.Row;
            BottomBar.style.alignItems = Align.FlexEnd;
            root.Add(BottomBar);

            _tip = new Label { name = "CursorTip", pickingMode = PickingMode.Ignore };
            _tip.style.position = Position.Absolute;
            Panelise(_tip);
            _tip.style.display = DisplayStyle.None;
            root.Add(_tip);
        }

        private void Update()
        {
            Simulation? simulation = _runner != null ? _runner.Simulation : null;
            if (simulation == null || _wallet == null || _localPlayer == null)
                return;
            int playerId = _localPlayer.PlayerId;
            Wallet wallet = simulation.State.GetPlayer(playerId).Wallet;
            int primary = wallet.Get(Game.Sim.Resources.Primary);
            int secondary = wallet.ResourceTypeCount > Game.Sim.Resources.Secondary ? wallet.Get(Game.Sim.Resources.Secondary) : 0;
            // Text only when it changes: a label rebuilds its layout on every assignment.
            if (primary == _shownPrimary && secondary == _shownSecondary && playerId == _shownPlayer)
                return;
            _shownPrimary = primary;
            _shownSecondary = secondary;
            _shownPlayer = playerId;
            _wallet.text = $"Игрок {playerId}    Основной ресурс: {primary}    Второй: {secondary}";
        }

        // Anything of the HUD that takes clicks lies under the pointer (the root and pass-through
        // containers ignore picking).
        public bool IsPointerOverUi(Vector2 screen)
        {
            if (Root?.panel == null)
                return false;
            Vector2 panel = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, Screen.height - screen.y));
            VisualElement? picked = Root.panel.Pick(panel);
            return picked != null && picked != Root;
        }

        public void ShowTip(string? text, Vector2 screen)
        {
            if (_tip == null || Root?.panel == null)
                return;
            if (string.IsNullOrEmpty(text))
            {
                _tip.style.display = DisplayStyle.None;
                return;
            }
            _tip.text = text;
            _tip.style.display = DisplayStyle.Flex;
            Vector2 panel = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, Screen.height - screen.y));
            _tip.style.left = panel.x + 18;
            _tip.style.top = panel.y + 18;
        }

        public static readonly Color ButtonColor = new Color(0.17f, 0.18f, 0.20f);

        // Dark buttons with white text, matching the plates (the default theme's light grey
        // buttons wash out white text).
        public static void StyleButton(Button button)
        {
            button.style.backgroundColor = ButtonColor;
            button.style.color = Color.white;
            button.style.borderTopColor = button.style.borderBottomColor = new Color(0.35f, 0.37f, 0.40f);
            button.style.borderLeftColor = button.style.borderRightColor = new Color(0.35f, 0.37f, 0.40f);
            button.style.fontSize = 14;
        }

        // Near-opaque dark plate with light text: readable over any part of the map.
        public static void Panelise(VisualElement element)
        {
            element.style.backgroundColor = new Color(0.04f, 0.045f, 0.05f, 0.94f);
            element.style.color = new Color(0.92f, 0.93f, 0.90f);
            element.style.paddingLeft = element.style.paddingRight = 8;
            element.style.paddingTop = element.style.paddingBottom = 4;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = 4;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 4;
            element.style.fontSize = 15;
        }
    }
}
