using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // factions.md UI: the selection card — what the selection is and does. One kind selected — its
    // icon, name, health, description (units.csv "description"), numbers and the hero's level;
    // several kinds — a tile per kind with how many (a click keeps only that kind).
    // Someone else's unit picked for a look (ally, Legion, creep) shows the same, plus whose it is.
    [RequireComponent(typeof(SimRunner), typeof(SelectionController), typeof(LocalPlayer))]
    public sealed class UnitInfo : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;

        private SimRunner? _runner;
        private SelectionController? _selection;
        private LocalPlayer? _localPlayer;
        private EntityView? _entities;
        private VisualElement? _card;
        private string _shown = "";
        private HudKit.Bar? _hp;
        private Label? _hpText;
        private Label? _extra;
        private Unit? _single;
        private int _shownHp = -1;
        private string _shownExtra = "";

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _selection = GetComponent<SelectionController>();
            _localPlayer = GetComponent<LocalPlayer>();
            _entities = GetComponent<EntityView>();
        }

        private void Start()
        {
            if (_hud?.BottomBar == null || _runner!.Simulation == null)
                return;
            _card = HudKit.Plate("UnitInfo", "hud-card", "hud-plate--main");
            _card.style.display = DisplayStyle.None;
            _hud.BottomBar.Insert(0, _card);
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_card == null || simulation == null)
                return;
            SimState state = simulation.State;
            var counts = new List<(UnitStats Stats, List<int> Ids)>();
            Unit? first = null;
            foreach (int id in _selection!.Selected)
            {
                Unit? u = state.FindUnit(id);
                if (u == null || u.IsDead)
                    continue;
                first ??= u;
                int i = counts.FindIndex(c => c.Stats == u.Stats);
                if (i < 0)
                    counts.Add((u.Stats, new List<int> { u.Id }));
                else
                    counts[i].Ids.Add(u.Id);
            }
            if (counts.Count == 0)
            {
                Unit? other = state.FindUnit(_selection.InspectedUnitId);
                first = other != null && !other.IsDead ? other : null;
            }

            string key = first == null ? "" : counts.Count > 1 ? "many|" + Describe(counts) : $"one|{first.Id}|{(counts.Count > 0 ? counts[0].Ids.Count : 1)}|{_localPlayer!.PlayerId}";
            if (key != _shown)
            {
                _shown = key;
                _card.Clear();
                _single = null;
                _card.style.display = key.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (first != null && counts.Count > 1)
                    BuildMany(counts);
                else if (first != null)
                    BuildSingle(state, first, counts.Count > 0 ? counts[0].Ids.Count : 1);
            }
            if (_single != null)
                UpdateSingle(state, _single);
        }

        private static string Describe(List<(UnitStats Stats, List<int> Ids)> counts)
        {
            var parts = new List<string>();
            foreach ((UnitStats stats, List<int> ids) in counts)
                parts.Add($"{stats.Id}:{ids.Count}");
            return string.Join(",", parts);
        }

        private void BuildMany(List<(UnitStats Stats, List<int> Ids)> counts)
        {
            int total = 0;
            foreach ((_, List<int> ids) in counts)
                total += ids.Count;
            HudKit.Text(_card!, $"ВЫДЕЛЕНО: {total}", "hud-caption");
            VisualElement tiles = HudKit.Child(_card!, "hud-tiles");
            foreach ((UnitStats stats, List<int> ids) in counts)
            {
                List<int> only = new List<int>(ids);
                var tile = new Button(() => _selection!.SelectOnly(only));
                tile.AddToClassList("hud-tile");
                MenuDraw.Frame(tile);
                HudKit.IconView(tile, stats.Id, stats.NameRu, "hud-tile__icon");
                HudKit.Text(tile, ids.Count.ToString(), "hud-tile__count");
                UnitStats tipStats = stats;
                _hud!.AttachTip(tile, () => new HudTip { Title = $"{tipStats.NameRu} × {only.Count}", Body = "Щелчок — оставить в выделении только их." });
                tiles.Add(tile);
            }
        }

        // One kind: icon, name, whose (when not ours), health of a lone unit, description, numbers, hero level.
        private void BuildSingle(SimState state, Unit unit, int count)
        {
            UnitStats s = unit.Stats;
            VisualElement head = HudKit.Child(_card!, "hud-card__head");
            HudKit.IconView(head, s.Id, s.NameRu, "hud-card__icon").style.unityBackgroundImageTintColor = Tint(state, unit.OwnerId);
            VisualElement names = HudKit.Child(head, "hud-card__names");
            HudKit.Text(names, count > 1 ? $"{s.NameRu} × {count}" : s.NameRu, "hud-title");
            string whose = DisplayNames.Whose(state, _localPlayer!.PlayerId, unit.OwnerId);
            Label owner = HudKit.Text(names, whose.Length > 0 ? whose.ToUpperInvariant() : FactionOf(state, unit.OwnerId), "hud-card__whose");
            owner.EnableInClassList("hud-alarm", state.Relations.IsEnemy(_localPlayer.PlayerId, unit.OwnerId));
            owner.EnableInClassList("hud-dim", !state.Relations.IsEnemy(_localPlayer.PlayerId, unit.OwnerId));
            // Folded: the name and the health stay, the words and the numbers go.
            _hud!.AddFold(_card!, head, "card");

            if (count == 1)
            {
                _single = unit;
                _shownHp = -1;
                _hp = HudKit.AddBar(_card!, "hud-bar--hp");
                VisualElement hpRow = HudKit.Child(_card!, "hud-card__hp");
                HudKit.Text(hpRow, "ПРОЧНОСТЬ", "hud-caption");
                _hpText = HudKit.Text(hpRow, "", "hud-card__hp-text");
            }
            Label description = HudKit.Text(_card!, s.Description, "hud-dim");
            description.AddToClassList("hud-card__desc");
            description.AddToClassList("hud-fold");
            VisualElement stats = HudKit.Child(_card!, "hud-stats");
            stats.AddToClassList("hud-fold");
            foreach ((string caption, string value) in EntityTips.UnitNumbers(s))
                HudKit.Stat(stats, caption, value);
            _extra = HudKit.Text(_card!, "", "hud-card__extra");
            _shownExtra = "";
            _extra.style.display = DisplayStyle.None;
        }

        private void UpdateSingle(SimState state, Unit unit)
        {
            if (unit.Hp != _shownHp)
            {
                _shownHp = unit.Hp;
                float share = unit.Hp / (float)Mathf.Max(1, unit.MaxHp);
                _hp!.Set(share);
                _hp.SetColor(HudKit.HealthColor(share));
                _hpText!.text = $"{unit.Hp} / {unit.MaxHp}";
            }
            string extra = "";
            if (unit.IsHero)
            {
                Player owner = state.GetPlayer(unit.OwnerId);
                string path = state.HeroPath(unit.OwnerId)?.NameRu ?? "путь не выбран";
                extra = $"Уровень {owner.HeroLevel} · опыт {owner.HeroXp} · {path}";
            }
            if (extra != _shownExtra)
            {
                _shownExtra = extra;
                _extra!.text = extra;
                _extra.style.display = extra.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // The icon in the owner's colour, a little lighter so a dark colour still reads on the plate.
        private StyleColor Tint(SimState state, int ownerId)
        {
            Color color = _entities != null ? _entities.OwnerColor(state, ownerId) : Color.white;
            return new StyleColor(Color.Lerp(color, Color.white, 0.35f));
        }

        private static string FactionOf(SimState state, int ownerId) => (state.GetPlayer(ownerId).Faction?.NameRu ?? "").ToUpperInvariant();
    }
}
