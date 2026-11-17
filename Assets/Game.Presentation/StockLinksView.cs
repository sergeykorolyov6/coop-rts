using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // factory-auto.md UI: for the selected building with a stock (factory, cave, ammo turret), own
    // or an ally's — chevrons over the belts and bridge ends beside it: green on those that feed it,
    // amber on those its surplus goes on to, each pointing the way the resource moves there.
    // Which is which the sim says (PlacementPreview.StockLinks).
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class StockLinksView : MonoBehaviour
    {
        [SerializeField] private Color _feedColor = new Color(0.40f, 0.95f, 0.50f);
        [SerializeField] private Color _passColor = new Color(1.00f, 0.75f, 0.25f);
        [SerializeField, Range(0.3f, 1f)] private float _size = 0.6f;

        private readonly List<(Building Link, Direction Flow, bool Feeds)> _links = new List<(Building, Direction, bool)>();

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private GroundMarks? _marks;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
            _marks = new GroundMarks(transform, "StockLinks");
        }

        private void LateUpdate()
        {
            _marks!.Begin();
            SimState? state = _runner!.Simulation?.State;
            Building? b = state?.FindBuilding(_selection!.SelectedBuildingId);
            if (b != null && !b.IsDead && b.Stock != null && state!.Relations.IsAlly(_localPlayer!.PlayerId, b.OwnerId))
            {
                PlacementPreview.StockLinks(state, b, _links);
                foreach ((Building link, Direction flow, bool feeds) in _links)
                {
                    // Over the belt's own arrow, clear of the resource riding on it.
                    float lift = PrimitiveModels.BuildingHeight(link.Stats) + 0.45f;
                    _marks.Chevron(link.CellX, link.CellY, flow, feeds ? _feedColor : _passColor, _size, lift);
                }
            }
            _marks.End();
        }

        private void OnDestroy() => _marks?.Destroy();
    }
}
