using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // What the cursor rests on, when no order tip is shown:
    // - deposit: resource, size and free cells (drill.md: "при наведении видно, сколько места свободно");
    // - trophy: how much is left (creep-camp.md);
    // - creep camp: its creeps and the aggro radius drawn on the ground (creep-camp.md).
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer), typeof(OrderController))]
    public sealed class HoverInfo : MonoBehaviour
    {
        private const int RingSegments = 48;

        [SerializeField] private RtsCamera? _camera;
        [SerializeField] private Hud? _hud;
        [SerializeField] private PlacementController? _placement;
        [SerializeField] private float _trophyPickCells = 0.8f;
        [Tooltip("How close to a camp's centre the cursor shows the camp, cells.")]
        [SerializeField] private float _campPickCells = 2.5f;
        [SerializeField] private Color _ringColor = new Color(0.95f, 0.30f, 0.25f);

        private readonly List<Transform> _ring = new List<Transform>();

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private OrderController? _orders;
        private Transform? _root;
        private Material? _ringMaterial;
        private bool _showingTip;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
            _orders = GetComponent<OrderController>();
            _root = new GameObject("HoverInfo").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        // After OrderController.Update: its order tip wins.
        private void LateUpdate()
        {
            Simulation? simulation = _runner!.Simulation;
            UnityEngine.InputSystem.Mouse? mouse = UnityEngine.InputSystem.Mouse.current;
            string? tip = null;
            CreepCamp? ringCamp = null;

            if (simulation != null && mouse != null && _camera != null && _hud != null
                && !(_placement != null && _placement.IsActive) && !_orders!.IsShowingTip)
            {
                Vector2 pointer = mouse.position.ReadValue();
                if (!_hud.IsPointerOverUi(pointer) && _camera.GroundPoint(pointer, out Vector3 ground))
                    tip = Describe(simulation.State, ground, out ringCamp);
            }

            DrawRing(ringCamp);
            if (tip != null)
            {
                _hud!.ShowTip(tip, mouse!.position.ReadValue());
                _showingTip = true;
            }
            else if (_showingTip)
            {
                _showingTip = false;
                if (!_orders!.IsShowingTip && !(_placement != null && _placement.IsActive))
                    _hud?.ShowTip(null, Vector2.zero);
            }
        }

        private string? Describe(SimState state, Vector3 ground, out CreepCamp? ringCamp)
        {
            int playerId = _localPlayer!.PlayerId;
            ringCamp = null;
            int cellX = Mathf.FloorToInt(ground.x), cellY = Mathf.FloorToInt(ground.z);

            foreach (CreepCamp camp in state.CreepCamps)
            {
                if (!state.Fog.IsDiscovered(playerId, camp.CenterX, camp.CenterY))
                    continue;
                float dx = ground.x - camp.CenterMilliX / (float)Grid.MilliPerCell;
                float dz = ground.z - camp.CenterMilliY / (float)Grid.MilliPerCell;
                float distance2 = dx * dx + dz * dz;
                int trophy = camp.TrophyPrimary + camp.TrophySecondary;
                if (camp.State == CreepCampState.Cleared)
                {
                    if (trophy > 0 && distance2 <= _trophyPickCells * _trophyPickCells)
                        return $"Трофей: осталось {trophy}\nСтроитель + ПКМ — унести в штаб по {camp.Stats.TrophyPerTrip}";
                    continue;
                }
                if (distance2 <= _campPickCells * _campPickCells)
                {
                    ringCamp = camp;
                    int radius = camp.Stats.AggroRadiusMilli / Grid.MilliPerCell;
                    return $"Лагерь крипов «{DisplayNames.Camp(camp.Stats.Id)}»: крипов {AliveCreeps(state, camp)}\n"
                        + $"Нападают на всё в радиусе {radius} клеток · трофей {trophy}";
                }
            }

            MapState map = state.Map;
            if (!map.InBounds(cellX, cellY) || !state.Fog.IsDiscovered(playerId, cellX, cellY))
                return null;
            // neutral-buildings.md: what a neutral building gives and how to take it.
            Building? poi = state.FindBuilding(map.BuildingAt(cellX, cellY));
            if (poi != null && !poi.IsDead && (poi.OwnerId == state.NeutralPlayerId || poi.Stats.Behavior == BuildingBehavior.Lighthouse))
            {
                int seconds = state.Rules.CaptureTimeTicks / Simulation.TicksPerSecond;
                bool guarded = state.IsGuarded(poi);
                if (poi.Stats.Behavior == BuildingBehavior.Lighthouse)
                {
                        return $"Маяк: подведите юнита — откроет обзор на {DisplayNames.Cells(poi.Stats.VisionMilli / Grid.MilliPerCell)}"
                            + (poi.LitForPlayerId != 0 ? $"\nСветит игроку {poi.LitForPlayerId}" : "");
                }
                switch (poi.Stats.Id)
                {
                    case BuildingIds.Cave:
                        return guarded
                            ? "Пещера Серых Существ: сначала победите её стражей"
                            : $"Пещера: подведите юнита на {seconds} с — станет вашей и будет нанимать особых юнитов\nПлатит со своего склада: подведите ленту от бура или склада";
                    default:
                        return $"Нейтральный {DisplayNames.Building(poi.Stats.Id).ToLowerInvariant()}: подведите юнита на {seconds} с — станет вашим";
                }
            }
            int depositId = map.DepositAt(cellX, cellY);
            if (depositId == 0)
                return null;
            foreach (Deposit deposit in map.Deposits)
            {
                if (deposit.Id != depositId)
                    continue;
                int free = 0;
                foreach (int index in deposit.Cells)
                {
                    if (map.BuildingAt(index % map.Width, index / map.Width) == 0)
                        free++;
                }
                string resource = deposit.ResourceType == Game.Sim.Resources.Primary ? "основной ресурс" : "второй ресурс";
                return $"Месторождение ({resource}): {DisplayNames.Cells(deposit.Cells.Count)}, свободно {free}";
            }
            return null;
        }

        private static int AliveCreeps(SimState state, CreepCamp camp)
        {
            int alive = 0;
            foreach (int id in camp.CreepIds)
            {
                Unit? creep = state.FindUnit(id);
                if (creep != null && !creep.IsDead)
                    alive++;
            }
            return alive;
        }

        // A dashed circle on the ground: segments around the camp's centre.
        private void DrawRing(CreepCamp? camp)
        {
            if (camp == null)
            {
                foreach (Transform segment in _ring)
                    segment.gameObject.SetActive(false);
                return;
            }
            if (_ring.Count == 0)
                CreateRing();
            float radius = camp.Stats.AggroRadiusMilli / (float)Grid.MilliPerCell;
            var centre = new Vector3(camp.CenterMilliX / (float)Grid.MilliPerCell, 0.05f, camp.CenterMilliY / (float)Grid.MilliPerCell);
            float length = 2f * Mathf.PI * radius / RingSegments * 0.6f;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * 360f / RingSegments;
                Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
                Transform segment = _ring[i];
                segment.gameObject.SetActive(true);
                segment.position = centre + rotation * Vector3.forward * radius;
                segment.rotation = rotation * Quaternion.Euler(0f, 90f, 0f);
                segment.localScale = new Vector3(0.12f, 0.03f, length);
            }
        }

        private void CreateRing()
        {
            for (int i = 0; i < RingSegments; i++)
            {
                GameObject go = Primitives.Create(PrimitiveType.Cube);
                go.name = "AggroRing";
                Destroy(go.GetComponent<Collider>());
                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                _ringMaterial ??= new Material(renderer.sharedMaterial) { color = _ringColor };
                renderer.sharedMaterial = _ringMaterial;
                go.transform.SetParent(_root, worldPositionStays: false);
                _ring.Add(go.transform);
            }
        }

        private void OnDestroy()
        {
            if (_ringMaterial != null)
                Destroy(_ringMaterial);
        }
    }
}
