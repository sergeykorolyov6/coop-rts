using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation
{
    // Drives a local match at Simulation.TicksPerSecond from Unity's frame loop.
    // One machine, no network; from M3 ticks come from the Game.Net lockstep instead.
    public sealed class SimRunner : MonoBehaviour
    {
        // Caps catch-up after a long frame (breakpoint, alt-tab) so the editor does not freeze.
        private const int MaxTicksPerFrame = 5;

        // Primary and secondary ore. Qualified: UnityEngine.Resources would shadow Game.Sim.Resources.
        private const int ResourceTypeCount = Game.Sim.Resources.Secondary + 1;

        [Header("Balance data (Assets/Data, generated from Docs/Design/*.csv)")]
        [SerializeField] private TextAsset? _unitsJson;
        [SerializeField] private TextAsset? _buildingsJson;
        [SerializeField] private TextAsset? _creepCampsJson;
        [SerializeField] private TextAsset? _rulesJson;
        [Tooltip("legion-ai.md: needed when the map has a Black Legion base.")]
        [SerializeField] private TextAsset? _legionJson;
        [SerializeField] private TextAsset? _wavesJson;

        [Header("Match")]
        [Tooltip("Assets/Data/maps/*.json")]
        [SerializeField] private TextAsset? _mapJson;
        [SerializeField] private int _seed = 1;
        [Tooltip("Human players, allied to each other; player N takes start slot N of the map.")]
        [SerializeField, Range(1, 4)] private int _playerCount = 2;
        [Tooltip("Owner of the creep camps (Grey Creatures), hostile to every human player.")]
        [SerializeField] private int _creepPlayerId = 10;
        [Tooltip("Plays the Black Legion when the map has its base (legion-ai.md), hostile to every human player.")]
        [SerializeField] private int _legionPlayerId = 9;

        [Header("Read-only")]
        [SerializeField] private int _tick;
        [SerializeField] private string _stateHash = "";

        private Simulation? _simulation;
        private float _accumulatedSeconds;

        public Simulation? Simulation => _simulation;

        // The sim clears its events every tick and a frame may run several ticks: views that show
        // events (notifications, minimap pings, "+5") hear them here, tick by tick, none lost.
        public event Action<IReadOnlyList<SimEvent>>? TickEvents;

        // How far the frame is between the last tick and the next one, 0..1: views interpolate
        // movement with it so units do not step at 20 Hz.
        public float InterpolationAlpha => Mathf.Clamp01(_accumulatedSeconds * Simulation.TicksPerSecond);

        private void Awake()
        {
            if (_unitsJson == null || _buildingsJson == null || _creepCampsJson == null || _rulesJson == null || _mapJson == null)
            {
                Debug.LogError("SimRunner: assign the balance json files and the map in the Inspector.", this);
                enabled = false;
                return;
            }

            var data = _legionJson != null && _wavesJson != null
                ? GameData.Parse(_unitsJson.text, _buildingsJson.text, _creepCampsJson.text, _rulesJson.text, _legionJson.text, _wavesJson.text)
                : GameData.Parse(_unitsJson.text, _buildingsJson.text, _creepCampsJson.text, _rulesJson.text);

            var humans = new List<int>();
            for (int id = 1; id <= _playerCount; id++)
                humans.Add(id);
            MapSetup map = MapLoader.Parse(_mapJson.text, humans);

            bool legion = map.LegionSlots.Count > 0;
            if (legion && data.Legion.Count == 0)
            {
                Debug.LogError("SimRunner: the map has a Black Legion base — assign legion.json and waves.json.", this);
                enabled = false;
                return;
            }
            var playerIds = new List<int>(humans) { _creepPlayerId };
            if (legion)
                playerIds.Add(_legionPlayerId);
            var relations = new List<RelationSetup>();
            foreach (int a in humans)
            {
                foreach (int b in humans)
                {
                    if (a < b)
                        relations.Add(new RelationSetup(a, b, Relation.Ally));
                }
                relations.Add(new RelationSetup(a, _creepPlayerId, Relation.Enemy));
                if (legion)
                    relations.Add(new RelationSetup(a, _legionPlayerId, Relation.Enemy));
            }

            _simulation = new Simulation(new MatchSetup(_seed, data, map, ResourceTypeCount, playerIds, relations,
                _creepPlayerId, legion ? _legionPlayerId : MatchSetup.NoPlayer));
            ShowState();
        }

        private void Update()
        {
            if (_simulation == null)
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
