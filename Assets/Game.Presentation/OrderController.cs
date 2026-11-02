using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Right click with a selection turns into one command, picked by what is under the cursor:
    // enemy → attack (combat.md); own or ally headquarters while a worker carries cargo → deliver
    // there (manual-mining.md); unfinished or damaged own/ally building → build/repair
    // (construction.md); trophy → carry (creep-camp.md); ore → mine (manual-mining.md);
    // anything else → move. Worker-only orders go to the workers; the rest of the selection walks there.
    // With Shift every order joins the units' queues (order-queue.md); delivering cargo is a setting, not queued.
    // The sim validates every command itself; rejections come back as CommandRejected events.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class OrderController : MonoBehaviour
    {
        [SerializeField] private RtsCamera? _camera;
        [SerializeField] private Hud? _hud;
        [SerializeField] private PlacementController? _placement;
        [Tooltip("How close to the camp centre a click counts as on the trophy, cells.")]
        [SerializeField] private float _trophyPickCells = 0.8f;

        private readonly List<int> _workers = new List<int>();
        private readonly List<int> _others = new List<int>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        public enum Intent { None, Attack, Deliver, Repair, Carry, Mine }

        private bool _showingTip;

        // What a right click would do right now; CursorView picks the cursor by it.
        public Intent CurrentIntent { get; private set; }

        // The cursor tip is this controller's; hover info yields to it.
        public bool IsShowingTip => _showingTip;

        private void Update()
        {
            Mouse? mouse = Mouse.current;
            Simulation? simulation = _runner!.Simulation;
            CurrentIntent = Intent.None;
            if (mouse == null || simulation == null || _camera == null || _selection!.Selected.Count == 0)
            {
                HideTip();
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();
            // Placing owns the tip and the right click (it cancels the placement, not an order).
            if (_placement != null && _placement.IsBusy)
            {
                _showingTip = false;
                return;
            }
            if ((_hud != null && _hud.IsPointerOverUi(pointer)) || !_camera.GroundPoint(pointer, out Vector3 ground))
            {
                HideTip();
                return;
            }

            SimState state = simulation.State;
            SplitSelection(state);
            Intent intent = Resolve(state, pointer, ground, out int targetId);
            CurrentIntent = intent;
            ShowTip(state, intent, targetId, pointer);
            if (mouse.rightButton.wasPressedThisFrame)
                Order(simulation, intent, targetId, ground);
        }

        // What a right click here would do; the cursor tip and the order use the same answer.
        // Fog: the player can only target what they see (the sim would allow it, the screen must not).
        private Intent Resolve(SimState state, Vector2 pointer, Vector3 ground, out int targetId)
        {
            int playerId = _localPlayer!.PlayerId;
            int cellX = Mathf.FloorToInt(ground.x), cellY = Mathf.FloorToInt(ground.z);
            targetId = 0;

            Unit? enemyUnit = _selection!.UnitAt(state, pointer,
                u => state.Relations.IsEnemy(playerId, u.OwnerId) && state.Fog.IsVisible(playerId, u.CellX, u.CellY));
            if (enemyUnit != null)
            {
                targetId = enemyUnit.Id;
                return Intent.Attack;
            }

            Building? building = BuildingAt(state, cellX, cellY);
            if (building != null && state.Relations.IsEnemy(playerId, building.OwnerId) && state.Fog.IsVisible(playerId, cellX, cellY))
            {
                targetId = building.Id;
                return Intent.Attack;
            }
            if (_workers.Count == 0)
                return Intent.None;

            if (building != null && state.Relations.IsAlly(playerId, building.OwnerId))
            {
                targetId = building.Id;
                // Only picks the intent; DeliverCargoCommand checks the full rule itself.
                if (building.IsComplete && building.Stats.Id == BuildingKinds.Headquarters && AnyCarries(state))
                    return Intent.Deliver;
                if (!building.IsComplete || building.Hp < building.MaxHp)
                    return Intent.Repair;
                return Intent.None;
            }
            if (building == null && TrophyAt(state, playerId, ground, out int campIndex))
            {
                targetId = campIndex;
                return Intent.Carry;
            }
            if (building == null && state.Map.InBounds(cellX, cellY) && state.Map.OreAt(cellX, cellY) != MapSetup.NoOre
                && state.Fog.IsDiscovered(playerId, cellX, cellY))
                return Intent.Mine;
            return Intent.None;
        }

        private void Order(Simulation simulation, Intent intent, int targetId, Vector3 ground)
        {
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            int tick = state.Tick;
            bool queued = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            int cellX = Mathf.FloorToInt(ground.x), cellY = Mathf.FloorToInt(ground.z);
            int milliX = Mathf.RoundToInt(ground.x * Grid.MilliPerCell), milliY = Mathf.RoundToInt(ground.z * Grid.MilliPerCell);
            int[] workers = _workers.ToArray();

            ICommand? workerCommand = intent switch
            {
                Intent.Deliver => new DeliverCargoCommand(playerId, tick, workers, targetId),
                Intent.Repair => new RepairCommand(playerId, tick, workers, targetId, queued),
                Intent.Carry => new CarryTrophyCommand(playerId, tick, workers, targetId, queued),
                Intent.Mine => new MineCommand(playerId, tick, workers, cellX, cellY, queued),
                _ => null,
            };

            if (intent == Intent.Attack)
                simulation.Enqueue(new AttackCommand(playerId, tick, Concat(_workers, _others), targetId, queued));
            else if (workerCommand != null)
            {
                simulation.Enqueue(workerCommand);
                if (_others.Count > 0)
                    simulation.Enqueue(new MoveCommand(playerId, tick, _others.ToArray(), milliX, milliY, queued));
            }
            else
                simulation.Enqueue(new MoveCommand(playerId, tick, Concat(_workers, _others), milliX, milliY, queued));
        }

        // A hint at the cursor for what the right click does, so mining and the rest are discoverable.
        private void ShowTip(SimState state, Intent intent, int targetId, Vector2 pointer)
        {
            string? text = intent switch
            {
                Intent.Attack => "ПКМ — атаковать",
                Intent.Deliver => "ПКМ — сдать груз в этот штаб",
                Intent.Repair => state.FindBuilding(targetId) is { IsComplete: false } ? "ПКМ — помочь достроить" : "ПКМ — чинить",
                Intent.Carry => $"ПКМ — нести трофей (осталось {Trophy(state, targetId)})",
                Intent.Mine => $"ПКМ — копать: {state.Rules.ManualCargo} ед. за {DisplayNames.Seconds(state.Rules.ManualCargo * state.Rules.ManualMiningTicksPerUnit)}, рабочий сам носит в штаб",
                _ => null,
            };
            if (text == null)
            {
                HideTip();
                return;
            }
            _hud?.ShowTip(text + "\nShift — в очередь", pointer);
            _showingTip = true;
        }

        private void HideTip()
        {
            if (!_showingTip)
                return;
            _showingTip = false;
            _hud?.ShowTip(null, Vector2.zero);
        }

        private static int Trophy(SimState state, int campIndex)
        {
            CreepCamp camp = state.CreepCamps[campIndex];
            return camp.TrophyPrimary + camp.TrophySecondary;
        }

        private void SplitSelection(SimState state)
        {
            _workers.Clear();
            _others.Clear();
            foreach (int id in _selection!.Selected)
            {
                Unit? unit = state.FindUnit(id);
                if (unit == null)
                    continue;
                (unit.Worker != null ? _workers : _others).Add(id);
            }
        }

        private bool AnyCarries(SimState state)
        {
            foreach (int id in _workers)
            {
                Worker? worker = state.FindUnit(id)?.Worker;
                if (worker != null && worker.Cargo > 0)
                    return true;
            }
            return false;
        }

        private bool TrophyAt(SimState state, int playerId, Vector3 ground, out int campIndex)
        {
            foreach (CreepCamp camp in state.CreepCamps)
            {
                if (camp.State != CreepCampState.Cleared || camp.TrophyPrimary + camp.TrophySecondary == 0
                    || !state.Fog.IsDiscovered(playerId, camp.CenterX, camp.CenterY))
                    continue;
                float dx = ground.x - camp.CenterMilliX / (float)Grid.MilliPerCell;
                float dz = ground.z - camp.CenterMilliY / (float)Grid.MilliPerCell;
                if (dx * dx + dz * dz <= _trophyPickCells * _trophyPickCells)
                {
                    campIndex = camp.Index;
                    return true;
                }
            }
            campIndex = -1;
            return false;
        }

        private static Building? BuildingAt(SimState state, int cellX, int cellY)
        {
            foreach (Building building in state.Buildings)
            {
                if (!building.IsDead && building.OccupiesCell(cellX, cellY))
                    return building;
            }
            return null;
        }

        private static int[] Concat(List<int> a, List<int> b)
        {
            var result = new int[a.Count + b.Count];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Count);
            return result;
        }
    }
}
