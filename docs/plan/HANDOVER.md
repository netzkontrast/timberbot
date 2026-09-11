# Handover: the Wardens, session to session

This file is the first thing to read when you continue the Wardens work. It is a stack: the newest
entry is at the top, and every session that ends adds one using the template at the bottom. An entry
says who wrote it, what they did and verified, where the mod stands with sources, what to do next and
in what order, and what they left open on purpose. The plan an entry points to says *how*.

The rest of the map: `AGENTS.md` (conventions, the state section), `wardens/README.md` (what the mod
is and how each part works), `wardens/CHANGELOG.md` (what each version added), `wardens/playtest/PLAYTEST.md`
(the in-game checklist, the MCP tool table, the findings), `design/` (why things are the way they are).

---

## 2026-09-11: the level loader, compiled and seen in the game (game machine)

**Plan:** the previous entry's game-machine list (build, run the transition, answer
`wardens-campaign-maps.md` §5). **Goal:** the game starts on the right map, from the mod.

**Status: `built` (0.4.1). The main-menu level start is `verified`; the in-game `campaign next`
switch is `built`, not run.**

### What I did

Game machine: Windows 10, .NET SDK 8.0.300, Timberborn 1.1.2.4 at `F:\Steam`. Decompiled
`Timberborn.GameSceneLoading`, `SceneLoading`, `MainMenuSceneLoading`, `GameSaveRepositorySystemUI`,
`Autosaving` and `MapRepositorySystem` with `ilspycmd`. Built main as it arrived, then rewrote the
transition on what the decompile shows and built, deployed and launched again.

| Command | Result line |
|---|---|
| `dotnet build wardens/src/Wardens.csproj -c Release -p:GameManagedDir=F:\…\Managed` on main `147e615` (0.3.7) | `0 Warnung(en)`, `0 Fehler` |
| the same after the rewrite (0.4.1) | `0 Warnung(en)`, `0 Fehler` |
| launch with `campaign.handoff.json` = `{"level":"01"}` | Player.log `FactionId: Wardens, MapFileReference: Name: Wardens 01 First Light, … GameMode: Order: 20` |
| `uv run --project python --extra dev pytest -q .claude/skills/driving-iterations/scripts wardens/tools` | `116 passed in 6.78s` |
| `python wardens/tools/mapsmith levels --verify` | `levels: consistent with WardensCampaign.cs` |
| `python wardens/tools/check_cutscenes.py wardens/src` | `problems: none` |
| `python wardens/tools/validate.py` | one problem, pre-existing on main: `level 02: Wardens 02 The Sump.timber exists but the table says shipped=false` |
| `check_doc_drift.py` over the three mirrors | `problems: none` (all `UNMARKED`, WP5) |
| `dotnet test wardens/test` | **not run** (SDK 8, tests target net10) |

The full `[Wardens]` log of the run is in `wardens/playtest/PLAYTEST.md`, "Level start from the main menu".

### Publish check

Branch `feat/level-loader-verified`; `git ls-remote origin feat/level-loader-verified` ->
`07f445936d5983614d352b462dd18a6e59735195` (code and docs; this entry is the next commit on it).

### What I found

1. **The cloud transition compiled and could never have worked** (read). *Symptom:* 0 errors on the
   first build. *Source:* `ilspycmd Timberborn.GameSceneLoading.dll`: `NewGameConfiguration(string,
   MapFileReference, GameModeSpec, string)`; 1.1 has no `NewGameMode`; `GameSceneLoader` has
   `StartNewGame`, `StartNewGameInstantly`, `StartSaveGame*`, no main-menu method; `MapRepository`
   has no `GetCustomMaps`/`GetUserMaps`; nothing registered `GameSceneLoader` with the locator. *Consequence:*
   every `campaign next` would have written a handoff that the menu then also failed to start. *Remedy:*
   the rewrite calls the read API directly (0.4.1).
