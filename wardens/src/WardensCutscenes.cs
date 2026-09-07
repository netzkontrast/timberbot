// WardensCutscenes.cs. The cutscene runner and its triggers.
//
// design/wardens-cutscenes.md. Scenes are Cutscenes/*.json in the mod folder (WardensCutsceneScript
// is the format). One runner plays them on the main thread: it pauses and locks the speed, shows
// the overlay (WardensCutsceneOverlay), flies the camera through WardensCameraDirector one shot at
// a time, places pointers, toasts and Uplink lines, and hands the game back, in every path, through
// Finish(), with the speed unlock in a finally (a scene that throws must not leave the game locked
// at speed 0, the rule the old WardensColdBoot followed).
//
// Triggers: NewGameInitializedEvent (new_game), the chapter service's ChapterOpened event
// (chapter:<Id>) and the finished tutorial set polled twice a second (tutorial:<Id>, first poll
// silent), all under the policy the Cold Boot had: Wardens faction, tutorial on, and
// "cutscenes": true in settings.json. Triggered scenes are queued and started on the next
// UpdateSingleton, one at a time. The MCP `cutscene` tool bypasses the policy: play, skip,
// continue, reload are the tuning loop. No save state: every trigger is an event a loaded save
// does not re-post.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Timberborn.BlockSystem;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.Common;
using Timberborn.Coordinates;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.Localization;
using Timberborn.QuickNotificationSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using Timberborn.TutorialSettingsSystem;
using Timberborn.TutorialSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensCutscenes : ILoadableSingleton, IUpdatableSingleton
    {
        public const string ColdBootId = "ColdBoot";
        public const string FolderName = "Cutscenes";
        public const float RestoreSeconds = 1.5f;
        private const float PollSeconds = 0.5f;

        private readonly EventBus _eventBus;
        private readonly FactionService _factionService;
        private readonly TutorialSettings _tutorialSettings;
        private readonly TutorialService _tutorialService;
        private readonly SpeedManager _speedManager;
        private readonly DistrictCenterRegistry _districts;
        private readonly EntitySelectionService _selection;
        private readonly CharacterPopulation _population;
        private readonly ILoc _loc;
        private readonly QuickNotificationService _quickNotifications;
        private readonly WardensCameraDirector _director;
        private readonly WardensPointer _pointer;
        private readonly WardensChat _chat;
        private readonly WardensCutsceneOverlay _overlay;
        private readonly WardensChapterService _chapters;

        private readonly List<CutsceneScene> _scenes = new List<CutsceneScene>();
        private readonly Dictionary<string, CutsceneScene> _byId = new Dictionary<string, CutsceneScene>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _played = new List<string>();
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<string> _finishedSeen = new HashSet<string>();
        private string _folder = "";
        private bool _triggersEnabled;
        private bool _pollTutorials;
        private bool _reconciled;
        private float _nextPoll;

        // the running scene (main thread only)
        private CutsceneScene _scene;
        private int _shot = -1;
        private WardensCameraDirector.Keyframe _startPose;
        private float _startSpeed;
        private float _shotStart;
        private bool _flightDone;
        private bool _continued;
        private bool _locked;
        private bool _restoring;
        private int _flightToken;

        public WardensCutscenes(EventBus eventBus, FactionService factionService, TutorialSettings tutorialSettings,
            TutorialService tutorialService, SpeedManager speedManager, DistrictCenterRegistry districts,
            EntitySelectionService selection, CharacterPopulation population, ILoc loc,
            QuickNotificationService quickNotifications, WardensCameraDirector director, WardensPointer pointer,
            WardensChat chat, WardensCutsceneOverlay overlay, WardensChapterService chapters)
        {
            _eventBus = eventBus;
            _factionService = factionService;
            _tutorialSettings = tutorialSettings;
            _tutorialService = tutorialService;
            _speedManager = speedManager;
            _districts = districts;
            _selection = selection;
            _population = population;
            _loc = loc;
            _quickNotifications = quickNotifications;
            _director = director;
            _pointer = pointer;
            _chat = chat;
            _overlay = overlay;
            _chapters = chapters;
        }

        public bool Playing => _scene != null;
        public string CurrentId => _scene?.Id;
        public bool TriggersEnabled => _triggersEnabled;
        public int Count => _scenes.Count;
        public bool HasPlayed(string id) => _played.Contains(id);

        public void Load()
        {
            _eventBus.Register(this);
            _overlay.SkipClicked += () => Skip();
            _overlay.ContinueClicked += () => Continue();
            _chapters.ChapterOpened += OnChapterOpened;
            bool wardens = _factionService.Current?.Id == WardensStartingPopulation.FactionId;
            bool setting = WardensSettings.Load().Cutscenes;
            _triggersEnabled = wardens && setting && !_tutorialSettings.DisableTutorial;
            LoadScenes();
            Debug.Log($"[Wardens] cutscenes: {_scenes.Count} loaded from {_folder}, triggers={_triggersEnabled} " +
                      $"(wardens={wardens}, setting={setting}, tutorial={!_tutorialSettings.DisableTutorial})");
        }

        // ---- triggers (main thread) -----------------------------------------------------------

        // Posted by GameInitializer for a NEW game only; loaded saves never see it.
        [OnEvent]
        public void OnNewGameInitialized(NewGameInitializedEvent e) => Trigger(WardensCutsceneScript.TriggerNewGame);

        private void OnChapterOpened(WardensChapter chapter) => Trigger(WardensCutsceneScript.TriggerChapterPrefix + chapter.Id);

        private void PollTutorials()
        {
            if (!_pollTutorials || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;
            bool announce = _reconciled;
            _reconciled = true;
            foreach (string id in _tutorialService._finishedTutorials)
                if (_finishedSeen.Add(id) && announce) Trigger(WardensCutsceneScript.TriggerTutorialPrefix + id);
        }

        private void Trigger(string trigger)
        {
            if (!_triggersEnabled) return;
            foreach (var scene in _scenes)
            {
                if (!scene.HasTrigger(trigger)) continue;
                _queue.Enqueue(scene.Id);
                Debug.Log($"[Wardens] cutscene {scene.Id}: queued by {trigger}");
            }
        }

        // ---- the loop (main thread, every frame) ------------------------------------------------

        public void UpdateSingleton()
        {
            try
            {
                PollTutorials();
                if (_scene != null) { Advance(); return; }
                if (_restoring)
                {
                    if (_director.IsFlying) return;
                    _restoring = false;
                }
                if (_queue.Count > 0)
                {
                    var id = _queue.Dequeue();
                    if (_byId.TryGetValue(id, out var scene)) Start(scene, "trigger");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] cutscenes: " + ex.Message);
                if (_scene != null) Finish(skipped: true);
            }
        }

        private void Advance()
        {
            var shot = _scene.Shots[_shot];
            if (!_flightDone) return;
            if (Time.unscaledTime - _shotStart < shot.Seconds) return;
            if (shot.WaitForContinue && !_continued)
            {
                _overlay.SetWaitingForContinue(true);
                return;
            }
            if (_shot + 1 < _scene.Shots.Count) StartShot(_shot + 1);
            else Finish(skipped: false);
        }

        // ---- playing --------------------------------------------------------------------------

        /// MCP entry point: plays now, replacing a running scene, whatever the trigger policy says.
        public JObject Play(string id)
        {
            if (!_byId.TryGetValue(id ?? "", out var scene))
                throw new ArgumentException($"unknown cutscene '{id}'; loaded: {string.Join(", ", Ids())}");
            if (_scene != null) Finish(skipped: true);
            Start(scene, "mcp");
            return State();
        }

        public bool Skip()
        {
            if (_scene == null) return false;
            Finish(skipped: true);
            return true;
        }

        public bool Continue()
        {
            if (_scene == null) return false;
            _continued = true;
            _overlay.SetWaitingForContinue(false);
            return true;
        }

        public JObject Reload()
        {
            LoadScenes();
            return State();
        }

        private void Start(CutsceneScene scene, string why)
        {
            _director.Stop();
            _startPose = _director.Current();
            _startSpeed = _speedManager.CurrentSpeed;
            _scene = scene;
            _shot = -1;
            _locked = false;
            _restoring = false;
            try
            {
                if (scene.Pause)
                {
                    _speedManager.ChangeAndLockSpeed(0f);
                    _locked = true;
                }
                _overlay.Show(scene.Letterbox, scene.Skippable, scene.Shots.Count);
                if (!string.IsNullOrEmpty(scene.Say)) _chat.SystemSays(scene.Say);
                Debug.Log($"[Wardens] cutscene {scene.Id}: start ({why}, {scene.Shots.Count} shots, {scene.Seconds:0.#} s)");
                StartShot(0);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Wardens] cutscene {scene.Id}: {ex.Message}");
                Finish(skipped: true);
            }
        }

        private void StartShot(int index)
        {
            _shot = index;
            var shot = _scene.Shots[index];
            _shotStart = Time.unscaledTime;
            _continued = false;
            _flightDone = true;
            _overlay.SetShot(index, CaptionText(shot));
            if (shot.Camera.Count > 0)
            {
                var frames = new List<WardensCameraDirector.Keyframe>(shot.Camera.Count);
                foreach (var k in shot.Camera) frames.Add(Resolve(k));
                _flightDone = false;
                int token = ++_flightToken;
                _director.Fly(frames, () => { if (token == _flightToken) _flightDone = true; });
            }
            if (shot.Point != null) TryPoint(shot);
            if (!string.IsNullOrEmpty(shot.Toast))
            {
                try { _quickNotifications.SendNotification(shot.Toast); }
                catch (Exception ex) { Debug.LogWarning("[Wardens] cutscene toast: " + ex.Message); }
            }
            if (!string.IsNullOrEmpty(shot.Say)) _chat.SystemSays(shot.Say);
        }

        // Every path out of a scene: the last shot, Skip, an exception. The speed unlock is in the
        // finally so a failure mid-shot never leaves the game locked at 0.
        private void Finish(bool skipped)
        {
            var scene = _scene;
            if (scene == null) return;
            _scene = null;
            _flightToken++;
            try
            {
                _director.Stop();
                try { _pointer.Clear(); } catch (Exception ex) { Debug.LogWarning("[Wardens] cutscene pointers: " + ex.Message); }
                _overlay.Hide();
                if (scene.RestoreCamera)
                {
                    var back = _startPose;
                    back.Time = RestoreSeconds;
                    _director.Fly(new List<WardensCameraDirector.Keyframe> { back });
                    _restoring = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Wardens] cutscene {scene.Id}: teardown: {ex.Message}");
            }
            finally
            {
                if (_locked)
                {
                    try { _speedManager.UnlockSpeed(); }
                    catch (Exception ex) { Debug.LogWarning("[Wardens] cutscene unlock speed: " + ex.Message); }
                    _locked = false;
                }
                if (scene.Pause && !scene.LeavePaused)
                {
                    try { _speedManager.ChangeSpeed(Mathf.RoundToInt(_startSpeed)); }
                    catch (Exception ex) { Debug.LogWarning("[Wardens] cutscene restore speed: " + ex.Message); }
                }
            }
            _played.Add(scene.Id);
            Debug.Log($"[Wardens] cutscene {scene.Id}: {(skipped ? "skipped" : "finished")} at shot {_shot + 1}/{scene.Shots.Count}");
        }

        // ---- anchors --------------------------------------------------------------------------

        private WardensCameraDirector.Keyframe Resolve(CutsceneKeyframe k)
        {
            var frame = _startPose;
            frame.Target = AnchorWorld(k.Anchor, k.X, k.Y, k.Z) + GridOffsetToWorld(k.OffsetX, k.OffsetY, k.OffsetZ);
            frame.H = k.H ?? (_startPose.H + (k.DH ?? 0f));
            frame.V = k.V ?? (_startPose.V + (k.DV ?? 0f));
            frame.Zoom = k.Zoom ?? (_startPose.Zoom + (k.DZoom ?? 0f));
            frame.Time = k.T;
            return frame;
        }

        private Vector3 AnchorWorld(CutsceneAnchor anchor, float x, float y, float z)
        {
            switch (anchor)
            {
                case CutsceneAnchor.Core:
                {
                    var coords = CoreCoords();
                    return coords != null ? CoordinateSystem.GridToWorldCentered(coords.Value) : _startPose.Target;
                }
                case CutsceneAnchor.Selection:
                    return _selection.IsAnythingSelected ? _selection.SelectedObject.Transform.position : _startPose.Target;
                case CutsceneAnchor.Bot:
                    foreach (var c in _population.Characters)
                        if (c.GetComponent<Bot>() != null) return c.Transform.position;
                    return _startPose.Target;
                case CutsceneAnchor.Grid:
                    return CoordinateSystem.GridToWorldCentered(
                        new Vector3Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y), Mathf.RoundToInt(z)));
                case CutsceneAnchor.World:
                    return new Vector3(x, y, z);
                default:
                    return _startPose.Target;
            }
        }

        // Grid (x, y, z = height) to world (x, height, z): the same axis swap GridToWorld makes.
        private static Vector3 GridOffsetToWorld(float dx, float dy, float dz) => new Vector3(dx, dz, dy);

        private Vector3Int? CoreCoords()
        {
            foreach (var dc in _districts.AllDistrictCenters)
            {
                var block = dc.GetComponent<BlockObject>();
                if (block != null) return block.Coordinates;
            }
            return null;
        }

        private void TryPoint(CutsceneShot shot)
        {
            var p = shot.Point;
            Vector3Int? coords = null;
            switch (p.Anchor)
            {
                case CutsceneAnchor.Core:
                    coords = CoreCoords();
                    break;
                case CutsceneAnchor.Grid:
                    coords = new Vector3Int(p.X, p.Y, p.Z);
                    break;
                case CutsceneAnchor.Selection:
                    if (_selection.IsAnythingSelected)
                    {
                        var block = _selection.SelectedObject.GetComponent<BlockObject>();
                        if (block != null) coords = block.Coordinates;
                    }
                    break;
            }
            if (coords == null)
            {
                Debug.Log($"[Wardens] cutscene {_scene.Id}/{shot.Id}: pointer anchor {WardensCutsceneScript.AnchorName(p.Anchor)} not found, skipped");
                return;
            }
            var at = coords.Value + new Vector3Int(p.OffsetX, p.OffsetY, p.OffsetZ);
            try
            {
                _pointer.Point(at, p.Message, p.Seconds ?? Mathf.Max(1f, shot.Seconds), p.Color, false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] cutscene pointer: " + ex.Message);
            }
        }

        private string CaptionText(CutsceneShot shot)
        {
            if (!string.IsNullOrEmpty(shot.Caption))
            {
                try { return _loc.T(shot.Caption); }
                catch { return shot.Caption; }
            }
            return shot.Text ?? "";
        }

        // ---- the files ------------------------------------------------------------------------

        private void LoadScenes()
        {
            _scenes.Clear();
            _byId.Clear();
            _errors.Clear();
            _folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", FolderName);
            if (!System.IO.Directory.Exists(_folder))
            {
                _errors.Add("folder missing: " + _folder);
                Debug.LogWarning("[Wardens] cutscenes: no " + _folder + " (the build deploys src/Cutscenes there)");
                return;
            }
            var files = System.IO.Directory.GetFiles(_folder, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var name = System.IO.Path.GetFileName(file);
                try
                {
                    var scene = WardensCutsceneScript.Parse(JObject.Parse(System.IO.File.ReadAllText(file)));
                    scene.File = name;
                    var stem = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (!string.Equals(scene.Id, stem, StringComparison.Ordinal))
                        Debug.LogWarning($"[Wardens] cutscenes: {name} carries id '{scene.Id}' (tools/check_cutscenes.py wants the file stem)");
                    if (_byId.ContainsKey(scene.Id))
                    {
                        _errors.Add($"{name}: duplicate id '{scene.Id}' ({_byId[scene.Id].File})");
                        continue;
                    }
                    _scenes.Add(scene);
                    _byId[scene.Id] = scene;
                }
                catch (Exception ex)
                {
                    _errors.Add($"{name}: {ex.Message}");
                    Debug.LogWarning($"[Wardens] cutscenes: {name}: {ex.Message}");
                }
            }
            _pollTutorials = false;
            foreach (var scene in _scenes)
                foreach (var t in scene.Triggers)
                    if (t.StartsWith(WardensCutsceneScript.TriggerTutorialPrefix, StringComparison.Ordinal)) _pollTutorials = true;
        }

        private List<string> Ids()
        {
            var ids = new List<string>();
            foreach (var scene in _scenes) ids.Add(scene.Id);
            return ids;
        }

        // ---- MCP ------------------------------------------------------------------------------

        private string Waiting()
        {
            if (_scene == null) return null;
            var shot = _scene.Shots[_shot];
            if (!_flightDone) return "flight";
            if (Time.unscaledTime - _shotStart < shot.Seconds) return "time";
            if (shot.WaitForContinue && !_continued) return WardensCutsceneScript.WaitContinue;
            return null;
        }

        /// One block for wardens_status and the frames.
        public JObject Summary()
        {
            var o = new JObject
            {
                ["playing"] = _scene != null,
                ["id"] = _scene?.Id,
                ["shot"] = _scene != null ? (int?)_shot : null,
                ["shots"] = _scene != null ? (int?)_scene.Shots.Count : null,
                ["waiting"] = Waiting(),
            };
            return o;
        }

        /// Everything the `cutscene` tool reports.
        public JObject State()
        {
            var state = Summary();
            if (_scene != null)
            {
                var shot = _scene.Shots[_shot];
                state["shot_id"] = shot.Id;
                state["caption"] = CaptionText(shot);
                state["elapsed"] = Time.unscaledTime - _shotStart;
            }
            state["played"] = new JArray(_played);
            state["queued"] = new JArray(_queue);
            state["triggers_enabled"] = _triggersEnabled;
            state["folder"] = _folder;
            var scenes = new JArray();
            foreach (var scene in _scenes)
            {
                var shots = new JArray();
                foreach (var shot in scene.Shots)
                    shots.Add(new JObject { ["id"] = shot.Id, ["seconds"] = shot.Seconds, ["wait"] = shot.WaitForContinue ? WardensCutsceneScript.WaitContinue : WardensCutsceneScript.WaitTime, ["keyframes"] = shot.Camera.Count });
                scenes.Add(new JObject
                {
                    ["id"] = scene.Id,
                    ["file"] = scene.File,
                    ["on"] = new JArray(scene.Triggers),
                    ["seconds"] = scene.Seconds,
                    ["pause"] = scene.Pause,
                    ["leave_paused"] = scene.LeavePaused,
                    ["restore_camera"] = scene.RestoreCamera,
                    ["shots"] = shots,
                });
            }
            state["scenes"] = scenes;
            state["errors"] = new JArray(_errors);
            return state;
        }
    }
}
