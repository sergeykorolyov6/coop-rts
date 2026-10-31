using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Presentation;

// Drives a local match at Simulation.TicksPerSecond from Unity's frame loop.
// M0: one machine, no network; from M3 ticks come from the Game.Net lockstep instead.
public sealed class SimRunner : MonoBehaviour
{
    // Caps catch-up after a long frame (breakpoint, alt-tab) so the editor does not freeze.
    private const int MaxTicksPerFrame = 5;

    [Header("Match (M0 debug setup; real values come from mission data in M1)")]
    [SerializeField] private int _seed = 1;
    [SerializeField, Min(1)] private int _mapWidth = 64;
    [SerializeField, Min(1)] private int _mapHeight = 64;
    [SerializeField, Min(1)] private int _playerCount = 2;
    [SerializeField, Min(1)] private int _resourceTypeCount = 2;

    [Header("Read-only")]
    [SerializeField] private int _tick;
    [SerializeField] private string _stateHash = "";

    private Simulation? _simulation;
    private float _accumulatedSeconds;

    public Simulation? Simulation => _simulation;

    private void Awake()
    {
        var playerIds = new List<int>();
        var relations = new List<RelationSetup>();
        for (int a = 1; a <= _playerCount; a++)
        {
            playerIds.Add(a);
            for (int b = a + 1; b <= _playerCount; b++)
                relations.Add(new RelationSetup(a, b, Relation.Ally));
        }

        _simulation = new Simulation(new MatchSetup(_seed, _mapWidth, _mapHeight, _resourceTypeCount, playerIds, relations));
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
        int width = _simulation?.State.Grid.Width ?? _mapWidth;
        int height = _simulation?.State.Grid.Height ?? _mapHeight;

        Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
        for (int x = 0; x <= width; x++)
            Gizmos.DrawLine(new Vector3(x, 0f, 0f), new Vector3(x, 0f, height));
        for (int z = 0; z <= height; z++)
            Gizmos.DrawLine(new Vector3(0f, 0f, z), new Vector3(width, 0f, z));
    }
}
