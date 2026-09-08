// Executable form of docs/spec/mcp-endpoint.md and docs/spec/error-contract.md.
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Timberbot;
using Xunit;

namespace Timberbot.Tests
{
    internal sealed class FakeHost : IMcpHost
    {
        public bool GameReady { get; set; } = true;
        public string ModVersion => "0.8.0-test";
        public Dictionary<string, string> Reads = new Dictionary<string, string>
        {
            ["/api/summary"] = "{\"day\":3,\"faction\":\"Folktails\"}",
            ["/api/buildings"] = "{\"total\":1,\"offset\":0,\"limit\":50,\"items\":[{\"id\":42,\"name\":\"Path\"}]}",
            ["/api/beavers"] = "{\"total\":0,\"offset\":0,\"limit\":20,\"items\":[]}",
            ["/api/prefabs"] = "[{\"name\":\"Path\",\"sizeX\":1},{\"name\":\"WaterPump.Folktails\",\"sizeX\":1},{\"name\":\"LumberjackFlag.Folktails\",\"sizeX\":1}]",
        };
        public List<(string route, IDictionary<string, string> query)> ReadCalls = new List<(string, IDictionary<string, string>)>();
        public string ExecuteRead(string route, IDictionary<string, string> query)
        {
            ReadCalls.Add((route, new Dictionary<string, string>(query)));
            if (route == "/api/tiles") return Tiles(int.Parse(query["x1"]), int.Parse(query["y1"]), int.Parse(query["x2"]), int.Parse(query["y2"]));
            return Reads.TryGetValue(route, out var v) ? v : "{\"error\":\"unknown_endpoint: nope\"}";
        }
        public bool HasWriteRoute(string route) => route.StartsWith("/api/") || route.StartsWith("mcp:");

        private static string Tiles(int x1, int y1, int x2, int y2)
        {
            var tiles = new JArray();
            for (int y = y1; y <= y2; y++)
                for (int x = x1; x <= x2; x++)
                {
                    var t = new JObject { ["x"] = x, ["y"] = y, ["terrain"] = 4 + (x % 3), ["water"] = x == x1 ? 1.5 : 0.0, ["badwater"] = 0.0,
                        ["entrance"] = 0, ["seedling"] = 0, ["dead"] = 0, ["contaminated"] = 0, ["moist"] = 0, ["occupants"] = new JArray() };
                    if (x == x2 && y == y2) t["occupants"] = new JArray(new JObject { ["name"] = "LumberjackFlag.Folktails", ["z"] = 5 });
                    if (x == x1 + 1 && y == y1) t["occupants"] = new JArray(new JObject { ["name"] = "Pine", ["z"] = 4 });
                    tiles.Add(t);
                }
            return new JObject { ["mapSize"] = new JObject { ["x"] = 128, ["y"] = 128, ["z"] = 23 }, ["region"] = new JObject(), ["tiles"] = tiles }.ToString(Formatting.None);
        }
    }

    public class McpServerTests
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");
        private readonly FakeHost _host = new FakeHost();
        private DateTimeOffset _now = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
        private McpServer NewServer(string allowedOrigin = null) => new McpServer(_host, Key, () => _now, allowedOrigin);

        private static JObject Meta(string version = McpServer.ProtocolVersion, bool elicitation = false)
        {
            var caps = new JObject();
            if (elicitation) caps["elicitation"] = new JObject { ["form"] = new JObject() };
            return new JObject
            {
                [McpServer.MetaProtocolVersion] = version,
                [McpServer.MetaClientCapabilities] = caps,
                [McpServer.MetaClientInfo] = new JObject { ["name"] = "test", ["version"] = "1" },
            };
        }

