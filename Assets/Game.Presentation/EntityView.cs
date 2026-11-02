using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Placeholder look of units, buildings and creep camps until there is art: primitives in the
    // owner's colour (creeps neutral grey), shape by type. Only reads the sim state.
    // Others' units and buildings hide in the local player's fog (Visibility).
    // Conveyors and bridge ends show their direction; a conveyor shows the unit it carries in the
    // resource's colour, moving with the sim — a unit that stands means the line is clogged (conveyors.md).
    // A working drill spins its rotor; a stopped one stands still (drill.md: state visible from afar).
    // Hit flash and the data for death effects are served to CombatEffects.
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class EntityView : MonoBehaviour
    {
        [Tooltip("Colour of player N is element N-1. Every object of the player, unfinished ones included.")]
        [SerializeField] private Color[] _playerColors =
        {
            new Color(0.20f, 0.45f, 0.95f),
            new Color(0.95f, 0.30f, 0.25f),
            new Color(0.95f, 0.80f, 0.20f),
            new Color(0.30f, 0.80f, 0.35f),
        };
        [Tooltip("creep-camp.md: creeps are neutral grey.")]
        [SerializeField] private Color _creepColor = new Color(0.55f, 0.55f, 0.55f);
        [SerializeField] private Color _primaryCargoColor = new Color(0.80f, 0.55f, 0.25f);
        [SerializeField] private Color _secondaryCargoColor = new Color(0.35f, 0.70f, 0.85f);
        [SerializeField] private Color _trophyColor = new Color(0.95f, 0.80f, 0.30f);
        [SerializeField] private Color _arrowColor = new Color(0.92f, 0.94f, 1.00f);
        [SerializeField] private Color _flashColor = Color.white;
        [SerializeField] private float _flashSeconds = 0.08f;
        [Tooltip("Degrees per second of a working drill's rotor.")]
        [SerializeField] private float _rotorSpeed = 240f;
        [Tooltip("Height of a construction site at zero progress, as a share of the finished height.")]
        [SerializeField, Range(0.05f, 1f)] private float _minSiteHeight = 0.15f;

        private readonly Dictionary<int, UnitVisual> _units = new Dictionary<int, UnitVisual>();
        private readonly Dictionary<int, BuildingVisual> _buildings = new Dictionary<int, BuildingVisual>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private readonly List<int> _gone = new List<int>();
        private readonly List<Transform> _trophies = new List<Transform>();
        private readonly List<Visual> _flashing = new List<Visual>();

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private Material? _baseMaterial;
        private Transform? _root;
        private int _lastTick = -1;

        private void Start()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
            _root = new GameObject("Entities").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        // Interpolated position on the ground, as drawn this frame: picking matches what the player sees.
        public bool TryGetUnitPosition(int unitId, out Vector3 position)
        {
            if (_units.TryGetValue(unitId, out UnitVisual? visual))
            {
                position = visual.Transform.position;
                return true;
            }
            position = default;
            return false;
        }

        // Centre of the body as drawn, its colour and size: where a death or a strike is shown.
        public bool TryGetEntity(int id, out Vector3 centre, out Color color, out float size)
        {
            Visual? visual = _units.TryGetValue(id, out UnitVisual? unit) ? unit : _buildings.TryGetValue(id, out BuildingVisual? building) ? building : null;
            if (visual == null || !visual.Transform.gameObject.activeInHierarchy)
            {
                centre = default;
                color = default;
                size = 0f;
                return false;
            }
            centre = visual.Renderer.bounds.center;
            color = visual.Color;
            size = visual.Size;
            return true;
        }

        // combat.md UI: the struck body flashes for a moment.
        public void Flash(int id)
        {
            Visual? visual = _units.TryGetValue(id, out UnitVisual? unit) ? unit : _buildings.TryGetValue(id, out BuildingVisual? building) ? building : null;
            if (visual == null)
                return;
            if (visual.FlashUntil <= 0f)
                _flashing.Add(visual);
            visual.FlashUntil = Time.unscaledTime + _flashSeconds;
            visual.Renderer.sharedMaterial = MaterialFor(_flashColor);
        }

        // After SimRunner.Update has advanced the sim this frame.
        private void LateUpdate()
        {
            Simulation? simulation = _runner != null ? _runner.Simulation : null;
            if (simulation == null)
                return;

            SimState state = simulation.State;
            if (state.Tick != _lastTick)
            {
                _lastTick = state.Tick;
                SyncUnits(state);
                SyncBuildings(state);
                SyncTrophies(state);
            }

            float alpha = _runner!.InterpolationAlpha;
            foreach (UnitVisual visual in _units.Values)
                visual.Transform.localPosition = Vector3.LerpUnclamped(visual.From, visual.To, alpha);

            for (int i = _flashing.Count - 1; i >= 0; i--)
            {
                Visual visual = _flashing[i];
                if (Time.unscaledTime < visual.FlashUntil)
                    continue;
                visual.FlashUntil = 0f;
                if (visual.Renderer != null)
                    visual.Renderer.sharedMaterial = MaterialFor(visual.Color);
                _flashing.RemoveAt(i);
            }

            float spin = _rotorSpeed * Time.deltaTime;
            foreach (BuildingVisual visual in _buildings.Values)
            {
                if (visual.Rotor != null && visual.Spinning)
                    visual.Rotor.Rotate(0f, spin, 0f, Space.World);
            }
        }

        private void SyncUnits(SimState state)
        {
            foreach (Unit unit in state.Units)
            {
                Vector3 position = ToWorld(unit.X, unit.Y);
                if (!_units.TryGetValue(unit.Id, out UnitVisual? visual))
                {
                    visual = CreateUnit(state, unit);
                    _units.Add(unit.Id, visual);
                    visual.To = position;
                }
                visual.From = visual.To;
                visual.To = position;
                visual.Alive = true;
                visual.Transform.gameObject.SetActive(Visibility.UnitShown(state, _localPlayer!.PlayerId, unit));

                // manual-mining.md: it is visible that the worker carries cargo, and of which type.
                if (visual.Cargo != null && unit.Worker != null)
                {
                    bool carrying = unit.Worker.CargoType != Worker.NoCargo && unit.Worker.Cargo > 0;
                    visual.Cargo.gameObject.SetActive(carrying);
                    if (carrying)
                        visual.Cargo.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(ResourceColor(unit.Worker.CargoType));
                }
            }

            RemoveMissing(_units);
        }

        private void SyncBuildings(SimState state)
        {
            foreach (Building building in state.Buildings)
            {
                if (!_buildings.TryGetValue(building.Id, out BuildingVisual? visual))
                {
                    visual = CreateBuilding(state, building);
                    _buildings.Add(building.Id, visual);
                }
                visual.Alive = true;
                bool shown = Visibility.BuildingShown(state, _localPlayer!.PlayerId, building);
                visual.Transform.gameObject.SetActive(shown);
                if (visual.Arrow != null)
                    visual.Arrow.gameObject.SetActive(shown);
                if (visual.Item != null)
                    SyncItem(visual.Item, building, shown);
                if (visual.Rotor != null)
                {
                    visual.Rotor.gameObject.SetActive(shown && building.IsComplete);
                    visual.Spinning = building.IsComplete && building.Drill != null && !building.Drill.IsStopped;
                }

                // A site grows with its progress; the colour stays the owner's (drill.md, conveyors.md).
                float share = building.IsComplete || building.BuildWork <= 0
                    ? 1f
                    : Mathf.Lerp(_minSiteHeight, 1f, building.BuildProgress / (float)building.BuildWork);
                float height = visual.FullHeight * share;
                Transform body = visual.Transform;
                body.localScale = new Vector3(body.localScale.x, ShapeHeightScale(visual.Shape, height), body.localScale.z);
                body.localPosition = new Vector3(body.localPosition.x, height * 0.5f, body.localPosition.z);
            }

            RemoveMissing(_buildings);
        }

        // creep-camp.md: the trophy lies on the ground at the cleared camp until carried away.
        private void SyncTrophies(SimState state)
        {
            for (int i = 0; i < state.CreepCamps.Count; i++)
            {
                CreepCamp camp = state.CreepCamps[i];
                if (_trophies.Count <= i)
                {
                    Transform trophy = CreatePrimitive(PrimitiveType.Cube, $"Trophy {i}", _trophyColor);
                    trophy.localPosition = ToWorld(camp.CenterMilliX, camp.CenterMilliY) + new Vector3(0f, 0.25f, 0f);
                    trophy.localScale = new Vector3(0.7f, 0.5f, 0.7f);
                    _trophies.Add(trophy);
                }
                bool lying = camp.State == CreepCampState.Cleared && camp.TrophyPrimary + camp.TrophySecondary > 0
                    && Visibility.CellDiscovered(state, _localPlayer!.PlayerId, camp.CenterX, camp.CenterY);
                _trophies[i].gameObject.SetActive(lying);
            }
        }

        private void RemoveMissing<T>(Dictionary<int, T> visuals) where T : Visual
        {
            _gone.Clear();
            foreach (KeyValuePair<int, T> pair in visuals)
            {
                if (!pair.Value.Alive)
                    _gone.Add(pair.Key);
                pair.Value.Alive = false;
            }
            foreach (int id in _gone)
            {
                visuals[id].DestroyObjects();
                visuals.Remove(id);
            }
        }

        private UnitVisual CreateUnit(SimState state, Unit unit)
        {
            PrimitiveType shape = UnitShape(unit.Stats);
            float size = unit.Stats.SizeMilli / (float)Grid.MilliPerCell;

            // The pivot stands on the ground at the unit's position; the body is lifted onto it.
            Transform pivot = new GameObject($"{unit.Stats.Id} #{unit.Id}").transform;
            pivot.SetParent(_root, worldPositionStays: false);

            Color color = OwnerColor(state, unit.OwnerId);
            Transform body = CreatePrimitive(shape, "Body", color);
            body.SetParent(pivot, worldPositionStays: false);
            body.localScale = new Vector3(size, ShapeHeightScale(shape, size), size);
            body.localPosition = new Vector3(0f, size * 0.5f, 0f);

            Transform? cargo = null;
            if (unit.Worker != null)
            {
                cargo = CreatePrimitive(PrimitiveType.Cube, "Cargo", _primaryCargoColor);
                cargo.SetParent(pivot, worldPositionStays: false);
                cargo.localScale = Vector3.one * size * 0.45f;
                cargo.localPosition = new Vector3(0f, size * 1.15f, 0f);
                cargo.gameObject.SetActive(false);
            }

            return new UnitVisual(pivot, body.GetComponent<MeshRenderer>(), color, size, cargo);
        }

        private BuildingVisual CreateBuilding(SimState state, Building building)
        {
            PrimitiveType shape = BuildingShape(building.Stats.Id);
            float height = BuildingHeight(building.Stats.Id);
            Color color = OwnerColor(state, building.OwnerId);
            Transform body = CreatePrimitive(shape, $"{building.Stats.Id} #{building.Id}", color);

            // Cell (x, y) covers world XZ [x, x+1) × [y, y+1); a small inset keeps neighbours apart.
            const float inset = 0.08f;
            body.localScale = new Vector3(building.Width - inset, ShapeHeightScale(shape, height), building.Height - inset);
            body.localPosition = new Vector3(building.CellX + building.Width * 0.5f, height * 0.5f, building.CellY + building.Height * 0.5f);
            var visual = new BuildingVisual(body, body.GetComponent<MeshRenderer>(), color, Mathf.Max(building.Width, building.Height), shape, height);

            if (building.Stats.Id == BuildingKinds.Conveyor || building.Stats.Id == BuildingKinds.ConveyorBridge)
            {
                // A bar from the cell centre towards the exit side, as in the placement silhouette.
                Quaternion rotation = DirectionRotation(building.Direction);
                Transform arrow = CreatePrimitive(PrimitiveType.Cube, "Arrow", _arrowColor);
                arrow.localRotation = rotation;
                arrow.localScale = new Vector3(0.16f, 0.04f, 0.5f);
                arrow.localPosition = CellCentre(building) + new Vector3(0f, height + 0.02f, 0f) + rotation * new Vector3(0f, 0f, 0.15f);
                visual.Arrow = arrow;
            }
            if (building.Drill != null || building.Stats.Id == BuildingKinds.Drill)
            {
                // Two crossed bars on the drill's top: they turn while it mines.
                Transform rotor = new GameObject("Rotor").transform;
                rotor.SetParent(_root, worldPositionStays: false);
                rotor.localPosition = CellCentre(building) + new Vector3(0f, height + 0.08f, 0f);
                Color rotorColor = Color.Lerp(color, Color.black, 0.45f);
                for (int i = 0; i < 2; i++)
                {
                    Transform bar = CreatePrimitive(PrimitiveType.Cube, "Bar", rotorColor);
                    bar.SetParent(rotor, worldPositionStays: false);
                    bar.localScale = new Vector3(building.Width * 0.8f, 0.1f, 0.22f);
                    bar.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                }
                rotor.gameObject.SetActive(false);
                visual.Rotor = rotor;
            }
            if (building.Conveyor != null)
            {
                Transform item = CreatePrimitive(PrimitiveType.Cube, "Item", _primaryCargoColor);
                item.localScale = Vector3.one * 0.3f;
                item.gameObject.SetActive(false);
                visual.Item = item;
            }
            return visual;
        }

        // ItemPos runs 0..CellLength from the back edge to the front edge of the cell.
        private void SyncItem(Transform item, Building building, bool shown)
        {
            Conveyor conveyor = building.Conveyor!;
            bool carrying = shown && conveyor.HasItem;
            item.gameObject.SetActive(carrying);
            if (!carrying)
                return;
            float along = conveyor.ItemPos / (float)Conveyor.CellLength - 0.5f;
            item.localPosition = CellCentre(building) + new Vector3(0f, 0.28f, 0f) + DirectionRotation(building.Direction) * new Vector3(0f, 0f, along);
            item.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(ResourceColor(conveyor.ItemType));
        }

        private static Vector3 CellCentre(Building building) =>
            new Vector3(building.CellX + building.Width * 0.5f, 0f, building.CellY + building.Height * 0.5f);

        // Direction.Up is +Y in cells, which is +Z in the world.
        private static Quaternion DirectionRotation(Direction direction) => direction switch
        {
            Direction.Right => Quaternion.Euler(0f, 90f, 0f),
            Direction.Up => Quaternion.identity,
            Direction.Left => Quaternion.Euler(0f, 270f, 0f),
            _ => Quaternion.Euler(0f, 180f, 0f),
        };

        private Transform CreatePrimitive(PrimitiveType type, string name, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            // Picking goes through sim coordinates, not physics.
            Destroy(go.GetComponent<Collider>());
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            // A primitive gets the active pipeline's default material: no Shader.Find needed.
            _baseMaterial ??= renderer.sharedMaterial;
            renderer.sharedMaterial = MaterialFor(color);
            go.transform.SetParent(_root, worldPositionStays: false);
            return go.transform;
        }

        // One shared material per colour: nothing is allocated per frame or per object.
        private Material MaterialFor(Color color)
        {
            if (!_materials.TryGetValue(color, out Material? material))
            {
                material = new Material(_baseMaterial) { color = color };
                _materials.Add(color, material);
            }
            return material;
        }

        public Color OwnerColor(SimState state, int ownerId)
        {
            if (ownerId == state.CreepPlayerId)
                return _creepColor;
            int index = ownerId - 1;
            return index >= 0 && index < _playerColors.Length ? _playerColors[index] : Color.white;
        }

        private Color ResourceColor(int resourceType) =>
            resourceType == Game.Sim.Resources.Primary ? _primaryCargoColor : _secondaryCargoColor;

        private static Vector3 ToWorld(int milliX, int milliY) =>
            new Vector3(milliX / (float)Grid.MilliPerCell, 0f, milliY / (float)Grid.MilliPerCell);

        private static PrimitiveType UnitShape(UnitStats stats)
        {
            if (stats.HasTag(UnitTags.Creep))
                return PrimitiveType.Sphere;
            if (stats.HasTag(UnitTags.Worker))
                return PrimitiveType.Cube;
            if (stats.HasTag("ranged"))
                return PrimitiveType.Cylinder;
            return PrimitiveType.Capsule;
        }

        private static PrimitiveType BuildingShape(string id) =>
            id == BuildingKinds.Drill || id == "turret" ? PrimitiveType.Cylinder : PrimitiveType.Cube;

        private static float BuildingHeight(string id)
        {
            switch (id)
            {
                case BuildingKinds.Headquarters: return 2.0f;
                case BuildingKinds.Factory: return 1.5f;
                case BuildingKinds.Drill: return 1.0f;
                case BuildingKinds.Storage: return 1.0f;
                case BuildingKinds.Conveyor: return 0.12f;
                case BuildingKinds.ConveyorBridge: return 0.4f;
                default: return 1.2f;
            }
        }

        // Unity's capsule and cylinder are 2 units tall at scale 1, cube and sphere are 1.
        private static float ShapeHeightScale(PrimitiveType shape, float height) =>
            shape == PrimitiveType.Capsule || shape == PrimitiveType.Cylinder ? height * 0.5f : height;

        private void OnDestroy()
        {
            foreach (Material material in _materials.Values)
                Destroy(material);
        }

        private abstract class Visual
        {
            protected Visual(Transform transform, MeshRenderer renderer, Color color, float size)
            {
                Transform = transform;
                Renderer = renderer;
                Color = color;
                Size = size;
            }

            public Transform Transform { get; }
            public MeshRenderer Renderer { get; }
            public Color Color { get; }
            public float Size { get; }
            public bool Alive { get; set; } = true;
            public float FlashUntil { get; set; }

            public virtual void DestroyObjects() => Destroy(Transform.gameObject);
        }

        private sealed class UnitVisual : Visual
        {
            public UnitVisual(Transform pivot, MeshRenderer body, Color color, float size, Transform? cargo)
                : base(pivot, body, color, size) => Cargo = cargo;

            public Transform? Cargo { get; }
            public Vector3 From { get; set; }
            public Vector3 To { get; set; }
        }

        private sealed class BuildingVisual : Visual
        {
            public BuildingVisual(Transform body, MeshRenderer renderer, Color color, float size, PrimitiveType shape, float fullHeight)
                : base(body, renderer, color, size)
            {
                Shape = shape;
                FullHeight = fullHeight;
            }

            public PrimitiveType Shape { get; }
            public float FullHeight { get; }
            public Transform? Arrow { get; set; }
            public Transform? Item { get; set; }
            public Transform? Rotor { get; set; }
            public bool Spinning { get; set; }

            public override void DestroyObjects()
            {
                base.DestroyObjects();
                if (Arrow != null)
                    Destroy(Arrow.gameObject);
                if (Item != null)
                    Destroy(Item.gameObject);
                if (Rotor != null)
                    Destroy(Rotor.gameObject);
            }
        }
    }
}
