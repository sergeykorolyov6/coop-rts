using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    // Which player this machine controls. In a network game — the place the host gave (network.md);
    // alone one machine can play every human player in turn: F1–F4 switch, so co-op is testable solo.
    [RequireComponent(typeof(SimRunner))]
    public sealed class LocalPlayer : MonoBehaviour
    {
        [SerializeField, Range(1, 4)] private int _playerId = 1;
        [Tooltip("F1–F4 switch the controlled player. Debug until there is a network.")]
        [SerializeField] private bool _hotSeatKeys = true;

        private SimRunner? _runner;

        public int PlayerId => _playerId;

        public event Action? Changed;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            if (GameSession.IsNetworkMatch)
            {
                _playerId = GameSession.Net!.Client!.Slot;
                _hotSeatKeys = false;
            }
        }

        private void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            if (!_hotSeatKeys || keyboard == null || _runner == null || _runner.Simulation == null)
                return;

            if (keyboard.f1Key.wasPressedThisFrame) Switch(1);
            else if (keyboard.f2Key.wasPressedThisFrame) Switch(2);
            else if (keyboard.f3Key.wasPressedThisFrame) Switch(3);
            else if (keyboard.f4Key.wasPressedThisFrame) Switch(4);
        }

        private void Switch(int playerId)
        {
            if (playerId == _playerId || !_runner!.Simulation!.State.HasPlayer(playerId))
                return;
            _playerId = playerId;
            Changed?.Invoke();
        }
    }
}