        private static McpHttpRequest Req(string method, JObject prms, object id = null, string version = McpServer.ProtocolVersion, bool elicitation = false, string toolName = null)
        {
            prms = prms ?? new JObject();
            prms["_meta"] = Meta(version, elicitation);
            var body = new JObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = prms };
            if (id != null) body["id"] = JToken.FromObject(id);
            var r = new McpHttpRequest { Method = "POST", Body = body.ToString(Formatting.None) };
            r.Headers["MCP-Protocol-Version"] = version;
            r.Headers["Mcp-Method"] = method;
            if (method == "tools/call") r.Headers["Mcp-Name"] = toolName ?? prms.Value<string>("name");
            return r;
        }

        private static McpHttpRequest Call(string tool, JObject args, object id = null, bool elicitation = false, string requestState = null, JObject inputResponses = null)
        {
            var prms = new JObject { ["name"] = tool, ["arguments"] = args ?? new JObject() };
            if (requestState != null) prms["requestState"] = requestState;
            if (inputResponses != null) prms["inputResponses"] = inputResponses;
            return Req("tools/call", prms, id ?? 7, elicitation: elicitation);
        }

        private static JObject Body(McpHttpResponse r) => JObject.Parse(r.Body);

        // ---- transport / envelope -------------------------------------------------

        [Fact]
        public void Discover_ReturnsVersionsCapabilitiesAndServerInfo()
        {
            var r = NewServer().Handle(Req("server/discover", null, "discover-1"));
            Assert.Equal(200, r.Status);
            var res = Body(r)["result"];
            Assert.Equal("complete", res.Value<string>("resultType"));
            Assert.Equal(McpServer.ProtocolVersion, res["supportedVersions"][0].Value<string>());
            Assert.NotNull(res["capabilities"]["tools"]);
            Assert.Equal("timberbot", res["_meta"][McpServer.MetaServerInfo].Value<string>("name"));
            Assert.Equal("0.8.0-test", res["_meta"][McpServer.MetaServerInfo].Value<string>("version"));
            Assert.True(res.Value<int>("ttlMs") > 0);
            Assert.Equal("private", res.Value<string>("cacheScope"));
            Assert.Equal("discover-1", Body(r).Value<string>("id"));
        }

        [Fact]
        public void Get_Is405()
        {
            var req = Req("tools/list", null, 1); req.Method = "GET";
            var r = NewServer().Handle(req);
            Assert.Equal(405, r.Status);
            Assert.Equal(McpServer.ErrInvalidRequest, Body(r)["error"].Value<int>("code"));
        }

        [Theory]
        [InlineData("http://localhost:3000", 200)]
        [InlineData("http://127.0.0.1:8085", 200)]
        [InlineData("https://evil.example", 403)]
        public void Origin_IsValidated(string origin, int expected)
        {
            var req = Req("tools/list", null, 1); req.Headers["Origin"] = origin;
            Assert.Equal(expected, NewServer().Handle(req).Status);
        }

        [Fact]
        public void ConfiguredCorsOrigin_IsAccepted()
        {
            var req = Req("tools/list", null, 1); req.Headers["Origin"] = "http://my-dashboard.local:9000";
            Assert.Equal(200, NewServer("http://my-dashboard.local:9000").Handle(req).Status);
        }

        [Fact]
        public void MissingProtocolVersionMeta_Is400InvalidParams()
        {
            var body = new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list",
                ["params"] = new JObject { ["_meta"] = new JObject { [McpServer.MetaClientCapabilities] = new JObject() } } };
            var req = new McpHttpRequest { Body = body.ToString() };
            req.Headers["MCP-Protocol-Version"] = McpServer.ProtocolVersion; req.Headers["Mcp-Method"] = "tools/list";
            var r = NewServer().Handle(req);
            Assert.Equal(400, r.Status);
            Assert.Equal(McpServer.ErrInvalidParams, Body(r)["error"].Value<int>("code"));
        }

        [Fact]
        public void MissingClientCapabilitiesMeta_Is400InvalidParams()
        {
            var body = new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list",
                ["params"] = new JObject { ["_meta"] = new JObject { [McpServer.MetaProtocolVersion] = McpServer.ProtocolVersion } } };
            var req = new McpHttpRequest { Body = body.ToString() };
            req.Headers["MCP-Protocol-Version"] = McpServer.ProtocolVersion; req.Headers["Mcp-Method"] = "tools/list";
            var r = NewServer().Handle(req);
            Assert.Equal(400, r.Status);
            Assert.Equal(McpServer.ErrInvalidParams, Body(r)["error"].Value<int>("code"));
        }

        [Fact]
        public void HeaderMismatch_Method_Is400()
        {
            var req = Req("tools/list", null, 1); req.Headers["Mcp-Method"] = "tools/call";
            var r = NewServer().Handle(req);
            Assert.Equal(400, r.Status);
            Assert.Equal(McpServer.ErrHeaderMismatch, Body(r)["error"].Value<int>("code"));
        }

        [Fact]
        public void HeaderMissing_ProtocolVersion_Is400()
        {
            var req = Req("tools/list", null, 1); req.Headers.Remove("MCP-Protocol-Version");
            var r = NewServer().Handle(req);
            Assert.Equal(400, r.Status);
            Assert.Equal(McpServer.ErrHeaderMismatch, Body(r)["error"].Value<int>("code"));
        }

        [Fact]
        public void HeaderMismatch_Name_Is400_AndBase64SentinelIsDecoded()
        {
            var bad = Call("timberborn_get_summary", null); bad.Headers["Mcp-Name"] = "other";
            var r = NewServer().Handle(bad);
            Assert.Equal(400, r.Status);
            Assert.Equal(McpServer.ErrHeaderMismatch, Body(r)["error"].Value<int>("code"));

            var ok = Call("timberborn_get_summary", null);
            ok.Headers["Mcp-Name"] = "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("timberborn_get_summary")) + "?=";
            Assert.Equal(200, NewServer().Handle(ok).Status);
        }

        [Fact]
        public void UnsupportedVersion_Is400WithSupportedList()
        {
            var r = NewServer().Handle(Req("tools/list", null, 1, version: "2025-11-25"));
            Assert.Equal(400, r.Status);
            var err = Body(r)["error"];
            Assert.Equal(McpServer.ErrUnsupportedVersion, err.Value<int>("code"));
            Assert.Equal(McpServer.ProtocolVersion, err["data"]["supported"][0].Value<string>());
            Assert.Equal("2025-11-25", err["data"].Value<string>("requested"));
        }

        [Fact]
        public void UnknownMethod_Is404_AndInitializeNamesVersion()
        {
            var r = NewServer().Handle(Req("resources/list", null, 1));
            Assert.Equal(404, r.Status);
            Assert.Equal(McpServer.ErrMethodNotFound, Body(r)["error"].Value<int>("code"));

            var init = NewServer().Handle(Req("initialize", null, 2));
            Assert.Equal(404, init.Status);
            Assert.Contains(McpServer.ProtocolVersion, Body(init)["error"].Value<string>("message"));
        }

        [Fact]
        public void Notification_Is202Empty()
        {
            var r = NewServer().Handle(Req("notifications/whatever", null, null));
            Assert.Equal(202, r.Status);
            Assert.Equal("", r.Body);
        }

        [Fact]
        public void ParseError_Is400()
        {
            var req = new McpHttpRequest { Body = "{not json" };
            var r = NewServer().Handle(req);
            Assert.Equal(400, r.Status);
            Assert.Equal(McpServer.ErrParse, Body(r)["error"].Value<int>("code"));
        }

        // ---- tools/list ---------------------------------------------------------------

        [Fact]
        public void ToolsList_IsDeterministicCacheableAndAnnotated()
        {
            var s = NewServer();
            var a = Body(s.Handle(Req("tools/list", null, 1)))["result"];
            var b = Body(s.Handle(Req("tools/list", null, 2)))["result"];
            Assert.Equal(a["tools"].ToString(), b["tools"].ToString());
            Assert.Equal(McpCatalog.Tools.Count, ((JArray)a["tools"]).Count);
            Assert.True(a.Value<int>("ttlMs") > 0);
            Assert.Equal("private", a.Value<string>("cacheScope"));
            string prev = null;
            foreach (var t in a["tools"])
            {
                var name = t.Value<string>("name");
                Assert.StartsWith(McpCatalog.Prefix, name);
                if (prev != null) Assert.True(string.CompareOrdinal(prev, name) < 0, "tools must be sorted by name");
                prev = name;
                Assert.False(t["inputSchema"].Value<bool>("additionalProperties"));
                Assert.Equal("object", t["inputSchema"].Value<string>("type"));
                foreach (var key in new[] { "readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint" })
                    Assert.Equal(JTokenType.Boolean, t["annotations"][key].Type);
                Assert.False(string.IsNullOrWhiteSpace(t.Value<string>("description")));
            }
        }

        [Fact]
        public void Catalog_DestructiveToolsAreMarkedAndReadToolsAreReadOnly()
        {
            Assert.True(McpCatalog.Find("timberborn_demolish").Annotations().Value<bool>("destructiveHint"));
            Assert.True(McpCatalog.Find("timberborn_unlock_science").Annotations().Value<bool>("destructiveHint"));
            Assert.True(McpCatalog.Find("timberborn_get_summary").Annotations().Value<bool>("readOnlyHint"));
            Assert.True(McpCatalog.Find("timberborn_find_placement").Annotations().Value<bool>("readOnlyHint"));
            Assert.False(McpCatalog.Find("timberborn_place_building").Annotations().Value<bool>("readOnlyHint"));
            Assert.False(McpCatalog.Find("timberborn_place_building").Annotations().Value<bool>("destructiveHint"));
        }

        // ---- tools/call: reads ------------------------------------------------------

        [Fact]
        public void UnknownTool_IsInvalidParams()
        {
            var r = NewServer().Handle(Call("timberborn_nope", null));
            Assert.Equal(McpServer.ErrInvalidParams, Body(r)["error"].Value<int>("code"));
        }

        [Fact]
        public void GetSummary_ReturnsStructuredContentFromSnapshotRoute()
        {
            var r = NewServer().Handle(Call("timberborn_get_summary", null));
            Assert.Equal(200, r.Status);
            var res = Body(r)["result"];
            Assert.Equal("complete", res.Value<string>("resultType"));
            Assert.False(res.Value<bool>("isError"));
            Assert.Equal(3, res["structuredContent"].Value<int>("day"));
            Assert.Equal("text", res["content"][0].Value<string>("type"));
            Assert.Equal(res["structuredContent"].ToString(Formatting.None), res["content"][0].Value<string>("text"));
            Assert.Equal("timberbot", res["_meta"][McpServer.MetaServerInfo].Value<string>("name"));
            Assert.Single(_host.ReadCalls);
            Assert.Equal("/api/summary", _host.ReadCalls[0].route);
            Assert.Equal("json", _host.ReadCalls[0].query["format"]);
        }

        [Fact]
        public void GateClosed_IsToolErrorNotProtocolError()
        {
            _host.GameReady = false;
            var r = NewServer().Handle(Call("timberborn_get_summary", null));
            Assert.Equal(200, r.Status);
            var res = Body(r)["result"];
            Assert.True(res.Value<bool>("isError"));
            Assert.Equal("GAME_NOT_READY", res["structuredContent"].Value<string>("code"));
            Assert.Contains("Launch", res["structuredContent"].Value<string>("hint"));
            Assert.Empty(_host.ReadCalls);
            // discovery still works
            Assert.Equal(200, NewServer().Handle(Req("tools/list", null, 3)).Status);
        }

        [Fact]
        public void InvalidArguments_AreRejected()
        {
            var extra = NewServer().Handle(Call("timberborn_get_buildings", new JObject { ["bogus"] = 1 }));
            Assert.Equal(McpServer.ErrInvalidParams, Body(extra)["error"].Value<int>("code"));
            Assert.Contains("bogus", Body(extra)["error"].Value<string>("message"));

            var range = NewServer().Handle(Call("timberborn_set_speed", new JObject { ["speed"] = 9 }));
            Assert.Equal(McpServer.ErrInvalidParams, Body(range)["error"].Value<int>("code"));

            var type = NewServer().Handle(Call("timberborn_get_buildings", new JObject { ["limit"] = "ten" }));
            Assert.Equal(McpServer.ErrInvalidParams, Body(type)["error"].Value<int>("code"));

            var missing = NewServer().Handle(Call("timberborn_place_building", new JObject { ["prefab"] = "Path" }));
            Assert.Equal(McpServer.ErrInvalidParams, Body(missing)["error"].Value<int>("code"));

            var en = NewServer().Handle(Call("timberborn_get_region", new JObject { ["x1"] = 0, ["y1"] = 0, ["x2"] = 1, ["y2"] = 1, ["format"] = "xml" }));
            Assert.Equal(McpServer.ErrInvalidParams, Body(en)["error"].Value<int>("code"));
        }

        [Fact]
        public void GetBuildings_MapsArgumentsToQueryWithDefaultLimit()
        {
            var r = NewServer().Handle(Call("timberborn_get_buildings", new JObject { ["name"] = "Pump", ["x"] = 10, ["y"] = 20, ["radius"] = 5 }));
            Assert.Equal(200, r.Status);
            var q = _host.ReadCalls[0].query;
            Assert.Equal("Pump", q["name"]); Assert.Equal("10", q["x"]); Assert.Equal("20", q["y"]); Assert.Equal("5", q["radius"]);
            Assert.Equal("50", q["limit"]);
            Assert.Equal(42, Body(r)["result"]["structuredContent"]["items"][0].Value<int>("id"));
        }

        [Fact]
        public void GetPrefabs_RunsOnTheGameThreadAndFiltersOnCompletion()
        {
            var s = NewServer();
            var r = s.Handle(Call("timberborn_get_prefabs", new JObject { ["name"] = "folktails", ["limit"] = 1 }));
            Assert.True(r.IsDeferred);
            Assert.Equal(McpCatalog.PrefabsRoute, r.Deferred.Route);
            Assert.True(McpCatalog.Find("timberborn_get_prefabs").Annotations().Value<bool>("readOnlyHint"));
            var wrapped = JObject.Parse(s.WrapToolResult(r.Deferred, 200, _host.Reads["/api/prefabs"]));
            var sc = wrapped["result"]["structuredContent"];
            Assert.Equal(2, sc.Value<int>("total"));
            Assert.Equal(1, sc.Value<int>("count"));
            Assert.True(sc.Value<bool>("has_more"));
            Assert.Equal("WaterPump.Folktails", sc["items"][0].Value<string>("name"));
        }

        [Fact]
        public void GetRegion_MapFormatRendersTextAndCapsSize()
        {
            var r = NewServer().Handle(Call("timberborn_get_region", new JObject { ["x1"] = 10, ["y1"] = 20, ["x2"] = 13, ["y2"] = 22 }));
            var res = Body(r)["result"];
            Assert.False(res.Value<bool>("isError"));
            var map = res["content"][0].Value<string>("text");
            Assert.Contains("region x10..13 y20..22", map);
            Assert.Contains("legend:", map);
            Assert.Contains("~", map);                 // water column at x1
            Assert.Contains("L", map);                 // lumberjack at (x2,y2)
            Assert.Contains("T", map);                 // pine
            Assert.DoesNotContain("\u001b", map, StringComparison.Ordinal);      // no ANSI escapes
            Assert.Equal(map, res["structuredContent"].Value<string>("map"));
            Assert.Null(res["structuredContent"]["tiles"]);
            Assert.Equal("json", _host.ReadCalls[0].query["format"]);

            var big = NewServer().Handle(Call("timberborn_get_region", new JObject { ["x1"] = 0, ["y1"] = 0, ["x2"] = 99, ["y2"] = 99, ["format"] = "json" }));
            var bres = Body(big)["result"];
            Assert.True(bres.Value<bool>("isError"));
            Assert.Equal("INVALID_PARAM", bres["structuredContent"].Value<string>("code"));
            Assert.Contains("20x20", bres["structuredContent"].Value<string>("reason"));

            var both = NewServer().Handle(Call("timberborn_get_region", new JObject { ["x1"] = 0, ["y1"] = 0, ["x2"] = 2, ["y2"] = 2, ["format"] = "both" }));
            var bothRes = Body(both)["result"];
            Assert.Equal(2, ((JArray)bothRes["content"]).Count);
            Assert.Equal(9, ((JArray)bothRes["structuredContent"]["tiles"]).Count);
        }

        // ---- tools/call: writes -----------------------------------------------------

        [Fact]
        public void PlaceBuilding_IsDeferredToTheWriteQueue()
        {
            var r = NewServer().Handle(Call("timberborn_place_building", new JObject { ["prefab"] = "Path", ["x"] = 10, ["y"] = 11, ["z"] = 2 }, id: 99));
            Assert.True(r.IsDeferred);
            Assert.Equal("/api/building/place", r.Deferred.Route);
            Assert.Equal(99, r.Deferred.RequestId.Value<int>());
            Assert.Equal("Path", r.Deferred.Body.Value<string>("prefab"));
            Assert.Equal(2, r.Deferred.Body.Value<int>("z"));
            Assert.Equal("json", r.Deferred.Body.Value<string>("format"));
            Assert.Null(r.Deferred.Body["orientation"]); // absent -> route default "south"
        }

        [Fact]
        public void WrapToolResult_SuccessAndLegibleFailure()
        {
            var s = NewServer();
            var d = s.Handle(Call("timberborn_place_building", new JObject { ["prefab"] = "Path", ["x"] = 10, ["y"] = 11, ["z"] = 2 }, id: 5)).Deferred;

            var ok = JObject.Parse(s.WrapToolResult(d, 200, "{\"id\":123,\"name\":\"Path\",\"x\":10,\"y\":11,\"z\":2}"));
            Assert.Equal(5, ok.Value<int>("id"));
            Assert.False(ok["result"].Value<bool>("isError"));
            Assert.Equal(123, ok["result"]["structuredContent"].Value<int>("id"));

            var fail = JObject.Parse(s.WrapToolResult(d, 200,
                "{\"error\":\"occupied by Path at (10,11,2). demolish it or try a different location\",\"x\":10,\"y\":11,\"z\":2,\"prefab\":\"LumberjackFlag.IronTeeth\"}"));
            var sc = fail["result"]["structuredContent"];
            Assert.True(fail["result"].Value<bool>("isError"));
            Assert.False(sc.Value<bool>("ok"));
            Assert.Equal("PLACEMENT_OCCUPIED", sc.Value<string>("code"));
            Assert.Equal("occupied by Path at (10,11,2)", sc.Value<string>("reason"));
            Assert.Equal("demolish it or try a different location", sc.Value<string>("hint"));
            Assert.Equal(10, sc["at"].Value<int>("x")); Assert.Equal(11, sc["at"].Value<int>("y")); Assert.Equal(2, sc["at"].Value<int>("z"));
            Assert.Equal("LumberjackFlag.IronTeeth", sc["details"].Value<string>("prefab"));
            Assert.Contains("PLACEMENT_OCCUPIED", fail["result"]["content"][0].Value<string>("text"));

            var crash = JObject.Parse(s.WrapToolResult(d, 500, "{\"error\":\"internal_error: boom\"}"));
            Assert.Equal(McpServer.ErrInternal, crash["error"].Value<int>("code"));

            var anon = JObject.Parse(s.WrapToolResult(d, 200, new { speed = 2 }));
            Assert.Equal(2, anon["result"]["structuredContent"].Value<int>("speed"));
        }

        [Fact]
        public void WaitFrames_IsDeferredWithFrameCount()
        {
            var r = NewServer().Handle(Call("timberborn_wait_frames", new JObject { ["frames"] = 30 }));
            Assert.True(r.IsDeferred);
            Assert.Equal(McpCatalog.WaitRoute, r.Deferred.Route);
            Assert.Equal(30, r.Deferred.WaitFrames);
        }

        [Fact]
        public void FindPlacement_IsQueuedButReadOnly()
        {
            var r = NewServer().Handle(Call("timberborn_find_placement", new JObject { ["prefab"] = "WaterPump.Folktails", ["x"] = 50, ["y"] = 50, ["radius"] = 10 }));
            Assert.True(r.IsDeferred);
            Assert.Equal("/api/placement/find", r.Deferred.Route);
            Assert.Equal(10, r.Deferred.Body.Value<int>("radius"));
        }

        // ---- MRTR confirmation --------------------------------------------------------

        [Fact]
        public void Demolish_WithElicitationClient_RequiresRoundTrip()
        {
            var s = NewServer();
            var args = new JObject { ["id"] = 42 };
            var first = s.Handle(Call("timberborn_demolish", args, id: 1, elicitation: true));
            Assert.False(first.IsDeferred);
            var res = Body(first)["result"];
            Assert.Equal("input_required", res.Value<string>("resultType"));
            Assert.Equal("elicitation/create", res["inputRequests"]["confirm"].Value<string>("method"));
            Assert.Equal("form", res["inputRequests"]["confirm"]["params"].Value<string>("mode"));
            var state = res.Value<string>("requestState");
            Assert.False(string.IsNullOrEmpty(state));

            var accept = new JObject { ["confirm"] = new JObject { ["action"] = "accept", ["content"] = new JObject { ["confirm"] = true } } };
            var second = s.Handle(Call("timberborn_demolish", args, id: 2, elicitation: true, requestState: state, inputResponses: accept));
            Assert.True(second.IsDeferred);
            Assert.Equal("/api/building/demolish", second.Deferred.Route);
            Assert.Null(second.Deferred.Body["confirm"]);

            var decline = new JObject { ["confirm"] = new JObject { ["action"] = "decline" } };
            var third = s.Handle(Call("timberborn_demolish", args, id: 3, elicitation: true, requestState: state, inputResponses: decline));
            Assert.False(third.IsDeferred);
            Assert.Equal("CONFIRMATION_DECLINED", Body(third)["result"]["structuredContent"].Value<string>("code"));
        }

        [Fact]
        public void Demolish_TamperedOrExpiredOrForeignStateNeverExecutes()
        {
            var s = NewServer();
            var args = new JObject { ["id"] = 42 };
            var state = Body(s.Handle(Call("timberborn_demolish", args, id: 1, elicitation: true)))["result"].Value<string>("requestState");
            var accept = new JObject { ["confirm"] = new JObject { ["action"] = "accept" } };

            var tampered = state.Substring(0, state.Length - 2) + (state.EndsWith("A") ? "BB" : "AA");
            var r1 = s.Handle(Call("timberborn_demolish", args, id: 2, elicitation: true, requestState: tampered, inputResponses: accept));
            Assert.False(r1.IsDeferred);
            Assert.Equal("input_required", Body(r1)["result"].Value<string>("resultType"));

            // same state, different arguments -> digest mismatch
            var r2 = s.Handle(Call("timberborn_demolish", new JObject { ["id"] = 43 }, id: 3, elicitation: true, requestState: state, inputResponses: accept));
            Assert.False(r2.IsDeferred);

            // same state, different tool
            var r3 = s.Handle(Call("timberborn_unlock_science", new JObject { ["building"] = "X" }, id: 4, elicitation: true, requestState: state, inputResponses: accept));
            Assert.False(r3.IsDeferred);

            // expired
            _now = _now.AddSeconds(McpServer.ConfirmTtlSeconds + 1);
            var r4 = s.Handle(Call("timberborn_demolish", args, id: 5, elicitation: false, requestState: state, inputResponses: accept));
            Assert.False(r4.IsDeferred);
            Assert.Equal("CONFIRMATION_EXPIRED", Body(r4)["result"]["structuredContent"].Value<string>("code"));

            // a different server instance (different key) rejects it
            var other = new McpServer(_host, Encoding.UTF8.GetBytes("ffffffffffffffffffffffffffffffff"), () => _now);
            _now = _now.AddSeconds(-(McpServer.ConfirmTtlSeconds + 1));
            Assert.False(other.Handle(Call("timberborn_demolish", args, id: 6, elicitation: true, requestState: state, inputResponses: accept)).IsDeferred);
        }

        [Fact]
        public void Demolish_WithPlainClient_NeedsExplicitConfirmFlag()
        {
            var s = NewServer();
            var r = s.Handle(Call("timberborn_demolish", new JObject { ["id"] = 42 }));
            Assert.False(r.IsDeferred);
            var sc = Body(r)["result"]["structuredContent"];
            Assert.Equal("CONFIRMATION_REQUIRED", sc.Value<string>("code"));
            Assert.Contains("confirm=true", sc.Value<string>("hint"));

            var ok = s.Handle(Call("timberborn_demolish", new JObject { ["id"] = 42, ["confirm"] = true }));
            Assert.True(ok.IsDeferred);
            Assert.Equal(42, ok.Deferred.Body.Value<int>("id"));
            Assert.Null(ok.Deferred.Body["confirm"]);

            var no = s.Handle(Call("timberborn_demolish", new JObject { ["id"] = 42, ["confirm"] = false }));
            Assert.False(no.IsDeferred);
        }

        [Fact]
        public void NonDestructiveWrite_DoesNotAskForConfirmation()
        {
            var r = NewServer().Handle(Call("timberborn_set_speed", new JObject { ["speed"] = 0 }, elicitation: true));
            Assert.True(r.IsDeferred);
            Assert.Equal("/api/speed", r.Deferred.Route);
        }
    }

    public class TimberbotErrorsTests
    {
        [Fact]
        public void PlacementOccupied_IsSplitIntoCodeReasonHintAt()
        {
            Assert.True(TimberbotErrors.TryParse(
                "{\"error\":\"occupied by Path at (120,130,2). demolish it or try a different location\",\"x\":120,\"y\":130,\"z\":2,\"prefab\":\"LumberjackFlag.IronTeeth\"}", out var e));
            Assert.Equal("PLACEMENT_OCCUPIED", e.Code);
            Assert.Equal("occupied by Path at (120,130,2)", e.Reason);
            Assert.Equal("demolish it or try a different location", e.Hint);
            Assert.Equal((120, 130, 2), (e.X.Value, e.Y.Value, e.Z.Value));
            Assert.Equal("LumberjackFlag.IronTeeth", e.Details.Value<string>("prefab"));
            var json = e.ToJson();
            Assert.False(json.Value<bool>("ok"));
            Assert.Equal(130, json["at"].Value<int>("y"));
        }

        [Fact]
        public void PrefixedLegacyMessages_MapToStableCodes()
        {
            Assert.True(TimberbotErrors.TryParse("{\"error\":\"invalid_param: speed must be 0-3 (0=pause, 1=normal, 2=fast, 3=fastest)\",\"got\":5}", out var e));
            Assert.Equal("INVALID_PARAM", e.Code);
            Assert.Equal("speed must be 0-3 (0=pause, 1=normal, 2=fast, 3=fastest)", e.Reason);
            Assert.Null(e.Hint);
            Assert.Equal(5, e.Details.Value<int>("got"));
            Assert.Null(e.X);

            Assert.True(TimberbotErrors.TryParse("{\"error\":\"not_found: no entity with this id. ids are ephemeral, re-query buildings\",\"id\":-1}", out var nf));
            Assert.Equal("NOT_FOUND", nf.Code);
            Assert.Equal("no entity with this id", nf.Reason);
            Assert.Equal("ids are ephemeral, re-query buildings", nf.Hint);

            Assert.True(TimberbotErrors.TryParse("{\"error\":\"insufficient_science: not enough science points to unlock\",\"building\":\"Engine.IronTeeth\",\"scienceCost\":600,\"currentPoints\":450}", out var sci));
            Assert.Equal("INSUFFICIENT_SCIENCE", sci.Code);
            Assert.Equal(600, sci.Details.Value<int>("scienceCost"));

            Assert.True(TimberbotErrors.TryParse(TimberbotAgentState.GameNotReadyJson, out var gate));
            Assert.Equal("GAME_NOT_READY", gate.Code);
            Assert.Equal("player must press Launch in the Timberbot widget", gate.Hint);

            Assert.True(TimberbotErrors.TryParse("{\"error\":\"something completely new happened\"}", out var unk));
            Assert.Equal("UNKNOWN_ERROR", unk.Code);
            Assert.Equal("something completely new happened", unk.Reason);
        }

        [Fact]
        public void NonErrorPayloads_AreNotErrors()
        {
            Assert.False(TimberbotErrors.TryParse("{\"id\":5,\"name\":\"Path\"}", out _));
            Assert.False(TimberbotErrors.TryParse("[1,2,3]", out _));
            Assert.False(TimberbotErrors.TryParse("{\"error\":{\"nested\":true}}", out _));
            Assert.False(TimberbotErrors.TryParse("not json", out _));
        }

        [Fact]
        public void PartialCoordinates_StayInDetails()
        {
            Assert.True(TimberbotErrors.TryParse("{\"error\":\"invalid_param: bad\",\"x\":3}", out var e));
            Assert.Null(e.X);
            Assert.Equal(3, e.Details.Value<int>("x"));
        }
    }

    public class TimberbotMapTextTests
    {
        private static JObject Tiles(params (int x, int y, int terrain, double water, string occ, int entrance, int dead)[] cells)
        {
            var arr = new JArray();
            foreach (var c in cells)
            {
                var t = new JObject { ["x"] = c.x, ["y"] = c.y, ["terrain"] = c.terrain, ["water"] = c.water, ["badwater"] = 0.0,
                    ["entrance"] = c.entrance, ["seedling"] = 0, ["dead"] = c.dead, ["contaminated"] = 0, ["moist"] = 0 };
                t["occupants"] = c.occ == null ? new JArray() : new JArray(new JObject { ["name"] = c.occ, ["z"] = c.terrain });
                arr.Add(t);
            }
            return new JObject { ["mapSize"] = new JObject { ["x"] = 8, ["y"] = 8, ["z"] = 12 }, ["tiles"] = arr };
        }

        [Fact]
        public void RendersNorthUpWithLegendAndDeterministicGlyphs()
        {
            var tiles = Tiles((0, 0, 3, 0, null, 0, 0), (1, 0, 3, 1.2, null, 0, 0), (0, 1, 4, 0, "Path", 0, 0), (1, 1, 4, 0, "LumberjackFlag.Folktails", 0, 0));
            var text = TimberbotMapText.Render(tiles, 0, 0, 1, 1);
            var lines = text.Split('\n');
            Assert.StartsWith("region x0..1 y0..1", lines[0]);
            Assert.Equal("  1 =L", lines[1]);   // y=1 row first (north up): path, lumberjack
            Assert.Equal("  0 3~", lines[2]);   // y=0: terrain 3, water
            Assert.Equal("    01", lines[3]);   // x axis
            Assert.Contains("==path", lines[4]);
            Assert.Contains("L=lumberjack", lines[4]);
            Assert.Contains("~=water", lines[4]);
            Assert.Contains("z levels present: 3 4", lines[5]);
            Assert.Equal(text, TimberbotMapText.Render(tiles, 0, 0, 1, 1));
        }

        [Fact]
        public void MissingTilesEntrancesAndToonOccupantsAreHandled()
        {
            var tiles = Tiles((0, 0, 0, 0, null, 1, 0), (1, 0, 5, 0, "Pine", 0, 1));
            ((JObject)tiles["tiles"][1])["occupants"] = "Pine:5+Platform:6";
            var text = TimberbotMapText.Render(tiles, 0, 0, 2, 0);
            Assert.Contains("  0 @+?", text);   // entrance, dead tree (dead wins over the occupant glyph), missing tile
            Assert.Contains("?=no data", text);
        }

        [Fact]
        public void UnknownOccupant_FallsBackToInitial()
        {
            Assert.Equal(('Z', "building: ZiplineTower"), TimberbotMapText.Symbol("ZiplineTower.IronTeeth"));
            Assert.Equal(('=', "path"), TimberbotMapText.Symbol("Path"));
            Assert.Equal(('X', "dam/levee/floodgate"), TimberbotMapText.Symbol("Levee"));
        }
    }
}
