// TimberbotMcp.cs. Stateless MCP (2026-07-28) protocol core for POST /mcp.
//
// Unity-free: compiled into the xUnit project. The Unity side
// (TimberbotHttpServer) implements IMcpHost, hands each HTTP request to
// McpServer.Handle, sends the returned body, and — for write tools — enqueues
// the returned McpDeferredWrite as an ordinary write job and later wraps the
// job result with WrapToolResult. Spec: docs/spec/mcp-endpoint.md, ADR-001.
//
// Threading: Handle() runs on the HTTP listener thread. Read tools call
// IMcpHost.ExecuteRead, which must only touch published snapshots (the same
// path as GET /api/*). Nothing in this file touches game state.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Timberbot
{
    public enum McpToolKind { Read, Write, Destructive }

    public sealed class McpToolSpec
    {
        public string Name;
        public string Title;
        public string Description;
        public JObject InputSchema;
        public McpToolKind Kind;
        public bool Idempotent;
        // REST route this tool maps to. GET routes run inline (read); POST routes are queued.
        public string Route;
        public bool IsPost;
        // tool argument name -> REST parameter name (identity when absent)
        public Dictionary<string, string> ArgMap = new Dictionary<string, string>();

        public JObject Annotations() => new JObject
        {
            ["readOnlyHint"] = Kind == McpToolKind.Read,
            ["destructiveHint"] = Kind == McpToolKind.Destructive,
            ["idempotentHint"] = Idempotent,
            ["openWorldHint"] = false,
        };

        public JObject ToListJson() => new JObject
        {
            ["name"] = Name,
            ["title"] = Title,
            ["description"] = Description,
            ["inputSchema"] = InputSchema.DeepClone(),
            ["annotations"] = Annotations(),
        };

        public string RestKey(string arg) => ArgMap.TryGetValue(arg, out var k) ? k : arg;
    }

    // What the Unity side must provide. Kept tiny so tests can fake it.
    public interface IMcpHost
    {
        bool GameReady { get; }
        string ModVersion { get; }
        // Runs a GET route on the calling thread from published snapshots; returns the JSON text.
        string ExecuteRead(string route, IDictionary<string, string> query);
        bool HasWriteRoute(string route);
    }

    public sealed class McpHttpRequest
    {
        public string Method = "POST";
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body = "";
        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
    }

    // A write tool that the host must enqueue; the HTTP response is produced later by WrapToolResult.
    public sealed class McpDeferredWrite
    {
        public JToken RequestId;
        public McpToolSpec Tool;
        public string Route;
        public JObject Body;
        public int WaitFrames;
    }

    public sealed class McpHttpResponse
    {
        public int Status = 200;
        public string Body = "";
        public string ContentType = "application/json";
        public McpDeferredWrite Deferred;
        public bool IsDeferred => Deferred != null;
    }

    public static class McpCatalog
    {
        public const string Prefix = "timberborn_";
        public const string WaitRoute = "mcp:wait";
        // Prefab enumeration walks live BuildingService state, so it runs as a main-thread job, not on the listener thread.
        public const string PrefabsRoute = "mcp:prefabs";
        public const int MapMaxSide = 64;
        public const int JsonMaxSide = 20;

        private static readonly List<McpToolSpec> _tools = Build();
        public static IReadOnlyList<McpToolSpec> Tools => _tools;

        public static McpToolSpec Find(string name)
        {
            foreach (var t in _tools) if (t.Name == name) return t;
            return null;
        }

        private static JObject Int(string desc, int? min = null, int? max = null)
        {
            var o = new JObject { ["type"] = "integer", ["description"] = desc };
            if (min.HasValue) o["minimum"] = min.Value;
            if (max.HasValue) o["maximum"] = max.Value;
            return o;
        }
        private static JObject Str(string desc, params string[] enumValues)
        {
            var o = new JObject { ["type"] = "string", ["description"] = desc };
            if (enumValues != null && enumValues.Length > 0) o["enum"] = new JArray(enumValues);
            return o;
        }
        private static JObject Bool(string desc) => new JObject { ["type"] = "boolean", ["description"] = desc };
        private static JObject Schema(JObject props, params string[] required)
        {
            var s = new JObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
            if (required.Length > 0) s["required"] = new JArray(required);
            return s;
        }
        private static JObject Confirm() => Bool("Set true to consent explicitly. Clients that support elicitation get an interactive confirmation instead.");

        private static List<McpToolSpec> Build()
        {
            var list = new List<McpToolSpec>
            {
                new McpToolSpec {
                    Name = Prefix + "get_summary", Title = "Colony summary",
                    Description = "Compact colony snapshot: day, weather cycle, drought countdown, population, housing, employment, wellbeing, science, per-district resources and days of food/water left, alert counts, building role counts, district center coordinates. Call this first. Read-only, fresh as of the current frame.",
                    Kind = McpToolKind.Read, Idempotent = true, Route = "/api/summary",
                    InputSchema = Schema(new JObject()) },
                new McpToolSpec {
                    Name = Prefix + "get_region", Title = "Map region",
                    Description = "Terrain, water and occupants for a rectangle of tiles. format=map (default) returns a plain-text grid (~1 token per cell): digits are terrain height % 10, '~' water, '!' contaminated water, letters are occupants (legend included), '@' entrances; max 64x64. format=json returns raw per-tile data (terrain, water depth, badwater, soil contaminated/moist, occupants with z), ~57 tokens per cell, max 20x20. Coordinates: x,y horizontal, z up. Read-only.",
                    Kind = McpToolKind.Read, Idempotent = true, Route = "/api/tiles",
                    InputSchema = Schema(new JObject {
                        ["x1"] = Int("West edge (inclusive)", 0), ["y1"] = Int("South edge (inclusive)", 0),
                        ["x2"] = Int("East edge (inclusive)", 0), ["y2"] = Int("North edge (inclusive)", 0),
                        ["format"] = Str("map (default), json, or both", "map", "json", "both") }, "x1", "y1", "x2", "y2") },
                new McpToolSpec {
                    Name = Prefix + "get_buildings", Title = "List buildings",
                    Description = "Paginated building list with id, name (prefab), x/y/z origin, orientation, finished/paused flags, priority, workers and alerts. Filter by name substring or by Manhattan radius around x,y; pass id for one building; detail=full adds inventory and recipes (much larger). Ids are ephemeral: re-query after a save load. Read-only.",
                    Kind = McpToolKind.Read, Idempotent = true, Route = "/api/buildings",
                    InputSchema = Schema(new JObject {
                        ["limit"] = Int("Max items (default 50)", 1, 200), ["offset"] = Int("Skip first N items", 0),
                        ["name"] = Str("Case-insensitive substring of the prefab name, e.g. 'Pump'"),
                        ["id"] = Int("Return only this building id", 1),
                        ["x"] = Int("Center x for radius filter", 0), ["y"] = Int("Center y for radius filter", 0),
                        ["radius"] = Int("Manhattan radius around x,y", 1, 256),
                        ["detail"] = Str("basic (default) or full", "basic", "full") }) },
                new McpToolSpec {
                    Name = Prefix + "get_beavers", Title = "List beavers",
                    Description = "Paginated beaver list with id, name, position, wellbeing, workplace, district, home and critical-need flag. detail=full adds every need (about 1,400 tokens per beaver; use limit). Read-only.",
                    Kind = McpToolKind.Read, Idempotent = true, Route = "/api/beavers",
                    InputSchema = Schema(new JObject {
                        ["limit"] = Int("Max items (default 20)", 1, 200), ["offset"] = Int("Skip first N items", 0),
                        ["name"] = Str("Case-insensitive name substring"), ["id"] = Int("Return only this beaver id", 1),
                        ["detail"] = Str("basic (default) or full", "basic", "full") }) },
                new McpToolSpec {
                    Name = Prefix + "get_prefabs", Title = "List placeable prefabs",
                    Description = "Building templates that can be passed to place_building: exact prefab name (faction-suffixed, e.g. 'LumberjackFlag.Folktails'; only 'Path' has no suffix), footprint size, science cost, unlocked flag and material cost. Filter by name substring to keep the payload small. Read-only.",
                    Kind = McpToolKind.Read, Idempotent = true, Route = PrefabsRoute, IsPost = true,
                    InputSchema = Schema(new JObject {
                        ["name"] = Str("Case-insensitive substring filter, e.g. 'Pump'"),
                        ["limit"] = Int("Max items (default 50)", 1, 500) }) },
                new McpToolSpec {
                    Name = Prefix + "find_placement", Title = "Find valid placements",
                    Description = "Searches a rectangle (x1,y1,x2,y2) or a radius around x,y for tiles where the prefab can be placed, using the game's own validators. Returns up to 10 candidates with x,y,z, orientation, entrance tile, path access, reachability from the district, distance, power adjacency, flooding and water depth (for pumps). Use its x,y,z,orientation verbatim in place_building. Read-only, but runs on the game thread and may take a few frames.",
                    Kind = McpToolKind.Read, Idempotent = true, Route = "/api/placement/find", IsPost = true,
                    InputSchema = Schema(new JObject {
                        ["prefab"] = Str("Exact prefab name from get_prefabs"),
                        ["x1"] = Int("West edge", 0), ["y1"] = Int("South edge", 0), ["x2"] = Int("East edge", 0), ["y2"] = Int("North edge", 0),
                        ["x"] = Int("Center x (alternative to the rectangle)", 0), ["y"] = Int("Center y", 0),
                        ["radius"] = Int("Radius around x,y (default 30)", 1, 128) }, "prefab") },
                new McpToolSpec {
                    Name = Prefix + "place_building", Title = "Place a building",
                    Description = "Places a construction site for `prefab` with its bottom-left corner at x,y on terrain height z (z must equal the terrain height; take it from find_placement). Validated by the game's placement rules; on failure the result explains why (occupied by whom, terrain conflict, not unlocked, ...) and what to try. Returns the new building id. Prefer find_placement first.",
                    Kind = McpToolKind.Write, Idempotent = false, Route = "/api/building/place", IsPost = true,
                    InputSchema = Schema(new JObject {
                        ["prefab"] = Str("Exact prefab name"), ["x"] = Int("Bottom-left x", 0), ["y"] = Int("Bottom-left y", 0),
                        ["z"] = Int("Terrain height at x,y", 0),
                        ["orientation"] = Str("south (default), west, north or east", "south", "west", "north", "east") }, "prefab", "x", "y", "z") },
                new McpToolSpec {
                    Name = Prefix + "place_path", Title = "Place a path with A*",
                    Description = "Routes and places path tiles from (x1,y1) to (x2,y2) around buildings, water and ruins, adding stairs across height changes. Returns counts of placed paths/stairs/platforms and structured errors for segments that could not be placed. Nothing is demolished.",
                    Kind = McpToolKind.Write, Idempotent = false, Route = "/api/path/place", IsPost = true,
                    InputSchema = Schema(new JObject {
                        ["x1"] = Int("Start x", 0), ["y1"] = Int("Start y", 0), ["x2"] = Int("End x", 0), ["y2"] = Int("End y", 0),
                        ["style"] = Str("Routing style; 'direct' (default) is the shortest route"),
                        ["sections"] = Int("Optional number of sections to split the route into", 0, 64) }, "x1", "y1", "x2", "y2") },
                new McpToolSpec {
                    Name = Prefix + "demolish", Title = "Demolish a building",
                    Description = "Marks the building with this id for demolition (construction sites are removed immediately). Irreversible: materials may be lost. Requires confirmation.",
                    Kind = McpToolKind.Destructive, Idempotent = true, Route = "/api/building/demolish", IsPost = true,
                    InputSchema = Schema(new JObject { ["id"] = Int("Building id from get_buildings", 1), ["confirm"] = Confirm() }, "id") },
                new McpToolSpec {
                    Name = Prefix + "set_speed", Title = "Set game speed",
                    Description = "0 pauses the game, 1 normal, 2 fast, 3 fastest. Use 0 to freeze the world while planning and 1-3 to let time pass; combine with wait_frames.",
                    Kind = McpToolKind.Write, Idempotent = true, Route = "/api/speed", IsPost = true,
                    InputSchema = Schema(new JObject { ["speed"] = Int("0-3", 0, 3) }, "speed") },
                new McpToolSpec {
                    Name = Prefix + "set_workers", Title = "Set desired workers",
                    Description = "Sets the desired worker count of a workplace building (clamped to 1..maxWorkers by the game).",
                    Kind = McpToolKind.Write, Idempotent = true, Route = "/api/building/workers", IsPost = true,
                    InputSchema = Schema(new JObject { ["id"] = Int("Building id", 1), ["count"] = Int("Desired workers", 1, 64) }, "id", "count") },
                new McpToolSpec {
                    Name = Prefix + "unlock_science", Title = "Unlock a building with science",
                    Description = "Spends science points to unlock a building type (exact prefab name from get_prefabs where unlocked is false). Fails with cost vs current points when unaffordable. Irreversible; requires confirmation.",
                    Kind = McpToolKind.Destructive, Idempotent = true, Route = "/api/science/unlock", IsPost = true,
                    InputSchema = Schema(new JObject { ["building"] = Str("Exact prefab name"), ["confirm"] = Confirm() }, "building") },
                new McpToolSpec {
                    Name = Prefix + "set_distribution", Title = "Set district import/export",
                    Description = "Configures a good's distribution for a district: import option (as named by the game, see the distribution section of get_summary or the REST /api/distribution output) and export threshold (-1 keeps the current value).",
                    Kind = McpToolKind.Write, Idempotent = true, Route = "/api/distribution", IsPost = true,
                    InputSchema = Schema(new JObject {
                        ["district"] = Str("District name exactly as shown in get_summary"), ["good"] = Str("Good id, e.g. 'Log'"),
                        ["import"] = Str("Import option name"), ["exportThreshold"] = Int("Export threshold; -1 = unchanged", -1) }, "district", "good") },
                new McpToolSpec {
                    Name = Prefix + "wait_frames", Title = "Let the game run",
                    Description = "Waits N Unity frames (1-600) on the game thread and returns when they have elapsed, so the world can advance between observations. Frames, not game ticks: at speed 1 about 60 frames pass per real second; at speed 0 the world does not change. Idempotent in effect only if the game is paused.",
                    Kind = McpToolKind.Write, Idempotent = false, Route = WaitRoute, IsPost = true,
                    InputSchema = Schema(new JObject { ["frames"] = Int("Frames to wait", 1, 600) }, "frames") },
            };
            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return list;
        }
    }

    // Minimal JSON-Schema validation for the catalog shapes we emit (object/properties/required/
    // additionalProperties/type/enum/minimum/maximum). Returns null when valid.
    public static class McpSchema
    {
        public static string Validate(JObject schema, JObject args)
        {
            args = args ?? new JObject();
            var props = schema["properties"] as JObject ?? new JObject();
            var required = schema["required"] as JArray;
            if (required != null)
                foreach (var r in required)
                    if (args[r.Value<string>()] == null || args[r.Value<string>()].Type == JTokenType.Null)
                        return $"missing required argument '{r.Value<string>()}'";
            bool additional = !(schema["additionalProperties"]?.Type == JTokenType.Boolean && !schema["additionalProperties"].Value<bool>());
            foreach (var prop in args.Properties())
            {
                var ps = props[prop.Name] as JObject;
                if (ps == null)
                {
                    if (!additional) return $"unknown argument '{prop.Name}'";
                    continue;
                }
                var v = prop.Value;
                if (v.Type == JTokenType.Null) continue;
                var type = ps.Value<string>("type");
                switch (type)
                {
                    case "integer":
                        if (v.Type != JTokenType.Integer && !(v.Type == JTokenType.Float && Math.Abs(v.Value<double>() % 1) < 1e-9))
                            return $"argument '{prop.Name}' must be an integer";
                        var iv = v.Value<double>();
                        if (ps["minimum"] != null && iv < ps.Value<double>("minimum")) return $"argument '{prop.Name}' must be >= {ps["minimum"]}";
                        if (ps["maximum"] != null && iv > ps.Value<double>("maximum")) return $"argument '{prop.Name}' must be <= {ps["maximum"]}";
                        break;
                    case "number":
                        if (v.Type != JTokenType.Integer && v.Type != JTokenType.Float) return $"argument '{prop.Name}' must be a number";
                        break;
                    case "string":
                        if (v.Type != JTokenType.String) return $"argument '{prop.Name}' must be a string";
                        var en = ps["enum"] as JArray;
                        if (en != null)
                        {
                            bool ok = false;
                            foreach (var e in en) if (e.Value<string>() == v.Value<string>()) { ok = true; break; }
                            if (!ok) return $"argument '{prop.Name}' must be one of {en.ToString(Formatting.None)}";
                        }
                        break;
                    case "boolean":
                        if (v.Type != JTokenType.Boolean) return $"argument '{prop.Name}' must be a boolean";
                        break;
                }
            }
            return null;
        }
    }

    public sealed class McpServer
    {
        public const string ProtocolVersion = "2026-07-28";
        public const string MetaProtocolVersion = "io.modelcontextprotocol/protocolVersion";
        public const string MetaClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
        public const string MetaClientInfo = "io.modelcontextprotocol/clientInfo";
        public const string MetaServerInfo = "io.modelcontextprotocol/serverInfo";
        public const string ServerName = "timberbot";
        public const int ListTtlMs = 3600000;
        public const int ConfirmTtlSeconds = 120;

        // JSON-RPC / MCP error codes (spec 2026-07-28 basic/index#error-codes)
        public const int ErrParse = -32700, ErrInvalidRequest = -32600, ErrMethodNotFound = -32601, ErrInvalidParams = -32602, ErrInternal = -32603;
        public const int ErrHeaderMismatch = -32020, ErrMissingClientCapability = -32021, ErrUnsupportedVersion = -32022;

        private readonly IMcpHost _host;
        private readonly byte[] _hmacKey;
        private readonly Func<DateTimeOffset> _clock;
        private readonly string _allowedOrigin;

        public McpServer(IMcpHost host, byte[] hmacKey = null, Func<DateTimeOffset> clock = null, string allowedOrigin = null)
        {
            _host = host;
            _hmacKey = hmacKey ?? NewKey();
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _allowedOrigin = allowedOrigin;
        }

        private static byte[] NewKey()
        {
            var key = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
            return key;
        }

        // ------------------------------------------------------------------
        // HTTP entry point
        // ------------------------------------------------------------------
        public McpHttpResponse Handle(McpHttpRequest req)
        {
            if (!string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase))
                return Http(405, ErrorEnvelope(null, ErrInvalidRequest, "MCP endpoint accepts POST only"));

            var origin = req.Header("Origin");
            if (!OriginAllowed(origin))
                return Http(403, ErrorEnvelope(null, ErrInvalidRequest, "Origin not allowed"));

            JObject msg;
            try { msg = JObject.Parse(req.Body ?? ""); }
            catch (Exception ex) { return Http(400, ErrorEnvelope(null, ErrParse, "Parse error: " + ex.Message)); }

            if (msg.Value<string>("jsonrpc") != "2.0")
                return Http(400, ErrorEnvelope(IdOf(msg), ErrInvalidRequest, "jsonrpc must be \"2.0\""));
            var method = msg.Value<string>("method");
            if (string.IsNullOrEmpty(method))
                return Http(400, ErrorEnvelope(IdOf(msg), ErrInvalidRequest, "method is required"));

            var idTok = msg["id"];
            bool isNotification = idTok == null;
            if (!isNotification && (idTok.Type == JTokenType.Null || (idTok.Type != JTokenType.String && idTok.Type != JTokenType.Integer)))
                return Http(400, ErrorEnvelope(null, ErrInvalidRequest, "id must be a string or integer"));
            if (isNotification)
                return new McpHttpResponse { Status = 202, Body = "" };

            var id = idTok;
            var prms = msg["params"] as JObject ?? new JObject();
            var meta = prms["_meta"] as JObject;

            // R8: required _meta fields
            var version = meta?.Value<string>(MetaProtocolVersion);
            if (string.IsNullOrEmpty(version))
                return Http(400, ErrorEnvelope(id, ErrInvalidParams, "Invalid params: missing required _meta field " + MetaProtocolVersion));
            var caps = meta[MetaClientCapabilities] as JObject;
            if (caps == null)
                return Http(400, ErrorEnvelope(id, ErrInvalidParams, "Invalid params: missing required _meta field " + MetaClientCapabilities));

            // R6: headers mirror the body
            var hVersion = req.Header("MCP-Protocol-Version");
            if (hVersion == null) return Http(400, ErrorEnvelope(id, ErrHeaderMismatch, "Header mismatch: MCP-Protocol-Version header is required"));
            if (hVersion != version) return Http(400, ErrorEnvelope(id, ErrHeaderMismatch, $"Header mismatch: MCP-Protocol-Version header value '{hVersion}' does not match body value '{version}'"));
            var hMethod = req.Header("Mcp-Method");
            if (hMethod == null) return Http(400, ErrorEnvelope(id, ErrHeaderMismatch, "Header mismatch: Mcp-Method header is required"));
            if (hMethod != method) return Http(400, ErrorEnvelope(id, ErrHeaderMismatch, $"Header mismatch: Mcp-Method header value '{hMethod}' does not match body value '{method}'"));
            if (method == "tools/call")
            {
                var bodyName = prms.Value<string>("name") ?? "";
                var hName = DecodeHeaderValue(req.Header("Mcp-Name"));
                if (hName == null) return Http(400, ErrorEnvelope(id, ErrHeaderMismatch, "Header mismatch: Mcp-Name header is required for tools/call"));
                if (hName != bodyName) return Http(400, ErrorEnvelope(id, ErrHeaderMismatch, $"Header mismatch: Mcp-Name header value '{hName}' does not match body value '{bodyName}'"));
            }

            // R9: version support
            if (version != ProtocolVersion)
            {
                var data = new JObject { ["supported"] = new JArray(ProtocolVersion), ["requested"] = version };
                return Http(400, ErrorEnvelope(id, ErrUnsupportedVersion, "Unsupported protocol version", data));
            }

            switch (method)
            {
                case "server/discover": return Http(200, ResultEnvelope(id, Discover()));
                case "tools/list": return Http(200, ResultEnvelope(id, ToolsList()));
                case "tools/call": return ToolsCall(id, prms, caps);
                case "initialize":
                    return Http(404, ErrorEnvelope(id, ErrMethodNotFound, "Method not found: this server implements MCP " + ProtocolVersion + " (stateless, no initialize handshake); send server/discover"));
                default:
                    return Http(404, ErrorEnvelope(id, ErrMethodNotFound, "Method not found: " + method));
            }
        }

        // ------------------------------------------------------------------
        // Methods
        // ------------------------------------------------------------------
        private JObject Discover()
        {
            var r = new JObject
            {
                ["resultType"] = "complete",
                ["supportedVersions"] = new JArray(ProtocolVersion),
                ["capabilities"] = new JObject { ["tools"] = new JObject() },
                ["instructions"] = "Timberborn colony control. Start with timberborn_get_summary, then timberborn_get_region for spatial questions (format=map is ~1 token per cell). Use timberborn_find_placement before timberborn_place_building and reuse its x,y,z,orientation. Mutations run sequentially on the game thread; call them one at a time. Destructive tools (demolish, unlock_science) ask for confirmation. Ids are ephemeral across save loads.",
                ["ttlMs"] = ListTtlMs,
                ["cacheScope"] = "private",
            };
            return WithServerInfo(r);
        }

        private JObject ToolsList()
        {
            var arr = new JArray();
            foreach (var t in McpCatalog.Tools) arr.Add(t.ToListJson());
            return WithServerInfo(new JObject
            {
                ["resultType"] = "complete",
                ["tools"] = arr,
                ["ttlMs"] = ListTtlMs,
                ["cacheScope"] = "private",
            });
        }

        private McpHttpResponse ToolsCall(JToken id, JObject prms, JObject clientCaps)
        {
            var name = prms.Value<string>("name");
            var tool = name == null ? null : McpCatalog.Find(name);
            if (tool == null) return Http(200, ErrorEnvelope(id, ErrInvalidParams, "Unknown tool: " + (name ?? "(null)")));

            var args = prms["arguments"] as JObject ?? new JObject();
            var invalid = McpSchema.Validate(tool.InputSchema, args);
            if (invalid != null) return Http(200, ErrorEnvelope(id, ErrInvalidParams, "Invalid arguments for " + tool.Name + ": " + invalid));

            if (!_host.GameReady)
                return Http(200, ResultEnvelope(id, ToolError(TimberbotErrors.Make("GAME_NOT_READY",
                    "the game is not ready for agent commands", "the player must press Launch in the Timberbot widget (or load a save); retry afterwards"))));

            if (tool.Kind == McpToolKind.Destructive)
            {
                var gate = CheckConfirmation(id, tool, args, prms, clientCaps);
                if (gate != null) return gate;
            }

            if (!tool.IsPost) return ExecuteReadTool(id, tool, args);

            var body = new JObject();
            foreach (var p in args.Properties())
            {
                if (p.Name == "confirm") continue;
                body[tool.RestKey(p.Name)] = p.Value.DeepClone();
            }
            body["format"] = "json";
            var deferred = new McpDeferredWrite { RequestId = id, Tool = tool, Route = tool.Route, Body = body };
            if (tool.Route == McpCatalog.WaitRoute)
                deferred.WaitFrames = args.Value<int?>("frames") ?? 1;
            if (!_host.HasWriteRoute(tool.Route))
                return Http(200, ErrorEnvelope(id, ErrInternal, "Internal error: write route not registered: " + tool.Route));
            return new McpHttpResponse { Status = 200, Deferred = deferred };
        }

        // ------------------------------------------------------------------
        // Read tools (listener thread, snapshots only via host)
        // ------------------------------------------------------------------
        private McpHttpResponse ExecuteReadTool(JToken id, McpToolSpec tool, JObject args)
        {
            if (tool.Route == "/api/tiles") return GetRegion(id, tool, args);

            var query = new Dictionary<string, string> { ["format"] = "json" };
            foreach (var p in args.Properties())
                if (p.Value.Type != JTokenType.Null) query[tool.RestKey(p.Name)] = ScalarToString(p.Value);
            // sane defaults for paginated lists
            if (tool.Route == "/api/buildings" && !query.ContainsKey("limit")) query["limit"] = "50";
            if (tool.Route == "/api/beavers" && !query.ContainsKey("limit")) query["limit"] = "20";

            string json;
            try { json = _host.ExecuteRead(tool.Route, query); }
            catch (Exception ex) { return Http(200, ErrorEnvelope(id, ErrInternal, "Internal error: " + ex.Message)); }

            JToken parsed;
            try { parsed = JToken.Parse(json ?? "null"); }
            catch { return Http(200, ResultEnvelope(id, ToolError(TimberbotErrors.Make("INTERNAL_ERROR", "the game returned non-JSON data", "retry; if it persists, check timberbot.log")))); }

            if (TimberbotErrors.TryParse(parsed, out var err))
                return Http(200, ResultEnvelope(id, ToolError(err)));

            return Http(200, ResultEnvelope(id, ToolOk(parsed, parsed.ToString(Formatting.None))));
        }

        private static JToken FilterPrefabs(JToken parsed, JObject args)
        {
            var list = parsed as JArray;
            if (list == null) return parsed;
            var filter = args.Value<string>("name");
            int limit = args.Value<int?>("limit") ?? 50;
            var outArr = new JArray();
            int total = 0;
            foreach (var item in list)
            {
                var n = item.Value<string>("name") ?? "";
                if (!string.IsNullOrEmpty(filter) && n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                total++;
                if (outArr.Count < limit) outArr.Add(item);
            }
            return new JObject { ["total"] = total, ["count"] = outArr.Count, ["has_more"] = total > outArr.Count, ["items"] = outArr };
        }

        private McpHttpResponse GetRegion(JToken id, McpToolSpec tool, JObject args)
        {
            int x1 = args.Value<int>("x1"), y1 = args.Value<int>("y1"), x2 = args.Value<int>("x2"), y2 = args.Value<int>("y2");
            if (x2 < x1) { var t = x1; x1 = x2; x2 = t; }
            if (y2 < y1) { var t = y1; y1 = y2; y2 = t; }
            var format = args.Value<string>("format") ?? "map";
            int w = x2 - x1 + 1, h = y2 - y1 + 1;
            int maxSide = format == "map" ? McpCatalog.MapMaxSide : McpCatalog.JsonMaxSide;
            if (w > maxSide || h > maxSide)
                return Http(200, ResultEnvelope(id, ToolError(TimberbotErrors.Make("INVALID_PARAM",
                    $"region {w}x{h} exceeds the {maxSide}x{maxSide} cap for format={format}",
                    format == "map" ? "split the area into smaller regions" : "use format=map for large areas (about 1 token per cell) or shrink the rectangle"))));

            var query = new Dictionary<string, string>
            {
                ["format"] = "json",
                ["x1"] = x1.ToString(CultureInfo.InvariantCulture), ["y1"] = y1.ToString(CultureInfo.InvariantCulture),
                ["x2"] = x2.ToString(CultureInfo.InvariantCulture), ["y2"] = y2.ToString(CultureInfo.InvariantCulture),
            };
            string json;
            try { json = _host.ExecuteRead("/api/tiles", query); }
            catch (Exception ex) { return Http(200, ErrorEnvelope(id, ErrInternal, "Internal error: " + ex.Message)); }
            JObject tiles;
            try { tiles = JObject.Parse(json); }
            catch { return Http(200, ResultEnvelope(id, ToolError(TimberbotErrors.Make("INTERNAL_ERROR", "the game returned non-JSON tile data", "retry")))); }
            if (TimberbotErrors.TryParse(tiles, out var err)) return Http(200, ResultEnvelope(id, ToolError(err)));

            var mapText = TimberbotMapText.Render(tiles, x1, y1, x2, y2);
            var structured = new JObject
            {
                ["mapSize"] = tiles["mapSize"]?.DeepClone(),
                ["region"] = new JObject { ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 },
            };
            var content = new JArray();
            if (format == "map" || format == "both")
            {
                structured["map"] = mapText;
                content.Add(new JObject { ["type"] = "text", ["text"] = mapText });
            }
            if (format == "json" || format == "both")
            {
                structured["tiles"] = tiles["tiles"]?.DeepClone() ?? new JArray();
                content.Add(new JObject { ["type"] = "text", ["text"] = structured["tiles"].ToString(Formatting.None) });
            }
            var result = new JObject { ["resultType"] = "complete", ["content"] = content, ["structuredContent"] = structured, ["isError"] = false };
            return Http(200, ResultEnvelope(id, WithServerInfo(result)));
        }

        // ------------------------------------------------------------------
        // Deferred write completion (called by the host on the main-thread job's completion path)
        // ------------------------------------------------------------------
        public string WrapToolResult(McpDeferredWrite ctx, int statusCode, object jobResult)
        {
            if (statusCode >= 500)
                return ErrorEnvelope(ctx.RequestId, ErrInternal, "Internal error: " + Stringify(jobResult)).ToString(Formatting.None);
            var text = Stringify(jobResult);
            JToken parsed;
            try { parsed = JToken.Parse(string.IsNullOrEmpty(text) ? "null" : text); }
            catch { parsed = new JObject { ["raw"] = text }; }
            if (TimberbotErrors.TryParse(parsed, out var err))
                return ResultEnvelope(ctx.RequestId, ToolError(err)).ToString(Formatting.None);
            if (ctx.Route == McpCatalog.PrefabsRoute)
            {
                parsed = FilterPrefabs(parsed, ctx.Body);
                text = parsed.ToString(Formatting.None);
            }
            return ResultEnvelope(ctx.RequestId, ToolOk(parsed, text)).ToString(Formatting.None);
        }

        public string WrapToolFailure(McpDeferredWrite ctx, string message)
            => ErrorEnvelope(ctx.RequestId, ErrInternal, "Internal error: " + message).ToString(Formatting.None);

        private static string Stringify(object result)
        {
            if (result == null) return "";
            if (result is string s) return s;
            return JsonConvert.SerializeObject(result);
        }

        // ------------------------------------------------------------------
        // MRTR confirmation for destructive tools (spec R21/R22)
        // ------------------------------------------------------------------
        private McpHttpResponse CheckConfirmation(JToken id, McpToolSpec tool, JObject args, JObject prms, JObject clientCaps)
        {
            var explicitConfirm = args["confirm"]?.Type == JTokenType.Boolean && args.Value<bool>("confirm");
            if (explicitConfirm) return null;

            var state = prms.Value<string>("requestState");
            var responses = prms["inputResponses"] as JObject;
            bool clientHasElicitation = clientCaps?["elicitation"] != null && clientCaps["elicitation"].Type != JTokenType.Null;

            if (!string.IsNullOrEmpty(state))
            {
                var status = VerifyState(state, tool, args);
                if (status == StateStatus.Valid)
                {
                    var resp = responses?["confirm"] as JObject;
                    var action = resp?.Value<string>("action");
                    if (action == "accept")
                    {
                        var content = resp["content"] as JObject;
                        var flag = content?["confirm"];
                        if (flag == null || flag.Type != JTokenType.Boolean || flag.Value<bool>()) return null; // consented
                    }
                    if (action == "decline" || action == "cancel" || action == "accept")
                        return Http(200, ResultEnvelope(id, ToolError(TimberbotErrors.Make("CONFIRMATION_DECLINED",
                            "the user declined " + tool.Name, "do not retry without new instructions"))));
                    // valid state but no usable response: ask again
                    if (clientHasElicitation) return Http(200, ResultEnvelope(id, InputRequired(tool, args)));
                }
                else
                {
                    var why = status == StateStatus.Expired ? "the confirmation expired" : "the confirmation state is invalid";
                    if (clientHasElicitation) return Http(200, ResultEnvelope(id, InputRequired(tool, args)));
                    return Http(200, ResultEnvelope(id, ToolError(TimberbotErrors.Make("CONFIRMATION_EXPIRED", why, "call the tool again to obtain a fresh confirmation"))));
                }
            }

            if (clientHasElicitation) return Http(200, ResultEnvelope(id, InputRequired(tool, args)));
            return Http(200, ResultEnvelope(id, ToolError(TimberbotErrors.Make("CONFIRMATION_REQUIRED",
                tool.Name + " is destructive and was called without consent",
                "ask the user, then retry with confirm=true"))));
        }

        private JObject InputRequired(McpToolSpec tool, JObject args)
        {
            var summary = new StringBuilder();
            foreach (var p in args.Properties()) if (p.Name != "confirm") summary.Append(p.Name).Append('=').Append(p.Value.ToString(Formatting.None)).Append(' ');
            var request = new JObject
            {
                ["method"] = "elicitation/create",
                ["params"] = new JObject
                {
                    ["mode"] = "form",
                    ["message"] = $"Timberbot wants to run {tool.Name} ({summary.ToString().Trim()}). This cannot be undone. Proceed?",
                    ["requestedSchema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject { ["confirm"] = new JObject { ["type"] = "boolean", ["title"] = "Proceed", ["description"] = "true to execute" } },
                        ["required"] = new JArray("confirm"),
                    },
                },
            };
            return WithServerInfo(new JObject
            {
                ["resultType"] = "input_required",
                ["inputRequests"] = new JObject { ["confirm"] = request },
                ["requestState"] = MintState(tool, args),
            });
        }

        private enum StateStatus { Valid, Expired, Invalid }

        private string MintState(McpToolSpec tool, JObject args)
        {
            var payload = new JObject
            {
                ["t"] = tool.Name,
                ["h"] = ArgsDigest(args),
                ["e"] = _clock().ToUnixTimeSeconds() + ConfirmTtlSeconds,
                ["n"] = Guid.NewGuid().ToString("N"),
            };
            var body = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            return B64(body) + "." + B64(Sign(body));
        }

        private StateStatus VerifyState(string state, McpToolSpec tool, JObject args)
        {
            try
            {
                var dot = state.IndexOf('.');
                if (dot <= 0) return StateStatus.Invalid;
                var body = UnB64(state.Substring(0, dot));
                var sig = UnB64(state.Substring(dot + 1));
                var expected = Sign(body);
                if (sig.Length != expected.Length) return StateStatus.Invalid;
                int diff = 0;
                for (int i = 0; i < sig.Length; i++) diff |= sig[i] ^ expected[i];
                if (diff != 0) return StateStatus.Invalid;
                var payload = JObject.Parse(Encoding.UTF8.GetString(body));
                if (payload.Value<string>("t") != tool.Name) return StateStatus.Invalid;
                if (payload.Value<string>("h") != ArgsDigest(args)) return StateStatus.Invalid;
                if (payload.Value<long>("e") < _clock().ToUnixTimeSeconds()) return StateStatus.Expired;
                return StateStatus.Valid;
            }
            catch { return StateStatus.Invalid; }
        }

        private byte[] Sign(byte[] body)
        {
            using (var h = new HMACSHA256(_hmacKey)) return h.ComputeHash(body);
        }

        private static string ArgsDigest(JObject args)
        {
            var canonical = new JObject();
            var names = new List<string>();
            foreach (var p in args.Properties()) if (p.Name != "confirm") names.Add(p.Name);
            names.Sort(StringComparer.Ordinal);
            foreach (var n in names) canonical[n] = args[n];
            using (var sha = SHA256.Create())
                return B64(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString(Formatting.None))));
        }

        private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] UnB64(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        // ------------------------------------------------------------------
        // Envelopes
        // ------------------------------------------------------------------
        private JObject WithServerInfo(JObject result)
        {
            var meta = result["_meta"] as JObject ?? new JObject();
            meta[MetaServerInfo] = new JObject { ["name"] = ServerName, ["version"] = _host.ModVersion ?? "unknown" };
            result["_meta"] = meta;
            return result;
        }

        private JObject ToolOk(JToken structured, string text)
        {
            return WithServerInfo(new JObject
            {
                ["resultType"] = "complete",
                ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text ?? structured.ToString(Formatting.None) }),
                ["structuredContent"] = structured,
                ["isError"] = false,
            });
        }

        private JObject ToolError(TimberbotErrorInfo err)
        {
            var structured = err.ToJson();
            var text = err.Code + ": " + err.Reason + (string.IsNullOrEmpty(err.Hint) ? "" : ". " + err.Hint);
            return WithServerInfo(new JObject
            {
                ["resultType"] = "complete",
                ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }),
                ["structuredContent"] = structured,
                ["isError"] = true,
            });
        }

        private static JObject ResultEnvelope(JToken id, JObject result)
            => new JObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone() ?? JValue.CreateNull(), ["result"] = result };

        private static JObject ErrorEnvelope(JToken id, int code, string message, JObject data = null)
        {
            var err = new JObject { ["code"] = code, ["message"] = message };
            if (data != null) err["data"] = data;
            return new JObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone() ?? JValue.CreateNull(), ["error"] = err };
        }

        private static McpHttpResponse Http(int status, JObject body)
            => new McpHttpResponse { Status = status, Body = body.ToString(Formatting.None) };

        private static JToken IdOf(JObject msg)
        {
            var id = msg["id"];
            if (id == null || (id.Type != JTokenType.String && id.Type != JTokenType.Integer)) return null;
            return id;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private bool OriginAllowed(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return true;
            if (!string.IsNullOrEmpty(_allowedOrigin) && string.Equals(origin.TrimEnd('/'), _allowedOrigin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return true;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host;
            return host == "localhost" || host == "127.0.0.1" || host == "::1" || host == "[::1]";
        }

        // Mcp-Name may be carried as =?base64?...?= (spec: value encoding).
        public static string DecodeHeaderValue(string value)
        {
            if (value == null) return null;
            if (value.StartsWith("=?base64?", StringComparison.Ordinal) && value.EndsWith("?=", StringComparison.Ordinal))
            {
                try
                {
                    var inner = value.Substring(9, value.Length - 11);
                    return Encoding.UTF8.GetString(Convert.FromBase64String(inner));
                }
                catch { return value; }
            }
            return value;
        }

        private static string ScalarToString(JToken v)
        {
            switch (v.Type)
            {
                case JTokenType.Integer: return v.Value<long>().ToString(CultureInfo.InvariantCulture);
                case JTokenType.Float: return v.Value<double>().ToString(CultureInfo.InvariantCulture);
                case JTokenType.Boolean: return v.Value<bool>() ? "true" : "false";
                case JTokenType.String: return v.Value<string>();
                default: return v.ToString(Formatting.None);
            }
        }
    }
}
