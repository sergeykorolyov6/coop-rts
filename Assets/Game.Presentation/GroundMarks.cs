using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // Flat boxes on the ground in plain colours — silhouettes, frames, arrows, beams: one pool of
    // cubes reused frame to frame, one material per colour. Begin, take a box for every mark, End
    // hides whatever was not taken. (GroundDashes is the one-colour sibling for dashed lines.)
    public sealed class GroundMarks
    {
        // How far from the cell's centre an arrow's middle sits, towards the side it points to.
        private const float ArrowForward = 0.18f;

        private readonly List<Transform> _pool = new List<Transform>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private readonly Transform _root;
        private readonly string _name;
        private Material? _base;
        private int _used;

        public GroundMarks(Transform parent, string name)
        {
            _root = new GameObject(name).transform;
            _root.SetParent(parent, worldPositionStays: false);
            _name = name;
        }

        public void Begin() => _used = 0;

        public void End()
        {
            for (int i = _used; i < _pool.Count; i++)
                _pool[i].gameObject.SetActive(false);
        }

        public void Clear()
        {
            Begin();
            End();
        }

        // A box in this colour, shown; the caller places and sizes it.
        public Transform Take(Color color)
        {
            if (_used == _pool.Count)
            {
                GameObject go = Primitives.Create(PrimitiveType.Cube);
                go.name = _name;
                Object.Destroy(go.GetComponent<Collider>());
                _base ??= go.GetComponent<MeshRenderer>().sharedMaterial;
                go.transform.SetParent(_root, worldPositionStays: false);
                _pool.Add(go.transform);
            }
            Transform box = _pool[_used++];
            box.gameObject.SetActive(true);
            box.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            return box;
        }

        // A square on the cell (x, y): `size` of the cell wide, `lift` above the ground.
        public void Tile(int x, int y, Color color, float size, float thickness, float lift)
        {
            Transform tile = Take(color);
            tile.localScale = new Vector3(size, thickness, size);
            tile.localRotation = Quaternion.identity;
            tile.localPosition = Ground.Lift(new Vector3(x + 0.5f, lift, y + 0.5f));
        }

        // A bar from the centre of the cell (x, y) towards the side a belt there gives to.
        public void Arrow(int x, int y, Direction direction, Color color, Vector3 size, float lift)
        {
            Transform arrow = Take(color);
            Quaternion facing = Ground.Facing(direction);
            arrow.localScale = size;
            arrow.localRotation = facing;
            arrow.localPosition = Ground.Lift(new Vector3(x + 0.5f, lift, y + 0.5f)) + facing * new Vector3(0f, 0f, ArrowForward);
        }

        // The border cells of a frame of cells, each as a small square.
        public void Frame(RectInt cells, Color color, float size, float thickness, float lift)
        {
            for (int x = cells.xMin; x < cells.xMax; x++)
            {
                Tile(x, cells.yMin, color, size, thickness, lift);
                if (cells.height > 1)
                    Tile(x, cells.yMax - 1, color, size, thickness, lift);
            }
            for (int y = cells.yMin + 1; y < cells.yMax - 1; y++)
            {
                Tile(cells.xMin, y, color, size, thickness, lift);
                if (cells.width > 1)
                    Tile(cells.xMax - 1, y, color, size, thickness, lift);
            }
        }

        public void Destroy()
        {
            foreach (Material material in _materials.Values)
                Object.Destroy(material);
        }

        private Material MaterialFor(Color color)
        {
            if (!_materials.TryGetValue(color, out Material? material))
            {
                material = new Material(_base) { color = color };
                _materials.Add(color, material);
            }
            return material;
        }
    }
}
