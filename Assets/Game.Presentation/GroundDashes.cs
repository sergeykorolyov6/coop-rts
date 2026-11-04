using System.Collections.Generic;
using UnityEngine;

namespace Game.Presentation
{
    // Dashed marks on the ground in one colour (turret range, rally line): a pool of flat cubes
    // reused frame to frame. Begin, add dashes and lines, End hides whatever was not used.
    public sealed class GroundDashes
    {
        private readonly List<Transform> _pool = new List<Transform>();
        private readonly Transform _root;
        private readonly string _name;
        private readonly Color _color;
        private readonly float _width;
        private Material? _material;
        private int _used;

        public GroundDashes(Transform parent, string name, Color color, float width)
        {
            _root = new GameObject(name).transform;
            _root.SetParent(parent, worldPositionStays: false);
            _name = name;
            _color = color;
            _width = width;
        }

        public void Begin() => _used = 0;

        public void End()
        {
            for (int i = _used; i < _pool.Count; i++)
                _pool[i].gameObject.SetActive(false);
        }

        // Dashes of about `step` world units, each `fill` of its step long.
        public void Line(Vector3 from, Vector3 to, float step, float fill)
        {
            int dashes = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / step));
            float gap = (1f - fill) * 0.5f;
            for (int i = 0; i < dashes; i++)
                Dash(Vector3.Lerp(from, to, (i + gap) / dashes), Vector3.Lerp(from, to, (i + 1f - gap) / dashes));
        }

        public void Dash(Vector3 from, Vector3 to)
        {
            if (_used == _pool.Count)
                _pool.Add(Create(PrimitiveType.Cube, _name));
            Transform t = _pool[_used++];
            t.gameObject.SetActive(true);
            Vector3 mid = (from + to) * 0.5f;
            t.position = new Vector3(mid.x, 0.06f, mid.z);
            Vector3 along = to - from;
            t.rotation = Quaternion.LookRotation(along.sqrMagnitude > 0f ? along : Vector3.forward);
            t.localScale = new Vector3(_width, 0.03f, along.magnitude);
        }

        // A primitive in the marks' colour under the same root (the rally flag).
        public Transform Create(PrimitiveType type, string name)
        {
            GameObject go = Primitives.Create(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            _material ??= new Material(renderer.sharedMaterial) { color = _color };
            renderer.sharedMaterial = _material;
            go.transform.SetParent(_root, worldPositionStays: false);
            return go.transform;
        }

        public void Destroy()
        {
            if (_material != null)
                Object.Destroy(_material);
        }
    }
}
