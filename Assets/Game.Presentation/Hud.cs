using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Root of the in-game UI (UI Toolkit, built in code; the look is Assets/UI/Hud.uss):
    // the wallet on top (with income over the last minute), under it the allies with buttons to
    // give them resources (coop-tools.md), a bottom bar that panels fill, a tip that follows the
    // cursor and a tip over buttons (HudTip: title, hotkey, price, what it is for).
    // Mouse input on the map asks IsPointerOverUi first so a button click never also orders units.
    [RequireComponent(typeof(UIDocument))]
    public sealed class Hud : MonoBehaviour
    {
        [SerializeField] private SimRunner? _runner;
        [SerializeField] private LocalPlayer? _localPlayer;

        [Tooltip("coop-tools.md: resources one click gives an ally; with Shift five times as much.")]
        [SerializeField] private int _giveStep = 50;

        private const float AllyRowHeight = 30f;

        // The height of the allies' panel for that many allies: what sits below it leaves room (NotificationFeed).
        public static float AlliesPanelHeight(int allies) => allies * AllyRowHeight + 8f;

        // Where the allies' panel starts: under the wallet.
        public const float AlliesTop = 60f;

        private VisualElement? _alliesPanel;
        private readonly System.Collections.Generic.List<AllyRow> _allyRows = new System.Collections.Generic.List<AllyRow>();
        private int _alliesFor = -1;

        private VisualElement? _wallet;
        private VisualElement? _walletPip;
        private Label? _walletName;
        private Label? _walletFaction;
        private Label? _primaryAmount, _secondaryAmount, _primaryIncome, _secondaryIncome;
        private VisualElement? _tip;
        private Label? _tipText;
        private VisualElement? _uiTip;
        private VisualElement? _uiTipAnchor;
        private int _shownPrimary = -1, _shownSecondary = -1, _shownPlayer = -1;
        private int _shownIncomePrimary = -1, _shownIncomeSecondary = -1;

        // Wallet.Earned once per sim second, last minute: income = now minus a minute ago.
        private const int IncomeWindowSeconds = 60;
        private readonly int[] _earnedPrimary = new int[IncomeWindowSeconds + 1];
        private readonly int[] _earnedSecondary = new int[IncomeWindowSeconds + 1];
        private int _samples;
        private int _lastSampleSecond = -1;
        private int _samplePlayer = -1;

        private EntityView? _entities;

        public VisualElement? Root { get; private set; }
        public VisualElement? BottomBar { get; private set; }
        public VisualElement? TopRight { get; private set; }

        private void Awake()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            root.AddToClassList("hud");
            Root = root;
            _entities = _runner != null ? _runner.GetComponent<EntityView>() : null;

            BottomBar = new VisualElement { name = "BottomBar", pickingMode = PickingMode.Ignore };
            root.Add(BottomBar);
            // The objective, then the first steps under it (MissionPanel, Guide).
            TopRight = new VisualElement { name = "TopRight", pickingMode = PickingMode.Ignore };
            root.Add(TopRight);

            _tip = HudKit.Plate("CursorTip");
            _tip.pickingMode = PickingMode.Ignore;
            _tipText = HudKit.Text(_tip, "", "hud-text");
            _tip.style.display = DisplayStyle.None;
            root.Add(_tip);

            // factions.md: what a button's unit, building or ability does, above the button.
            _uiTip = HudKit.Plate("ButtonTip");
            _uiTip.pickingMode = PickingMode.Ignore;
            _uiTip.style.display = DisplayStyle.None;
            root.Add(_uiTip);
        }

        private void Start()
        {
            if (Root == null || _runner == null || _runner.Simulation == null)
                return;
            _wallet = HudKit.Plate("Wallet", "hud-plate--main");
            VisualElement who = HudKit.Child(_wallet, "hud-wallet__who");
            VisualElement player = HudKit.Child(who, "hud-wallet__player");
            _walletPip = HudKit.Child(player, "hud-pip");
            _walletName = HudKit.Text(player, "", "hud-wallet__name");
            _walletFaction = HudKit.Text(who, "", "hud-wallet__faction");
            (_primaryAmount, _primaryIncome) = AddResource(_wallet, Game.Sim.Resources.Primary);
            (_secondaryAmount, _secondaryIncome) = AddResource(_wallet, Game.Sim.Resources.Secondary);
            Root.Add(_wallet);
        }

        private (Label Amount, Label Income) AddResource(VisualElement wallet, int resource)
        {
            bool secondary = resource == Game.Sim.Resources.Secondary;
            var box = new VisualElement();
            box.AddToClassList("hud-resource");
            box.AddToClassList(secondary ? "hud-resource--secondary" : "hud-resource--primary");
            HudKit.IconView(box, secondary ? "res_secondary" : "res_primary", secondary ? "К" : "Р", "hud-resource__icon");
            Label amount = HudKit.Text(box, "0", "hud-resource__amount");
            Label income = HudKit.Text(box, "", "hud-resource__income");
            wallet.Add(box);
            AttachTip(box, () => secondary
                ? new HudTip
                {
                    Title = "Второй ресурс — кристаллы",
                    Body = "Голубые месторождения и кристальная порода. Добывается так же, как руда: бур на месторождении, лента до штаба. "
                         + "Нужен для турелей, тяжёлых юнитов и осады.",
                    Note = "Число справа — сколько пришло за последнюю минуту.",
                }
                : new HudTip
                {
                    Title = "Основной ресурс — руда",
                    Body = "Бур на месторождении добывает сам; лента везёт руду в штаб, и только там она попадает в кошелёк. "
                         + "Строитель может копать и носить руду вручную — медленно. Тратится на здания и юнитов.",
                    Note = "Число справа — сколько пришло за последнюю минуту.",
                });
            return (amount, income);
        }

        private void Update()
        {
            HideStaleTip();
            Simulation? simulation = _runner != null ? _runner.Simulation : null;
            if (simulation == null || _wallet == null || _localPlayer == null)
                return;
            int playerId = _localPlayer.PlayerId;
            Player player = simulation.State.GetPlayer(playerId);
            Wallet wallet = player.Wallet;
            bool hasSecondary = wallet.ResourceTypeCount > Game.Sim.Resources.Secondary;
            int primary = wallet.Get(Game.Sim.Resources.Primary);
            int secondary = hasSecondary ? wallet.Get(Game.Sim.Resources.Secondary) : 0;
            UpdateAllies(simulation.State, playerId, primary, secondary, hasSecondary);

            SampleIncome(simulation.State.Tick, playerId, wallet, hasSecondary);
            int oldest = _samples > IncomeWindowSeconds ? (_samples - IncomeWindowSeconds - 1) % _earnedPrimary.Length : 0;
            int newest = (_samples - 1 + _earnedPrimary.Length) % _earnedPrimary.Length;
            int incomePrimary = _samples > 0 ? _earnedPrimary[newest] - _earnedPrimary[oldest] : 0;
            int incomeSecondary = _samples > 0 ? _earnedSecondary[newest] - _earnedSecondary[oldest] : 0;

            if (playerId != _shownPlayer)
            {
                _shownPlayer = playerId;
                _walletName!.text = $"ИГРОК {playerId}";
                _walletFaction!.text = (player.Faction?.NameRu ?? "").ToUpperInvariant();
                _walletPip!.style.backgroundColor = _entities != null ? _entities.OwnerColor(simulation.State, playerId) : Color.white;
            }
            // Text only when it changes: a label rebuilds its layout on every assignment.
            if (primary != _shownPrimary)
            {
                _shownPrimary = primary;
                _primaryAmount!.text = primary.ToString();
            }
            if (secondary != _shownSecondary)
            {
                _shownSecondary = secondary;
                _secondaryAmount!.text = secondary.ToString();
            }
            if (incomePrimary != _shownIncomePrimary)
            {
                _shownIncomePrimary = incomePrimary;
                SetIncome(_primaryIncome!, incomePrimary);
            }
            if (incomeSecondary != _shownIncomeSecondary)
            {
                _shownIncomeSecondary = incomeSecondary;
                SetIncome(_secondaryIncome!, incomeSecondary);
            }
        }

        private static void SetIncome(Label label, int perMinute)
        {
            label.text = $"+{perMinute}/мин";
            label.EnableInClassList("hud-resource__income--up", perMinute > 0);
        }

        // coop-tools.md: one row per team ally — its wallet and the buttons that give it resources.
        private void UpdateAllies(SimState state, int me, int myPrimary, int mySecondary, bool hasSecondary)
        {
            if (Root == null || _runner == null)
                return;
            if (_alliesFor != me)
            {
                _alliesFor = me;
                _alliesPanel?.RemoveFromHierarchy();
                _alliesPanel = null;
                _allyRows.Clear();
                // Nobody to give to alone, and nothing to give in a replay.
                if (state.TeamPlayerIds.Count < 2 || !state.IsTeamPlayer(me) || _runner.IsReplay)
                    return;
                _alliesPanel = HudKit.Plate("Allies");
                _alliesPanel.pickingMode = PickingMode.Ignore;
                foreach (int ally in state.TeamPlayerIds)
                {
                    if (ally != me)
                        _allyRows.Add(new AllyRow(this, _alliesPanel, state, ally, hasSecondary));
                }
                Root.Add(_alliesPanel);
            }
            foreach (AllyRow row in _allyRows)
                row.Update(state, myPrimary, mySecondary, _giveStep);
        }

        private void Give(int allyId, int resourceType)
        {
            Simulation? simulation = _runner != null ? _runner.Simulation : null;
            if (simulation == null || _localPlayer == null)
                return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            int amount = keyboard != null && keyboard.shiftKey.isPressed ? _giveStep * 5 : _giveStep;
            _runner!.Submit(new GiveResourcesCommand(_localPlayer.PlayerId, simulation.State.Tick, allyId, resourceType, amount));
        }

        private sealed class AllyRow
        {
            private readonly int _allyId;
            private readonly Label _primaryText;
            private readonly Label _secondaryText;
            private readonly Button _primary;
            private readonly Button? _secondary;
            private int _shownPrimary = -1, _shownSecondary = -1, _shownStep = -1;

            public AllyRow(Hud hud, VisualElement panel, SimState state, int allyId, bool hasSecondary)
            {
                _allyId = allyId;
                VisualElement row = HudKit.Child(panel, "hud-ally");
                VisualElement name = HudKit.Child(row, "hud-row");
                HudKit.Child(name, "hud-pip").style.backgroundColor = hud._entities != null ? hud._entities.OwnerColor(state, allyId) : Color.white;
                HudKit.Text(name, DisplayNames.PlayerName(state, allyId), "hud-ally__name");
                VisualElement wallet = HudKit.Child(row, "hud-ally__wallet");
                HudKit.Child(wallet, "hud-res").AddToClassList("hud-res--primary");
                _primaryText = HudKit.Text(wallet, "", "hud-price__value");
                HudKit.Child(wallet, "hud-res").AddToClassList("hud-res--secondary");
                _secondaryText = HudKit.Text(wallet, "", "hud-price__value");
                _primary = MakeButton(() => hud.Give(allyId, Game.Sim.Resources.Primary));
                row.Add(_primary);
                hud.AttachTip(_primary, () => new HudTip
                {
                    Title = "Передать руду",
                    Body = "Из вашего кошелька в кошелёк союзника.",
                    Note = "С Shift — в пять раз больше.",
                });
                if (hasSecondary)
                {
                    _secondary = MakeButton(() => hud.Give(allyId, Game.Sim.Resources.Secondary));
                    row.Add(_secondary);
                    hud.AttachTip(_secondary, () => new HudTip
                    {
                        Title = "Передать кристаллы",
                        Body = "Из вашего кошелька в кошелёк союзника.",
                        Note = "С Shift — в пять раз больше.",
                    });
                }
            }

            public void Update(SimState state, int myPrimary, int mySecondary, int step)
            {
                Wallet wallet = state.GetPlayer(_allyId).Wallet;
                int primary = wallet.Get(Game.Sim.Resources.Primary), secondary = wallet.Secondary;
                if (primary != _shownPrimary)
                {
                    _shownPrimary = primary;
                    _primaryText.text = primary.ToString();
                }
                if (secondary != _shownSecondary)
                {
                    _shownSecondary = secondary;
                    _secondaryText.text = secondary.ToString();
                }
                if (step != _shownStep)
                {
                    _shownStep = step;
                    _primary.text = $"+{step} руды";
                    if (_secondary != null)
                        _secondary.text = $"+{step} крист.";
                }
                _primary.SetEnabled(myPrimary >= step);
                _secondary?.SetEnabled(mySecondary >= step);
            }

            private static Button MakeButton(System.Action onClick)
            {
                var button = new Button(onClick);
                StyleButton(button);
                return button;
            }
        }

        private void SampleIncome(int tick, int playerId, Wallet wallet, bool hasSecondary)
        {
            if (playerId != _samplePlayer)
            {
                _samplePlayer = playerId;
                _samples = 0;
                _lastSampleSecond = -1;
            }
            int second = tick / Simulation.TicksPerSecond;
            if (second == _lastSampleSecond)
                return;
            _lastSampleSecond = second;
            int slot = _samples % _earnedPrimary.Length;
            _earnedPrimary[slot] = wallet.Earned(Game.Sim.Resources.Primary);
            _earnedSecondary[slot] = hasSecondary ? wallet.Earned(Game.Sim.Resources.Secondary) : 0;
            _samples++;
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
            if (_tipText!.text != text)
                _tipText.text = text;
            _tip.style.display = DisplayStyle.Flex;
            _tip.BringToFront();
            Vector2 panel = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, Screen.height - screen.y));
            _tip.style.left = panel.x + 18;
            _tip.style.top = panel.y + 18;
        }

        // Shows the tip above the element while the pointer is over it; tip() is asked on every enter,
        // so it may change (cooldowns, prices).
        public void AttachTip(VisualElement element, System.Func<HudTip> tip)
        {
            element.RegisterCallback<PointerEnterEvent>(_ => ShowUiTip(element, tip()));
            element.RegisterCallback<PointerLeaveEvent>(_ => HideUiTip());
            element.RegisterCallback<DetachFromPanelEvent>(_ => HideUiTip());
        }

        public void AttachTip(VisualElement element, System.Func<string> text) => AttachTip(element, () => HudTip.Text(text()));

        private void ShowUiTip(VisualElement anchor, HudTip tip)
        {
            if (_uiTip == null || Root == null || (tip.Title.Length == 0 && tip.Body.Length == 0))
                return;
            _uiTip.Clear();
            if (tip.Title.Length > 0 || tip.Hotkey.Length > 0)
            {
                VisualElement head = HudKit.Child(_uiTip, "hud-tip__head");
                HudKit.Text(head, tip.Title, "hud-tip__title");
                if (tip.Hotkey.Length > 0)
                    HudKit.Text(head, tip.Hotkey, "hud-tip__key");
            }
            if (tip.HasPrice)
            {
                VisualElement price = HudKit.Price(_uiTip, "hud-tip__price");
                HudKit.SetPrice(price, tip.Primary, tip.Secondary, tip.PrimaryShort, tip.SecondaryShort, tip.Time);
            }
            if (tip.Body.Length > 0)
                HudKit.Text(_uiTip, tip.Body, "hud-tip__body");
            if (tip.Stats.Count > 0)
            {
                VisualElement stats = HudKit.Child(_uiTip, "hud-stats");
                foreach ((string caption, string value) in tip.Stats)
                    HudKit.Stat(stats, caption, value);
            }
            if (tip.Note.Length > 0)
                HudKit.Text(_uiTip, tip.Note, "hud-tip__note");

            _uiTipAnchor = anchor;
            _uiTip.style.display = DisplayStyle.Flex;
            _uiTip.BringToFront();
            Rect box = anchor.worldBound;
            float width = 320f;
            // Over the button, kept inside the screen; a button in the top half gets it below instead.
            float left = Mathf.Clamp(box.xMin, 8f, Mathf.Max(8f, Root.layout.width - width - 8f));
            _uiTip.style.left = left;
            if (box.center.y > Root.layout.height * 0.5f)
            {
                _uiTip.style.top = StyleKeyword.Auto;
                _uiTip.style.bottom = Root.layout.height - box.yMin + 6;
            }
            else
            {
                _uiTip.style.bottom = StyleKeyword.Auto;
                _uiTip.style.top = box.yMax + 6;
            }
        }

        private void HideUiTip()
        {
            _uiTipAnchor = null;
            if (_uiTip != null)
                _uiTip.style.display = DisplayStyle.None;
        }

        // A button hidden or rebuilt under the pointer never gets PointerLeave: the tip goes when
        // the pointer is no longer over what it was about.
        private void HideStaleTip()
        {
            if (_uiTipAnchor == null || Root?.panel == null)
                return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            Vector2 screen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            Vector2 pointer = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, Screen.height - screen.y));
            if (_uiTipAnchor.panel == null || _uiTipAnchor.resolvedStyle.display == DisplayStyle.None || !_uiTipAnchor.worldBound.Contains(pointer))
                HideUiTip();
        }

        // A small framed button of the HUD (Hud.uss .hud-button).
        public static void StyleButton(Button button)
        {
            button.AddToClassList("hud-button");
            MenuDraw.Frame(button);
        }

        // A plate pinned to the screen's top centre, its own width wide.
        public static void PinTopCentre(VisualElement element, float top)
        {
            element.style.position = Position.Absolute;
            element.style.top = top;
            element.style.left = Length.Percent(50);
            element.style.translate = new Translate(Length.Percent(-50), 0);
        }

        // A dark framed plate with light text: readable over any part of the map (Hud.uss .hud-plate).
        public static void Panelise(VisualElement element) => HudKit.MakePlate(element);
    }
}
