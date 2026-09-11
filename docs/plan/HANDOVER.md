# Handover: the Wardens, session to session

This file is the first thing to read when you continue the Wardens work. It is a stack: the newest
entry is at the top, and every session that ends adds one using the template at the bottom. An entry
says who wrote it, what they did and verified, where the mod stands with sources, what to do next and
in what order, and what they left open on purpose. The plan an entry points to says *how*.

The rest of the map: `AGENTS.md` (conventions, the state section), `wardens/README.md` (what the mod
is and how each part works), `wardens/CHANGELOG.md` (what each version added), `wardens/playtest/PLAYTEST.md`
(the in-game checklist, the MCP tool table, the findings), `design/` (why things are the way they are).

---

## 2026-09-11, evening: planning iteration 05, a consistent story (game machine, no code changed)

**Plan:** [`iteration-05-consistent-story.md`](iteration-05-consistent-story.md). **Goal:** turn the
author's eight asks of this evening (steps with per-task scenes, the Iron Teeth set with the first
beaver, pregenerated sites, an in-game strategy runner, dialogs, one console, savegame starts, the
missing levels) into packages with acceptance types, and name what the next session does first.
**Status: the plan is `written`; the offline checks on `main` are `checked` (below). Nothing was built,
no version bumped, nothing seen in the game this session.**

### What I did

