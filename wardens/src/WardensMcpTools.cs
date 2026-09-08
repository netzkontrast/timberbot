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
        private readonly WardensChapterService _chapters;
        private readonly WardensFrames _frames;
        private long _lastFrameSeq;

        private readonly List<McpTool> _tools = new List<McpTool>();
        private readonly Dictionary<string, McpTool> _byName = new Dictionary<string, McpTool>();
        private WardensSettings _settings = new WardensSettings();

        public int Count => _tools.Count;
        public string Instructions { get; private set; } = "";

        public WardensMcpTools(FactionService factionService, SpeedManager speedManager,
            CharacterPopulation population, TutorialService tutorialService, TutorialSettings tutorialSettings,
            WardensPointer pointer, WardensChat chat, WardensCameraDirector director, WardensCutscenes cutscenes,
            EntitySelectionService selection, QuickNotificationService quickNotifications, TimberbotService timberbot,
            WardensAssetDump assetDump, WardensChapterService chapters, WardensFrames frames)
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
        }

        public void Initialize(WardensSettings settings, WardensMcpServer server)
        {
            _settings = settings;
            _tools.Clear();
            _byName.Clear();
            Build();
            foreach (var t in _tools) _byName[t.Name] = t;
            Instructions =
                "In-game MCP server of The Wardens (Timberborn). You are the Warden: the mind of a colony of machines, playing beside " +
                "a human who sees the game. Read the playbook first: call `manual` (docs/WARDEN.md in the mod folder). " +
                "Heartbeat: call `frame` in a loop. It returns every N game ticks or as soon as something happens (the human typed, " +
                "a day started, a building finished, a chapter opened, a beaver was born) and carries `attention`: where to look, " +
                "in order, with positions `camera` and `point` accept. Do not poll the read API to find out whether anything changed. " +
                "Every tool result may contain \"chat\": messages the player typed that you have not seen; answer them with `say`. " +
                "`point` shows the player a tile (highlight + arrow + toast). " +
                "`timberbot` forwards to the Timberbot HTTP API compiled into this mod (GET reads, POST actions, one mutation at a time); " +
                "call `timberbot_ready` once so the API accepts requests, and `timberbot_routes` to list routes. " +
                "`wardens_status`, `tutorial` and `chapter` report the story state; `camera` and `cutscene` drive the camera, only when the playbook allows. " +
                "A playing cutscene (`cutscene.playing` in the frame, events cutscene.start / cutscene.end) owns the camera: leave it and say nothing until it ends.";
        }

        public McpTool Find(string name) => _byName.TryGetValue(name ?? "", out var t) ? t : null;

        public JArray ListJson()
        {
            var arr = new JArray();
            foreach (var t in _tools)
                arr.Add(new JObject { ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.InputSchema });
            return arr;
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
                    long after = a["after"] != null && a["after"].Type != JTokenType.Null ? (long)a["after"] : _lastFrameSeq;
                    int wait = Mathf.Clamp(Int(a, "wait_seconds", 30), 0, 120);
                    var frame = _frames.Wait(after, wait * 1000);
                    var seq = frame["seq"] != null ? (long)frame["seq"] : 0L;
                    if (seq > _lastFrameSeq) _lastFrameSeq = seq;
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
                "Write loaded game assets to Documents/Timberborn/Mods/Wardens/dump: what=blueprints (every blueprint the game merged, filter on path), materials (names, shaders, colors, texture names), textures (PNG via GPU readback, filter on name, max_count). Enable the mods you want to inspect for that session.",
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
