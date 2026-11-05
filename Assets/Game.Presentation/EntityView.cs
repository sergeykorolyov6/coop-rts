using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Look of units, buildings and creep camps: models put together from primitives
    // (PrimitiveModels) in the owner's colour (creeps neutral grey), shape by type. Only reads the sim state.
    // Others' units and buildings hide in the local player's fog (Visibility).
    // Conveyors and bridge ends show their direction; a conveyor shows the unit it carries in the
    // resource's colour, moving with the sim — a unit that stands means the line is clogged (conveyors.md).
    // A working drill spins its rotor; a stopped one stands still (drill.md: state visible from afar).
    // Hit flash and the data for death effects are served to CombatEffects.
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class EntityView : MonoBehaviour
    {
        [Tooltip("Colour of player N is element N-1. Every object of the player, unfinished ones included.")]
        // Names of _playerColors in order, as missions.csv "colors" writes them.
        private static readonly string[] PaletteNames = { "blue", "red", "yellow", "green", "brown" };

        [Tooltip("Palette: blue, red, yellow, green, brown (missions.csv names them); by default player N takes colour N.")]
        [SerializeField] private Color[] _playerColors =
        {
            new Color(0.20f, 0.45f, 0.95f),
            new Color(0.95f, 0.30f, 0.25f),
            new Color(0.95f, 0.80f, 0.20f),
            new Color(0.30f, 0.80f, 0.35f),
            new Color(0.60f, 0.40f, 0.22f),
        };
        [Tooltip("creep-camp.md: creeps are neutral grey.")]
        [SerializeField] private Color _creepColor = new Color(0.55f, 0.55f, 0.55f);
        [Tooltip("legion-ai.md: the Black Legion — near black with a violet cast, so it still reads on dark ground.")]
        [SerializeField] private Color _legionColor = new Color(0.22f, 0.14f, 0.26f);
        [Tooltip("neutral-buildings.md: neutral drills, the cave and lighthouses until captured.")]
        [SerializeField] private Color _neutralColor = new Color(0.86f, 0.86f, 0.82f);
        [SerializeField] private Color _primaryCargoColor = new Color(0.80f, 0.55f, 0.25f);
        [SerializeField] private Color _secondaryCargoColor = new Color(0.35f, 0.70f, 0.85f);
        [SerializeField] private Color _trophyColor = new Color(0.95f, 0.80f, 0.30f);
        [SerializeField] private Color _arrowColor = new Color(0.92f, 0.94f, 1.00f);
        [SerializeField] private Color _fireColor = new Color(1.00f, 0.78f, 0.30f);
        [Tooltip("demolish.md: a building being demolished is drawn darker.")]
        [SerializeField, Range(0f, 1f)] private float _demolishDarken = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _boostLighten = 0.5f;
        [SerializeField] private Color _flashColor = Color.white;
        [SerializeField] private float _flashSeconds = 0.08f;
        [Tooltip("Degrees per second of a working drill's rotor.")]
        [SerializeField] private float _rotorSpeed = 240f;
        [Tooltip("Degrees per second a unit model turns to where it goes or what it works on.")]
        [SerializeField] private float _turnSpeed = 720f;
        [Tooltip("Cells per tick below which a unit counts as standing: separation nudges do not turn it.")]
        [SerializeField] private float _minTurnStep = 0.02f;
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
            Visual? visual = FindVisual(id);
            if (visual == null || !visual.Transform.gameObject.activeInHierarchy)
            {
                centre = default;
                color = default;
                size = 0f;
                return false;
            }
            centre = visual.Transform.position + Vector3.up * visual.CentreHeight;
            color = visual.Color;
            size = visual.Size;
            return true;
        }

        private Visual? FindVisual(int id) =>
            _units.TryGetValue(id, out UnitVisual? unit) ? unit : _buildings.TryGetValue(id, out BuildingVisual? building) ? building : null;

        // combat.md UI: the struck body flashes for a moment.
        public void Flash(int id)
        {
            Visual? visual = FindVisual(id);
            if (visual == null)
                return;
            if (visual.FlashUntil <= 0f)
                _flashing.Add(visual);
            visual.FlashUntil = Time.unscaledTime + _flashSeconds;
            Material flash = MaterialFor(_flashColor);
            foreach (MeshRenderer renderer in visual.Renderers)
                renderer.sharedMaterial = flash;
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
            float turn = _turnSpeed * Time.deltaTime;
            foreach (UnitVisual visual in _units.Values)
            {
                visual.Transform.localPosition = Vector3.LerpUnclamped(visual.From, visual.To, alpha);
                visual.Transform.localRotation = Quaternion.RotateTowards(visual.Transform.localRotation, visual.Heading, turn);
            }

            for (int i = _flashing.Count - 1; i >= 0; i--)
            {
                Visual visual = _flashing[i];
                if (Time.unscaledTime < visual.FlashUntil)
                    continue;
                visual.FlashUntil = 0f;
                visual.RestoreMaterials();
                // A demolishing building gets darkened again on the next sync.
                if (visual is BuildingVisual b)
                    b.Demolishing = false;
                else if (visual is UnitVisual u)
                    u.Boosted = false;
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
                Face(state, unit, visual);
                visual.Transform.gameObject.SetActive(Visibility.UnitShown(state, _localPlayer!.PlayerId, unit));

                // factions.md: a unit under a boost (Overdrive, frenzy) is drawn lighter.
                bool boosted = unit.BoostTicksLeft > 0;
                if (boosted != visual.Boosted && visual.FlashUntil <= 0f)
                {
                    visual.Boosted = boosted;
                    if (boosted)
                    {
                        Material light = MaterialFor(Color.Lerp(visual.Color, Color.white, _boostLighten));
                        foreach (MeshRenderer renderer in visual.Renderers)
                            renderer.sharedMaterial = light;
                    }
                    else
                        visual.RestoreMaterials();
                }

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

        // Models are built facing -Z (the camera). A moving unit turns to where it goes; a standing one
        // to what it fights, builds or repairs; otherwise it keeps its heading.
        private void Face(SimState state, Unit unit, UnitVisual visual)
        {
            Vector3 look = visual.To - visual.From;
            if (look.sqrMagnitude < _minTurnStep * _minTurnStep)
            {
                int targetId = unit.TargetId != 0 ? unit.TargetId : unit.Order.TargetId;
                Entity? target = targetId != 0 ? state.FindEntity(targetId) : null;
                look = target is Building b ? CellCentre(b) - visual.To
                    : target is Unit u ? ToWorld(u.X, u.Y) - visual.To
                    : Vector3.zero;
                look.y = 0f;
            }
            if (look.sqrMagnitude > 0.0001f)
                visual.Heading = Quaternion.LookRotation(-look, Vector3.up);
        }

        private void SyncBuildings(SimState state)
        {
            foreach (Building building in state.Buildings)
            {
                // neutral-buildings.md: a captured building takes its new owner's colour.
                if (_buildings.TryGetValue(building.Id, out BuildingVisual? visual) && visual.OwnerId != building.OwnerId)
                {
                    visual.DestroyObjects();
                    _buildings.Remove(building.Id);
                    visual = null;
                }
                if (visual == null)
                {
                    visual = CreateBuilding(state, building);
                    _buildings.Add(building.Id, visual);
                }
                visual.Alive = true;
                bool shown = Visibility.BuildingShown(state, _localPlayer!.PlayerId, building);
                visual.Transform.gameObject.SetActive(shown);
                if (visual.Arrow != null)
                    visual.Arrow.gameObject.SetActive(shown);
                if (visual.Item != null && building.Conveyor != null)
                    SyncItem(visual.Item, building, shown);
                else if (visual.Item != null)
                    visual.Item.gameObject.SetActive(shown && building.LitForPlayerId != 0);
                // demolish.md: dark while it goes, back to its colours when the demolition is cancelled.
                bool demolishing = building.DemolishTicksLeft > 0;
                if (demolishing != visual.Demolishing && visual.FlashUntil <= 0f)
                {
                    visual.Demolishing = demolishing;
                    if (demolishing)
                    {
                        Material dark = MaterialFor(Color.Lerp(visual.Color, Color.black, _demolishDarken));
                        foreach (MeshRenderer renderer in visual.Renderers)
                            renderer.sharedMaterial = dark;
                    }
                    else
                        visual.RestoreMaterials();
                }
                if (visual.Rotor != null)
                {
                    visual.Rotor.gameObject.SetActive(shown && building.IsComplete);
                    visual.Spinning = building.IsComplete && building.Drill != null && !building.Drill.IsStopped;
                }

                // A site grows with its progress; the colour stays the owner's (drill.md, conveyors.md).
                float share = building.IsComplete || building.BuildWork <= 0
                    ? 1f
                    : Mathf.Lerp(_minSiteHeight, 1f, building.BuildProgress / (float)building.BuildWork);
                visual.Transform.localScale = new Vector3(1f, share, 1f);
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
            float size = unit.Stats.SizeMilli / (float)Grid.MilliPerCell;
            Color color = OwnerColor(state, unit.OwnerId);
            var renderers = new List<MeshRenderer>();
            // The root stands on the ground at the unit's position; parts sit on it.
            Transform pivot = PrimitiveModels.Unit(unit.Stats, color, Part, renderers, out Transform? cargo);
            pivot.name = $"{unit.Stats.Id} #{unit.Id}";
            pivot.SetParent(_root, worldPositionStays: false);
            return new UnitVisual(pivot, renderers, color, size, size * 0.5f, cargo);
        }

        private BuildingVisual CreateBuilding(SimState state, Building building)
        {
            float height = PrimitiveModels.BuildingHeight(building.Stats);
            Color color = OwnerColor(state, building.OwnerId);
            var renderers = new List<MeshRenderer>();
            // Cell (x, y) covers world XZ [x, x+1) × [y, y+1); the root stands at the footprint's centre.
            Transform root = PrimitiveModels.Building(building, color, Part, renderers);
            root.name = $"{building.Stats.Id} #{building.Id}";
            root.SetParent(_root, worldPositionStays: false);
            root.localPosition = CellCentre(building);
            var visual = new BuildingVisual(root, renderers, color, Mathf.Max(building.Width, building.Height), height * 0.5f) { OwnerId = building.OwnerId };
            if (building.Stats.Behavior == BuildingBehavior.Lighthouse)
            {
                // The fire on top burns while the lighthouse shines for someone.
                Transform fire = CreatePrimitive(PrimitiveType.Sphere, "Fire", _fireColor);
                fire.localScale = Vector3.one * 0.6f;
                fire.localPosition = CellCentre(building) + new Vector3(0f, height + 0.1f, 0f);
                fire.gameObject.SetActive(false);
                visual.Item = fire;
            }

            if (building.Stats.IsBelt)
            {
                // A bar from the cell centre towards the exit side, as in the placement silhouette.
                Quaternion rotation = DirectionRotation(building.Direction);
                Transform arrow = CreatePrimitive(PrimitiveType.Cube, "Arrow", _arrowColor);
                arrow.localRotation = rotation;
                arrow.localScale = new Vector3(0.16f, 0.04f, 0.5f);
                arrow.localPosition = CellCentre(building) + new Vector3(0f, height + 0.02f, 0f) + rotation * new Vector3(0f, 0f, 0.15f);
                visual.Arrow = arrow;
            }
            if (building.Stats.Behavior == BuildingBehavior.Drill)
            {
                // Two crossed bars on the drill's top: they turn while it mines.
                Transform rotor = new GameObject("Rotor").transform;
                rotor.SetParent(_root, worldPositionStays: false);
                rotor.localPosition = CellCentre(building) + new Vector3(0f, height + 0.06f, 0f);
                Color rotorColor = Color.Lerp(color, Color.black, 0.45f);
                for (int i = 0; i < 2; i++)
                {
                    Transform bar = CreatePrimitive(PrimitiveType.Cube, "Bar", rotorColor);
                    bar.SetParent(rotor, worldPositionStays: false);
                    bar.localScale = new Vector3(building.Width * 0.75f, 0.1f, 0.2f);
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

        private Transform Part(PrimitiveType type, Color color) => CreatePrimitive(type, type.ToString(), color);

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
            GameObject go = Primitives.Create(type);
            go.name = name;
            // Picking goes through sim coordinates, not physics.
            Destroy(go.GetComponent<Collider>());
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            // Primitives.Create gives a URP Lit material that ships with the build.
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
            if (ownerId == state.LegionPlayerId)
                return _legionColor;
            if (ownerId == state.NeutralPlayerId)
                return _neutralColor;
            int index = ownerId - 1;
            // missions.csv "colors": the mission may give a start slot another colour of the palette
            // (Red Lightning plays red); players are 1..N in slot order (MissionLauncher).
            IReadOnlyList<string>? colors = _runner != null ? _runner.Mission?.Colors : null;
            if (colors != null && index >= 0 && index < colors.Count)
            {
                int named = System.Array.IndexOf(PaletteNames, colors[index]);
                if (named >= 0 && named < _playerColors.Length)
                    index = named;
            }
            return index >= 0 && index < _playerColors.Length ? _playerColors[index] : Color.white;
        }

        private Color ResourceColor(int resourceType) =>
            resourceType == Game.Sim.Resources.Primary ? _primaryCargoColor : _secondaryCargoColor;

        private static Vector3 ToWorld(int milliX, int milliY) =>
            new Vector3(milliX / (float)Grid.MilliPerCell, 0f, milliY / (float)Grid.MilliPerCell);

        private void OnDestroy()
        {
            foreach (Material material in _materials.Values)
                Destroy(material);
        }

        private abstract class Visual
        {
            private readonly Material[] _materials;

            protected Visual(Transform transform, List<MeshRenderer> renderers, Color color, float size, float centreHeight)
            {
                Transform = transform;
                Renderers = renderers;
                Color = color;
                Size = size;
                CentreHeight = centreHeight;
                _materials = new Material[renderers.Count];
                for (int i = 0; i < renderers.Count; i++)
                    _materials[i] = renderers[i].sharedMaterial;
            }

            public Transform Transform { get; }
            public List<MeshRenderer> Renderers { get; }
            public Color Color { get; }
            public float Size { get; }
            public float CentreHeight { get; }
            public bool Alive { get; set; } = true;
            public float FlashUntil { get; set; }

            // After a flash: every part gets its own colour back.
            public void RestoreMaterials()
            {
                for (int i = 0; i < Renderers.Count; i++)
                {
                    if (Renderers[i] != null)
                        Renderers[i].sharedMaterial = _materials[i];
                }
            }

            public virtual void DestroyObjects() => Destroy(Transform.gameObject);
        }

        private sealed class UnitVisual : Visual
        {
            public UnitVisual(Transform pivot, List<MeshRenderer> renderers, Color color, float size, float centreHeight, Transform? cargo)
                : base(pivot, renderers, color, size, centreHeight) => Cargo = cargo;

            public Transform? Cargo { get; }
            public bool Boosted { get; set; }
            public Vector3 From { get; set; }
            public Vector3 To { get; set; }
            public Quaternion Heading { get; set; } = Quaternion.identity;
        }

        private sealed class BuildingVisual : Visual
        {
            public BuildingVisual(Transform root, List<MeshRenderer> renderers, Color color, float size, float centreHeight)
                : base(root, renderers, color, size, centreHeight)
            {
            }

            public Transform? Arrow { get; set; }
            public Transform? Item { get; set; }
            public Transform? Rotor { get; set; }
            public bool Spinning { get; set; }
            public int OwnerId { get; set; }
            public bool Demolishing { get; set; }

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
