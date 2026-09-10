# Handover: the Wardens, session to session

This file is the first thing to read when you continue the Wardens work. It is a stack: the newest
entry is at the top, and every session that ends adds one using the template at the bottom. An entry
says who wrote it, what they did and verified, where the mod stands with sources, what to do next and
in what order, and what they left open on purpose. The plan an entry points to says *how*.

The rest of the map: `AGENTS.md` (conventions, the state section), `wardens/README.md` (what the mod
is and how each part works), `wardens/CHANGELOG.md` (what each version added), `wardens/playtest/PLAYTEST.md`
(the in-game checklist, the MCP tool table, the findings), `design/` (why things are the way they are).

---

## 2026-09-10, night: implementation session, the cloud half of iteration 04

**Plan:** [`iteration-04-first-light-verified.md`](iteration-04-first-light-verified.md) (its checkboxes
are the record of what is done). **Goal of this session:** every package of the plan that a cloud
container can do, written, offline-checked and committed, so the game machine starts with a build and
the proof run instead of with code to write.

### What I did

Same container as the planning entry below, plus the .NET 10 SDK installed with Microsoft's
`dotnet-install.sh` (`/root/.dotnet`, one command, no root needed) and numpy pulled in per run with
`uv run --project python --extra dev --with numpy`. Still no game. Five commits on
`claude/mod-iteration-handover-09vcku` (pull request #13), one per package, in the plan's order:

| Commit | Package | What it is |
|---|---|---|
| `edfa0c9` | WP7 | the level table names the levels; `wardens-02-the-pods.map.toml` is now `wardens-proto-crater.map.toml` and says what it is; four documents corrected |
| `a35c453` | WP1 | `WardensPure.BuildLoopbackUrl` merges a query carried in `path` with `query`; `Loopback` uses it; `wardens/test/` is the first C# test project for the mod (6 tests, seen failing first); CI lists it; PLAYTEST findings folded into one cause; contradictions row C25 |
| `3bba410` | WP2 | `WardensMcpServer.ListenLoop` runs each request on a pool thread (`HandleSafely`); `_lastFrameSeq` through `Interlocked`; `playtest/mcp_concurrency.py`; PLAYTEST records the cause of the dropped connection |
| `1aed346` | WP5 | `WardensLedger.cs`, its `Bind`, the MCP `ledger` tool (`compute` / `record`), and the five contract documents updated together (`WARDEN.md`, the skill, `BuildInstructions`, `wardens-play.md`, the PLAYTEST tool table, which also gains the `campaign` row it had been missing) |
| `fb19704` | WP8 | `Home(FirstLight)` in `gen_map.py`: same seed and heightfield, contamination zero, badwater sources turned clean, 3,020 plants; `SHIPPED` flag, `generate()` refuses to write an unshipped level without `--out`; `test_gen_map.py` |

Ran on the final tree, all clean:

| Check | Result |
|---|---|
| `uv run --project python --extra dev --with numpy pytest -q wardens/tools` | 98 passed (95 before, plus `test_gen_map.py`) |
| `python wardens/tools/check_cutscenes.py wardens/src` | `problems: none` |
| `python wardens/tools/mapsmith check` on both specs and the shipped `.timber` | `problems: none` ×3 |
| `dotnet test wardens/test/Wardens.Tests.csproj` | 6 passed |
| `dotnet test timberbot/test/Timberbot.Tests.csproj` | 468 passed (untouched, sanity) |
| `uv run … python wardens/tools/gen_map.py --check "wardens/src/Maps/Wardens 01 First Light.timber"` | `problems: none`; the file is byte-for-byte untouched |
| `gen_map.py --level 10 --out /tmp/home.timber` then `--check … --level 10` | `problems: none`; without `--out` it refuses, as designed |
| `ruff check python/` (what CI runs) | clean |

Not run, because they need the game or its files: `dotnet build wardens/src/Wardens.csproj`,
`python wardens/tools/validate.py`, anything in-game.

### Written but not compiled: read this first at the game machine

Five C# files changed without a compiler that has the game's assemblies. `Wardens.csproj` is an
SDK-style project with default source globbing, so the two new files compile in without a project
edit. The first `dotnet build wardens/src/Wardens.csproj -c Release` is their compile:

- `wardens/src/WardensPure.cs` (new): compiled and tested on net10.0 without the game, so only its
  call site is unproven.
- `wardens/src/WardensMcpTools.cs`: the `Loopback` call site (four lines), `using System.Threading;`,
  the `Interlocked` pair in the `frame` tool, one constructor parameter, the `ledger` tool block, one
  sentence in `BuildInstructions`.
- `wardens/src/WardensMcpServer.cs`: `ListenLoop` queues `HandleSafely` on the pool; nothing else.
- `wardens/src/WardensConfigurator.cs`: one `Bind<WardensLedger>()`.
- `wardens/src/WardensLedger.cs` (new): every type it injects is injected by `TimberbotReadV2` or
  `WardensFrames` in this same DLL (`ITerrainService`, `IThreadSafeColumnTerrainMap`, `MapIndexService`,
  `ISoilContaminationService`, `ISoilMoistureService`, `CharacterPopulation`, `DistrictCenterRegistry`,
  `IDayNightCycle`), and every member it calls is copied from a call that compiles there
  (`Size`, `ColumnCounts`, `GetColumnCeiling`, `VerticalStride`, `CellToIndex`, `SoilIsContaminated`,
  `SoilIsMoist`, `AllDistrictCenters`, `GetResourceCount(...).AllStock`, `BotsChargedStep.Energy`). If
  the build objects, it will be a member name here; the fix is to copy the exact call from those two
  files.

### What you should do, in this order

**At the game machine:** (1) build, fix any compile slip as above; (2) `python wardens/tools/validate.py`
prints `problems: none`; (3) load any game and run `python wardens/playtest/mcp_smoke.py`, then
`python wardens/playtest/mcp_concurrency.py` (WP2's check), then the two `timberbot` calls of the
plan's WP1 step 7, then `ledger` on a Wardens game (WP5 step 4: the line, then deltas a day later, then
`action=record` visible under `campaign action=ledger`); (4) WP3, the reference map from the map
editor; (5) WP4, the proof run, with its record committed; (6) WP6 for what it found; (7) WP9.

**In a cloud container:** nothing in the plan is left for you except what WP6 turns up. Do not start
level 02; do not "improve" the uncompiled C# without a build.

### What I found while implementing

- `Terrain.REQUIRE_BADWATER` and per-level `contract()` already existed, so *Home* was a subclass and a
  flag, as the map-set document predicted. The plateau holds exactly the 20× target (3,020 plants).
- The PLAYTEST tool table had no `campaign` row since 0.3.5. Added, next to `ledger`.
- `WARDEN.md` had five places that said `campaign action=record`; all now say `ledger action=record`,
  and the `ledger` tool's description says it writes the same record, so an agent that still calls
  `campaign action=record` is not wrong, only slower.
- `mapsmith build` takes `--out`; the README's example for the prototype now writes it outside
  `src/Maps`, where an unclaimed `.timber` would fail `validate.py`.

### Open questions I could not answer

- The planning entry's three (five or thirteen bots; the game machine's availability; whether the 1.1
  map editor saves with running water).
