// WardensLevelTasks.cs. A level's tasks: what the player is asked to do, checked against the game.
//
// Timberborn has no objective system, and the Wardens' tutorial line advances only when the player
// clicks the vanilla card's Continue, which the game hides when the player has the tutorial turned off
// (TutorialPanels.TutorialIsOn; a level ending on a tutorial then never ends). So a level's tasks live
// here: Levels/<id>.tasks.json in the mod folder, checked every half second whatever the tutorial
// setting. A done task stays done (saved with the game). When the last one is done the level is
// complete: the campaign records it, and the task panel (WardensTaskPanel.cs) turns into the
// level-end card whose Continue starts the next level (WardensLevelTransition.cs).
//
//   { "level": "01",
//     "tasks": [ { "id": "Charge", "title": "<loc key>", "text": "<loc key>", "after": ["Scavenge"],
//                  "checks": [ { "type": "powered", "template": "ChargingPost.Wardens", "count": 2 } ] } ] }
//
// The tasks are a graph. A task is live when every task in its `after` is done (without `after`: the
// task before it in the file, so a plain list still runs in order); several can be live at once and
// each is checked. The story rides on it: the moment a task goes live the cutscene runner plays the
// scenes bound to task:<level>.<Id>, and when it is done those bound to task_done:<level>.<Id>; the
// last one done plays level_complete:<level>. A task scene is a beat about that task's land and ask,
// which is what replaced the long opening tours and the tutorial-bound chapter table (retired in
// iteration 05: with the tutorial off, which is how the author plays, the chapters never fired).
//
// Two rules keep the scenes honest. The first poll waits for ShowPrimaryUIEvent, the last step of
// GameInitializer on a new game and a load alike: a new game posts NewGameInitializedEvent (which
// queues the level's opening) a few frames into play, and a task scene queued before it would play
// first. And the first poll reconciles silently: tasks a loaded colony already meets tick through with
// no toast and no scene; a task live when the game was saved plays nothing again; only a task that
// goes live from here on plays its scene. A new game has no saved state, so its first live tasks do.
//
// Check types: built (finished buildings of a template), powered (finished, on a network with a
// generator), generating (a generator actually making power), workers (Wardens assigned to finished
// buildings of a template), stock (a good in the districts' stock), beavers (beavers alive),
// built_in (finished buildings of any of `templates` whose Coordinates lie in `box`), clean_water
// (tiles in `box` with water at least 0.2 deep and contamination under 0.05). A box is
// [x1, y1, x2, y2] in grid tiles, inclusive; `where` is the loc key that names it on the panel.
// tools/validate.py checks the files: known types, known templates and goods, loc keys present.
//
// Game members, all in the 1.1.2.4 decompile: BuiltBuildingService.GetFinishedBuildings,
// MechanicalNode.Graph / OutputMultiplier (GoodPoweredGenerator sets it to 0 when it burns nothing),
// MechanicalGraph.NumberOfGenerators, Workplace.NumberOfAssignedWorkers,
// DistrictResourceCounter.GetResourceCount(good).AllStock, PopulationService.GlobalPopulationData.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Timberborn.Buildings;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.BlockSystem;
using Timberborn.Localization;
using Timberborn.MapIndexSystem;
using Timberborn.MechanicalSystem;
using Timberborn.Persistence;
using Timberborn.Population;
using Timberborn.QuickNotificationSystem;
using Timberborn.ResourceCountingSystem;
using Timberborn.SingletonSystem;
using Timberborn.TutorialSteps;
using Timberborn.UILayoutSystem;
using Timberborn.WaterSystem;
using Timberborn.WorkSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Wardens
{
    public sealed class LevelTaskCheck
    {
        public string Type = "";
        public string Template = "";
        public readonly List<string> Templates = new List<string>();   // built_in: any of these
        public string Good = "";
        public int Count = 1;
        public int[] Box;                                              // [x1, y1, x2, y2], inclusive
        public string Where = "";                                      // loc key naming the box
    }

    public sealed class LevelTask
    {
        public string Id = "";
        public string Title = "";      // loc key
        public string Text = "";       // loc key
        public readonly List<string> After = new List<string>();   // live once these are done
        public readonly List<LevelTaskCheck> Checks = new List<LevelTaskCheck>();
    }

    public static class LevelTaskFile
    {
        public const string FolderName = "Levels";
        public static readonly string[] Types = { "built", "powered", "generating", "workers", "stock", "beavers", "built_in", "clean_water" };
        public const string TriggerTaskPrefix = WardensCutsceneScript.TriggerTaskPrefix;
        public const string TriggerTaskDonePrefix = WardensCutsceneScript.TriggerTaskDonePrefix;
        public const string TriggerLevelCompletePrefix = WardensCutsceneScript.TriggerLevelCompletePrefix;

        public static string PathFor(string levelId) => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", FolderName, levelId + ".tasks.json");

        /// Parses one file; every problem goes to `errors` and the task or check is skipped. An `after`
        /// id that names no task is dropped with an error (tools/check_level_tasks.py also catches
        /// cycles, which the runtime does not look for: a task on a cycle simply never goes live).
        public static List<LevelTask> Parse(JObject json, List<string> errors)
        {
            var tasks = new List<LevelTask>();
            if (!(json["tasks"] is JArray list))
            {
                errors.Add("no \"tasks\" array");
                return tasks;
            }
            var ids = new HashSet<string>();
            string previous = null;
            foreach (var token in list)
            {
                if (!(token is JObject o)) { errors.Add("a task is not an object"); continue; }
                var task = new LevelTask
                {
                    Id = o.Value<string>("id") ?? "",
                    Title = o.Value<string>("title") ?? "",
                    Text = o.Value<string>("text") ?? "",
                };
                if (task.Id == "" || !ids.Add(task.Id)) { errors.Add($"task id '{task.Id}' missing or repeated"); continue; }
                if (o["after"] is JArray after)
                {
                    foreach (var t in after)
                        if (t.Type == JTokenType.String && (string)t != task.Id) task.After.Add((string)t);
                }
                else if (previous != null) task.After.Add(previous);
                previous = task.Id;
                if (o["checks"] is JArray checks)
                {
                    foreach (var c in checks)
                    {
                        if (!(c is JObject co)) { errors.Add($"{task.Id}: a check is not an object"); continue; }
                        var check = new LevelTaskCheck
                        {
                            Type = co.Value<string>("type") ?? "",
                            Template = co.Value<string>("template") ?? "",
                            Good = co.Value<string>("good") ?? "",
                            Count = co.Value<int?>("count") ?? 1,
                            Where = co.Value<string>("where") ?? "",
                        };
                        if (co["templates"] is JArray alts)
                            foreach (var t in alts) check.Templates.Add((string)t);
                        else if (check.Template != "") check.Templates.Add(check.Template);
                        if (co["box"] is JArray box && box.Count == 4)
                            check.Box = new[] { (int)box[0], (int)box[1], (int)box[2], (int)box[3] };
                        if (Array.IndexOf(Types, check.Type) < 0) { errors.Add($"{task.Id}: unknown check type '{check.Type}'"); continue; }
                        bool needsBox = check.Type == "built_in" || check.Type == "clean_water";
                        if (needsBox && check.Box == null) { errors.Add($"{task.Id}: check '{check.Type}' needs box [x1, y1, x2, y2]"); continue; }
                        bool needsTemplate = check.Type != "stock" && check.Type != "beavers" && check.Type != "clean_water";
                        if (check.Type == "stock" ? check.Good == "" : needsTemplate && check.Templates.Count == 0)
                        {
                            errors.Add($"{task.Id}: check '{check.Type}' needs {(check.Type == "stock" ? "good" : "template")}");
                            continue;
                        }
                        task.Checks.Add(check);
                    }
                }
                if (task.Checks.Count == 0) { errors.Add($"{task.Id}: no checks, it could never be done"); continue; }
                tasks.Add(task);
            }
            var kept = new HashSet<string>();
            foreach (var task in tasks) kept.Add(task.Id);
            foreach (var task in tasks)
                task.After.RemoveAll(id =>
                {
                    if (kept.Contains(id)) return false;
                    errors.Add($"{task.Id}: after names '{id}', which is not a task of this level; ignored");
                    return true;
                });
            return tasks;
        }
    }

    public class WardensLevelTasks : ILoadableSingleton, IUpdatableSingleton, ISaveableSingleton
    {
        private const float PollSeconds = 0.5f;
        // If ShowPrimaryUIEvent never arrives (another mod replaced the start, a game update renamed
        // it), start polling anyway after this long: a level that never completes is worse than a
        // task scene that plays before the opening.
        private const float StartTimeoutSeconds = 20f;
        private static readonly SingletonKey Key = new SingletonKey("WardensLevelTasks");
        private static readonly PropertyKey<string> LevelKey = new PropertyKey<string>("Level");
        private static readonly ListKey<string> DoneKey = new ListKey<string>("Done");

        private readonly ISingletonLoader _singletonLoader;
        private readonly EventBus _eventBus;
        private readonly WardensCampaignService _campaign;
        private readonly WardensLevelTransitionService _transition;
        private readonly WardensCutscenes _cutscenes;
        private readonly WardensChat _chat;
        private readonly QuickNotificationService _notifications;
        private readonly ILoc _loc;
        private readonly BuiltBuildingService _built;
        private readonly BuildingService _buildings;
        private readonly DistrictCenterRegistry _districts;
        private readonly PopulationService _population;
        private readonly IThreadSafeWaterMap _waterMap;
        private readonly MapIndexService _mapIndex;

        private readonly List<LevelTask> _tasks = new List<LevelTask>();
        private readonly HashSet<string> _done = new HashSet<string>();
        private readonly HashSet<string> _liveSeen = new HashSet<string>();   // live tasks whose scene has had its turn
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private string _levelId = "";
        private bool _complete;
        private bool _announced;        // this session saw the level complete (not a reload of an old win)
        private bool _started;          // ShowPrimaryUIEvent seen: the game's own start is over
        private bool _reconciled;       // the first poll (silent) is behind us
        private float _loadedAt;
        private float _nextPoll;

        public WardensLevelTasks(ISingletonLoader singletonLoader, EventBus eventBus, WardensCampaignService campaign,
            WardensLevelTransitionService transition, WardensCutscenes cutscenes, WardensChat chat,
            QuickNotificationService notifications, ILoc loc, BuiltBuildingService built,
            BuildingService buildings, DistrictCenterRegistry districts, PopulationService population,
            IThreadSafeWaterMap waterMap, MapIndexService mapIndex)
        {
            _waterMap = waterMap;
            _mapIndex = mapIndex;
            _singletonLoader = singletonLoader;
            _eventBus = eventBus;
            _campaign = campaign;
            _transition = transition;
            _cutscenes = cutscenes;
            _chat = chat;
            _notifications = notifications;
            _loc = loc;
            _built = built;
            _buildings = buildings;
            _districts = districts;
            _population = population;
        }

        /// A task went live (its `after` done), after the silent first poll. Its scene is queued.
        public event Action<LevelTask> TaskLive;
        public event Action<LevelTask> TaskDone;
        public event Action LevelComplete;

        public bool Active => _tasks.Count > 0;
        public bool Complete => _complete;
        public bool CompletedThisSession => _announced;
        public IReadOnlyList<LevelTask> Tasks => _tasks;
        public WardensLevel Level => _campaign.Level;
        public WardensLevel NextLevel => _campaign.Level == null ? null : WardensCampaignService.ById(_campaign.Level.Next);
        public bool IsDone(LevelTask task) => _done.Contains(task.Id);
        public int DoneCount => _done.Count;

        /// Not done, and every task it waits for is.
        public bool IsLive(LevelTask task)
        {
            if (_done.Contains(task.Id)) return false;
            foreach (var id in task.After) if (!_done.Contains(id)) return false;
            return true;
        }

        /// The live tasks, in file order.
        public List<LevelTask> Live
        {
            get
            {
                var live = new List<LevelTask>();
                foreach (var task in _tasks) if (IsLive(task)) live.Add(task);
                return live;
            }
        }

        /// The first live task in file order: what the panel's header and a one-line summary name.
        public LevelTask Current
        {
            get
            {
                foreach (var task in _tasks) if (IsLive(task)) return task;
                return null;
            }
        }

        public void Load()
        {
            _eventBus.Register(this);
            _loadedAt = Time.unscaledTime;
            if (!_campaign.Enabled || _campaign.Level == null) return;
            _levelId = _campaign.Level.Id;
            var path = LevelTaskFile.PathFor(_levelId);
            if (!File.Exists(path))
            {
                Debug.Log($"[Wardens] tasks: none for level {_levelId} ({path})");
                return;
            }
            try
            {
                var errors = new List<string>();
                _tasks.AddRange(LevelTaskFile.Parse(JObject.Parse(File.ReadAllText(path)), errors));
                foreach (var e in errors) Debug.LogWarning($"[Wardens] tasks {_levelId}: {e}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Wardens] tasks {_levelId}: {path}: {ex.Message}");
                _tasks.Clear();
            }
            bool saved = _singletonLoader.TryGetSingleton(Key, out var loader) && loader.Has(LevelKey) && loader.Get(LevelKey) == _levelId;
            if (saved && loader.Has(DoneKey))
                foreach (var id in loader.Get(DoneKey)) _done.Add(id);
            // What was live when the game was saved has had its scene: a reload does not replay it.
            if (saved)
                foreach (var task in Live) _liveSeen.Add(task.Id);
            _complete = _tasks.Count > 0 && _done.Count >= _tasks.Count;
            if (_complete) _campaign.CompleteByTasks(announce: false);
            Debug.Log($"[Wardens] tasks: level {_levelId}, {_tasks.Count} task(s), {_done.Count} done" +
                      $"{(_complete ? ", level complete" : "")}{(saved ? "" : ", no saved state (a new game)")}");
        }

        // GameInitializer's last step, on a new game and a load alike (Timberborn.GameStartup).
        [OnEvent]
        public void OnShowPrimaryUI(ShowPrimaryUIEvent e) => _started = true;

        public void Save(ISingletonSaver singletonSaver)
        {
            if (_tasks.Count == 0) return;
            var saver = singletonSaver.GetSingleton(Key);
            saver.Set(LevelKey, _levelId);
            saver.Set(DoneKey, new List<string>(_done));
        }

        public void UpdateSingleton()
        {
            if (_tasks.Count == 0 || _complete) return;
            if (!_started)
            {
                if (Time.unscaledTime - _loadedAt < StartTimeoutSeconds) return;
                _started = true;
                Debug.LogWarning($"[Wardens] tasks: no ShowPrimaryUIEvent within {StartTimeoutSeconds:0} s; polling anyway");
            }
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;
            bool announce = _reconciled;
            _reconciled = true;
            try
            {
                // Every live task is checked; one that is done can make others live, which are checked
                // in the same poll, so a colony that already did the next ones ticks through.
                int quiet = 0;
                for (bool progress = true; progress;)
                {
                    progress = false;
                    foreach (var task in Live)
                    {
                        if (!Achieved(task)) continue;
                        _done.Add(task.Id);
                        progress = true;
                        Debug.Log($"[Wardens] tasks: {task.Id} done ({_done.Count}/{_tasks.Count}){(announce ? "" : ", reconciled")}");
                        if (!announce) { quiet++; continue; }
                        var next = Current;
                        string line = _loc.T("Wardens.Tasks.Done", _loc.T(task.Title)) +
                                      (next != null ? " " + _loc.T("Wardens.Tasks.Next", _loc.T(next.Title)) : "");
                        Say(line, toast: true);
                        Safe(() => TaskDone?.Invoke(task), "task done handlers");
                        _cutscenes.TriggerStory(LevelTaskFile.TriggerTaskDonePrefix + _levelId + "." + task.Id);
                    }
                }
                if (quiet > 0) Debug.Log($"[Wardens] tasks: reconciled {quiet} already met, silently");
                foreach (var task in Live)
                {
                    if (!_liveSeen.Add(task.Id)) continue;
                    Debug.Log($"[Wardens] tasks: {task.Id} live");
                    Safe(() => TaskLive?.Invoke(task), "task live handlers");
                    _cutscenes.TriggerStory(LevelTaskFile.TriggerTaskPrefix + _levelId + "." + task.Id);
                }
                if (_done.Count >= _tasks.Count) Finish();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] tasks poll: " + ex.Message);
            }
        }

        // The level is won in this session: by play, or at load by a colony that met the last task
        // while no save recorded it. campaign.json knowing the win already makes it old news (no toast,
        // no end scene), whichever way the tasks got here.
        private void Finish()
        {
            _complete = true;
            _announced = true;
            bool oldNews = _campaign.RecordedComplete(_levelId);
            _campaign.CompleteByTasks(announce: true);
            Debug.Log($"[Wardens] tasks: level {_levelId} complete{(oldNews ? " (already in campaign.json)" : "")}");
            Safe(() => LevelComplete?.Invoke(), "level complete handlers");
            if (!oldNews) _cutscenes.TriggerStory(LevelTaskFile.TriggerLevelCompletePrefix + _levelId);
        }

        private static void Safe(Action action, string what)
        {
            try { action(); }
            catch (Exception ex) { Debug.LogWarning($"[Wardens] tasks {what}: {ex.Message}"); }
        }

        /// Ends this colony (exit-saved) and starts the next level. Main thread only (a UI click, a
        /// frame update, or the MCP queue).
        public WardensTransition StartNextLevel()
        {
            var next = NextLevel;
            if (next == null) return WardensTransition.Fail("this is the last level");
            if (!next.Shipped) return WardensTransition.Fail($"level {next.Id} ({next.Title}) is not built yet");
            Say(_loc.T("Wardens.Tasks.Starting", next.Title), toast: false);
            var result = _transition.Start(next, new CampaignMode());
            if (!result.Started) Say(result.Message, toast: true);
            return result;
        }

        // ---- checks -----------------------------------------------------------------------------

        public bool Achieved(LevelTask task)
        {
            foreach (var check in task.Checks)
                if (Have(check) < check.Count) return false;
            return true;
        }

        public int Have(LevelTaskCheck check)
        {
            switch (check.Type)
            {
                case "built":
                    return _built.GetFinishedBuildings(check.Template).Count;
                case "powered":
                {
                    int n = 0;
                    foreach (var b in _built.GetFinishedBuildings(check.Template))
                    {
                        var graph = b.GetComponent<MechanicalNode>()?.Graph;
                        if (graph != null && graph.NumberOfGenerators > 0) n++;
                    }
                    return n;
                }
                case "generating":
                {
                    int n = 0;
                    foreach (var b in _built.GetFinishedBuildings(check.Template))
                    {
                        var node = b.GetComponent<MechanicalNode>();
                        if (node != null && node.IsGenerator && node.Graph != null && node.OutputMultiplier > 0f) n++;
                    }
                    return n;
                }
                case "workers":
                {
                    int n = 0;
                    foreach (var b in _built.GetFinishedBuildings(check.Template))
                        n += b.GetComponent<Workplace>()?.NumberOfAssignedWorkers ?? 0;
                    return n;
                }
                case "stock":
                {
                    int n = 0;
                    foreach (var dc in _districts.AllDistrictCenters)
                    {
                        var counter = dc.GetComponent<DistrictResourceCounter>();
                        if (counter != null) n += counter.GetResourceCount(check.Good).AllStock;
                    }
                    return n;
                }
                case "beavers":
                    return _population.GlobalPopulationData.NumberOfBeavers;
                case "built_in":
                {
                    int n = 0;
                    foreach (var template in check.Templates)
                        foreach (var b in _built.GetFinishedBuildings(template))
                        {
                            var c = b.GetComponent<BlockObject>().Coordinates;
                            if (c.x >= check.Box[0] && c.x <= check.Box[2] && c.y >= check.Box[1] && c.y <= check.Box[3]) n++;
                        }
                    return n;
                }
                case "clean_water":
                    return CleanWaterTiles(check.Box);
            }
            return 0;
        }

        // Tiles in the box whose water is at least a fifth of a block deep and all but clean: the
        // water a beaver could drink (IThreadSafeWaterMap, the columns Timberbot's /api/tiles reads).
        private const float CleanDepth = 0.2f;
        private const float CleanContamination = 0.05f;

        private int CleanWaterTiles(int[] box)
        {
            int n = 0;
            int stride = _mapIndex.VerticalStride;
            for (int y = box[1]; y <= box[3]; y++)
                for (int x = box[0]; x <= box[2]; x++)
                {
                    if (x < 0 || y < 0 || x >= _mapIndex.TerrainSize.x || y >= _mapIndex.TerrainSize.y) continue;
                    int index2D = _mapIndex.CellToIndex(new Vector2Int(x, y));
                    for (int ci = 0; ci < _waterMap.ColumnCount(index2D); ci++)
                    {
                        var col = _waterMap.WaterColumns[ci * stride + index2D];
                        if (col.WaterDepth >= CleanDepth && col.Contamination < CleanContamination) { n++; break; }
                    }
                }
            return n;
        }

        /// "Close the creek: Floodgate in the creek 1/2": the task's title and its first unmet check,
        /// for the frame's `attention`.
        public string NextStep(LevelTask task)
        {
            string title = _loc.T(task.Title);
            foreach (var check in task.Checks)
                if (Have(check) < check.Count) return title + ": " + Describe(check);
            return title + ": met, ticks on the next poll";
        }

        /// "Charging Post powered: 1/2", in the player's language.
        public string Describe(LevelTaskCheck check)
        {
            int have = Math.Min(Have(check), check.Count);
            string name;
            switch (check.Type)
            {
                case "stock": name = GoodName(check.Good); break;
                case "beavers": name = ""; break;
                case "clean_water": name = _loc.T(check.Where); break;
                case "built_in":
                    name = _loc.T("Wardens.Tasks.In", string.Join(" / ", check.Templates.ConvertAll(BuildingName)), _loc.T(check.Where));
                    break;
                default: name = BuildingName(check.Templates.Count > 0 ? check.Templates[0] : check.Template); break;
            }
            return _loc.T("Wardens.Tasks.Check." + check.Type, name, have, check.Count);
        }

        private string BuildingName(string template)
        {
            if (_names.TryGetValue(template, out var name)) return name;
            try
            {
                var labeled = _buildings.GetBuildingTemplate(template).GetSpec<LabeledEntitySpec>();
                name = _loc.T(labeled.DisplayNameLocKey);
            }
            catch (Exception)
            {
                name = template;
            }
            _names[template] = name;
            return name;
        }

        private string GoodName(string good)
        {
            if (!_names.TryGetValue("good:" + good, out var name))
                _names["good:" + good] = name = _loc.T($"Good.{good}.PluralDisplayName");
            return name;
        }

        private void Say(string line, bool toast)
        {
            try { _chat.SystemSays(line); } catch (Exception ex) { Debug.LogWarning("[Wardens] tasks chat: " + ex.Message); }
            if (!toast) return;
            try { _notifications.SendNotification(line); } catch (Exception ex) { Debug.LogWarning("[Wardens] tasks toast: " + ex.Message); }
        }

        /// The frame's `task`: where the level stands, without the per-check detail State() carries.
        /// `current` is the first live task; `live` all of them (at most two by the data's rule).
        public JObject Summary()
        {
            var current = Current;
            var live = new JArray();
            foreach (var task in Live) live.Add(new JObject { ["id"] = task.Id, ["title"] = _loc.T(task.Title) });
            return new JObject
            {
                ["current"] = current?.Id,
                ["title"] = current == null ? null : _loc.T(current.Title),
                ["live"] = live,
                ["done"] = _done.Count,
                ["total"] = _tasks.Count,
                ["complete"] = _complete,
            };
        }

        public JObject State()
        {
            var tasks = new JArray();
            var current = Current;
            foreach (var task in _tasks)
            {
                var checks = new JArray();
                foreach (var c in task.Checks)
                    checks.Add(new JObject
                    {
                        ["type"] = c.Type, ["template"] = c.Template == "" ? null : c.Template,
                        ["templates"] = c.Templates.Count > 1 ? new JArray(c.Templates.ToArray()) : null,
                        ["box"] = c.Box == null ? null : new JArray(c.Box[0], c.Box[1], c.Box[2], c.Box[3]),
                        ["good"] = c.Good == "" ? null : c.Good, ["count"] = c.Count, ["have"] = Have(c),
                        ["text"] = Describe(c),
                    });
                tasks.Add(new JObject
                {
                    ["id"] = task.Id,
                    ["title"] = _loc.T(task.Title),
                    ["text"] = _loc.T(task.Text),
                    ["after"] = new JArray(task.After.ToArray()),
                    ["done"] = _done.Contains(task.Id),
                    ["live"] = IsLive(task),
                    ["current"] = current == task,
                    ["scenes"] = new JArray(_cutscenes.ScenesFor(LevelTaskFile.TriggerTaskPrefix + _levelId + "." + task.Id).ToArray()),
                    ["done_scenes"] = new JArray(_cutscenes.ScenesFor(LevelTaskFile.TriggerTaskDonePrefix + _levelId + "." + task.Id).ToArray()),
                    ["checks"] = checks,
                });
            }
            return new JObject
            {
                ["level"] = _levelId == "" ? null : _levelId,
                ["file"] = _levelId == "" ? null : LevelTaskFile.PathFor(_levelId),
                ["done"] = _done.Count,
                ["total"] = _tasks.Count,
                ["current"] = current?.Id,
                ["live"] = new JArray(Live.ConvertAll(t => t.Id).ToArray()),
                ["complete"] = _complete,
                ["tasks"] = tasks,
            };
        }
    }
}
