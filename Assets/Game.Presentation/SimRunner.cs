using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // Drives a local match at Simulation.TicksPerSecond from Unity's frame loop.
    // One machine, no network; from M3 ticks come from the Game.Net lockstep instead.
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

        [Header("Missions (mission.md, save-load.md)")]
        [Tooltip("Assets/Data/missions.json")]
        [SerializeField] private TextAsset? _missionsJson;
        [Tooltip("Assets/Data/maps/*.json; a mission finds its map by the asset name.")]
        [SerializeField] private TextAsset[] _maps = System.Array.Empty<TextAsset>();
        [Tooltip("Seed of a new mission; a save keeps its own.")]
        [SerializeField] private int _seed = 1;

        [Header("Read-only")]
        [SerializeField] private string _missionId = "";
        [SerializeField] private int _tick;
        [SerializeField] private string _stateHash = "";

        private Simulation? _simulation;
        private float _accumulatedSeconds;

        // Null while the main menu is shown (GameSession has no mission).
        public Simulation? Simulation => _simulation;

        public GameData? Data { get; private set; }
        public MissionCatalog? Catalog { get; private set; }
        public MissionStats? Mission { get; private set; }
        public int Seed { get; private set; }

        // The pause menu stops the clock (one machine; with the network the game cannot pause alone).
        public bool Paused { get; set; }

        // The sim clears its events every tick and a frame may run several ticks: views that show
        // events (notifications, minimap pings, "+5") hear them here, tick by tick, none lost.
        public event Action<IReadOnlyList<SimEvent>>? TickEvents;

        // How far the frame is between the last tick and the next one, 0..1: views interpolate
        // movement with it so units do not step at 20 Hz.
        public float InterpolationAlpha => Mathf.Clamp01(_accumulatedSeconds * Simulation.TicksPerSecond);

        public static readonly CommandCodec Codec = GameCommands.CreateCodec();

        private void Awake()
        {
            if (_unitsJson == null || _buildingsJson == null || _creepCampsJson == null || _rulesJson == null || _missionsJson == null)
            {
                Debug.LogError("SimRunner: assign the balance json files and missions.json in the Inspector.", this);
                enabled = false;
                return;
            }

            Data = _legionJson != null && _wavesJson != null
                ? GameData.Parse(_unitsJson.text, _buildingsJson.text, _creepCampsJson.text, _rulesJson.text, _legionJson.text, _wavesJson.text)
                : GameData.Parse(_unitsJson.text, _buildingsJson.text, _creepCampsJson.text, _rulesJson.text);
            Catalog = MissionCatalog.Parse(_missionsJson.text);

            if (GameSession.MissionId == null)
                return;
            MissionStats? mission = Catalog.Find(GameSession.MissionId);
            TextAsset? map = mission != null ? FindMap(mission.Map) : null;
            if (mission == null || map == null)
            {
                FailToMenu($"Миссия «{GameSession.MissionId}» или её карта не найдены");
                return;
            }

            MatchSetup setup = MissionLauncher.CreateSetup(Data, mission, map.text, GameSession.Seed);
            SaveGame? save = GameSession.PendingSave;
            try
            {
                _simulation = save != null ? save.Restore(setup, Codec) : new Simulation(setup);
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

        public byte[]? WriteSave() =>
            _simulation == null || Mission == null ? null : SaveGame.Capture(_simulation, Mission.Id, Seed, Codec).Write();

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
            if (_simulation == null || Paused)
                return;

            const float secondsPerTick = 1f / Simulation.TicksPerSecond;
            _accumulatedSeconds += Time.deltaTime;

            int ticks = 0;
            while (_accumulatedSeconds >= secondsPerTick && ticks < MaxTicksPerFrame)
            {
                _simulation.Tick();
                if (_simulation.Events.Count > 0)
                    TickEvents?.Invoke(_simulation.Events);
                _accumulatedSeconds -= secondsPerTick;
                ticks++;
            }
            if (ticks == MaxTicksPerFrame)
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
