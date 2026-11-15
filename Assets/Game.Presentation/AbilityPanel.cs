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

        private readonly List<CommandButton> _buttons = new List<CommandButton>();
        private readonly List<string> _abilities = new List<string>();
        private readonly List<Button> _pathButtons = new List<Button>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private VisualElement? _panel;
        private VisualElement? _paths;
        private VisualElement? _grid;
        private Label? _title;
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
            _panel = HudKit.Plate("AbilityPanel", "hud-commands");
            VisualElement title = HudKit.Child(_panel, "hud-commands__title");
            _title = HudKit.Text(title, "СПОСОБНОСТИ", "hud-caption");
            _panel.style.display = DisplayStyle.None;
            _hud.BottomBar.Add(_panel);
            _grid = HudKit.Child(_panel, "hud-commands__grid");

            _paths = HudKit.Child(_panel, "hud-commands__grid");
            _paths.name = "HeroPaths";
            foreach (HeroPathStats path in simulation.State.Data.HeroPaths)
            {
                HeroPathStats p = path;
                // The path's look is its first ability's icon.
                string icon = p.Abilities.Count > 0 ? p.Abilities[0] : p.Id;
                var button = new CommandButton(() => Choose(p.Id), icon, p.NameRu, "");
                button.Button.userData = p.HeroUnitId;
                _hud.AttachTip(button.Button, () => new HudTip { Title = $"Путь: {p.NameRu}", Body = $"{p.Description}\n{AbilityList(simulation.State, p)}", Note = "Выбирается один раз на игру." });
                _paths.Add(button.Button);
                _pathButtons.Add(button.Button);
            }
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
                    Circle(aim, Ground.Cells(a.RadiusMilli));
                if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
                {
                    EndTargeting();
                    return;
                }
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && _camera != null
                    && !(_hud != null && _hud.IsPointerOverUi(pointer)) && _camera.GroundPoint(pointer, out Vector3 ground))
                {
                    Issue(simulation, _targeting, Ground.Milli(ground.x), Ground.Milli(ground.z));
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
                _runner!.Submit(new ChooseHeroPathCommand(_localPlayer!.PlayerId, simulation.State.Tick, pathId));
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
                _runner!.Submit(new UseAbilityCommand(_localPlayer!.PlayerId, simulation.State.Tick, ids.ToArray(), abilityId, x, y));
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
            string title = pathless != null && _abilities.Count == 0 ? "ВЫБЕРИТЕ ПУТЬ ГЕРОЯ" : "СПОСОБНОСТИ";
            if (_title!.text != title)
                _title.text = title;
            foreach (Button b in _pathButtons)
                b.style.display = pathless != null && (string)b.userData == pathless.Stats.Id ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Sync(SimState state)
        {
            while (_buttons.Count < _abilities.Count)
            {
                int index = _buttons.Count;
                string hotkey = index < Hotkeys.Length ? Hotkeys[index].ToString() : "";
                var button = new CommandButton(() => { if (index < _abilities.Count) Press(_abilities[index]); }, "", "", hotkey);
                _hud!.AttachTip(button.Button, () => index < _abilities.Count && state.Data.TryGetAbility(_abilities[index], out AbilityStats? a)
                    ? EntityTips.Ability(a!, hotkey)
                    : new HudTip());
                _grid!.Add(button.Button);
                _buttons.Add(button);
            }
            for (int i = 0; i < _buttons.Count; i++)
            {
                bool used = i < _abilities.Count;
                _buttons[i].Button.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used)
                    continue;
                string id = _abilities[i];
                string name = AbilityName(state, id);
                _buttons[i].SetIcon(id, name);
                _buttons[i].SetName(name);
                Show(state, _buttons[i], id);
                _buttons[i].SetActive(_targeting == id);
            }
        }

        // How many of the selected have it ready ("2/3"), the cooldown left over the icon, or the
        // hero's level it opens at.
        private void Show(SimState state, CommandButton button, string abilityId)
        {
            int have = 0, readyCount = 0, soonest = int.MaxValue, needLevel = 0;
            AbilityStats? stats = state.Data.TryGetAbility(abilityId, out AbilityStats? found) ? found : null;
            int minLevel = stats?.MinLevel ?? 1;
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
            bool ready = readyCount > 0;
            button.SetBlocked(!ready);
            button.SetBadge(have > 1 ? $"{readyCount}/{have}" : "");
            if (ready || soonest == int.MaxValue)
            {
                button.SetCooldown(needLevel > 0 && !ready ? 1f : 0f, needLevel > 0 && !ready ? $"ур. {needLevel}" : "");
                return;
            }
            float share = stats != null && stats.CooldownTicks > 0 ? soonest / (float)stats.CooldownTicks : 1f;
            button.SetCooldown(share, Mathf.CeilToInt(soonest / (float)Simulation.TicksPerSecond).ToString());
        }

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
