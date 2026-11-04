using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using Grid = Game.Sim.Grid;

namespace Game.Presentation
{
    // factory.md M2 UI: for the selected own factory or cave, a flag at its rally point and a dashed
    // line to it on the ground.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class RallyView : MonoBehaviour
    {
        [SerializeField] private Color _color = new Color(0.55f, 0.95f, 0.55f);

        private readonly List<Transform> _dashes = new List<Transform>();
        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private Transform? _root;
        private Transform? _flag;
        private Material? _material;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
            _root = new GameObject("RallyPoint").transform;
            _root.SetParent(transform, worldPositionStays: false);
        }

        private void LateUpdate()
        {
            int used = 0;
            bool flag = false;
            Simulation? simulation = _runner!.Simulation;
            Building? b = simulation?.State.FindBuilding(_selection!.SelectedBuildingId);
            if (b != null && !b.IsDead && b.Factory != null && b.Factory.HasRally && b.OwnerId == _localPlayer!.PlayerId)
            {
                var from = new Vector3(b.CellX + b.Width * 0.5f, 0f, b.CellY + b.Height * 0.5f);
                var to = new Vector3(b.Factory.RallyX / (float)Grid.MilliPerCell, 0f, b.Factory.RallyY / (float)Grid.MilliPerCell);
                int dashes = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(from, to) / 0.8f));
                for (int i = 0; i < dashes; i++)
                    used = Dash(Vector3.Lerp(from, to, (i + 0.25f) / dashes), Vector3.Lerp(from, to, (i + 0.75f) / dashes), used);
                Flag().position = to + Vector3.up * 0.6f;
                flag = true;
            }
            if (_flag != null)
                _flag.gameObject.SetActive(flag);
            for (int i = used; i < _dashes.Count; i++)
                _dashes[i].gameObject.SetActive(false);
        }

        private Transform Flag()
        {
            if (_flag != null)
                return _flag;
            _flag = Create(PrimitiveType.Cylinder, "Flag");
            _flag.localScale = new Vector3(0.08f, 0.6f, 0.08f);
            Transform cloth = Create(PrimitiveType.Cube, "Cloth");
            cloth.SetParent(_flag, worldPositionStays: false);
            cloth.localScale = new Vector3(5f, 0.4f, 0.6f);
            cloth.localPosition = new Vector3(2.5f, 0.7f, 0f);
            return _flag;
        }

        private int Dash(Vector3 from, Vector3 to, int used)
        {
            if (used == _dashes.Count)
                _dashes.Add(Create(PrimitiveType.Cube, "RallyDash"));
            Transform t = _dashes[used];
            t.gameObject.SetActive(true);
            Vector3 mid = (from + to) * 0.5f;
            t.position = new Vector3(mid.x, 0.07f, mid.z);
            Vector3 along = to - from;
            t.rotation = Quaternion.LookRotation(along.sqrMagnitude > 0f ? along : Vector3.forward);
            t.localScale = new Vector3(0.12f, 0.03f, along.magnitude);
            return used + 1;
        }

        private Transform Create(PrimitiveType type, string name)
        {
            GameObject go = Primitives.Create(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            _material ??= new Material(renderer.sharedMaterial) { color = _color };
            renderer.sharedMaterial = _material;
            go.transform.SetParent(_root, worldPositionStays: false);
            return go.transform;
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
