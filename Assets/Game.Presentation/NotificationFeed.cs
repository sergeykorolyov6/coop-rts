using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Short messages from sim events for the local player, under the wallet: "place taken",
    // "drill attacked", "site frozen", "camp cleared"... Each fades after a few seconds; the same
    // message again while shown counts up (×3) instead of stacking.
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class NotificationFeed : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        [SerializeField] private float _seconds = 4f;
        [SerializeField] private int _maxShown = 6;
        [SerializeField] private Color _infoColor = new Color(0.90f, 0.92f, 0.88f);
        [SerializeField] private Color _warningColor = new Color(0.98f, 0.82f, 0.35f);
        [SerializeField] private Color _alarmColor = new Color(1.00f, 0.45f, 0.40f);

        private readonly List<Entry> _entries = new List<Entry>();

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private VisualElement? _column;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void OnEnable() => _runner!.TickEvents += OnTickEvents;

        private void OnDisable() => _runner!.TickEvents -= OnTickEvents;

        private void Start()
        {
            if (_hud?.Root == null)
                return;
            _column = new VisualElement { name = "Notifications", pickingMode = PickingMode.Ignore };
            _column.style.position = Position.Absolute;
            _column.style.left = 12;
            _column.style.top = 44;
            _column.style.alignItems = Align.FlexStart;
            _hud.Root.Add(_column);
        }

        private void Update()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - _entries[i].Shown;
                if (age >= _seconds)
                {
                    _entries[i].Label.RemoveFromHierarchy();
                    _entries.RemoveAt(i);
                }
                else if (age > _seconds - 1f)
                {
                    _entries[i].Label.style.opacity = _seconds - age;
                }
            }
        }

        private void OnTickEvents(IReadOnlyList<SimEvent> events)
        {
            Simulation? simulation = _runner!.Simulation;
            if (simulation == null)
                return;
            SimState state = simulation.State;
            int me = _localPlayer!.PlayerId;

            foreach (SimEvent e in events)
            {
                switch (e)
                {
                    case CommandRejected rejected when rejected.PlayerId == me:
                        Show(DisplayNames.Reject(rejected.Reason), _warningColor);
                        break;
                    case MiningStopped stopped when stopped.PlayerId == me:
                        Show("Клетку руды заняли — рабочий остановился", _warningColor);
                        break;
                    case ConstructionFrozen frozen when frozen.OwnerId == me:
                        Show($"Стройка заморожена: {BuildingName(state, frozen.BuildingId)}", _warningColor);
                        break;
                    case ConstructionCompleted completed when completed.OwnerId == me:
                        Show($"Построено: {BuildingName(state, completed.BuildingId)}", _infoColor);
                        break;
                    case UnitProduced produced when produced.OwnerId == me:
                        Unit? unit = state.FindUnit(produced.UnitId);
                        Show($"Готов: {(unit != null ? DisplayNames.Unit(unit.Stats.Id) : "юнит")}", _infoColor);
                        break;
                    // drill.md: the alarm goes to the owner and the allies, so the Defender knows where to go.
                    case BuildingAttacked attacked when state.Relations.IsAlly(me, attacked.OwnerId):
                        string whose = attacked.OwnerId == me ? "" : $" союзника {attacked.OwnerId}";
                        Show($"Атакован{whose}: {BuildingName(state, attacked.BuildingId)}", _alarmColor);
                        break;
                    case EntityDestroyed destroyed when destroyed.IsBuilding && destroyed.OwnerId == me:
                        Show("Здание разрушено", _alarmColor);
                        break;
                    case CampAlerted alerted when Discovered(state, me, alerted.CampIndex):
                        Show("Лагерь крипов встревожен", _warningColor);
                        break;
                    // creep-camp.md: every player hears it.
                    case CampCleared _:
                        Show("Лагерь крипов зачищен — трофей ждёт рабочих", _infoColor);
                        break;
                }
            }
        }

        private void Show(string text, Color color)
        {
            if (_column == null)
                return;
            foreach (Entry entry in _entries)
            {
                if (entry.Text != text)
                    continue;
                entry.Count++;
                entry.Shown = Time.unscaledTime;
                entry.Label.text = $"{text}  ×{entry.Count}";
                entry.Label.style.opacity = 1f;
                return;
            }

            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            Hud.Panelise(label);
            label.style.color = color;
            label.style.marginBottom = 4;
            _column.Add(label);
            _entries.Add(new Entry(text, label, Time.unscaledTime));
            if (_entries.Count > _maxShown)
            {
                _entries[0].Label.RemoveFromHierarchy();
                _entries.RemoveAt(0);
            }
        }

        private static string BuildingName(SimState state, int buildingId)
        {
            Building? building = state.FindBuilding(buildingId);
            return building != null ? DisplayNames.Building(building.Stats.Id).ToLowerInvariant() : "здание";
        }

        private static bool Discovered(SimState state, int playerId, int campIndex)
        {
            CreepCamp camp = state.CreepCamps[campIndex];
            return state.Fog.IsDiscovered(playerId, camp.CenterX, camp.CenterY);
        }

        private sealed class Entry
        {
            public Entry(string text, Label label, float shown)
            {
                Text = text;
                Label = label;
                Shown = shown;
            }

            public string Text { get; }
            public Label Label { get; }
            public float Shown { get; set; }
            public int Count { get; set; } = 1;
        }
    }
}