2. **Finding 5 of the previous entry is answered: injecting is safe** (read).
   `GameSceneLoadingConfigurator`, `GameSaveRepositorySystemUIConfigurator` and
   `MainMenuSceneLoadingConfigurator` are `[Context("Game")]` as well as MainMenu, `AutosavingConfigurator`
   is Game; `ValidatingGameLoader` (the in-game Load dialog) takes a `GameSceneLoader`. The MCP server
   came up after the rewrite, so `WardensConfigurator` resolved. *Remedy:* `WardensReflect.cs` and
   `WardensServiceLocator.cs` deleted, as the locator's own header asked once the bindings were confirmed.
3. **Starting a game from a MainMenu `Load()` is safe** (read, then seen). `SceneLoader.LoadSceneCoroutine`
   waits while `_isLoading`, so the request queues behind the menu's own load.
4. **The retired map never left** (seen). *Symptom:* `Wardens Wasteland.timber` still in the Maps folder.
   *Source:* `RemoveRetired` ran only when `Installed > 0`. *Consequence:* a second near-identical map in
   the list. *Remedy:* removed on every launch; the run logged `1 retired removed`.
5. **Leaving a level can keep the old colony** (read). `Autosaver.CreateExitSave()` is what *Exit to main
   menu* does; the new-game strategy calls it first. Not yet seen in a run.

### Where the mod stands

| Fact | Source |
|---|---|
| `{"level":"01"}` in `campaign.handoff.json` opens the game on level 01 as a Wardens new game | PLAYTEST.md, the run above |
| `campaign action=next` (in a game) exit-saves and starts the level; not yet run | `WardensLevelTransition.cs` |
| Level 02 still `shipped: false`, so `next` to 02 is refused by every strategy | `WardensCampaign.cs` `Levels`; `validate.py` |
| The vanilla Mods dialog still needs one click at every launch | seen |

### What you should do, in this order

Disposition of the previous entry's items: game machine "build and run the transition" **done**
(findings 1–3, §5 items 3 and 6 answered in `wardens-campaign-maps.md`); WP1, WP2, WP5, WP8 (cloud)
**deferred**, untouched; WP3, WP4, WP6 (game) **still open**; level 02 chapter table **blocked** on WP4
as recommended; "five bots or thirteen" **still open**.

**Game machine:** in a level-01 game, call `campaign action=next level_id=01 force=true` through the MCP
server and paste the reply and the `[Wardens] transition` lines into PLAYTEST.md; check an autosave
appeared under `Saves/First Light/`. Then WP4, the level-01 proof run.

**Cloud container:** WP1, WP2 as before.

### How you know it is done

The in-game `next` reply shows `started: true, strategy: new game` and the new map loads; PLAYTEST.md
has its log lines.

### Open questions I could not answer

None new. The level-02 question of the previous entry stands.

### What I deliberately did not do

I did not run `campaign next` in the game the human had just started (it ends that colony). I did not
flip level 02 to `shipped: true`. I did not keep the reflection strategies as a fallback: the API they
guessed at is now read, and a second path that cannot work is noise in every log.

---

## 2026-09-10, night: level 02 and the level transition (out of plan order)

**Plan:** none — the author asked directly for map design and "the functionality to load a map from
within the mod whenever needed". That is WP7 plus `wardens-campaign-design.md` §4.4, taken ahead of
WP1/WP2. **Goal:** level 02 exists as land with a machine-checked contract, and a running game can
ask for the next level.

**Status: the Python is `checked` (cloud). The C# is `written` — this container has no .NET SDK and
no game DLLs, so nothing in `wardens/src` has been compiled.**

### What I did

Cloud container: Linux, Python 3.11, `uv`, no `dotnet`, no game DLLs, no Timberborn. Read
`design/wardens-campaign-map-set.md`, `wardens-campaign-maps.md`, `wardens-campaign-design.md`,
`wardens-campaign-arc.md`, the two newest HANDOVER entries, the iteration-04 plan's package table,
and the sources `WardensCampaign.cs`, `WardensMapInstaller.cs`, `WardensMcpTools.cs`,
`WardensConfigurator.cs`, `WardensMainMenuConfigurator.cs`, `TimberbotAutoLoad.cs`, `gen_map.py` and
all of `wardens/tools/mapsmith`.

