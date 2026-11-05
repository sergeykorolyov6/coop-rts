using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // combat.md / factions.md UI, driven by sim events only (HitLanded, EntityDestroyed, AbilityUsed):
    // - a hit: the target flashes; a ranged attacker's projectile (units.csv / buildings.csv
    //   "projectile": arrow, shell, stone, bolt, spit) flies at it, a melee blow leaves a short streak;
    // - a death: the body bursts into fragments of its colour;
    // - an ability: its effect (abilities.csv "effect": starfall, meteor, whirl, heal, grace, ring,
    //   flash, charge, bolt, sparks) plays at its point.
    // Everything is pooled primitives moved by small tweens; the sim has already applied the result.
    [RequireComponent(typeof(SimRunner), typeof(EntityView))]
    public sealed class CombatEffects : MonoBehaviour
    {
        private const int FragmentsPerDeath = 8;

        [SerializeField] private Color _streakColor = new Color(1.00f, 0.95f, 0.70f);
        [SerializeField] private float _streakSeconds = 0.09f;
        [SerializeField] private float _deathSeconds = 0.7f;

        private static readonly Color Arrow = new Color(0.55f, 0.40f, 0.25f);
        private static readonly Color Shell = new Color(0.25f, 0.25f, 0.28f);
        private static readonly Color Stone = new Color(0.50f, 0.48f, 0.44f);
        private static readonly Color Bolt = new Color(0.55f, 0.85f, 1.00f);
        private static readonly Color Spit = new Color(0.45f, 0.85f, 0.30f);
        private static readonly Color Star = new Color(1.00f, 0.90f, 0.40f);
        private static readonly Color Fire = new Color(1.00f, 0.45f, 0.15f);
        private static readonly Color Heal = new Color(0.40f, 1.00f, 0.50f);
        private static readonly Color Gold = new Color(1.00f, 0.80f, 0.25f);
        private static readonly Color Spark = new Color(1.00f, 0.35f, 0.25f);

        private readonly List<Anim> _active = new List<Anim>();
        private readonly List<Transform> _cubes = new List<Transform>();
        private readonly List<Transform> _spheres = new List<Transform>();
        private readonly List<Transform> _discs = new List<Transform>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        private SimRunner? _runner;
        private EntityView? _entities;
        private Transform? _root;
        private Material? _baseMaterial;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _entities = GetComponent<EntityView>();
            _root = new GameObject("CombatEffects").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        private void OnEnable() => _runner!.TickEvents += OnTickEvents;

        private void OnDisable() => _runner!.TickEvents -= OnTickEvents;

        // Runs before EntityView drops the dead visuals and moves the units (it syncs in LateUpdate):
        // their place and colour are still the drawn ones here.
        private void OnTickEvents(IReadOnlyList<SimEvent> events)
        {
            SimState? state = _runner!.Simulation?.State;
            if (state == null)
                return;
            foreach (SimEvent e in events)
            {
                switch (e)
                {
                    case HitLanded hit:
                        if (!_entities!.TryGetEntity(hit.TargetId, out Vector3 target, out _, out _))
                            break;
                        _entities.Flash(hit.TargetId);
                        if (_entities.TryGetEntity(hit.AttackerId, out Vector3 attacker, out _, out _))
                            Shot(ProjectileOf(state, hit.AttackerId), attacker, target);
                        break;
                    case EntityDestroyed destroyed:
                        if (_entities!.TryGetEntity(destroyed.EntityId, out Vector3 centre, out Color color, out float size))
                            Burst(centre, color, size);
                        break;
                    case AbilityUsed used:
                        Spell(state, used);
                        break;
                }
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Anim a = _active[i];
                float t = (now - a.Born - a.Delay) / a.Seconds;
                if (t < 0f)
                {
                    a.Transform.gameObject.SetActive(false);
                    continue;
                }
                if (t >= 1f)
                {
                    a.Transform.gameObject.SetActive(false);
                    a.Pool.Add(a.Transform);
                    _active.RemoveAt(i);
                    continue;
                }
                a.Transform.gameObject.SetActive(true);
                Vector3 p = Vector3.Lerp(a.From, a.To, a.Ease ? 1f - (1f - t) * (1f - t) : t);
                p.y += a.Arc * 4f * t * (1f - t) - a.Fall * t * t;
                if (a.Orbit > 0f)
                {
                    float angle = a.Phase + t * Mathf.PI * 4f;
                    p += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * a.Orbit;
                }
                a.Transform.position = p;
                a.Transform.localScale = Vector3.Lerp(a.ScaleFrom, a.ScaleTo, t);
                if (a.Spin != Vector3.zero)
                    a.Transform.Rotate(a.Spin * Time.deltaTime);
                else if (a.FaceAlong && a.To != a.From)
                {
                    Vector3 next = Vector3.Lerp(a.From, a.To, Mathf.Min(1f, t + 0.05f));
                    next.y += a.Arc * 4f * (t + 0.05f) * (0.95f - t);
                    if ((next - p).sqrMagnitude > 0.0001f)
                        a.Transform.rotation = Quaternion.LookRotation(next - p);
                }
            }
        }

        private static string ProjectileOf(SimState state, int attackerId)
        {
            if (state.FindUnit(attackerId) is Unit u)
                return u.Stats.Projectile;
            return state.FindBuilding(attackerId)?.Stats.Projectile ?? "";
        }

        // A projectile at the speed of its kind; without one, the melee streak.
        private void Shot(string kind, Vector3 from, Vector3 to)
        {
            float distance = Vector3.Distance(from, to);
            switch (kind)
            {
                case "arrow":
                    Fly(_cubes, Arrow, from, to, distance / 22f, new Vector3(0.05f, 0.05f, 0.45f), arc: distance * 0.08f);
                    return;
                case "shell":
                    Fly(_spheres, Shell, from, to, distance / 18f, Vector3.one * 0.2f, arc: 0f);
                    return;
                case "stone":
                    Fly(_spheres, Stone, from, to, Mathf.Max(0.35f, distance / 10f), Vector3.one * 0.32f, arc: 1f + distance * 0.25f);
                    return;
                case "bolt":
                    Fly(_cubes, Bolt, from, to, distance / 35f, new Vector3(0.08f, 0.08f, 0.6f), arc: 0f);
                    return;
                case "spit":
                    Fly(_spheres, Spit, from, to, distance / 14f, Vector3.one * 0.18f, arc: distance * 0.15f);
                    return;
            }
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.0001f)
                return;
            Transform streak = Take(_cubes, PrimitiveType.Cube, _streakColor);
            streak.rotation = Quaternion.LookRotation(delta);
            Add(new Anim(streak, _cubes, _streakSeconds) { From = (from + to) * 0.5f, To = (from + to) * 0.5f, ScaleFrom = new Vector3(0.06f, 0.06f, delta.magnitude), ScaleTo = new Vector3(0.02f, 0.02f, delta.magnitude) });
        }

        private void Fly(List<Transform> pool, Color color, Vector3 from, Vector3 to, float seconds, Vector3 scale, float arc, float delay = 0f)
        {
            Transform t = Take(pool, pool == _cubes ? PrimitiveType.Cube : PrimitiveType.Sphere, color);
            Add(new Anim(t, pool, Mathf.Max(0.05f, seconds)) { From = from, To = to, Arc = arc, ScaleFrom = scale, ScaleTo = scale, FaceAlong = true, Delay = delay });
        }

        // factions.md: each ability's look, at the point the sim reports.
        private void Spell(SimState state, AbilityUsed used)
        {
            if (!state.Data.TryGetAbility(used.AbilityId, out AbilityStats? ability))
                return;
            var at = new Vector3(used.X / (float)Game.Sim.Grid.MilliPerCell, 0f, used.Y / (float)Game.Sim.Grid.MilliPerCell);
            float radius = Mathf.Max(0.5f, ability!.RadiusMilli / (float)Game.Sim.Grid.MilliPerCell);
            Vector3 caster = _entities!.TryGetUnitPosition(used.UnitId, out Vector3 drawn) ? drawn : at;
            switch (ability.Effect)
            {
                case "starfall":
                    Disc(at, radius, Star, 1.1f);
                    for (int i = 0; i < 10; i++)
                    {
                        // Spread over the area in a fixed pattern: the same cast looks the same.
                        float angle = i * 2.4f, r = radius * Mathf.Sqrt((i + 0.5f) / 10f);
                        Vector3 hit = at + new Vector3(Mathf.Cos(angle) * r, 0.1f, Mathf.Sin(angle) * r);
                        Transform star = Take(_spheres, PrimitiveType.Sphere, Star);
                        Add(new Anim(star, _spheres, 0.45f) { From = hit + new Vector3(-1.5f, 7f, -1.5f), To = hit, ScaleFrom = Vector3.one * 0.35f, ScaleTo = Vector3.one * 0.2f, Delay = i * 0.06f, Ease = true });
                        Pop(hit, Star, 0.45f + i * 0.06f);
                    }
                    break;
                case "meteor":
                    Transform rock = Take(_spheres, PrimitiveType.Sphere, Fire);
                    Add(new Anim(rock, _spheres, 0.7f) { From = at + new Vector3(-4f, 12f, -4f), To = at, ScaleFrom = Vector3.one * 1.6f, ScaleTo = Vector3.one * 1.2f, Ease = true });
                    Disc(at, radius, Fire, 0.6f, delay: 0.65f);
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = i * Mathf.PI / 6f;
                        Transform ember = Take(_cubes, PrimitiveType.Cube, i % 2 == 0 ? Fire : Star);
                        Add(new Anim(ember, _cubes, 0.6f) { From = at, To = at + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius, Arc = 1.5f, ScaleFrom = Vector3.one * 0.3f, ScaleTo = Vector3.zero, Delay = 0.65f, Spin = new Vector3(400f, 200f, 0f) });
                    }
                    break;
                case "whirl":
                    Disc(at, radius, Spark, 0.4f);
                    for (int i = 0; i < 6; i++)
                    {
                        Transform blade = Take(_cubes, PrimitiveType.Cube, new Color(0.85f, 0.85f, 0.9f));
                        Add(new Anim(blade, _cubes, 0.5f) { From = at + Vector3.up * 0.6f, To = at + Vector3.up * 0.6f, Orbit = radius * 0.8f, Phase = i * Mathf.PI / 3f, ScaleFrom = new Vector3(0.1f, 0.1f, 0.7f), ScaleTo = new Vector3(0.05f, 0.05f, 0.5f), Spin = new Vector3(0f, 900f, 0f) });
                    }
                    break;
                case "heal":
                case "grace":
                    Disc(at, radius, Heal, 0.9f);
                    int motes = ability.Effect == "grace" ? 16 : 8;
                    for (int i = 0; i < motes; i++)
                    {
                        float angle = i * 2.4f, r = radius * Mathf.Sqrt((i + 0.5f) / motes);
                        Vector3 p = at + new Vector3(Mathf.Cos(angle) * r, 0.2f, Mathf.Sin(angle) * r);
                        Transform mote = Take(_spheres, PrimitiveType.Sphere, Heal);
                        Add(new Anim(mote, _spheres, 0.9f) { From = p, To = p + Vector3.up * 2.2f, ScaleFrom = Vector3.one * 0.25f, ScaleTo = Vector3.zero, Delay = i * 0.03f });
                    }
                    break;
                case "ring":
                    Disc(at, radius, Gold, 0.7f);
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4f;
                        Transform spark = Take(_cubes, PrimitiveType.Cube, Gold);
                        Add(new Anim(spark, _cubes, 0.6f) { From = at + Vector3.up * 0.5f, To = at + Vector3.up * 0.5f + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius, ScaleFrom = Vector3.one * 0.2f, ScaleTo = Vector3.one * 0.05f });
                    }
                    break;
                case "flash":
                case "charge":
                    Pop(caster + Vector3.up * 0.5f, Bolt, 0f);
                    Pop(at + Vector3.up * 0.5f, ability.Effect == "charge" ? Gold : Bolt, 0.02f);
                    if (ability.Effect == "charge")
                        Disc(at, radius, Gold, 0.4f);
                    break;
                case "bolt":
                    Fly(_spheres, Star, caster + Vector3.up * 0.9f, at + Vector3.up * 0.4f, Vector3.Distance(caster, at) / 25f, Vector3.one * 0.35f, arc: 0f);
                    Pop(at + Vector3.up * 0.4f, Star, Vector3.Distance(caster, at) / 25f);
                    break;
                case "sparks":
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = i * Mathf.PI / 3f;
                        Transform spark = Take(_cubes, PrimitiveType.Cube, Spark);
                        Add(new Anim(spark, _cubes, 0.35f) { From = caster + Vector3.up * 0.4f, To = caster + Vector3.up * 0.4f + new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle)) * 0.7f, ScaleFrom = Vector3.one * 0.12f, ScaleTo = Vector3.zero });
                    }
                    break;
            }
        }

        // A flat disc on the ground growing to the area's radius and fading by shrinking its height.
        private void Disc(Vector3 at, float radius, Color color, float seconds, float delay = 0f)
        {
            Transform disc = Take(_discs, PrimitiveType.Cylinder, Color.Lerp(color, Color.black, 0.25f));
            Add(new Anim(disc, _discs, seconds) { From = at + Vector3.up * 0.03f, To = at + Vector3.up * 0.03f, ScaleFrom = new Vector3(0.2f, 0.02f, 0.2f), ScaleTo = new Vector3(radius * 2f, 0.005f, radius * 2f), Delay = delay, Ease = true });
        }

        // A quick flash: a sphere that swells and vanishes.
        private void Pop(Vector3 at, Color color, float delay)
        {
            Transform flash = Take(_spheres, PrimitiveType.Sphere, color);
            Add(new Anim(flash, _spheres, 0.25f) { From = at, To = at, ScaleFrom = Vector3.one * 0.3f, ScaleTo = Vector3.one * 1.1f, Delay = delay });
        }

        private void Burst(Vector3 centre, Color color, float size)
        {
            float scale = Mathf.Clamp(size * 0.3f, 0.15f, 0.8f);
            for (int i = 0; i < FragmentsPerDeath; i++)
            {
                float angle = i * (360f / FragmentsPerDeath) + 20f * (i % 3);
                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                float spread = (0.8f + 0.25f * (i % 4)) * Mathf.Max(1f, size * 0.6f);
                Transform fragment = Take(_cubes, PrimitiveType.Cube, i % 3 == 0 ? Color.Lerp(color, Color.white, 0.5f) : color);
                fragment.rotation = Quaternion.Euler(angle, angle, 0f);
                Add(new Anim(fragment, _cubes, _deathSeconds)
                {
                    From = centre,
                    To = centre + outward * spread,
                    Arc = (1.2f + 0.3f * (i % 2)) * 0.5f,
                    Fall = 1.5f,
                    ScaleFrom = Vector3.one * scale,
                    ScaleTo = Vector3.zero,
                    Spin = outward * 300f,
                });
            }
        }

        private void Add(Anim anim)
        {
            anim.Born = Time.unscaledTime;
            _active.Add(anim);
            if (anim.Delay > 0f)
                anim.Transform.gameObject.SetActive(false);
        }

        private Transform Take(List<Transform> pool, PrimitiveType type, Color color)
        {
            Transform item;
            if (pool.Count > 0)
            {
                item = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                item.gameObject.SetActive(true);
                item.rotation = Quaternion.identity;
            }
            else
            {
                GameObject go = Primitives.Create(type);
                go.name = "Effect";
                Destroy(go.GetComponent<Collider>());
                _baseMaterial ??= go.GetComponent<MeshRenderer>().sharedMaterial;
                go.transform.SetParent(_root, worldPositionStays: false);
                item = go.transform;
            }
            item.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            return item;
        }

        private Material MaterialFor(Color color)
        {
            if (!_materials.TryGetValue(color, out Material? material))
            {
                material = new Material(_baseMaterial) { color = color };
                _materials.Add(color, material);
            }
            return material;
        }

        private void OnDestroy()
        {
            foreach (Material material in _materials.Values)
                Destroy(material);
        }

        // One tweened primitive: from → to (with an arc up, a fall down or an orbit), scaling, spinning.
        private sealed class Anim
        {
            public Anim(Transform transform, List<Transform> pool, float seconds)
            {
                Transform = transform;
                Pool = pool;
                Seconds = seconds;
            }

            public Transform Transform { get; }
            public List<Transform> Pool { get; }
            public float Seconds { get; }
            public float Born { get; set; }
            public float Delay { get; set; }
            public Vector3 From { get; set; }
            public Vector3 To { get; set; }
            public float Arc { get; set; }
            public float Fall { get; set; }
            public float Orbit { get; set; }
            public float Phase { get; set; }
            public bool Ease { get; set; }
            public bool FaceAlong { get; set; }
            public Vector3 ScaleFrom { get; set; }
            public Vector3 ScaleTo { get; set; }
            public Vector3 Spin { get; set; }
        }
    }
}
