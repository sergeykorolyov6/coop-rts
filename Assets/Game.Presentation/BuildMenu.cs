using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Build menu, always shown in a mission (build-mode.md: the player has one builder, so there is
    // nothing to select first): a command button per building the local player's faction builds (factions.csv, in its order) with its icon, price and hotkey,
    // dimmed when the wallet cannot pay (the sim pays on start, so this is a hint, not a lock:
    // the button still works and the click is rejected with a reason).
    [RequireComponent(typeof(SimRunner), typeof(PlacementController))]
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
            new Entry { BuildingId = BuildingIds.SmallDrill, Hotkey = Key.N },
            new Entry { BuildingId = BuildingIds.Booster, Hotkey = Key.P },
        };

        private readonly List<(CommandButton Button, BuildingStats Stats, Key Key)> _buttons = new List<(CommandButton, BuildingStats, Key)>();

        private SimRunner? _runner;
        private PlacementController? _placement;
        private AbilityPanel? _abilities;
        private Guide? _guide;
        private GameMenu? _menu;
        private VisualElement? _panel;
        private VisualElement? _grid;
        private Label? _hint;
        private string _shownHint = "";
        private string _shownFaction = "";

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _placement = GetComponent<PlacementController>();
            _abilities = GetComponent<AbilityPanel>();
            _guide = GetComponent<Guide>();
            _menu = GetComponent<GameMenu>();
        }

        private void Start()
        {
            if (_hud?.BottomBar == null || _runner!.Simulation == null)
                return;
            _panel = HudKit.Plate("BuildMenu", "hud-commands");
            VisualElement title = HudKit.Child(_panel, "hud-commands__title");
            HudKit.Text(title, "СТРОЙКА", "hud-caption");
            _hint = HudKit.Text(title, "", "hud-caption");
            _grid = HudKit.Child(_panel, "hud-commands__grid");
            _hud.BottomBar.Add(_panel);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_panel == null || simulation == null || _localPlayer == null)
                return;

            // A replay has no local player to build for.
            Player player = simulation.State.GetPlayer(_localPlayer.PlayerId);
            bool show = !_runner.IsReplay && player.Faction != null;
            _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
                return;

            // Ctrl+F1–F4 may switch to a player of another faction.
            if ((player.Faction?.Id ?? "") != _shownFaction)
                Rebuild(simulation.State, player.Faction);
            Wallet wallet = player.Wallet;
            foreach ((CommandButton button, BuildingStats stats, _) in _buttons)
            {
                bool shortPrimary = wallet.Get(Game.Sim.Resources.Primary) < stats.CostPrimary;
                bool shortSecondary = wallet.Secondary < stats.CostSecondary;
                HudKit.SetPrice(button.Price, stats.CostPrimary, stats.CostSecondary, shortPrimary, shortSecondary);
                button.SetBlocked(shortPrimary || shortSecondary);
                button.SetActive(_placement!.BuildingId == stats.Id);
            }

            // What the mouse does now: nothing is said about cancelling until there is something to cancel.
            string hint = _placement!.IsActive ? "ЛКМ — ПОСТАВИТЬ ЕЩЁ · R — ПОВЕРНУТЬ · ПКМ / ESC — ЗАКОНЧИТЬ" : "ВЫБЕРИТЕ ЗДАНИЕ — КНОПКОЙ ИЛИ КЛАВИШЕЙ";
            if (hint != _shownHint)
            {
                _shownHint = hint;
                _hint!.text = hint;
            }

            Keyboard? keyboard = Keyboard.current;
            // Over the help or the pause menu the game's keys wait.
            if (keyboard == null || (_guide != null && _guide.IsBusy) || (_menu != null && _menu.IsOpen))
                return;
            foreach ((_, BuildingStats stats, Key key) in _buttons)
            {
                if (key != Key.None && keyboard[key].wasPressedThisFrame)
                {
                    Begin(stats.Id);
                    return;
                }
            }
        }

        // A building key or button ends an ability's aiming: the mouse goes to the silhouette.
        private void Begin(string buildingId)
        {
            if (_abilities != null)
                _abilities.CancelTargeting();
            _placement!.Begin(buildingId);
        }

        private void Rebuild(SimState state, FactionStats? faction)
        {
            _shownFaction = faction?.Id ?? "";
            _grid!.Clear();
            _buttons.Clear();
            if (faction == null)
                return;
            foreach (string id in faction.Buildings)
            {
                if (!state.Data.TryGetBuilding(id, out BuildingStats? stats))
                    continue;
                Key key = HotkeyOf(stats!);
                string hotkey = key != Key.None ? key.ToString() : "";
                var button = new CommandButton(() => Begin(id), id, stats!.NameRu, hotkey);
                BuildingStats tipStats = stats;
                _hud!.AttachTip(button.Button, () => EntityTips.Building(tipStats, hotkey, CurrentWallet()));
                _grid.Add(button.Button);
                _buttons.Add((button, stats, key));
            }
        }

        private Wallet? CurrentWallet()
        {
            Simulation? simulation = _runner!.Simulation;
            return simulation != null && _localPlayer != null ? simulation.State.GetPlayer(_localPlayer.PlayerId).Wallet : null;
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
    }
}
