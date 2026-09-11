# Iteration 05: a consistent story

> **For agentic workers:** work this plan package by package and report each package's state in the
> words of the `driving-iterations` skill (`written`, `checked`, `built`, `tested`, `verified`,
> `blocked`, with the evidence line beside it). Packages marked **[game]** need the machine with
> Timberborn on it; **[cloud]** packages run anywhere with Python. Read `docs/plan/HANDOVER.md` first:
> its newest entry says why the order below is the order, and which decisions the author has taken.
> Written 2026-09-11 on the game machine against `main` at `57e2da5` (0.4.25), from three digests of
> the code, the design documents and the playtest record; nothing in it is verified beyond what those
> records say.

**Goal.** The campaign tells one story from level 01 to level 10 without contradicting itself: every
level starts where the last one left off, each task carries its own scene instead of a long opening,
new buildings arrive when the story says they do (the Iron Teeth set with the first beaver), the map
itself tells the player and the Warden where things go, the Warden can run its routine acts without an
LLM turn per placement, the Wardens can speak in dialog, and the player sees one console instead of
three panels.

**The author's asks (2026-09-11), and the package that answers each:**

| Ask | Package |
|---|---|
| split the tasks and the cutscenes into steps that depend on one another, so the cutscenes focus on the current task | WP1 |
| progressively add new buildings, especially the Iron Teeth buildings, as soon as a pod spawned the first beaver | WP2 |
| pregenerate AI suggestions for the map | WP3 |
| an in-game harness for a Claude agent, with prebuilt strategies, that runs more independently from the MCP server | WP4 |
| a dialog system | WP5 |
| clean up the mod's UI, consolidate the UI for the Timberbot part of the Wardens mod | WP6 |
| experiment with specialized savegames for levels instead of an empty map | WP7 |
| implement the missing levels | WP8 |
| (carried from the record) the level-02 water checks, the balance pass, the Science question, the Gate's first run | WP0 |

**Architecture.** No new subsystem where an existing one stretches: dialogs are scenes without a
camera, suggestions are a mapsmith output, the harness is a runner over the Timberbot write path that
is already compiled in, the console is the two Wardens panels merged and the Timberbot widget hidden.
Two things are new: a task graph with per-task scenes replacing the tutorial-bound chapter table, and
a phase gate on the building bar keyed to story events and remembered in `campaign.json`.

**Spec.** `design/wardens-campaign-design.md` §3.3 (`LevelStart`), §4.5 (per-level chapters), §8;
`design/wardens-campaign-map-set.md` §2 (the land per level); `design/wardens-cutscenes.md` §3 (the
scene format); `design/wardens-play.md` §6; `wardens/playtest/PLAYTEST.md` (every 2026-09-11 finding);
`docs/plan/HANDOVER.md` (the newest six entries). Where this plan and those disagree, this plan was
written later and from the code; say so in the document you correct.

## Global constraints

Unchanged from iteration 04, restated where they bite here:

- No Harmony, no patches; game state on the main thread only; `OffThread` MCP tools are I/O only.
- Generators are the source: `gen_buildings.py`, `gen_tutorial.py`, `mapsmith`. A hand edit to a
  generated file is mirrored in its generator in the same commit.
- Before any push: `uv run --project python --extra dev pytest wardens/tools .claude/skills`,
  `python wardens/tools/check_cutscenes.py wardens/src`, `python wardens/tools/check_level_tasks.py wardens/src`,
  `python wardens/tools/mapsmith check --level <id>` for every touched level, `mapsmith levels --verify`,
  and on the game machine `python wardens/tools/validate.py wardens/src`, all printing `problems: none`.
- The agent contract is five documents that move together: `wardens/WARDEN.md` (source),
  `.claude/skills/warden-play/SKILL.md`, `WardensMcpTools.BuildInstructions`, `design/wardens-play.md`,
  the `warden_boot` prompt. Two packages here change it (WP1, WP4); each changes all five.
- The tool surface stays at the 19 tools of 0.4.25. New needs re-home on `campaign` and `cutscene`
  sub-actions (WP3, WP4, WP5 all do).
- The Timberbot copy is verbatim. WP6 needs one change in the copy's behaviour; it is made in
  `timberbot/src` first and re-copied.
- Build with the game closed; every build bumps the version; a deploy under a running game mixes a
  DLL with newer blueprints (`No type found for key <Spec>`).
- Pinned: the map names, level 01's seed and size. A shipped map changes under its name only while
  no player has it, which is still true of both.
- Nothing under `wardens/src/*.cs` is compiled by CI. The .NET SDK on the game machine is now
  10.0.401 (`dotnet --version`, 2026-09-11), so `dotnet test wardens/test` can run there.

## 0. Where the campaign stands, and what it contradicts

Verified in the game on 2026-09-11 (`HANDOVER.md`, the top six entries): level 01 plays from a handoff
start through its 12-shot opening and eight tasks to the level-end card; Continue exit-saves and starts
level 02 as a new game; level 02's reworked river reaches the gorge and its six tasks were finished on
day 7. Both levels are `shipped: true`; `campaign.json` reads `completed: ["01","02"]`.

What the record says is inconsistent, and the package that fixes each:

