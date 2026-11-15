using System.Collections.Generic;
using Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Sound of the game: effects for sim events and interface clicks, and music that follows the
    // mission (menu, calm, a wave on its way, battle, the end). One object for the whole run: it
    // makes itself before the first scene and stays over scene reloads, so music is not cut when a
    // mission starts. Clips are found by name in Resources/Audio; a missing clip is silence.
    public sealed class GameAudio : MonoBehaviour
    {
        private const string Folder = "Audio/";
        private const int Voices = 16;
        private const float SfxVolume = 0.8f;
        private const float MusicVolume = 0.4f;
        private const float FadeSeconds = 2.5f;
        // Music stays on the battle for this long after the last blow that involved the team.
        private const float BattleHoldSeconds = 12f;
        // Layered tracks (music_x_l0, _l1, _l2 split from one recording): l0 always plays, the
        // others come in as the intensity rises. Seconds for a layer to come in or out.
        private const int MaxLayers = 3;
        private const float LayerFadeSeconds = 2f;
        // An enemy this close to a team building (cells) raises the calm music by a layer.
        private const float ThreatCells = 12f;
        // Blows on or by the team in the last BlowWindow seconds; this many is the fiercest battle.
        private const float BlowWindow = 5f;
        private const int FierceBlows = 30;

        private enum Mood { None, Menu, Calm, Tension, Battle, Victory, Defeat }

        private static readonly string[] CalmTracks = { "music_calm_1", "music_calm_2", "music_calm_3" };
        private static readonly string[] BattleTracks = { "music_battle_1", "music_battle_2" };

        private readonly Dictionary<string, AudioClip?> _clips = new Dictionary<string, AudioClip?>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private readonly List<AudioSource> _voices = new List<AudioSource>();
        private readonly HashSet<VisualElement> _hookedRoots = new HashSet<VisualElement>();
        // NonSerialized: after scripts are recompiled during Play these come back empty, so the
        // sources are found again and the runner's events are subscribed to again (Bind).
        [System.NonSerialized] private MusicDeck? _musicA;
        [System.NonSerialized] private MusicDeck? _musicB;
        private bool _aIsCurrent;
        private float _intensity;
        private float _nextIntensityTime;
        private readonly Queue<float> _blowTimes = new Queue<float>();

        [System.NonSerialized] private SimRunner? _runner;
        [System.NonSerialized] private LocalPlayer? _localPlayer;
        [System.NonSerialized] private EntityView? _entities;
        [System.NonSerialized] private SelectionController? _selection;
        [System.NonSerialized] private Camera? _camera;

        private Mood _mood = Mood.None;
        private int _calmIndex;
        private int _battleIndex;
        private float _lastBattleTime = float.NegativeInfinity;
        private int _tensionUntilTick;
        private float _nextUiScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            var host = new GameObject("GameAudio");
            DontDestroyOnLoad(host);
            host.AddComponent<GameAudio>();
        }

        private void Awake() => EnsureSources();

        // Also from Update: scripts recompiled while playing in the editor keep this object but
        // drop its fields, and Awake does not run again. The sources are its children, found anew.
        private void EnsureSources()
        {
            if (_musicA != null)
                return;
            _voices.Clear();
            for (int i = 0; i < Voices; i++)
                _voices.Add(Source($"Voice{i}"));
            var a = new AudioSource[MaxLayers];
            var b = new AudioSource[MaxLayers];
            for (int i = 0; i < MaxLayers; i++)
            {
                a[i] = Source($"MusicA{i}");
                b[i] = Source($"MusicB{i}");
            }
            _musicA = new MusicDeck(a);
            _musicB = new MusicDeck(b);
        }

        private AudioSource Source(string name)
        {
            Transform? existing = transform.Find(name);
            if (existing != null && existing.TryGetComponent(out AudioSource found))
                return found;
            var child = new GameObject(name);
            child.transform.SetParent(transform, worldPositionStays: false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        // The scene is loaded anew for every mission: its runner and views are new objects. When the
        // runner it knew is gone, the audio finds the new one. A search may still meet the old
        // scene's runner in the frame it dies; then it is gone next frame and is looked for again.
        private void Bind()
        {
            if (_runner != null)
                return;
            if (!ReferenceEquals(_selection, null))
                _selection!.Changed -= OnSelectionChanged;
            _runner = FindAnyObjectByType<SimRunner>();
            if (_runner == null)
                return;
            _runner.TickEvents += OnTickEvents;
            _localPlayer = FindAnyObjectByType<LocalPlayer>();
            _entities = FindAnyObjectByType<EntityView>();
            _selection = FindAnyObjectByType<SelectionController>();
            RtsCamera? view = FindAnyObjectByType<RtsCamera>();
            _camera = view != null ? view.GetComponent<Camera>() : null;
            if (_selection != null)
                _selection.Changed += OnSelectionChanged;
            _hookedRoots.Clear();
            _nextUiScan = 0f;
            _lastBattleTime = float.NegativeInfinity;
            _tensionUntilTick = 0;
            _blowTimes.Clear();
        }

        private void Update()
        {
            EnsureSources();
            Bind();
            HookInterface();
            UpdateMusic();
        }

        // ---- Effects ----

        private void OnTickEvents(IReadOnlyList<SimEvent> events)
        {
            SimState? state = _runner != null ? _runner.Simulation?.State : null;
            // A save being replayed to its tick runs thousands of ticks at once: silent.
            if (state == null || _runner!.IsLoading)
                return;
            int me = _localPlayer != null ? _localPlayer.PlayerId : 0;
            foreach (SimEvent e in events)
            {
                switch (e)
                {
                    case HitLanded hit:
                        if (IsMine(state, OwnerOf(state, hit.AttackerId), me) || IsMine(state, OwnerOf(state, hit.TargetId), me))
                        {
                            _lastBattleTime = Time.unscaledTime;
                            _blowTimes.Enqueue(Time.unscaledTime);
                        }
                        PlayAt(ShotClip(state, hit.AttackerId), hit.TargetId, 0.45f, 0.07f);
                        break;
                    case EntityDestroyed destroyed:
                        PlayAt(destroyed.IsBuilding ? "sfx_death_building" : "sfx_death_unit", destroyed.EntityId, destroyed.Demolished ? 0.4f : 0.8f, 0.1f);
                        break;
                    case AbilityUsed used:
                        string ability = AbilityClip(state, used.AbilityId);
                        if (ability.Length > 0)
                            PlayAt(ability, Ground.At(used.X, used.Y), 0.9f, 0.1f);
                        break;
                    case RockDestroyed rock:
                        PlayAt("sfx_rock_break", Ground.Lift(new Vector3(rock.CellX + 0.5f, 0f, rock.CellY + 0.5f)), 0.7f, 0.1f);
                        break;
                    case ConstructionCompleted built when built.OwnerId == me:
                        Play("sfx_build_complete", 0.7f, 0.3f);
                        break;
                    case ConstructionFrozen frozen when frozen.OwnerId == me:
                        Play("sfx_ui_warning", 0.8f, 1f);
                        break;
                    case UnitProduced produced when produced.OwnerId == me:
                        Play("sfx_unit_ready", 0.7f, 0.3f);
                        break;
                    case CommandRejected rejected when rejected.PlayerId == me:
                        Play("sfx_ui_error", 0.8f, 0.25f);
                        break;
                    case BuildingCaptured captured when IsMine(state, captured.PlayerId, me):
                        Play("sfx_capture", 0.9f, 0.3f);
                        break;
                    case LighthouseLit lit when IsMine(state, lit.PlayerId, me):
                        Play("sfx_lighthouse", 0.8f, 0.5f);
                        break;
                    case BuildingAttacked attacked when attacked.OwnerId == me:
                        Play("sfx_alarm_base", 0.9f, 4f);
                        break;
                    case UnitAttacked attacked when attacked.OwnerId == me:
                        Play("sfx_alarm_unit", 0.6f, 4f);
                        break;
                    case WaveIncoming incoming:
                        _tensionUntilTick = Mathf.Max(_tensionUntilTick, Mathf.Max(incoming.LaunchTick, incoming.ArrivalTick));
                        Play("sfx_wave_incoming", 1f, 1f);
                        break;
                    case WaveLaunched launched:
                        _tensionUntilTick = Mathf.Max(_tensionUntilTick, Mathf.Max(launched.ArrivalTick, launched.Tick + 20 * Simulation.TicksPerSecond));
                        Play("sfx_wave_launched", 1f, 1f);
                        break;
                    case CampCleared:
                        Play("sfx_camp_cleared", 0.8f, 0.5f);
                        break;
                    case HeroDied died when died.PlayerId == me:
                        Play("sfx_unit_lost", 0.9f, 0.5f);
                        break;
                    case BuilderDied died when died.PlayerId == me:
                        Play("sfx_unit_lost", 0.9f, 0.5f);
                        break;
                    case HeroRespawned back when back.PlayerId == me:
                        Play("sfx_respawn", 0.8f, 0.5f);
                        break;
                    case BuilderRespawned back when back.PlayerId == me:
                        Play("sfx_respawn", 0.8f, 0.5f);
                        break;
                    case HeroLevelUp up when up.PlayerId == me:
                        Play("sfx_level_up", 0.9f, 0.5f);
                        break;
                    case Pinged ping when IsMine(state, ping.PlayerId, me):
                        Play("sfx_ping", 0.9f, 0.2f);
                        break;
                    case ResourcesGiven given when given.FromPlayerId == me || given.ToPlayerId == me:
                        Play("sfx_resources_given", 0.8f, 0.2f);
                        break;
                }
            }
        }

        private static int OwnerOf(SimState state, int entityId) => state.FindEntity(entityId)?.OwnerId ?? 0;

        // The local player or an ally: what the team is part of.
        private static bool IsMine(SimState state, int playerId, int me) =>
            playerId == me || (playerId != 0 && me != 0 && state.HasPlayer(playerId) && state.HasPlayer(me) && state.Relations.IsAlly(me, playerId));

        // units.csv / buildings.csv "projectile" names the shot; without one it is a melee blow.
        private static string ShotClip(SimState state, int attackerId)
        {
            string projectile = state.FindUnit(attackerId) is Unit u ? u.Stats.Projectile : state.FindBuilding(attackerId)?.Stats.Projectile ?? "";
            switch (projectile)
            {
                case "arrow": return "sfx_shot_arrow";
                case "shell": return "sfx_shot_shell";
                case "stone": return "sfx_shot_stone";
                case "bolt": return "sfx_shot_bolt";
                case "spit": return "sfx_shot_spit";
                default: return "sfx_hit_melee";
            }
        }

        // abilities.csv "effect" groups the abilities by how they sound.
        private static string AbilityClip(SimState state, string abilityId)
        {
            if (!state.Data.TryGetAbility(abilityId, out AbilityStats? ability))
                return "";
            switch (ability!.Effect)
            {
                case "starfall":
                case "meteor":
                case "ring": return "sfx_ability_blast";
                case "heal":
                case "grace": return "sfx_ability_heal";
                case "bolt":
                case "sparks":
                case "flash": return "sfx_ability_electric";
                case "charge":
                case "whirl": return "sfx_ability_charge";
                default: return "";
            }
        }

        // At an entity the fog shows; what the player does not see makes no sound.
        private void PlayAt(string clip, int entityId, float volume, float minInterval)
        {
            if (_entities != null && _entities.TryGetEntity(entityId, out Vector3 at, out _, out _))
                PlayAt(clip, at, volume, minInterval);
        }

        // The camera looks from far above, so distance is counted on the screen: full volume in
        // view, fading out beyond its edges, panned by where the point is across the screen.
        private void PlayAt(string clip, Vector3 world, float volume, float minInterval)
        {
            float pan = 0f;
            if (_camera != null)
            {
                Vector3 view = _camera.WorldToViewportPoint(world);
                if (view.z <= 0f)
                    return;
                float outside = Mathf.Max(Mathf.Max(-view.x, view.x - 1f), Mathf.Max(-view.y, view.y - 1f));
                volume *= Mathf.Clamp01(1f - outside * 1.5f);
                pan = Mathf.Clamp(view.x * 2f - 1f, -1f, 1f) * 0.7f;
            }
            Play(clip, volume, minInterval, pan, vary: true);
        }

        // minInterval: the same clip is not started again sooner, so a volley is one sound and not a wall of them.
        private void Play(string name, float volume, float minInterval, float pan = 0f, bool vary = false)
        {
            if (volume <= 0.01f)
                return;
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(name, out float last) && now - last < minInterval)
                return;
            AudioClip? clip = Clip(name);
            if (clip == null)
                return;
            AudioSource? voice = null;
            foreach (AudioSource candidate in _voices)
            {
                if (!candidate.isPlaying)
                {
                    voice = candidate;
                    break;
                }
            }
            if (voice == null)
                return;
            _lastPlayed[name] = now;
            voice.clip = clip;
            voice.volume = volume * SfxVolume;
            voice.panStereo = pan;
            voice.pitch = vary ? Random.Range(0.94f, 1.06f) : 1f;
            voice.Play();
        }

        private AudioClip? Clip(string name)
        {
            if (!_clips.TryGetValue(name, out AudioClip? clip))
            {
                clip = UnityEngine.Resources.Load<AudioClip>(Folder + name);
                _clips.Add(name, clip);
            }
            return clip;
        }

        // ---- Interface ----

        private void OnSelectionChanged() => Play("sfx_ui_select", 0.5f, 0.08f);

        // Every button of every UI document clicks; documents rebuild their trees, so look again now and then.
        private void HookInterface()
        {
            if (Time.unscaledTime < _nextUiScan)
                return;
            _nextUiScan = Time.unscaledTime + 1f;
            foreach (UIDocument document in FindObjectsByType<UIDocument>())
            {
                VisualElement? root = document.rootVisualElement;
                if (root != null && _hookedRoots.Add(root))
                    root.RegisterCallback<ClickEvent>(OnUiClick, TrickleDown.TrickleDown);
            }
        }

        private void OnUiClick(ClickEvent click)
        {
            for (VisualElement? element = click.target as VisualElement; element != null; element = element.parent)
            {
                if (element is Button)
                {
                    Play("sfx_ui_click", 0.6f, 0.03f);
                    return;
                }
            }
        }

        // ---- Music ----

        private void UpdateMusic()
        {
            Mood wanted = WantedMood();
            MusicDeck current = _aIsCurrent ? _musicA! : _musicB!;
            MusicDeck other = _aIsCurrent ? _musicB! : _musicA!;
            current.Update();
            other.Update();

            // Calm and battle play their tracks in turn, the next once one has run to its end; a faction
            // theme that ends during tension gives way to what tension plays.
            bool ended = _mood == wanted && (wanted == Mood.Calm || wanted == Mood.Battle || wanted == Mood.Tension) && current.Ended;
            if (wanted != _mood || ended)
            {
                Mood was = _mood;
                _mood = wanted;
                // A layered faction theme rides calm into tension: it does not start over, its
                // layers come in instead (heights of the same piece, not another track).
                bool calmFamily = (was == Mood.Calm || was == Mood.Tension) && (wanted == Mood.Calm || wanted == Mood.Tension);
                if (calmFamily && !ended && current.Layers > 1 && current.Track == FactionTheme())
                    current.SetLoop(wanted == Mood.Tension);
                else
                    StartTrack(TrackFor(wanted), loop: wanted == Mood.Menu || wanted == Mood.Tension);
            }

            UpdateIntensity();
            current = _aIsCurrent ? _musicA! : _musicB!;
            other = _aIsCurrent ? _musicB! : _musicA!;
            float fade = Time.unscaledDeltaTime / FadeSeconds;
            float layerFade = Time.unscaledDeltaTime / LayerFadeSeconds;
            current.Mix = Mathf.MoveTowards(current.Mix, 1f, fade);
            other.Mix = Mathf.MoveTowards(other.Mix, 0f, fade);
            current.Apply(_intensity, layerFade);
            other.Apply(_intensity, layerFade);
            if (other.Mix <= 0f)
                other.Stop();
        }

        // 0 — calm, nobody near; about 0.4 — an enemy seen by a team building; 1 — a wave on its way
        // or the fiercest battle. Worked out twice a second; the layers follow it smoothly.
        private void UpdateIntensity()
        {
            float now = Time.unscaledTime;
            while (_blowTimes.Count > 0 && now - _blowTimes.Peek() > BlowWindow)
                _blowTimes.Dequeue();
            if (now < _nextIntensityTime)
                return;
            _nextIntensityTime = now + 0.5f;
            SimState? state = _runner != null ? _runner.Simulation?.State : null;
            switch (_mood)
            {
                case Mood.Battle:
                    _intensity = 0.4f + 0.6f * Mathf.Clamp01(_blowTimes.Count / (float)FierceBlows);
                    return;
                case Mood.Tension:
                    _intensity = 1f;
                    return;
                case Mood.Calm:
                    _intensity = state != null && EnemyNearTeam(state) ? 0.4f : 0f;
                    return;
                default:
                    _intensity = 1f;
                    return;
            }
        }

        // An enemy unit the local player sees within ThreatCells of a team building (belts aside).
        private bool EnemyNearTeam(SimState state)
        {
            int me = _localPlayer != null ? _localPlayer.PlayerId : 0;
            if (!state.HasPlayer(me))
                return false;
            long reach = (long)(ThreatCells * Game.Sim.Grid.MilliPerCell);
            foreach (Unit u in state.Units)
            {
                if (u.IsDead || !state.Relations.IsEnemy(me, u.OwnerId) || !Visibility.UnitShown(state, me, u))
                    continue;
                foreach (Building b in state.Buildings)
                {
                    if (b.IsDead || b.Stats.IsBelt || !IsMine(state, b.OwnerId, me))
                        continue;
                    long dx = u.X - Game.Sim.Grid.CellToMilli(b.CellX) - Game.Sim.Grid.CellToMilli(b.Width) / 2;
                    long dy = u.Y - Game.Sim.Grid.CellToMilli(b.CellY) - Game.Sim.Grid.CellToMilli(b.Height) / 2;
                    if (dx * dx + dy * dy <= reach * reach)
                        return true;
                }
            }
            return false;
        }

        private Mood WantedMood()
        {
            SimState? state = _runner != null ? _runner.Simulation?.State : null;
            if (state == null)
                return Mood.Menu;
            if (state.Outcome == MissionOutcome.Victory)
                return Mood.Victory;
            if (state.Outcome == MissionOutcome.Defeat)
                return Mood.Defeat;
            if (_runner!.IsLoading)
                return Mood.Calm;
            if (Time.unscaledTime - _lastBattleTime < BattleHoldSeconds)
                return Mood.Battle;
            return state.Tick < _tensionUntilTick ? Mood.Tension : Mood.Calm;
        }

        // music_faction_<id> of the local player's faction, "" without one.
        private string FactionTheme()
        {
            SimState? state = _runner != null ? _runner.Simulation?.State : null;
            int me = _localPlayer != null ? _localPlayer.PlayerId : 0;
            string? faction = state != null && state.HasPlayer(me) ? state.GetPlayer(me).Faction?.Id : null;
            return faction != null && LayerClips(faction.Length > 0 ? "music_faction_" + faction : "").Count > 0 ? "music_faction_" + faction : "";
        }

        // The local player's faction theme opens the calm music and comes back between the shared calm tracks.
        private string CalmTrack()
        {
            string theme = FactionTheme();
            int turn = _calmIndex++;
            if (theme.Length > 0 && turn % 2 == 0)
                return theme;
            return CalmTracks[(theme.Length > 0 ? turn / 2 : turn) % CalmTracks.Length];
        }

        private string TrackFor(Mood mood)
        {
            switch (mood)
            {
                case Mood.Menu: return "music_menu";
                case Mood.Calm: return CalmTrack();
                // A layered faction theme plays the wave in at full; a whole one leaves it to the tension track.
                case Mood.Tension:
                    string theme = FactionTheme();
                    return theme.Length > 0 && LayerClips(theme).Count > 1 ? theme : "music_tension";
                case Mood.Battle: return BattleTracks[_battleIndex++ % BattleTracks.Length];
                case Mood.Victory: return "music_victory";
                case Mood.Defeat: return "music_defeat";
                default: return "";
            }
        }

        // The new track fades in on the idle deck while the old one fades out on the other.
        private void StartTrack(string name, bool loop)
        {
            _aIsCurrent = !_aIsCurrent;
            MusicDeck next = _aIsCurrent ? _musicA! : _musicB!;
            next.Load(name, LayerClips(name), loop);
        }

        // The clip itself, or its layers name_l0, name_l1, … in order; empty when there is neither.
        private List<AudioClip> LayerClips(string name)
        {
            var clips = new List<AudioClip>();
            if (name.Length == 0)
                return clips;
            AudioClip? whole = Clip(name);
            if (whole != null)
            {
                clips.Add(whole);
                return clips;
            }
            for (int i = 0; i < MaxLayers; i++)
            {
                AudioClip? layer = Clip($"{name}_l{i}");
                if (layer == null)
                    break;
                clips.Add(layer);
            }
            return clips;
        }

        // One track on up to MaxLayers sources started on the same sample. Mix is the crossfade
        // between tracks; each layer above l0 follows the intensity on its own fader.
        private sealed class MusicDeck
        {
            private readonly AudioSource[] _sources;
            private readonly float[] _layerLevel;
            private float _startedAt;
            private bool _scheduled;

            public MusicDeck(AudioSource[] sources)
            {
                _sources = sources;
                _layerLevel = new float[sources.Length];
            }

            public string Track { get; private set; } = "";
            public int Layers { get; private set; }
            public float Mix { get; set; }

            // Has run to its end (a looped track never does).
            public bool Ended
            {
                get
                {
                    if (!_scheduled || Layers == 0)
                        return Layers == 0 && Track.Length > 0;
                    AudioSource lead = _sources[0];
                    return !lead.loop && !lead.isPlaying && lead.time <= 0f
                        && Time.unscaledTime - _startedAt >= lead.clip.length - 0.25f;
                }
            }

            public void Load(string track, List<AudioClip> clips, bool loop)
            {
                Stop();
                Track = track;
                Layers = Mathf.Min(clips.Count, _sources.Length);
                Mix = 0f;
                for (int i = 0; i < _sources.Length; i++)
                {
                    _sources[i].clip = i < Layers ? clips[i] : null;
                    _sources[i].loop = loop;
                    _sources[i].volume = 0f;
                    _layerLevel[i] = 0f;
                    if (i < Layers && clips[i].loadState != AudioDataLoadState.Loaded)
                        clips[i].LoadAudioData();
                }
            }

            // Starts the layers together once every one of them is loaded; a start that did not take
            // (the very first frames of the run) is made again, all layers at once, so they stay in step.
            public void Update()
            {
                if (Layers == 0)
                    return;
                for (int i = 0; i < Layers; i++)
                {
                    if (_sources[i].clip.loadState != AudioDataLoadState.Loaded)
                        return;
                }
                AudioSource lead = _sources[0];
                // Standing at its very start after it should have begun. A playing track is past 0
                // even in the frames where isPlaying lags behind, so it is never restarted by mistake.
                float since = Time.unscaledTime - _startedAt;
                bool stalled = _scheduled && !lead.isPlaying && lead.time <= 0f && since > 0.5f
                    && (lead.loop || since < lead.clip.length - 0.25f);
                if (_scheduled && !stalled)
                    return;
                double at = AudioSettings.dspTime + 0.1;
                for (int i = 0; i < Layers; i++)
                    _sources[i].PlayScheduled(at);
                _scheduled = true;
                _startedAt = Time.unscaledTime;
            }

            // l0 always; l1 from intensity 0.2 to 0.45; l2 from 0.55 to 0.85.
            public void Apply(float intensity, float step)
            {
                for (int i = 0; i < Layers; i++)
                {
                    float target = i == 0 ? 1f
                        : i == 1 ? Mathf.InverseLerp(0.2f, 0.45f, intensity)
                        : Mathf.InverseLerp(0.55f, 0.85f, intensity);
                    // A track that is one piece plays whole: its single layer is all of it.
                    if (Layers == 1)
                        target = 1f;
                    _layerLevel[i] = Mathf.MoveTowards(_layerLevel[i], target, step);
                    _sources[i].volume = _layerLevel[i] * Mix * MusicVolume;
                }
            }

            // A track kept across calm and tension loops while the wave is on its way and ends as usual after.
            public void SetLoop(bool loop)
            {
                for (int i = 0; i < Layers; i++)
                    _sources[i].loop = loop;
            }

            public void Stop()
            {
                foreach (AudioSource source in _sources)
                {
                    if (source.isPlaying)
                        source.Stop();
                }
                _scheduled = false;
            }
        }
    }
}
