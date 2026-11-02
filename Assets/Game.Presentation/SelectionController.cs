using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Which of the local player's units are selected. Left click picks one, dragging a box picks
    // all inside, Shift adds. A click on an own building with no unit under the cursor selects
    // the building instead (the factory panel works on it). Groups as in SC2: Ctrl+digit sets, Shift+digit adds, digit recalls,
    // the digit twice centres the camera. Only own units: allies' units are not commandable.
    // Pure Presentation state: the sim never learns what is selected, commands carry the unit ids.
    [RequireComponent(typeof(SimRunner), typeof(EntityView), typeof(LocalPlayer))]
    public sealed class SelectionController : MonoBehaviour
    {
        private const int GroupCount = 10;

        [SerializeField] private RtsCamera? _camera;
        [Tooltip("The selection box is drawn on the HUD; clicks on its buttons are not map clicks.")]
        [SerializeField] private Hud? _hud;
        [SerializeField] private PlacementController? _placement;
        [Tooltip("A press that moves less than this is a click, not a box.")]
        [SerializeField] private float _dragThresholdPixels = 6f;
        [Tooltip("Extra pick radius around a unit, pixels: small units stay clickable when zoomed out.")]
        [SerializeField] private float _pickSlackPixels = 6f;
        [SerializeField] private float _doubleTapSeconds = 0.35f;

        private readonly List<int> _selected = new List<int>();
        private readonly List<int>[] _groups = new List<int>[GroupCount];
        private readonly List<int> _scratch = new List<int>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private LocalPlayer? _localPlayer;
        private Camera? _view;
        private VisualElement? _box;
        private Vector2 _pressAt;
        private bool _pressing;
        private int _lastGroup = -1;
        private float _lastGroupTime;

        public IReadOnlyList<int> Selected => _selected;

        // Own building selected by a click, 0 when none; exclusive with the unit selection.
        public int SelectedBuildingId { get; private set; }

        public event Action? Changed;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _localPlayer = GetComponent<LocalPlayer>();
            for (int i = 0; i < GroupCount; i++)
                _groups[i] = new List<int>();
            if (_camera != null)
                _view = _camera.GetComponent<Camera>();
        }

        private void OnEnable() => _localPlayer!.Changed += OnPlayerChanged;

        private void OnDisable() => _localPlayer!.Changed -= OnPlayerChanged;

        private void Start()
        {
            if (_hud?.Root == null)
                return;
            _box = new VisualElement { name = "SelectionBox", pickingMode = PickingMode.Ignore };
            _box.style.position = Position.Absolute;
            _box.style.backgroundColor = new Color(0.4f, 1f, 0.4f, 0.12f);
            _box.style.borderTopWidth = _box.style.borderBottomWidth = _box.style.borderLeftWidth = _box.style.borderRightWidth = 1f;
            _box.style.borderTopColor = _box.style.borderBottomColor = _box.style.borderLeftColor = _box.style.borderRightColor = new Color(0.4f, 1f, 0.4f, 0.9f);
            _box.style.display = DisplayStyle.None;
            _hud.Root.Add(_box);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null || _view == null)
                return;

            DropGone(simulation.State, _selected, notify: true);
            if (SelectedBuildingId != 0)
            {
                Building? building = simulation.State.FindBuilding(SelectedBuildingId);
                if (building == null || building.IsDead || building.OwnerId != _localPlayer!.PlayerId)
                    SetBuilding(0);
            }
            HandleGroups(simulation.State);
            HandleMouse(simulation.State);
        }

        // Own unit drawn under the screen point, or null. Used for orders too: what the player
        // clicks is what they see, interpolation included.
        public Unit? UnitAt(SimState state, Vector2 screen, Func<Unit, bool> filter)
        {
            if (_view == null)
                return null;
            Unit? best = null;
            float bestDistance = float.MaxValue;
            foreach (Unit unit in state.Units)
            {
                if (unit.IsDead || !filter(unit) || !ScreenCircle(unit, out Vector2 centre, out float radius))
                    continue;
                float distance = Vector2.Distance(centre, screen);
                if (distance <= radius + _pickSlackPixels && distance < bestDistance)
                {
                    best = unit;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void HandleMouse(SimState state)
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
                return;
            Vector2 pointer = mouse.position.ReadValue();

            // Placing a building owns the mouse; a press on a button is not a map click.
            if (mouse.leftButton.wasPressedThisFrame && !(_placement != null && _placement.IsBusy) && !(_hud != null && _hud.IsPointerOverUi(pointer)))
            {
                _pressing = true;
                _pressAt = pointer;
            }
            if (!_pressing)
                return;

            bool dragging = Vector2.Distance(_pressAt, pointer) >= _dragThresholdPixels;
            ShowBox(dragging, _pressAt, pointer);
            if (mouse.leftButton.isPressed)
                return;

            _pressing = false;
            ShowBox(false, _pressAt, pointer);
            bool add = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            int playerId = _localPlayer!.PlayerId;

            _scratch.Clear();
            if (dragging)
            {
                Rect rect = Rect.MinMaxRect(
                    Mathf.Min(_pressAt.x, pointer.x), Mathf.Min(_pressAt.y, pointer.y),
                    Mathf.Max(_pressAt.x, pointer.x), Mathf.Max(_pressAt.y, pointer.y));
                foreach (Unit unit in state.Units)
                {
                    if (!unit.IsDead && unit.OwnerId == playerId && ScreenCircle(unit, out Vector2 centre, out _) && rect.Contains(centre))
                        _scratch.Add(unit.Id);
                }
            }
            else
            {
                Unit? unit = UnitAt(state, pointer, u => u.OwnerId == playerId);
                if (unit != null)
                    _scratch.Add(unit.Id);
                else if (!add && _camera!.GroundPoint(pointer, out Vector3 ground))
                {
                    Building? building = OwnBuildingAt(state, playerId, Mathf.FloorToInt(ground.x), Mathf.FloorToInt(ground.z));
                    if (building != null)
                    {
                        _selected.Clear();
                        SetBuilding(building.Id);
                        return;
                    }
                }
            }

            // Shift-click on a selected unit takes it out, as in SC2.
            if (add && !dragging && _scratch.Count == 1 && _selected.Contains(_scratch[0]))
            {
                _selected.Remove(_scratch[0]);
                Changed?.Invoke();
                return;
            }
            Select(_scratch, add);
        }

        private void HandleGroups(SimState state)
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            bool ctrl = keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed;
            bool shift = keyboard.shiftKey.isPressed;

            for (int group = 0; group < GroupCount; group++)
            {
                if (!DigitKey(keyboard, group).wasPressedThisFrame)
                    continue;
                List<int> members = _groups[group];
                if (ctrl)
                {
                    members.Clear();
                    members.AddRange(_selected);
                }
                else if (shift)
                {
                    foreach (int id in _selected)
                    {
                        if (!members.Contains(id))
                            members.Add(id);
                    }
                }
                else
                {
                    DropGone(state, members, notify: false);
                    bool again = group == _lastGroup && Time.unscaledTime - _lastGroupTime <= _doubleTapSeconds;
                    Select(members, add: false);
                    if (again)
                        CentreCamera();
                    _lastGroup = group;
                    _lastGroupTime = Time.unscaledTime;
                }
                return;
            }
        }

        private void Select(List<int> ids, bool add)
        {
            SelectedBuildingId = 0;
            if (!add)
                _selected.Clear();
            foreach (int id in ids)
            {
                if (!_selected.Contains(id))
                    _selected.Add(id);
            }
            Changed?.Invoke();
        }

        private void CentreCamera()
        {
            if (_camera == null || _selected.Count == 0)
                return;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (int id in _selected)
            {
                if (_entities!.TryGetUnitPosition(id, out Vector3 position))
                {
                    sum += position;
                    count++;
                }
            }
            if (count > 0)
                _camera.Focus(sum / count);
        }

        // Dead, gone or no longer ours (control never changes hands in M1, but stay safe).
        private void DropGone(SimState state, List<int> ids, bool notify)
        {
            int playerId = _localPlayer!.PlayerId;
            bool removed = false;
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                Unit? unit = state.FindUnit(ids[i]);
                if (unit == null || unit.IsDead || unit.OwnerId != playerId)
                {
                    ids.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed && notify)
                Changed?.Invoke();
        }

        private void SetBuilding(int id)
        {
            SelectedBuildingId = id;
            Changed?.Invoke();
        }

        private static Building? OwnBuildingAt(SimState state, int playerId, int cellX, int cellY)
        {
            foreach (Building building in state.Buildings)
            {
                if (!building.IsDead && building.OwnerId == playerId && building.OccupiesCell(cellX, cellY))
                    return building;
            }
            return null;
        }

        private void OnPlayerChanged()
        {
            SelectedBuildingId = 0;
            _selected.Clear();
            foreach (List<int> group in _groups)
                group.Clear();
            Changed?.Invoke();
        }

        // The unit's body as a circle on screen: centre at half height, radius from its size.
        private bool ScreenCircle(Unit unit, out Vector2 centre, out float radius)
        {
            centre = default;
            radius = 0f;
            if (!_entities!.TryGetUnitPosition(unit.Id, out Vector3 ground))
                return false;
            float size = unit.Stats.SizeMilli / (float)Grid.MilliPerCell;
            Vector3 middle = ground + Vector3.up * size * 0.5f;
            Vector3 screen = _view!.WorldToScreenPoint(middle);
            if (screen.z <= 0f)
                return false;
            Vector3 edge = _view.WorldToScreenPoint(middle + _view.transform.right * size * 0.5f);
            centre = screen;
            radius = Vector2.Distance(centre, edge);
            return true;
        }

        private void ShowBox(bool visible, Vector2 from, Vector2 to)
        {
            if (_box == null)
                return;
            _box.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible || _box.panel == null)
                return;
            // Input System: origin bottom-left; UI Toolkit panel: top-left and scaled.
            Vector2 a = RuntimePanelUtils.ScreenToPanel(_box.panel, new Vector2(from.x, Screen.height - from.y));
            Vector2 b = RuntimePanelUtils.ScreenToPanel(_box.panel, new Vector2(to.x, Screen.height - to.y));
            _box.style.left = Mathf.Min(a.x, b.x);
            _box.style.top = Mathf.Min(a.y, b.y);
            _box.style.width = Mathf.Abs(a.x - b.x);
            _box.style.height = Mathf.Abs(a.y - b.y);
        }

        // Digit 1 is group 0 … digit 0 is group 9, matching the keyboard row.
        private static UnityEngine.InputSystem.Controls.KeyControl DigitKey(Keyboard keyboard, int group) =>
            keyboard[group == 9 ? Key.Digit0 : Key.Digit1 + group];
    }
}
