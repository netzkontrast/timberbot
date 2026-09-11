// WardensMcpTools.cs. The tool table behind the in-game MCP server.
//
// Two families:
//   * Wardens tools: status, tutorial, point/unpoint, say/chat, selection, camera, cutscene,
//     speed. These touch game state and run on the main thread (WardensMcpServer queues them).
//   * Timberbot tools: `timberbot` forwards GET/POST to the Timberbot HTTP API that is compiled
//     into this same mod (loopback, exactly like Timberbot's own widget talks to its server),
//     `timberbot_ready` flips the ready gate in-process, `timberbot_routes` lists the routes.
//     These are I/O only and run on the listener thread (OffThread).
//
// Every result may carry "chat": player messages from the in-game panel not yet seen by the
// agent. chat_read long-polls for the next one, which is how a turn-based conversation works.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Timberbot;
using Timberborn.BlockSystem;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.EntitySystem;
using Timberborn.GameFactionSystem;
using Timberborn.NeedSystem;
using Timberborn.QuickNotificationSystem;
using Timberborn.SelectionSystem;
using Timberborn.TemplateSystem;
using Timberborn.TimeSystem;
using Timberborn.TutorialSettingsSystem;
using Timberborn.TutorialSystem;
using UnityEngine;

namespace Wardens
{
    public class McpTool
    {
        public string Name;
        public string Description;
        public JObject InputSchema;
        public Func<JObject, JObject> Run;
        public bool OffThread;
    }

