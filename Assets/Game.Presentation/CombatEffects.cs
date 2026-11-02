using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // combat.md UI: a hit is seen — the target flashes and a short streak runs from the attacker;
    // a death is seen — the body bursts into a few fragments of its colour that fly and shrink.
    // Driven by sim events only (HitLanded, EntityDestroyed); objects are pooled.
    [RequireComponent(typeof(SimRunner), typeof(EntityView))]
    public sealed class CombatEffects : MonoBehaviour
    {
        private const int FragmentsPerDeath = 8;

        [SerializeField] private Color _streakColor = new Color(1.00f, 0.95f, 0.70f);
        [SerializeField] private float _streakSeconds = 0.09f;
        [SerializeField] private float _deathSeconds = 0.7f;

        private readonly List<Effect> _active = new List<Effect>();
        private readonly List<Transform> _pool = new List<Transform>();
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

        // Runs before EntityView drops the dead visuals (it syncs in LateUpdate): their place and
        // colour are still known here.
        private void OnTickEvents(IReadOnlyList<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                switch (e)
                {
                    case HitLanded hit:
                        if (!_entities!.TryGetEntity(hit.TargetId, out Vector3 target, out _, out _))
                            break;
                        _entities.Flash(hit.TargetId);
                        if (_entities.TryGetEntity(hit.AttackerId, out Vector3 attacker, out _, out _))
                            Streak(attacker, target);
                        break;
                    case EntityDestroyed destroyed:
                        if (_entities!.TryGetEntity(destroyed.EntityId, out Vector3 centre, out Color color, out float size))
                            Burst(centre, color, size);
                        break;
                }
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Effect effect = _active[i];
                float t = (now - effect.Born) / effect.Seconds;
                if (t >= 1f)
                {
                    effect.Transform.gameObject.SetActive(false);
                    _pool.Add(effect.Transform);
                    _active.RemoveAt(i);
                    continue;
                }
                if (effect.Velocity != Vector3.zero)
                {
                    // Fragments fly out, fall a little and shrink to nothing.
                    effect.Transform.position = effect.Start + effect.Velocity * t + Vector3.down * (1.5f * t * t);
                    effect.Transform.localScale = Vector3.one * effect.Scale * (1f - t);
                    effect.Transform.Rotate(effect.Velocity * 300f * Time.deltaTime);
                }
            }
        }

        private void Streak(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.0001f)
                return;
            Transform streak = Take(_streakColor);
            streak.position = (from + to) * 0.5f;
            streak.rotation = Quaternion.LookRotation(delta);
            streak.localScale = new Vector3(0.06f, 0.06f, delta.magnitude);
            _active.Add(new Effect(streak, Time.unscaledTime, _streakSeconds, Vector3.zero, from, 0f));
        }

        private void Burst(Vector3 centre, Color color, float size)
        {
            float scale = Mathf.Clamp(size * 0.3f, 0.15f, 0.8f);
            for (int i = 0; i < FragmentsPerDeath; i++)
            {
                float angle = i * (360f / FragmentsPerDeath) + 20f * (i % 3);
                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 velocity = (outward * (0.8f + 0.25f * (i % 4)) + Vector3.up * (1.2f + 0.3f * (i % 2))) * Mathf.Max(1f, size * 0.6f);
                Transform fragment = Take(i % 3 == 0 ? Color.Lerp(color, Color.white, 0.5f) : color);
                fragment.position = centre;
                fragment.rotation = Quaternion.Euler(angle, angle, 0f);
                fragment.localScale = Vector3.one * scale;
                _active.Add(new Effect(fragment, Time.unscaledTime, _deathSeconds, velocity, centre, scale));
            }
        }

        private Transform Take(Color color)
        {
            Transform item;
            if (_pool.Count > 0)
            {
                item = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                item.gameObject.SetActive(true);
            }
            else
            {
                GameObject go = Primitives.Create(PrimitiveType.Cube);
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

        private sealed class Effect
        {
            public Effect(Transform transform, float born, float seconds, Vector3 velocity, Vector3 start, float scale)
            {
                Transform = transform;
                Born = born;
                Seconds = seconds;
                Velocity = velocity;
                Start = start;
                Scale = scale;
            }

            public Transform Transform { get; }
            public float Born { get; }
            public float Seconds { get; }
            public Vector3 Velocity { get; }
            public Vector3 Start { get; }
            public float Scale { get; }
        }
    }
}
