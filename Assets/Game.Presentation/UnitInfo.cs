using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // factions.md UI: what the selection is and does. One kind selected — its name, description
    // (units.csv "description"), numbers and abilities; several kinds — how many of each.
    // Someone else's unit picked for a look (ally, Legion, creep) shows the same, plus whose it is.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class UnitInfo : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private Label? _label;
        private string _shown = "";

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void Start()
        {
            if (_hud?.BottomBar == null || _runner!.Simulation == null)
                return;
            _label = new Label { name = "UnitInfo", pickingMode = PickingMode.Ignore };
            Hud.Panelise(_label);
            _label.style.maxWidth = 340;
            _label.style.whiteSpace = WhiteSpace.Normal;
            _label.style.marginRight = 8;
            _label.style.display = DisplayStyle.None;
            _hud.BottomBar.Insert(0, _label);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_label == null || simulation == null)
                return;
            string text = Describe(simulation.State);
            if (text == _shown)
                return;
            _shown = text;
            _label.text = text;
            _label.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // "HP 220 · броня 1 · урон 12 (здания 12) · дальность 0,5 · скорость 1,9 кл./с"
        public static string StatsLine(UnitStats s)
        {
            string line = $"Прочность {s.Hp} · броня {s.Armor}";
            if (s.Damage > 0 || s.DamageVsBuildings > 0)
            {
                line += $" · урон {s.Damage}";
                if (s.DamageVsBuildings != s.Damage)
                    line += $" (по зданиям {s.DamageVsBuildings})";
                line += $" · дальность {Cells(s.AttackRangeMilli)}";
            }
            return line + $" · скорость {Cells(s.MoveSpeedMilliPerTick * Simulation.TicksPerSecond)} кл./с";
        }

        private string Describe(SimState state)
        {
            var counts = new List<(UnitStats Stats, int Count)>();
            Unit? first = null;
            foreach (int id in _selection!.Selected)
            {
                Unit? u = state.FindUnit(id);
                if (u == null || u.IsDead)
                    continue;
                first ??= u;
                int i = counts.FindIndex(c => c.Stats == u.Stats);
                if (i < 0)
                    counts.Add((u.Stats, 1));
                else
                    counts[i] = (u.Stats, counts[i].Count + 1);
            }
            if (counts.Count == 0)
            {
                Unit? other = state.FindUnit(_selection.InspectedUnitId);
                return other != null && !other.IsDead ? Single(state, other, 1) : "";
            }
            if (counts.Count > 1)
            {
                var parts = new List<string>();
                foreach ((UnitStats stats, int count) in counts)
                    parts.Add($"{stats.NameRu} × {count}");
                return "Выделено: " + string.Join(", ", parts);
            }

            return Single(state, first!, counts[0].Count);
        }

        // One kind: name, whose (when not ours), live HP of a lone unit, description, numbers, hero level.
        private string Single(SimState state, Unit first, int count)
        {
            UnitStats s = first.Stats;
            string head = count > 1 ? $"{s.NameRu} × {count}" : $"{s.NameRu} · {first.Hp} / {first.MaxHp}";
            string whose = DisplayNames.Whose(state, _localPlayer!.PlayerId, first.OwnerId);
            if (whose.Length > 0)
                head += $"\n{whose}";
            string text = $"{head}\n{s.Description}\n{StatsLine(s)}";
            if (first.IsHero)
            {
                Player owner = state.GetPlayer(first.OwnerId);
                string path = state.HeroPath(first.OwnerId)?.NameRu ?? "путь не выбран";
                text += $"\nУровень {owner.HeroLevel}, опыт {owner.HeroXp} · {path}";
            }
            return text;
        }

        private static string Cells(int milli) => DisplayNames.Number(milli / (double)Game.Sim.Grid.MilliPerCell, "0.#");
    }
}
