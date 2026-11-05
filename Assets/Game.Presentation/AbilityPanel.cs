using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // factions.md UI: a button per ability the selected own units have (Q, E, X), with how many of
    // them have it ready and the shortest cooldown left. A boost goes at once; Blink waits for a
    // left click on the ground (right click or Esc cancels). All selected units with the ability use it.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class AbilityPanel : MonoBehaviour
    {
        // R rotates a building being placed; X stays free of it.
        private static readonly Key[] Hotkeys = { Key.Q, Key.E, Key.X };

        [SerializeField] private Hud? _hud;
        [SerializeField] private RtsCamera? _camera;

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<string> _shown = new List<string>();
        private readonly List<string> _abilities = new List<string>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private VisualElement? _panel;
        private string? _targeting;
        private int _endedFrame = -1;

        // Choosing a point for Blink: the mouse belongs to the panel (selection and orders wait).
        public bool IsBusy => _targeting != null || _endedFrame == Time.frameCount;

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
            _panel = new VisualElement { name = "AbilityPanel" };
            _panel.style.flexDirection = FlexDirection.Row;
            Hud.Panelise(_panel);
            _panel.style.display = DisplayStyle.None;
            _hud.BottomBar.Add(_panel);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_panel == null || simulation == null)
                return;
            SimState state = simulation.State;
            Collect(state);
            _panel.style.display = _abilities.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_abilities.Count == 0)
            {
                EndTargeting();
                return;
            }
            Sync(state);

            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;
            if (_targeting != null)
            {
                _hud?.ShowTip($"{AbilityName(state, _targeting)}: ЛКМ — куда, ПКМ/Esc — отмена", mouse != null ? mouse.position.ReadValue() : Vector2.zero);
                if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
                {
                    EndTargeting();
                    return;
                }
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && _camera != null
                    && !(_hud != null && _hud.IsPointerOverUi(mouse.position.ReadValue()))
                    && _camera.GroundPoint(mouse.position.ReadValue(), out Vector3 ground))
                {
                    Issue(simulation, _targeting, Mathf.RoundToInt(ground.x * Game.Sim.Grid.MilliPerCell), Mathf.RoundToInt(ground.z * Game.Sim.Grid.MilliPerCell));
                    EndTargeting();
                }
                return;
            }
            for (int i = 0; i < _abilities.Count && i < Hotkeys.Length; i++)
            {
                if (keyboard != null && keyboard[Hotkeys[i]].wasPressedThisFrame)
                    Press(_abilities[i]);
            }
        }

        private void Press(string abilityId)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || !simulation.State.Data.TryGetAbility(abilityId, out AbilityStats? ability))
                return;
            if (ability!.Kind == AbilityKind.Blink)
                _targeting = abilityId;
            else
                Issue(simulation, abilityId, 0, 0);
        }

        private void Issue(Simulation simulation, string abilityId, int x, int y)
        {
            var ids = new List<int>();
            foreach (int id in _selection!.Selected)
            {
                Unit? u = simulation.State.FindUnit(id);
                if (u != null && Index(u.Stats, abilityId) >= 0)
                    ids.Add(id);
            }
            if (ids.Count > 0)
                simulation.Enqueue(new UseAbilityCommand(_localPlayer!.PlayerId, simulation.State.Tick, ids.ToArray(), abilityId, x, y));
        }

        private void EndTargeting()
        {
            if (_targeting == null)
                return;
            _targeting = null;
            _endedFrame = Time.frameCount;
            _hud?.ShowTip(null, Vector2.zero);
        }

        // Abilities of the selection in the order they first appear.
        private void Collect(SimState state)
        {
            _abilities.Clear();
            foreach (int id in _selection!.Selected)
            {
                Unit? u = state.FindUnit(id);
                if (u == null || u.IsDead)
                    continue;
                foreach (string a in u.Stats.Abilities)
                {
                    if (!_abilities.Contains(a))
                        _abilities.Add(a);
                }
            }
        }

        private void Sync(SimState state)
        {
            while (_buttons.Count < _abilities.Count)
            {
                int index = _buttons.Count;
                var button = new Button(() => { if (index < _abilities.Count) Press(_abilities[index]); });
                // Wide enough for "Воодушевление (E)" on one line.
                button.style.width = 140;
                button.style.height = 52;
                button.style.whiteSpace = WhiteSpace.Normal;
                Hud.StyleButton(button);
                _panel!.Add(button);
                _buttons.Add(button);
                _shown.Add("");
            }
            for (int i = 0; i < _buttons.Count; i++)
            {
                bool used = i < _abilities.Count;
                _buttons[i].style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used)
                    continue;
                string text = Describe(state, _abilities[i], i, out bool ready);
                if (text != _shown[i])
                {
                    _shown[i] = text;
                    _buttons[i].text = text;
                }
                _buttons[i].style.opacity = ready ? 1f : 0.45f;
            }
        }

        private string Describe(SimState state, string abilityId, int slot, out bool ready)
        {
            int have = 0, readyCount = 0, soonest = int.MaxValue, needLevel = 0;
            int minLevel = state.Data.TryGetAbility(abilityId, out AbilityStats? stats) ? stats!.MinLevel : 1;
            foreach (int id in _selection!.Selected)
            {
                Unit? u = state.FindUnit(id);
                int index = u != null ? Index(u.Stats, abilityId) : -1;
                if (index < 0)
                    continue;
                have++;
                // factions.md: a hero's ability opens at its level.
                int level = u!.IsHero ? state.GetPlayer(u.OwnerId).HeroLevel : 1;
                if (level < minLevel)
                {
                    needLevel = minLevel;
                    continue;
                }
                int cooldown = u.AbilityCooldown(index);
                if (cooldown == 0)
                    readyCount++;
                else if (cooldown < soonest)
                    soonest = cooldown;
            }
            ready = readyCount > 0;
            string key = slot < Hotkeys.Length ? $" ({Hotkeys[slot]})" : "";
            string status = ready ? $"готово: {readyCount}/{have}"
                : soonest != int.MaxValue ? $"через {DisplayNames.Seconds(soonest)}"
                : needLevel > 0 ? $"с ур. {needLevel}" : "не готово";
            return $"{AbilityName(state, abilityId)}{key}\n{status}";
        }

        private static string AbilityName(SimState state, string abilityId) =>
            state.Data.TryGetAbility(abilityId, out AbilityStats? a) ? a!.NameRu : abilityId;

        private static int Index(UnitStats stats, string abilityId)
        {
            for (int i = 0; i < stats.Abilities.Count; i++)
            {
                if (stats.Abilities[i] == abilityId)
                    return i;
            }
            return -1;
        }
    }
}
