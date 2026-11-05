using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Build menu while own builders are selected: a button per building the local player's faction
    // builds (factions.csv, in its order) with its price and hotkey,
    // grey when the wallet cannot pay (the sim pays on start, so this is a hint, not a lock:
    // the button still works and the click is rejected with a reason).
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(PlacementController))]
    public sealed class BuildMenu : MonoBehaviour
    {
        [Serializable]
        private sealed class Entry
        {
            public string BuildingId = "";
            public Key Hotkey = Key.None;
        }

        [SerializeField] private Hud? _hud;
        [SerializeField] private LocalPlayer? _localPlayer;
        [Tooltip("Hotkeys by building id, or by its model kind (PrimitiveModels.Shape): another faction's factory gets F too.")]
        [SerializeField] private Entry[] _hotkeys =
        {
            // W, A, S, D, Q, E move and turn the camera; Z, X, C, V are the abilities'.
            new Entry { BuildingId = BuildingIds.Drill, Hotkey = Key.B },
            new Entry { BuildingId = BuildingIds.Conveyor, Hotkey = Key.L },
            new Entry { BuildingId = BuildingIds.ConveyorBridge, Hotkey = Key.M },
            new Entry { BuildingId = BuildingIds.Storage, Hotkey = Key.K },
            new Entry { BuildingId = BuildingIds.Factory, Hotkey = Key.F },
            new Entry { BuildingId = BuildingIds.Headquarters, Hotkey = Key.H },
            new Entry { BuildingId = BuildingIds.Turret, Hotkey = Key.T },
            new Entry { BuildingId = BuildingIds.Wall, Hotkey = Key.Y },
            new Entry { BuildingId = "bs_drill_small", Hotkey = Key.N },
            new Entry { BuildingId = BuildingIds.Booster, Hotkey = Key.P },
        };
        [SerializeField] private Color _activeColor = new Color(0.25f, 0.45f, 0.30f);

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<bool> _affordable = new List<bool>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private PlacementController? _placement;
        private VisualElement? _panel;
        private StyleColor _normalColor;
        private string _shownFaction = "";

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _placement = GetComponent<PlacementController>();
        }

        private void Start()
        {
            if (_hud?.BottomBar == null || _runner!.Simulation == null)
                return;
            _panel = new VisualElement { name = "BuildMenu" };
            _panel.style.flexDirection = FlexDirection.Row;
            // Wider than the bar (many buildings, a narrow window): the buttons wrap into two rows.
            _panel.style.flexWrap = Wrap.Wrap;
            _panel.style.flexShrink = 1;
            Hud.Panelise(_panel);
            _normalColor = Hud.ButtonColor;
            _hud.BottomBar.Add(_panel);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_panel == null || simulation == null || _localPlayer == null)
                return;

            bool show = HasWorkers(simulation.State);
            _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
                return;

            Player player = simulation.State.GetPlayer(_localPlayer.PlayerId);
            // F1–F4 may switch to a player of another faction.
            if ((player.Faction?.Id ?? "") != _shownFaction)
                Rebuild(simulation.State, player.Faction);
            Wallet wallet = player.Wallet;
            for (int i = 0; i < _buttons.Count; i++)
            {
                var stats = (BuildingStats)_buttons[i].userData;
                bool affordable = wallet.CanPay(stats.CostPrimary, stats.CostSecondary);
                if (affordable != _affordable[i])
                {
                    _affordable[i] = affordable;
                    _buttons[i].style.opacity = affordable ? 1f : 0.45f;
                }
                _buttons[i].style.backgroundColor = _placement!.BuildingId == stats.Id ? new StyleColor(_activeColor) : _normalColor;
            }

            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            foreach (Button button in _buttons)
            {
                var stats = (BuildingStats)button.userData;
                Key key = HotkeyOf(stats);
                if (key != Key.None && keyboard[key].wasPressedThisFrame)
                {
                    _placement!.Begin(stats.Id);
                    return;
                }
            }
        }

        private void Rebuild(SimState state, FactionStats? faction)
        {
            _shownFaction = faction?.Id ?? "";
            foreach (Button old in _buttons)
                old.RemoveFromHierarchy();
            _buttons.Clear();
            _affordable.Clear();
            if (faction == null)
                return;
            foreach (string id in faction.Buildings)
            {
                if (!state.Data.TryGetBuilding(id, out BuildingStats? stats))
                    continue;
                string price = stats!.CostSecondary > 0 ? $"{stats.CostPrimary} + {stats.CostSecondary}" : $"{stats.CostPrimary}";
                Key key = HotkeyOf(stats);
                var button = new Button(() => _placement!.Begin(id))
                {
                    text = $"{DisplayNames.Building(id)}\n{price}" + (key != Key.None ? $" · {key}" : ""),
                    userData = stats,
                };
                // Wide enough for the longest name on one line ("Мастерская", "Сторожевая").
                button.style.width = 108;
                button.style.height = 52;
                button.style.whiteSpace = WhiteSpace.Normal;
                Hud.StyleButton(button);
                BuildingStats tipStats = stats;
                _hud!.AttachTip(button, () => $"{tipStats.NameRu}: {tipStats.Description}");
                _panel!.Add(button);
                _buttons.Add(button);
                _affordable.Add(true);
            }
        }

        private Key HotkeyOf(BuildingStats stats)
        {
            string shape = PrimitiveModels.Shape(stats);
            foreach (Entry entry in _hotkeys)
            {
                if (entry.BuildingId == stats.Id)
                    return entry.Hotkey;
            }
            foreach (Entry entry in _hotkeys)
            {
                if (entry.BuildingId == shape)
                    return entry.Hotkey;
            }
            return Key.None;
        }

        private bool HasWorkers(SimState state)
        {
            foreach (int id in _selection!.Selected)
            {
                if (state.FindUnit(id)?.Worker != null)
                    return true;
            }
            return false;
        }
    }
}