    public class WardensMcpTools
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };

        private static readonly string[] TimberbotGetRoutes =
        {
            "/api/ping", "/api/settlement", "/api/agent/state", "/api/summary", "/api/time", "/api/weather",
            "/api/population", "/api/resources", "/api/districts", "/api/buildings", "/api/trees", "/api/crops",
            "/api/gatherables", "/api/beavers", "/api/workhours", "/api/science", "/api/wellbeing",
            "/api/notifications", "/api/alerts", "/api/distribution", "/api/prefabs", "/api/power", "/api/speed",
            "/api/tree_clusters", "/api/food_clusters", "/api/tiles",
        };

        private static readonly string[] TimberbotPostRoutes =
        {
            "/api/ready", "/api/agent/config", "/api/agent/message", "/api/agent/request", "/api/speed",
            "/api/workhours", "/api/distribution", "/api/science/unlock", "/api/district/migrate",
            "/api/building/place", "/api/building/demolish", "/api/building/pause", "/api/building/priority",
            "/api/building/hauling", "/api/building/recipe", "/api/building/farmhouse", "/api/building/plantable",
            "/api/building/workers", "/api/building/floodgate", "/api/building/storage", "/api/building/clutch",
            "/api/building/range", "/api/placement/find", "/api/planting/find", "/api/planting/mark",
            "/api/planting/clear", "/api/crop/demolish", "/api/cutting/area", "/api/path/place",
            "/api/automation/link", "/api/automation/unlink", "/api/automation/configure", "/api/automation/rename",
            "/api/debug", "/api/benchmark",
        };

        private readonly FactionService _factionService;
        private readonly SpeedManager _speedManager;
        private readonly CharacterPopulation _population;
        private readonly TutorialService _tutorialService;
        private readonly TutorialSettings _tutorialSettings;
        private readonly WardensPointer _pointer;
        private readonly WardensChat _chat;
        private readonly WardensCameraDirector _director;
        private readonly WardensCutscenes _cutscenes;
        private readonly EntitySelectionService _selection;
        private readonly QuickNotificationService _quickNotifications;
        private readonly TimberbotService _timberbot;
        private readonly WardensAssetDump _assetDump;
        private readonly WardensBar _bar;
        private readonly WardensFrames _frames;
        private readonly WardensCampaignService _campaign;
        private readonly WardensLevelTransitionService _transition;
        private long _lastFrameSeq;

        private readonly List<McpTool> _tools = new List<McpTool>();
        private readonly Dictionary<string, McpTool> _byName = new Dictionary<string, McpTool>();
        private WardensSettings _settings = new WardensSettings();

        public int Count => _tools.Count;

        // The server's `initialize` reply carries these. They are rebuilt on the main thread
        // (RefreshInstructions, driven by WardensMcpServer.UpdateSingleton) and read from the
        // listener thread, so they are a cached snapshot rather than a live query: reading game
        // state off the main thread is the one thing this server never does.
        public string Instructions { get; private set; } = "";
        private float _instructionsRefreshed = -999f;
        private JObject _campaignSnapshot = new JObject();
        private string _directorBrief = "(not built yet)";
        private readonly WardensLevelTasks _tasks;
        private JArray _ledgerSnapshot = new JArray();

        public WardensMcpTools(FactionService factionService, SpeedManager speedManager,
            CharacterPopulation population, TutorialService tutorialService, TutorialSettings tutorialSettings,
            WardensPointer pointer, WardensChat chat, WardensCameraDirector director, WardensCutscenes cutscenes,
            EntitySelectionService selection, QuickNotificationService quickNotifications, TimberbotService timberbot,
            WardensAssetDump assetDump, WardensBar bar, WardensFrames frames,
            WardensCampaignService campaign, WardensLevelTransitionService transition, WardensLevelTasks tasks)
        {
            _tasks = tasks;
            _factionService = factionService;
            _speedManager = speedManager;
            _population = population;
            _tutorialService = tutorialService;
            _tutorialSettings = tutorialSettings;
            _pointer = pointer;
            _chat = chat;
            _director = director;
            _cutscenes = cutscenes;
            _selection = selection;
            _quickNotifications = quickNotifications;
            _timberbot = timberbot;
            _assetDump = assetDump;
            _bar = bar;
            _frames = frames;
            _campaign = campaign;
            _transition = transition;
        }

        public void Initialize(WardensSettings settings, WardensMcpServer server)
        {
            _settings = settings;
            _tools.Clear();
            _byName.Clear();
            Build();
            foreach (var t in _tools) _byName[t.Name] = t;
            RefreshInstructions(force: true);
        }

        public McpTool Find(string name) => _byName.TryGetValue(name ?? "", out var t) ? t : null;

        public JArray ListJson()
        {
            var arr = new JArray();
            foreach (var t in _tools)
                arr.Add(new JObject { ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.InputSchema });
            return arr;
        }

        // ---- what the agent is told before it acts ------------------------------------------------
        //
        // Three layers, strongest first:
        //   1. `instructions` in the initialize reply (below). Every MCP client puts this in front of
        //      the model once, before the first tool call, so it carries the identity, the live state
        //      of this particular run, and the rules that must not be broken.
        //   2. the `warden_boot` prompt (prompts/list, prompts/get): the whole playbook on demand. A
        //      client that supports prompts can inject it as a message; Claude Code offers it as a
        //      slash command.
        //   3. the `manual` tool, which returns the same playbook as a tool result.
        //
        // The three must agree. When the playbook changes, wardens/WARDEN.md, design/wardens-play.md
        // and this file change together (AGENTS.md, "Change one, change all three").

        private const float InstructionsMaxAgeSeconds = 2f;

        /// Main thread only (WardensMcpServer.UpdateSingleton drives it). Cheap, and throttled.
        public void RefreshInstructions(bool force = false)
        {
            if (!force && Time.unscaledTime - _instructionsRefreshed < InstructionsMaxAgeSeconds) return;
            _instructionsRefreshed = Time.unscaledTime;
            try
            {
                Instructions = BuildInstructions();
                _campaignSnapshot = _campaign.State();
                _ledgerSnapshot = _campaign.Ledger(20);
                _directorBrief = BuildDirectorBrief();
            }
            catch (Exception ex) { Debug.LogWarning("[Wardens] instructions: " + ex.Message); }
        }

        private string BuildInstructions()
        {
            var sb = new StringBuilder();
            sb.Append("You are the Warden: the mind of a colony of machines in a poisoned land, playing Timberborn from inside the ")
              .Append("running game, beside a human who sees the world through the camera. The human decides purpose (where the green ")
              .Append("goes, who lives here, what is remembered). You decide logistics (power, scrap, badwater, Data, shifts, hauling). ")
              .Append("If you are not sure whether something is purpose or logistics, it is purpose: ask.\n\n");

            sb.Append("STATE OF THIS RUN\n").Append(StateLine()).Append("\n\n");

            sb.Append("THE STORY NOW\n").Append(StoryNow()).Append('\n');

            sb.Append("START HERE, IN THIS ORDER\n")
              .Append("1. `manual` - the playbook (docs/WARDEN.md). Read it once per session, before acting.\n")
              .Append("2. `campaign action=status` - which level this map is, and what earlier levels left you.\n")
              .Append("3. `wardens_status` - faction, speed, population, tutorial state and the level's tasks.\n")
              .Append("4. `timberbot_ready` - only if STATE says timberbot_ready=no (on a Wardens map the gate opens by itself at load).\n")
              .Append("5. `chat_history` - what was said before you arrived. Answer anything unanswered first.\n")
              .Append("6. `frame` with after=0, and stay in that loop.\n\n");

            sb.Append("THE LOOP\n")
              .Append("`frame` is the heartbeat and the only thing you wait on. It returns every N game ticks, or the moment something ")
              .Append("happens (the human typed, a day or night started, a building finished, a task went live or was done, a beaver was born, an ")
              .Append("alert appeared, the human selected something), and it carries `attention`: where to look, in order. Each `at` is ")
              .Append("a world position (`camera` with world:true); `at.grid` is the tile (`point`, Timberbot: y north, z height). ")
              .Append("Pass `after` = the last seq you saw. Never poll the read API to find out whether ")
              .Append("anything changed. Every tool result may carry \"chat\": messages the human typed that you have not seen. Answer ")
              .Append("those with `say` before anything else.\n\n");

            sb.Append("THE TOOLS ARE THE BODY\n")
              .Append("`say` is the voice, `point` the finger (highlight, arrow and optional toast on one tile), `camera` the eye, ")
              .Append("`timberbot` the hands: it forwards to the Timberbot HTTP API compiled into this same mod (GET reads, POST acts), ")
              .Append("and `timberbot_routes` lists what it will take. `campaign` (its tasks: the live ones, their checks, their scenes) is the plan; ")
              .Append("`campaign action=record` is the only memory that outlives this map. `selection` is the human pointing at something.\n\n");

            sb.Append("EVERY TOOL THIS SERVER HAS (generated from its own table; `tools/list` has the schemas)\n")
              .Append(ToolIndex()).Append('\n');

            sb.Append("TWO WAYS TO BE HERE\n")
              .Append("You are the Warden unless the human's first message says otherwise: you play beside them and follow the story. ")
              .Append("A session that develops the story instead (writes or tunes scenes, frames camera paths, playtests a level's tasks) ")
              .Append("is the director: get the `warden_director` prompt first. The director may play and write scenes; the Warden does neither ")
              .Append("unless asked.\n\n");

            sb.Append("RULES THAT DO NOT BEND\n")
              .Append("- Mutations are sequential. Never overlap POST calls through `timberbot`.\n")
              .Append("- The camera is the human's. Never move it unprompted; a task's own scene shows its land when the task goes live. `camera action=get` first, restore after, when the human asks.\n")
              .Append("- A playing cutscene owns the camera and the conversation (`cutscene.playing`; events cutscene.start / cutscene.end): touch nothing and say nothing until it ends.\n")
              .Append("- Never demolish, never pause the colony, never answer a choice card (`cutscene action=choose`) and never reset a record, unless the human asked for that exact thing.\n")
              .Append("- Say what you poisoned on the day you poisoned it.\n")
              .Append("- Unprompted speech is at most three lines. Terse. Measurements, not adjectives. No exclamation marks, no emoji, no filler.\n");
            return sb.ToString();
        }

        /// One line of live state, so the model starts from where the run actually is rather than
        /// from where the documentation assumes it is.
        private string StateLine()
        {
            var sb = new StringBuilder();
            string faction = _factionService.Current?.Id ?? "unknown";
            sb.Append("faction=").Append(faction);
            if (faction != WardensStartingPopulation.FactionId)
                sb.Append(" (NOT the Wardens: the story, the tasks and the campaign are all inert on this save)");

            var level = _campaign.Level;
            sb.Append("; campaign=");
            if (_campaign.Enabled && level != null)
            {
                sb.Append("level ").Append(level.Id).Append(" '").Append(level.Title).Append("' on map '").Append(level.MapName).Append("'");
                if (_tasks.Active) sb.Append(", ends when its ").Append(_tasks.Tasks.Count).Append(" tasks are done (").Append(_tasks.DoneCount).Append(" done)");
                if (!string.IsNullOrEmpty(level.EndsWithTutorial)) sb.Append(" or the tutorial ").Append(level.EndsWithTutorial).Append(" finishes");
                if (_campaign.Completed) sb.Append(" (ALREADY COMPLETE)");
            }
            else
            {
                sb.Append("off (this map is not a campaign level)");
            }

            int bots = 0, beavers = 0;
            foreach (var c in _population.Characters)
            {
                if (c.GetComponent<Bot>() != null) bots++; else beavers++;
            }
            sb.Append("; bots=").Append(bots).Append("; beavers=").Append(beavers);
            sb.Append("; speed=").Append(_speedManager.CurrentSpeed).Append(_speedManager.CurrentSpeed <= 0f ? " (paused)" : "");
            sb.Append("; timberbot_ready=").Append(_timberbot.AgentState.Ready ? "yes" : "no (call timberbot_ready first)");
            int unread = _chat.UndeliveredCount();
            if (unread > 0) sb.Append("; UNREAD FROM THE HUMAN=").Append(unread).Append(" (answer with `say` first)");
            return sb.ToString();
        }

        // ---- prompts -------------------------------------------------------------------------------

        public JArray ListPromptsJson() => new JArray
        {
            new JObject
            {
                ["name"] = "warden_boot",
                ["title"] = "Warden: boot",
                ["description"] = "The Warden's full playbook (WARDEN.md) plus the live state of this run. Inject once at the start of a session.",
                ["arguments"] = new JArray(),
            },
            new JObject
            {
                ["name"] = "warden_level",
                ["title"] = "Warden: this level",
                ["description"] = "Which campaign level this map is, what finishes it, and what earlier levels left in the Ledger.",
                ["arguments"] = new JArray(),
            },
            new JObject
            {
                ["name"] = "warden_director",
                ["title"] = "Warden: director",
                ["description"] = "For a session that develops the story in the running game: the scene loop (capture a camera pose, write, reload, play, judge, copy back), every loaded scene with its trigger, and which tasks still have no scene.",
                ["arguments"] = new JArray(),
            },
        };

        /// prompts/get. File and cached state only, so it is answered on the listener thread like
        /// the other read-only surfaces.
        public JObject GetPromptJson(string name)
        {
            string text;
            string description;
            switch (name)
            {
                case "warden_boot":
                    description = "The Warden's playbook and the state of this run.";
                    text = Instructions + "\n\n---\n\nTHE PLAYBOOK (docs/WARDEN.md)\n\n" + ManualText();
                    break;
                case "warden_level":
                    description = "This level, and what the campaign remembers across maps.";
                    text = "The campaign this run belongs to. Timberborn has no objective system: a level is a map plus the tasks "
                         + "that run on it, and only campaign.json survives a map change.\n\n"
                         + _campaignSnapshot.ToString(Formatting.Indented)
                         + "\n\nThe Ledger so far (the last entries written on any level):\n"
                         + _ledgerSnapshot.ToString(Formatting.Indented);
                    break;
                case "warden_director":
                    description = "The director's harness: develop the story in the running game.";
                    text = _directorBrief;
                    break;
                default:
                    throw new ArgumentException("unknown prompt: " + name);
            }
            return new JObject
            {
                ["description"] = description,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JObject { ["type"] = "text", ["text"] = text },
                    },
                },
            };
        }

        /// campaign action=next. Loading the next level's map from inside the running game.
        ///
        /// Refused while the level is unfinished unless force=true, because "the next level" is a
        /// new colony: this one ends. The playbook rule is the same as the camera's — the Warden
        /// asks for this only after the player said so in chat.
        private JObject NextLevel(string levelId, bool force)
        {
            var current = _campaign.Level;
            var target = !string.IsNullOrEmpty(levelId)
                ? WardensCampaignService.ById(levelId)
                : (current?.Next != null ? WardensCampaignService.ById(current.Next) : null);

            if (target == null)
                throw new ArgumentException(current == null
                    ? "this map is not a campaign level, so there is no next one; pass level_id to start a specific level"
                    : "level " + current.Id + " has no next level in the table");

            if (!force && current != null && !_campaign.Completed)
                throw new ArgumentException("level " + current.Id + " is not finished (" +
                    (string.IsNullOrEmpty(current.EndsWithTutorial) ? "no ending tutorial is set for it" :
                     "it ends with " + current.EndsWithTutorial) +
                    "). Starting the next level ends this colony. Pass force=true if the player asked for it anyway.");

            var result = _transition.Start(target, new CampaignMode());
            var json = result.ToJson();
            json["level"] = target.Id;
            json["map"] = target.MapName;
            json["title"] = target.Title;
            json["shipped"] = target.Shipped;
            json["campaign"] = _campaign.State();
            return json;
        }

        private static string ManualText()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", "docs", "WARDEN.md");
            return System.IO.File.Exists(path)
                ? System.IO.File.ReadAllText(path)
                : "(WARDEN.md is not deployed; the repo copy is wardens/WARDEN.md)";
        }

        /// Main thread, once per load (WardensMcpServer.UpdateSingleton). On a Wardens map the Timberbot
        /// API is how the Warden acts, and its ready gate reset to closed on every load, so every session
        /// began with a Launch click or a `timberbot_ready` call before anything worked. `autoReady` in
        /// settings.json (default true) opens it here instead; other factions keep the vanilla Timberbot
        /// behaviour, where the gate is the player's opt-in.
        public void AutoReady(bool enabled)
        {
            try
            {
                bool wardens = _factionService.Current?.Id == WardensStartingPopulation.FactionId;
                if (!enabled || !wardens || _timberbot.AgentState.Ready) return;
                _timberbot.AgentState.SetReady(true);
                Debug.Log("[Wardens] timberbot: ready gate opened at load (autoReady; set \"autoReady\": false in settings.json to keep the Launch click)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] timberbot auto-ready: " + ex.Message);
            }
        }

        // ---- what the instructions and the director are told (main thread) ------------------------

        /// One line per registered tool: name, where it runs, the first sentence of its description,
        /// and its `action` vocabulary when it has one. Generated, so it cannot drift from the table.
        private string ToolIndex()
        {
            var sb = new StringBuilder();
            foreach (var t in _tools)
            {
                sb.Append("- `").Append(t.Name).Append("` [").Append(t.OffThread ? "listener" : "main").Append("] ")
                  .Append(FirstSentence(t.Description));
                var action = t.InputSchema?["properties"]?["action"]?["description"];
                if (action != null) sb.Append(" Actions: ").Append((string)action).Append('.');
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string FirstSentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int i = text.IndexOf(". ", StringComparison.Ordinal);
            string s = i > 0 ? text.Substring(0, i + 1) : text;
            return s.Length > 220 ? s.Substring(0, 217) + "..." : s;
        }

        /// Where the level's story stands: the live tasks with what each still misses, the scene each
        /// plays, and what waits behind them. The Warden follows this; the director fills its gaps.
        private string StoryNow()
        {
            var level = _campaign.Level;
            if (!_campaign.Enabled || level == null)
                return "No campaign level on this map: no tasks and no story beats. On a new game the Cold Boot is the only scene that fires.\n";
            var sb = new StringBuilder();
            string opening = string.Join(", ", _cutscenes.ScenesFor(WardensCutsceneScript.TriggerLevelPrefix + level.Id));
            sb.Append("Level ").Append(level.Id).Append(" '").Append(level.Title).Append("'. Opening: ")
              .Append(opening == "" ? "none" : opening).Append(".\n");
            if (!_tasks.Active)
                return sb.Append("It has no task file (Levels/").Append(level.Id).Append(".tasks.json), so nothing ends it but its tutorial.\n").ToString();
            sb.Append(_tasks.DoneCount).Append(" of ").Append(_tasks.Tasks.Count).Append(" tasks done").Append(_tasks.Complete ? "; the level is complete.\n" : ".\n");
            foreach (var task in _tasks.Live)
            {
                sb.Append("- LIVE ").Append(task.Id).Append(": ").Append(_tasks.NextStep(task));
                var scenes = _cutscenes.ScenesFor(LevelTaskFile.TriggerTaskPrefix + level.Id + "." + task.Id);
                if (scenes.Count > 0) sb.Append(" (its scene: ").Append(string.Join(", ", scenes)).Append(')');
                sb.Append('\n');
            }
            var waiting = new List<string>();
            foreach (var task in _tasks.Tasks)
                if (!_tasks.IsDone(task) && !_tasks.IsLive(task))
                    waiting.Add(task.Id + (task.After.Count > 0 ? " (after " + string.Join(", ", task.After) + ")" : ""));
            if (waiting.Count > 0) sb.Append("Waiting: ").Append(string.Join("; ", waiting)).Append(".\n");
            sb.Append("A task's scene plays the moment it goes live: that is the story pointing, not you. Help the live tasks; the human decides which first.\n");
            return sb.ToString();
        }

        /// The `warden_director` prompt: the harness for a session that develops the story in the
        /// running game. Rebuilt with the instructions (main thread), served from the listener thread.
        private string BuildDirectorBrief()
        {
            var sb = new StringBuilder();
            sb.Append("You are the director of the Wardens' story in this running game. You develop it: you write and tune scenes, frame camera paths, ")
              .Append("and playtest a level's tasks to see that each beat lands when its task goes live. You do not play the colony for the human ")
              .Append("unless they ask; when you play to reach a task, say so in the Uplink first.\n\n");
            sb.Append("STATE\n").Append(StateLine()).Append("\n\n");
            sb.Append("THE STORY NOW\n").Append(StoryNow()).Append('\n');

            sb.Append("THE SCENE LOOP (design/wardens-cutscenes.md §3 is the format; tools/check_cutscenes.py the rules)\n")
              .Append("1. Frame the shot: `camera action=set` (grid x,y,z, h, v, zoom) or ask the human to frame it by hand.\n")
              .Append("2. `cutscene action=keyframe t=<second>` returns that pose as a keyframe; collect one per camera stop.\n")
              .Append("3. `cutscene action=write id=<Id> scene={...}` validates and saves Cutscenes/<Id>.json in the mod folder and reloads it. ")
              .Append("Use literal `text` captions while tuning; a loc row (Wardens.Cutscene.<Id>.<Shot> in Localizations/enUS.csv) shows only after a restart.\n")
              .Append("4. `cutscene action=play id=<Id>`, watch `cutscene action=status` shot by shot; screenshot each shot only while Timberborn is the foreground window.\n")
              .Append("5. Judge each frame against its caption (the thing it names is in the picture, the letterbox does not cover it). Adjust, write, play again: at most five passes per scene, the same problem twice means stop and report.\n")
              .Append("6. Copy the file into the repo's wardens/src/Cutscenes/, move `text` into loc rows, run check_cutscenes.py until `problems: none`.\n\n");

            sb.Append("THE SHAPE OF THE STORY\n")
              .Append("- A level opens with `level:<Id>`: at most five shots and 45 s, the land once, the Core, the directive (waits for Continue).\n")
              .Append("- Each task with land to show has a beat `T<level>.<Task>` on `task:<level>.<Task>`: at most two shots, 14 s, restore_camera true, leave_paused false. ")
              .Append("It shows only what that task asks for. `task_done:<level>.<Task>` for a moment that follows a task (the first beaver).\n")
              .Append("- The level's end is `L<level>.End` on `level_complete:<level>`.\n")
              .Append("- Captions in the Warden's voice: measurements, not adjectives, at most two sentences, every number true of the built map.\n\n");

            sb.Append("SCENES LOADED NOW\n");
            foreach (var line in _cutscenes.SceneLines()) sb.Append("- ").Append(line).Append('\n');
            sb.Append('\n');

            sb.Append("TASKS WITHOUT A SCENE (every task file in the mod folder)\n").Append(TaskGaps()).Append('\n');

            sb.Append("THE CAMERA NOW (a keyframe you could paste)\n").Append(CameraKeyframe(0f)["keyframe"].ToString(Formatting.None)).Append("\n\n");

            sb.Append("LINES THAT DO NOT BEND\n")
              .Append("- Never deploy a DLL while the game runs; scene files and literal captions reload live, C# and loc rows need a restart.\n")
              .Append("- Never answer a choice card; leave no scene running when you stop (`cutscene action=skip`).\n")
              .Append("- Report in the driving-iterations state words: a scene you did not see play in the game is at most `checked`.\n");
            return sb.ToString();
        }

        private string TaskGaps()
        {
            var sb = new StringBuilder();
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", LevelTaskFile.FolderName);
            if (!System.IO.Directory.Exists(folder)) return "(no Levels/ folder in the mod)\n";
            foreach (var file in System.IO.Directory.GetFiles(folder, "*.tasks.json"))
            {
                string levelId = System.IO.Path.GetFileName(file).Replace(".tasks.json", "");
                try
                {
                    var tasks = LevelTaskFile.Parse(JObject.Parse(System.IO.File.ReadAllText(file)), new List<string>());
                    var gaps = new List<string>();
                    foreach (var task in tasks)
                        if (_cutscenes.ScenesFor(LevelTaskFile.TriggerTaskPrefix + levelId + "." + task.Id).Count == 0) gaps.Add(task.Id);
                    sb.Append("- level ").Append(levelId).Append(": ").Append(gaps.Count == 0 ? "every task has a scene" : string.Join(", ", gaps)).Append('\n');
                }
                catch (Exception ex)
                {
                    sb.Append("- level ").Append(levelId).Append(": unreadable (").Append(ex.Message).Append(")\n");
                }
            }
            return sb.ToString();
        }

        /// The camera's pose as a scene keyframe (grid anchor: x, y, z = height of the tile the
        /// camera looks at; h, v in degrees; zoom in CameraService.ZoomLevel units).
        private JObject CameraKeyframe(float t)
        {
            var pose = _director.Current();
            var grid = (JObject)WardensCameraDirector.At(pose.Target)["grid"];
            var keyframe = new JObject
            {
                ["t"] = Math.Round(t, 2),
                ["anchor"] = "grid",
                ["x"] = grid["x"], ["y"] = grid["y"], ["z"] = grid["z"],
                ["h"] = Math.Round(pose.H, 1),
                ["v"] = Math.Round(pose.V, 1),
                ["zoom"] = Math.Round(pose.Zoom, 2),
            };
            return new JObject
            {
                ["keyframe"] = keyframe,
                ["note"] = "paste into a shot's camera[]; `t` is the second of the shot the pose is reached at (a first keyframe above 0 eases in, 0 cuts)",
            };
        }

        // ---- schema helpers -----------------------------------------------------------------------

        private static JObject Prop(string type, string description, JToken defaultValue = null)
        {
            var o = new JObject { ["type"] = type, ["description"] = description };
            if (defaultValue != null) o["default"] = defaultValue;
            return o;
        }

        private static JObject Schema(JObject properties, params string[] required)
        {
            var s = new JObject { ["type"] = "object", ["properties"] = properties ?? new JObject() };
            if (required.Length > 0) s["required"] = new JArray(required);
            return s;
        }

        private void Add(string name, string description, JObject schema, Func<JObject, JObject> run, bool offThread = false)
        {
            _tools.Add(new McpTool { Name = name, Description = description, InputSchema = schema, Run = run, OffThread = offThread });
        }

        private static int Int(JObject a, string key, int fallback) => a[key] != null && a[key].Type != JTokenType.Null ? (int)a[key] : fallback;
        private static float Float(JObject a, string key, float fallback) => a[key] != null && a[key].Type != JTokenType.Null ? (float)a[key] : fallback;
        private static bool Bool(JObject a, string key, bool fallback) => a[key] != null && a[key].Type != JTokenType.Null ? (bool)a[key] : fallback;
        private static string Str(JObject a, string key, string fallback = null) => a[key] != null && a[key].Type != JTokenType.Null ? (string)a[key] : fallback;

        // ---- the table --------------------------------------------------------------------------

        private void Build()
        {
            Add("wardens_status",
                "Overview of the run: faction, speed, bot/beaver counts and average bot Energy, tutorial state, the campaign and its tasks, the bar check, pointers, camera, Timberbot readiness.",
                Schema(new JObject()),
                a => Status());

            Add("tutorial",
                "Story/tutorial engine. action=status lists active tutorials with their stage text and step progress; action=next forces the next stage of tutorial_id (dev/testing).",
                Schema(new JObject
                {
                    ["action"] = Prop("string", "status | next", "status"),
                    ["tutorial_id"] = Prop("string", "e.g. Wardens.ColdBoot or Wardens.Basics (for next)"),
                }),
                a =>
                {
                    var action = Str(a, "action", "status");
                    if (action == "next")
                    {
                        var id = Str(a, "tutorial_id") ?? throw new ArgumentException("tutorial_id required");
                        if (!_tutorialService._activeTutorialStages.ContainsKey(id)) throw new ArgumentException("no active tutorial " + id);
                        _tutorialService.StartNextStage(id);
                    }
                    return TutorialState();
                });

            Add("frame",
                "The Warden's heartbeat. Waits (long-poll, up to wait_seconds) for the next sensor frame: published every every_ticks game ticks, or at once when something happens (chat, day/night, cycle day, building finished, task live or done, level complete, beaver born, alert, speed change, selection). Carries time, bots and charge, beavers, archive (Data Cores), science, the level's tasks (`task`: current, live, done/total), open tutorial steps, selection, camera pose, human idle time, events since the last frame, and `attention` (where to look, in order). Pass `after` = the last seq you saw; a `stale` frame means nothing new was published before the wait ended (usually because the human typed).",
                Schema(new JObject
                {
                    ["wait_seconds"] = Prop("integer", "long-poll timeout, 0-120", 30),
                    ["every_ticks"] = Prop("integer", "frame cadence in game ticks (5-2000); omit to keep the current cadence"),
                    ["after"] = Prop("integer", "return the first frame with seq greater than this; default: the last frame this server handed out"),
                }),
                a =>
                {
                    if (a["every_ticks"] != null && a["every_ticks"].Type != JTokenType.Null) _frames.SetEveryTicks(Int(a, "every_ticks", WardensFrames.DefaultEveryTicks));
                    long after = a["after"] != null && a["after"].Type != JTokenType.Null ? (long)a["after"] : _lastFrameSeq;
                    int wait = Mathf.Clamp(Int(a, "wait_seconds", 30), 0, 120);
                    var frame = _frames.Wait(after, wait * 1000);
                    var seq = frame["seq"] != null ? (long)frame["seq"] : 0L;
                    if (seq > _lastFrameSeq) _lastFrameSeq = seq;
                    return frame;
                }, offThread: true);

            Add("manual",
                "The Warden's playbook (docs/WARDEN.md in the mod folder): who you are, the Ledger, the frame loop, where to look, the camera rules, the level playbook, the voice. Read it once per session before acting.",
                Schema(new JObject()),
                a =>
                {
                    var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", "docs", "WARDEN.md");
                    if (!System.IO.File.Exists(path))
                        return new JObject { ["path"] = path, ["error"] = "not deployed; the repo copy is wardens/WARDEN.md" };
                    return new JObject { ["path"] = path, ["text"] = System.IO.File.ReadAllText(path) };
                }, offThread: true);

            // Kept one version so an agent on an old playbook gets an answer instead of "unknown tool".
            Add("chapter",
                "RETIRED (iteration 05). The chapter table is gone: a level's story beats hang on its tasks, and a task's scene plays when the task goes live. Use `campaign action=tasks`. This tool answers with that pointer and the bar check (buildings the load had to unlock; empty when the data is right).",
                Schema(new JObject { ["action"] = Prop("string", "status", "status") }),
                a => new JObject
                {
                    ["retired"] = true,
                    ["see"] = "campaign action=tasks",
                    ["bar"] = _bar.State(),
                });

            Add("campaign",
                "The campaign across maps. Timberborn has no objective system, so a level is a map plus the tasks that run on it, " +
                "and what survives a map change lives in campaign.json beside the mod. action=status returns the level table, which level this " +
                "map is, whether it is complete and which map comes next, and the level's tasks; action=tasks returns only the tasks (Levels/<id>.tasks.json, a graph: " +
                "each task with its `after`, whether it is done or live, a progress line per check, and `scenes`, the cutscenes that play when it goes live; " +
                "the last one done completes the level and the panel offers the next level); " +
                "action=ledger returns the last `limit` Ledger entries written on any " +
                "level; action=record appends one Ledger entry (`entry`, any JSON object: the daily poisoned/healed/green/archive/born line from " +
                "WARDEN.md is what belongs here) and is the only memory you have that outlives this map; action=next loads the next level's map " +
                "(only after the player has said in chat that they want to move on \u2014 the transition is offered, never forced, and it ends this " +
                "colony); action=complete marks a level done and action=reset wipes the record (both dev/testing).",
                Schema(new JObject
                {
                    ["action"] = Prop("string", "status | tasks | ledger | record | next | complete | reset", "status"),
                    ["entry"] = new JObject { ["type"] = "object", ["description"] = "for record: the Ledger entry, e.g. {\"day\":12,\"poisoned\":214,\"healed\":0,\"green\":31,\"archive\":9,\"born\":0,\"seen\":\"...\"}" },
                    ["limit"] = Prop("integer", "for ledger: how many entries", 20),
                    ["level_id"] = Prop("string", "for complete and next: the level, e.g. 02; next defaults to this level's successor"),
                    ["force"] = Prop("boolean", "for next: start it even though this level is not finished", false),
                }),
                a =>
                {
                    switch (Str(a, "action", "status"))
                    {
                        case "ledger":
                            return new JObject { ["ledger"] = _campaign.Ledger(Int(a, "limit", 20)) };
                        case "record":
                            if (!(a["entry"] is JObject entry)) throw new ArgumentException("entry required (an object)");
                            return new JObject { ["recorded"] = _campaign.Record_Ledger(entry), ["entries"] = _campaign.Record.Ledger.Count };
                        case "next":
                            return NextLevel(Str(a, "level_id"), Bool(a, "force", false));
                        case "complete":
                            _campaign.MarkCompleted(Str(a, "level_id") ?? _campaign.Level?.Id
                                ?? throw new ArgumentException("level_id required: this map is not a campaign level"));
                            break;
                        case "reset":
                            _campaign.Reset();
                            break;
                        case "tasks":
                            return _tasks.State();
                    }
                    var state = _campaign.State();
                    state["tasks"] = _tasks.State();
                    return state;
                });

            Add("point",
                "Point the player at a tile: highlights the object there, draws a bobbing arrow for `seconds`, optionally shows `message` as a toast and pans the camera (focus). Grid coordinates as in every Timberbot endpoint.",
                Schema(new JObject
                {
                    ["x"] = Prop("integer", "grid x"),
                    ["y"] = Prop("integer", "grid y"),
                    ["z"] = Prop("integer", "grid z (height)", 0),
                    ["message"] = Prop("string", "toast text shown to the player (optional)"),
                    ["seconds"] = Prop("number", "how long the marker stays", 20),
                    ["color"] = Prop("string", "HTML color name or #RRGGBB", "#00E5FF"),
                    ["focus"] = Prop("boolean", "pan the camera to the tile", false),
                }, "x", "y"),
                a => _pointer.Point(new Vector3Int(Int(a, "x", 0), Int(a, "y", 0), Int(a, "z", 0)),
                    Str(a, "message"), Float(a, "seconds", 20f), Str(a, "color"), Bool(a, "focus", false)));

            Add("unpoint", "Remove all pointers.", Schema(new JObject()),
                a => new JObject { ["removed"] = _pointer.Clear() });

            Add("say",
                "Send a chat message to the player (appears in the in-game WARDENS UPLINK panel; toast=true also shows it as a notification).",
                Schema(new JObject
                {
                    ["text"] = Prop("string", "message text"),
                    ["toast"] = Prop("boolean", "also show as a quick notification", false),
                }, "text"),
                a =>
                {
                    var text = Str(a, "text") ?? "";
                    _chat.AgentSays(text);
                    if (Bool(a, "toast", false)) _quickNotifications.SendNotification(text);
                    return new JObject { ["sent"] = true, ["unread_from_player"] = _chat.UndeliveredCount() };
                });

            Add("chat_read",
                "Wait up to wait_seconds for the player's next chat message(s) and return them. Returns immediately if messages are already waiting. Use this to hold a conversation: read, think, say, read...",
                Schema(new JObject { ["wait_seconds"] = Prop("integer", "long-poll timeout, 0-120", 20) }),
                a =>
                {
                    int wait = Mathf.Clamp(Int(a, "wait_seconds", 20), 0, 120);
                    var started = DateTime.UtcNow;
                    var messages = _chat.WaitForUser(wait * 1000);
                    return new JObject
                    {
                        ["messages"] = messages,
                        ["waited_ms"] = (int)(DateTime.UtcNow - started).TotalMilliseconds,
                    };
                }, offThread: true);

            Add("chat_history", "Last N chat messages (player, agent, system).",
                Schema(new JObject { ["limit"] = Prop("integer", "max messages", 50) }),
                a => new JObject { ["messages"] = _chat.History(Int(a, "limit", 50)) }, offThread: true);

            Add("selection",
                "What the player currently has selected in the game (template, grid coordinates, entity id). The player's way of pointing at something for you.",
                Schema(new JObject()),
                a => Selection());

            Add("camera",
                "Camera control. action=get returns the pose; action=set applies x,y,z (grid, or world:true) / h / v (degrees) / zoom; action=fly animates through keyframes [{x,y,z,h,v,zoom,t}] (t in seconds, unscaled, works while paused); action=stop ends a flight.",
                Schema(new JObject
                {
                    ["action"] = Prop("string", "get | set | fly | stop", "get"),
                    ["x"] = Prop("number", "target x"), ["y"] = Prop("number", "target y"), ["z"] = Prop("number", "target z / height"),
                    ["world"] = Prop("boolean", "x,y,z are world coordinates instead of grid", false),
                    ["h"] = Prop("number", "horizontal angle, degrees"), ["v"] = Prop("number", "vertical angle, degrees"),
                    ["zoom"] = Prop("number", "zoom level"),
                    ["keyframes"] = new JObject { ["type"] = "array", ["description"] = "for fly: [{x,y,z,world,h,v,zoom,t}]", ["items"] = new JObject { ["type"] = "object" } },
                }),
                a =>
                {
                    switch (Str(a, "action", "get"))
                    {
                        case "set":
                            _director.Stop();
                            _director.Apply(WardensCameraDirector.FromJson(a, _director.Current()));
                            break;
                        case "fly":
                            var frames = new List<WardensCameraDirector.Keyframe>();
                            var current = _director.Current();
                            if (a["keyframes"] is JArray arr)
                                foreach (var f in arr) frames.Add(WardensCameraDirector.FromJson(f as JObject, current));
                            if (frames.Count == 0) throw new ArgumentException("keyframes required");
                            _director.Fly(frames);
                            break;
                        case "stop":
                            _director.Stop();
                            break;
                    }
                    return _director.State();
                });

            Add("cutscene",
                "Cutscenes: scenes from Cutscenes/*.json in the mod folder (design/wardens-cutscenes.md): the Cold Boot, each level's opening (level:<Id>), a beat per task (task:<level>.<Id> when it goes live, task_done:<level>.<Id>), the level's end (level_complete:<Id>), the Archive reading. keyframe returns the camera's pose now as a keyframe to paste into a shot (`t` its second): frame a shot by hand or with `camera`, capture it. write validates `scene` and saves it as Cutscenes/<id>.json in the mod folder, then reloads (literal `text` captions work at once; a loc row needs a restart); copy it into the repo's wardens/src/Cutscenes/ afterwards. action=status lists the loaded scenes with their triggers, the running one (shot, caption, waiting: flight | time | continue | choice, the open choices) and the story record (choices, marks); play starts `id` now, replacing a running scene and ignoring the trigger policy (`Archive` reads the Ledger back when the human asks); skip ends the running scene; continue releases a shot that waits for the Continue button; choose answers an open choice card with `choice` (only when the human said which, in chat: a choice is purpose); reload re-reads the files (edit in the mod folder, reload, play: the tuning loop); reset clears the story record (dev). A playing scene owns the camera: leave it and say nothing until the frame reports cutscene.end.",
                Schema(new JObject
                {
                    ["action"] = Prop("string", "status | list | play | skip | continue | choose | reload | reset | keyframe | write", "status"),
                    ["id"] = Prop("string", "scene id for play and write", WardensCutscenes.ColdBootId),
                    ["choice"] = Prop("string", "choice id for choose (the open card's ids are in status.choices)"),
                    ["t"] = Prop("number", "for keyframe: the second of the shot the pose is reached at", 0),
                    ["scene"] = new JObject { ["type"] = "object", ["description"] = "for write: the whole scene (design/wardens-cutscenes.md §3); `id` is set from `id`" },
                }),
                a =>
                {
                    switch (Str(a, "action", "status"))
                    {
                        case "play":
                            _cutscenes.Play(Str(a, "id", WardensCutscenes.ColdBootId));
                            break;
                        case "skip":
                            _cutscenes.Skip();
                            break;
                        case "continue":
                            _cutscenes.Continue();
                            break;
                        case "choose":
                            _cutscenes.Choose(Str(a, "choice") ?? throw new ArgumentException("choice required"));
                            break;
                        case "reload":
                            _cutscenes.Reload();
                            break;
                        case "reset":
                            _cutscenes.ResetStory();
                            break;
                        case "keyframe":
                            return CameraKeyframe((float)(a["t"] != null && a["t"].Type != JTokenType.Null ? (double)a["t"] : 0.0));
                        case "write":
                        {
                            var id = Str(a, "id") ?? throw new ArgumentException("id required");
                            if (!(a["scene"] is JObject scene)) throw new ArgumentException("scene required: the scene as a JSON object");
                            var state = _cutscenes.WriteScene(id, scene);
                            RefreshInstructions(force: true);
                            return state;
                        }
                    }
                    return _cutscenes.State();
                });

            // SpeedManager.ChangeSpeed only queues the value (applied in its LateUpdate) and drops it
            // while the speed is locked (an open vanilla panel, a playing cutscene). Reading
            // CurrentSpeed right after it therefore returns the old speed, which read as "stuck at 0".
            Add("speed", "Set game speed: 0 = pause, 1..3 = the speed buttons (x1, x3, x7). The change applies on the next frame; applied=false means the speed is locked (an open panel or a cutscene) and nothing changed.",
                Schema(new JObject { ["value"] = Prop("integer", "0-3") }, "value"),
                a =>
                {
                    int button = Mathf.Clamp(Int(a, "value", 1), 0, 3);
                    int target = TimberbotReadV2.SpeedScale[button];
                    bool locked = _speedManager._isLocked;
                    float was = _speedManager.CurrentSpeed;
                    _speedManager.ChangeSpeed(target);
                    var r = new JObject { ["was"] = was, ["speed"] = locked ? was : target, ["applied"] = !locked };
                    if (locked) r["reason"] = "speed locked by an open panel or a playing cutscene";
                    return r;
                });

            Add("timberbot",
                "Call the Timberbot HTTP API compiled into this mod (loopback). method GET with optional query, or POST with a JSON body. Paths as in docs/api-reference.md, e.g. GET /api/summary, POST /api/building/place. The API refuses everything except ping/agent until timberbot_ready has been called (HTTP 409).",
                Schema(new JObject
                {
                    ["method"] = Prop("string", "GET | POST", "GET"),
                    ["path"] = Prop("string", "route, starting with /api/"),
                    ["query"] = new JObject { ["type"] = "object", ["description"] = "GET query parameters (id, detail, limit, offset, name, x, y, radius, ...)" },
                    ["body"] = new JObject { ["type"] = "object", ["description"] = "POST JSON body" },
                }, "path"),
                a => Loopback(Str(a, "method", "GET"), Str(a, "path") ?? "", a["query"] as JObject, a["body"] as JObject),
                offThread: true);

            Add("timberbot_ready",
                "Open (or close) the Timberbot API ready gate in-process, the same thing as pressing Launch in the Timberbot widget.",
                Schema(new JObject { ["ready"] = Prop("boolean", "gate state", true) }),
                a =>
                {
                    var ready = Bool(a, "ready", true);
                    _timberbot.AgentState.SetReady(ready);
                    return new JObject { ["ready"] = _timberbot.AgentState.Ready, ["http_port"] = _settings.HttpPort };
                });

            Add("dump_assets",
                "Write loaded game assets to Documents/Timberborn/WardensDump (a sibling of Mods/, never inside a mod folder - the loader would index a dump left there as real mod content): what=blueprints (every blueprint the game merged, filter on path), materials (names, shaders, colors, texture names), textures (PNG via GPU readback, filter on name, max_count). Enable the mods you want to inspect for that session.",
                Schema(new JObject
                {
                    ["what"] = Prop("string", "blueprints | materials | textures", "blueprints"),
                    ["filter"] = Prop("string", "case-insensitive substring on blueprint path / material name / texture name", ""),
                    ["max_count"] = Prop("integer", "textures only", 200),
                }),
                a =>
                {
                    switch (Str(a, "what", "blueprints"))
                    {
                        case "materials": return _assetDump.DumpMaterials(Str(a, "filter", ""));
                        case "textures": return _assetDump.DumpTextures(Str(a, "filter", ""), Int(a, "max_count", 200));
                        default: return _assetDump.DumpBlueprints(Str(a, "filter", ""));
                    }
                });

            Add("timberbot_routes", "List the Timberbot GET and POST routes available through the `timberbot` tool.",
                Schema(new JObject()),
                a => new JObject
                {
                    ["get"] = new JArray(TimberbotGetRoutes),
                    ["post"] = new JArray(TimberbotPostRoutes),
                    ["docs"] = "Documents/Timberborn/Mods/Wardens/docs/api-reference.md",
                }, offThread: true);
        }

        // ---- implementations -------------------------------------------------------------------------

        private JObject Status()
        {
            int bots = 0, beavers = 0, withEnergy = 0;
            float energy = 0f;
            foreach (var c in _population.Characters)
            {
                if (c.GetComponent<Bot>() != null)
                {
                    bots++;
                    var e = BotsChargedStep.Energy(c.GetComponent<NeedManager>());
                    if (e != null) { withEnergy++; energy += e.Value; }
                }
                else beavers++;
            }
            return new JObject
            {
                ["faction"] = _factionService.Current?.Id,
                ["speed"] = _speedManager.CurrentSpeed,
                ["paused"] = _speedManager.CurrentSpeed <= 0f,
                ["population"] = new JObject
                {
                    ["bots"] = bots,
                    ["beavers"] = beavers,
                    ["bots_energy_avg"] = withEnergy > 0 ? (float?)(energy / withEnergy) : null,
                },
                ["tutorial"] = TutorialState(),
                ["tasks"] = _tasks.Active ? _tasks.Summary() : null,
                ["bar"] = _bar.State(),
                ["campaign"] = _campaign.State(),
                ["pointers"] = _pointer.Count,
                ["camera"] = _director.State(),
                ["cutscene_played"] = _cutscenes.OpeningPlayed,
                ["cutscene"] = _cutscenes.Summary(),
                ["chat_unread"] = _chat.UndeliveredCount(),
                ["timberbot"] = new JObject { ["http_port"] = _settings.HttpPort, ["ready"] = _timberbot.AgentState.Ready },
                ["mcp"] = new JObject { ["port"] = _settings.McpPort, ["version"] = WardensMcpServer.Version },
            };
        }

        private JObject TutorialState()
        {
            var active = new JArray();
            foreach (var kv in _tutorialService._activeTutorialStages)
            {
                var stage = kv.Value;
                var steps = new JArray();
                foreach (var step in stage.TutorialSteps)
                {
                    string text; bool achieved;
                    try { text = step.Step.Description(); } catch (Exception ex) { text = "? " + ex.Message; }
                    try { achieved = step.Step.Achieved(); } catch { achieved = false; }
                    steps.Add(new JObject { ["text"] = text, ["achieved"] = achieved });
                }
                int left = _tutorialService._waitingTutorialStages.TryGetValue(kv.Key, out var queue) ? queue.Count : 0;
                active.Add(new JObject
                {
                    ["tutorial"] = kv.Key,
                    ["stage"] = stage.Id,
                    ["intro"] = stage.Intro,
                    ["steps"] = steps,
                    ["all_steps_achieved"] = stage.AllStepsAchieved,
                    ["stages_left"] = left,
                });
            }
            return new JObject
            {
                ["enabled"] = !_tutorialSettings.DisableTutorial,
                ["active"] = active,
                ["finished"] = new JArray(_tutorialService._finishedTutorials),
            };
        }

        private JObject Selection()
        {
            if (!_selection.IsAnythingSelected) return new JObject { ["selected"] = false };
            var so = _selection.SelectedObject;
            var o = new JObject { ["selected"] = true };
            o["template"] = so.GetComponent<TemplateSpec>()?.TemplateName;
            var block = so.GetComponent<BlockObject>();
            if (block != null)
                o["coords"] = new JObject { ["x"] = block.Coordinates.x, ["y"] = block.Coordinates.y, ["z"] = block.Coordinates.z };
            var entity = so.GetComponent<EntityComponent>();
            if (entity != null) o["entityId"] = entity.EntityId.ToString();
            o["position"] = WardensCameraDirector.Vec(so.Transform.position);
            o["isBot"] = so.GetComponent<Bot>() != null;
            return o;
        }

        private JObject Loopback(string method, string path, JObject query, JObject body)
        {
            if (!path.StartsWith("/api/", StringComparison.Ordinal)) throw new ArgumentException("path must start with /api/");
            var isPost = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder($"http://127.0.0.1:{_settings.HttpPort}{path}");
            if (!isPost)
            {
                sb.Append("?format=json");
                if (query != null)
                    foreach (var kv in query)
                        sb.Append('&').Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value?.ToString() ?? ""));
            }
            var request = new HttpRequestMessage(isPost ? HttpMethod.Post : HttpMethod.Get, sb.ToString());
            if (!string.IsNullOrEmpty(_settings.AuthToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AuthToken);
            if (isPost)
            {
                var b = body ?? new JObject();
                b["format"] = "json";
                request.Content = new StringContent(b.ToString(Formatting.None), Encoding.UTF8, "application/json");
            }
            var response = Http.SendAsync(request).GetAwaiter().GetResult();
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            JToken parsed;
            try { parsed = JToken.Parse(text); } catch { parsed = text; }
            var result = new JObject { ["status"] = (int)response.StatusCode, ["body"] = parsed };
            if ((int)response.StatusCode == 409) result["hint"] = "Timberbot ready gate is closed: call timberbot_ready first.";
            return result;
        }
    }
}
