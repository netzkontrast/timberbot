// WardensMcpServer.cs. An MCP server that runs inside the game.
//
// Model Context Protocol, Streamable HTTP transport, JSON-RPC 2.0 over POST /mcp on
// http://127.0.0.1:<mcpPort>/ (default 8090). Claude Code connects to it straight from the
// repo's .mcp.json; no Python in between. `ping`, `tools/list` and `prompts/list` are answered
// on the listener thread; everything that reads game state -- `tools/call`, `initialize` and
// `prompts/get` -- is parked on a queue for the main thread (same model as TimberbotHttpServer:
// listener thread accepts, UpdateSingleton drains, the response is written from the thread pool).
// Tools that only do I/O (chat long-poll, Timberbot loopback) are marked OffThread and answered
// directly.
//
// `prompts/list` and `prompts/get` serve the Warden's boot prompt and the level briefing
// (WardensMcpTools, "prompts"): a client that supports prompts can put the whole playbook in
// front of the model before the first tool call. The `instructions` field of the initialize
// reply carries the short version, built from live state when the request is served.
//
// Not implemented on purpose: the optional GET SSE stream (server-initiated messages;
// answered 405), sessions (Mcp-Session-Id) and resources. Tool results carry the JSON both
// as text content and as structuredContent, plus "chat": player messages typed in the
// in-game panel that the agent has not seen yet.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensSettings
    {
        public int McpPort = 8090;
        public bool McpEnabled = true;
        public int HttpPort = 8085;          // Timberbot's port, for loopback tools
        public string AuthToken = "";        // Timberbot's bearer token, if any
        public bool ChapterGating = true;    // WardensChapters.cs: false opens every chapter at load
        public bool Cutscenes = true;        // WardensCutscenes.cs: false keeps the scene triggers off (MCP play still works)
        public bool InstallMaps = true;      // WardensMapInstaller.cs: false leaves Documents/Timberborn/Maps alone

        // Fixed for the process, and Environment.GetFolderPath is a shell call, not a field read:
        // resolve both once. ModDir is where every file this mod owns lives (settings.json,
        // story.json, campaign.json, docs/, Maps/).
        public static readonly string ModDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Timberborn", "Mods", "Wardens");

        public static readonly string Path = System.IO.Path.Combine(ModDir, "settings.json");

        public static WardensSettings Load()
        {
            var s = new WardensSettings();
            try
            {
                if (File.Exists(Path))
                {
                    var json = JObject.Parse(File.ReadAllText(Path));
                    s.McpPort = json.Value<int?>("mcpPort") ?? s.McpPort;
                    s.McpEnabled = json.Value<bool?>("mcpEnabled") ?? s.McpEnabled;
                    s.HttpPort = json.Value<int?>("httpPort") ?? s.HttpPort;
                    s.AuthToken = (json.Value<string>("authToken") ?? "").Trim();
                    s.ChapterGating = json.Value<bool?>("chapterGating") ?? s.ChapterGating;
                    s.Cutscenes = json.Value<bool?>("cutscenes") ?? s.Cutscenes;
                    s.InstallMaps = json.Value<bool?>("installMaps") ?? s.InstallMaps;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] settings.json: " + ex.Message);
            }
            return s;
        }
    }

    public class WardensMcpServer : ILoadableSingleton, IUpdatableSingleton, IUnloadableSingleton
    {
        public const string Version = "0.3.9";
        private static readonly string[] SupportedProtocolVersions = { "2024-11-05", "2025-03-26", "2025-06-18" };

        // A request the listener thread parked for the main thread. `Respond` returns the finished
        // JSON-RPC response and is only ever invoked from UpdateSingleton, so anything it closes
        // over may touch game state freely.
        private class PendingCall
        {
            public HttpListenerContext Ctx;
            public JToken Id;
            public Func<JObject> Respond;
        }

        private readonly WardensMcpTools _tools;
        private readonly WardensChat _chat;
        private readonly ConcurrentQueue<PendingCall> _pending = new ConcurrentQueue<PendingCall>();
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public WardensSettings Settings { get; private set; }
        public int Port => Settings?.McpPort ?? 8090;
        public bool Running => _running;

        public WardensMcpServer(WardensMcpTools tools, WardensChat chat)
        {
            _tools = tools;
            _chat = chat;
        }

        public void Load()
        {
            Settings = WardensSettings.Load();
            _tools.Initialize(Settings);
            if (!Settings.McpEnabled)
            {
                Debug.Log("[Wardens] MCP server disabled in settings.json (mcpEnabled=false)");
                return;
            }
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop) { IsBackground = true, Name = "Wardens-MCP" };
                _thread.Start();
                Debug.Log($"[Wardens] MCP server listening on http://127.0.0.1:{Port}/mcp");
                _chat.SystemSays($"MCP server up on 127.0.0.1:{Port}. An agent can connect now.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Wardens] MCP server failed to start on port {Port}: {ex.Message}");
            }
        }

        public void Unload()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            while (_pending.TryDequeue(out var call))
                TryWrite(call.Ctx, 200, JsonRpcError(call.Id, -32000, "game unloading"));
        }

        // ---- listener thread ---------------------------------------------------------------

        private void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { if (!_running) break; continue; }
                try { Handle(ctx); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Wardens] MCP request failed: " + ex.Message);
                    TryWrite(ctx, 200, JsonRpcError(null, -32603, ex.Message));
                }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var method = ctx.Request.HttpMethod.ToUpperInvariant();

            if (path == "")
            {
                WriteText(ctx, 200, "text/plain",
                    $"Wardens MCP server {Version}. POST JSON-RPC to /mcp (MCP Streamable HTTP). Tools: {_tools.Count}.");
                return;
            }
            if (path != "/mcp") { WriteText(ctx, 404, "text/plain", "not found"); return; }

            switch (method)
            {
                case "OPTIONS":
                    ctx.Response.Headers["Allow"] = "POST, OPTIONS, DELETE";
                    WriteEmpty(ctx, 204);
                    return;
                case "GET":
                    ctx.Response.Headers["Allow"] = "POST";
                    WriteText(ctx, 405, "text/plain", "no server-to-client stream; POST JSON-RPC here");
                    return;
                case "DELETE":
                    WriteEmpty(ctx, 200);   // session end: nothing to tear down
                    return;
                case "POST":
                    break;
                default:
                    WriteText(ctx, 405, "text/plain", "method not allowed");
                    return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();

            JToken msg;
            try { msg = JToken.Parse(body); }
            catch (JsonException ex) { WriteJson(ctx, 200, JsonRpcError(null, -32700, "parse error: " + ex.Message)); return; }

            if (msg is JArray batch)
            {
                var responses = new JArray();
                foreach (var item in batch)
                {
                    if (!(item is JObject o)) continue;
                    if (NeedsMainThread(o))
                    {
                        WriteJson(ctx, 200, JsonRpcError(o["id"], -32600,
                            $"batched {(string)o["method"]} is not supported; one request per POST"));
                        return;
                    }
                    if (IsNotification(o)) continue;
                    responses.Add(DispatchImmediate(o));
                }
                if (responses.Count == 0) WriteEmpty(ctx, 202); else WriteJson(ctx, 200, responses);
                return;
            }

            if (!(msg is JObject req)) { WriteJson(ctx, 200, JsonRpcError(null, -32600, "invalid request")); return; }
            if (IsNotification(req)) { WriteEmpty(ctx, 202); return; }   // notifications/initialized etc.
            if (NeedsMainThread(req)) { Enqueue(ctx, req); return; }
            WriteJson(ctx, 200, DispatchImmediate(req));
        }

        // `initialize` and `prompts/get` quote live game state (the faction, the level, the
        // population, the ready gate), so they go through the same main-thread queue as a tool
        // call rather than reading a snapshot from the listener thread.
        private static bool NeedsMainThread(JObject o)
        {
            switch ((string)o["method"])
            {
                case "tools/call":
                case "initialize":
                case "prompts/get":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsNotification(JObject o) => o["id"] == null || o["id"].Type == JTokenType.Null;

        private JObject DispatchImmediate(JObject req)
        {
            var id = req["id"];
            var method = (string)req["method"] ?? "";
            var p = req["params"] as JObject ?? new JObject();
            switch (method)
            {
                case "ping":
                    return Result(id, new JObject());
                case "tools/list":
                    return Result(id, new JObject { ["tools"] = _tools.ListJson() });
                case "prompts/list":
                    return Result(id, new JObject { ["prompts"] = _tools.ListPromptsJson() });
                default:
                    return JsonRpcError(id, -32601, "method not found: " + method);
            }
        }

        // Park the request for the main thread. A tool with OffThread set is pure I/O and answers
        // here instead, which is what keeps the long-polls (`frame`, `chat_read`) off the queue.
        private void Enqueue(HttpListenerContext ctx, JObject req)
        {
            var id = req["id"];
            var p = req["params"] as JObject ?? new JObject();
            Func<JObject> respond;

            if ((string)req["method"] == "tools/call")
            {
                var name = (string)p["name"] ?? "";
                var args = p["arguments"] as JObject ?? new JObject();
                var tool = _tools.Find(name);
                if (tool == null) { WriteJson(ctx, 200, JsonRpcError(id, -32602, "unknown tool: " + name)); return; }
                if (tool.OffThread) { WriteJson(ctx, 200, RunTool(id, tool, args)); return; }
                respond = () => RunTool(id, tool, args);
            }
            else if ((string)req["method"] == "initialize")
            {
                respond = () => Result(id, InitializeResult(p));
            }
            else
            {
                var name = (string)p["name"] ?? "";
                respond = () => Result(id, _tools.GetPromptJson(name));
            }
            _pending.Enqueue(new PendingCall { Ctx = ctx, Id = id, Respond = respond });
        }

        private JObject InitializeResult(JObject p) => new JObject
        {
            ["protocolVersion"] = Negotiate((string)p["protocolVersion"]),
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["prompts"] = new JObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JObject { ["name"] = "wardens", ["version"] = Version },
            ["instructions"] = _tools.BuildInstructions(),
        };

        // ---- main thread ----------------------------------------------------------------------

        public void UpdateSingleton()
        {
            if (!_running) return;
            int n = 0;
            while (n++ < 8 && _pending.TryDequeue(out var call))
            {
                JObject response;
                try { response = call.Respond(); }
                catch (ArgumentException ex) { response = JsonRpcError(call.Id, -32602, ex.Message); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Wardens] MCP request failed on the main thread: " + ex);
                    response = JsonRpcError(call.Id, -32603, ex.Message);
                }
                var ctx = call.Ctx;
                ThreadPool.QueueUserWorkItem(_ => TryWrite(ctx, 200, response));
            }
        }

        private JObject RunTool(JToken id, McpTool tool, JObject args)
        {
            JObject result;
            bool isError = false;
            try
            {
                result = tool.Run(args) ?? new JObject();
            }
            catch (Exception ex)
            {
                isError = true;
                result = new JObject { ["error"] = ex.GetType().Name + ": " + ex.Message };
                Debug.LogWarning($"[Wardens] tool {tool.Name} failed: {ex}");
            }
            if (!isError)
            {
                var chat = _chat.TakeUndelivered();
                if (chat.Count > 0) result["chat"] = chat;
            }
            var content = new JArray { new JObject { ["type"] = "text", ["text"] = result.ToString(Formatting.None) } };
            return Result(id, new JObject
            {
                ["content"] = content,
                ["structuredContent"] = result,
                ["isError"] = isError,
            });
        }

        // ---- JSON-RPC helpers ------------------------------------------------------------------

        private static JObject Result(JToken id, JToken result) => new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone() ?? JValue.CreateNull(),
            ["result"] = result,
        };

        private static JObject JsonRpcError(JToken id, int code, string message) => new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone() ?? JValue.CreateNull(),
            ["error"] = new JObject { ["code"] = code, ["message"] = message },
        };

        private static string Negotiate(string requested)
        {
            foreach (var v in SupportedProtocolVersions) if (v == requested) return v;
            return SupportedProtocolVersions[SupportedProtocolVersions.Length - 1];
        }

        private static void TryWrite(HttpListenerContext ctx, int status, JToken data)
        {
            try { WriteJson(ctx, status, data); }
            catch (Exception ex) { Debug.LogWarning("[Wardens] MCP response failed: " + ex.Message); }
        }

        private static void WriteJson(HttpListenerContext ctx, int status, JToken data)
        {
            WriteText(ctx, status, "application/json", data.ToString(Formatting.None));
        }

        private static void WriteText(HttpListenerContext ctx, int status, string contentType, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = contentType + "; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void WriteEmpty(HttpListenerContext ctx, int status)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentLength64 = 0;
            ctx.Response.OutputStream.Close();
        }
    }
}
