using System;
using System.Collections.Generic;
using System.IO;
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

        private void Awake() => _runner = GetComponent<SimRunner>();

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
            if (_overlay == null || _runner!.Simulation == null)
                return;
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;
            if (_open)
                Close();
            else if (_placement == null || !_placement.IsBusy)
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
            _runner!.Paused = true;
            Begin("Пауза");
            AddButton("Продолжить (Esc)", Close);
            AddButton("Сохранить", Save);
            AddButton("Загрузить", () => ShowLoad(back: ShowPause));
            AddButton("Выйти в меню", () =>
            {
                GameSession.ToMenu();
                SimRunner.Reload();
            });
            AddButton("Закрыть игру", Quit);
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
            Hud.StyleButton(button);
            button.style.minHeight = 40;
            button.style.marginTop = button.style.marginBottom = 4;
            button.style.fontSize = 16;
            _column!.Add(button);
            if (_message != null)
            {
                _message.RemoveFromHierarchy();
                _column.Add(_message);
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
