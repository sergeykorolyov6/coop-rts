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
    // save-load.md UI: the main menu when no mission runs (a button per mission from missions.json,
    // load, quit) and the pause menu on Esc in a mission (continue, save, load, to the main menu,
    // quit). Esc first cancels a placement; the menu opens on the next press.
    // A new mission or a load reloads the scene with GameSession set: every view starts clean.
    // network.md UI: "Сетевая игра" — create (port) or join (address), the lobby (places, factions,
    // the host's mission and "Начать"); in a network match Esc opens the menu without a pause.
    [RequireComponent(typeof(SimRunner))]
    public sealed class GameMenu : MonoBehaviour
    {
        [SerializeField] private Hud? _hud;
        [SerializeField] private PlacementController? _placement;
        [SerializeField] private int _maxSavesListed = 10;

        private SimRunner? _runner;
        private VisualElement? _overlay;
        private VisualElement? _column;
        private Label? _message;
        private bool _open;
        private bool _inLobby;
        private string _lobbyShown = "";

        private AbilityPanel? _abilities;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _abilities = GetComponent<AbilityPanel>();
        }

        private void OnEnable() => _runner!.NetNoticed += OnNetNotice;

        private void OnDisable() => _runner!.NetNoticed -= OnNetNotice;

        private void Start()
        {
            if (_hud?.Root == null)
                return;
            _overlay = new VisualElement { name = "GameMenu" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = _overlay.style.right = _overlay.style.top = _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _column = new VisualElement();
            Hud.Panelise(_column);
            _column.style.paddingLeft = _column.style.paddingRight = 24;
            _column.style.paddingTop = _column.style.paddingBottom = 16;
            _column.style.minWidth = 380;
            _overlay.Add(_column);
            _hud.Root.Add(_overlay);

            if (_runner!.Simulation == null)
                ShowMain();
            else
                Close();
        }

        private void Update()
        {
            if (_overlay != null && _inLobby)
                RefreshLobby();
            if (_overlay == null || _runner!.Simulation == null)
                return;
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;
            if (_open)
                Close();
            else if ((_placement == null || !_placement.IsBusy) && (_abilities == null || !_abilities.IsBusy))
                ShowPause();
        }

        private void ShowMain()
        {
            Begin("CoopRTS");
            foreach (MissionStats mission in Missions())
            {
                MissionStats m = mission;
                string players = m.Players == 1 ? "1 игрок" : $"{m.Players} игрока (F1/F2 — переключить)";
                AddButton($"{m.NameRu}\n{players}", () => StartMission(m));
            }
            AddButton("Сетевая игра", ShowNetwork);
            AddButton("Загрузить", () => ShowLoad(back: ShowMain));
            AddButton("Выйти из игры", Quit);
            if (GameSession.Error != null)
            {
                Say(GameSession.Error);
                GameSession.Error = null;
            }
        }

        private void ShowPause()
        {
            if (_runner!.IsNetwork)
            {
                // network.md: one player cannot stop the others' game.
                Begin("Меню — игра идёт");
                AddButton("Продолжить (Esc)", Close);
                AddButton("Выйти в меню", ToMenu);
                AddButton("Закрыть игру", Quit);
                return;
            }
            _runner.Paused = true;
            Begin("Пауза");
            AddButton("Продолжить (Esc)", Close);
            AddButton("Сохранить", Save);
            AddButton("Загрузить", () => ShowLoad(back: ShowPause));
            AddButton("Выйти в меню", ToMenu);
            AddButton("Закрыть игру", Quit);
        }

        private static void ToMenu()
        {
            GameSession.ToMenu();
            SimRunner.Reload();
        }

        private void ShowNetwork()
        {
            Begin("Сетевая игра");
            TextField name = AddField("Ваше имя", PlayerPrefs.GetString(NamePref, Environment.UserName));
            TextField address = AddField("Адрес хоста (для подключения)", PlayerPrefs.GetString(AddressPref, "127.0.0.1"));
            TextField port = AddField("Порт", PlayerPrefs.GetInt(PortPref, LockstepSettings.DefaultPort).ToString());
            AddButton("Создать игру", () => Host(name.value, port.value));
            AddButton("Подключиться", () => Join(name.value, address.value, port.value));
            AddButton("Назад", ShowMain);
            Say("Через интернет: у хоста проброшен порт или вы в одной виртуальной сети (ZeroTier, Radmin VPN)");
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
            string key = net.Connecting ? "connecting" : net.Error ?? (client == null ? "" : $"{client.Slot}|{client.MissionId}|{Describe(client.Lobby)}");
            if (key == _lobbyShown)
                return;
            _lobbyShown = key;

            Begin("Лобби");
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
                Say("Подключение…");
                AddButton("Отмена", () => { LeaveLobby(); ShowNetwork(); });
                return;
            }
            if (net.IsHost)
            {
                List<string> addresses = NetSession.LocalAddresses();
                string list = addresses.Count > 0 ? string.Join(", ", addresses) : "посмотрите в настройках сети";
                AddLabel($"Вы хост. Адрес для друзей: {list}, порт {net.Port}");
            }

            MissionStats? mission = _runner!.Catalog?.Find(client.MissionId);
            if (net.IsHost && _runner.Catalog != null)
            {
                var missions = new List<MissionStats>(Missions());
                var names = missions.ConvertAll(m => $"{m.NameRu} ({m.Players} игр.)");
                int index = Math.Max(0, missions.FindIndex(m => m.Id == client.MissionId));
                var pick = new DropdownField("Миссия", names, index);
                pick.RegisterValueChangedCallback(e => client.PickMission(missions[names.IndexOf(e.newValue)].Id));
                AddDropdown(pick);
            }
            else
            {
                AddLabel($"Миссия: {mission?.NameRu ?? client.MissionId}");
            }

            List<FactionStats> factions = PlayerFactions();
            var factionNames = factions.ConvertAll(f => f.NameRu);
            foreach (LobbyEntry entry in client.Lobby)
            {
                string factionName = factions.Find(f => f.Id == entry.FactionId)?.NameRu ?? entry.FactionId;
                if (entry.Slot == client.Slot)
                {
                    var pick = new DropdownField($"{entry.Slot}. {entry.Name} (вы)", factionNames, Math.Max(0, factionNames.IndexOf(factionName)));
                    pick.RegisterValueChangedCallback(e => client.PickFaction(factions[factionNames.IndexOf(e.newValue)].Id));
                    AddDropdown(pick);
                }
                else
                {
                    AddLabel($"{entry.Slot}. {entry.Name} — {factionName}");
                }
            }

            int need = mission?.Players ?? 0;
            if (client.Lobby.Count != need)
                AddLabel($"Нужно игроков: {need}, сейчас {client.Lobby.Count}");
            if (net.IsHost)
            {
                var start = new Button(() => client.RequestStart(UnityEngine.Random.Range(1, int.MaxValue))) { text = "Начать" };
                StyleMenuButton(start);
                start.SetEnabled(client.Lobby.Count == need);
                _column!.Add(start);
            }
            else
            {
                AddLabel("Ждём, пока хост начнёт");
            }
            AddButton("Выйти", () => { LeaveLobby(); ShowMain(); });
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
                Say("Игра окончена: без хоста ходы больше не приходят");
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

        private void ShowLoad(Action back)
        {
            Begin("Загрузить");
            List<SaveStore.Entry> saves = SaveStore.List();
            if (saves.Count == 0)
                Say("Сохранений пока нет");
            for (int i = 0; i < saves.Count && i < _maxSavesListed; i++)
            {
                SaveStore.Entry entry = saves[i];
                AddButton(Describe(entry), () => Load(entry));
            }
            AddButton("Назад", back);
        }

        private string Describe(SaveStore.Entry entry)
        {
            try
            {
                SaveGame save = SaveGame.Read(File.ReadAllBytes(entry.Path));
                string name = _runner!.Catalog?.Find(save.MissionId)?.NameRu ?? save.MissionId;
                return $"{name} · {DisplayNames.Clock(save.Tick)} в миссии\n{entry.Written:dd.MM.yyyy HH:mm}";
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                return $"Повреждено: {Path.GetFileName(entry.Path)}";
            }
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
                SaveStore.Write(bytes);
                Say("Сохранено");
            }
            catch (IOException e)
            {
                Say($"Не удалось сохранить: {e.Message}");
            }
        }

        private void Load(SaveStore.Entry entry)
        {
            try
            {
                SaveGame save = SaveGame.Read(File.ReadAllBytes(entry.Path));
                if (_runner!.Catalog?.Find(save.MissionId) == null)
                {
                    Say("Миссия из сохранения не найдена");
                    return;
                }
                Say("Загрузка…");
                GameSession.Load(save);
                SimRunner.Reload();
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                Say("Не удалось загрузить: файл повреждён или от другой версии игры");
            }
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

        private void Begin(string title)
        {
            _open = true;
            _overlay!.style.display = DisplayStyle.Flex;
            _column!.Clear();
            var header = new Label(title);
            header.style.fontSize = 26;
            header.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.style.marginBottom = 12;
            _column.Add(header);
            _message = new Label { pickingMode = PickingMode.Ignore };
            _message.style.unityTextAlign = TextAnchor.MiddleCenter;
            _message.style.marginTop = 8;
            _message.style.display = DisplayStyle.None;
        }

        private void AddButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            StyleMenuButton(button);
            _column!.Add(button);
            KeepMessageLast();
        }

        private static void StyleMenuButton(Button button)
        {
            Hud.StyleButton(button);
            button.style.minHeight = 40;
            button.style.marginTop = button.style.marginBottom = 4;
            button.style.fontSize = 16;
        }

        // The default popup field is light grey with light text in this theme: dark like the buttons.
        private void AddDropdown(DropdownField field)
        {
            field.style.marginTop = field.style.marginBottom = 4;
            VisualElement? input = field.Q(className: "unity-base-popup-field__input");
            if (input != null)
            {
                input.style.backgroundColor = Hud.ButtonColor;
                input.style.color = Color.white;
            }
            VisualElement? text = field.Q(className: "unity-base-popup-field__text");
            if (text != null)
                text.style.color = Color.white;
            _column!.Add(field);
            KeepMessageLast();
        }

        private TextField AddField(string label, string value)
        {
            var field = new TextField(label) { value = value };
            field.style.marginTop = field.style.marginBottom = 4;
            _column!.Add(field);
            KeepMessageLast();
            return field;
        }

        private void AddLabel(string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = label.style.marginBottom = 4;
            _column!.Add(label);
            KeepMessageLast();
        }

        private void KeepMessageLast()
        {
            if (_message != null && _message.parent != null)
            {
                _message.RemoveFromHierarchy();
                _column!.Add(_message);
            }
        }

        private void Say(string text)
        {
            if (_message == null)
                return;
            if (_message.parent == null)
                _column!.Add(_message);
            _message.text = text;
            _message.style.display = DisplayStyle.Flex;
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
