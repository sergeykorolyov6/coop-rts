using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Build menu while own workers are selected: a button per building with its price and hotkey,
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
        [Tooltip("Buildings of the player's menu (PLAN.md: M1 + the turret of M2).")]
        [SerializeField] private Entry[] _entries =
        {
            new Entry { BuildingId = BuildingKinds.Drill, Hotkey = Key.D },
            new Entry { BuildingId = BuildingKinds.Conveyor, Hotkey = Key.C },
            new Entry { BuildingId = BuildingKinds.ConveyorBridge, Hotkey = Key.B },
            new Entry { BuildingId = BuildingKinds.Storage, Hotkey = Key.S },
            new Entry { BuildingId = BuildingKinds.Factory, Hotkey = Key.F },
            new Entry { BuildingId = BuildingKinds.Headquarters, Hotkey = Key.H },
            new Entry { BuildingId = "turret", Hotkey = Key.T },
        };
        [SerializeField] private Color _activeColor = new Color(0.25f, 0.45f, 0.30f);

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<bool> _affordable = new List<bool>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private PlacementController? _placement;
        private VisualElement? _panel;
        private StyleColor _normalColor;

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
            Hud.Panelise(_panel);
            foreach (Entry entry in _entries)
            {
                if (!_runner.Simulation.State.Data.TryGetBuilding(entry.BuildingId, out BuildingStats? stats))
                    continue;
                string id = entry.BuildingId;
                string price = stats!.CostSecondary > 0 ? $"{stats.CostPrimary} + {stats.CostSecondary}" : $"{stats.CostPrimary}";
                var button = new Button(() => _placement!.Begin(id))
                {
                    text = $"{DisplayNames.Building(id)}\n{price} · {entry.Hotkey}",
                    userData = stats,
                };
                button.style.width = 92;
                button.style.height = 52;
                button.style.whiteSpace = WhiteSpace.Normal;
                Hud.StyleButton(button);
                _panel.Add(button);
                _buttons.Add(button);
                _affordable.Add(true);
            }
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

            Wallet wallet = simulation.State.GetPlayer(_localPlayer.PlayerId).Wallet;
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
            foreach (Entry entry in _entries)
            {
                if (entry.Hotkey != Key.None && keyboard[entry.Hotkey].wasPressedThisFrame)
                {
                    _placement!.Begin(entry.BuildingId);
                    return;
                }
            }
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
