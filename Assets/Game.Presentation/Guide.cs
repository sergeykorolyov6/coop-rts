using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Help for someone who did not make the game (решение Claude, 15.11.2026, open-questions.md):
    // - F1 opens "Как играть": the loop of the game in six steps and the controls (hotkeys.md);
    //   alone it pauses the match, in a network game the match goes on;
    // - "Первые шаги" under the objective: a checklist of the economy's first moves that ticks
    //   itself off from the sim's state (drill, belt, ore in the headquarters, factory, army)
    //   and goes away when all is done or the player hides it (remembered in PlayerPrefs).
    // Only reads the sim.
    [RequireComponent(typeof(SimRunner), typeof(LocalPlayer))]
    public sealed class Guide : MonoBehaviour
    {
        private const string HiddenPref = "guide_first_steps_hidden";

        [SerializeField] private Hud? _hud;

        private static readonly string[] Steps =
        {
            "Поставьте бур на руду: B — бур, щелчок по месторождению; строитель пойдёт сам.",
            "Протяните ленту (L) от бура к штабу: зажмите ЛКМ и ведите.",
            "Дождитесь, пока руда дойдёт до штаба — она попадёт в кошелёк.",
            "Постройте завод (F) и подведите к нему ленту: он платит из своего склада.",
            "Наймите армию в заводе. Волны Легиона — таймер выше.",
        };

        private static readonly (string Key, string What)[] Keys =
        {
            ("ЛКМ, рамка", "выделить; Shift — добавить"),
            ("ПКМ", "идти с боем, атаковать, строить, чинить, копать"),
            ("Ctrl + ПКМ", "идти, не отвлекаясь"),
            ("Shift + приказ", "в очередь"),
            ("B L F … (меню стройки)", "строить подряд; ПКМ — закончить"),
            ("W A S D, края", "камера; Q / E — поворот, колесо — ближе"),
            ("Space", "камера к штабу"),
            ("Tab · G", "строитель · герой (дважды — камера)"),
            ("F2", "вся армия"),
            ("1–0", "группа; Ctrl+цифра — запомнить, Shift — добавить"),
            ("Z X C V", "способности выделенных"),
            ("R · Del", "повернуть здание · снести выделенное"),
            ("Del без здания", "снос рамкой; лента поверх своей — разворот"),
            ("U", "восстановить разрушенное"),
            ("Alt + ЛКМ", "метка для союзника"),
            ("Esc", "отменить; иначе меню"),
        };

        private SimRunner? _runner;
        private LocalPlayer? _localPlayer;
        private VisualElement? _help;
        private VisualElement? _steps;
        private readonly VisualElement?[] _stepRows = new VisualElement?[Steps.Length];
        private int _shownDone = -1;
        private bool _pausedByHelp;
        private int _closedFrame = -1;

        // The help is open, or closed this very frame by Esc: the pause menu waits.
        public bool IsBusy => (_help != null && _help.style.display == DisplayStyle.Flex) || _closedFrame == Time.frameCount;

        private void Awake()
        {
            _runner = GetComponent<SimRunner>();
            _localPlayer = GetComponent<LocalPlayer>();
        }

        private void Start()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_hud?.Root == null || simulation == null)
                return;
            BuildHelp();
            if (simulation.State.Goal != MissionGoal.None && !_runner.IsReplay && PlayerPrefs.GetInt(HiddenPref, 0) == 0)
                BuildSteps();
        }

        private void Update()
        {
            Simulation? simulation = _runner!.Simulation;
            if (_help == null || simulation == null)
                return;
            Keyboard? keyboard = Keyboard.current;
            bool ctrl = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed);
            bool open = _help.style.display == DisplayStyle.Flex;
            // Ctrl+F1 switches the player (LocalPlayer).
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame && !ctrl)
                SetHelp(!open);
            else if (open && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetHelp(false);
            if (_steps != null && simulation.State.Outcome != MissionOutcome.InProgress)
            {
                _steps.RemoveFromHierarchy();
                _steps = null;
            }
            if (_steps != null)
                UpdateSteps(simulation.State);
        }

        private void SetHelp(bool open)
        {
            if (!open)
                _closedFrame = Time.frameCount;
            _help!.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
                _help.BringToFront();
            // network.md: one player cannot stop the others' game.
            if (open && !_runner!.IsNetwork && !_runner.Paused)
            {
                _runner.Paused = true;
                _pausedByHelp = true;
            }
            else if (!open && _pausedByHelp)
            {
                _runner!.Paused = false;
                _pausedByHelp = false;
            }
        }

        private void BuildHelp()
        {
            _help = new VisualElement { name = "Help" };
            _help.style.display = DisplayStyle.None;
            _help.RegisterCallback<PointerDownEvent>(e =>
            {
                // A click outside the plate closes it.
                if (e.target == _help)
                    SetHelp(false);
            });
            VisualElement plate = HudKit.Plate("HelpPlate", "hud-help");
            _help.Add(plate);
            HudKit.Text(plate, "КАК ИГРАТЬ", "hud-help__title");
            HudKit.Text(plate, "Кооперативная стратегия: у каждого игрока своя база, свой цвет и свой кошелёк, туман войны общий. "
                + "Вместе выстоять против волн Чёрного Легиона и разрушить его базу.", "hud-dim");
            VisualElement columns = HudKit.Child(plate, "hud-help__columns");

            VisualElement loop = HudKit.Child(columns, "hud-help__column");
            HudKit.Text(loop, "ЭКОНОМИКА И БОЙ", "hud-caption");
            string[] loopSteps =
            {
                "Строитель — это вы: строит, копает руду руками, носит трофеи. Погиб — вернётся у штаба.",
                "Бур на месторождении добывает сам. Оранжевые клетки — руда, голубые — кристаллы (второй ресурс).",
                "Лента везёт ресурс в штаб — только там он попадает в кошелёк. Склад копит и сводит ленты вместе.",
                "Завод делает юнитов и платит из своего склада: подведите к нему ленту от бура или склада.",
                "Легион присылает волны — таймер справа вверху. Турели и армия держат оборону у буров и штаба.",
                "Разведчик ищет базу Легиона. Победа — когда разрушены все его штабы и заводы.",
            };
            for (int i = 0; i < loopSteps.Length; i++)
            {
                VisualElement row = HudKit.Child(loop, "hud-help__step");
                HudKit.Text(row, (i + 1).ToString(), "hud-help__number");
                HudKit.Text(row, loopSteps[i], "hud-help__step-text");
            }
            HudKit.Text(loop, "Наведите мышь на кнопку — подсказка скажет, что это, сколько стоит и зачем.", "hud-dim");

            VisualElement controls = HudKit.Child(columns, "hud-help__column");
            HudKit.Text(controls, "УПРАВЛЕНИЕ", "hud-caption");
            foreach ((string key, string what) in Keys)
            {
                VisualElement row = HudKit.Child(controls, "hud-key-row");
                HudKit.Text(row, key, "hud-key");
                HudKit.Text(row, what, "hud-dim");
            }
            HudKit.Text(plate, "F1 ИЛИ ESC — ЗАКРЫТЬ", "hud-caption").AddToClassList("hud-help__footer");
            _hud!.Root!.Add(_help);
        }

        private void BuildSteps()
        {
            _steps = HudKit.Plate("FirstSteps");
            VisualElement head = HudKit.Child(_steps, "hud-steps__head");
            HudKit.Text(head, "ПЕРВЫЕ ШАГИ", "hud-caption");
            var hide = new Button(() =>
            {
                PlayerPrefs.SetInt(HiddenPref, 1);
                PlayerPrefs.Save();
                _steps?.RemoveFromHierarchy();
                _steps = null;
            }) { text = "Скрыть" };
            Hud.StyleButton(hide);
            _hud!.AttachTip(hide, () => new HudTip { Title = "Скрыть первые шаги", Body = "Больше не показывать этот список. Справка всегда по F1." });
            head.Add(hide);
            // Folded: only the step to do now.
            _hud.AddFold(_steps, head, "steps");
            for (int i = 0; i < Steps.Length; i++)
            {
                VisualElement row = HudKit.Child(_steps, "hud-step");
                HudKit.Child(row, "hud-step__box");
                HudKit.Text(row, Steps[i], "hud-step__text");
                _stepRows[i] = row;
            }
            _hud.TopRight!.Add(_steps);
        }

        private void UpdateSteps(SimState state)
        {
            int me = _localPlayer!.PlayerId;
            bool drill = false, belt = false, factory = false, army = false;
            foreach (Building b in state.Buildings)
            {
                if (b.IsDead || b.OwnerId != me)
                    continue;
                drill |= b.Stats.Behavior == BuildingBehavior.Drill;
                belt |= b.Stats.Behavior == BuildingBehavior.Conveyor;
                factory |= b.Factory != null && b.IsComplete;
            }
            foreach (Unit u in state.Units)
            {
                // The hero comes on its own; the step is about hiring.
                if (!u.IsDead && u.OwnerId == me && !u.IsBuilder && !u.IsRepairer && !u.IsHero && u.Worker == null)
                {
                    army = true;
                    break;
                }
            }
            bool income = state.GetPlayer(me).Wallet.Earned(Game.Sim.Resources.Primary) > 0;
            bool[] done = { drill, belt, income, factory, army };
            int doneMask = 0;
            for (int i = 0; i < done.Length; i++)
            {
                if (done[i])
                    doneMask |= 1 << i;
            }
            if (doneMask == _shownDone)
                return;
            _shownDone = doneMask;
            if (doneMask == (1 << done.Length) - 1)
            {
                // All done: the list has nothing more to teach.
                _steps!.RemoveFromHierarchy();
                _steps = null;
                return;
            }
            bool nowMarked = false;
            for (int i = 0; i < done.Length; i++)
            {
                _stepRows[i]!.EnableInClassList("hud-step--done", done[i]);
                bool now = !done[i] && !nowMarked;
                nowMarked |= now;
                _stepRows[i]!.EnableInClassList("hud-step--now", now);
            }
        }
    }
}
