using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // Models of units and buildings put together from a few primitives until there is art
    // (decision for M1: no generated or imported models). Every type has its own silhouette;
    // the main parts take the owner's colour, details a darker or lighter shade of it,
    // metal parts neutral grey (CLAUDE.md: colour by owner, shape by type).
    // A model is a root on the ground with parts as children: scaling the root's Y grows a site.
    public static class PrimitiveModels
    {
        private const string Turret = "turret";
        private const string LegionFactory = "legion_factory";

        // Tags of units.csv that only pick a silhouette; the sim does not react to them.
        private const string SiegeTag = "siege";
        private const string ArmoredTag = "armored";
        private const string RangedTag = "ranged";
        private const string ScoutTag = "scout";
        private const string FastTag = "fast";

        private static readonly Color Metal = new Color(0.32f, 0.33f, 0.36f);
        private static readonly Color Dark = new Color(0.10f, 0.10f, 0.12f);

        // Makes a coloured primitive without a collider; the caller owns materials and parenting.
        public delegate Transform PartFactory(PrimitiveType type, Color color);

        public static Transform Unit(UnitStats stats, Color owner, PartFactory part, List<MeshRenderer> renderers, out Transform? cargo)
        {
            float s = stats.SizeMilli / (float)Grid.MilliPerCell;
            var root = new GameObject(stats.Id).transform;
            Color shade = Color.Lerp(owner, Color.black, 0.45f);
            Color light = Color.Lerp(owner, Color.white, 0.45f);
            cargo = null;

            if (stats.HasTag(UnitTags.Creep))
            {
                // A grey blob with two dark eyes looking at the camera.
                Add(root, part, renderers, PrimitiveType.Sphere, owner, new Vector3(0f, s * 0.5f, 0f), new Vector3(s, s * 0.9f, s));
                for (int side = -1; side <= 1; side += 2)
                    Add(root, part, renderers, PrimitiveType.Cube, Dark, new Vector3(side * s * 0.18f, s * 0.6f, -s * 0.42f), new Vector3(s * 0.12f, s * 0.16f, s * 0.08f));
                return root;
            }

            if (stats.HasTag(UnitTags.Worker))
            {
                // Stocky body, light head, a backpack frame that holds the cargo.
                Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, s * 0.35f, 0f), new Vector3(s * 0.75f, s * 0.7f, s * 0.6f));
                Add(root, part, renderers, PrimitiveType.Cube, light, new Vector3(0f, s * 0.85f, 0f), new Vector3(s * 0.42f, s * 0.32f, s * 0.42f));
                Add(root, part, renderers, PrimitiveType.Cube, Metal, new Vector3(0f, s * 0.45f, s * 0.38f), new Vector3(s * 0.55f, s * 0.5f, s * 0.14f));
                cargo = part(PrimitiveType.Cube, owner);
                cargo.SetParent(root, worldPositionStays: false);
                cargo.localScale = Vector3.one * s * 0.42f;
                cargo.localPosition = new Vector3(0f, s * 1.25f, 0f);
                cargo.gameObject.SetActive(false);
                return root;
            }

            if (stats.HasTag(SiegeTag))
            {
                // Artillery: a low tracked hull and a long barrel raised at the camera.
                Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(0f, s * 0.15f, 0f), new Vector3(s * 0.8f, s * 0.3f, s * 0.9f));
                Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, s * 0.4f, s * 0.1f), new Vector3(s * 0.6f, s * 0.25f, s * 0.55f));
                Add(root, part, renderers, PrimitiveType.Cylinder, Metal, new Vector3(0f, s * 0.65f, -s * 0.35f), new Vector3(s * 0.12f, s * 0.45f, s * 0.12f));
                root.GetChild(root.childCount - 1).localRotation = Quaternion.Euler(55f, 0f, 0f);
                return root;
            }

            if (stats.HasTag(ArmoredTag) && stats.HasTag(RangedTag))
            {
                // Tank: a wide hull, a light turret and a short thick barrel.
                Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, s * 0.2f, 0f), new Vector3(s * 0.85f, s * 0.4f, s * 0.9f));
                Add(root, part, renderers, PrimitiveType.Cube, light, new Vector3(0f, s * 0.5f, 0f), new Vector3(s * 0.5f, s * 0.22f, s * 0.5f));
                Add(root, part, renderers, PrimitiveType.Cylinder, Metal, new Vector3(0f, s * 0.5f, -s * 0.4f), new Vector3(s * 0.16f, s * 0.25f, s * 0.16f));
                root.GetChild(root.childCount - 1).localRotation = Quaternion.Euler(90f, 0f, 0f);
                return root;
            }

            if (stats.HasTag(ArmoredTag))
            {
                // Brute: a hunched block with heavy shoulders and a small head sunk in front.
                Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, s * 0.4f, 0f), new Vector3(s * 0.7f, s * 0.8f, s * 0.6f));
                for (int side = -1; side <= 1; side += 2)
                    Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(side * s * 0.42f, s * 0.7f, 0f), new Vector3(s * 0.3f, s * 0.35f, s * 0.45f));
                Add(root, part, renderers, PrimitiveType.Cube, light, new Vector3(0f, s * 0.75f, -s * 0.3f), Vector3.one * s * 0.25f);
                return root;
            }

            if (stats.HasTag(ScoutTag))
            {
                // Scout: a low flat body on the run and a tall antenna for its wide sight.
                Add(root, part, renderers, PrimitiveType.Sphere, owner, new Vector3(0f, s * 0.3f, 0f), new Vector3(s * 0.75f, s * 0.4f, s * 0.9f));
                Add(root, part, renderers, PrimitiveType.Sphere, light, new Vector3(0f, s * 0.45f, -s * 0.3f), Vector3.one * s * 0.3f);
                Add(root, part, renderers, PrimitiveType.Cylinder, Metal, new Vector3(s * 0.15f, s * 0.8f, s * 0.2f), new Vector3(s * 0.05f, s * 0.35f, s * 0.05f));
                Add(root, part, renderers, PrimitiveType.Sphere, light, new Vector3(s * 0.15f, s * 1.15f, s * 0.2f), Vector3.one * s * 0.14f);
                return root;
            }

            if (stats.HasTag(FastTag))
            {
                // Raider: a diamond body leaning forward and a blade at the camera.
                Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, s * 0.4f, 0f), Vector3.one * s * 0.5f);
                root.GetChild(root.childCount - 1).localRotation = Quaternion.Euler(45f, 0f, 45f);
                Add(root, part, renderers, PrimitiveType.Cube, light, new Vector3(s * 0.25f, s * 0.35f, -s * 0.35f), new Vector3(s * 0.06f, s * 0.12f, s * 0.6f));
                return root;
            }

            // Foot soldier: capsule body and a light head; melee holds a shield, ranged a rifle.
            Add(root, part, renderers, PrimitiveType.Capsule, owner, new Vector3(0f, s * 0.45f, 0f), new Vector3(s * 0.6f, s * 0.45f, s * 0.6f));
            Add(root, part, renderers, PrimitiveType.Sphere, light, new Vector3(0f, s * 1.05f, 0f), Vector3.one * s * 0.36f);
            if (stats.HasTag(RangedTag))
                Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(s * 0.3f, s * 0.55f, -s * 0.3f), new Vector3(s * 0.1f, s * 0.1f, s * 0.7f));
            else
                Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(-s * 0.1f, s * 0.5f, -s * 0.38f), new Vector3(s * 0.6f, s * 0.7f, s * 0.1f));
            return root;
        }

        // Height of the finished model, for labels and the arrow on conveyors.
        public static float BuildingHeight(string id)
        {
            switch (id)
            {
                case BuildingKinds.Headquarters: return 2.4f;
                case BuildingKinds.Factory: return 1.7f;
                case LegionFactory: return 1.7f;
                case Turret: return 1.3f;
                case BuildingKinds.Drill: return 1.0f;
                case BuildingKinds.Storage: return 1.0f;
                case BuildingKinds.Conveyor: return 0.12f;
                case BuildingKinds.ConveyorBridge: return 0.45f;
                default: return 1.2f;
            }
        }

        public static Transform Building(Building building, Color owner, PartFactory part, List<MeshRenderer> renderers)
        {
            var root = new GameObject(building.Stats.Id).transform;
            float w = building.Width, h = building.Height;
            Color shade = Color.Lerp(owner, Color.black, 0.4f);
            Color light = Color.Lerp(owner, Color.white, 0.35f);
            // Front faces the camera (−Z): doors and openings go there.

            switch (building.Stats.Id)
            {
                case BuildingKinds.Headquarters:
                    Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(0f, 0.2f, 0f), new Vector3(w - 0.1f, 0.4f, h - 0.1f));
                    Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, 1.0f, 0.1f), new Vector3(w * 0.75f, 1.3f, h * 0.65f));
                    Add(root, part, renderers, PrimitiveType.Cube, light, new Vector3(-w * 0.22f, 1.7f, h * 0.15f), new Vector3(0.9f, 1.4f, 0.9f));
                    Add(root, part, renderers, PrimitiveType.Cylinder, Metal, new Vector3(-w * 0.22f, 2.5f, h * 0.15f), new Vector3(0.12f, 0.2f, 0.12f));
                    Add(root, part, renderers, PrimitiveType.Cube, Dark, new Vector3(w * 0.12f, 0.75f, -h * 0.23f), new Vector3(1.0f, 0.8f, 0.1f));
                    break;

                case BuildingKinds.Factory:
                case LegionFactory:
                    Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(0f, 0.12f, 0f), new Vector3(w - 0.1f, 0.24f, h - 0.1f));
                    Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, 0.7f, 0.15f), new Vector3(w - 0.4f, 1.0f, h - 0.7f));
                    Add(root, part, renderers, PrimitiveType.Cube, light, new Vector3(0f, 1.25f, 0.15f), new Vector3(w - 0.6f, 0.15f, h - 0.9f));
                    Add(root, part, renderers, PrimitiveType.Cylinder, Metal, new Vector3(w * 0.3f, 1.2f, h * 0.3f), new Vector3(0.35f, 0.6f, 0.35f));
                    Add(root, part, renderers, PrimitiveType.Cube, Dark, new Vector3(0f, 0.55f, -(h - 0.7f) * 0.5f + 0.1f), new Vector3(1.0f, 0.65f, 0.1f));
                    break;

                case BuildingKinds.Drill:
                    Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(0f, 0.12f, 0f), new Vector3(w - 0.12f, 0.24f, h - 0.12f));
                    Add(root, part, renderers, PrimitiveType.Cylinder, owner, new Vector3(0f, 0.6f, 0f), new Vector3(w * 0.68f, 0.38f, h * 0.68f));
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i % 2 == 0 ? -1 : 1) * (w * 0.5f - 0.18f), z = (i < 2 ? -1 : 1) * (h * 0.5f - 0.18f);
                        Add(root, part, renderers, PrimitiveType.Cube, Metal, new Vector3(x, 0.4f, z), new Vector3(0.14f, 0.5f, 0.14f));
                    }
                    break;

                case BuildingKinds.Storage:
                    Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, 0.48f, 0f), new Vector3(w - 0.25f, 0.9f, h - 0.25f));
                    Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(0f, 0.3f, 0f), new Vector3(w - 0.18f, 0.1f, h - 0.18f));
                    Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(0f, 0.7f, 0f), new Vector3(w - 0.18f, 0.1f, h - 0.18f));
                    Add(root, part, renderers, PrimitiveType.Cube, light, new Vector3(0f, 0.96f, 0f), new Vector3(w - 0.45f, 0.06f, h - 0.45f));
                    break;

                case BuildingKinds.Conveyor:
                    // A dark belt between two rails in the owner's colour, along the direction.
                    bool alongX = building.Direction == Direction.Right || building.Direction == Direction.Left;
                    Add(root, part, renderers, PrimitiveType.Cube, Dark, new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.08f, 0.9f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 offset = alongX ? new Vector3(0f, 0.07f, side * 0.4f) : new Vector3(side * 0.4f, 0.07f, 0f);
                        Vector3 scale = alongX ? new Vector3(0.96f, 0.12f, 0.1f) : new Vector3(0.1f, 0.12f, 0.96f);
                        Add(root, part, renderers, PrimitiveType.Cube, owner, offset, scale);
                    }
                    break;

                case Turret:
                    // A squat round base on a plinth, a gun housing and a barrel pointing at the camera.
                    Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(0f, 0.15f, 0f), new Vector3(w - 0.2f, 0.3f, h - 0.2f));
                    Add(root, part, renderers, PrimitiveType.Cylinder, owner, new Vector3(0f, 0.55f, 0f), new Vector3(w * 0.6f, 0.25f, h * 0.6f));
                    Add(root, part, renderers, PrimitiveType.Cube, light, new Vector3(0f, 0.95f, 0f), new Vector3(0.7f, 0.4f, 0.7f));
                    Add(root, part, renderers, PrimitiveType.Cylinder, Metal, new Vector3(0f, 0.95f, -0.65f), new Vector3(0.14f, 0.45f, 0.14f));
                    root.GetChild(root.childCount - 1).localRotation = Quaternion.Euler(90f, 0f, 0f);
                    break;

                case BuildingKinds.ConveyorBridge:
                    Add(root, part, renderers, PrimitiveType.Cube, shade, new Vector3(0f, 0.18f, 0f), new Vector3(0.8f, 0.36f, 0.8f));
                    Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, 0.4f, 0f), new Vector3(0.9f, 0.1f, 0.9f));
                    break;

                default:
                    Add(root, part, renderers, PrimitiveType.Cube, owner, new Vector3(0f, 0.6f, 0f), new Vector3(w - 0.1f, 1.2f, h - 0.1f));
                    break;
            }
            return root;
        }

        private static void Add(Transform root, PartFactory part, List<MeshRenderer> renderers, PrimitiveType type, Color color, Vector3 position, Vector3 scale)
        {
            Transform t = part(type, color);
            t.SetParent(root, worldPositionStays: false);
            t.localPosition = position;
            // Unity's capsule and cylinder are 2 units tall at scale 1: for them callers pass half the height.
            t.localScale = scale;
            renderers.Add(t.GetComponent<MeshRenderer>());
        }
    }
}