Game machine (Windows 10, Timberborn 1.1.2.4 at `F:\Steam`, .NET SDK now 10.0.401). Local `main` was
36 commits behind `origin/main`; fast-forwarded to `57e2da5` (PR #24, 0.4.25) before reading anything.
Three subagent digests over the current tree, relayed into the plan's §0 table: the task/cutscene/
campaign C#, the faction/bots/UI/save paths, the design documents and every 2026-09-11 playtest finding.
Read myself: the six newest entries here, the iteration-04 plan, `first-light-opening.md`,
`WardensLevelTasks.cs`, both task files, PLAYTEST.md's 2026-09-11 sections, the level table, the
`warden-play` act lists. Diffed the game's `Blueprints.zip` against the faction's collection: 159 Iron
Teeth buildings, 27 on the Wardens bar (the pool WP2 draws from). Wrote the plan and this entry.

### Evidence

| Command | Result line |
|---|---|
| `git pull --ff-only origin main` | `57e2da5 Merge pull request #24 from netzkontrast/feat/first-light-opening` |
| `uv run --project python --extra dev pytest -q wardens/tools .claude/skills` | `183 passed in 9.92s` |
| `python wardens/tools/check_cutscenes.py wardens/src` | `cutscenes: 10 (…)`, `problems: none` |
| `python wardens/tools/check_level_tasks.py wardens/src` | `task files: 2`, `problems: none` |
| `python wardens/tools/validate.py wardens/src` | `templates: 88 (+9 aliases), goods: 63 … cutscenes: 10`, `problems: none` |
| `python wardens/tools/mapsmith levels --verify` | `levels: consistent with WardensCampaign.cs` |
| `check_doc_drift.py` over the three mirrors | `problems: none` (still `UNMARKED`) |
| `dotnet --version` | `10.0.401` |
| `dotnet build` / `dotnet test` | **not run** — no code changed this session |

### Publish check

Branch `plan/iteration-05-consistent-story`; `git ls-remote origin plan/iteration-05-consistent-story` →
`67ce17be67707258cc2ce2c5cb9aafaabd1a1998` (the plan and this entry; the commit that fills in this line
follows it on the same branch).

### Disposition of the entry below

- Start level 02 fresh on 0.4.25, watch `TheSump` to the end, screenshot it: **deferred** to the next
  session's first twenty minutes (plan §3 item 1, WP0 item 1) — the game was not started this session.
- "Next package: decide how level 02 checks held water": **blocked (question)**, now WP0 item 2 with a
  recommendation (a `water_depth` check used by Keep it clean and Hold the gorge, the Gorge box at x ≥ 57).
- "Does the river back up behind the three Levees?" and "task text or check?": **still open**, inside WP0 item 2.

### What I found (all read, none reproduced in the game)

1. **The chapter scenes cannot fire the way the author plays.** `WardensChapters.cs:136,158`: with the
   tutorial off, `_openAll` marks every chapter opened on the first, silent poll, so `Badwater` … `Green`
   and `LevelEnd` never play; on level 02 they would hang on level 01's tutorial ids anyway. *Consequence:*
   the five chapter scenes and the end card are dead content on every real run. *Remedy:* WP1 retires the
   table; scenes attach to tasks.
2. **`SaveLevelStarter` looks in the wrong folder.** `WardensLevelTransition.cs:306` hardcodes `Saves/`;
   this machine saves to `ExperimentalSaves/`, which `TimberbotAutoLoad.cs:73-74` already handles through
   `GameSaveRepository.DefaultSaveDirectory`. *Consequence:* a shipped save could never be found.
   *Remedy:* WP7 step 1.
3. **`dotnet test wardens/test` can run here now.** `dotnet --version` prints 10.0.401; `AGENTS.md`'s
   Quick Reference and my own notes still say SDK 8 cannot run the net10 test projects. *Remedy:* run it
   at the next build; the sentence in `AGENTS.md` is corrected when a session has the result line.
4. **The tool surface is nineteen tools, not six.** `WardensMcpTools.Build()` registers 19; the
   `driving-iterations` skill says "The tool surface is six tools" and the five-document rule counts on it.
   *Consequence:* a stale claim in the discipline itself. *Remedy:* a one-line correction in the skill, its
   own small commit (a changed claim is its own package by the skill's edit tiers).
5. **Leftovers to sweep in the packages that touch them:** `bot_workforce.py:33-34` still defines
   `CHAPTER_LOCK = 999999` "equal to `gen_buildings.CHAPTER_LOCK`", which no longer exists (WP2 step 1
   reuses the value on purpose and fixes the comment); level 09's row is `MapName "Wardens 09 The City"`
   with title *The Ark* (WP8); two `Ladder.*.blueprint.json` files from the Leaf Coats import sit in no
   collection.
6. **Both openings were cut short by the humans who saw them** (Continue at the directive, Skip at shot
   4 of 11; shots 5–11 of `TheSump` never seen). Nobody wrote "too long"; the behaviour did. Recorded here
   as the reason WP1 cuts openings to five shots, and WP0 item 1 is the last full viewing.

### Where the mod stands

| Fact | Source |
|---|---|
| 0.4.25 on `main`; levels 01 and 02 shipped, both played to their end on 2026-09-11 | the two entries below |
| Level 02's water at tick 0 and Salvage 20 are `built` (0.4.25), not seen | the entry below |
| The chapter table gates nothing and fires nothing with the tutorial off | finding 1 |
| No level starts from a save; every level starts with 13 bots, 10 scrap, no beaver | `WardensStartingPopulation.cs`, `WardensLevelTransition.cs` |
| 19 MCP tools; no suggestion, strategy or dialog system exists; one choice card (`LevelEnd.end`) | `WardensMcpTools.cs`, the digests |
| Levels 03, 05, 09, 10: rows only | `WardensCampaign.cs:207-210`, `levels.toml` |

### What you should do, in this order

**Game machine, next session (plan §3):** 1. WP0 item 1, the fresh level 02 on 0.4.25 (20 min).
2. WP1, the task graph and per-task scenes, through its step 4 on a fresh level 01 and the author's
day-31 save. 3. WP2 if WP1 is `verified` by mid-session. 4. WP7 steps 1–2 if time.

**Cloud container, any time:** WP3 (mapsmith `sites`, Python only), WP8 level 03's spec and contract
(`island`, `land_disconnected`), the checker halves of WP1 and WP5. Each its own branch and PR.

### How you know it is done

The plan's §1 table has a state word beside every package; the next entry above this one reports WP0
item 1 and WP1 in the state words with `Player.log` lines.

### Open questions I could not answer

Four shape the packages and were put to the author as one card at the end of this session (answers, when
given, are appended under this list):

**1. Openings and chapters (WP1).**

| Option | Cost | Risk | |
|---|---|---|---|
| Cut openings to ≤ 5 shots, one 1–2-shot scene per task on going live, retire the chapter table and its five scenes | a day: C#, checkers, two openings re-cut, ~10 task scenes framed by the director | the act list, `chapter` tool and frames change: five documents in one commit | **recommended** |
| Same, but keep the chapter table beside the tasks | half a day less | two progression systems, one of them dead with the tutorial off | |
| Keep the openings whole, add task scenes only | the scenes' cost | the openings stay long; the same land is shown twice | |

**2. How the new buildings arrive (WP2).**

| Option | Cost | Risk | |
|---|---|---|---|
| Padlocked from the start, open on the first beaver (the mechanism seen on 0.4.1) | the generator batch + `WardensPhases.cs` | the padlock tooltip says "999999 Science" | **recommended** |
| Hidden until the first beaver, then appear with a toast | + a spike: no tool-button hide call has been used here | an unverified UI API on the critical path | |

**3. What the harness is (WP4).**

| Option | Cost | Risk | |
|---|---|---|---|
| A C# strategy runner over WP3's sites, `assist` and `auto` modes, a run record; Claude steers | two packages | the runner places where the human wanted something else (assist is the default) | **recommended first** |
| An in-game Claude API client that runs the loop itself | the runner anyway, plus a key in `settings.json` and a C# tool loop | cost per playtest, a key on disk | |
| Both, runner first | the sum | — | |

**4. The first big package of the next session, after the 0.4.25 check.**

| Option | Why | |
|---|---|---|
| WP1 steps and scenes | the biggest story lever; verifiable on level 01 the same session | **recommended** |
| WP7 savegame starts | the author's experiment; needs the author at the keyboard to bake the save | |
| WP8 level 03 | the story's next beat; a session of Python before anything is seen | |
| WP2 buildings | the day-31 save is a ready test bed | |

**Answered by the author, 2026-09-11, end of the session:** all four recommended rows — WP1 cuts the
openings, gives each task its scene and retires the chapter table; WP2 padlocks the new buildings from
the start and opens them on the first beaver; WP4 is the C# strategy runner with Claude steering (the
in-game API client stays WP4c, unasked); the next session opens with WP1 after the 0.4.25 check. The
plan stands as written; no package needs reshaping.

### What I deliberately did not do

I did not start the game or build: a planning session with the state of the tree unknown until the
fast-forward is not the session to deploy in. I did not edit `AGENTS.md`'s SDK sentence (finding 3) or the
skill's "six tools" (finding 4): each is a changed claim and gets its own commit. I did not resolve the
level-02 water checks, the bot count or Science: they are the author's calls and the plan carries a
recommendation for each. I did not re-run the three subagents' claims in the game.

---

## 2026-09-11: level 02 reworked, given an opening and six tasks, and played to its end (game machine)

**Plan:** the author asked for the level 02 cutscene and a map overhaul, then for six tasks, better
buildings and the Iron Teeth water-control set. They chose Wasserkontrolle and Brücken, and "Das
Wasser-Rätsel" for the tasks. In chat they asked Claude to finish the level. **Status:** `verified` on
0.4.24: all six tasks done on day 7 by Claude over MCP and the author by hand. Level 02's water at tick 0
and Salvage at 20 are `built` in 0.4.25 but not yet seen in the game.

### What I did

1de5047: the level 02 rework (mapsmith watercourse fix and `check_watercourses`), `Cutscenes/TheSump.json`
(11 shots), `Levels/02.tasks.json`, the check types `built_in` and `clean_water`, and Levee, Floodgate,
Double Floodgate and Cable Bridge 2/3/4 re-specced by `gen_buildings.py`. f3d39ef: `[water] fill` in the
level 02 spec, Salvage 40 → 20. 5d531dd: the PLAYTEST record, the warden-play level 02 task list,
design §2, CHANGELOG 0.4.25.

| Command | Result line |
|---|---|
| `pytest wardens/tools` | `177 passed` |
| `validate.py wardens/src` | `problems: none` |
| `mapsmith check wardens/maps/wardens-02-the-sump.map.toml` | `problems: none`; `single_gorge … one narrows at 62,48`; `never_touch … 0 stray contact(s)` |
| `check_level_tasks.py wardens/src` | `task files: 2`, `problems: none` |
| `dotnet build … -c Release` (game closed) | `Wardens version 0.4.24 -> 0.4.25`, `0 Warnung(en)`, `0 Fehler` |
| Player.log (0.4.24) | `handoff: starting level 02` (61) … `Gorge done (5/6)`, `Power done (6/6)`, `tasks: level 02 complete` (6939–6945); no exception |

### Publish check

`git ls-remote origin feat/first-light-opening` → `5d531dd4bcfccd19456e64b29b88afbd95e19fcd`; this entry
goes on top of it.

### Disposition of the entry below

- Level 02 tasks, opening, ending: **done** (1de5047; the ending is the task card).
- Build 0.4.23: **done** (0.4.24 and 0.4.25 built and deployed with the game closed).
- Skip: **done** for TheSump. The author pressed it at shot 4, and the game came back paused and unlocked.
- A fresh level 01 with no `Can't validate` line: **deferred** (no level 01 start this session; level 02's
  new game logged none).
- 13 → 19 Wardens: **blocked (question)**, still unread.

### What I found

Seven findings in `wardens/playtest/PLAYTEST.md`, "Level 02 played through". The ones that shape the next
package:

1. Keep it clean and Hold the gorge both pass without the water the level is about. The creek as it runs
   has 83 clean tiles against the 40 asked for. Three one-block Levees at x 56, one tile short of the high
   banks, count as the gorge held.
2. New Floodgates stand at 0.6–0.7, below the creek's surface, so nothing pools until they are raised.
3. The placement finder offers no Sludge Pump site. The path router puts Paths on the creek bed.

### What you should do, in this order

**Game machine:**

1. Start level 02 fresh on 0.4.25. Write `{"level":"02"}` to
   `Documents/Timberborn/Mods/Wardens/campaign.handoff.json` and start the game.
2. Watch TheSump to the end, all 11 shots, and check that the river and the creek are wet at tick 0.
3. Screenshot the scene with `scene-shots.ps1` while Timberborn is in the foreground.

**Next package (either machine):** decide how level 02 checks held water (findings 3 and 4). Options:

- A `water_level` check: the river's surface in the gorge box above a set height.
- A Keep it clean threshold measured on a pond behind raised gates.
- The Gorge box moved to x ≥ 57.

### How you know it is done

- A fresh level 02 shows water in the opening's first shot.
- On a replay, a Levee line at x 56 no longer completes the Gorge task.

### Open questions I could not answer

- Does the river actually back up behind the three Levees? The water level behind them was not measured.
- Should the Creek task tell the player to raise the gates, or should the check require water behind them?

### What I deliberately did not do

I did not press the level-end card: level 03 is not shipped, so it has no Continue. I did not change the
Clean or Gorge checks either. Both are proposals and need the author's call.

---

## 2026-09-11: level tasks and the level-end card that starts level 02 (game machine)

**Plan:** the author asked for clear tasks whose completion loads level 02, scouted over MCP; they chose
eight tasks to the first beaver and a card whose Continue loads the next level. **Status:** `built`
(0.4.22) and `verified` end to end on the author's colony: all 8 tasks, the level-end card, and its
Continue loading level 02 as a new Wardens game with no loading problem (addendum, 16:54: the first
beaver woke, `tasks: level 01 complete`, `transition: starting level 02 (The Sump) … as a new Wardens game`,
`campaign: level 02 … completed=[01]`, 0 `Can't validate` / exception lines).

### What I did

`Levels/01.tasks.json`, `WardensLevelTasks.cs` (checks, save, completion, next level),
`WardensTaskPanel.cs` (list and card), `check_level_tasks.py` with tests, MCP `campaign action=tasks`
and frame fields, level 02 marked shipped, the save auto-loader fixed for `ExperimentalSaves`.

| Command | Result line |
|---|---|
| `pytest wardens/tools .claude/skills` | `173 passed` |
| `validate.py wardens/src` | `problems: none` |
| `dotnet build … -c Release` (0.4.22) | `0 Warnung(en)`, `0 Fehler` |
| Player.log, autoload | `[Timberbot] auto-loading: First Light - 2026-09-11 16h27m, Day 2-14.autosave` |
| Player.log, tasks | `tasks: level 01, 8 task(s), 0 done` … `Reeds done (5/8)` … `Haul done (6/8)`, `Power done (7/8)` |

### What you should do, in this order

**Done:** the card and the transition (addendum above). **Next package:** level 02 has no tasks, no
opening and no ending; write `Levels/02.tasks.json` from its design (`wardens-campaign-map-set.md` §2, the
one dam decision) after a scouting pass over MCP. **Game machine:** close the game and build once (0.4.23: the panel under the goods bar, two task texts with the game's
building names). Still open from the entry below: a fresh level 01 with no `Can't validate` line, Skip.

### Open questions I could not answer

The colony went from 13 to 19 Wardens while the tasks ran, with no pod-born beaver; the source is not read.

---

## 2026-09-11: Claude played level 01 — Wardens walk on one level, and the map lost entities on load (game machine)

**Plan:** the author asked Claude to connect over MCP and playtest level 01 itself; the fixes follow the
author's choice "ruins on the pad". **Status:** mapsmith and the maps `checked`; 0.4.18 `built` and
`verified` for the walk fix, the tiles read, the speed tool and `at.grid`; the footprint fix `checked`,
not yet built (the game was running).

### What I did

Played the scripted opening from the end of the cutscene: two Charging Posts for the 10 starting scrap,
two Scavenger Flags, paths. Both flags said "Nothing to do in range" for two days; the colony sat at 0
scrap with 11 of 13 Wardens at 0 Energy. Read why (the nav mesh joins a tile only to same-height
neighbours), recovered with one Stairs, then fixed mapsmith's walk model and moved the opening's ruins
onto the pad. Deployed 0.4.18, started fresh: the new flags worked with no Stairs. That start showed a
Loading issues dialog: the game deletes two of the three river-head sources and all six UndergroundRuins
on every load, since the remake. Read why (3x3 and 5x5 footprints) and fixed mapsmith and the specs.

| Command | Result line |
|---|---|
| `pytest wardens/tools .claude/skills` | `158 passed` (6 new: on foot vs Stairs, `on_foot_scatter`, `on_foot_from`, the pad ruins, source spacing, footprint checks) |
| `mapsmith check` on all three specs | `problems: none` each; level 01 walk: `first light … on foot`, `near ruins … 1-2 Stairs` |
| `check_cutscenes.py wardens/src` | `problems: none` |
| `dotnet build … -c Release` (0.4.18, game closed) | `0 Warnung(en)`, `0 Fehler` |
| 0.4.18 start, `/api/tiles` Sump | `badwater: 1` (read 0 before) |
| 0.4.18 start, `speed value=1` | `{"was":0,"speed":1,"applied":true}` |
| 0.4.18 start, frame | `"at":{"x":19,"y":6,"z":59,"grid":{"x":19,"y":59,"z":6}}` |
| 0.4.18 start, flags at 17,49 and 18,49 | no alert; ruin 16,48 gone and 16,46 H2 → H1 by day 1 at 87 % |
| Player.log, 0.4.18 start | `Can't validate loaded BlockObject BadwaterSource(Clone) at (34, 1, 4) … Deleting it.` (and 35,1 and six UndergroundRuins) |

### Publish check

Branch `feat/first-light-opening`; `git ls-remote` after this entry's push is in the PR #22 head.

### What I found

The eight findings are in `wardens/playtest/PLAYTEST.md`, "Level 01 played by Claude over MCP". The two
that change how maps are made: Wardens walk on one level (a Stairs per level; mapsmith assumed one level
of climb), and big entities are validated block by block on load (a `BadwaterSource` is 3x3,
`UndergroundRuins` a 5x5 surface object). Open: idle Wardens at 0 Energy by day 2.8, Wardens at 0 Energy
still walking (the story says they stop), and `placement/find` missing the pump shelf.

### What you should do, in this order

Disposition of the previous entry: the Sump caption **done** (seen); Skip mid-opening **still open**
(the author pressed Continue both times); the director's zoom pass **done** (the scale is 1.3^zoom * 32,
no clamp; the Warden shot now looks at the Core's door, not yet judged). **Game machine:** close the game,
build once (0.4.19 deploys the footprint fix and the Head caption), start a fresh level 01, and read
Player.log for `Can't validate`: there must be none, and no Loading issues dialog. Then screenshot the
Ruins and Warden shots. **Balance pass:** decide what 0 Energy means before tuning Posts.

### How you know it is done

A fresh level 01 opens with no dialog, the first flags work without Stairs, and Player.log has no
`Can't validate` line.

### Open questions I could not answer

What the game does with a Warden at 0 Energy (the need specs and bot behaviours are not read).

### What I deliberately did not do

I did not restore the three river-head sources: one is what every playtest and the water fill ran on.
I did not place UndergroundRuins on flat ground for a later act; that needs a flat-5x5 rule.

---

## 2026-09-11: level 01 ships with its water (game machine)

**Plan:** package 10 of `docs/plan/first-light-opening.md`; the author chose "pre-fill the map" for the
dry river. **Status: `verified` (0.4.13).**

### What I did

Decoded the 1.1.2.4 water format from the decompile (`WaterColumnPackedListSerializer`,
`ColumnOutflowsPackedListSerializer`, `WaterMapLoader`, `WaterSimulationMigrator`) and checked it against
two autosaves: day 15 of the first land and day 3 of the remade one. Added `[water] fill` to mapsmith, and
a checker that rejects a column token the game's reader would misparse. Filled level 01 to the surface the
day-3 autosave settled at (4.17 → 4.2; spring 12.02 → 12.25). Deployed with the game closed, started a fresh
level 01, and read the result through the MCP server and DPI-aware screenshots.

| Command | Result line |
|---|---|
| `pytest wardens/tools/test_mapsmith.py` | `100 passed` (9 new: fill by level and by depth, refusals, five bad tokens) |
| `mapsmith build --level 01` | `problems: none`; 554 wet columns; Sump `1.2:1:0:3:1.2`, spring `0.25:0:0:12:0.25` |
| `dotnet build … -c Release` (0.4.13) | `0 Warnung(en)`, `0 Fehler`; the deployed map `cmp`-identical to the repo's |
| `/api/tiles` y 48, tick 1, paused | `water 1.2` on x 36–47 (the Sump and the channel) |
| Player.log | no exception; `cutscene FirstLight: start (trigger, 12 shots, 99 s)` … `finished at shot 12/12` |

### Publish check

Branch `feat/first-light-opening`; the `git ls-remote` line is in the commit that follows this entry's
push (see PR #22's head).

### What I found

1. **Every mapsmith map has its source strengths halved on load** (read). `WaterSimulationMigrator` migrates
   any file without its key and multiplies `SpecifiedStrength` by 0.5. The playtested strengths are the
   halved ones, so mapsmith deliberately does not write the key. Recorded in `timber-format.md`.
2. **The water arrives and settles flat** (seen). The game's own equilibrium on day 3 was one surface across
   the river, the channel and the Sump. A pre-fill at that level loads without a flood wave.
3. The caption args fix (0.4.11) is seen working: "The Core. 13 Wardens online, charged to full."

### What you should do, in this order

Disposition of the previous entry: "if 0.4.11 did not deploy" **done** (deployed as 0.4.12, the Core
caption seen); the Skip check **still open**; the director's second pass **still open**; the dry river
**done** (this entry). **Game machine:** close the game and build once more, so the new Sump caption
("badwater, 1.2 deep") deploys; press Skip mid-opening once. **Anyone designing levels 03, 04 or 08:**
`wardens-campaign-map-set.md` §5 is unblocked; take fill levels from an autosave, never guess them.

### How you know it is done

The Sump shot's caption matches the full basin; Skip leaves the game paused and unlocked.

### Open questions I could not answer

None new.

### What I deliberately did not do

I did not write `WaterSimulationMigrator` into maps (it would double every source against what was played).
I did not copy a save's water into the map: the fill is computed from the spec, so a terrain edit keeps working.

---

## 2026-09-11: the remade level 01 in the game, and its opening cutscene (game machine)

**Plan:** `docs/plan/first-light-opening.md` (the author's goal: the new map starts with an extensive
cutscene), after the level-01 remake of PR #21. **Status: the opening `verified` (0.4.10); two fixes from
its first run `built` (0.4.11), deployed when the game next closes. The remade land `verified` for its
boot and its day-1 Sump.**

### What I did

Game machine, Timberborn 1.1.2.4 at `F:\Steam`, two fresh level-01 starts by the handoff, the author's
tutorial setting off. Read through the Wardens MCP server (`wardens_status`, `/api/tiles`, `cutscene
status`, `frame`) and the log; screenshots of the opening every 4 s.

| Command | Result line |
|---|---|
| `dotnet build … -c Release` (0.4.10, deployed with the game closed) | `0 Warnung(en)`, `0 Fehler` |
| the same with `-p:ModDir=<scratch>` (0.4.11, the game running) | `0 Warnung(en)`, `0 Fehler` |
| `uv run --project python --extra dev pytest wardens/tools .claude/skills -q` | `139 passed in 8.20s` |
| `python wardens/tools/check_cutscenes.py wardens/src` | `cutscenes: 9 (…, FirstLight, …)`, `problems: none` |
| `python wardens/tools/validate.py wardens/src` | only the known `level 02 … shipped=false` |
| `python wardens/tools/gen_tutorial.py`, then `git diff enUS.csv` | only the 12 new rows and the Wake line |
| Player.log, 0.4.10 start | `level triggers=True (… tutorial=False)`, `cutscene FirstLight: queued by level:01`, `start (trigger, 12 shots, 99 s)`, `finished at shot 12/12` |

### Publish check

Branch `feat/first-light-opening`, stacked on `feat/level-01-remake` (PR #21);
`git ls-remote origin feat/first-light-opening` -> `4943574345af67b1e84846369fee58a6112c859d` (code and
docs; this entry is the next commit on it).

### What I found

The six findings, each with symptom, source, consequence and remedy, are in `PLAYTEST.md`, "The remade
level 01 and its opening". In short: captions with `args` printed `System.Object[]` in every scene that
has them (fixed, 0.4.11); the river is dry while the opening shows it (the author's decision, below);
`cutscene_played` ignored a level opening (fixed, 0.4.11); the badtide replays finish vanilla's
first-badtide tutorial on day 1 (deferred, predates this work); the Warden close-up does not get closer
(deferred to the next director pass); the speed once would not leave 0 (not found). Verified along the
way: the 0.4.9 boot line (13 charged, 10 scrap), the land's heights against the spec, and the Sump at
0.5 on every tile by day 1, 41 %.

### Where the mod stands

| Fact | Source |
|---|---|
| A new game on level 01 plays `FirstLight` instead of the Cold Boot, tutorial on or off | the log lines above |
| `level:<Id>` is a trigger; the Cold Boot still plays on every other map | `WardensCutscenes.OnNewGameInitialized` |
| `.claude/agents/cutscene-director.md` tunes scenes live over the MCP server | the file; the todo |
| 0.4.11 is staged, not deployed; a watcher deploys it when Timberborn exits | this session |

### What you should do, in this order

Disposition of the previous entry's items: the in-game `campaign next` run **done** in the earlier Gate
session (PLAYTEST.md); WP4, the level-01 proof run, **still open**; "five bots or thirteen" **moot** for the
Wake card (it no longer states a count) and still open for the design.

**Game machine:** if 0.4.11 did not deploy, close the game and run the build. Then start level 01 by the
handoff and check the Core shot reads "The Core. 13 Wardens online, charged to full.", and press Skip once
mid-scene: the game must stay paused and unlocked. Run the `cutscene-director` agent for its second pass
(the Warden close-up's zoom limit), and settle the dry river as the author decides.

### How you know it is done

The Core caption shows the number; `wardens_status.cutscene_played` is true after the opening; the
todo's state column has no open row but package 10.

### Open questions I could not answer

The dry river (package 10 of the todo), put to the author.

### What I deliberately did not do

I did not turn the author's tutorial setting on for campaign starts; it is theirs. I did not press
Continue on the directive card or touch the game while the author played. I did not deploy 0.4.11 under
the running game.

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

## 2026-09-11: every building on the bar from the first frame of every level (out of plan order)

**Plan:** none — the author asked directly: "include all buildings for all levels right from the start".
That retires the chapter gate of `wardens-chapter-1-plan.md` §4 (the padlocks), taken ahead of WP1/WP2.
**Goal:** on every map the Wardens play, the whole building bar is buildable from the first frame, and the
story (chapters, toasts, cutscenes) still advances with the tutorial line.

**Status: the data, the generators and the checks are `checked` (cloud). The C# is `written` — this
container has no .NET SDK and no game DLLs, so nothing in `wardens/src` has been compiled.**

### What I did

Cloud container: Linux, Python 3.11, `uv`, no `dotnet`, no game DLLs, no Timberborn. Read `AGENTS.md`,
`wardens/README.md`, `wardens/CHANGELOG.md`, the two newest entries here, the iteration-04 plan,
`design/wardens-chapter-1-plan.md`, `design/wardens-campaign-design.md`, `design/leafcoats-port-plan.md`,
`wardens/playtest/PLAYTEST.md`, the five documents of the agent contract, and the sources
`WardensChapters.cs`, `WardensFrames.cs`, `WardensCutscenes.cs`, `WardensMcpTools.cs`, `WardensMcpServer.cs`,
`WardensCampaign.cs`, `gen_buildings.py`, `gen_tutorial.py`, `gen_port.py`, `validate.py`, `check_cutscenes.py`.

Decisions taken without the author (the session was autonomous), each recorded in the changelog:

1. **The data is the gate, and there is no gate.** Every building in `Buildings.Wardens` ships with
   `ScienceCost: 0`: the nine chapter padlocks (999999) and the science prices of Planter Rig (60),
   Stairs (70) and Platform (100). Mirrored in `gen_buildings.py` (`CHAPTER_LOCK` is gone).
2. **Chapters stay as story beats.** `WardensChapters.cs` still announces a chapter when its tutorial
   finishes (toast, Uplink line, `ChapterOpened` for the cutscene) but unlocks nothing; `Opened()` reads
   the finished-tutorial set, which also settles the playtest finding "every chapter complete on a fresh
   save". One safety net at load unlocks anything still carrying a cost and logs it (`unlocked_at_load`).
   `chapterGating` is retired (a leftover key logs one line).
3. **The tutorial line follows.** Reforestation goes straight to `build(Planter)` (stage
   `Wardens.Reforestation.BuildPlanter`), the Science card stops promising unlocks, Vertical architecture
   requires `Wardens.Wellbeing` alone (stairs are free, so `StairsUnlockedTrigger` has nothing to see; the
   `Stairs.Folktails` alias stays because the vanilla singleton still resolves the name at load).
4. **The Leaf Coats port stays out.** All 44 `Buildings.WardensPort` blueprints reference the local-only
   bundle (`*.LeafCoats.Model`), so wiring the collection into the faction would crash a shipped mod at
   load. Its science costs are untouched; `gen_port.py`'s docstring says what wiring it in now requires.
5. **`validate.py` enforces the rule** (no science cost in any collection the faction lists; the chapter
   table through two pure helpers), and `test_validate.py` runs the same rules against the source tree
   without the game's files, so CI guards it.

Changed, one branch, one pull request: 12 blueprints, `gen_buildings.py`, `gen_tutorial.py` (and its
regenerated stages and loc rows: the csv now carries the generator's row order), `enUS.csv`,
`settings.json`, `manifest.json`, `WardensChapters.cs` (rewritten), `WardensMcpServer.cs`,
`WardensFrames.cs`, `WardensMcpTools.cs`, `WardensConfigurator.cs`, `validate.py`, `test_validate.py`
(new), `gen_port.py` (docstring), `README.md` (root and wardens), `AGENTS.md`, `CHANGELOG.md`,
`PLAYTEST.md`, dated status notes in five design documents and two passages of the iteration-04 plan.

### Evidence

| Command | Result line |
|---|---|
| `uv run --project python --extra dev pytest -q wardens/tools .claude/skills/driving-iterations/scripts` | `121 passed in 3.57s` |
| `python wardens/tools/check_cutscenes.py wardens/src` | `problems: none` |
| `python wardens/tools/mapsmith check wardens/maps/wardens-wasteland.map.toml` | `problems: none` |
| `python wardens/tools/mapsmith check --level 02` | `problems: none` |
| `python wardens/tools/mapsmith levels --verify` | `levels: consistent with WardensCampaign.cs` |
| `python .claude/skills/driving-iterations/scripts/check_doc_drift.py --root . wardens/WARDEN.md .claude/skills/warden-play/SKILL.md design/wardens-play.md` | three `UNMARKED`, `problems: none` (the five documents did not change) |
| `python wardens/tools/gen_tutorial.py` (twice) | `tutorials: 18, stages: 40, loc rows: 63 replaced 63`; the second run changed nothing |
| `ruff check --config python/pyproject.toml wardens/tools/validate.py wardens/tools/test_validate.py` | no findings (the 16 in the two generators predate this entry) |
| `python wardens/tools/validate.py wardens/src` | **not run here** — needs the game's `Blueprints.zip` |
| `dotnet build wardens/src/Wardens.csproj -c Release` | **not run here** — no .NET SDK in this container |

### Publish check

Branch `claude/mod-building-all-levels-2q3noz`, PR #16 open against main.
`git ls-remote origin claude/mod-building-all-levels-2q3noz` -> `33989315566cd2efc51412c985ad20c7723284e5`
(the change itself; the commit carrying this entry follows it on the same branch).

### What I found

1. **`chapter status` listed every chapter complete on a fresh save because the check read the unlock
   state** (read: `WardensChapters.cs` before this change, `IsComplete` was true when every template was
   unlocked, and `_unlockAll` unlocked everything when the tutorial was off or `chapterGating` false).
   *Symptom:* PLAYTEST.md's finding of 2026-09-10. *Source:* read, not reproduced. *Consequence:* the agent's
   `chapter.next` attention item was empty on such a save. *Remedy:* `Opened()` reads the finished-tutorial
   set; the frames and `wardens_status` use it. WP6 item 2 of the iteration-04 plan is marked settled.
2. **Three tutorial-line steps assumed a lock** (read, `gen_tutorial.py`): `AccumulateScienceForBuildingStepSpec`
   and `UnlockBuildingTutorialStepSpec` for the Planter Rig, and `StairsUnlockedTrigger` as a requirement of
   Vertical architecture. *Consequence:* with the rig free the two steps would auto-complete at best; with
   stairs free the trigger never fires if it listens for an unlock event, and the tutorial never starts.
   *Remedy:* the steps are gone and the requirement is `Wardens.Wellbeing` alone. Whether the vanilla trigger
   also checks the initial state was not read (no decompile here); the remedy does not depend on it.
3. **The port cannot be included** (read): all 44 blueprints reference `*.LeafCoats.Model` assets from the
   git-ignored bundle. *Consequence:* a load crash if wired in without the local copies. *Remedy:* left out,
   recorded in the changelog and the README; `gen_port.py`'s docstring names the extra step (ScienceCost 0)
   for whoever wires it in locally.
4. **`enUS.csv` was not in the generator's row order** (reproduced: `gen_tutorial.py` moved 34 rows). The
   chapter and cutscene rows had been appended after the tutorial rows by a later `gen_buildings.py` run.
   *Consequence:* a larger diff than the edit, nothing else. *Remedy:* none needed; the file now matches
   what the generator writes.

### Where the mod stands

| Fact | Source |
|---|---|
| No building in `Buildings.Wardens` carries a science cost; the bar is open from the first frame | the 20 blueprints; `test_validate.py::test_shipped_bar_is_free_from_the_start` |
| The chapters announce on tutorial completion and unlock nothing; `complete` means the tutorial finished | `WardensChapters.cs` |
| `chapterGating` no longer exists; `settings.json` ships without it | `WardensMcpServer.cs`, `wardens/src/settings.json` |
| The Leaf Coats port is still not in the faction and cannot ship | `Faction.Wardens.blueprint.json`, `gen_port.py` docstring |
| No C# in this entry is past `written` | no .NET SDK here |
| Everything in the previous entry still stands (level 02's row unfinished, the transition unverified) | that entry |

### What you should do, in this order

Disposition of the previous entry's items: WP1, WP2, WP5, WP8 (cloud) **deferred** — the author asked for
the open bar instead, and they are unchanged and still small; WP3, WP4, WP6 (game) **blocked** on the game
machine as before, with WP4 step 6 and WP6 item 2 rewritten for the open bar; "five bots or thirteen"
**still open**; stamping the mirrors **deferred** to WP5 as before; the level-02 chapter-table question
**still open**, and smaller now: a chapter table for level 02 is a list of story beats, not a gate.

**If you are at the game machine:** read this entry, then run
`dotnet build wardens/src/Wardens.csproj -c Release` (the rewritten `WardensChapters.cs` uses only calls the
old file or the Timberbot copy used: `UnlockIgnoringCost`, `UnlockInternal`, `_finishedTutorials`,
`ToolButtons`, `BuildingSpec.ScienceCost`), then `python wardens/tools/validate.py` (expect
`science-priced: 0 (must be 0)` and `problems: none`). Start a new game on `Wardens 01 First Light`, tutorial
on, and check: no padlock on the bar; `chapter status` shows `complete: []`, `next: Badwater`,
`unlocked_at_load: []`; finish the Scrap tutorial (`tutorial next` is fine) and see the "Chapter 2:
Badwater." toast within a second; grep `Player.log` for `[Wardens] chapters:` and paste the `bar checked`
line into `wardens/playtest/PLAYTEST.md`. Then continue with WP3 and WP4 as the previous entry says.

**If you are in a cloud container:** WP1 and WP2 from the iteration-04 plan, untouched and still small, each
its own branch and PR in the state words.

### How you know it is done

The `dotnet build` result line and the `[Wardens] chapters: bar checked, N tool buttons, 0 unlocked at load`
log line are in a HANDOVER entry; PLAYTEST.md's chapter bullet has been walked once; `AGENTS.md`'s state
section rewrites the 2026-09-11 update from evidence.

### Open questions I could not answer

**Should Science still exist as a currency?** With nothing to unlock, the Cruncher's Science Points recipe
is a score, not a resource, and the Signal chapter's line "Science, or Data Cores for the Archive" is a
choice between a number and a good.

| Option | Cost | Risk | |
|---|---|---|---|
| Leave it: Science is a measure, Data Cores are the economy (what this entry ships) | none | the Signal card's choice feels empty to a player who reads it | **recommended until the proof run** |
| Drop the Science recipe from the Cruncher and reword Signal | an hour: `gen_buildings.py`, the Signal captions, the act list in the `warden-play` skill | the Cruncher's power tension (Core 150 vs. 120) loses its "think about what" framing | |
| Give Science a use again (a late-game wonder, the Ark) | a design pass | out of scope for Act I | |

What settles it: the author's reading of the Signal chapter on the proof run (WP4).

### What I deliberately did not do

I did not wire `Buildings.WardensPort` into the faction (finding 3). I did not rename the
`Wardens.Chapter.<Id>.Unlocked` loc rows: the key is historical, the text is new, and a rename touches the
generator, the C#, the validator and the csv for no behaviour. I did not touch the five documents of the
agent contract: none of their claims changed (the act list still says a chapter "opens" when its tutorial
finishes, which is still true). I did not change any cutscene caption. I did not edit older HANDOVER
entries, and the plan only where it described the padlocks.

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
