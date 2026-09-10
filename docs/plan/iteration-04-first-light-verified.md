# Iteration 04: First Light, verified

> **For agentic workers:** work this plan package by package (superpowers:subagent-driven-development
> or superpowers:executing-plans; steps use `- [ ]` checkboxes). Packages marked **[game]** need a
> machine with Timberborn installed and cannot be done from a cloud container: do the **[cloud]**
> packages there, push each as its own pull request, and leave the game-side steps to the human with
> the evidence they need, through `docs/plan/HANDOVER.md`. Read that file before this one: it says
> what was done before, what was verified, and why the order below is the order.

**Goal:** A human and the Warden play campaign level 01, *Wardens 01 First Light*, end to end on its
own map, from the New Game screen to the completion toast, with every defect the run finds fixed at
its root and `AGENTS.md`'s "The Wardens: state" rewritten from "not yet verified" to what was seen.

**Architecture:** No new subsystem. Two small fixes in the in-game MCP server (the loopback URL the
`timberbot` tool builds, and a listener loop that handles one request at a time), one new main-thread
tool (`ledger`), one reference `.timber` written by the game's own 1.1 map editor to settle the format
questions, and the proof run itself. Level 02, the level transition (`ILevelStarter`) and the
*Continue campaign* button stay out until level 01 has been seen working.

**Tech stack:** C# `netstandard2.1` (Bindito, publicized game DLLs; compiles only where the game is
installed), xUnit on `net10.0` for Unity-free helpers, Python 3.10+ (`wardens/tools`; `mapsmith` is
standard library, `gen_map.py` needs numpy).

