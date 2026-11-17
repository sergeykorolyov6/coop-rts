using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // factory.md UI: for a selected own factory (or captured cave) — unit buttons from its "produces"
    // with icon, price and time (dimmed when its stock cannot pay or the queue is full), the stock that
    // belts fill (M2) as two bars, the queue as slots with the first unit's progress, a cancel button
    // that drops the last unit (full refund, in the sim), and a hint about the rally point.
    // factory-auto.md: a "repeat" switch; repeats not yet paid are dimmed, the first one waiting
    // for the stock says so.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class FactoryPanel : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        private readonly List<(CommandButton Button, UnitStats Stats)> _buttons = new List<(CommandButton, UnitStats)>();
        private readonly List<(VisualElement Slot, VisualElement Icon, VisualElement Fill)> _slots = new List<(VisualElement, VisualElement, VisualElement)>();
        private string _shownType = "";

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private VisualElement? _panel;
        private Label? _title;
        private VisualElement? _stock;
        private HudKit.Bar? _primaryBar, _secondaryBar;
        private Label? _primaryStock, _secondaryStock;
        private Label? _note;
        private VisualElement? _grid;
        private VisualElement? _queue;
        private Button? _cancel;
        private Button? _repeat;
        private bool? _shownRepeat;
        private string _shownNote = "";

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

            _panel = HudKit.Plate("FactoryPanel", "hud-commands");
            VisualElement title = HudKit.Child(_panel, "hud-commands__title");
            _title = HudKit.Text(title, "", "hud-caption");
            // Folded: the unit buttons and the queue; the stock and the hint go.
            _hud.AddFold(_panel, title, "factory");

            _stock = HudKit.Child(_panel, "hud-stock");
            _stock.AddToClassList("hud-fold");
            HudKit.Text(_stock, "СКЛАД", "hud-caption").style.marginRight = 10;
            HudKit.Child(_stock, "hud-res").AddToClassList("hud-res--primary");
            _primaryBar = HudKit.AddBar(_stock, "hud-stock__bar");
            _primaryBar.SetColor(new Color(0.93f, 0.63f, 0.30f));
            _primaryStock = HudKit.Text(_stock, "", "hud-price__value");
            HudKit.Child(_stock, "hud-res").AddToClassList("hud-res--secondary");
            _secondaryBar = HudKit.AddBar(_stock, "hud-stock__bar");
            _secondaryBar.SetColor(new Color(0.41f, 0.75f, 0.96f));
            _secondaryStock = HudKit.Text(_stock, "", "hud-price__value");
            _hud.AttachTip(_stock, () => new HudTip
            {
                Title = "Склад завода",
                Body = "Завод платит за юнитов из своего склада, а не из кошелька. Склад пополняют ленты от бура или склада, подведённые к заводу. "
                    + "Излишек уходит дальше по лентам, которые смотрят от здания. Стрелки на лентах рядом: зелёные — кормят завод, жёлтые — уносят излишек.",
            });

            _note = HudKit.Text(_panel, "", "hud-note");
            _note.AddToClassList("hud-fold");
            _grid = HudKit.Child(_panel, "hud-commands__grid");

            _queue = HudKit.Child(_panel, "hud-queue");
            for (int i = 0; i < simulation.State.Rules.FactoryQueue; i++)
            {
                VisualElement slot = HudKit.Child(_queue, "hud-slot");
                VisualElement icon = HudKit.Child(slot, "hud-slot__icon");
                icon.AddToClassList("hud-icon");
                VisualElement fill = HudKit.Child(slot, "hud-slot__fill");
                _slots.Add((slot, icon, fill));
            }
            _slots[0].Slot.AddToClassList("hud-slot--first");
            _cancel = new Button(Cancel) { text = "Отменить последний" };
            Hud.StyleButton(_cancel);
            _hud.AttachTip(_cancel, () => new HudTip { Title = "Отменить", Body = "Убирает последнего юнита из очереди; ресурс вернётся на склад полностью." });
            _queue.Add(_cancel);
            _repeat = new Button(ToggleRepeat) { text = "Повтор: выкл" };
            Hud.StyleButton(_repeat);
            _hud.AttachTip(_repeat, () => new HudTip
            {
                Title = "Повтор",
                Body = "Очередь идёт по кругу, пока хватает склада: готовый юнит снова встаёт в конец и оплачивается со склада, когда дойдёт его черёд.",
                Note = "Выключили — неоплаченные места уходят из очереди.",
            });
            _queue.Add(_repeat);

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
            if (factory.Stats.Id != _shownType)
                RebuildButtons(state, factory.Stats);
            IReadOnlyList<string> queue = factory.Factory!.Queue;
            bool queueFull = queue.Count >= state.Rules.FactoryQueue;
            Wallet wallet = factory.Stock ?? state.GetPlayer(_localPlayer!.PlayerId).Wallet;

            string name = DisplayNames.Building(factory.Stats.Id).ToUpperInvariant();
            string title = factory.IsComplete ? $"{name} · НАЙМ" : $"{name} · СТРОИТСЯ";
            if (_title!.text != title)
                _title.text = title;

            // Shown — by the style sheet: a folded panel hides it (Hud.uss .hud-folded).
            _stock!.style.display = factory.Stock != null ? StyleKeyword.Null : DisplayStyle.None;
            int primary = wallet.Get(Game.Sim.Resources.Primary), secondary = wallet.Secondary;
            if (factory.Stock != null)
            {
                int cap = Mathf.Max(1, state.Rules.FactoryStock);
                _primaryBar!.Set(primary / (float)cap);
                _secondaryBar!.Set(secondary / (float)cap);
                SetText(_primaryStock!, $"{primary}/{cap}");
                SetText(_secondaryStock!, $"{secondary}/{cap}");
            }

            // What to do next, in words: an empty stock is the usual reason a factory stands.
            bool empty = factory.Stock != null && primary + secondary == 0 && queue.Count == 0;
            bool waiting = queue.Count > 0 && !factory.Factory.Paid[0];
            string note = empty
                ? "Склад пуст: подведите к заводу ленту от бура или склада — без неё юнитов не на что делать."
                : waiting
                    ? $"Ждёт ресурса: на складе не хватает на {DisplayNames.Unit(queue[0]).ToLowerInvariant()}."
                : factory.Factory.HasRally
                    ? "Новые юниты идут к точке сбора. ПКМ по зданию — убрать её."
                    : "ПКМ по земле — точка сбора для новых юнитов.";
            if (note != _shownNote)
            {
                _shownNote = note;
                _note!.text = note;
                _note.EnableInClassList("hud-note--warning", empty || waiting);
            }

            foreach ((CommandButton button, UnitStats stats) in _buttons)
            {
                bool shortPrimary = primary < stats.CostPrimary, shortSecondary = secondary < stats.CostSecondary;
                HudKit.SetPrice(button.Price, stats.CostPrimary, stats.CostSecondary, shortPrimary, shortSecondary);
                button.SetBlocked(shortPrimary || shortSecondary || queueFull);
                int queued = 0;
                foreach (string id in queue)
                {
                    if (id == stats.Id)
                        queued++;
                }
                button.SetBadge(queued > 0 ? $"×{queued}" : "");
            }

            float share = queue.Count > 0 && state.Data.TryGetUnit(queue[0], out UnitStats? first) && first!.BuildTimeTicks > 0
                ? Mathf.Clamp01(factory.Factory.Progress / (float)first.BuildTimeTicks)
                : 0f;
            for (int i = 0; i < _slots.Count; i++)
            {
                (VisualElement slot, VisualElement icon, VisualElement fill) = _slots[i];
                bool used = i < queue.Count;
                icon.style.visibility = used ? UnityEngine.UIElements.Visibility.Visible : UnityEngine.UIElements.Visibility.Hidden;
                if (used)
                    HudKit.SetIcon(icon, queue[i], DisplayNames.Unit(queue[i]));
                fill.style.width = Length.Percent(i == 0 && used ? share * 100f : 0f);
                // factory-auto.md: a repeat not yet paid is dimmed.
                slot.style.opacity = used && !factory.Factory.Paid[i] ? 0.45f : 1f;
            }
            _cancel!.SetEnabled(queue.Count > 0);
            bool repeat = factory.Factory.Repeat;
            if (_shownRepeat != repeat)
            {
                _shownRepeat = repeat;
                _repeat!.text = repeat ? "Повтор: вкл" : "Повтор: выкл";
                // Lit while on: the queue goes round by itself.
                _repeat.style.color = repeat ? new StyleColor(new Color(0.55f, 0.95f, 0.65f)) : new StyleColor(StyleKeyword.Null);
            }
            _repeat!.SetEnabled(factory.IsComplete);
        }

        private static void SetText(Label label, string text)
        {
            if (label.text != text)
                label.text = text;
        }

        // The buttons follow the selected building's "produces": the factory and the cave differ.
        private void RebuildButtons(SimState state, BuildingStats building)
        {
            _shownType = building.Id;
            _grid!.Clear();
            _buttons.Clear();
            foreach (string unitId in building.Produces)
            {
                if (!state.Data.TryGetUnit(unitId, out UnitStats? stats))
                    continue;
                string id = unitId;
                var button = new CommandButton(() => Produce(id), id, stats!.NameRu, "");
                UnitStats tipStats = stats;
                _hud!.AttachTip(button.Button, () => EntityTips.Unit(tipStats, "", Stock()));
                _grid.Add(button.Button);
                _buttons.Add((button, stats));
            }
            HudKit.CloseGrid(_grid);
        }

        private Wallet? Stock()
        {
            Simulation? simulation = _runner!.Simulation;
            Building? factory = simulation != null ? SelectedFactory(simulation.State) : null;
            return factory == null ? null : factory.Stock ?? simulation!.State.GetPlayer(_localPlayer!.PlayerId).Wallet;
        }

        // A click with a full queue or an empty wallet still goes to the sim: it answers with
        // CommandRejected, which the notifications show (step 5).
        private void Produce(string unitId)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _selection!.SelectedBuildingId == 0)
                return;
            _runner!.Submit(new ProduceCommand(_localPlayer!.PlayerId, simulation.State.Tick, _selection.SelectedBuildingId, unitId));
        }

        private void Cancel()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _selection!.SelectedBuildingId == 0)
                return;
            _runner!.Submit(new CancelProductionCommand(_localPlayer!.PlayerId, simulation.State.Tick, _selection.SelectedBuildingId));
        }

        private void ToggleRepeat()
        {
            Simulation? simulation = _runner!.Simulation;
            Building? factory = simulation != null ? SelectedFactory(simulation.State) : null;
            if (factory == null)
                return;
            _runner!.Submit(new SetRepeatCommand(_localPlayer!.PlayerId, simulation!.State.Tick, factory.Id, !factory.Factory!.Repeat));
        }

        private Building? SelectedFactory(SimState state)
        {
            Building? building = state.FindBuilding(_selection!.SelectedBuildingId);
            return building != null && !building.IsDead && building.Factory != null && building.OwnerId == _localPlayer!.PlayerId ? building : null;
        }
    }
}
