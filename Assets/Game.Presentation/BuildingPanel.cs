using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // The selection card of a building. demolish.md UI: for a selected own building — its icon, name,
    // health, what it does and "Снести (Del)" that turns into "Отменить снос" with the time left
    // while it goes. Points of interest cannot be demolished. Someone else's building (Legion,
    // neutral, ally) shows whose it is and its numbers, no button.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class BuildingPanel : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private EntityView? _entities;
        private VisualElement? _card;
        private Button? _demolish;
        private HudKit.Bar? _hp;
        private Label? _hpText;
        private Label? _state;
        private string _shown = "";
        private string _shownButton = "";
        private string _shownState = "";
        private int _shownHp = -1;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
            _entities = GetComponent<EntityView>();
        }

        private void Start()
        {
            if (_hud?.BottomBar == null || _runner!.Simulation == null)
                return;
            _card = HudKit.Plate("BuildingPanel", "hud-card", "hud-plate--main");
            _card.style.display = DisplayStyle.None;
            _hud.BottomBar.Insert(0, _card);
            _demolish = new Button(Demolish);
            Hud.StyleButton(_demolish);
            _demolish.AddToClassList("hud-card__button");
            _hud.AttachTip(_demolish, () => new HudTip
            {
                Title = "Снести",
                Hotkey = "Del",
                Body = "Здание разбирается несколько секунд; часть цены вернётся в кошелёк. Пока идёт снос, его можно отменить.",
            });
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_card == null || simulation == null)
                return;
            SimState state = simulation.State;
            Building? b = state.FindBuilding(_selection!.SelectedBuildingId);
            if (b != null && b.IsDead)
                b = null;
            _card.style.display = b != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (b == null)
            {
                _shown = "";
                return;
            }

            int me = _localPlayer!.PlayerId;
            bool own = b.OwnerId == me;
            string key = $"{b.Id}|{me}|{b.OwnerId}";
            if (key != _shown)
            {
                _shown = key;
                Build(state, b, own);
            }

            Keyboard? keyboard = Keyboard.current;
            if (own && keyboard != null && keyboard.deleteKey.wasPressedThisFrame)
                Demolish();

            if (b.Hp != _shownHp)
            {
                _shownHp = b.Hp;
                float share = b.Hp / (float)Mathf.Max(1, b.MaxHp);
                _hp!.Set(share);
                _hp.SetColor(HudKit.HealthColor(share));
                _hpText!.text = $"{b.Hp} / {b.MaxHp}";
            }
            string status = !b.IsComplete ? "Строится — строитель должен стоять рядом" : b.DemolishTicksLeft > 0 ? "Идёт снос" : "";
            if (status != _shownState)
            {
                _shownState = status;
                _state!.text = status;
                _state.style.display = status.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!own)
                return;
            bool can = !b.Stats.Invulnerable;
            string button = !can ? "Снести нельзя (точка интереса)"
                : b.DemolishTicksLeft > 0 ? $"Отменить снос ({DisplayNames.Seconds(b.DemolishTicksLeft)})"
                : $"Снести (Del){RefundText(state, b.OwnerId, b.Stats)}";
            if (button != _shownButton)
            {
                _shownButton = button;
                _demolish!.text = button;
                _demolish.SetEnabled(can);
            }
        }

        private void Build(SimState state, Building b, bool own)
        {
            _card!.Clear();
            _shownHp = -1;
            _shownButton = "";
            _shownState = "";
            int me = _localPlayer!.PlayerId;
            VisualElement head = HudKit.Child(_card, "hud-card__head");
            Color color = _entities != null ? _entities.OwnerColor(state, b.OwnerId) : Color.white;
            HudKit.IconView(head, b.Stats.Id, b.Stats.NameRu, "hud-card__icon").style.unityBackgroundImageTintColor = Color.Lerp(color, Color.white, 0.35f);
            VisualElement names = HudKit.Child(head, "hud-card__names");
            HudKit.Text(names, DisplayNames.Building(b.Stats.Id), "hud-title");
            string whose = DisplayNames.Whose(state, me, b.OwnerId);
            Label owner = HudKit.Text(names, whose.Length > 0 ? whose.ToUpperInvariant() : (state.GetPlayer(b.OwnerId).Faction?.NameRu ?? "").ToUpperInvariant(), "hud-card__whose");
            bool enemy = state.Relations.IsEnemy(me, b.OwnerId);
            owner.EnableInClassList("hud-alarm", enemy);
            owner.EnableInClassList("hud-dim", !enemy);
            // Folded: the name, the health and the demolish button stay.
            _hud!.AddFold(_card, head, "card");

            _hp = HudKit.AddBar(_card, "hud-bar--hp");
            VisualElement hpRow = HudKit.Child(_card, "hud-card__hp");
            HudKit.Text(hpRow, "ПРОЧНОСТЬ", "hud-caption");
            _hpText = HudKit.Text(hpRow, "", "hud-card__hp-text");
            _state = HudKit.Text(_card, "", "hud-card__extra");
            _state.style.display = DisplayStyle.None;
            Label description = HudKit.Text(_card, b.Stats.Description, "hud-dim");
            description.AddToClassList("hud-card__desc");
            description.AddToClassList("hud-fold");
            if (!own)
            {
                VisualElement stats = HudKit.Child(_card, "hud-stats");
                stats.AddToClassList("hud-fold");
                foreach ((string caption, string value) in EntityTips.BuildingNumbers(b.Stats))
                    HudKit.Stat(stats, caption, value);
                if (b.Stats.Produces.Count > 0)
                {
                    var names2 = new string[b.Stats.Produces.Count];
                    for (int i = 0; i < names2.Length; i++)
                        names2[i] = DisplayNames.Unit(b.Stats.Produces[i]);
                    Label makes = HudKit.Text(_card, $"Делает: {string.Join(", ", names2)}", "hud-dim");
                    makes.AddToClassList("hud-card__desc");
                    makes.AddToClassList("hud-fold");
                }
            }
            else
            {
                _card.Add(_demolish!);
            }
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
            _runner.Submit(new DemolishCommand(_localPlayer!.PlayerId, simulation!.State.Tick, b.Id));
        }

        private Building? Selected(SimState state)
        {
            Building? b = state.FindBuilding(_selection!.SelectedBuildingId);
            return b != null && !b.IsDead && b.OwnerId == _localPlayer!.PlayerId ? b : null;
        }
    }
}
