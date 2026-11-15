using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using Game.Net;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // save-load.md UI: the main menu when no mission runs (missions, the network game, load,
    // replays, quit) and the pause menu on Esc in a mission (continue, save, load, to the main menu,
    // quit). Esc first cancels a placement; the menu opens on the next press.
    // A new mission or a load reloads the scene with GameSession set: every view starts clean.
    // network.md UI: "Сетевая игра" — create (port) or join (address), the lobby (places, factions,
    // the host's mission or save and "Начать"); in a network match Esc opens the menu without a pause.
    // save-load.md: "Загрузка… N %" while a save replays, the replays list, "Удалить" in both lists.
    //
    // Look: Assets/UI/Menu.uss and MenuDraw; this class builds the tree and names the classes.
    // The main menu is the title and the sections on the left, the open section's plate beside
    // them, all over the backdrop picture; the pause menu is one plate over the dimmed game.
    // A plate scrolls what does not fit, so every screen fits any window.
    [RequireComponent(typeof(SimRunner))]
    public sealed class GameMenu : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        [SerializeField] private PlacementController? _placement;
        [SerializeField] private int _maxSavesListed = 10;

        private enum Section { None, Missions, Network, Load, Replays }

        private const string Primary = "menu-button--primary";
        private const string Danger = "menu-button--danger";

        // The four playable factions' colours (missions.csv names), the row of marks over the title.
        private static readonly string[] EmblemColors = { "blue", "red", "brown", "yellow" };

        // The backdrop is drawn this much larger, so its slow drift never shows an edge.
        private const float BackdropScale = 1.05f;
        private const float BackdropBreath = 0.01f;
        // Pixels the backdrop moves against the pointer, and by itself.
        private static readonly Vector2 PointerShift = new Vector2(8f, 5f);
        private static readonly Vector2 IdleShift = new Vector2(6f, 4f);

        private SimRunner? _runner;
        private AbilityPanel? _abilities;
        private Guide? _guide;
        private EntityView? _entities;

        private bool _main;
        private VisualElement? _overlay;
        private VisualElement? _backdrop;
        private VisualElement? _nav;
        private readonly List<(Section Section, Button Button)> _navItems = new List<(Section, Button)>();
        private Label? _navMessage;
        private VisualElement? _plate;
        private Label? _title;
        private ScrollView? _body;
        private Label? _message;

        private Section _section;
        private string _shownTitle = "";
        private bool _plateShown;
        private bool _open;
        private bool _inLobby;
        private string _lobbyShown = "";
        private Label? _loadingLabel;
        private VisualElement? _loadingFill;
        private Vector2 _pointerShift;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _abilities = GetComponent<AbilityPanel>();
            _guide = GetComponent<Guide>();
            _entities = GetComponent<EntityView>();
        }

        private void OnEnable() => _runner!.NetNoticed += OnNetNotice;

        private void OnDisable() => _runner!.NetNoticed -= OnNetNotice;

        private void Start()
        {
            if (_hud?.Root == null)
                return;
            _main = _runner!.Simulation == null;
            _overlay = new VisualElement { name = "GameMenu" };
            _overlay.AddToClassList("menu");
            _overlay.AddToClassList(_main ? "menu--main" : "menu--pause");
            BuildPlate(_main ? BuildMain(_overlay) : _overlay);
            _hud.Root.Add(_overlay);

            if (_main)
                ShowMain();
            else if (_runner.IsLoading)
                ShowLoading();
            else
                Close();
        }

        private void Update()
        {
            if (_overlay == null)
                return;
            if (_inLobby)
                RefreshLobby();
            Keyboard? keyboard = Keyboard.current;
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (_main)
            {
                DriftBackdrop();
                // Esc shuts the open section; a lobby is left by its own button.
                if (escape && _plateShown && GameSession.Net == null)
                    ShowMain();
                return;
            }
            if (_runner!.Simulation == null)
                return;
            if (_loadingLabel != null)
            {
                if (_runner.IsLoading)
                {
                    int percent = Mathf.FloorToInt(_runner.LoadProgress * 100f);
                    _loadingLabel.text = $"Загрузка… {percent} %";
                    _loadingFill!.style.width = Length.Percent(percent);
                    return;
                }
                _loadingLabel = null;
                _loadingFill = null;
                Close();
            }
            if (!escape)
                return;
            if (_open)
                Close();
            else if ((_placement == null || !_placement.IsBusy) && (_abilities == null || !_abilities.IsBusy) && (_guide == null || !_guide.IsBusy))
                ShowPause();
        }

        // ---- The main menu ----

        // The backdrop, the title and the sections; returns the place of the plate beside them.
        private VisualElement BuildMain(VisualElement overlay)
        {
            _backdrop = Child(overlay, "menu-backdrop");
            // The left third goes dark under the title and the sections, the picture stays on the
            // right; top and bottom fade a little, so the picture has no hard edge.
            Color dark = new Color(0.016f, 0.024f, 0.04f);
            MenuDraw.Gradient(Child(overlay, "menu-shade"), horizontal: true,
                (0f, Faded(dark, 0.94f)), (0.26f, Faded(dark, 0.8f)), (0.6f, Faded(dark, 0f)), (1f, Faded(dark, 0f)));
            MenuDraw.Gradient(Child(overlay, "menu-shade"), horizontal: false,
                (0f, Faded(dark, 0.5f)), (0.16f, Faded(dark, 0f)), (0.8f, Faded(dark, 0f)), (1f, Faded(dark, 0.6f)));

            VisualElement layout = Child(overlay, "menu-layout");
            VisualElement side = Child(layout, "menu-side");
            VisualElement emblem = Child(side, "menu-emblem");
            foreach (string color in EmblemColors)
                Child(emblem, "menu-emblem__pip").style.backgroundColor = _entities != null ? _entities.PaletteColor(color) : Color.white;
            VisualElement logo = Child(side, "menu-logo");
            Text(logo, "COOP", "menu-logo__word");
            Text(logo, "RTS", "menu-logo__word").AddToClassList("menu-logo__word--accent");
            Text(side, "КООПЕРАТИВНАЯ СТРАТЕГИЯ · 1–4 ИГРОКА", "menu-tagline");
            Child(Child(side, "menu-rule"), "menu-rule__mark");

            _nav = Child(side, "menu-nav");
            AddSection(Section.Missions, "Миссии", ShowMissions);
            AddSection(Section.Network, "Сетевая игра", ShowNetwork);
            AddSection(Section.Load, "Загрузить", () => ShowLoad(back: null));
            AddSection(Section.Replays, "Реплеи", ShowReplays);
            AddSection(Section.None, "Выйти из игры", Quit);
            _navMessage = Text(side, "", "menu-message");
            Child(side, "menu-spacer");
            Text(side, $"V{Application.version}", "menu-footnote");
            return Child(layout, "menu-stage");
        }

        private static Color Faded(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        private void AddSection(Section section, string text, Action open)
        {
            var button = new Button(open) { text = text.ToUpperInvariant() };
            button.AddToClassList("menu-nav__item");
            MenuDraw.Frame(button);
            Text(button, $"{_navItems.Count + 1:00}", "menu-nav__index");
            _nav!.Add(button);
            _navItems.Add((section, button));
        }

        private void MarkSections()
        {
            foreach ((Section section, Button button) in _navItems)
                button.EnableInClassList("menu-nav__item--active", section != Section.None && section == _section);
            // In a lobby the player sits in a room: its own button leaves it, not a stray click on a section.
            _nav?.SetEnabled(GameSession.Net == null);
        }

        // Slow drift of the backdrop, and a little against the pointer: the picture is not a still.
        private void DriftBackdrop()
        {
            if (_backdrop == null)
                return;
            Vector2 wanted = Vector2.zero;
            Mouse? mouse = Mouse.current;
            if (mouse != null && Screen.width > 0 && Screen.height > 0)
            {
                Vector2 pointer = mouse.position.ReadValue();
                wanted = new Vector2(
                    -(Mathf.Clamp01(pointer.x / Screen.width) - 0.5f) * 2f * PointerShift.x,
                    (Mathf.Clamp01(pointer.y / Screen.height) - 0.5f) * 2f * PointerShift.y);
            }
            _pointerShift = Vector2.Lerp(_pointerShift, wanted, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
            float time = Time.unscaledTime;
            _backdrop.style.translate = new Translate(
                _pointerShift.x + Mathf.Sin(time * 0.11f) * IdleShift.x,
                _pointerShift.y + Mathf.Cos(time * 0.07f) * IdleShift.y);
            float scale = BackdropScale + BackdropBreath * Mathf.Sin(time * 0.05f);
            _backdrop.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }

        private void ShowMain()
        {
            _open = true;
            _overlay!.style.display = DisplayStyle.Flex;
            _overlay.BringToFront();
            _section = Section.None;
            _shownTitle = "";
            _plateShown = false;
            _plate!.style.display = DisplayStyle.None;
            MarkSections();
            if (_navMessage != null)
                _navMessage.style.display = DisplayStyle.None;
            if (GameSession.Error != null)
            {
                Say(GameSession.Error);
                GameSession.Error = null;
            }
        }

        // All the maps are here until there is a campaign.
        private void ShowMissions()
        {
            Begin("Миссии", Section.Missions, wide: true);
            AddMissions("Один игрок", m => m.Players == 1);
            AddMissions("Несколько игроков", m => m.Players > 1);
            AddNote("Миссию на несколько игроков можно пройти и одному: Ctrl+F1–F4 переключает игрока. Вместе с друзьями — «Сетевая игра».");
        }

        private void AddMissions(string caption, Predicate<MissionStats> take)
        {
            VisualElement? cards = null;
            foreach (MissionStats mission in Missions())
            {
                if (!take(mission))
                    continue;
                if (cards == null)
                {
                    AddCaption(caption);
                    cards = Child(_body!, "menu-cards");
                }
                cards.Add(MissionCard(mission));
            }
        }

        private Button MissionCard(MissionStats mission)
        {
            var card = new Button(() => StartMission(mission));
            card.AddToClassList("menu-card");
            MenuDraw.Frame(card);
            Text(card, mission.NameRu, "menu-card__title");
            VisualElement meta = Child(card, "menu-card__meta");
            for (int slot = 0; slot < mission.Players; slot++)
                Child(meta, "menu-pip").style.backgroundColor = SlotColor(mission, slot);
            Text(meta, $"{PlayersText(mission.Players)} · {GoalText(mission.Goal)}".ToUpperInvariant(), "menu-card__detail");
            return card;
        }

        private Color SlotColor(MissionStats? mission, int slot) =>
            _entities != null ? _entities.SlotColor(mission?.Colors, slot) : Color.white;

        private static string PlayersText(int count)
        {
            int ones = count % 10, tens = count % 100;
            string word = ones == 1 && tens != 11 ? "игрок" : ones >= 2 && ones <= 4 && (tens < 12 || tens > 14) ? "игрока" : "игроков";
            return $"{count} {word}";
        }

        private static string GoalText(MissionGoal goal)
        {
            switch (goal)
            {
                case MissionGoal.DestroyLegion: return "уничтожить Легион";
                case MissionGoal.KillBoss: return "победить вожака";
                default: return "свободная игра";
            }
        }

        // ---- The pause menu ----

        private void ShowPause()
        {
            if (_runner!.IsNetwork)
            {
                // network.md: one player cannot stop the others' game.
                Begin("Меню — игра идёт");
                AddButton("Продолжить (Esc)", Close, Primary);
                AddButton("Сохранить", Save);
                AddButton("Выйти в меню", ToMenu);
                AddButton("Закрыть игру", Quit);
                return;
            }
            if (_runner.IsReplay)
            {
                _runner.Paused = true;
                Begin("Реплей");
                AddButton("Продолжить (Esc)", Close, Primary);
                AddButton("Смотреть сначала", () => SimRunner.Reload());
                AddButton("Выйти в меню", ToMenu);
                AddButton("Закрыть игру", Quit);
                return;
            }
            _runner.Paused = true;
            Begin("Пауза");
            AddButton("Продолжить (Esc)", Close, Primary);
            AddButton("Сохранить", Save);
            AddButton("Загрузить", () => ShowLoad(back: ShowPause));
            AddButton("Выйти в меню", ToMenu);
            AddButton("Закрыть игру", Quit);
        }

        // The match is kept as a replay on the way out (save-load.md).
        private void ToMenu()
        {
            _runner!.RecordReplay();
            GameSession.ToMenu();
            SimRunner.Reload();
        }

        private void ShowLoading()
        {
            Begin("Загрузка");
            _loadingLabel = AddLabel("Загрузка… 0 %");
            _loadingLabel.AddToClassList("menu-text--centre");
            _loadingFill = Child(Child(_body!, "menu-progress"), "menu-progress__fill");
        }

        // ---- The network game ----

        private void ShowNetwork()
        {
            Begin("Сетевая игра", Section.Network);
            TextField name = Field("Ваше имя", PlayerPrefs.GetString(NamePref, Environment.UserName));
            _body!.Add(name);
            VisualElement where = Child(_body, "menu-row");
            TextField address = Field("Адрес хоста (для подключения)", PlayerPrefs.GetString(AddressPref, "127.0.0.1"));
            where.Add(address);
            TextField port = Field("Порт", PlayerPrefs.GetInt(PortPref, LockstepSettings.DefaultPort).ToString());
            port.AddToClassList("menu-field--short");
            where.Add(port);
            VisualElement actions = Child(_body, "menu-row");
            actions.Add(MenuButton("Создать игру", () => Host(name.value, port.value), Primary));
            actions.Add(MenuButton("Подключиться", () => Join(name.value, address.value, port.value)));
            AddNote("Через интернет: у хоста проброшен порт или вы в одной виртуальной сети (ZeroTier, Radmin VPN)");
        }

        private void Host(string name, string portText)
        {
            if (!TryPort(portText, out int port) || _runner!.Data == null || _runner.Catalog == null)
                return;
            Remember(name, null, port);
            MissionStats? first = null;
            foreach (MissionStats m in Missions())
            {
                if (m.Players > 1 && first == null)
                    first = m;
            }
            try
            {
                GameSession.Net = NetSession.Host(_runner.Data, _runner.Catalog, _runner.ContentHash,
                    (first ?? _runner.Catalog.Missions[0]).Id, port, PlayerName(name));
            }
            catch (SocketException e)
            {
                Say($"Не удалось открыть порт {port}: {e.Message}");
                return;
            }
            ShowLobby();
        }

        private void Join(string name, string address, string portText)
        {
            if (!TryPort(portText, out int port))
                return;
            address = address.Trim();
            if (address.Length == 0)
            {
                Say("Укажите адрес хоста");
                return;
            }
            Remember(name, address, port);
            GameSession.Net = NetSession.Join(address, port, PlayerName(name), _runner!.ContentHash);
            ShowLobby();
        }

        private void ShowLobby()
        {
            _inLobby = true;
            _lobbyShown = "";
            RefreshLobby();
        }

        // Rebuilt only when what it shows changes, so an open dropdown is not closed every frame.
        private void RefreshLobby()
        {
            NetSession? net = GameSession.Net;
            if (net == null)
            {
                _inLobby = false;
                return;
            }
            LockstepClient? client = net.Client;
            string key = net.Connecting ? "connecting" : net.Error ?? (client == null ? "" : $"{client.Slot}|{client.MissionId}|{client.LobbySaveTick}|{Describe(client.Lobby)}");
            if (key == _lobbyShown)
                return;
            _lobbyShown = key;

            Begin("Лобби", Section.Network);
            if (net.Error != null)
            {
                string error = net.Error;
                LeaveLobby();
                ShowNetwork();
                Say(error);
                return;
            }
            if (net.Connecting || client == null || client.Slot == 0)
            {
                AddLabel("Подключение…");
                AddButton("Отмена", () => { LeaveLobby(); ShowNetwork(); });
                return;
            }
            if (net.IsHost)
            {
                List<string> addresses = NetSession.LocalAddresses();
                string list = addresses.Count > 0 ? string.Join(", ", addresses) : "посмотрите в настройках сети";
                AddCaption("Вы хост · адрес для друзей");
                AddLabel($"{list}, порт {net.Port}").AddToClassList("menu-text--accent");
            }

            MissionStats? mission = _runner!.Catalog?.Find(client.MissionId);
            bool fromSave = client.LobbySaveTick >= 0;
            if (fromSave)
            {
                // save-load.md: the match goes on from the save, with its factions.
                AddLabel($"Продолжаем сохранение: {mission?.NameRu ?? client.MissionId} · {DisplayNames.Clock(client.LobbySaveTick)} в миссии");
                if (net.IsHost)
                    AddButton("Новая игра вместо сохранения", () => client.PickSave(Array.Empty<byte>()));
            }
            else if (net.IsHost && _runner.Catalog != null)
            {
                var missions = new List<MissionStats>(Missions());
                var names = missions.ConvertAll(m => $"{m.NameRu} ({m.Players} игр.)");
                int index = Math.Max(0, missions.FindIndex(m => m.Id == client.MissionId));
                var pick = new DropdownField("Миссия".ToUpperInvariant(), names, index);
                pick.RegisterValueChangedCallback(e => client.PickMission(missions[names.IndexOf(e.newValue)].Id));
                pick.AddToClassList("menu-field");
                _body!.Add(pick);
                AddButton("Продолжить сохранение…", () => ShowLobbySaves(client));
            }
            else
            {
                AddCaption("Миссия");
                AddLabel(mission?.NameRu ?? client.MissionId);
            }

            AddCaption("Игроки");
            List<FactionStats> factions = PlayerFactions();
            var factionNames = factions.ConvertAll(f => f.NameRu);
            foreach (LobbyEntry entry in client.Lobby)
            {
                string factionName = factions.Find(f => f.Id == entry.FactionId)?.NameRu ?? entry.FactionId;
                bool mine = entry.Slot == client.Slot;
                VisualElement row = Child(_body!, "menu-slot");
                Child(row, "menu-pip").style.backgroundColor = SlotColor(mission, entry.Slot - 1);
                Text(row, $"{entry.Slot}. {entry.Name}{(mine ? " (вы)" : "")}", "menu-slot__name");
                if (mine && !fromSave)
                {
                    var pick = new DropdownField(factionNames, Math.Max(0, factionNames.IndexOf(factionName)));
                    pick.RegisterValueChangedCallback(e => client.PickFaction(factions[factionNames.IndexOf(e.newValue)].Id));
                    pick.AddToClassList("menu-field");
                    pick.AddToClassList("menu-slot__pick");
                    row.Add(pick);
                }
                else
                {
                    Text(row, factionName, "menu-slot__faction");
                }
            }

            int need = mission?.Players ?? 0;
            if (client.Lobby.Count != need)
                AddNote($"Нужно игроков: {need}, сейчас {client.Lobby.Count}");
            Child(_body!, "menu-gap");
            if (net.IsHost)
                AddButton("Начать", () => client.RequestStart(UnityEngine.Random.Range(1, int.MaxValue)), Primary).SetEnabled(client.Lobby.Count == need);
            else
                AddNote("Ждём, пока хост начнёт");
            AddButton("Выйти из лобби", () => { LeaveLobby(); ShowNetwork(); });
        }

        // The host picks a save to go on from; the lobby comes back with it.
        private void ShowLobbySaves(LockstepClient client)
        {
            _inLobby = false;
            Begin("Продолжить сохранение", Section.Network, wide: true);
            ShowFiles(SaveStore.Kind.Save, "Сохранений пока нет", entry =>
            {
                SaveGame? save = ReadFile(entry);
                if (save == null)
                    return;
                client.PickSave(save.Write());
                ShowLobby();
            });
            AddButton("Назад", ShowLobby);
        }

        private void LeaveLobby()
        {
            _inLobby = false;
            GameSession.LeaveNetwork();
        }

        private void OnNetNotice(NetNotice notice)
        {
            if (_overlay == null)
                return;
            if (notice.Kind == NetNoticeKind.Rejected)
            {
                LeaveLobby();
                ShowNetwork();
                Say($"Хост отказал: {notice.Text}");
            }
            else if (notice.Kind == NetNoticeKind.HostLost)
            {
                if (_runner!.Simulation == null)
                {
                    LeaveLobby();
                    ShowNetwork();
                    Say("Хост отключился");
                    return;
                }
                Begin("Хост отключился");
                AddLabel("Игра окончена: без хоста ходы больше не приходят");
                AddButton("Выйти в меню", ToMenu);
            }
        }

        private static string Describe(IReadOnlyList<LobbyEntry> lobby)
        {
            var parts = new List<string>();
            foreach (LobbyEntry e in lobby)
                parts.Add($"{e.Slot}:{e.Name}:{e.FactionId}");
            return string.Join(";", parts);
        }

        private List<FactionStats> PlayerFactions()
        {
            var list = new List<FactionStats>();
            if (_runner!.Data != null)
            {
                foreach (FactionStats f in _runner.Data.Factions)
                {
                    if (f.Role == FactionRole.Player)
                        list.Add(f);
                }
            }
            return list;
        }

        private const string NamePref = "net_name";
        private const string AddressPref = "net_address";
        private const string PortPref = "net_port";

        private static void Remember(string name, string? address, int port)
        {
            PlayerPrefs.SetString(NamePref, name.Trim());
            if (address != null)
                PlayerPrefs.SetString(AddressPref, address);
            PlayerPrefs.SetInt(PortPref, port);
            PlayerPrefs.Save();
        }

        private static string PlayerName(string name) => name.Trim().Length > 0 ? name.Trim() : "Игрок";

        private bool TryPort(string text, out int port)
        {
            if (int.TryParse(text.Trim(), out port) && port > 0 && port < 65536)
                return true;
            Say("Порт — число от 1 до 65535");
            return false;
        }

        // ---- Saves and replays ----

        // back: where "Назад" leads; null in the main menu, where the sections are always beside.
        private void ShowLoad(Action? back)
        {
            Begin("Загрузить", _main ? Section.Load : Section.None, wide: true);
            ShowFiles(SaveStore.Kind.Save, "Сохранений пока нет", Load, () => ShowLoad(back));
            if (back != null)
                AddButton("Назад", back);
        }

        private void ShowReplays()
        {
            Begin("Реплеи", Section.Replays, wide: true);
            ShowFiles(SaveStore.Kind.Replay, "Реплеев пока нет: запись матча сохраняется сама, когда он кончается или вы выходите", Watch, ShowReplays);
        }

        // One row per file: open it, or delete it (the first click asks, the second deletes);
        // without refresh — no deleting (picking a save in the lobby).
        private void ShowFiles(SaveStore.Kind kind, string empty, Action<SaveStore.Entry> open, Action? refresh = null)
        {
            List<SaveStore.Entry> files = SaveStore.List(kind);
            if (files.Count == 0)
                AddNote(empty);
            for (int i = 0; i < files.Count && i < _maxSavesListed; i++)
            {
                SaveStore.Entry entry = files[i];
                VisualElement row = Child(_body!, "menu-file");
                Button openButton = MenuButton("", () => open(entry));
                openButton.AddToClassList("menu-file__open");
                Describe(entry, kind, out string title, out string detail);
                Text(openButton, title, "menu-file__title");
                if (detail.Length > 0)
                    Text(openButton, detail, "menu-file__detail");
                row.Add(openButton);
                if (refresh == null)
                    continue;
                Button? delete = null;
                delete = MenuButton("Удалить", () =>
                {
                    if (delete!.userData == null)
                    {
                        delete.userData = true;
                        delete.text = "Точно?";
                        delete.AddToClassList(Danger);
                        return;
                    }
                    try
                    {
                        SaveStore.Delete(entry);
                    }
                    catch (IOException e)
                    {
                        Say($"Не удалось удалить: {e.Message}");
                        return;
                    }
                    refresh();
                });
                delete.AddToClassList("menu-file__delete");
                row.Add(delete);
            }
        }

        // title: the mission and how far in; detail: when written and who played.
        private void Describe(SaveStore.Entry entry, SaveStore.Kind kind, out string title, out string detail)
        {
            try
            {
                SaveGame save = SaveGame.Read(File.ReadAllBytes(entry.Path));
                string name = _runner!.Catalog?.Find(save.MissionId)?.NameRu ?? save.MissionId;
                string time = kind == SaveStore.Kind.Save ? $"{DisplayNames.Clock(save.Tick)} в миссии" : $"длина {DisplayNames.Clock(save.Tick)}";
                string factions = save.Factions.Count > 0 ? " · " + FactionNames(save.Factions) : "";
                title = $"{name} · {time}";
                detail = $"{entry.Written:dd.MM.yyyy HH:mm}{factions}";
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                title = $"Повреждено: {Path.GetFileName(entry.Path)}";
                detail = "";
            }
        }

        private string FactionNames(IReadOnlyList<string> ids)
        {
            var names = new List<string>();
            foreach (string id in ids)
                names.Add(_runner!.Data != null && _runner.Data.TryGetFaction(id, out FactionStats? f) ? f!.NameRu : id);
            return string.Join(", ", names);
        }

        private void Watch(SaveStore.Entry entry)
        {
            SaveGame? replay = ReadFile(entry);
            if (replay == null)
                return;
            GameSession.StartReplay(replay);
            SimRunner.Reload();
        }

        // A readable file of a mission this game has, or null and the reason said.
        private SaveGame? ReadFile(SaveStore.Entry entry)
        {
            try
            {
                SaveGame save = SaveGame.Read(File.ReadAllBytes(entry.Path));
                if (_runner!.Catalog?.Find(save.MissionId) != null)
                    return save;
                Say("Миссия из файла не найдена");
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                Say("Не удалось открыть: файл повреждён или от другой версии игры");
            }
            return null;
        }

        private void StartMission(MissionStats mission)
        {
            GameSession.StartMission(mission.Id, UnityEngine.Random.Range(1, int.MaxValue));
            SimRunner.Reload();
        }

        private void Save()
        {
            byte[]? bytes = _runner!.WriteSave();
            if (bytes == null)
                return;
            try
            {
                SaveStore.Write(SaveStore.Kind.Save, bytes);
                Say("Сохранено");
            }
            catch (IOException e)
            {
                Say($"Не удалось сохранить: {e.Message}");
            }
        }

        private void Load(SaveStore.Entry entry)
        {
            SaveGame? save = ReadFile(entry);
            if (save == null)
                return;
            // Loading a save leaves a network game: it goes on alone (Ctrl+F1–F4 for the others).
            GameSession.LeaveNetwork();
            GameSession.Load(save);
            SimRunner.Reload();
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Close()
        {
            _open = false;
            _overlay!.style.display = DisplayStyle.None;
            if (_runner!.Simulation != null)
                _runner.Paused = false;
        }

        // ---- The plate and what fills it ----

        private void BuildPlate(VisualElement parent)
        {
            _plate = Child(parent, "menu-plate");
            MenuDraw.Frame(_plate);
            VisualElement head = Child(_plate, "menu-plate__head");
            _title = Text(head, "", "menu-plate__title");
            Child(head, "menu-plate__rule");
            _body = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _body.AddToClassList("menu-scroll");
            _plate.Add(_body);
            _message = Text(_plate, "", "menu-message");
        }

        // An empty plate with this title; section — which of the main menu's sections it belongs to.
        private void Begin(string title, Section section = Section.None, bool wide = false)
        {
            _open = true;
            _overlay!.style.display = DisplayStyle.Flex;
            // Over whatever the game's panels have added to the root since.
            _overlay.BringToFront();
            _section = section;
            MarkSections();
            _plateShown = true;
            _plate!.style.display = DisplayStyle.Flex;
            _plate.EnableInClassList("menu-plate--wide", wide);
            _body!.Clear();
            _message!.style.display = DisplayStyle.None;
            if (_navMessage != null)
                _navMessage.style.display = DisplayStyle.None;
            _loadingLabel = null;
            _loadingFill = null;
            if (title == _shownTitle)
                return;
            // Another screen (a lobby that only refreshes keeps its place): from the top, sliding in —
            // the class goes off a frame later and the transition of Menu.uss plays.
            _shownTitle = title;
            _title!.text = title.ToUpperInvariant();
            _body.scrollOffset = Vector2.zero;
            VisualElement plate = _plate;
            plate.AddToClassList("menu-plate--entering");
            plate.schedule.Execute(() => plate.RemoveFromClassList("menu-plate--entering")).StartingIn(30);
        }

        private Button AddButton(string text, Action onClick, string? kind = null)
        {
            Button button = MenuButton(text, onClick, kind);
            _body!.Add(button);
            return button;
        }

        private static Button MenuButton(string text, Action onClick, string? kind = null)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("menu-button");
            if (kind != null)
                button.AddToClassList(kind);
            MenuDraw.Frame(button);
            return button;
        }

        private static TextField Field(string label, string value)
        {
            var field = new TextField(label.ToUpperInvariant()) { value = value };
            field.AddToClassList("menu-field");
            return field;
        }

        private Label AddLabel(string text) => Text(_body!, text, "menu-text");

        private void AddNote(string text) => Text(_body!, text, "menu-note");

        private void AddCaption(string text) => Text(_body!, text.ToUpperInvariant(), "menu-caption");

        // Under the plate's content, or under the sections when no plate is open.
        private void Say(string text)
        {
            Label? label = _plateShown ? _message : _navMessage;
            if (label == null)
                return;
            label.text = text;
            label.style.display = DisplayStyle.Flex;
        }

        // Layout and text never take the pointer: a click goes to the button they lie on.
        private static VisualElement Child(VisualElement parent, string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            parent.Add(element);
            return element;
        }

        private static Label Text(VisualElement parent, string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private IEnumerable<MissionStats> Missions()
        {
            if (_runner!.Catalog == null)
                yield break;
            // The Legion's mission first: it is the main one.
            var list = new List<MissionStats>(_runner.Catalog.Missions);
            list.Sort((a, b) => b.HasLegion.CompareTo(a.HasLegion));
            foreach (MissionStats m in list)
                yield return m;
        }
    }
}