Two decisions were put to the author as blocking questions before any work started, both answered:
the two generators converge on **mapsmith** (`gen_map.py` keeps level 01's provenance), and the
transition is **built now with reflection** for the unverified APIs rather than deferred.

Changed, all on one branch, one pull request:

- `wardens/tools/mapsmith/contracts.py` (new) and a `[contract]` spec table; `avoid` on the `hill` op.
- `wardens/maps/wardens-02-the-sump.map.toml` (new) and `wardens/src/Maps/Wardens 02 The Sump.timber`.
- `wardens/maps/levels.toml` (new) and `mapsmith levels --verify`.
- `wardens/src/WardensLevelTransition.cs`, `WardensHandoff.cs`, `WardensReflect.cs`,
  `WardensServiceLocator.cs` (all new); `campaign action=next`; the two configurators.
- `wardens-02-the-pods.map.toml` -> `prototype-two-streams.map.toml` (WP7).

### Evidence

| Command | Result line |
|---|---|
| `uv run --project python --extra dev pytest -q .claude/skills/driving-iterations/scripts wardens/tools` | `116 passed in 2.54s` |
| `python wardens/tools/mapsmith check --level 02` | `problems: none` (three contract notes) |
| `python wardens/tools/mapsmith levels --verify` | `levels: consistent with WardensCampaign.cs` |
| `python wardens/tools/check_cutscenes.py wardens/src` | `problems: none` |
| `uv run --project python --extra dev ruff check --config python/pyproject.toml wardens/tools/mapsmith` | `All checks passed!` |
| `uv run --project python --extra dev mypy --config-file python/pyproject.toml wardens/tools/mapsmith` | `Success: no issues found in 12 source files` |
| `dotnet build wardens/src/Wardens.csproj -c Release` | **not run here** — no .NET SDK in this container |
| `dotnet test wardens/test` | **not run here** — same |

### Publish check

Branch `claude/relaxed-noether-pu25ru`, PR #15 open against main.
`git ls-remote origin claude/relaxed-noether-pu25ru` -> `c93ccab8936fb4cfa05c8c639a89ceaa79d7d096`.

### What I found

1. **A contract catches what an eye does not** (reproduced). The first Sump looked like a gorge map
   and was not: `single_gorge` reported zero dammable narrows, because a `river` op's `valley` caps
   run after the spur hills and flatten them (`terrain.py`, `op_river` after `op_hill`).
   *Symptom:* every cross-section along the river measured bank = 2. *Source:* op order, read and
   then measured with a cross-section dump. *Consequence:* the level has no dam decision, and the
   map still loads. *Remedy:* the new `avoid` key on `op_hill`; spurs are raised after the river.
2. **My own contract had the bug it exists to catch** (reproduced by a test). `contract_single_gorge`
   reset the current run in the off-map and near-the-edge branches without flushing it.
   *Consequence:* any gorge reaching the map border was discarded — which is most of them, since a
   river that enters and leaves the map ends its last run there. *Remedy:* one `close_run()` every
   branch goes through, plus `test_single_gorge_fails_when_there_is_nowhere_to_dam` and three others.
3. **The level-numbering dispute was already settled in the C# table** (read), exactly as the
   planning entry said. *Remedy:* the prototype spec is renamed and claims no level. WP7 is done.
4. **`SaveReference`, `SettlementReference` and `UserDataFolder` are not guesses** (read):
   `TimberbotAutoLoad.cs` lines 73-79 construct them and the Wardens csproj references those
   assemblies (lines 91-92, 120). *Remedy:* the save strategy uses them directly; only the loader
   *instance* goes through reflection.
5. **Injecting the transition's dependencies is the one change that must not be made** (read).
   Bindito resolves a configurator's bindings at scene load, so a dependency not bound in that
   context takes down `WardensConfigurator` and with it the MCP server, the chat and the chapters.
   *Remedy:* nothing new is injected; `WardensServiceLocator.cs` carries the reasoning so the next
   session does not "simplify" it back.

### Where the mod stands

| Fact | Source |
|---|---|
| Level 02 has a map, a spec and a machine-checked contract | `wardens/maps/wardens-02-the-sump.map.toml`, the evidence table above |
| Level 02's row still says `shipped: false` and has no ending tutorial, so completing it is not detectable | `WardensCampaign.cs`, `Levels` |
| `campaign action=next` exists and refuses on an unfinished level unless `force=true` | `WardensMcpTools.cs`, `NextLevel` |
| The transition never starts a level in this container; three strategies are tried in order, the fallback is a handoff file the main menu reads | `WardensLevelTransition.cs` |
| No C# in this entry is past `written` | no .NET SDK here |
| The *Continue campaign* main-menu button is still not built | `grep MainMenuPanel wardens/src` finds nothing |

### What you should do, in this order

Disposition of the previous entry's items: WP1, WP2, WP5, WP8 (cloud) **deferred** — the author
asked for map design and the transition instead, and they are unchanged and still small; WP7
**done** (finding 3); WP3, WP4, WP6 (game) **blocked** on the game machine as before; "five bots or
thirteen" **still open**; stamping the mirrors with `doc-source`/`doc-hash` **deferred** to WP5 as
that entry asked.

**If you are at the game machine:** read `docs/plan/HANDOVER.md` (this entry) and
`design/wardens-campaign-maps.md` §5. Run
`dotnet build wardens/src/Wardens.csproj -c Release` — this is the first compiler any of the new C#
has seen, so expect errors and treat them as the finding. Then start a game on
`Wardens 01 First Light` and call `campaign action=next force=true` through the MCP server, and
paste every `[Wardens] transition` and `[Wardens] handoff` line from `Player.log` into
`wardens/playtest/PLAYTEST.md`. Each names the exact type, method or property that was missing;
that list is the answer to §5 and is the deliverable of this package more than the transition is.

**If you are in a cloud container:** WP1 and WP2 from the iteration-04 plan, untouched and still
small, each its own branch and PR in the state words.

### How you know it is done

`dotnet build` result line is in a HANDOVER entry; one `campaign action=next` has produced either a
started level or a named list of missing members; `design/wardens-campaign-maps.md` §5 has a table
with answers in it instead of an open list; `AGENTS.md`'s state section stops calling the transition
"not built".

### Open questions I could not answer

**Level 02 has no chapter table and no ending tutorial, so the campaign cannot detect that it is
complete. Which comes first?**

| Option | Cost | Risk | |
|---|---|---|---|
| Write level 02's chapter table and ending tutorial next, before any of it is verified in-game | ~a day; touches `WardensChapters.cs`, the tutorial generator and the loc rows | Tuning a chapter line for a map nobody has played tends to be redone after the first playtest | |
| Verify level 01 end to end first (WP4), then write level 02's line with what that run taught | The transition stays untestable past "the map loads" until then | Level 02 sits half-built for an iteration | **recommended** |
| Ship level 02 as a free-play map with no chapter line at all | Small | The campaign silently treats it as a non-campaign map, which is the failure `mapsmith levels --verify` was written to catch | |

What settles it: the level-01 proof run (WP4). Until then the row stays `shipped: false`.

### What I deliberately did not do

I did not inject `ValidatingGameLoader` or `GameSceneLoader` anywhere (finding 5). I did not build
the *Continue campaign* button: it needs the same unverified new-game path, and one unverified
mechanism at a time is enough. I did not touch level 01's map, seed or name, or replace
`gen_map.py`'s provenance of it with a mapsmith rebuild. I did not stamp the five documents; that is
WP5's commit, as the previous entry asked.

---

## 2026-09-10, night: the `driving-iterations` skill (iteration 04, no package worked)

**Plan:** [`iteration-04-first-light-verified.md`](iteration-04-first-light-verified.md), unchanged
except its header. **Goal:** give every later session, cloud or game, one discipline for reporting a
package's state and writing this file, distilled from `netzkontrast/agency`, before any package is worked.

### What I did

Cloud container: Linux, Python 3.11, `uv`, no `dotnet`, no Timberborn. Read the agency repo (its
remote-agent doctrine, the looper loop rubrics, the steward handover template, the drift tooling, the
skills directory, the ADRs and plans), partly through subagents whose reports are summarised in the
skill's references. Wrote `.claude/skills/driving-iterations/` (SKILL.md, five references, a stdlib
doc-drift checker with six tests), pointed `AGENTS.md` and the plan header at it, and pressure-tested
the skill three times with a small model in a clean context (`references/pressure-scenarios.md`).
No C#, no generator, no map, no cutscene touched. Evidence:

| Command | Result line |
|---|---|
| `uv run --project python --extra dev pytest -q .claude/skills/driving-iterations/scripts wardens/tools` | `101 passed in 2.16s` |
| `python wardens/tools/check_cutscenes.py wardens/src` | `problems: none` |
| `python wardens/tools/mapsmith check wardens/maps/wardens-wasteland.map.toml` | `problems: none` |
| `python .claude/skills/driving-iterations/scripts/check_doc_drift.py --root . wardens/WARDEN.md .claude/skills/warden-play/SKILL.md design/wardens-play.md` | three `UNMARKED`, `problems: none` (WP5 stamps them) |
| `dotnet build wardens/src/Wardens.csproj -c Release` | not run here (game machine); nothing under `wardens/src` changed |

Branch `claude/superclaude-skills-next-steps-e3u3ya`, PR #14.

### What I found (read, not reproduced)

1. The agency repo's `ralph-skill` no longer exists (history rewritten; its successor `loop` needs the agency MCP server). What it taught survives as the rubrics and the control guards in the skill's `references/rubrics.md`.
2. A small model asked "is WP1 done?" under time pressure, without the skill, answered "ready to hand off, code complete, push to main, risks none identified" for an uncompiled C# change. With the skill it answered `checked (cloud)` with the evidence lines and the two `dotnet` commands as not run here. The two loopholes found on the way (handing over an unpushed branch; deciding an unknown by inventing a fact) are closed and recorded.
3. The five documents of the agent contract carry no drift markers yet; the checker reports them `UNMARKED` until WP5 stamps them.