- One new: the `frame` tool's default `after` is "the last seq this server handed out", one field
  shared by every client. With requests on pool threads a second MCP client would interleave with the
  first. One client is the design; if a second is ever wanted, `after` becomes per-client.

### What I deliberately did not do

- No version bump and no 0.4.0 changelog entry: WP9 is the close-out after the proof run. The
  Unreleased note in `wardens/CHANGELOG.md` describes the state instead.
- No change under `wardens/src/Timberbot/`: no Timberbot bug was involved; the passthrough bug was the
  Wardens' own, and the HTTP server's query parsing is sound.
- numpy stays out of the Python dev extras: `--with numpy` keeps it out of the package, and
  `test_gen_map.py` skips without it (CI, if it ran, would skip it too).
- No separate branches per package: the session's designated branch is one branch, so the five
  packages are five commits on pull request #13, which now carries code as well as the plan.

---

## 2026-09-10, evening: planning session for iteration 04

**Plan:** [`iteration-04-first-light-verified.md`](iteration-04-first-light-verified.md).
**Goal of the iteration:** level 01 plays end to end on its own map, the defects the proof finds are
fixed at the root, and the state section in `AGENTS.md` stops saying "not yet verified".

### What I did

A planning session in a cloud container: Linux, Python 3.11, `uv`, no `dotnet`, no Timberborn. I could
read everything and run the Python checks; I could not build the mod or load a map. I changed no
code. What I produced is this file, the plan, and three pointers to them (`AGENTS.md` Read First and
state section, `wardens/README.md`, `wardens/CHANGELOG.md`).

