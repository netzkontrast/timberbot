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
using System.Threading;
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
        private readonly WardensChapterService _chapters;
        private readonly WardensFrames _frames;
        private readonly WardensCampaignService _campaign;
        // The last frame seq handed out. The `frame` tool is OffThread and WardensMcpServer runs each
        // request on a pool thread, so this is read and advanced with Interlocked, never assigned.
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
        private JArray _ledgerSnapshot = new JArray();

        public WardensMcpTools(FactionService factionService, SpeedManager speedManager,
            CharacterPopulation population, TutorialService tutorialService, TutorialSettings tutorialSettings,
            WardensPointer pointer, WardensChat chat, WardensCameraDirector director, WardensCutscenes cutscenes,
            EntitySelectionService selection, QuickNotificationService quickNotifications, TimberbotService timberbot,
            WardensAssetDump assetDump, WardensChapterService chapters, WardensFrames frames,
            WardensCampaignService campaign)
        {
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
            _chapters = chapters;
            _frames = frames;
            _campaign = campaign;
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

            sb.Append("START HERE, IN THIS ORDER\n")
              .Append("1. `manual` - the playbook (docs/WARDEN.md). Read it once per session, before acting.\n")
              .Append("2. `campaign action=status` - which level this map is, and what earlier levels left you.\n")
              .Append("3. `wardens_status` - faction, speed, population, tutorial and chapter state.\n")
              .Append("4. `timberbot_ready` - once; the read/write API refuses everything until you call it.\n")
              .Append("5. `chat_history` - what was said before you arrived. Answer anything unanswered first.\n")
              .Append("6. `frame` with after=0, and stay in that loop.\n\n");

            sb.Append("THE LOOP\n")
              .Append("`frame` is the heartbeat and the only thing you wait on. It returns every N game ticks, or the moment something ")
              .Append("happens (the human typed, a day or night started, a building finished, a chapter opened, a beaver was born, an ")
              .Append("alert appeared, the human selected something), and it carries `attention`: where to look, in order, with positions ")
              .Append("`camera` and `point` accept. Pass `after` = the last seq you saw. Never poll the read API to find out whether ")
              .Append("anything changed. Every tool result may carry \"chat\": messages the human typed that you have not seen. Answer ")
              .Append("those with `say` before anything else.\n\n");

            sb.Append("THE TOOLS ARE THE BODY\n")
              .Append("`say` is the voice, `point` the finger (highlight, arrow and optional toast on one tile), `camera` the eye, ")
              .Append("`timberbot` the hands: it forwards to the Timberbot HTTP API compiled into this same mod (GET reads, POST acts), ")
              .Append("and `timberbot_routes` lists what it will take. `campaign`, `chapter` and `tutorial` are the memory of the plan; ")
              .Append("`campaign action=record` is the only memory that outlives this map. `selection` is the human pointing at something.\n\n");

            sb.Append("RULES THAT DO NOT BEND\n")
              .Append("- Mutations are sequential. Never overlap POST calls through `timberbot`.\n")
              .Append("- The camera is the human's. Never move it unprompted except once at a chapter transition; `camera action=get` first, restore after.\n")
              .Append("- A playing cutscene owns the camera and the conversation (`cutscene.playing`; events cutscene.start / cutscene.end): touch nothing and say nothing until it ends.\n")
              .Append("- Never demolish, never pause the colony, never force a chapter open (`chapter action=unlock`), never answer a choice card (`cutscene action=choose`) and never reset a record, unless the human asked for that exact thing.\n")
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
                sb.Append(" (NOT the Wardens: the story, the chapters and the campaign are all inert on this save)");

            var level = _campaign.Level;
            sb.Append("; campaign=");
            if (_campaign.Enabled && level != null)
            {
                sb.Append("level ").Append(level.Id).Append(" '").Append(level.Title).Append("' on map '").Append(level.MapName)
                  .Append("', ends when the tutorial ").Append(level.EndsWithTutorial).Append(" finishes");
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
                    text = "The campaign this run belongs to. Timberborn has no objective system: a level is a map plus the chapter "
                         + "line that runs on it, and only campaign.json survives a map change.\n\n"
                         + _campaignSnapshot.ToString(Formatting.Indented)
                         + "\n\nThe Ledger so far (the last entries written on any level):\n"
                         + _ledgerSnapshot.ToString(Formatting.Indented);
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

        private static string ManualText()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", "docs", "WARDEN.md");
            return System.IO.File.Exists(path)
                ? System.IO.File.ReadAllText(path)
                : "(WARDEN.md is not deployed; the repo copy is wardens/WARDEN.md)";
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
                "Overview of the run: faction, speed, bot/beaver counts and average bot Energy, tutorial and chapter state, pointers, camera, Timberbot readiness.",
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
                "The Warden's heartbeat. Waits (long-poll, up to wait_seconds) for the next sensor frame: published every every_ticks game ticks, or at once when something happens (chat, day/night, cycle day, building finished, chapter opened, beaver born, alert, speed change, selection). Carries time, bots and charge, beavers, archive (Data Cores), science, chapter, open tutorial steps, selection, camera pose, human idle time, events since the last frame, and `attention` (where to look, in order). Pass `after` = the last seq you saw; a `stale` frame means nothing new was published before the wait ended (usually because the human typed).",
                Schema(new JObject
                {
                    ["wait_seconds"] = Prop("integer", "long-poll timeout, 0-120", 30),
                    ["every_ticks"] = Prop("integer", "frame cadence in game ticks (5-2000); omit to keep the current cadence"),
                    ["after"] = Prop("integer", "return the first frame with seq greater than this; default: the last frame this server handed out"),
                }),
                a =>
                {
                    if (a["every_ticks"] != null && a["every_ticks"].Type != JTokenType.Null) _frames.SetEveryTicks(Int(a, "every_ticks", WardensFrames.DefaultEveryTicks));
                    long after = a["after"] != null && a["after"].Type != JTokenType.Null ? (long)a["after"] : Interlocked.Read(ref _lastFrameSeq);
                    int wait = Mathf.Clamp(Int(a, "wait_seconds", 30), 0, 120);
                    var frame = _frames.Wait(after, wait * 1000);
                    var seq = frame["seq"] != null ? (long)frame["seq"] : 0L;
                    long seen;
                    do
                    {
                        seen = Interlocked.Read(ref _lastFrameSeq);
                        if (seq <= seen) break;
                    } while (Interlocked.CompareExchange(ref _lastFrameSeq, seq, seen) != seen);
                    return frame;
                }, offThread: true);

            Add("manual",
                "The Warden's playbook (docs/WARDEN.md in the mod folder): who you are, the Ledger, the frame loop, where to look, the camera rules, the chapter playbook, the voice. Read it once per session before acting.",
                Schema(new JObject()),
                a =>
                {
                    var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WardensSettings.Path) ?? "", "docs", "WARDEN.md");
                    if (!System.IO.File.Exists(path))
                        return new JObject { ["path"] = path, ["error"] = "not deployed; the repo copy is wardens/WARDEN.md" };
                    return new JObject { ["path"] = path, ["text"] = System.IO.File.ReadAllText(path) };
                }, offThread: true);

            Add("chapter",
                "Story chapters that gate the building bar (WardensChapters.cs): each chapter opens when its tutorial finishes and unlocks the padlocked buildings. action=status lists every chapter with its gate and per-building lock state; action=unlock opens chapter_id now (dev/testing).",
                Schema(new JObject
                {
                    ["action"] = Prop("string", "status | unlock", "status"),
                    ["chapter_id"] = Prop("string", "e.g. Badwater, Signal, Pods, Power, Green (for unlock)"),
                }),
                a =>
                {
                    if (Str(a, "action", "status") == "unlock")
                    {
                        var id = Str(a, "chapter_id") ?? throw new ArgumentException("chapter_id required");
                        if (!_chapters.Force(id)) throw new ArgumentException("unknown chapter " + id);
                    }
                    return _chapters.State();
                });

            Add("campaign",
                "The campaign across maps. Timberborn has no objective system, so a level is a map plus the chapter line that runs on it, " +
                "and what survives a map change lives in campaign.json beside the mod. action=status returns the level table, which level this " +
                "map is, whether it is complete and which map comes next; action=ledger returns the last `limit` Ledger entries written on any " +
                "level; action=record appends one Ledger entry (`entry`, any JSON object: the daily poisoned/healed/green/archive/born line from " +
                "WARDEN.md is what belongs here) and is the only memory you have that outlives this map; action=complete marks a level done and " +
                "action=reset wipes the record (both dev/testing).",
                Schema(new JObject
                {
                    ["action"] = Prop("string", "status | ledger | record | complete | reset", "status"),
                    ["entry"] = new JObject { ["type"] = "object", ["description"] = "for record: the Ledger entry, e.g. {\"day\":12,\"poisoned\":214,\"healed\":0,\"green\":31,\"archive\":9,\"born\":0,\"seen\":\"...\"}" },
                    ["limit"] = Prop("integer", "for ledger: how many entries", 20),
                    ["level_id"] = Prop("string", "for complete: the level, e.g. 01; defaults to the current one"),
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
                        case "complete":
                            _campaign.MarkCompleted(Str(a, "level_id") ?? _campaign.Level?.Id
                                ?? throw new ArgumentException("level_id required: this map is not a campaign level"));
                            break;
                        case "reset":
                            _campaign.Reset();
                            break;
                    }
                    return _campaign.State();
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
                "Cutscenes: scenes from Cutscenes/*.json in the mod folder (design/wardens-cutscenes.md): the Cold Boot, one per chapter, the level's end card, the Archive reading. action=status lists the loaded scenes with their triggers, the running one (shot, caption, waiting: flight | time | continue | choice, the open choices) and the story record (choices, marks); play starts `id` now, replacing a running scene and ignoring the trigger policy (`Archive` reads the Ledger back when the human asks); skip ends the running scene; continue releases a shot that waits for the Continue button; choose answers an open choice card with `choice` (only when the human said which, in chat: a choice is purpose); reload re-reads the files (edit in the mod folder, reload, play: the tuning loop); reset clears the story record (dev). A playing scene owns the camera: leave it and say nothing until the frame reports cutscene.end.",
                Schema(new JObject
                {
                    ["action"] = Prop("string", "status | list | play | skip | continue | choose | reload | reset", "status"),
                    ["id"] = Prop("string", "scene id for play", WardensCutscenes.ColdBootId),
                    ["choice"] = Prop("string", "choice id for choose (the open card's ids are in status.choices)"),
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
                    }
                    return _cutscenes.State();
                });

            Add("speed", "Set game speed: 0 = pause, 1..3 = the speed buttons.",
                Schema(new JObject { ["value"] = Prop("integer", "0-3") }, "value"),
                a => { _speedManager.ChangeSpeed(Mathf.Clamp(Int(a, "value", 1), 0, 3)); return new JObject { ["speed"] = _speedManager.CurrentSpeed }; });

            Add("timberbot",
                "Call the Timberbot HTTP API compiled into this mod (loopback). method GET with optional query (`path` may carry its own query, e.g. /api/tiles?x1=10&y1=40&x2=30&y2=55; `query` entries override it), or POST with a JSON body. Paths as in docs/api-reference.md, e.g. GET /api/summary, POST /api/building/place. The API refuses everything except ping/agent until timberbot_ready has been called (HTTP 409).",
                Schema(new JObject
                {
                    ["method"] = Prop("string", "GET | POST", "GET"),
                    ["path"] = Prop("string", "route, starting with /api/"),
                    ["query"] = new JObject { ["type"] = "object", ["description"] = "GET query parameters (id, detail, limit, offset, name, x, y, radius, ...); merged with any query carried in path" },
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
                ["chapter"] = _chapters.Summary(),
                ["campaign"] = _campaign.State(),
                ["pointers"] = _pointer.Count,
                ["camera"] = _director.State(),
                ["cutscene_played"] = _cutscenes.HasPlayed(WardensCutscenes.ColdBootId),
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
            if (path == null || !path.StartsWith("/api/", StringComparison.Ordinal)) throw new ArgumentException("path must start with /api/");
            var isPost = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
            // A GET's query may arrive inside `path`, in `query`, or both. WardensPure merges them and
            // adds format=json once. (Before: "?format=json" was appended after a query carried in
            // `path`, so the last parameter's value arrived as "55?format=json" and parsed as 0: the
            // dropped tiles bound, the ignored offset and the empty name filter of the 2026-09-10 playtest.)
            var url = isPost
                ? $"http://127.0.0.1:{_settings.HttpPort}{path}"
                : WardensPure.BuildLoopbackUrl(_settings.HttpPort, path, query);
            var request = new HttpRequestMessage(isPost ? HttpMethod.Post : HttpMethod.Get, url);
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