### Where the mod stands

Unchanged from the entry below: version 0.3.6, level 01 never started in the game, WP1 to WP9 open. Source: that entry and `AGENTS.md`, state section.

### What you should do, in this order

Disposition of the previous entry's items: WP1, WP2, WP7, WP5, WP8 (cloud) deferred, this session built the discipline instead of a package; WP3, WP4, WP6 (game) blocked on the game machine as before; "five bots or thirteen" still open, a worked example of how to decide it is in `references/decision-methods.md`.

**If you are in a cloud container:** invoke `driving-iterations`, then the previous entry's order: WP1, WP2, WP7, WP5, WP8, each its own branch and PR, each reported in the state words with its evidence table. In WP5, stamp the mirrors with `doc-source`/`doc-hash` markers and add the checker to the plan's global constraints.

**If you are at the game machine:** WP3, then build whatever merged, then WP4 with its run record, findings in the four-slot form. Candidates the agency read surfaced for the author, none started: a select-based receive loop with a named timeout for `mcp_concurrency.py`; a 25-second cap on a single `frame` wait with a typed no-op return; the tool descriptions in the grammar of `references/templates.md` §5, from which the docs' tool tables could be derived; a sha256 lock for the verbatim Timberbot copy; a regenerate-and-diff check for the generators; a one-page decision digest emitted at session start.

### How you know it is done

This entry is above the planning entry; PR #14 merged; the next entry after this one reports a package in the state words with an evidence table.

### Open questions I could not answer

Whether the author wants the `<!-- AGENT: … -->` fill-in notes from `references/templates.md` §2 inside this file's template itself.

### What I deliberately did not do

I did not work WP1 although it was next: a skill written and a package worked in the same session would have been two half-tested things. I did not stamp the five documents: that is WP5's commit, with the contract change it belongs to. I did not edit this file's template.

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
