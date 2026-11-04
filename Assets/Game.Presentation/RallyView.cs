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

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private GroundDashes? _dashes;
        private Transform? _flag;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
            _dashes = new GroundDashes(transform, "RallyPoint", _color, 0.12f);
        }

        private void LateUpdate()
        {
            _dashes!.Begin();
            bool flag = false;
            Simulation? simulation = _runner!.Simulation;
            Building? b = simulation?.State.FindBuilding(_selection!.SelectedBuildingId);
            if (b != null && !b.IsDead && b.Factory != null && b.Factory.HasRally && b.OwnerId == _localPlayer!.PlayerId)
            {
                var from = new Vector3(b.CellX + b.Width * 0.5f, 0f, b.CellY + b.Height * 0.5f);
                var to = new Vector3(b.Factory.RallyX / (float)Grid.MilliPerCell, 0f, b.Factory.RallyY / (float)Grid.MilliPerCell);
                _dashes.Line(from, to, 0.8f, 0.5f);
                Flag().position = to + Vector3.up * 0.6f;
                flag = true;
            }
            if (_flag != null)
                _flag.gameObject.SetActive(flag);
            _dashes.End();
        }

        private Transform Flag()
        {
            if (_flag != null)
                return _flag;
            _flag = _dashes!.Create(PrimitiveType.Cylinder, "Flag");
            _flag.localScale = new Vector3(0.08f, 0.6f, 0.08f);
            Transform cloth = _dashes.Create(PrimitiveType.Cube, "Cloth");
            cloth.SetParent(_flag, worldPositionStays: false);
            cloth.localScale = new Vector3(5f, 0.4f, 0.6f);
            cloth.localPosition = new Vector3(2.5f, 0.7f, 0f);
            return _flag;
        }

        private void OnDestroy()
        {
            _dashes?.Destroy();
        }
    }
}