**Spec:** this plan argues from `design/wardens-campaign-design.md` §10 (rollout),
`design/wardens-campaign-map-set.md` §5 and §7, `design/wardens-play.md` §6,
`wardens/playtest/PLAYTEST.md` ("Playtest findings", "Known gaps") and `AGENTS.md` ("The Wardens:
state"). Where this plan and those documents disagree, this plan was written later and from the code;
say so in the document you correct.

## Global constraints

- No Harmony, no patches: every mod behaviour is a singleton in a Bindito context (`AGENTS.md`, Wardens Side).
- Game state is touched on the main thread only. MCP tools that touch it are queued through
  `WardensMcpServer.UpdateSingleton`; `OffThread` tools are I/O only.
- Generators are the source: a hand edit to a generated blueprint, tutorial or map is mirrored in
  `wardens/tools/gen_*.py` in the same commit.
- Before any in-game test and before any push, all of these print no problems:
  `python wardens/tools/validate.py` (needs the game's `Blueprints.zip`; game machine only),
  `python wardens/tools/gen_map.py --check "wardens/src/Maps/Wardens 01 First Light.timber"`,
  `python wardens/tools/mapsmith check wardens/maps/<spec>.map.toml`,
  `python wardens/tools/check_cutscenes.py wardens/src`,
  `uv run --project python --extra dev pytest wardens/tools`.
- The agent contract is five documents that must agree: `wardens/WARDEN.md` (the source),
  `.claude/skills/warden-play/SKILL.md`, `WardensMcpTools.BuildInstructions`, `design/wardens-play.md`
  and the `warden_boot` prompt (which returns `WARDEN.md`). Change `WARDEN.md` and the other four in
  the same commit.
- Pinned and never changed: the map name `Wardens 01 First Light`, seed 3000, size 96.
- `wardens/src/Maps/` holds exactly the `.timber` files that `WardensCampaignService.Levels` marks
  `shipped: true`; `validate.py` fails on anything else.
- Nothing under `wardens/src/*.cs` is compiled by CI. A C# change there is done only after
  `dotnet build wardens/src/Wardens.csproj -c Release` succeeds on the game machine.
- The Timberbot copy in `wardens/src/Timberbot/` is verbatim: fix Timberbot bugs in `timberbot/src`
  and re-copy; never edit the copy.

## 0. Why this iteration, and not level 02

Where the Wardens stand on 2026-09-10, evening (`wardens/CHANGELOG.md`, `AGENTS.md`,
`wardens/playtest/PLAYTEST.md`):

| Built | Seen in the game |
|---|---|
| Faction, 18 tutorials and their stages, chapter gating, art (0.1, 0.2) | the mod loads; the tutorial line and the chapter toasts were never walked on the campaign map |
| Cutscenes: 8 scenes, runner, overlay, story record (0.3.0) | never played |
| Level 01 as a level: renamed map, `gen_map.py` registry with a contract, map installer, campaign service, `campaign` tool, live-state `initialize` instructions, prompts, the `warden-play` skill (0.3.5) | the mod loads, the installer runs at the main menu, `initialize` and `prompts/*` answer, `campaign` says "not a campaign level" on a vanilla map. **A new game on `Wardens 01 First Light` has never been started.** |
| mapsmith and two specs (the wasteland as data, a level-02 prototype) | never loaded |

The one playtest through the MCP loop (PLAYTEST.md, 2026-09-10) ran on a map nobody identified,
ended in a softlock (the Core "Flooded.", every bot unemployed, energy falling with nothing to charge
at) and a dropped MCP connection, and reported four Timberbot API bugs. Reading the code in the
planning session traced three of the four to one line in the `timberbot` passthrough (§WP1) and the
dropped connection to the MCP listener handling requests one at a time (§WP2). Neither is fixed.

Everything the campaign design still wants (`wardens-campaign-design.md` §10: level 02, the
transition, the menu button) sits on level 01 being a level that plays. Building level 02 now would
be a third unverified layer on the pile. So this is a **proof iteration**: make level 01 true, fix
what the proof finds, and hand the next iteration a verified base and an honest state section.
Level 10 (*Home*), the map-set document's declared next map, is the optional last package because it
costs a flag and proves the level registry.

## 1. The packages, in order

| # | Package | Runs | Needs | Done when |
|---|---|---|---|---|
| WP1 | Loopback URL: merge a query carried in `path` | **written and unit-tested 2026-09-10**; the game-machine build and the in-game check are open | — | `dotnet test wardens/test` passes; a `timberbot` call with `path=/api/tiles?x1=15&y1=40&x2=30&y2=55` honours all four bounds in the game |
| WP2 | MCP listener: one pool thread per request | **written 2026-09-10**; the game-machine build and the check-script run are open | — | `python wardens/playtest/mcp_concurrency.py` reports `ping` answered while a `frame` waits |
| WP3 | A 1.1-native reference map from the game's map editor | game (20 min) + cloud | — | `.claude/skills/timberborn-mapsmith/references/timber-format.md` "Still open" items 1 and 2 answered with a file; the water encoding is either decoded or recorded as not decodable from one map |
| WP4 | The proof run of level 01 | game | WP1, WP2 deployed | `wardens/playtest/runs/<date>-level-01.md` filled in; `campaign.json` shows `"completed": ["01"]` |
| WP5 | The `ledger` tool and the five-way contract | **written 2026-09-10**; the game-machine build and the in-game check are open | — | one call returns the Ledger line; the five documents agree |
| WP6 | Fix what the run found | cloud + game | WP4 | every finding has a fix or a recorded decision; no open softlock in PLAYTEST.md |
| WP7 | Level numbering: one answer | **done 2026-09-10** | — | one name per level in every document; the prototype spec no longer claims to be level 02 |
| WP8 | Level 10 *Home* as a generator flag (optional) | **done 2026-09-10** (unshipped, not loaded in-game) | — | `gen_map.py --level 10 --out <scratch> ` builds and `--check` is clean; nothing new in `src/Maps` |
| WP9 | Close the iteration | cloud | all | changelog 0.4.0, `AGENTS.md` state rewritten, `HANDOVER.md` has a new entry |

Cloud packages first, in this order: WP1, WP2, WP7, WP5, WP8. They touch different files and can run
in parallel branches. WP3 and WP4 need the human at the game machine; WP4 needs WP1 and WP2 in the
deployed DLL, or the run hits the same two failures as the last one.

---

## WP1: Loopback URL, the query carried in `path` [cloud → game build]

**Root cause (read in the code, not yet reproduced).** `WardensMcpTools.Loopback`
(`wardens/src/WardensMcpTools.cs`, the private method in the implementations region after `Status()`) builds
`http://127.0.0.1:<port><path>?format=json&<query>…`. When the agent puts the query inside `path`,
which is what the playtest did (`path: "/api/tiles?x1=..&y1=..&x2=..&y2=.."`), the URL becomes
`/api/tiles?x1=..&y1=..&x2=..&y2=55?format=json`. `HttpListener` reads the last value as
`55?format=json`, `int.TryParse` fails (`TimberbotHttpServer.cs`, the `QueryString[...]` block), and
`y2` is 0: "whichever param is last is dropped". The same line explains "`offset` ignored" (last
param) and "`/api/buildings?name=ChargingPost` returned `total: 0`" (`name` became
`ChargingPost?format=json`). The playtest's workaround, appending a harmless trailing parameter,
moved the corruption onto a parameter nobody reads.

**Files:**
- Create: `wardens/src/WardensPure.cs` (Unity-free helpers, the `TimberbotPure.cs` pattern)
- Create: `wardens/test/Wardens.Tests.csproj`, `wardens/test/WardensPureTests.cs`
- Modify: `wardens/src/WardensMcpTools.cs` (`Loopback`; the `timberbot` tool description)
- Modify: `.github/workflows/dotnet-tests.yml` (path filter and a second test project)
- Modify: `wardens/playtest/PLAYTEST.md` ("Timberbot API bugs found in this session"), `docs/audit/contradictions.md` (one row)

**Interfaces:**
- Produces: `public static string WardensPure.BuildLoopbackUrl(int port, string path, JObject query)`.
  `path` starts with `/api/` and may carry a query; `query` entries override same-named path entries
  and are appended in order; `format=json` is added only when no `format` is present. Throws
  `ArgumentException` for a path outside `/api/`.

- [x] **Step 1: Write the failing tests**

`wardens/test/WardensPureTests.cs`:

```csharp
using Newtonsoft.Json.Linq;
using Wardens;
using Xunit;

public class WardensPureTests
{
    [Fact]
    public void Query_carried_in_path_is_kept_whole()
    {
        var url = WardensPure.BuildLoopbackUrl(8085, "/api/tiles?x1=10&y1=40&x2=30&y2=55", null);
        Assert.Equal("http://127.0.0.1:8085/api/tiles?x1=10&y1=40&x2=30&y2=55&format=json", url);
    }

    [Fact]
    public void Explicit_query_overrides_the_path_entry()
    {
        var url = WardensPure.BuildLoopbackUrl(8085, "/api/beavers?limit=5&offset=0", new JObject { ["offset"] = 10 });
        Assert.Equal("http://127.0.0.1:8085/api/beavers?limit=5&offset=10&format=json", url);
    }

    [Fact]
    public void Format_is_added_once_and_a_caller_s_format_wins()
    {
        Assert.Equal("http://127.0.0.1:8085/api/summary?format=json", WardensPure.BuildLoopbackUrl(8085, "/api/summary", null));
        Assert.Equal("http://127.0.0.1:8085/api/tiles?format=toon", WardensPure.BuildLoopbackUrl(8085, "/api/tiles?format=toon", null));
    }

    [Fact]
    public void Values_are_escaped()
    {
        var url = WardensPure.BuildLoopbackUrl(8085, "/api/buildings", new JObject { ["name"] = "Charging Post" });
        Assert.Equal("http://127.0.0.1:8085/api/buildings?name=Charging%20Post&format=json", url);
    }

    [Fact]
    public void Path_outside_api_is_refused()
    {
        Assert.Throws<System.ArgumentException>(() => WardensPure.BuildLoopbackUrl(8085, "/mcp", null));
    }
}
```

`wardens/test/Wardens.Tests.csproj` (the `timberbot/test` pattern with only the Unity-free file):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

  <!-- Only sources with no Unity or Timberborn dependency go here. -->
  <ItemGroup>
    <Compile Include="..\src\WardensPure.cs" Link="Shared\WardensPure.cs" />
  </ItemGroup>

</Project>
```

- [x] **Step 2: Run the tests to see them fail**

Run: `dotnet test wardens/test/Wardens.Tests.csproj`
Expected: build error, `WardensPure` does not exist. (No `dotnet` in the container: install the
.NET 10 SDK with Microsoft's `dotnet-install.sh --channel 10.0`; the test project needs no game DLLs.)

- [x] **Step 3: Write the helper**

`wardens/src/WardensPure.cs`:

```csharp
// WardensPure.cs. Helpers with no Unity or game dependency, so they compile in wardens/test.
//
// Keep this file free of UnityEngine and Timberborn.* usings: wardens/test/Wardens.Tests.csproj
// compiles it on net10.0 without the game's assemblies, the way timberbot/test does with
// TimberbotPure.cs.

using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Wardens
{
    public static class WardensPure
    {
        /// The URL the `timberbot` MCP tool calls for a GET. `path` may carry its own query
        /// ("/api/tiles?x1=1&y1=2"); entries in `query` override same-named ones from the path and
        /// are appended in order; `format=json` is added only when no `format` is present.
        /// Before this, a query inside `path` got "?format=json" appended after it, so the last
        /// parameter's value arrived as "55?format=json" and parsed as 0.
        public static string BuildLoopbackUrl(int port, string path, JObject query)
        {
            if (path == null || !path.StartsWith("/api/", StringComparison.Ordinal))
                throw new ArgumentException("path must start with /api/");
            int q = path.IndexOf('?');
            string route = q < 0 ? path : path.Substring(0, q);
            var pairs = new List<KeyValuePair<string, string>>();
            if (q >= 0)
            {
                foreach (var raw in path.Substring(q + 1).Split('&'))
                {
                    if (raw.Length == 0) continue;
                    int eq = raw.IndexOf('=');
                    string key = Uri.UnescapeDataString(eq < 0 ? raw : raw.Substring(0, eq));
                    string value = eq < 0 ? "" : Uri.UnescapeDataString(raw.Substring(eq + 1));
                    pairs.Add(new KeyValuePair<string, string>(key, value));
                }
            }
            if (query != null)
            {
                foreach (var kv in query)
                {
                    pairs.RemoveAll(p => p.Key == kv.Key);
                    pairs.Add(new KeyValuePair<string, string>(kv.Key, kv.Value?.ToString() ?? ""));
                }
            }
            if (!pairs.Exists(p => p.Key == "format"))
                pairs.Add(new KeyValuePair<string, string>("format", "json"));
            var sb = new StringBuilder("http://127.0.0.1:").Append(port).Append(route);
            for (int i = 0; i < pairs.Count; i++)
                sb.Append(i == 0 ? '?' : '&')
                  .Append(Uri.EscapeDataString(pairs[i].Key)).Append('=').Append(Uri.EscapeDataString(pairs[i].Value));
            return sb.ToString();
        }
    }
}
```

- [x] **Step 4: Run the tests to see them pass**

Run: `dotnet test wardens/test/Wardens.Tests.csproj`
Expected: 5 passed.

- [x] **Step 5: Use it in `Loopback`**

In `wardens/src/WardensMcpTools.cs`, `Loopback`: replace the `StringBuilder` block that appends
`?format=json` and the `&k=v` loop with

```csharp
            var url = isPost
                ? $"http://127.0.0.1:{_settings.HttpPort}{path}"
                : WardensPure.BuildLoopbackUrl(_settings.HttpPort, path, query);
            var request = new HttpRequestMessage(isPost ? HttpMethod.Post : HttpMethod.Get, url);
```

and keep the existing `if (!path.StartsWith("/api/", …)) throw` above it for the POST case. In the
`timberbot` tool's description, after "GET with optional query", add: "`path` may carry its own query
(`/api/tiles?x1=10&y1=40&x2=30&y2=55`); `query` entries override it."

- [x] **Step 6: CI**

In `.github/workflows/dotnet-tests.yml` add `wardens/src/WardensPure.cs` and `wardens/test/**` to
the `relevant` filter, and after the existing Test step:

```yaml
      - name: Restore (Wardens pure helpers)
        run: dotnet restore wardens/test/Wardens.Tests.csproj
      - name: Test (Wardens pure helpers)
        run: dotnet test wardens/test/Wardens.Tests.csproj --configuration Release --verbosity normal
```

GitHub Actions is off on this fork (`AGENTS.md`, Quick Reference), so this documents intent; the
local `dotnet test` is the gate.

- [ ] **Step 7: Build on the game machine**

Run: `dotnet build wardens/src/Wardens.csproj -c Release`
Expected: `Deployed Wardens (with Timberbot) to …`. Then, in a loaded game with the ready gate open:
`timberbot method=GET path=/api/tiles?x1=15&y1=40&x2=30&y2=55` returns tiles up to x 30 and y 55, and
`timberbot path=/api/beavers?limit=2&offset=2` returns a different page than `offset=0`.

- [x] **Step 8: Record it**

In `PLAYTEST.md`, "Timberbot API bugs found in this session": replace the first three bullets with
one that names the cause (the passthrough's query concatenation) and the fix (WP1, date). Add a row to
`docs/audit/contradictions.md` in the style of C22: claim "three independent `/api` bugs"; code says one
line in `Loopback`; anchors `wardens/src/WardensMcpTools.cs` (`Loopback`),
`timberbot/src/TimberbotHttpServer.cs` (`QueryString["y2"]`); confidence HIGH once Step 7 has run.

- [x] **Step 9: Commit**

```bash
git add wardens/src/WardensPure.cs wardens/src/WardensMcpTools.cs wardens/test .github/workflows/dotnet-tests.yml wardens/playtest/PLAYTEST.md docs/audit/contradictions.md
git commit -m "Wardens: the timberbot passthrough keeps a query carried in path"
```

---

## WP2: MCP listener, one pool thread per request [cloud → game build]

**Root cause (read in the code).** `WardensMcpServer.ListenLoop` (`wardens/src/WardensMcpServer.cs`)
calls `Handle(ctx)` inline on the single `Wardens-MCP` thread. `frame` and `chat_read` are `OffThread`
tools that block inside `Handle` for up to `wait_seconds` (120 s at most) waiting on
`WardensFrames.Wait` / `WardensChat`. While one of them waits, every other MCP request, including a
`say`, a client's `ping` and a reconnecting client's `initialize`, sits in the socket backlog. A client
with a request timeout reports the server dead. That is what "the MCP connection dropped partway
through night 1 and did not recover" looks like from the agent's side.

What already makes this safe to parallelise: `_pending` is a `ConcurrentQueue<PendingCall>`;
`WardensChat.TakeUndelivered()` and `WardensFrames.Wait()` take their locks; `Instructions` is a
snapshot string replaced whole on the main thread. The one field written by an `OffThread` tool from
the listener side is `WardensMcpTools._lastFrameSeq`.

**Files:**
- Modify: `wardens/src/WardensMcpServer.cs` (`ListenLoop`)
- Modify: `wardens/src/WardensMcpTools.cs` (`_lastFrameSeq` in the `frame` tool; `using System.Threading;`)
- Create: `wardens/playtest/mcp_concurrency.py`
- Modify: `wardens/playtest/PLAYTEST.md` ("Every run" and the findings paragraph)

- [x] **Step 1: The listener**

Replace the body of `ListenLoop` and add `HandleSafely`:

```csharp
        private void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { if (!_running) break; continue; }
                // One request per pool thread. `frame` and `chat_read` long-poll for up to 120 s
                // inside Handle, and while one of them waited on this thread every other request
                // (a `say`, a client's `ping`, a reconnecting client's `initialize`) sat in the
                // socket backlog: from the agent's side the server had died.
                ThreadPool.QueueUserWorkItem(_ => HandleSafely(ctx));
            }
        }

        private void HandleSafely(HttpListenerContext ctx)
        {
            try { Handle(ctx); }
            catch (Exception ex)
            {
                Debug.LogWarning("[Wardens] MCP request failed: " + ex.Message);
                TryWrite(ctx, 200, JsonRpcError(null, -32603, ex.Message));
            }
        }
```

- [x] **Step 2: The one shared field**

In the `frame` tool (`WardensMcpTools.cs`) replace the two uses of `_lastFrameSeq`:

```csharp
                    long after = a["after"] != null && a["after"].Type != JTokenType.Null
                        ? (long)a["after"] : Interlocked.Read(ref _lastFrameSeq);
                    …
                    long seen;
                    do
                    {
                        seen = Interlocked.Read(ref _lastFrameSeq);
                        if (seq <= seen) break;
                    } while (Interlocked.CompareExchange(ref _lastFrameSeq, seq, seen) != seen);
```

- [x] **Step 3: The check script**

`wardens/playtest/mcp_concurrency.py` (standard library only, like `mcp_smoke.py`):

```python
"""Is the MCP server answering while a long-poll waits? Run with a game loaded.

    python wardens/playtest/mcp_concurrency.py          # default http://127.0.0.1:8090/mcp

Starts a `frame` with wait_seconds=20 on one thread and a `ping` on another. Before WP2 the ping
took ~20 s (the listener handled one request at a time); after, under a second. Exit code 1 if the
ping takes longer than 2 s.
"""
from __future__ import annotations

import json
import sys
import threading
import time
import urllib.request

URL = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8090/mcp"


def post(payload: dict, timeout: int = 60) -> dict:
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(), method="POST",
                                 headers={"Content-Type": "application/json", "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main() -> int:
    post({"jsonrpc": "2.0", "id": 1, "method": "initialize",
          "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "concurrency", "version": "0"}}})
    frame = threading.Thread(target=post, args=({"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                                                 "params": {"name": "frame", "arguments": {"wait_seconds": 20, "after": 10 ** 12}}},))
    frame.start()
    time.sleep(0.5)                      # let the frame call reach the server first
    t0 = time.monotonic()
    post({"jsonrpc": "2.0", "id": 3, "method": "ping"})
    took = time.monotonic() - t0
    frame.join()
    print(f"ping answered in {took:.2f} s while a 20 s frame long-poll was pending")
    return 0 if took < 2.0 else 1


if __name__ == "__main__":
    sys.exit(main())
```

`after: 10**12` guarantees the frame call waits the full 20 s (no frame has that seq).

- [ ] **Step 4: Build and run it on the game machine**

Run: `dotnet build wardens/src/Wardens.csproj -c Release`, load any game, then
`python wardens/playtest/mcp_concurrency.py`.
Expected: `ping answered in 0.0x s …`, exit 0. Then `python wardens/playtest/mcp_smoke.py` still passes.

- [x] **Step 5: Record and commit**

Add the script to `PLAYTEST.md` "Every run", and under the 2026-09-10 findings write one sentence:
the drop is explained by the serial listener, fixed in WP2 (date), verified by the script.

```bash
git add wardens/src/WardensMcpServer.cs wardens/src/WardensMcpTools.cs wardens/playtest/mcp_concurrency.py wardens/playtest/PLAYTEST.md
git commit -m "Wardens: the MCP listener answers while a long-poll waits"
```

---

## WP3: A reference map from the 1.1 map editor [game 20 min → cloud]

Every map this repo writes claims `GameVersion 0.7.10.0`, the layout that was verified from a map
that loaded in 0.7.10, and ships with the water map all zeros because the pre-filled encoding is
unknown (`.claude/skills/timberborn-mapsmith/references/timber-format.md`). One file written by the
current game answers the open questions at once, and it costs the human twenty minutes.

**Files:**
- Create: `wardens/maps/reference/reference-1.1-editor.timber` (binary, a few tens of KB; committed)
- Create: `wardens/maps/reference/README.md` (what is in it, which game version wrote it)
- Modify: `.claude/skills/timberborn-mapsmith/references/timber-format.md`, `design/wardens-wasteland.md`
  ("Open questions"), `design/wardens-campaign-map-set.md` (§5, §8)

- [ ] **Step 1 [game]: Make the map**

In Timberborn: Main menu → Map Editor → New map, the smallest size offered. Raise a plateau, dig one
pit three blocks deep and one shallow channel to the map edge, place one **Water Source** at the head
of the channel and one **Badwater Source** beside the pit, one **Starting Location** on flat ground,
one ruin column of each height the palette offers, one **Underground Ruins**, one Pine, one Birch,
one Blueberry Bush, and one of every other object type in the palette (one each is enough). Run the
editor's water simulation (the play button) until the pit holds water and the channel flows. Save as
`Reference 1-1 Editor`. Note the game version from the main menu.

- [ ] **Step 2 [game]: Commit the file**

Copy `Documents/Timberborn/Maps/Reference 1-1 Editor.timber` to
`wardens/maps/reference/reference-1.1-editor.timber`, write the README (game version, what was
placed, that the water was running when saved), commit and push.

- [ ] **Step 3 [cloud]: Read it**

```bash
python - <<'PY'
import json, zipfile
z = zipfile.ZipFile("wardens/maps/reference/reference-1.1-editor.timber")
print(z.namelist()); print(z.read("version.txt"))
w = json.loads(z.read("world.json"))
print(w["GameVersion"]); print(sorted(w["Singletons"]))
for k, v in w["Singletons"].items():
    print(k, {kk: (str(vv)[:80]) for kk, vv in v.items()})
print(sorted({e["Template"] for e in w["Entities"]}))
print({e["Template"]: sorted(e["Components"]) for e in w["Entities"]})
PY
```

- [ ] **Step 4 [cloud]: Write down what changed**

In `timber-format.md`: move every template name seen in the file to the verified list; update the
GameVersion section with what a 1.1-written file claims; describe the `WaterMapNew` layout as
written by 1.1 (`WaterColumns` and `ColumnOutflows` values over the filled pit and the flowing
channel: if a column carries a plain depth per level, say so and give one example cell; if it does
not decode from one file, say that). Strike "Still open" items 1 and 2 and update 3 and 4 with what
was learned. Mirror the answers in `wardens-wasteland.md` and `wardens-campaign-map-set.md` §5.

Decision rule for the format: **do not** change `wardens/tools/mapsmith/world.py` or `gen_map.py` to claim 1.1 in
this package. The proof run (WP4, step 2) says whether the 0.7.10 layout still migrates; if it does,
the writers stay as they are and the water encoding becomes iteration 05's first map task.

- [ ] **Step 5: Commit**

```bash
git add wardens/maps/reference .claude/skills/timberborn-mapsmith/references/timber-format.md design/wardens-wasteland.md design/wardens-campaign-map-set.md
git commit -m "Maps: a reference .timber written by the 1.1 editor, and what it answers"
```

---

## WP4: The proof run of level 01 [game]

**Preconditions.** WP1 and WP2 built and deployed; `python wardens/tools/validate.py` prints
`problems: none`; in the Mod Manager The Wardens is on and **Timberbot API is off**; the Leaf Coats
local copies are removed (`python wardens/tools/import_leafcoats.py --remove`) or the seven workshop
mods they duplicate are off; Claude Code connected through the repo's `.mcp.json`.

**The record.** Create `wardens/playtest/runs/<YYYY-MM-DD>-level-01.md` from the table below and
fill in every row as it happens. Next to it: `campaign.json` and `story.json` from
`Documents/Timberborn/Mods/Wardens/`, and `player-log-wardens.txt`, the `[Wardens]` lines from
`%USERPROFILE%\AppData\LocalLow\Mechanistry\Timberborn\Player.log`
(`findstr "[Wardens]" Player.log > player-log-wardens.txt`). Commit the folder.

**Fast-forward rule.** `chapter action=unlock` and `tutorial action=next` are allowed from step 8 on,
never before it, and every forced step is written in the record. The first three chapters are played
honestly, because they are the ones the softlock lived in.

- [ ] **1. Main menu.** `Player.log` has `[Wardens] maps: installed …` (or `up to date`). New Game →
  Custom maps lists `[Custom] Wardens 01 First Light`. *If not:* is the file in
  `Documents/Timberborn/Maps`? `installMaps` in `settings.json`? A warning in the log names the path.
- [ ] **2. Start.** Faction The Wardens, tutorial **on**, Normal. The map loads; an "older version"
  notice is acceptable. *If it refuses:* the log names a singleton or template; WP3's reference map
  says what 1.1 writes for it. Record the answer to "does 1.1 migrate the 0.7.10 layout" either way.
- [ ] **3. Cold Boot.** Letterbox, three captions over about 22 s, the three archived-badtide toasts
  and their Uplink lines, the game paused afterwards and unlocked (speed buttons work).
  `cutscene action=status` during the scene shows `playing`. Write down: the zoom scale (`dzoom`
  ±0.15 too little or too much), whether the letterbox collides with the tutorial panel, whether
  Escape reaches the overlay or opens the pause menu (`design/wardens-cutscenes.md` §12).
- [ ] **4. Day 1 ground truth, before unpausing.** Boot the Warden (`manual`, `campaign action=status`
  → `enabled: true`, `level: "01"`; `wardens_status` → `faction: Wardens`; `timberbot_ready`).
  Then `timberbot GET /api/alerts` and `timberbot GET path=/api/tiles?x1=14&y1=40&x2=32&y2=58`.
  Expect: no `Flooded.` alert; `water` 0.0 on the pad tiles (Z 8); `population` bots = N. Write N
  down (the design says 5; the last run saw 13, see WP6). *If the Core is flooded:* save the tiles
  JSON and the log and stop the run; the decision tree is WP6 item 1.
- [ ] **5. First Light.** The human follows the cards (Charging Post beside the Core with a shaft,
  two Scavenger Flags, paths); the Warden runs the loop (`frame`), says one line at boot, writes
  the D1 Ledger line, records it (`campaign action=record`). Every bot is alive at dawn of day 2
  (`bots.energy_min` above 0.35 in the frame).
- [ ] **6. Badwater opens.** Scrap reaches 10, `Wardens.Scrap` finishes: the "Chapter 2: Badwater."
  toast, the same line in the Uplink, the four padlocks gone within a second, the `Badwater`
  cutscene (two shots, the first caption with the scrap count, the second waiting for Continue, the
  camera restored). `chapter action=status`: `complete` was `[]` before and is `["Badwater"]` after.
- [ ] **7. The Sump.** Does the basin fill from the river during days 1–2? Is the Sludge Pump
  placeable on it and does it pump once built? (`design/wardens-wasteland.md` open question 3: if
  not, the bed goes to 3 in `FirstLight.build_terrain`, and that is a new map file under the same
  name, which is allowed only because the map has never shipped to a player.)
- [ ] **8. Signal, Pods, Power.** Play or fast-forward. Each chapter's scene plays once and only at its
  opening; the power budget behaves as the playbook says (Core 150, Post 50, Cruncher 120).
- [ ] **9. Green.** The first beaver is born; the `Green` scene marks `birthday`; `LevelEnd` shows
  *Continue to Level 02* / *Stay*; the human clicks *Stay*; `story.json` carries both.
- [ ] **10. Completion.** `Wardens.MoreBeavers` finishes: the toast reads "Level 01 complete: First
  Light. Level 02 (The Sump) is not built yet." and the Uplink has the same line; `campaign.json`
  has `"completed": ["01"]`; `campaign action=status` shows `level_complete: true`.
- [ ] **11. Reloads.** Load the day-1 save: no Cold Boot, no chapter toast, `campaign.json`
  untouched. Load a save from after step 10: no second completion toast.
- [ ] **12. WP2 check.** `python wardens/playtest/mcp_concurrency.py` → exit 0.
- [ ] **13. WP1 check.** The two `timberbot` calls from WP1 step 7 behave.
- [ ] **14. Findings.** Every deviation becomes a dated bullet under "Playtest findings" in
  `PLAYTEST.md` and an item in WP6. Commit the run folder.

```bash
git add wardens/playtest/runs wardens/playtest/PLAYTEST.md
git commit -m "Playtest: level 01 proof run, <date>"
```

---

## WP5: The `ledger` tool, and the five-way contract [cloud → game build]

`design/wardens-play.md` §6 lists it first: the daily entry should cost one call, not a scan of
`/api/tiles` through the passthrough and arithmetic in the agent's head. It also makes the proof run
measurable: the Ledger line at every day change, from the same numbers every time.

**Files:**
- Create: `wardens/src/WardensLedger.cs`
- Modify: `wardens/src/WardensConfigurator.cs` (one `Bind`), `wardens/src/WardensMcpTools.cs`
  (constructor, `Add("ledger", …)`, `BuildInstructions`), `wardens/WARDEN.md` ("The Ledger"),
  `.claude/skills/warden-play/SKILL.md` (§3), `design/wardens-play.md` (§6 item 1),
  `wardens/playtest/PLAYTEST.md` (tool table)

**Interfaces:**
- Produces: `JObject WardensLedger.Compute()` returning `{day, poisoned, healed, green, archive,
  born, bots, bots_charged, poisoned_new: [{x,y,z}…≤5], delta: {poisoned, green, archive, born},
  line}`. Main thread only. `healed` and the deltas compare with the previous `Compute()` of this
  session; the first call of a session has `null` deltas and `healed` 0.
- MCP tool `ledger`: `action=compute` (default) returns that object; `action=record seen="…"`
  also appends it (without `poisoned_new`, with `seen`) through `WardensCampaignService.Record_Ledger`.

- [x] **Step 1: The class**

`wardens/src/WardensLedger.cs`:

```csharp
// WardensLedger.cs. The Ledger in one call: poisoned, healed, green, archive, born, bots.
//
// design/wardens-play.md §2 and §6 item 1; WARDEN.md "The Ledger". The agent used to compute the
// daily line from GET /api/tiles through the passthrough (9,216 cells of JSON a day on a 96x96 map)
// and do the arithmetic itself. This counts the same things on the main thread, from the same
// services the tiles route reads (TimberbotReadV2.cs: ISoilContaminationService.SoilIsContaminated,
// ISoilMoistureService.SoilIsMoist over each column's top), keeps the previous call's poisoned set so
// `healed` and `poisoned_new` exist, and formats the line WARDEN.md prescribes.
//
// Main thread only: the MCP tool that calls Compute() is queued, never OffThread, because the soil
// services are not on the verified thread-safe list (docs/audit/contradictions.md, C14).
// Session memory only: a reload starts without a previous set, so the first line of a session has no
// deltas. Persisting it is the Archive-in-the-save step (wardens-play.md §6 item 3), not this one.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.GameDistricts;
using Timberborn.MapIndexSystem;
using Timberborn.NeedSystem;
using Timberborn.ResourceCountingSystem;
using Timberborn.SoilContaminationSystem;
using Timberborn.SoilMoistureSystem;
using Timberborn.TerrainSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace Wardens
{
    public class WardensLedger
    {
        private const string DataCoreId = "DataCore";
        private const float Charged = 0.35f;   // WardensFrames.LowEnergy: under it a Warden is "low"

        private readonly MapIndexService _mapIndex;
        private readonly IThreadSafeColumnTerrainMap _terrain;
        private readonly ISoilContaminationService _contamination;
        private readonly ISoilMoistureService _moisture;
        private readonly CharacterPopulation _population;
        private readonly DistrictCenterRegistry _districts;
        private readonly IDayNightCycle _dayNightCycle;

        private HashSet<int> _previousPoisoned;
        private JObject _previous;

        public WardensLedger(MapIndexService mapIndex, IThreadSafeColumnTerrainMap terrain,
            ISoilContaminationService contamination, ISoilMoistureService moisture,
            CharacterPopulation population, DistrictCenterRegistry districts, IDayNightCycle dayNightCycle)
        {
            _mapIndex = mapIndex;
            _terrain = terrain;
            _contamination = contamination;
            _moisture = moisture;
            _population = population;
            _districts = districts;
            _dayNightCycle = dayNightCycle;
        }

        public JObject Compute()
        {
            // Map size: the member TimberbotReadV2 reports as `mapSize` in /api/tiles; use that one.
            var size = _mapIndex.TotalSize;
            int stride = _mapIndex.VerticalStride;
            var poisoned = new HashSet<int>();
            var newlyPoisoned = new JArray();
            int green = 0;
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    int index2D = _mapIndex.CellToIndex(new Vector2Int(x, y));
                    int columns = _terrain.ColumnCounts[index2D];
                    if (columns == 0) continue;
                    int top = _terrain.GetColumnCeiling((columns - 1) * stride + index2D);
                    var cell = new Vector3Int(x, y, top);
                    if (_contamination.SoilIsContaminated(cell))
                    {
                        poisoned.Add(index2D);
                        if (_previousPoisoned != null && !_previousPoisoned.Contains(index2D) && newlyPoisoned.Count < 5)
                            newlyPoisoned.Add(new JObject { ["x"] = x, ["y"] = y, ["z"] = top });
                    }
                    else if (_moisture.SoilIsMoist(cell)) green++;
                }
            }
            int healed = 0;
            if (_previousPoisoned != null)
                foreach (int i in _previousPoisoned)
                    if (!poisoned.Contains(i)) healed++;

            int bots = 0, charged = 0, beavers = 0;
            foreach (var c in _population.Characters)
            {
                if (c.GetComponent<Bot>() == null) { beavers++; continue; }
                bots++;
                var energy = BotsChargedStep.Energy(c.GetComponent<NeedManager>());
                if (energy != null && energy.Value >= Charged) charged++;
            }
            int archive = 0;
            foreach (var dc in _districts.AllDistrictCenters)
            {
                var counter = dc.GetComponent<DistrictResourceCounter>();
                if (counter != null) archive += counter.GetResourceCount(DataCoreId).AllStock;
            }

            var entry = new JObject
            {
                ["day"] = _dayNightCycle.DayNumber,
                ["poisoned"] = poisoned.Count,
                ["healed"] = healed,
                ["green"] = green,
                ["archive"] = archive,
                ["born"] = beavers,
                ["bots"] = bots,
                ["bots_charged"] = charged,
                ["poisoned_new"] = newlyPoisoned,
            };
            entry["delta"] = Delta(entry, _previous);
            entry["line"] = Line(entry);
            _previousPoisoned = poisoned;
            _previous = entry;
            return entry;
        }

        private static JObject Delta(JObject now, JObject before)
        {
            var d = new JObject();
            foreach (var key in new[] { "poisoned", "green", "archive", "born" })
                d[key] = before == null ? (int?)null : (int)now[key] - (int)before[key];
            return d;
        }

        // D12  poisoned 214 (+8)  healed 0  green 31 (-3)  archive 9 (+3)  born 0  bots 5/5 charged
        private static string Line(JObject e)
        {
            var d = (JObject)e["delta"];
            string With(string key) => d[key] == null || d[key].Type == JTokenType.Null
                ? $"{e[key]}" : $"{e[key]} ({(int)d[key]:+0;-0;0})";
            return $"D{e["day"]}  poisoned {With("poisoned")}  healed {e["healed"]}  green {With("green")}  " +
                   $"archive {With("archive")}  born {With("born")}  bots {e["bots_charged"]}/{e["bots"]} charged";
        }
    }
}
```

- [x] **Step 2: Bind and register**

`WardensConfigurator.cs`, after `Bind<WardensFrames>().AsSingleton();`:

```csharp
            Bind<WardensLedger>().AsSingleton();
```

`WardensMcpTools.cs`: add `WardensLedger ledger` to the constructor (field `_ledger`), and in
`Build()` after the `campaign` tool:

```csharp
            Add("ledger",
                "The Ledger in one call (WARDEN.md, \"The Ledger\"): poisoned (tiles whose soil is contaminated), healed (poisoned at " +
                "the previous call and clean now), green (moist and clean), archive (Data Cores in stock), born (beavers), bots and how " +
                "many are charged, up to five newly poisoned tiles, deltas since the previous call, and `line`, the formatted entry. " +
                "action=compute returns it; action=record also appends it to campaign.json (the same as campaign action=record) with " +
                "your `seen` line: the daily routine in one call.",
                Schema(new JObject
                {
                    ["action"] = Prop("string", "compute | record", "compute"),
                    ["seen"] = Prop("string", "for record: the day's observation, one line, the thing the camera could not have shown"),
                }),
                a =>
                {
                    var entry = _ledger.Compute();
                    if (Str(a, "action", "compute") == "record")
                    {
                        var stored = (JObject)entry.DeepClone();
                        stored.Remove("poisoned_new");
                        stored["seen"] = Str(a, "seen", "");
                        entry["recorded"] = _campaign.Record_Ledger(stored);
                    }
                    return entry;
                });
```

(Main thread by default: no `offThread: true`.)

- [x] **Step 3: The five documents, one commit**

`WARDEN.md`, "The Ledger": keep the field table as *definitions* and delete the `/api/tiles`
instructions from it; before the table add "One call computes it: `ledger`. `ledger action=record
seen="…"` computes and writes the day's entry to the campaign record in the same call." Replace the
paragraph "Write the day's entry to the campaign record as well …" accordingly. In "The loop", the
routine on `since.day_changed` becomes "`ledger action=record`, then a look at `open_steps` and
`bots.unemployed`".

`SKILL.md` §3: the same change (the table stays as definitions; the call is one line).

`BuildInstructions` ("THE TOOLS ARE THE BODY"): add "`ledger` is the conscience: one call for the daily
line, `action=record` writes it to campaign.json with your `seen`."

`design/wardens-play.md` §6 item 1: mark built, with the date and "WP5, iteration 04".

`PLAYTEST.md` tool table: a `ledger | main | …` row after `campaign`.

- [ ] **Step 4: Build on the game machine and check**

Run: `dotnet build wardens/src/Wardens.csproj -c Release`. In a loaded Wardens game after
`timberbot_ready`: `ledger` returns a line like `D1  poisoned 731  healed 0  green 118  archive 0  born 0  bots 13/13 charged`;
a second call a day later carries deltas; `ledger action=record seen="…"` adds an entry
(`campaign action=ledger` shows it).

- [x] **Step 5: Commit**

```bash
git add wardens/src/WardensLedger.cs wardens/src/WardensConfigurator.cs wardens/src/WardensMcpTools.cs wardens/WARDEN.md .claude/skills/warden-play/SKILL.md design/wardens-play.md wardens/playtest/PLAYTEST.md
git commit -m "Wardens: the ledger tool, the daily line in one call"
```

---

## WP6: Fix what the run found [cloud + game]

Not plannable in detail before WP4. The procedure, and the suspects known today.

**Procedure per finding.** A dated bullet in `PLAYTEST.md` "Playtest findings"; a fix in the code
that owns the behaviour (Timberbot bugs in `timberbot/src`, then re-copy to `wardens/src/Timberbot/`);
a regression check where one can exist offline (`wardens/test`, `wardens/tools/test_*.py`,
`timberbot/test`); a row in `docs/audit/contradictions.md` when a documented claim was refuted; the
game-machine build and the relevant WP4 row re-run before the fix is called done.

**Known suspects, with where to look:**

1. **The flooded Core** (the softlock). Decision tree: (a) if WP4 step 4 shows `water` 0.0 on the pad
   and no `Flooded.` alert, the last run was on another map; close the finding. (b) if the pad is wet
   at Z 8 on a dry-shipped map, the 0.7.10 water map does not migrate to 1.1 as empty water: compare
   with WP3's reference `WaterMapNew` and write the 1.1 layout in `gen_map.py` and
   `wardens/tools/mapsmith/world.py` (this is the one case where the shipped file changes under its name, allowed
   because no player has it yet). (c) if only the Core and the Scrap Pile flag `Flooded.` with dry
   ground, look at what the game's flood check reads for those two templates
   (`gen_buildings.py` re-specs them from Iron Teeth) and record it in `faction-wardens.md` §3.
2. **`chapter status` listing every chapter complete on a new save.** `WardensChapterService.IsComplete`
   is true when every template is unlocked, and `_unlockAll` opens everything when the tutorial is off
   or `chapterGating` is false. Check the save's tutorial toggle first; if it was on, check
   `BuildingUnlockingService.Unlocked` for a template with `ScienceCost 999999` on a fresh game.
3. **13 Wardens where the design says 5.** `WardensStartingPopulation` replaces every starting beaver
   the game mode spawns (Normal spawns adults and children; 13 is that total). Decide with the author:
   keep the mode's count (then `WARDEN.md`'s `bots 5/5` example, `wardens-campaign-concept.md` §1 and
   §2 and `wardens-chapter-1-plan.md` §2 change) or cap the bots at `LevelStart.Bots`
   (`wardens-campaign-design.md` §3.3; spawn only the first N positions). The Chapter 1 economy in
   `wardens-chapter-1-plan.md` §2 was tuned for the smaller number.
4. **`/api/alerts` returns `type: "Flooded."`.** `BuildAlertsFromBuildings` (`TimberbotReadV2.cs`)
   copies `StatusAlerts` strings verbatim; the documented enum in `docs/api-reference.md` is not what
   the code emits. One contradictions row, and either the doc or a normalisation in the route.
5. **Cutscene tuning** from WP4 step 3: `dzoom`, `v`, the letterbox width, Escape. Edit the scene
   files in the mod folder, `cutscene action=reload` / `play`, copy back to `wardens/src/Cutscenes/`,
   run `python wardens/tools/check_cutscenes.py wardens/src`.

---

## WP7: Level numbering, one answer [cloud]

Three documents and the code say **02 = The Sump** (a gorge and a confluence) and **03 = The Pods**
(a lake with an island): `wardens-campaign-arc.md` §3, `wardens-campaign-story.md` §4–5,
`wardens-campaign-map-set.md` §1, and `WardensCampaignService.Levels`. One says otherwise:
`wardens-campaign-maps.md` §6.5 calls the second map "The Pods" with a different brief (two clean
streams, a contaminated crater), and `wardens/maps/wardens-02-the-pods.map.toml` was built to that
brief. `AGENTS.md` settles it: "The campaign is the level table." The spec is neither level; it is
the proof that mapsmith's eleven ops can make a different map, and it stays as that.

**Files:**
- Rename: `wardens/maps/wardens-02-the-pods.map.toml` → `wardens/maps/wardens-proto-crater.map.toml`
- Modify: its header and `name`; `design/wardens-campaign-maps.md` §6.5; `design/mapsmith.md`
  (status line, "Also surfaced" paragraph, "Next"); `wardens/README.md` ("The campaign", the sentence
  naming the two specs); anything `grep` finds

- [x] **Step 1: Find every mention**

Run: `grep -rn "wardens-02-the-pods\|Wardens 02 The Pods" --include='*.md' --include='*.toml' --include='*.py' .`
Expected: the spec itself, `design/mapsmith.md`, `wardens/README.md`, the mapsmith skill or its
references if they cite the file. Every hit is edited in Step 3.

- [x] **Step 2: Rename the spec**

```bash
git mv wardens/maps/wardens-02-the-pods.map.toml wardens/maps/wardens-proto-crater.map.toml
```

Set `name = "Wardens Proto Crater"` and `description = "A mapsmith prototype, not a campaign level: two clean streams off a ridge, a contaminated crater in the middle, wrecks on the flats."`.
Replace the header comment's first paragraph and delete the "NUMBERING IS UNRESOLVED" paragraph;
the header now says: this spec proves the vocabulary (a different map from the same eleven ops, no
new Python); it is not level 02 (*The Sump*, a gorge and a confluence) or level 03 (*The Pods*, a lake
and an island), whose briefs are `design/wardens-campaign-map-set.md` §2; level 02's spec is
iteration 05's first map task.

- [x] **Step 3: Correct the documents**

`wardens-campaign-maps.md` §6.5: add a line "**Corrected (date):** the second level is *The Sump*
(`wardens-campaign-arc.md` §3, `WardensCampaign.cs`); the brief in this section became the
`wardens-proto-crater` spec." `design/mapsmith.md`: status line names the prototype; the "Also
surfaced" paragraph gets "resolved (date): the table wins"; "Next" lists level 02's spec by its real
name. `wardens/README.md`: the two-spec sentence names the prototype and says level 02 has no spec yet.
Every other `grep` hit likewise.

- [x] **Step 4: Check and commit**

Run: `python wardens/tools/mapsmith check wardens/maps/wardens-proto-crater.map.toml` and
`uv run --project python --extra dev pytest wardens/tools/test_mapsmith.py`.
Expected: `problems: none`; all tests pass.

```bash
git add -A wardens/maps design/wardens-campaign-maps.md design/mapsmith.md wardens/README.md
git commit -m "Maps: the level table names the levels; the crater spec is a prototype"
```

---

## WP8: Level 10, *Home*, as a generator flag [cloud, optional]

`design/wardens-campaign-map-set.md` §7 step 3: level 10 is level 01's heightfield with the
contamination zeroed, the badwater gone and the trees grown. `gen_map.py` already has the two hooks
this needs (`Terrain.REQUIRE_BADWATER`, per-level `contract()`); the registry has one entry. This
proves the registry with the cheapest possible second level. The map is **not shipped**: its level
has no ending tutorial yet and `validate.py` refuses a shipped level without one, so it is written
outside `src/Maps` and the table row keeps `shipped: false`.

**Files:**
- Modify: `wardens/tools/gen_map.py` (`Terrain.SHIPPED`, `class Home`, the registry, `generate`, `--list`)
- Create: `wardens/tools/test_gen_map.py`

- [x] **Step 1: Write the failing tests**

`wardens/tools/test_gen_map.py`:

```python
"""Tests for gen_map.py's level registry: level 10 is level 01 healed, and a level that is not
shipped never lands in src/Maps.

    uv run --project python --extra dev pytest wardens/tools/test_gen_map.py

gen_map.py needs numpy, which the Python dev extras do not carry; the tests skip without it.
"""
from __future__ import annotations

from pathlib import Path

import pytest

np = pytest.importorskip("numpy")
import gen_map  # noqa: E402


def test_home_is_first_light_healed() -> None:
    first = gen_map.FirstLight().build()
    home = gen_map.Home().build()
    assert np.array_equal(home.height, first.height)
    assert not any(e["Template"] == "BadwaterSource" for e in home.entities)
    assert home.contamination().sum() == 0
    assert len(home.trees) >= gen_map.Home.TREE_TARGET * len(first.trees)


def test_home_passes_its_own_check(tmp_path: Path) -> None:
    out = tmp_path / "Wardens 10 Home.timber"
    assert gen_map.generate(gen_map.Home, out, None, None, None) == 0
    assert gen_map.check(out) == []


def test_unshipped_level_refuses_src_maps() -> None:
    with pytest.raises(SystemExit):
        gen_map.generate(gen_map.Home, None, None, None, None)
```

- [x] **Step 2: Run them to see them fail**

Run: `pip install numpy && uv run --project python --extra dev pytest wardens/tools/test_gen_map.py -v`
(or `python -m pytest` in an environment with numpy).
Expected: `AttributeError: module 'gen_map' has no attribute 'Home'`.

- [x] **Step 3: The level**

In `gen_map.py`: on `Terrain`, next to `REQUIRE_BADWATER`, add `SHIPPED = True  # false: never written into src/Maps (validate.py rejects an unclaimed .timber)`.
After `FirstLight`, add:

```python
class Home(FirstLight):
    """Level 10. Level 01's basin from the same seed, healed: the contamination zero, the badwater
    sources replaced by clean ones so the river runs clear, the plateau under trees.
    design/wardens-campaign-map-set.md §2 (10), design/wardens-campaign-story.md §12. Not shipped
    until its level has an ending; write it with --out.
    """

    LEVEL = "10"
    MAP_NAME = "Wardens 10 Home"
    DESCRIPTION = ("The Wardens' basin, three thousand days on: the river runs clear and the ash is "
                   "under birches. Campaign level 10: Home.")
    REQUIRE_BADWATER = False
    SHIPPED = False
    TREE_TARGET = 20            # times level 01's plant count (the map-set contract says >= 20x)

    def contamination(self) -> np.ndarray:
        return np.zeros((self.size, self.size))

    def build_entities(self) -> None:
        super().build_entities()        # the same rng stream: terrain, ruins and the grove are level 01's
        for e in self.entities:
            if e["Template"] == "BadwaterSource":
                e["Template"] = "WaterSource"
        self.sources = []
        h = self.height
        s = self.size
        taken = set(self.trees) | {(x, y) for x, y, _ in self.ruins}
        want = self.TREE_TARGET * len(self.trees)
        order = [(x, y) for y in range(2, s - 2) for x in range(2, s - 2)]
        self.rng.shuffle(order)
        for x, y in order:
            if len(self.trees) >= want:
                break
            if (x, y) in taken or not self.free(x, y, margin=0) or h[y, x] < 5:
                continue
            if any(abs(x - rx) <= 1 and abs(y - ry) <= 1 for rx, ry, _ in self.ruins):
                continue
            taken.add((x, y))
            self.trees.append((x, y))
            kind = self.rng.random()
            template = "Birch" if kind < 0.5 else "Pine" if kind < 0.85 else "BlueberryBush"
            self.entities.append({"Id": self.ident(template, x, y), "Template": template,
                                  "Components": {"BlockObject": {"Coordinates": {"X": x, "Y": y, "Z": self.z_at(x, y)}},
                                                 "Growable": {"GrowthProgress": 1.0}}})

    def contract(self, world: dict, grid: np.ndarray) -> list[str]:
        bad = []
        reference = FirstLight(size=self.size).build()
        if not np.array_equal(surface(grid), reference.height):
            bad.append("heightfield differs from level 01 (Home is level 01's land, healed)")
        cont = floats_of(world, "SoilContaminationSimulator", "ContaminationLevels")
        if cont.any():
            bad.append("contamination is not zero everywhere")
        if templates(world, "BadwaterSource"):
            bad.append("a BadwaterSource on healed land")
        plants = len(templates(world, "Pine")) + len(templates(world, "Birch")) + len(templates(world, "BlueberryBush"))
        if plants < self.TREE_TARGET * len(reference.trees):
            bad.append(f"{plants} plants, expected >= {self.TREE_TARGET * len(reference.trees)} ({self.TREE_TARGET}x level 01)")
        return bad
```

Register it: `LEVELS = {FirstLight.LEVEL: FirstLight, Home.LEVEL: Home}`. In `generate()`, first line:

```python
    if not cls.SHIPPED and out is None:
        raise SystemExit(f"level {cls.LEVEL} ({cls.MAP_NAME}) is not shipped: pass --out to write it outside src/Maps")
```

In `main()`, the `--all` loop skips `not cls.SHIPPED` with a printed note, and `--list` appends
`(not shipped)` to such rows.

If the plateau cannot hold 20× (the loop ends early and the contract fails), lower `TREE_TARGET` to
what fits and change the "≥ 20×" line in `wardens-campaign-map-set.md` §2 (10) to the same number,
in the same commit.

- [x] **Step 4: Run the tests and the checks**

Run: the test command from Step 2; then
`python wardens/tools/gen_map.py --level 10 --out /tmp/home.timber --preview /tmp/home.png` and
`python wardens/tools/gen_map.py --check /tmp/home.timber --level 10` and
`python wardens/tools/gen_map.py --list`.
Expected: 3 passed; `problems: none`; the list shows `10  Wardens 10 Home … (not shipped)`.
Then: `python wardens/tools/gen_map.py --check "wardens/src/Maps/Wardens 01 First Light.timber"`
still prints `problems: none` and `git status` shows nothing new under `wardens/src/Maps`.

- [x] **Step 5: Commit**

```bash
git add wardens/tools/gen_map.py wardens/tools/test_gen_map.py
git commit -m "Maps: level 10 Home as a regeneration pass over level 01, unshipped"
```

Tick step 3 in `design/wardens-campaign-map-set.md` §7 the way steps 1 and 2 are ticked.

---

## WP9: Close the iteration [cloud]

- [ ] `python wardens/tools/bump_version.py --minor` → 0.4.0; a `## 0.4.0 (<date>): First Light,
  verified` entry at the top of `wardens/CHANGELOG.md` listing WP1–WP8 by outcome, and the sentence
  "Verified in-game on <date>" with what the proof run established. `python wardens/tools/gen_thumbnail.py --check`.
- [ ] Rewrite `AGENTS.md` "The Wardens: state" as "state on <date> (v0.4.0)": what is verified (each
  WP4 row that passed, by name), what is still open (each that did not), the next map task (level 02,
  *The Sump*: gorge and confluence ops, its chapter table and tutorials, `wardens-campaign-design.md`
  §10 step 3) and the next system task (the water encoding if WP3 decoded it; otherwise the
  transition, §10 step 4).
- [ ] Add a new entry at the top of `docs/plan/HANDOVER.md` using its template; keep this plan's
  checkboxes ticked as the record of what was done.
- [ ] `python wardens/tools/package.py --list` shows the release layout; `python wardens/tools/package.py`
  after a Release build writes `dist/Wardens-v0.4.0.zip`.

## Out of scope for this iteration

Level 02's terrain (gorge, confluence) and its tutorials; `ILevelStarter` and the *Continue campaign*
button (`wardens-campaign-design.md` §10 steps 4–5); frames on the Timberbot WebSocket; the Archive in
the save (`ISaveableSingleton`); a new game from the API; a screenshot tool; Spike B; the Leaf Coats
building port into the faction's collection; the `ICustomMapItemFactory` map listing; German
localization. Each is a candidate for iteration 05 and is listed in `HANDOVER.md` with what it waits for.

## Risks

| Risk | Sign | What to do |
|---|---|---|
| The 0.7.10 layout no longer migrates | WP4 step 2 refuses the map | WP3's file gives the 1.1 layout; write it in both generators; the map changes under its name (allowed: never shipped) |
| The game machine is not available for weeks | WP4 stalls | do WP1, WP2, WP5, WP7, WP8 anyway; leave the run checklist and the deployed-build instructions in `HANDOVER.md`; do not start level 02 |
| `WardensLedger` service types do not resolve (`MapIndexService.TotalSize`, `IThreadSafeColumnTerrainMap`) | the Wardens build fails | copy the exact members `TimberbotReadV2` uses for `/api/tiles` (they compile there) |
| The pool-thread listener exposes a race nobody saw | a tool result carries another call's `chat` block, or a frame `seq` goes backwards | `WardensChat.TakeUndelivered` is locked; check `RunTool`'s `chat` attach and the `Interlocked` loop in WP2 step 2 |
| 20× trees do not fit on the plateau | WP8 contract fails | lower `TREE_TARGET` and the doc together |