Read, in full: `AGENTS.md`, `wardens/README.md`, `wardens/CHANGELOG.md`, `wardens/WARDEN.md`,
`.claude/skills/warden-play/SKILL.md`, `wardens/playtest/PLAYTEST.md`, `design/wardens-campaign-design.md`,
`design/wardens-campaign-map-set.md`, `design/wardens-campaign-concept.md`, `design/wardens-play.md`,
`design/wardens-wasteland.md`, `design/mapsmith.md`, `design/playtest-and-video-capture.md`,
`agents/beaver-developer.md`, `docs/plan/roadmap-v2-mod-first.md`, both map specs, the mapsmith format
reference, and the sources `WardensCampaign.cs`, `WardensChapters.cs`, `WardensFrames.cs`,
`WardensMcpServer.cs`, `WardensMcpTools.cs`, `WardensStartingPopulation.cs`, `WardensMapInstaller.cs`,
`gen_map.py`, `validate.py`, `check_cutscenes.py`, the tests, and the tiles and alerts routes in
`TimberbotReadV2.cs`. Skimmed: the story, the arc, the research plan, the cutscene design (§12), the
chapter-1 plan's status notes, the contradictions log.

Ran, on commit `1ff19b3` (main after PR #12), all clean:

| Check | Result |
|---|---|
| `uv run --project python --extra dev pytest -q wardens/tools` | 95 passed |
| `python wardens/tools/check_cutscenes.py wardens/src` | 8 scenes, `problems: none` |
| `python wardens/tools/mapsmith check wardens/maps/wardens-wasteland.map.toml` | `problems: none` (4159 tiles reachable) |
| `python wardens/tools/mapsmith check wardens/maps/wardens-02-the-pods.map.toml` | `problems: none` (8509 tiles reachable) |
| `python wardens/tools/mapsmith check "wardens/src/Maps/Wardens 01 First Light.timber"` | `problems: none` (lenient model, as the note says) |

Not run here, because they need the game's files or the SDK: `validate.py`, `gen_map.py --check`
(numpy is not in the dev extras), `dotnet build`, `dotnet test`.

### What I found by reading (nothing below is reproduced yet)

1. **Three of the four "Timberbot API bugs" in PLAYTEST.md are one line.** The `timberbot` MCP tool's
   `Loopback` appends `?format=json&…` to the path. A query passed inside `path` therefore ends in
   `…&y2=55?format=json`, and the last parameter parses as 0. That is the tiles bound "dropped", the
   `offset` "ignored", and `name=ChargingPost` matching nothing. Plan WP1 has the fix and its tests.
2. **The dropped MCP connection has a plausible cause.** `WardensMcpServer.ListenLoop` handles each
   request inline on one thread; `frame` and `chat_read` block that thread for up to 120 s; every
   other request queues behind them. Plan WP2 moves each request to the thread pool; the locks it
   relies on are already there.
3. **The level-numbering dispute has a tie-break.** The C# level table, the arc and the story all say
   02 is *The Sump* and 03 is *The Pods*; only `wardens-campaign-maps.md` §6.5 and the spec named
   `wardens-02-the-pods` disagree, and the spec's land matches neither level's brief. `AGENTS.md`
   already says the table is the source. Plan WP7.
4. **Level 10 costs less than the map-set doc thought.** `gen_map.py` already carries
   `Terrain.REQUIRE_BADWATER` and per-level `contract()`; *Home* is a subclass and a registry entry.
   Plan WP8, optional.
5. **The "five bots" of the design are thirteen in the game.** `WardensStartingPopulation` replaces
   every beaver the game mode spawns, adults and children; the playtest counted 13. The Chapter 1
   economy was tuned for five. That is a decision for the author (plan WP6 item 3).
6. **The flooded-Core softlock is unattributed.** The playtest that saw it did not confirm which map
   it ran on, and a dry-shipped map cannot pool 0.1 water on a Z 8 pad by itself. The plan makes this
   the first thing the proof run measures (WP4 step 4) and gives the decision tree (WP6 item 1).

### Where the mod stands

| Fact | Source |
|---|---|
| Version 0.3.6 in `manifest.json`; the changelog's top entry is "Unreleased (0.3.5)" | `wardens/src/manifest.json`, `wardens/CHANGELOG.md` |
| Verified in-game on 2026-09-10: the mod loads, the map installer runs at the main menu, `initialize` returns live-state instructions, `prompts/list` and `prompts/get` work, `campaign` reports "not a campaign level" on a vanilla map | `wardens/CHANGELOG.md`, Unreleased |
| Never done: a new game on `Wardens 01 First Light`; the chapter line on that map; any cutscene; the completion toast; `campaign.json` written by a real run | `AGENTS.md` state section, `CHANGELOG.md` |
| Spike A (bots replace the starting beavers) works in the game | PLAYTEST.md findings: 13 Wardens, 0 beavers on day 1 |
| The level transition (`ILevelStarter`), the *Continue campaign* button, `/api/campaign` are not built | `wardens-campaign-design.md` §10 steps 4–5; `grep ILevelStarter wardens/src` finds nothing |
| Level 02 has no map, no chapter table, no tutorials; its row in the table has an empty ending tutorial and `shipped: false` | `WardensCampaign.cs`, `Levels` |
| The map format is verified for 0.7.10 only; water ships dry; `RuinColumnH*` and `UndergroundRuins` are verified names in that layout | `timber-format.md`, `wardens-wasteland.md` |
| No CI runs on this fork (GitHub Actions off); nothing under `wardens/src/*.cs` is compiled anywhere but the game machine | `AGENTS.md`, Quick Reference |
| No open issues, no open pull requests on 2026-09-10 evening | GitHub |

### What you should do, in this order

**If you are in a cloud container (no game):** WP1, WP2, WP7, WP5, WP8 from the plan, in that order,
each as its own branch and pull request, each with its offline checks green. Do not touch level 02.
When you finish, add your entry here and list, for the human, exactly which builds and which checklist
rows are waiting on the game machine.

**If you are at the game machine:** WP3 first (twenty minutes, it settles the format questions), then
build whatever cloud packages have merged (`dotnet build wardens/src/Wardens.csproj -c Release`),
then WP4, the proof run, with its record committed. Then WP6 with whatever the run found. WP9 closes.

**Either way:** before you push, the checks in the plan's "Global constraints" are green; every C#
change in `wardens/src` waits for a game-machine build before it is called done; the five documents
of the agent contract move together.

### Rules that do not bend (pointers, not repetition)

`AGENTS.md`, "Wardens Side" is the list. The ones that bite most often: every name the game resolves
at load is a crash risk (run `validate.py`); generators are the source; game-state work on the main
thread only; the Timberbot copy is verbatim; the map name, seed and size of level 01 never change;
`wardens/src/Maps/` holds exactly the shipped maps.

### How you know the iteration is done

`wardens/playtest/runs/<date>-level-01.md` exists with every row filled; `campaign.json` from that
run shows `"completed": ["01"]`; PLAYTEST.md has no open softlock; `AGENTS.md`'s state section is
rewritten with a new date; `CHANGELOG.md` has 0.4.0; this file has your entry above mine.

### Open questions I could not answer

- Five bots or thirteen (found item 5): the author's call, before WP4 if possible, because the
  proof run's economy depends on it.
- Whether the human's game machine is available soon. If not, the cloud packages still land and the
  run waits; nothing in the plan should be skipped to compensate.
- Whether the map editor in 1.1 can save with running water (WP3 step 1 assumes it can).

### What I deliberately did not do

I did not fix the two MCP bugs myself although the fixes are small: the Wardens' C# compiles only
where the game is, a change I cannot build is a change I cannot call done, and the next agent at the
game machine will build it once for the run anyway. I did not rename the level-02 spec: it is a
judgment call the author may want to see first, and WP7 makes it a ten-minute task. I did not touch
`AGENTS.md`'s state section beyond a pointer: it is rewritten at the end of the iteration, from
evidence, not now.

---

## Template for your entry

```markdown
## <date>: <what the session was> (iteration NN)

**Plan:** <path>. **Goal:** <one sentence>.

### What I did
<environment: cloud or game machine; what was read; what was run and the results, as a table;
what changed, by pull request>

### What I found
<numbered; each with the file and the line or method it was found in; say "read" or "reproduced">

### Where the mod stands
<a table of facts with sources; only what you verified or what a named document says>

### What you should do, in this order
<split by "cloud container" and "game machine"; name the plan packages, not new work>

### How you know it is done
<observable outcomes>

### Open questions I could not answer
### What I deliberately did not do
```
