// WardensCutscenes.cs. The cutscene runner and its triggers.
//
// design/wardens-cutscenes.md. Scenes are Cutscenes/*.json in the mod folder (WardensCutsceneScript
// is the format). One runner plays them on the main thread: it pauses and locks the speed, shows
// the overlay (WardensCutsceneOverlay), flies the camera through WardensCameraDirector one shot at
// a time, fills the captions' {0}.. from the game (`args`), places pointers, highlights, toasts and
// Uplink lines, replays archived badtides (WardensArchivedBadtides), records marks and choices in
// the story record (WardensStoryState), skips shots whose `when` condition does not hold, and hands
// the game back, in every path, through Finish(), with the speed unlock in a finally (a scene
// that throws must not leave the game locked at speed 0,
// the rule the old WardensColdBoot followed).
//
// Triggers: NewGameInitializedEvent (new_game) and the finished tutorial set polled twice a second
// (tutorial:<Id>, first poll silent), under the policy the Cold Boot had: Wardens faction, tutorial
// on, and "cutscenes": true in settings.json. The level's story is the exception, because it is the
// level and not a tutorial (DisableTutorial is the player's global setting, and the author plays with
// it off): level:<Id> on a new game on campaign level <Id>, replacing the new_game scenes there, and
// the task triggers WardensLevelTasks fires through TriggerStory: task:<level>.<Id> when a task goes
// live, task_done:<level>.<Id> when it is done, level_complete:<Id> when the last one is. Those need
// the Wardens and the setting only. Triggered scenes are queued and started on the next
// UpdateSingleton, one at a time, in file-name order when one trigger fires several. The MCP
// `cutscene` tool bypasses the policy: play, skip, continue, choose, reload are the tuning loop.
// No save state of its own: every trigger is an event a loaded save does not re-post; the story
// record (choices, marks) is the one thing that persists, in story.json.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Timberborn.Beavers;
using Timberborn.BlockSystem;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.Common;
using Timberborn.Coordinates;
using Timberborn.GameCycleSystem;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.Localization;
using Timberborn.QuickNotificationSystem;
using Timberborn.ResourceCountingSystem;
using Timberborn.ScienceSystem;
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
        private const string DataCoreId = "DataCore";
        private const string None = "none";

        private readonly EventBus _eventBus;
        private readonly FactionService _factionService;
        private readonly TutorialSettings _tutorialSettings;
        private readonly TutorialService _tutorialService;
        private readonly SpeedManager _speedManager;
        private readonly DistrictCenterRegistry _districts;
        private readonly EntitySelectionService _selection;
        private readonly CharacterPopulation _population;
        private readonly IDayNightCycle _dayNightCycle;
        private readonly GameCycleService _cycles;
        private readonly ScienceService _science;
        private readonly ILoc _loc;
        private readonly QuickNotificationService _quickNotifications;
        private readonly WardensCameraDirector _director;
        private readonly WardensPointer _pointer;
        private readonly WardensChat _chat;
        private readonly WardensCutsceneOverlay _overlay;
        private readonly WardensStoryState _story;
        private readonly WardensArchivedBadtides _badtides;
        private readonly WardensCampaignService _campaign;

        private readonly List<CutsceneScene> _scenes = new List<CutsceneScene>();
        private readonly Dictionary<string, CutsceneScene> _byId = new Dictionary<string, CutsceneScene>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _played = new List<string>();
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<string> _finishedSeen = new HashSet<string>();
        private string _folder = "";
        private bool _triggersEnabled;
        private bool _levelTriggersEnabled;
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
            EntitySelectionService selection, CharacterPopulation population, IDayNightCycle dayNightCycle,
            GameCycleService cycles, ScienceService science, ILoc loc, QuickNotificationService quickNotifications,
            WardensCameraDirector director, WardensPointer pointer, WardensChat chat, WardensCutsceneOverlay overlay,
            WardensStoryState story, WardensArchivedBadtides badtides, WardensCampaignService campaign)
        {
            _eventBus = eventBus;
            _factionService = factionService;
            _tutorialSettings = tutorialSettings;
            _tutorialService = tutorialService;
            _speedManager = speedManager;
            _districts = districts;
            _selection = selection;
            _population = population;
            _dayNightCycle = dayNightCycle;
            _cycles = cycles;
            _science = science;
            _loc = loc;
            _quickNotifications = quickNotifications;
            _director = director;
            _pointer = pointer;
            _chat = chat;
            _overlay = overlay;
            _story = story;
            _badtides = badtides;
            _campaign = campaign;
        }

        public bool Playing => _scene != null;

        /// A choice card was answered: (choice key, choice id), after it is in the story record.
        public event Action<string, string> Chose;
        public string CurrentId => _scene?.Id;
        public bool TriggersEnabled => _triggersEnabled;
        public int Count => _scenes.Count;
        public bool HasPlayed(string id) => _played.Contains(id);

        /// The game's opening has played this session: the Cold Boot, or a level's own opening
        /// (a scene bound to level:<Id>), which replaces it on that level.
        public bool OpeningPlayed
        {
            get
            {
                if (HasPlayed(ColdBootId)) return true;
                foreach (var scene in _scenes)
                    if (_played.Contains(scene.Id))
                        foreach (var t in scene.Triggers)
                            if (t.StartsWith(WardensCutsceneScript.TriggerLevelPrefix, StringComparison.Ordinal)) return true;
                return false;
            }
        }

        public void Load()
        {
            _eventBus.Register(this);
            _overlay.SkipClicked += () => Guard(() => Skip(), "skip");
            _overlay.ContinueClicked += () => Guard(() => Continue(), "continue");
            _overlay.ChoiceClicked += id => Guard(() => Choose(id), "choose");
            bool wardens = _factionService.Current?.Id == WardensStartingPopulation.FactionId;
            bool setting = WardensSettings.Load().Cutscenes;
            _triggersEnabled = wardens && setting && !_tutorialSettings.DisableTutorial;
            _levelTriggersEnabled = wardens && setting;
            LoadScenes();
            Debug.Log($"[Wardens] cutscenes: {_scenes.Count} loaded from {_folder}, triggers={_triggersEnabled}, " +
                      $"level triggers={_levelTriggersEnabled} " +
                      $"(wardens={wardens}, setting={setting}, tutorial={!_tutorialSettings.DisableTutorial})");
        }

        // A UI callback must not throw into UI Toolkit: log and carry on.
        private static void Guard(Action action, string what)
        {
            try { action(); }
            catch (Exception ex) { Debug.LogWarning($"[Wardens] cutscene {what}: {ex.Message}"); }
        }

        // ---- triggers (main thread) -----------------------------------------------------------

        // Posted by GameInitializer for a NEW game only; loaded saves never see it. On a campaign
        // level, a scene bound to level:<Id> is the opening and the new_game scenes stay quiet.
        [OnEvent]
        public void OnNewGameInitialized(NewGameInitializedEvent e)
        {
            var level = _campaign.Enabled ? _campaign.Level : null;
            if (level != null && _levelTriggersEnabled &&
                Trigger(WardensCutsceneScript.TriggerLevelPrefix + level.Id, force: true) > 0) return;
            Trigger(WardensCutsceneScript.TriggerNewGame);
        }

        /// The level's story beats (WardensLevelTasks): task:<level>.<Id>, task_done:<level>.<Id>,
        /// level_complete:<Id>. The level policy (Wardens, "cutscenes": true), not the tutorial's.
        /// Returns how many scenes were queued.
        public int TriggerStory(string trigger) => _levelTriggersEnabled ? Trigger(trigger, force: true) : 0;

        /// The scenes bound to a trigger, for the `campaign tasks` listing.
        public List<string> ScenesFor(string trigger)
        {
            var ids = new List<string>();
            foreach (var scene in _scenes) if (scene.HasTrigger(trigger)) ids.Add(scene.Id);
            return ids;
        }

        private void PollTutorials()
        {
            if (!_pollTutorials || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;
            bool announce = _reconciled;
            _reconciled = true;
            foreach (string id in _tutorialService._finishedTutorials)
                if (_finishedSeen.Add(id) && announce) Trigger(WardensCutsceneScript.TriggerTutorialPrefix + id);
        }

        // Queues every scene bound to `trigger`; returns how many. `force` skips the tutorial half of
        // the policy (the caller has checked the rest), which the level's own triggers do.
        private int Trigger(string trigger, bool force = false)
        {
            if (!_triggersEnabled && !force) return 0;
            int queued = 0;
            foreach (var scene in _scenes)
            {
                if (!scene.HasTrigger(trigger)) continue;
                _queue.Enqueue(scene.Id);
                queued++;
                Debug.Log($"[Wardens] cutscene {scene.Id}: queued by {trigger}");
            }
            return queued;
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
            if (shot.WaitForChoice && !_continued) return;          // the choice card is up; the pick ends the shot
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
            var shot = _scene.Shots[_shot];
            if (shot.WaitForChoice) return false;                    // a choice card needs a choice, not Continue
            _continued = true;
            _overlay.SetWaitingForContinue(false);
            return true;
        }

        /// Answers the open choice card; the pick is recorded under the shot's choice key.
        public bool Choose(string id)
        {
            if (_scene == null) return false;
            var shot = _scene.Shots[_shot];
            if (!shot.WaitForChoice || _continued) return false;
            CutsceneChoice found = null;
            foreach (var c in shot.Choices) if (c.Id == id) found = c;
            if (found == null)
            {
                var ids = new List<string>();
                foreach (var c in shot.Choices) ids.Add(c.Id);
                throw new ArgumentException($"no choice '{id}' in {_scene.Id}/{shot.Id}; choices: {string.Join(", ", ids)}");
            }
            _story.SetChoice(shot.ChoiceKey, found.Id);
            try { Chose?.Invoke(shot.ChoiceKey, found.Id); }
            catch (Exception ex) { Debug.LogWarning($"[Wardens] cutscene choice listener: {ex.Message}"); }
            _continued = true;
            _overlay.SetChoices(null);
            Debug.Log($"[Wardens] cutscene {_scene.Id}/{shot.Id}: chose {found.Id} ({shot.ChoiceKey})");
            return true;
        }

        public JObject Reload()
        {
            LoadScenes();
            return State();
        }

        public string ResetStory() => _story.Reset();

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

        // Starts the first shot at or after `index` whose `when` holds; ends the scene when none is left.
        private void StartShot(int index)
        {
            int i = index;
            while (i < _scene.Shots.Count && !Applies(_scene.Shots[i]))
            {
                Debug.Log($"[Wardens] cutscene {_scene.Id}/{_scene.Shots[i].Id}: skipped (when {_scene.Shots[i].When.ChoiceKey})");
                i++;
            }
            if (i >= _scene.Shots.Count)
            {
                Finish(skipped: false);
                return;
            }
            _shot = i;
            var shot = _scene.Shots[i];
            _shotStart = Time.unscaledTime;
            _continued = false;
            _flightDone = true;
            if (shot.Mark != null) _story.SetMark(shot.Mark, _dayNightCycle.DayNumber);
            _overlay.SetShot(i, CaptionText(shot.Caption, shot.Text, shot.Args));
            if (shot.WaitForChoice)
            {
                var choices = new List<KeyValuePair<string, string>>();
                foreach (var c in shot.Choices)
                    choices.Add(new KeyValuePair<string, string>(c.Id, CaptionText(c.Caption, c.Text, null)));
                _overlay.SetChoices(choices);
            }
            if (shot.Camera.Count > 0)
            {
                var frames = new List<WardensCameraDirector.Keyframe>(shot.Camera.Count);
                foreach (var k in shot.Camera) frames.Add(Resolve(k));
                _flightDone = false;
                int token = ++_flightToken;
                _director.Fly(frames, () => { if (token == _flightToken) _flightDone = true; });
            }
            if (shot.Point != null) TryPoint(shot, shot.Point, arrow: true);
            if (shot.Highlight != null) TryPoint(shot, shot.Highlight, arrow: false);
            if (!string.IsNullOrEmpty(shot.Toast))
            {
                try { _quickNotifications.SendNotification(shot.Toast); }
                catch (Exception ex) { Debug.LogWarning("[Wardens] cutscene toast: " + ex.Message); }
            }
            if (!string.IsNullOrEmpty(shot.Say)) _chat.SystemSays(shot.Say);
            if (shot.Badtide > 0) _badtides.Replay(shot.Badtide);
        }

        private bool Applies(CutsceneShot shot)
        {
            if (shot.When == null) return true;
            var chosen = _story.Choice(shot.When.ChoiceKey);
            if (shot.When.Is != null) return chosen == shot.When.Is;
            return chosen != shot.When.IsNot;
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
                Debug.LogWarning($"[Wardens] cutscene {scene.Id}: finish: {ex.Message}");
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

        // ---- captions and args ----------------------------------------------------------------

        // ILoc has T(key) and generic T<T1..T3>(key, p1..p3), no params object[]: an array passed
        // whole binds to T<object[]> and the caption reads "System.Object[]" (seen in the game,
        // 0.4.10). Past three args, format the row ourselves.
        private string Localize(string key, object[] v)
        {
            switch (v.Length)
            {
                case 0: return _loc.T(key);
                case 1: return _loc.T(key, v[0]);
                case 2: return _loc.T(key, v[0], v[1]);
                case 3: return _loc.T(key, v[0], v[1], v[2]);
                default: return string.Format(_loc.T(key), v);
            }
        }

        private string CaptionText(string key, string literal, List<string> args)
        {
            var values = new object[args != null ? args.Count : 0];
            for (int i = 0; i < values.Length; i++) values[i] = ArgValue(args[i]);
            if (!string.IsNullOrEmpty(key))
            {
                try { return Localize(key, values); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Wardens] cutscene caption {key}: {ex.Message}");
                    return key;
                }
            }
            if (string.IsNullOrEmpty(literal)) return "";
            if (values.Length == 0) return literal;
            try { return string.Format(literal, values); }
            catch (FormatException) { return literal; }
        }

        // day | cycle | cycle_day | bots | beavers | archive | science | good:<Id> | choice:<key> | mark:<name>
        private object ArgValue(string arg)
        {
            try
            {
                switch (arg)
                {
                    case "day": return _dayNightCycle.DayNumber;
                    case "cycle": return (int)_cycles.Cycle;
                    case "cycle_day": return _cycles.CycleDay;
                    case "bots": return CountCharacters(bots: true);
                    case "beavers": return CountCharacters(bots: false);
                    case "archive": return Stock(DataCoreId);
                    case "science": return _science.SciencePoints;
                }
                if (arg.StartsWith(WardensCutsceneScript.ArgGoodPrefix, StringComparison.Ordinal))
                    return Stock(arg.Substring(WardensCutsceneScript.ArgGoodPrefix.Length));
                if (arg.StartsWith(WardensCutsceneScript.ArgChoicePrefix, StringComparison.Ordinal))
                    return _story.Choice(arg.Substring(WardensCutsceneScript.ArgChoicePrefix.Length)) ?? None;
                if (arg.StartsWith(WardensCutsceneScript.ArgMarkPrefix, StringComparison.Ordinal))
                {
                    var mark = _story.Mark(arg.Substring(WardensCutsceneScript.ArgMarkPrefix.Length));
                    return mark != null ? (object)mark.Value : None;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Wardens] cutscene arg {arg}: {ex.Message}");
            }
            return "?";
        }

        private int CountCharacters(bool bots)
        {
            int n = 0;
            foreach (var c in _population.Characters)
                if ((c.GetComponent<Bot>() != null) == bots) n++;
            return n;
        }

        private int Stock(string goodId)
        {
            int total = 0;
            foreach (var dc in _districts.AllDistrictCenters)
            {
                var counter = dc.GetComponent<DistrictResourceCounter>();
                if (counter != null) total += counter.GetResourceCount(goodId).AllStock;
            }
            return total;
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
                case CutsceneAnchor.Beaver:
                    foreach (var c in _population.Characters)
                        if (c.GetComponent<Beaver>() != null) return c.Transform.position;
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

        private void TryPoint(CutsceneShot shot, CutscenePointer p, bool arrow)
        {
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
                Debug.Log($"[Wardens] cutscene {_scene.Id}/{shot.Id}: {(arrow ? "pointer" : "highlight")} anchor {WardensCutsceneScript.AnchorName(p.Anchor)} not found, skipped");
                return;
            }
            var at = coords.Value + new Vector3Int(p.OffsetX, p.OffsetY, p.OffsetZ);
            try
            {
                _pointer.Point(at, arrow ? p.Message : null, p.Seconds ?? Mathf.Max(1f, shot.Seconds), p.Color, false, arrow);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] cutscene pointer: " + ex.Message);
            }
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
            if (shot.WaitForChoice && !_continued) return WardensCutsceneScript.WaitChoice;
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
            if (_scene != null && _scene.Shots[_shot].WaitForChoice && !_continued)
            {
                var ids = new JArray();
                foreach (var c in _scene.Shots[_shot].Choices) ids.Add(c.Id);
                o["choices"] = ids;
            }
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
                state["caption"] = CaptionText(shot.Caption, shot.Text, shot.Args);
                state["elapsed"] = Time.unscaledTime - _shotStart;
                if (shot.WaitForChoice) state["choice_key"] = shot.ChoiceKey;
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
                {
                    var s = new JObject
                    {
                        ["id"] = shot.Id,
                        ["seconds"] = shot.Seconds,
                        ["wait"] = shot.WaitForChoice ? WardensCutsceneScript.WaitChoice : shot.WaitForContinue ? WardensCutsceneScript.WaitContinue : WardensCutsceneScript.WaitTime,
                        ["keyframes"] = shot.Camera.Count,
                    };
                    if (shot.WaitForChoice) s["choice_key"] = shot.ChoiceKey;
                    if (shot.When != null) s["when"] = shot.When.ChoiceKey + (shot.When.Is != null ? " is " + shot.When.Is : " is not " + shot.When.IsNot);
                    shots.Add(s);
                }
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
            state["story"] = _story.ToJson();
            state["errors"] = new JArray(_errors);
            return state;
        }
    }
}
