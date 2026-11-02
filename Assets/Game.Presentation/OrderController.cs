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
    // The sim validates every command itself; rejections come back as CommandRejected events.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class OrderController : MonoBehaviour
    {
        [SerializeField] private RtsCamera? _camera;
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

        private void Update()
        {
            Mouse? mouse = Mouse.current;
            Simulation? simulation = _runner!.Simulation;
            if (mouse == null || simulation == null || _camera == null || _selection!.Selected.Count == 0)
                return;
            if (!mouse.rightButton.wasPressedThisFrame)
                return;

            Vector2 pointer = mouse.position.ReadValue();
            if (!_camera.GroundPoint(pointer, out Vector3 ground))
                return;

            Order(simulation, pointer, ground);
        }

        private void Order(Simulation simulation, Vector2 pointer, Vector3 ground)
        {
            SimState state = simulation.State;
            int playerId = _localPlayer!.PlayerId;
            int tick = state.Tick;
            int cellX = Mathf.FloorToInt(ground.x), cellY = Mathf.FloorToInt(ground.z);
            int milliX = Mathf.RoundToInt(ground.x * Grid.MilliPerCell), milliY = Mathf.RoundToInt(ground.z * Grid.MilliPerCell);
            SplitSelection(state);
            int[] all = Concat(_workers, _others);

            // Fog: the player can only target what they see (the sim would allow it, the screen must not).
            Unit? enemyUnit = _selection!.UnitAt(state, pointer,
                u => state.Relations.IsEnemy(playerId, u.OwnerId) && state.Fog.IsVisible(playerId, u.CellX, u.CellY));
            if (enemyUnit != null)
            {
                simulation.Enqueue(new AttackCommand(playerId, tick, all, enemyUnit.Id));
                return;
            }

            Building? building = BuildingAt(state, cellX, cellY);
            if (building != null && state.Relations.IsEnemy(playerId, building.OwnerId) && state.Fog.IsVisible(playerId, cellX, cellY))
            {
                simulation.Enqueue(new AttackCommand(playerId, tick, all, building.Id));
                return;
            }

            if (_workers.Count > 0)
            {
                int[] workers = _workers.ToArray();
                ICommand? workerCommand = null;

                if (building != null && state.Relations.IsAlly(playerId, building.OwnerId))
                {
                    // Only picks the intent; DeliverCargoCommand checks the full rule itself.
                    if (building.IsComplete && building.Stats.Id == BuildingKinds.Headquarters && AnyCarries(state))
                        workerCommand = new DeliverCargoCommand(playerId, tick, workers, building.Id);
                    else if (!building.IsComplete || building.Hp < building.MaxHp)
                        workerCommand = new RepairCommand(playerId, tick, workers, building.Id);
                }
                else if (building == null && TrophyAt(state, playerId, ground, out int campIndex))
                {
                    workerCommand = new CarryTrophyCommand(playerId, tick, workers, campIndex);
                }
                else if (building == null && state.Map.InBounds(cellX, cellY) && state.Map.OreAt(cellX, cellY) != MapSetup.NoOre
                         && state.Fog.IsDiscovered(playerId, cellX, cellY))
                {
                    workerCommand = new MineCommand(playerId, tick, workers, cellX, cellY);
                }

                if (workerCommand != null)
                {
                    simulation.Enqueue(workerCommand);
                    if (_others.Count > 0)
                        simulation.Enqueue(new MoveCommand(playerId, tick, _others.ToArray(), milliX, milliY));
                    return;
                }
            }

            simulation.Enqueue(new MoveCommand(playerId, tick, all, milliX, milliY));
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
