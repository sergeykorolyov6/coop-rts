using System.Collections.Generic;
using System.Linq;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // factions.md UI: a button per ability the selected own units have (Z, X, C, V), with how many
    // of them have it ready, the cooldown left or the hero's level it opens at, and its description
    // on hover. Self abilities go at once; point abilities wait for a left click on the ground with a
    // ring of their area under the cursor (right click or Esc cancels). A hero without a path shows
    // the three paths to choose from instead.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class AbilityPanel : MonoBehaviour
    {
        // W, A, S, D, Q, E belong to the camera, R rotates a building being placed.
        private static readonly Key[] Hotkeys = { Key.Z, Key.X, Key.C, Key.V };
        private const int RingSegments = 24;

        [SerializeField] private Hud? _hud;
        [SerializeField] private RtsCamera? _camera;
        [SerializeField] private Color _ringColor = new Color(0.98f, 0.85f, 0.30f);

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<string> _shown = new List<string>();
        private readonly List<string> _abilities = new List<string>();
        private readonly List<Button> _pathButtons = new List<Button>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private VisualElement? _panel;
        private VisualElement? _paths;
        private GroundDashes? _ring;
        private string? _targeting;
        private int _endedFrame = -1;

        // Choosing a point: the mouse belongs to the panel (selection and orders wait).
        public bool IsBusy => _targeting != null || _endedFrame == Time.frameCount;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
            _ring = new GroundDashes(transform, "AbilityArea", _ringColor, 0.12f);
        }

        private void Start()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_hud?.BottomBar == null || simulation == null)
                return;
            _panel = new VisualElement { name = "AbilityPanel" };
            _panel.style.flexDirection = FlexDirection.Row;
            Hud.Panelise(_panel);
            _panel.style.display = DisplayStyle.None;
            _hud.BottomBar.Add(_panel);

            _paths = new VisualElement { name = "HeroPaths" };
            _paths.style.flexDirection = FlexDirection.Row;
            _paths.Add(new Label("Путь героя:") { pickingMode = PickingMode.Ignore });
            foreach (HeroPathStats path in simulation.State.Data.HeroPaths)
            {
                HeroPathStats p = path;
                var button = new Button(() => Choose(p.Id)) { text = p.NameRu, userData = p.HeroUnitId };
                button.style.width = 110;
                button.style.height = 52;
                Hud.StyleButton(button);
                _hud.AttachTip(button, () => $"{p.NameRu}: {p.Description}\n{AbilityList(simulation.State, p)}");
                _paths.Add(button);
                _pathButtons.Add(button);
            }
            _panel.Add(_paths);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            _ring!.Begin();
            if (_panel == null || simulation == null)
            {
                _ring.End();
                return;
            }
            SimState state = simulation.State;
            Unit? pathless = Collect(state);
            ShowPaths(state, pathless);
            _panel.style.display = _abilities.Count > 0 || pathless != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (_abilities.Count == 0)
                EndTargeting();
            Sync(state);
            HandleInput(simulation);
            _ring.End();
        }

        private void HandleInput(Simulation simulation)
        {
            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;
            if (_targeting != null)
            {
                SimState state = simulation.State;
                Vector2 pointer = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
                _hud?.ShowTip($"{AbilityName(state, _targeting)}: ЛКМ — куда, ПКМ/Esc — отмена", pointer);
                if (_camera != null && _camera.GroundPoint(pointer, out Vector3 aim) && state.Data.TryGetAbility(_targeting, out AbilityStats? a) && a!.RadiusMilli > 0)
                    Circle(aim, a.RadiusMilli / (float)Game.Sim.Grid.MilliPerCell);
                if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
                {
                    EndTargeting();
                    return;
                }
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && _camera != null
                    && !(_hud != null && _hud.IsPointerOverUi(pointer)) && _camera.GroundPoint(pointer, out Vector3 ground))
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
            if (ability!.AtPoint)
                _targeting = abilityId;
            else
                Issue(simulation, abilityId, 0, 0);
        }

        private void Choose(string pathId)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation != null)
                simulation.Enqueue(new ChooseHeroPathCommand(_localPlayer!.PlayerId, simulation.State.Tick, pathId));
        }

        private void Issue(Simulation simulation, string abilityId, int x, int y)
        {
            var ids = new List<int>();
            foreach (int id in _selection!.Selected)
            {
                Unit? u = simulation.State.FindUnit(id);
                if (u != null && simulation.State.AbilitiesOf(u).Contains(abilityId))
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

        // Abilities of the selection in the order they first appear; returns a selected hero whose
        // owner has not chosen a path yet.
        private Unit? Collect(SimState state)
        {
            _abilities.Clear();
            Unit? pathless = null;
            foreach (int id in _selection!.Selected)
            {
                Unit? u = state.FindUnit(id);
                if (u == null || u.IsDead)
                    continue;
                if (u.IsHero && state.GetPlayer(u.OwnerId).HeroPathId.Length == 0)
                    pathless = u;
                foreach (string a in state.AbilitiesOf(u))
                {
                    if (!_abilities.Contains(a))
                        _abilities.Add(a);
                }
            }
            return pathless;
        }


        private void ShowPaths(SimState state, Unit? pathless)
        {
            _paths!.style.display = pathless != null ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (Button b in _pathButtons)
                b.style.display = pathless != null && (string)b.userData == pathless.Stats.Id ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Sync(SimState state)
        {
            while (_buttons.Count < _abilities.Count)
            {
                int index = _buttons.Count;
                var button = new Button(() => { if (index < _abilities.Count) Press(_abilities[index]); });
                button.style.width = 120;
                button.style.height = 52;
                button.style.whiteSpace = WhiteSpace.Normal;
                Hud.StyleButton(button);
                _hud!.AttachTip(button, () => index < _abilities.Count ? Description(state, _abilities[index]) : "");
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
                int index = u != null && state.AbilitiesOf(u).Contains(abilityId) ? u.Stats.AbilityIndex(abilityId) : -1;
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

        private static string Description(SimState state, string abilityId) =>
            state.Data.TryGetAbility(abilityId, out AbilityStats? a) ? $"{a!.NameRu}: {a.Description}" : abilityId;

        private static string AbilityList(SimState state, HeroPathStats path)
        {
            var lines = new List<string>();
            foreach (string id in path.Abilities)
            {
                if (state.Data.TryGetAbility(id, out AbilityStats? a))
                    lines.Add($"• {a!.NameRu} (с ур. {a.MinLevel}): {a.Description}");
            }
            return string.Join("\n", lines);
        }

        private static string AbilityName(SimState state, string abilityId) =>
            state.Data.TryGetAbility(abilityId, out AbilityStats? a) ? a!.NameRu : abilityId;

        private void Circle(Vector3 centre, float radius)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float a = (i + 0.15f) / RingSegments * Mathf.PI * 2f, b = (i + 0.85f) / RingSegments * Mathf.PI * 2f;
                _ring!.Dash(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius, centre + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius);
            }
        }


        private void OnDestroy() => _ring?.Destroy();
    }
}