| Contradiction | Source | Package |
|---|---|---|
| Two of two humans cut the openings short: Continue at the directive (01), Skip at shot 4 of 11 (02); shots 5–11 of `TheSump` have never been seen | `PLAYTEST.md` "Level 02 played through", `first-light-opening.md` | WP1 |
| The chapter table is global and bound to level 01's tutorial ids; with the tutorial off (how the author plays) no chapter scene ever fires, and `LevelEnd` would offer "Continue to Level 02" on level 02 | `WardensChapters.cs:75-87`, `wardens-campaign-design.md` §4.5 | WP1 |
| Level 02 starts with 13 bots, 10 scrap and no beaver; its premise is a beaver who needs clean water, and the design says "6 bots + 1 beaver (carried)" | `WardensStartingPopulation.cs`, `wardens-campaign-concept.md:30` | WP7 |
| The design says wood and clean water "arrive in Act II, with the beavers"; the bar has no Iron Teeth food, wood, housing or water building, and nothing changes when the first beaver wakes | `wardens-chapter-1-plan.md:7-13`, `faction-wardens.md:13-18`, this session's diff of `Blueprints.zip` (159 Iron Teeth buildings, 27 on the bar) | WP2 |
| `placement/find` offered no Sludge Pump site twice; the path router put Paths on the creek bed and on a Floodgate's site | `PLAYTEST.md` findings 5–6 (0.4.24), 7 (0.4.14) | WP3 |
| Every routine act (two Posts, two flags, paths, Stairs) costs the Warden one LLM turn each and the MCP server must be up for any of it | `warden-play/SKILL.md` §2, §2b | WP4 |
| The only branching UI is one choice card in `LevelEnd.json`; the level-05 fork and every conversation the story wants have nowhere to happen | `wardens-campaign-story.md:281-298`, `WardensCutsceneOverlay.cs:123-146` | WP5 |
| Three panels in three corners, two agent-status surfaces that share no state, four ways to show the same line, two Continue buttons for one level end | the UI table in the HANDOVER entry of 2026-09-11 (planning) | WP6 |
| Levels 03, 05, 09, 10 are rows in the table with no land, no tasks, no text | `WardensCampaign.cs:207-210`, `levels.toml` | WP8 |
| Keep it clean and Hold the gorge pass without the water the level is about; the Wardens drain by day; 5 bots or 13; 13 became 19 unexplained; Science is a number nobody spends; the Gate has never run | `PLAYTEST.md`, `HANDOVER.md` open questions | WP0 |

## 1. The packages, in order

| # | Package | Runs | Needs | Done when |
|---|---|---|---|---|
| WP0 | Carry-overs: the fresh level 02 on 0.4.25, the water checks, the balance read, Science, the Gate | game + cloud | — | each item has a PLAYTEST row or a recorded decision |
| WP1 | Steps: a task graph with per-task scenes; openings cut to five shots; the chapter table retired | cloud → game | — | a fresh level 01 opens in ≤ 45 s, each task's scene plays once when the task goes live, a reload plays nothing, `check_level_tasks.py` and `check_cutscenes.py` know the new fields |
| WP2 | Phases: the Iron Teeth beaver set arrives with the first beaver, remembered across levels | cloud → game | WP1 (the `unlocks` field) | the padlocks open within a second of `beaver.born`, a level entered afterwards starts with them open, `validate.py` enforces the phase table |
| WP3 | Sites: mapsmith writes `Levels/<id>.sites.json`; the tasks, the frame, the panel and the runner read it | cloud → game | — | every placing task of levels 01 and 02 has a site, every site passes the walk and footprint checks, `campaign action=tasks` carries them |
| WP4 | Strategies: an in-game runner for prebuilt strategies, assist and auto modes, a run record | cloud → game | WP3 | level 01's opening runs to Scavenge done with no MCP client connected; the record names the day each task fell |
| WP5 | Dialogs: scenes without a camera; speakers, branching, the speaker card | cloud → game | WP1 | the first-beaver dialog plays on `phase:beavers`, a choice writes `story.json`, `check_cutscenes.py` rejects a dangling `next` |
| WP6 | The console: one Wardens panel; the Timberbot widget hidden in the Wardens build; one voice | design → game | WP1, WP5 | one panel on the left carries tasks, the dialog slot and the Uplink; the Timberbot widget, modal and console do not appear on a Wardens map; the ready gate is a toggle in the header |
| WP7 | Savegame starts: a level can start from a shipped save; level 02's start carries a beaver | game | — | Continue on level 01's card loads level 02's start save (`strategy: save`), the opening plays, a reload of it does not |
| WP8 | Levels 03 *The Pods*, 10 *Home*, 05 *The Archive*, 09 *The Ark* | cloud → game, one level per session | WP1–WP3, WP5, WP7 | each level `shipped: true` with land, tasks, sites, an opening, its dialogs and a playtest row |
| WP9 | Close: changelog 0.5.0, `AGENTS.md` state, the five documents stamped | cloud | all | the usual |

Cloud-first packages in this order: WP3, WP1's checker and data half, WP5's checker half, WP8's specs
and contracts. Game-machine packages: everything that compiles, in the order WP0-item-1, WP1, WP2, WP7,
WP4, WP5, WP6. The next session's order is §3.

---

## WP0: Carry-overs [game + cloud]

Each item is small and each blocks the story being consistent.

- [ ] **1. A fresh level 02 on 0.4.25** (game, 20 min). `{"level":"02"}` in `campaign.handoff.json`,
  launch. Check: the river and the creek wet at tick 0 (`/api/tiles` y 44–48, x 30–40: `water > 0`);
  `TheSump` to the end, all 11 shots, screenshots with `scene-shots.ps1` while Timberborn is in front;
  Salvage's target 20 on the panel. Rows in `PLAYTEST.md`. Shots 5–11 have never been seen by anyone;
  this is the last time the whole opening plays before WP1 cuts it, so judge each shot's framing now.
- [ ] **2. The water checks** (cloud, then game). Recommended: a new check type `water_depth`
  `{box, min_depth, count}` counting tiles whose deepest column holds at least `min_depth`
  (`IThreadSafeWaterMap.WaterColumns[..].WaterDepth`, the member `clean_water` already reads), used
  twice: Keep it clean becomes 40 tiles clean **and** ≥ 0.5 deep in a box behind the gates (a pond,
  not a creek); Hold the gorge moves its box to x ≥ 57 and adds ≥ 20 tiles ≥ 1.0 deep upstream of it
  (x 44–56). The task texts say "raise the gates to 1.0" in words. Alternative the author may prefer:
  keep the counts and only move the box. `check_level_tasks.py` learns the type.
