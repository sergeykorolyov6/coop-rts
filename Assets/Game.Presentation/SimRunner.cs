using System;
using System.Collections.Generic;
using Game.Net;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // Drives a match at Simulation.TicksPerSecond from Unity's frame loop: alone, or in a network
    // game (network.md) only as far as the lockstep allows. Every command of the local player goes
    // through Submit. Polls the network session in the lobby too, and starts the match the host started.
    // save-load.md: a save loads by replaying its commands a slice per frame ("Загрузка… N %"); a replay
    // plays a whole recorded match at the chosen speed without orders; a match's recording is kept as
    // a replay when it ends or the player leaves it.
    // The match is the mission GameSession names (mission.md): humans 1..N allied, the Legion 9,
    // creeps 10, neutral buildings 11 (MissionLauncher). Without one the main menu shows (GameMenu).
    public sealed class SimRunner : MonoBehaviour
    {
        // Caps catch-up after a long frame (breakpoint, alt-tab) so the editor does not freeze.
        private const int MaxTicksPerFrame = 5;

        [Header("Balance data (Assets/Data, generated from Docs/Design/*.csv)")]
        [SerializeField] private TextAsset? _unitsJson;
        [SerializeField] private TextAsset? _buildingsJson;
        [SerializeField] private TextAsset? _creepCampsJson;
        [SerializeField] private TextAsset? _rulesJson;
        [Tooltip("legion-ai.md: needed when the map has a Black Legion base.")]
        [SerializeField] private TextAsset? _legionJson;
        [SerializeField] private TextAsset? _wavesJson;
        [Tooltip("Assets/Data/factions.json: what each faction starts with and builds.")]
        [SerializeField] private TextAsset? _factionsJson;
        [Tooltip("Assets/Data/abilities.json: Blink, Overdrive and the other abilities (factions.md).")]
        [SerializeField] private TextAsset? _abilitiesJson;
        [Tooltip("Assets/Data/hero_levels.json: Yellow Star's hero levels (factions.md).")]
        [SerializeField] private TextAsset? _heroLevelsJson;
        [Tooltip("Assets/Data/hero_paths.json: the hero's paths — Warrior, Mage, Supporter.")]
        [SerializeField] private TextAsset? _heroPathsJson;

        [Header("Missions (mission.md, save-load.md)")]
        [Tooltip("Assets/Data/missions.json")]
        [SerializeField] private TextAsset? _missionsJson;
        [Tooltip("Assets/Data/maps/*.json; a mission finds its map by the asset name.")]
        [SerializeField] private TextAsset[] _maps = System.Array.Empty<TextAsset>();

        [Header("Read-only")]
        [SerializeField] private string _missionId = "";
        [SerializeField] private int _tick;
        [SerializeField] private string _stateHash = "";

        private Simulation? _simulation;
        private float _accumulatedSeconds;
        private SaveGame? _loading;
        private SaveGame? _replay;
        private bool _replayRecorded;

        // Slice of loading per frame: the frame stays alive and the progress moves.
        private const float LoadBudgetSeconds = 0.03f;
        private const int LoadSliceTicks = 100;

        // save-load.md: shorter matches are not worth a replay.
        private const int MinReplayTicks = 30 * Simulation.TicksPerSecond;

        // Null while the main menu is shown (GameSession has no mission).
        public Simulation? Simulation => _simulation;

        public GameData? Data { get; private set; }
        public MissionCatalog? Catalog { get; private set; }
        public MissionStats? Mission { get; private set; }
        public int Seed { get; private set; }

        // The pause menu stops the clock (one machine; with the network the game cannot pause alone).
        public bool Paused { get; set; }

        // network.md: this match runs over the network.
        public bool IsNetwork => _simulation != null && GameSession.IsNetworkMatch;

        // A network match stands waiting for another player's turn.
        public bool NetWaiting => IsNetwork && !IsLoading && GameSession.Net!.Client!.Waiting;

        // save-load.md: a save is being replayed up to its tick; nothing else runs meanwhile.
        public bool IsLoading => _loading != null;
        public float LoadProgress => _loading == null || _simulation == null || _loading.Tick == 0 ? 1f : _simulation.State.Tick / (float)_loading.Tick;

        // Watching a recorded match: orders do nothing, ReplaySpeed sets the pace (0 — stands).
        public bool IsReplay => _replay != null;
        public int ReplayLengthTicks => _replay?.Tick ?? 0;
        public int ReplaySpeed { get; set; } = 1;
        public bool ReplayFinished { get; private set; }

        // At the end: the replay came to the recorded StateHash (the same version of the game).
        public bool ReplayMatched { get; private set; }

        // Lobby and match messages for the player (PlayerLeft, Desync, HostLost, Rejected).
        public event Action<NetNotice>? NetNoticed;

        // network.md, Fail cases: one hash of every text a match depends on, compared in the lobby.
        public ulong ContentHash { get; private set; }

        // The sim clears its events every tick and a frame may run several ticks: views that show
        // events (notifications, minimap pings, "+5") hear them here, tick by tick, none lost.
        public event Action<IReadOnlyList<SimEvent>>? TickEvents;

        // How far the frame is between the last tick and the next one, 0..1: views interpolate
        // movement with it so units do not step at 20 Hz.
        public float InterpolationAlpha => Mathf.Clamp01(_accumulatedSeconds * Simulation.TicksPerSecond);

        public static readonly CommandCodec Codec = GameCommands.CreateCodec();

        private void Awake()
        {
            if (_unitsJson == null || _buildingsJson == null || _creepCampsJson == null || _rulesJson == null || _missionsJson == null || _factionsJson == null)
            {
                Debug.LogError("SimRunner: assign the balance json files, factions.json and missions.json in the Inspector.", this);
                enabled = false;
                return;
            }

            Data = GameData.Parse(new GameDataSources
            {
                Units = _unitsJson.text,
                Buildings = _buildingsJson.text,
                CreepCamps = _creepCampsJson.text,
                Rules = _rulesJson.text,
                Legion = _legionJson != null ? _legionJson.text : "[]",
                Waves = _wavesJson != null ? _wavesJson.text : "[]",
                Factions = _factionsJson.text,
                Abilities = _abilitiesJson != null ? _abilitiesJson.text : "[]",
                HeroLevels = _heroLevelsJson != null ? _heroLevelsJson.text : "[]",
                HeroPaths = _heroPathsJson != null ? _heroPathsJson.text : "[]",
            });
            Catalog = MissionCatalog.Parse(_missionsJson.text);
            DisplayNames.Data = Data;
            ContentHash = Game.Net.ContentHash.Of(ContentTexts());

            if (GameSession.MissionId == null)
                return;
            MissionStats? mission = Catalog.Find(GameSession.MissionId);
            TextAsset? map = mission != null ? FindMap(mission.Map) : null;
            if (mission == null || map == null)
            {
                FailToMenu($"Миссия «{GameSession.MissionId}» или её карта не найдены");
                return;
            }

            MatchSetup setup = MissionLauncher.CreateSetup(Data, mission, map.text, GameSession.Seed, GameSession.Factions);
            SaveGame? save = GameSession.PendingSave ?? GameSession.Replay;
            try
            {
                // A save starts at tick 0 too and is played up to its tick in Update, a slice a frame.
                _simulation = save != null ? save.BeginReplay(setup, Codec) : new Simulation(setup);
                _replay = GameSession.Replay;
                _loading = _replay == null ? GameSession.PendingSave : null;
            }
            catch (Exception e) when (e is System.IO.InvalidDataException || e is System.IO.EndOfStreamException || e is ArgumentException)
            {
                Debug.LogWarning($"SimRunner: the save did not load: {e.Message}", this);
                FailToMenu("Не удалось загрузить сохранение: оно от другой версии игры или повреждено");
                return;
            }
            GameSession.SaveConsumed();
            Mission = mission;
            Seed = GameSession.Seed;
            _missionId = mission.Id;
            ShowState();
        }

        // Starts the scene over with the given session (GameMenu): every view rebuilds for the new match.
        public static void Reload() =>
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);

        // save-load.md: a network game too — every machine's log has everyone's commands.
        public byte[]? WriteSave() =>
            _simulation == null || Mission == null || IsLoading || IsReplay ? null : SaveGame.Capture(_simulation, Mission.Id, Seed, Codec).Write();

        // save-load.md: keeps the match as a replay once (at its end, or when the player leaves it).
        public void RecordReplay()
        {
            if (_replayRecorded || _simulation == null || Mission == null || IsLoading || IsReplay || _simulation.State.Tick < MinReplayTicks)
                return;
            _replayRecorded = true;
            try
            {
                SaveStore.Write(SaveStore.Kind.Replay, SaveGame.Capture(_simulation, Mission.Id, Seed, Codec).Write());
            }
            catch (System.IO.IOException e)
            {
                Debug.LogWarning($"SimRunner: the replay was not written: {e.Message}", this);
            }
        }

        // The local player's command: into the sim, or to the host in a network game.
        public void Submit(ICommand command)
        {
            if (_simulation == null || IsLoading || IsReplay)
                return;
            if (IsNetwork)
                GameSession.Net!.Client!.Submit(command);
            else
                _simulation.Enqueue(command);
        }

        private IEnumerable<string> ContentTexts()
        {
            foreach (TextAsset? asset in new[] { _unitsJson, _buildingsJson, _creepCampsJson, _rulesJson, _legionJson, _wavesJson,
                         _factionsJson, _abilitiesJson, _heroLevelsJson, _heroPathsJson, _missionsJson })
                yield return asset != null ? asset.text : "";
            // By name: the hash must not depend on the order of the list in the Inspector.
            var maps = new List<TextAsset>();
            foreach (TextAsset asset in _maps)
            {
                if (asset != null)
                    maps.Add(asset);
            }
            maps.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (TextAsset asset in maps)
                yield return asset.name + "\n" + asset.text;
        }

        private void FailToMenu(string message)
        {
            GameSession.ToMenu();
            GameSession.Error = message;
        }

        private TextAsset? FindMap(string name)
        {
            foreach (TextAsset asset in _maps)
            {
                if (asset != null && asset.name == name)
                    return asset;
            }
            return null;
        }

        private void Update()
        {
            if (GameSession.Net != null)
            {
                PollNetwork();
                if (_simulation == null || !enabled)
                    return;
            }
            if (_simulation == null)
                return;
            if (_loading != null)
            {
                LoadSlice();
                return;
            }
            if (_replay != null)
            {
                RunReplay();
                return;
            }
            if (IsNetwork)
            {
                RunNetwork();
                return;
            }
            if (Paused)
                return;

            const float secondsPerTick = 1f / Simulation.TicksPerSecond;
            _accumulatedSeconds += Time.deltaTime;

            int ticks = 0;
            while (_accumulatedSeconds >= secondsPerTick && ticks < MaxTicksPerFrame)
            {
                _simulation.Tick();
                AfterTick();
                _accumulatedSeconds -= secondsPerTick;
                ticks++;
            }
            if (ticks == MaxTicksPerFrame)
                _accumulatedSeconds = 0f;

            ShowState();
        }

        private void PollNetwork()
        {
            NetSession net = GameSession.Net!;
#if !UNITY_EDITOR
            // A minimised machine that stops ticking stops everyone's lockstep. Not in the editor:
            // there it is a project setting.
            Application.runInBackground = true;
#endif
            net.Poll(Time.unscaledDeltaTime);
            LockstepClient? client = net.Client;
            if (client != null)
            {
                foreach (NetNotice notice in client.TakeNotices())
                {
                    if (notice.Kind == NetNoticeKind.Desync)
                        Debug.LogError($"Network: StateHash differs at tick {notice.Tick} (network.md).", this);
                    NetNoticed?.Invoke(notice);
                }
            }
            // The host started: every machine reloads into the match (the lobby is the menu scene).
            if (_simulation == null && client?.Start != null && !client.Disconnected)
            {
                GameSession.StartNetworkMatch(client.Start);
                enabled = false;
                Reload();
            }
        }

        // network.md: real time, but never past the turns the host has sent; a machine that fell
        // behind (more turns waiting than the delay) catches up, up to MaxTicksPerFrame.
        private void RunNetwork()
        {
            LockstepClient client = GameSession.Net!.Client!;
            const float secondsPerTick = 1f / Simulation.TicksPerSecond;
            _accumulatedSeconds += Time.deltaTime;
            int ticks = 0;
            while (ticks < MaxTicksPerFrame && (_accumulatedSeconds >= secondsPerTick || client.TurnsAhead > LockstepSettings.DelayTurns + 1))
            {
                if (!client.TryStep(_simulation!))
                {
                    // Waiting must not bank time for a burst afterwards.
                    _accumulatedSeconds = Mathf.Min(_accumulatedSeconds, secondsPerTick);
                    break;
                }
                AfterTick();
                _accumulatedSeconds = Mathf.Max(0f, _accumulatedSeconds - secondsPerTick);
                ticks++;
            }
            ShowState();
        }

        private void OnApplicationQuit()
        {
            RecordReplay();
            GameSession.LeaveNetwork();
        }

        private void AfterTick()
        {
            IReadOnlyList<SimEvent> events = _simulation!.Events;
            if (events.Count == 0)
                return;
            TickEvents?.Invoke(events);
            foreach (SimEvent e in events)
            {
                if (e is MissionEnded)
                    RecordReplay();
            }
        }

        // save-load.md: replay a slice of the save per frame; at its tick it must match.
        private void LoadSlice()
        {
            SaveGame save = _loading!;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (_simulation!.State.Tick < save.Tick && clock.Elapsed.TotalSeconds < LoadBudgetSeconds)
                _simulation.FastForward(Math.Min(save.Tick, _simulation.State.Tick + LoadSliceTicks));
            ShowState();
            if (_simulation.State.Tick < save.Tick)
                return;
            if (!save.Matches(_simulation))
            {
                Debug.LogWarning("SimRunner: the save replays to another StateHash (rules or data changed).", this);
                FailToMenu("Не удалось загрузить сохранение: оно от другой версии игры или повреждено");
                enabled = false;
                Reload();
                return;
            }
            _loading = null;
            _accumulatedSeconds = 0f;
        }

        // save-load.md: the recorded match at ReplaySpeed; stops at its last tick and checks it.
        private void RunReplay()
        {
            if (ReplayFinished || Paused || ReplaySpeed <= 0)
                return;
            const float secondsPerTick = 1f / Simulation.TicksPerSecond;
            _accumulatedSeconds += Time.deltaTime * ReplaySpeed;
            int ticks = 0;
            while (_accumulatedSeconds >= secondsPerTick && ticks < MaxTicksPerFrame * ReplaySpeed)
            {
                _simulation!.Tick();
                AfterTick();
                _accumulatedSeconds -= secondsPerTick;
                ticks++;
                if (_simulation.State.Tick >= _replay!.Tick)
                {
                    ReplayFinished = true;
                    ReplayMatched = _replay.Matches(_simulation);
                    _accumulatedSeconds = 0f;
                    break;
                }
            }
            if (ticks == MaxTicksPerFrame * ReplaySpeed)
                _accumulatedSeconds = 0f;
            ShowState();
        }

        private void ShowState()
        {
            if (_simulation == null)
                return;
            _tick = _simulation.State.Tick;
            _stateHash = _simulation.StateHash.ToString("X16");
        }

        // One Unity unit per cell on the XZ plane, origin at cell (0, 0).
        private void OnDrawGizmos()
        {
            if (_simulation == null)
                return;
            int width = _simulation.State.Grid.Width;
            int height = _simulation.State.Grid.Height;

            Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
            for (int x = 0; x <= width; x++)
                Gizmos.DrawLine(new Vector3(x, 0f, 0f), new Vector3(x, 0f, height));
            for (int z = 0; z <= height; z++)
                Gizmos.DrawLine(new Vector3(0f, 0f, z), new Vector3(width, 0f, z));
        }
    }
}
