using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // save-load.md UI: while a replay plays — a bar on top with the time and length, pause and
    // speed ×1…×8, whose eyes the fog is (Ctrl+F1–F4 switch), and at the end whether the replay came
    // to the recorded StateHash.
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class ReplayPanel : MonoBehaviour
    {
        private static readonly int[] Speeds = { 1, 2, 4, 8 };

        [SerializeField] private Hud? _hud;
        [SerializeField] private Color _activeColor = new Color(0.25f, 0.45f, 0.30f);

        private readonly List<Button> _speedButtons = new List<Button>();

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private Label? _time;
        private Button? _pause;
        private string _shown = "";

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void Start()
        {
            if (_hud?.Root == null || !_runner!.IsReplay)
                return;
            var bar = new VisualElement { name = "ReplayPanel" };
            Hud.Panelise(bar);
            bar.style.position = Position.Absolute;
            // Under the wallet line, so the two never overlap in a narrow window.
            bar.style.top = 36;
            bar.style.left = Length.Percent(50);
            bar.style.translate = new Translate(Length.Percent(-50), 0);
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;

            _time = new Label { pickingMode = PickingMode.Ignore };
            _time.style.marginRight = 8;
            _time.style.minWidth = 300;
            bar.Add(_time);
            _pause = AddButton(bar, "Пауза", () => _runner!.ReplaySpeed = _runner.ReplaySpeed == 0 ? 1 : 0);
            foreach (int speed in Speeds)
            {
                int s = speed;
                _speedButtons.Add(AddButton(bar, $"×{s}", () => _runner!.ReplaySpeed = s));
            }
            _hud.Root.Add(bar);
        }

        private Button AddButton(VisualElement bar, string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            Hud.StyleButton(button);
            button.style.height = 30;
            button.style.minWidth = 44;
            bar.Add(button);
            return button;
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_time == null || simulation == null)
                return;
            string whose = DisplayNames.PlayerName(simulation.State, _localPlayer!.PlayerId);
            string text = _runner.ReplayFinished
                ? (_runner.ReplayMatched ? "Реплей закончился — совпал с записью" : "Реплей разошёлся с записью — другая версия игры")
                : $"Реплей {DisplayNames.Clock(simulation.State.Tick)} / {DisplayNames.Clock(_runner.ReplayLengthTicks)} · глазами: {whose} (Ctrl+F1–F4)";
            if (text != _shown)
            {
                _shown = text;
                _time.text = text;
            }
            _pause!.text = _runner.ReplaySpeed == 0 ? "Дальше" : "Пауза";
            for (int i = 0; i < Speeds.Length; i++)
                _speedButtons[i].style.backgroundColor = _runner.ReplaySpeed == Speeds[i] ? new StyleColor(_activeColor) : new StyleColor(Hud.ButtonColor);
        }
    }
}