- [ ] **3. The balance read, before any tuning** (cloud: decompile; `ilspycmd` is on the game
  machine). Read `Timberborn.Bots` (the Energy need's effects, what a bot at 0 does) and where 13 Wardens
  became 19 (`BotFactory` is called once in the mod; the growth is vanilla: read `BotAssembler`-less
  paths, `CharacterSpawner`, and the pod's `BreedingPod` for a bot outcome). Record both in
  `PLAYTEST.md` under "Balance" with the member names. Only then decide Posts (capacity, count, the
  Core's 150) and the bot count (WP7's `prepare_start` caps it per level, which retires "5 or 13").
- [ ] **4. Science.** With WP2's phases keyed to story events, Science unlocks nothing. Recommended:
  drop `SciencePointsNumbercruncher` from the Cruncher's recipes (`gen_buildings.py`) and reword the
  Signal caption and the act list to "Data Cores for the Archive"; the Cruncher's power tension stays.
  Deferred until WP2 is `verified`, so the choice is made once.
- [ ] **5. The Gate's first run** (game, with two saves). Walk the six rows of `PLAYTEST.md`
  "Checks for the Gate" on the author's level 01 colony and a level 02 game. Findings in the four-slot
  form. WP7 depends on the Gate's `districts` readings surviving a shipped-save start.

---

## WP1: Steps — a task graph with per-task scenes [cloud → game]

**Why.** The openings front-load everything the level will show, and both humans left early. The
five chapter scenes hang on level 01's tutorial ids, which never finish with the tutorial off. The
tasks are the one progression the game actually runs (`WardensLevelTasks.cs`), so the scenes attach
to them: a task's scene plays when the task goes live, shows only its land and its ask, and is over
in ten seconds. The opening keeps five shots: the restart, the land in one flight, the Core, the
Wardens, the directive.

**Files:**
- Modify: `wardens/src/WardensLevelTasks.cs` (`after`, `scene`, `done_scene`, `unlocks`; `Live` replaces `Current`; the reconcile poll), `WardensTaskPanel.cs` (live tasks, a Show button per site once WP3 lands), `WardensCutsceneScript.cs` + `WardensCutscenes.cs` (triggers `task:<level>.<Id>`, `task_done:<level>.<Id>`, `level_complete:<Id>`), `WardensFrames.cs` (`task.live[]`, events `task.live:<Id>`), `WardensMcpTools.cs` (`campaign action=tasks` shape; `BuildInstructions`)
- Delete: `WardensChapters.cs`'s table and the `chapter` tool's `unlock`; keep a `chapter` tool that answers "retired, see tasks" for one version, then remove it with the five documents in the same commit. `Cutscenes/Badwater.json`, `Signal.json`, `Pods.json`, `Power.json`, `Green.json`, `LevelEnd.json` become task scenes or go (table below)
- Modify: `wardens/src/Levels/01.tasks.json`, `02.tasks.json`, `Cutscenes/FirstLight.json`, `TheSump.json`, new `Cutscenes/T01.*.json`, `T02.*.json`, `Localizations/enUS.csv`
- Modify: `wardens/tools/check_level_tasks.py` (+tests), `check_cutscenes.py` (+tests), `validate.py` (the chapter rules go)
- Modify: the five documents; `design/wardens-cutscenes.md` (trigger table), `wardens/README.md`, `CHANGELOG.md`

**The task file, extended** (unknown fields were already ignored, so old files stay valid):

```json
{ "id": "Sump",
  "title": "Wardens.Task.01.Sump", "text": "Wardens.Task.01.Sump.Text",
  "after": ["Scavenge"],
  "scene": "T01.Sump",
  "done_scene": null,
  "unlocks": [],
  "checks": [ { "type": "built", "template": "SludgePump.Wardens", "count": 1 },
              { "type": "stock", "good": "Badwater", "count": 20 } ] }
```

- `after`: task ids that must be done first. Omitted = the previous task in the file (today's
  behaviour). `[]` = live from the start. A task is **live** when all of `after` are done and it is
  not done. Several tasks may be live at once (level 01: `Store` and `Reeds` after `Sump`).
- `scene`: played once, the moment the task goes live, through the trigger `task:<level>.<Id>`.
  `done_scene`: the moment it is done, `task_done:<level>.<Id>`. Both optional.
- `unlocks`: WP2's field; `["phase:beavers"]` opens a phase when the task is done.
- The reconcile rule: on the first poll after a load, tasks that are already achieved tick through
  **silently** (no toast, no scene) — the way `WardensChapters` and the tutorial poll already do.
  Only tasks that go live *after* the first poll play their scene. A save from mid-level therefore
  plays at most one scene on load: the newly live task's.

**Triggers.** `task:` and `task_done:` are level triggers by policy (Wardens + `cutscenes: true`, not
the tutorial), the same as `level:`. `level_complete:<Id>` replaces `chapter:Green` for the end
card; `LevelEnd.json` becomes `L01.End.json` with `"on": ["level_complete:01"]` and its choice card,
or is retired in favour of the panel's card (two Continue buttons is one too many; recommended:
retire the scene's card, keep a one-shot end scene with no choice).

**The split** (FirstLight 12 → 5, the rest into task scenes of 1–2 shots, ≤ 12 s each):

| Was | Becomes | Trigger |
|---|---|---|
| FirstLight: restart, archive, head, river, sump, shore, ruins, spring, crossing, core, warden, directive | `FirstLight`: restart, the land (one flight head → river → sump → spring), the Core, the Wardens at the door, the directive | `level:01` |
| Badwater (2), the sump shot, the shore shot | `T01.Charge` (the Core's door, the Post's tile), `T01.Scavenge` (the pad's ruins), `T01.Sump` (the Sump and its seep, the shelf) | `task:01.*` |
| Pods (2) | `T01.FirstLight` (where the pods stand) | `task:01.FirstLight` |
| Power (2) | `T01.Power` (the Cell, what it burns) | `task:01.Power` |
| Green (2), `mark: birthday` | `T01.Born` (the first beaver, the mark) | `task_done:01.FirstLight` |
| Signal (2) | retired: level 01 has no Cruncher task; the Cruncher returns with WP2's phase or level 03 | — |
| LevelEnd (4) | `L01.End` (one card, no choice) or nothing | `level_complete:01` |
| TheSump 11 | `TheSump` 5 (arrival, the water in one flight source → confluence → gorge, the wrecks, the Core, the directive) + `T02.Creek`, `T02.Clean`, `T02.Drain`, `T02.Gorge` | `level:02`, `task:02.*` |

The `cutscene-director` agent frames each new scene in the running game (five passes at most per
scene). Every number in a caption is read from the built map.

- [ ] **Step 1 [cloud]:** `check_level_tasks.py`: `after` ids exist and form no cycle; `scene` /
  `done_scene` name an existing scene whose `on` lists the matching trigger; tests for each.
  `check_cutscenes.py`: `task:` / `task_done:` / `level_complete:` triggers cross-checked against the
  task files and the level table; a `chapter:` trigger is an error once the table is gone.
- [ ] **Step 2 [cloud]:** the task files, the scene files and the loc rows as in the table, checked.
- [ ] **Step 3 [cloud, `written`]:** the C#. `Live` is a list; `UpdateSingleton` polls every live task;
  the first poll is the reconcile; `TaskLive` and `TaskDone` events; the cutscene runner subscribes
  and fires the triggers; the frame's `task` gains `live: [{id, title}]`. The chapter service goes;
  `WardensFrames` drops `chapter` and `attention`'s `chapter.next`; `wardens_status` drops `chapter`.
- [ ] **Step 4 [game]:** build, then: the author's day-31 save by `autoload.json` (expect the tasks
  to tick through with no scene, one `[Wardens] tasks: reconciled N` line); a fresh level 01 by the
  handoff (expect `FirstLight` 5 shots, then `T01.Charge` the moment the game unpauses, `T01.Scavenge`
  when Charge falls, and so on; `Player.log`: `cutscene T01.Charge: queued by task:01.Charge`).
  Skip once mid-task-scene: the game returns paused and unlocked.
- [ ] **Step 5:** the five documents (the act list becomes the task list with the scenes named),
  `wardens-cutscenes.md`'s trigger table, README, CHANGELOG, a HANDOVER entry.

---

## WP2: Phases — the Iron Teeth set arrives with the first beaver [cloud → game]

**Why.** The bar opened whole on 2026-09-11 because the padlocks gated bot buildings behind
tutorials nobody finishes. The author's ask now is different: keep everything the bots need open from
frame one, and let *new* buildings arrive when the story turns — the first beaver. That is a phase
gate, keyed to story events, remembered in `campaign.json` so a later level starts where the story is.

**Mechanism.** The one that was seen in the game on 0.4.1 (`chapters: 5 gates, gating=True`): a
phase building ships with `ScienceCost: 999999` and is unlocked through
`BuildingUnlockingService.UnlockIgnoringCost` + `ToolUnlockingService.UnlockInternal`, the calls
`WardensChapters.CheckTheBar` still makes. The padlock is visible from the start (the player sees what
is coming). *Alternative, unverified:* hide the tool button until the phase opens; needs a
`ToolButton` visibility call nobody has used here — only if the author prefers hidden, and then as a
spike first.

**The phase table** (`gen_buildings.py`, `PHASES`; `validate.py` enforces it; `WardensPhases.cs`
reads the same table from the blueprints' `ScienceCost` and a `WardensPhaseSpec {Phase}` written into
each phase blueprint):

| Phase | Opens on | Buildings (re-specced from Iron Teeth by `gen_buildings.py`, scrap costs, bot-default workers) |
|---|---|---|
| `boot` | always | the 27 on the bar today |
| `beavers` | `beaver.born` (the first, ever) | **Water Pump** ← `DeepWaterPump` (clean water, the beavers' need), **Cistern** ← `MediumTank`, **Gatherer Flag** ← `GathererFlag`, **Field** ← `FarmHouse` (Kohlrabi first), **Bunk** ← `Barrack`, **Lumberjack Flag** ← `LumberjackFlag`, **Sawmill** ← `WoodWorkshop`, **Campfire** ← `Campfire`, **Medical Bed** ← `MedicalBed`, **Warehouse** ← `SmallWarehouse` (goods for two species) |
| `industry` | level 03 entered, or a task's `unlocks` | **Centrifuge**, **Decontamination Pod**, **Smelter**, **Metalsmith**, **Cruncher II** ← `Numbercruncher` (the Signal chapter's home), **Bot Assembler**, **Bot Part Factory**, **Hydroponic Garden**, **Large Tank**, **Irrigation Barrier**, **Dynamite** |
| `city` | level 09 entered | the monuments; **the Ark** ← `EarthRepopulator` (the wonder art `FactionWonderSpec` already points at) |

The lists are the author's to edit; the mechanism does not care. Iron Teeth buildings that need a
good the Wardens cannot make (Grease, Extract, Coffee) wait for the recipe that makes it.

**Persistence.** `campaign.json` gains `phases: ["beavers"]`. `WardensPhases.Load()` unlocks every
building of every open phase (replacing `CheckTheBar`'s "unlock anything priced" with "unlock what the
story opened, warn about the rest"). `OnBeaverBorn` (`BeaverBornEvent`, already handled in
`WardensFrames`) opens `beavers` once, toasts, says one Uplink line, fires the trigger
`phase:beavers` (WP5's first dialog hangs on it). A task's `unlocks: ["phase:industry"]` does the
same. `campaign action=phase open=<id>` is the dev path; `frame.phases` lists the open ones.

- [ ] **Step 1 [cloud]:** `gen_buildings.py`: the `beavers` batch as `vanilla(...)` re-specs with
  the phase field; `PHASES`; `validate.py`: `boot` buildings cost 0, phase buildings cost the lock
  value and carry `WardensPhaseSpec`, no building in two phases; `test_validate.py` covers all three.
  Needs the game's `Blueprints.zip`: the generator runs on the game machine, the tests anywhere.
- [ ] **Step 2 [cloud, `written`]:** `WardensPhases.cs` (bind in `WardensConfigurator`), the
  `campaign.json` field, `frame.phases`, the `campaign` sub-action, `WardensLevelTasks`' `unlocks`.
- [ ] **Step 3 [game]:** build; a fresh level 01: ten padlocks on the bar, `Player.log`
  `[Wardens] phases: open=[boot], locked=10`; the author's day-31 save (a beaver lives): the padlocks
  open at load, `phases: open=[boot, beavers]`; a new game on level 02 afterwards starts with them
  open. `validate.py`: `problems: none`.
- [ ] **Step 4:** README ("The bar"), CHANGELOG, the act list in the five documents ("when the
  first beaver wakes, the bar grows: water first").

---

## WP3: Sites — the map tells you where things go [cloud → game]

**Why.** Every "where" the Warden needed this week it got wrong or the finder got wrong: the pump
shelf, the gate tiles, the route to the creek. mapsmith knows the terrain, the walk model, the
footprints and the water; it can name the sites when it builds the map, and both the agent and the
player can read them.

**Output.** `wardens/src/Levels/<id>.sites.json`, written by `mapsmith sites --level <id>` (and by
`build`), deployed with the level files:

```json
{ "level": "01", "spec_sha": "…", "sites": [
  { "id": "posts",       "for": "Charge",   "template": "ChargingPost.Wardens", "tiles": [[24,49,8],[22,49,8]], "why": "beside the Core, on the pad, a shaft's reach" },
  { "id": "flags",       "for": "Scavenge", "template": "ScavengerFlag.IronTeeth", "tiles": [[17,49,8],[18,49,8]], "why": "the pad's three ruins within range, on foot" },
  { "id": "stairs.sump", "for": "Sump",     "template": "Stairs.Wardens", "route": [[20,52,8],[20,53,7]], "why": "the shore's terraces 8 → 7 → 6" },
  { "id": "pump",        "for": "Sump",     "template": "SludgePump.Wardens", "tiles": [[32,47,6]], "why": "the west shelf, water 1.2 deep beside it" },
  { "id": "gates",       "for": "Creek",    "template": "Floodgate.Wardens", "tiles": [[38,42,6],[39,42,6]], "height": 1.0, "why": "above the confluence, banks either side" },
  { "id": "gorge",       "for": "Gorge",    "template": "Levee.Wardens", "tiles": [[62,46,5],[62,47,5],[62,48,5]], "why": "the narrows: banks ≥ 3 above the bed" } ] }
```

Site kinds and their rules (each a mapsmith function with a test): `beside` (n free flat tiles of the
footprint within r of an anchor, on the anchor's level), `flags` (a flat tile within the flag's range
of ≥ k scrap yielders reachable on foot), `stairs` (the cheapest Stairs route between two levels from
the walk model), `pump` (a bank tile whose neighbour column holds water ≥ d, footprint flat),
`gates` (a cross-section of a watercourse above a named confluence with both banks higher than the
bed), `dam` (the narrowest cross-section whose banks are ≥ 3 above the bed), `flat_ground` (a flat
poisoned/clean patch of ≥ n tiles for a Reed Bed or a Field), `near_spring` (irrigated ground for the
Planter). `mapsmith check` fails a site whose tiles are not flat, not free, not on foot or by the
listed Stairs from the Core, or whose footprint (`SIZES`) does not fit.

**Consumers.** `WardensLevelTasks` loads the file and attaches each task's sites to `State()` and to
the frame's `task.live[].sites`; the task panel gets a **Show** button per site that calls
`WardensPointer` (WP6 moves it into the console); the runner (WP4) places on them; the act lists in
`warden-play` cite them instead of hand-typed coordinates. The `timberbot` placement finder is not
touched — the sites answer the level's questions, the finder stays the general tool.

- [ ] **Step 1 [cloud]:** the site functions and `sites` command with tests; levels 01 and 02 get
  their files; every placing task in both task files names a site (`check_level_tasks.py`: a task
  with a `built`/`built_in`/`powered` check must have ≥ 1 site whose template matches).
- [ ] **Step 2 [cloud, `written`]:** the loader in `WardensLevelTasks`, the frame field, the panel button.
- [ ] **Step 3 [game]:** build; on level 02, `campaign action=tasks` shows the gate tiles; Show points
  at them; a Floodgate placed on each by the tool bar completes Creek. On level 01 the pump site is
  placeable by `POST /api/building/place` with the site's tile.

---

## WP4: Strategies — the in-game harness [cloud → game]

**What the ask means, as read.** The Warden's routine acts (the opening of every level, Stairs to a
shelf, gates raised to 1.0) are the same every time and cost an LLM turn each over MCP; and a playtest
of a level needs a human or a connected Claude. A runner inside the game that executes a prebuilt
strategy for a task — placing on WP3's sites through the Timberbot write path that is already compiled
in — needs neither. Claude keeps purpose, conversation and deviation; the runner keeps logistics. A
second reading, an in-game Claude API client that runs the loop itself, builds on the same runner and
is WP4c, taken only if the author wants it.

**Files:**
- Create: `wardens/src/WardensStrategies.cs` (the runner, main thread, one step per poll), `wardens/src/Strategies/01.json`, `02.json`, `wardens/tools/check_strategies.py` (+tests)
- Modify: `WardensLevelTasks.cs` (a live task with a strategy in `assist` mode shows **Do it**; in `auto` mode runs it), `WardensTaskPanel.cs`, `WardensMcpTools.cs` (`campaign action=strategy run|status|stop task=<Id>`), `WardensMcpServer.cs` (`"strategy": "off" | "assist" | "auto"` in `settings.json`), `WardensFrames.cs` (`strategy` block, event `strategy.step:<task>.<n>`)
- The five documents (the Warden's loop changes: "a task with a strategy is the runner's; you watch it and say what it did").

**The strategy file:**

```json
{ "level": "01", "tasks": {
  "Charge":   [ { "place": "ChargingPost.Wardens", "site": "posts", "count": 2 },
                { "shaft": { "from": "core", "to": "posts" } },
                { "wait": { "type": "powered", "template": "ChargingPost.Wardens", "count": 2 } } ],
  "Scavenge": [ { "place": "ScavengerFlag.IronTeeth", "site": "flags", "count": 2 },
                { "path": { "from": "flags", "to": "core" } } ],
  "Sump":     [ { "stairs": "stairs.sump" }, { "place": "SludgePump.Wardens", "site": "pump" },
                { "path": { "from": "pump", "to": "core" } } ],
  "Creek":    [ { "place": "Floodgate.Wardens", "site": "gates", "count": 2 }, { "floodgate": { "site": "gates", "height": 1.0 } } ] } }
```

Steps: `place`, `path`, `shaft`, `stairs`, `floodgate`, `recipe`, `priority`, `workers`, `wait`
(a task check), `say`. Each runs through the in-process methods behind the Timberbot routes
(`TimberbotPlacement`, `TimberbotWrite`), never over HTTP; each writes one line to the Uplink as the
Warden ("Two Posts beside the Core."), and one `[Wardens] strategy` log line. A failed step stops
the task's strategy with the failure in the frame and the Uplink; the runner never retries a
mutation blind (the playbook's own rule). `check_strategies.py`: every step's site exists in the
sites file with a matching template, every `wait` is a valid check, every template is on the bar in
the phase the task can be reached in.

**The harness.** `settings.json` `"strategy": "auto"` plus `autoload.json` or a handoff is a
playtest with nobody at the keyboard: the runner plays each live task's strategy as it goes live,
and `Documents/Timberborn/Mods/Wardens/runs/<utc>-<level>.jsonl` records one line per event (task
live/done with day and tick, each step and its result, the daily `bots.energy_min`, every
`status.alert`). `wardens/playtest/run_dump.py` already reads the log; it learns this file. A level's
regression test is: launch, wait, read the record — the proof run of iteration 04, without the human.

- [ ] **WP4a [cloud → game]:** the runner with `place`, `path`, `stairs`, `shaft`, `wait`, `say`;
  `assist` mode; level 01's Charge and Scavenge strategies. Verified: a fresh level 01, the author
  presses **Do it** twice, both tasks fall with no MCP client connected.
- [ ] **WP4b [game]:** `auto` mode and the run record; level 01 to Scavenge done unattended; the
  record committed under `wardens/playtest/runs/`.
- [ ] **WP4c [deferred, the author's call]:** an in-game Claude client (`claude-api` skill: Messages
  API, the 19 tools as function definitions, a key in `settings.json`, the frame loop in C#). Only
  after WP4b, and only if the MCP path is not enough.

---

## WP5: Dialogs — scenes without a camera [cloud → game]

**Why not a new system.** A scene already has captions, `args`, choices recorded in `story.json`,
`when`, `say`, triggers, a checker and a tuning loop. A dialog is a scene with `letterbox: false`,
`pause: false`, no camera keyframes, a `speaker` per shot and a `next` per choice. The overlay draws
a speaker card instead of the letterbox. One runner, one checker, one record.

**Schema additions** (`WardensCutsceneScript.cs`, `check_cutscenes.py`):
- scene: `"kind": "dialog"` (implies `letterbox: false`, `pause: false`, no speed lock; the game
  keeps running while the card is up); `"speaker_default"`.
- shot: `speaker` ∈ `Core | Warden | Archive | Directive` (a fixed set, each with a portrait from
  `Sprites/Avatars/` and a loc name), `next: "<shotId>"` (jump; default the next shot), and on a
  choice `next` as well. `when` stays for skipping shots by an earlier choice.
- triggers: everything WP1 has, plus `phase:<id>` (WP2) and `dialog:` on request through
  `cutscene action=play id=`.

**The speaker card** (`WardensCutsceneOverlay.cs`): bottom-left above the console (WP6), 480 px,
portrait 64 px, the speaker's name in cyan, the line, the choices as buttons, Continue; Escape closes
a skippable dialog; every line also lands in the Uplink as `<Speaker>:` so the conversation is in the
log afterwards.

**The first two dialogs:** `D.FirstBeaver.json` on `phase:beavers` (the Core speaks: what wakes with
her, water before food; one choice: *where should they live?* → `mark: home.pad | home.spring`, read
back by the Archive); `D02.Directive.json` replacing `TheSump`'s directive card. Level 05's fork
(*Monument / Theirs*) is a dialog in WP8; its answer lives in `story.json` (decided here: the story
record, not `campaign.json`, since `wardens-cutscenes.md` §7 already says so and the code reads it).

- [ ] **Step 1 [cloud]:** the checker (`speaker` in the set, `next` targets exist, no unreachable
  shot, a `dialog` scene has no camera keyframes), tests, the two dialog files and loc rows.
- [ ] **Step 2 [cloud, `written`]:** the parser, the runner's dialog path (no pause, no lock, the
  card), the overlay's card, the Uplink echo.
- [ ] **Step 3 [game]:** build; `cutscene play id=D.FirstBeaver`: the card, the choice, `story.json`
  carries the mark, the game never paused; then the real trigger on the day-31 save's first beaver via
  `campaign action=phase open=beavers`.

---

## WP6: The console — one panel, one voice [design → game]

**What there is** (the UI table in the HANDOVER entry): the Timberbot widget top-right (pill,
Launch/Stop, Edit, Minimize) with its settings modal and its action console bottom-left; the WARDENS
UPLINK bottom-left; the task panel top-left; the cutscene overlay; toasts. Two agent-status surfaces
that share no state; the same line sent to a toast *and* the Uplink by four different classes; two
Continue buttons for one level end.

**What there will be.** One **Wardens console**, left edge, dockable, three sections in one frame:
**Level** (title, `n/m`, the live tasks with their checks and Show buttons, the end card when done),
**Voice** (the dialog card's slot, WP5), **Uplink** (the log and the input). Its header carries the
ready-gate pip and toggle (the same call as `timberbot_ready`), the strategy mode (WP4), and a gear
that opens the Timberbot settings modal (ports, auth) for the few who need it. The Timberbot widget
and its action console do not mount on a Wardens map.

**The verbatim rule.** `TimberbotPanel` mounts itself from the copy's configurator. The change is
upstream: `timberbot/src/TimberbotPanel.cs` reads a new `settings.json` key `"widget": "full" |
"hidden"` (default `full`) and a public `OpenSettings()`; `timberbot/src` gets the change, the tests
run there, then the copy is re-copied whole. The Wardens' `settings.json` ships `"widget": "hidden"`.

**One voice.** `WardensVoice.Say(line, kind)` replaces the four toast+Uplink pairs (campaign,
tasks, chapters, cutscenes) and the MCP `say`'s optional toast: `kind` = `line` (Uplink only), `event`
(Uplink + toast), `speaker` (WP5's card + Uplink). One place decides what toasts.

- [ ] **Step 1 [design]:** `design/wardens-ui/Console.dc.html` (the `design` skill), three states:
  playing, dialog open, level complete. The author picks from it before any C#.
- [ ] **Step 2 [cloud, `written`]:** upstream `TimberbotPanel` change + tests; re-copy; `WardensConsole.cs`
  replacing `WardensTaskPanel` and `WardensChat`'s UI (the message store stays); `WardensVoice`.
- [ ] **Step 3 [game]:** build; a Wardens map shows one panel; a Lakes/Folktails map still shows the
  Timberbot widget; the ready toggle opens the gate (`GAME_NOT_READY` before, `200` after); the
  dialog card lands in the slot; a screenshot per state into `design/wardens-ui/`.

---

## WP7: Savegame starts — a level begins where the story is [game]

**Why.** Level 02's premise is a beaver who needs clean water; the level starts with 13 bots, 10
scrap and no beaver, because every level is a new game and `LevelStart`/`carry`
(`wardens-campaign-design.md` §3.3) were never built. A save carries everything vanilla persists: the
Core, the bots, a beaver, goods, the Uplink history, the task state. Shipping a *start save* per
level makes the start the author's exact intent and needs no new spawn API at runtime.

**What exists.** `SaveLevelStarter` (`WardensLevelTransition.cs:265-310`) loads
`<Saves>/Wardens Campaign/<MapName>.timber` through `ValidatingGameLoader` — dead code, and it
hardcodes `Saves/` where this machine (and the experimental branch) uses `ExperimentalSaves/`
(`TimberbotAutoLoad.cs:73-74` already reads `GameSaveRepository.DefaultSaveDirectory`).
`autoload.json` loads any save at the main menu. A loaded save posts no `NewGameInitializedEvent`, so
the bot swap and the opening do not run — the first is wanted (the save has its bots), the second must
be re-triggered.

**The experiment, in order:**

- [ ] **1. Fix the directory** (`SaveLevelStarter` uses `DefaultSaveDirectory`; the settlement is the
  level's title, the save is named `Start`). `WardensMapInstaller` learns `Saves/<level>/Start.timber`
  from the mod folder → `<saves>/<title>/Start.timber`, copied fresh at every main menu so the start
  is always pristine (a player's own saves in the same settlement are untouched). The level row gains
  `StartSave: true`; the strategy order puts the save first for such a level.
- [ ] **2. `campaign action=prepare_start bots=6 beavers=1 goods="ScrapMetal:10,Water:20"`** (dev
  only, main thread): on a fresh new game, destroys bots beyond `bots` (the swap's own
  `DestroyCharacter`), spawns `beavers` adults at the Core (`BeaverFactory`, the member the pod uses —
  read in the decompile first; if it needs a pod, the author builds one and waits five days, which is
  the fallback), gives `goods` into the Core's inventory (`GiveExistingIgnoringCapacity`, the call the
  starting scrap uses), and writes a `WardensLevelStart {Fresh: true}` singleton into the save. The
  author saves as `Start`, copies the file into `wardens/src/Saves/02/`.
- [ ] **3. The opening on a loaded start.** `WardensCutscenes` fires `level:<Id>` on `Load` when
  `WardensLevelStart.Fresh` is true, then clears it (saved false on the next save). `WardensLevelTasks`
  starts with `Done` empty because the start save has none. `campaign.json` records the level as
  entered, as today.
- [ ] **4. Verify:** Continue on level 01's card → `transition: starting level 02 … strategy: save`,
  the opening plays, tasks 0/6, `Player.log` has no `Can't validate` and no exception; a second load
  of that colony's autosave plays no opening; `validate.py` checks a `StartSave` level ships the file.
- [ ] **5. Record the costs.** A shipped save is bound to the game version (migration on load) and
  to the mod's template names: a renamed blueprint deletes the entity on load. The release checklist
  gains "regenerate every start save and autoload each once"; `package.py` ships them.

If step 2's spawn fails on the decompile read, the level-02 start still ships (13 bots, the Core,
goods, the Uplink's first lines) and the beaver arrives by the fallback; the experiment is not blocked.

---

## WP8: The missing levels [cloud → game, one level per session]

Each level is the same package shape: the spec and its `[contract]` (mapsmith), the tasks and their
sites, an opening of ≤ 5 shots and a scene per task, its dialogs, the loc rows, `shipped: true`, the
playtest row. New mapsmith ops and check types are named per level; each is a tested function.

| Level | Land (`map-set.md` §2) | New mapsmith | Tasks (the win) | New check types | Story assets |
|---|---|---|---|---|---|
| **03 The Pods** | a lake with two badwater inlets, a land-disconnected green island of 40–80 tiles, underwater ruins | `lake`, `island`, contract `land_disconnected`, `underwater_ruins` (footprint-aware) | bridge or ferry to the island (Cable Bridge), Water Pump + Cistern, Field, 3 pods, 20 beavers, the island's contamination at zero for a cycle | `contamination_zero {box, cycles}`, `population {beavers}` | opening 5, task scenes, dialog `D03.Island` (the choice: pods on the island or the shore → mark) |
| **10 Home** | level 01's spec with `[heal]` overrides: contamination zero, the badwater source a water source, trees grown | `heal` block on an existing spec (no new terrain op) | the epilogue: the Ledger read back (dialog), the last Post deconstructed or 10 days | `days {n}`, `demolished {template}` | the reading `Wardens.Reading.10.*` as a dialog; no tasks scene |
| **05 The Archive** | 128², fertile, a human town on a street grid, contamination zero | `town` (ruins on a grid), size 128 (`LAYERS` check) | Data quota (`stock DataCore 50`), the reading, the fork *Monument / Theirs* | `stock` suffices; `choice {key, is}` for the fork's consequence | `Wardens.Reading.05.*` (6 cards, drafted in `wardens-campaign-story.md`), dialog `D05.Fork` |
| **09 The Ark** | 128², dense ruins (scrap ≥ wonder cost + 50 %), ≥ 6 sub-surface badwater sources, a flat Ark footprint | `subsurface_sources`, `ruin_field` density, `flat_pad {size}` | barriers hold, Data feeds the Ark, the wonder complete | `wonder` (`GameWonderCompletion`) | the Ark ← `EarthRepopulator` re-spec (WP2 `city`), opening 5, `D09.Finished` |

Order: **03, 10, 05, 09**. 03 is the story's next beat and has the one new primitive (the island)
that 07 and 08 need too; 10 is the cheapest map and closes the loop so the campaign can be played
start to end early; 05 and 09 carry the readings and the wonder. The full ten-level arc's 04, 06,
07, 08 stay out of the table until these four are `verified`.

---

## WP9: Close the iteration [cloud]

- [ ] `bump_version.py --minor` → 0.5.0; the CHANGELOG entry lists WP0–WP8 by outcome and names what
  was seen in the game and on which build.
- [ ] `AGENTS.md` "The Wardens: state" rewritten from the playtest rows.
- [ ] The five documents stamped with `doc-source`/`doc-hash` (`check_doc_drift.py --update`), after
  a read against `WARDEN.md`; the checker added to the global constraints.
- [ ] A HANDOVER entry; `package.py` writes `dist/Wardens-v0.5.0.zip` with the start saves in it.

## 2. Out of scope

The full arc's levels 04, 06, 07, 08; custom 3D art (the Ark reuses the vanilla wonder); the Leaf
Coats port (`Buildings.WardensPort`, bundle-bound); frames on the Timberbot WebSocket; the
`Continue campaign` main-menu button (WP7's start saves make the New Game screen's map list enough
for now); German localization; the in-game Claude client (WP4c) unless the author asks.

## 3. The next session (the game machine, the author present)

In this order, each reported in the state words before the next starts:

1. **WP0 item 1** (20 min): the fresh level 02 on 0.4.25 — water at tick 0, `TheSump` to the end,
   screenshots. It closes the previous entry and is the last look at the long opening.
2. **WP1** (the session's core): checker and data first (`checked`), then the C#, one build with the
   game closed, then the day-31 save by `autoload.json` and a fresh level 01 by the handoff.
   Acceptance is §WP1 step 4. The `cutscene-director` agent frames `T01.*` in the running game.
3. **WP2** (if WP1 is `verified` by mid-session): the generator batch and the phase gate; the
   day-31 save is the test bed (a beaver lives there).
4. **WP7 step 1–2** (if time): the directory fix and `prepare_start`; the author bakes level 02's
   start save. Its verification is the session after.

In parallel, from a cloud container: **WP3** (Python only), the specs and contracts of **WP8 level
03** (`island`, `land_disconnected`), and the checker halves of WP1 and WP5. Each its own branch and
pull request; nothing of theirs is built until the game machine picks it up.

## 4. Risks

| Risk | Sign | What to do |
|---|---|---|
| Several tasks live at once confuses the panel and the Warden | two `task:` scenes queue back to back on a fresh start | at most two live tasks per level in the data (`check_level_tasks.py` warns above two); the queue plays one scene, drops the second's if the first task is done by then |
| A phase padlock's tooltip shows "999999 Science" | seen on the bar | the lock value becomes a loc'd label if `ToolButton` exposes one (read the decompile); otherwise the phase's Uplink line names what is coming |
| The pod's `BeaverFactory` needs a pod entity to spawn | `prepare_start` throws or spawns nothing | the fallback in WP7 step 2: build a pod, wait five days, save; the start save is still consistent |
| A shipped save dies on a game update or a blueprint rename | `Can't validate` lines on a start | the release checklist's regenerate-and-autoload step; `validate.py` fails a `StartSave` level whose save is older than its map |
| The runner places where the human wanted something else | the author overrides a step in play | `assist` is the default; `auto` is the harness only; a step's site is shown before it runs |
| WP1 removes chapters the five documents still describe | `check_doc_drift.py` clean while `WARDEN.md` names chapters | the five change in WP1's commit; the drift stamps come in WP9 after a read |
| The Iron Teeth food chain needs goods the Wardens do not make | a Field with no seed crop, a Bunk with no wood | the `beavers` batch ships only buildings whose inputs exist (Water, Kohlrabi, Logs from the Lumberjack Flag); `validate.py` checks every recipe's inputs are makeable |
