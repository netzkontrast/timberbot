# Playtesting the Wardens with an agent

Everything an agent needs is inside the mod: the Timberbot API (HTTP/WS) and an MCP server
that Claude Code connects to directly.

## One-time setup

1. Build + deploy: `dotnet build wardens/src/Wardens.csproj -c Release` (or install a packaged build:
   `python wardens/tools/package.py` after the build writes `dist/Wardens-v<version>.zip`, whose `README.txt`
   has the two copy steps).
2. In Timberborn's Mod Manager enable **The Wardens** and **disable Timberbot API** (the same
   code is compiled into the Wardens; two copies fight over port 8085).
3. New Game → faction **The Wardens**, tutorial toggle on → map **[Custom] Wardens 01 First Light** (the
   build installs it to `Documents/Timberborn/Maps`; any map works if it is missing). The Cold Boot
   cutscene plays (22 s, paused: letterbox, three captions, one orbit around the Core, Skip at the top
   right); the tutorial cards are bottom-right throughout and the game stays paused afterwards.
4. Save as settlement `Wardens`, save `smoke` so `tbot launch --settlement=Wardens --save=smoke` can
   reload it.

Claude Code: the repo's `.mcp.json` registers `wardens` (http://127.0.0.1:8090/mcp). Approve it once
when Claude Code asks; it connects whenever a game is loaded (the server starts with the game
context and stops on exit to the main menu).

## Every run

```bash
python wardens/playtest/mcp_smoke.py --say "hello from the smoke test"   # MCP: initialize, tools, status, chat
uv run --project python wardens/playtest/smoke.py                       # Timberbot API: faction/bots/needs
```

The Timberbot API refuses reads/writes until the ready gate is open: press **Launch** in the widget,
or call the MCP tool `timberbot_ready`.

To skip the menu and open straight onto a level, write the request before launching (the menu
consumes it once; the vanilla **Mods** dialog at startup still needs its OK first):

```bash
echo '{ "level": "01" }' > ~/Documents/Timberborn/Mods/Wardens/campaign.handoff.json
```

## MCP tools

| Tool | Thread | What |
|---|---|---|
| `wardens_status` | main | faction, speed, bots/beavers + avg Energy, tutorial + chapter state, pointers, camera, ready gate |
| `tutorial` | main | `status`, or `next` to force the next stage of a tutorial id |
| `chapter` | main | `status`: every story chapter with its tutorial, whether the story has reached it and the buildings it is about (nothing is locked; `unlocked_at_load` is empty when the data is right); `unlock` announces `chapter_id` now |
| `frame` | listener | long-poll for the next sensor frame: every `every_ticks` game ticks or on an event (chat, day, building, chapter, birth, alert, selection); carries `attention` (where to look) |
| `manual` | listener | the Warden's playbook, `docs/WARDEN.md` from the mod folder |
| `point` / `unpoint` | main | highlight + bobbing arrow + toast on a tile, optional camera pan |
| `say` | main | message into the in-game WARDENS UPLINK panel (optional toast) |
| `chat_read` | listener | long-poll (≤120 s) for the player's next chat message |
| `chat_history` | listener | last N messages |
| `selection` | main | what the player has selected (their way of pointing at something) |
| `camera` | main | get / set / fly keyframes / stop |
| `cutscene` | main | `status` / `list` the scenes from `Cutscenes/*.json`, the running one and the story record; `play` `id` (default ColdBoot, replaces a running scene, ignores the trigger policy; `Archive` reads the Ledger back), `skip`, `continue`, `choose` `choice` (an open card), `reload` (edit in the mod folder, reload, play), `reset` (archive `story.json`) |
| `speed` | main | 0 pause … 3 |
| `timberbot` | listener | GET/POST passthrough to the compiled-in Timberbot API (loopback) |
| `timberbot_ready` | main | open the ready gate in-process |
| `timberbot_routes` | listener | route list |
| `dump_assets` | main | write loaded blueprints / materials / textures to `Documents/Timberborn/WardensDump` (for the Leaf Coats port; deliberately not inside `Mods/Wardens/`) |

Every tool result may carry `chat`: player messages not yet delivered to the agent.

## Conversation loop (how the agent plays with you)

The full loop is `wardens/WARDEN.md`: the agent reads it with `manual`, then lives on `frame`, which
wakes it every 60 game ticks or when something happens, and tells it where to look. The short form:

1. Agent calls `frame` (or `chat_read`, waits up to 20 s), you type in the panel and press Enter.
2. Agent answers with `say`, points with `point` when it talks about a place, acts through
   `timberbot` (`POST /api/building/place` etc.).
3. You point back by selecting something in the game; the agent reads it with `selection`.

## Checks for the tutorial

- `wardens_status` → `tutorial.active` shows `Wardens.ColdBoot.Wake` right after a new game, with
  `stages_left: 2`; no vanilla tutorial ids appear (they are neutered by the faction modifier).
- During the orbit, 3 badtide notification toasts + sounds fire at roughly 1.5 s/5.5 s/9.5 s
  ("archived" badtides, see design/wardens-chapter-1-plan.md), each dropping a line into the
  WARDENS UPLINK chat with a real logged duration. `chat_history` should show 3 "Archive: badtide
  N of 3" lines after the orbit finishes.
- Click Continue through the three Cold Boot cards (Wake, Badtides, Directive). `Wardens.Basics`
  follows (move, rotate, zoom,
  pause/unpause, speed 2 → 3 → 1), then `Wardens.Scrap`: "Place: Scavenger Flag (0/2)",
  "Build: Scavenger Flag (0/2)", "Connect: Scavenger Flag (0/2)", "Stock: Scrap Metal (0/10)".
- Then Badwater and Biomass (Sludge Pump, Charging Post + "Select a Warden" + "Charge: every Warden
  above 50% (n/5)", Reed Bed, "Plant: Sludge Reed (0/40)"), Working hours (18), Storage (Scrap Pile
  → Scrap Metal, 2 Sludge Tanks → Badwater, Crate Rack → Biomass), Science ("Build: The Cruncher",
  "Power: The Cruncher"), Pods (2 Breeding Pods), Power and pods (Badwater Cell, "Power: Breeding
  Pod"), First beaver ("Beavers: (0/1)"), Reforestation (build the
  Planter Rig, "Plant: Birch (0/20)"), Maintenance (select a Warden, well-being panel, hours 16).
- Event tutorials: Dams (end of cycle 3 without a Dam), Vertical architecture (after
  Maintenance), Layer tool (first Platform finished), Haulers (cycle ≥ 3 with 3 idle Wardens or 20 bots),
  Droughts / Badtides (after the first one ends).
- The building bar shows only the Wardens set (Core is pre-placed; Charging Post, Cruncher, Sludge
  Burner, Power Shaft, Scrap Pile, Reed Bed, Sludge Pump, Badwater Cell, Sludge Tank, two pods,
  Planter Rig, Stairs, Platform, Dam, Crate Rack, Hauler Dock,
  Observation Deck, Scavenger Flag, Path). Placing the Cruncher next to the Core with a Power Shaft
  between them should light its illuminator (Core outputs 150 hp).
- Chapters (`wardens/README.md`, "How the chapters work"): on a new game **no building is padlocked
  and none carries a science price** (the whole bar is buildable from the first frame, on every map);
  `chapter status` shows `complete: []`, `next: Badwater`, `next_waits_for: Wardens.Scrap`, and
  `unlocked_at_load: []` (a template listed there means a blueprint shipped with a science cost: the log
  names it, `validate.py` names the file). Finishing the Scrap tutorial (or `tutorial next` through it)
  must show the "Chapter 2: Badwater." toast within a second and put the same line in the chat panel;
  `complete` becomes `["Badwater"]`. Reloading the save shows no toast and keeps `complete`. To replay a
  later chapter's beat use `chapter unlock chapter_id=Signal`; starting with the tutorial off lists every
  chapter as complete (the story cannot advance without it) and changes nothing on the bar.
- `python wardens/tools/validate.py` must print `problems: none` before every in-game test (it includes
  `check_cutscenes.py`, which also runs alone and without the game's files:
  `python wardens/tools/check_cutscenes.py wardens/src`).
- The map (`design/wardens-wasteland.md`): the game may show an "older version" notice on load (the file
  claims 0.7.10 on purpose). Expect the Core on a flat pad with a dry basin east of it that the badwater
  from the north edge fills during the first day; ruin columns to the north-west and south of the Core;
  a pond with pines and birches on the hill in the north-east. If the map does not appear in the list,
  check `Documents/Timberborn/Maps/Wardens 01 First Light.timber` exists; if it fails to load, the log names
  the singleton or template, and `python wardens/tools/gen_map.py --check "<file>"` rules out the
  static causes.
- A Charging Post next to the Core, connected by a shaft, is what keeps the bots alive; build it first.
- The Forestry section must show plant buttons for the common trees (Planter Rig) and Sludge Reed
  (Reed Bed); a missing planter building for a plantable crashes the bottom bar at load.
- Power budget to verify: Core 150 hp; Charging Post 50; Cruncher 120; Badwater Cell +100 (needs a Sludge
  Pump on badwater and a bot working it); Sludge Burner +200 once Reed Beds deliver Biomass.

## Checks for level tasks (0.4.20+)

- A campaign level shows its tasks in a panel under the goods bar (`LEVEL 01: First Light  n/8`), with the
  vanilla tutorial on or off. Player.log: `[Wardens] tasks: level 01, 8 task(s), N done`.
- The current task shows its instruction and a line per check with progress; done ones are ticked.
  A task-done line appears in the Uplink and as a toast; a save from later in the level ticks through
  the tasks it already meets, one per half second.
- When the last task is done: the panel becomes the level-end card (`LEVEL 01 COMPLETE`), the toast names
  level 02. **Continue to level 02: The Sump** exit-saves and starts level 02; the log shows the
  transition. **Stay** folds the card; `+` opens it again.
- MCP: `campaign action=tasks` lists the checks with `have`/`count`; frames carry `task` and the events
  `task.done:<id>` and `level.complete`.
- To launch into a save for testing: `autoload.json` in the mod folder,
  `{"settlement": "First Light", "save": "<save name without .timber>"}` (one-shot, the game's own
  saves folder, `ExperimentalSaves` on the experimental branch).

### The tasks on the author's day-31 colony (2026-09-11, 0.4.22)

| Check | Result |
|---|---|
| Load by `autoload.json` | `[Timberbot] auto-loading: First Light - 2026-09-11 16h27m, Day 2-14.autosave` |
| Tasks at load | `tasks: level 01, 8 task(s), 0 done`, then Charge … Reeds done within seconds (5/8), each with its Uplink line |
| Haul, Second power | done once the Hauler Dock had 2 Wardens (7/8); the Badwater Cell making power as soon as haulers fed it |
| The panel | header and count shown top right; the body was hidden under the selected building's panel on the right, so it moves under the goods bar (0.4.23) |
| Names | the game calls the tanks, the pile and the dock "Small Tank", "Small Industrial Pile", "Hauler Dock"; the task texts now say so (0.4.23) |
| First Light, level complete | the first beaver woke: `tasks: FirstLight done (8/8)`, `campaign: Level 01 complete: First Light. Next: level 02, The Sump. …`, `tasks: level 01 complete`; campaign.json `completed: ["01"]` |
| Continue on the card | the author pressed it: exit save `First Light - … Day 2-15.autosave`, then `transition: starting level 02 (The Sump) on 'Wardens 02 The Sump' as a new Wardens game` |
| Level 02 loads | `campaign: level 02 (The Sump) … completed=[01]`; `new game: replaced 13 starting beavers with bots, … 10 Scrap Metal in the Core`; no `Can't validate`, no exception (the footprint fix holds on level 02 too). `tasks: none for level 02`: it has no tasks, no opening scene and no ending yet |

## Checks for cutscenes

Design and open questions: `design/wardens-cutscenes.md` (§9 is this list, §12 what the run answers).

- A new Wardens game (tutorial on): the letterbox comes up with the Uplink line "Cold Boot...", the
  three captions follow each other (orbit 8 s, push in 8 s, settle 6 s) with the dots filling, the
  camera orbits the Core once and ends where it started, the tutorial cards on the right stay clickable,
  and afterwards the game is paused but not locked (the speed buttons work). The log has
  `[Wardens] cutscenes: 1 loaded from ...` and `cutscene ColdBoot: start (trigger, 3 shots, 22 s)`.
- Skip at any point: the overlay goes, the camera stops where it is, the game is paused and unlocked.
- `cutscene status` while it plays shows `playing`, `id`, `shot`, `caption`, `waiting` (`flight`, `time`);
  `wardens_status.cutscene_played` is true afterwards; `frame` carries `cutscene` and the events
  `cutscene.start:ColdBoot` / `cutscene.end:ColdBoot`, with `attention[0].what == "cutscene"` while it plays.
- On a loaded save nothing plays; `cutscene play` replays it (on any faction, any map); edit
  `Documents/Timberborn/Mods/Wardens/Cutscenes/ColdBoot.json`, `cutscene reload`, `play`: the change shows.
  A broken edit lands in `errors` and the other scenes still load.
- Tutorial off, or `"cutscenes": false` in `settings.json`: nothing plays on a new game; `play` still works.
- **Level 01's opening (0.4.10).** A new game on `Wardens 01 First Light`, tutorial on or off (the handoff
  `{"level":"01"}` is the quick way): `FirstLight` plays instead of the Cold Boot. The log has
  `level triggers=True`, `cutscene FirstLight: queued by level:01` and `start (trigger, 12 shots, 99 s)`,
  and no `ColdBoot` line. The camera visits, in order, the whole plateau, the river's head at the north
  edge, the river, the Sump with its seep lit, the terraced shore, a ruin near the Core, the spring, the
  crossing, the Core, a Warden; the last card waits for Continue and the game stays paused. With
  `"cutscenes": false` nothing plays.
- `chapter unlock chapter_id=Badwater` opens the chapter and plays `Badwater`: two shots around the Core,
  the first caption with the scrap count filled in ("Scrap: 10. ..."), the second with a Continue button
  (Return or Space also continues); the camera flies back to where it was. Signal, Pods (three stock
  counts in the first caption) and Power the same way.
- `chapter unlock chapter_id=Green` plays `Green` (the camera on the first beaver, or the Core when there
  is none; "Day N. Born M.") and then `LevelEnd`: the level's end card with *Continue to Level 02* and
  *Stay*. A click writes `Documents/Timberborn/Mods/Wardens/story.json` (`choices.LevelEnd.end`, and
  `marks.birthday` from the Green scene), plays the matching closing shot, and `cutscene status` shows
  the record under `story`. `cutscene choose choice=stay` answers a card from the agent's side.
- `cutscene play id=Archive`: five cards with today's day and cycle, Wardens and beavers, Data Cores,
  science and scrap, the birthday mark and the level-end choice (`none` until they exist), then
  "Recorded."; Escape skips it (if the game's pause menu opens too, note it: the key goes, the button stays).
- `cutscene reset` archives `story.json` as `story.<timestamp>.json`; the next Green plays the card again.
- What to tune first: the zoom scale (`dzoom` ±0.15 is a placeholder), the tilt (`v` 60 and 35), whether
  the letterbox collides with the tutorial panel, and whether the caption is centered (the game's
  `text--centered` class) or needs a fixed width.

## Level 01 played by Claude over MCP, the opening as scripted (2026-09-11, 0.4.14, game machine)

A fresh handoff start of the remade land. Claude played through the `wardens` MCP tools from the end of
the opening to day 4, following the playbook's opening (two Charging Posts for the 10 starting scrap,
then two Scavenger Flags and paths); the author built alongside from day 3 and quit at day 8.

| Check | Result |
|---|---|
| Opening, Sump caption (0.4.14) | `cutscene status` read "Below the Core, the Sump: badwater, 1.2 deep. The seep and the river keep it full." |
| After the opening | the scene ended by Continue (`finished at shot 12/12`); `speed value=1` then ran the game, so it was paused and not locked |
| Skip mid-scene | not pressed: the author pressed Continue at the directive before the MCP `skip` arrived |
| Warden close-up at zoom -4.5 → -5 | framed the Core's wall: all 13 Wardens stand at 23,48, inside the Core, while the scene plays |
| The flags | both "Nothing to do in range" from day 1, ruins 2–4 tiles away one level down; cleared within 100 ticks of the first Stairs finishing (18,42) |
| Energy | 11 of 13 Wardens at 0 by day 2.8 with two Posts (capacity 1 each) |

Findings:

1. **Wardens cannot step one terrain level without Stairs, and every ruin on level 01 sat one level
   below the pad** (critical, reproduced). Symptom: flags at 18,44 and 21,46 (height 8) said "Nothing to
   do in range" with ruins at 19,42 and 14,48 (height 7); unemployed Wardens wandered for two days and
   none left height 8; scrap stayed at 0 after the two Posts. A Stairs at 18,42 cleared both alerts and a
   ruin paid 15 scrap within the day. The author's earlier colony on this land needed 6 Stairs.
   Source (read): the terrain nav mesh joins a tile only to same-height neighbours
   (`TerrainNavMeshUpdater.TilesAreOrthogonallyConnected`), and a flag's range is the terrain it walks to
   (`BuildingTerrainRange`, `InRangeYielders`). mapsmith assumed one level of climb (`max_step = 1`), so
   its walk report said "near ruins 10 min" and the level passed `reachable_scatter`. Consequence: the
   scripted opening softlocks at 0 scrap; recovered here only by taking both Posts down (2 scrap each
   back) for one Stairs. Remedy (the author chose it): mapsmith models on foot (one level) and with Stairs
   (`on_foot_from`, `[checks] on_foot_scatter`, Stairs counts in the walk report), and level 01's three
   first ruins stand on the pad (0.4.16 map); the playbook says Stairs before the Sump.
2. **Idle Wardens drain to 0 by day 2.8** (reproduced, the 0.4.5 finding 2). Two Posts of capacity 1
   for 13 Wardens. Remedy: open, the balance pass.
3. **Wardens at 0 Energy keep walking** (seen). Wardens listed at `energy 0.00` changed position between
   frames and walked down the new Stairs; unemployed ones refilled from 0 to 0.49–0.98 within a day
   while no Post stood. The frame's `why` ("a stopped Warden does not get up") and the story ("stops where
   it stands") say otherwise. Source: not read (the Energy need's effects and the bot behaviours).
   Remedy: open; read the bot need specs before the balance pass decides what 0 should mean.
4. **`/api/tiles` reported `badwater: 0` on every Sump tile** at contamination 1. Source (read):
   `ColumnContamination(x, y, col.Ceiling)` returns 0 unless the water reaches that height. Remedy
   (0.4.16): the column's own `Contamination`, upstream in `timberbot/src` and re-copied. Not yet seen.
5. **The frame's `at` is a world position** (`y` is the height), and `point` takes a grid tile, so
   passing one to the other points at the wrong place. Remedy (0.4.17): every `at` also carries
   `at.grid`. Not yet seen.
6. **The Warden close-up has no Warden to show:** at scene time the Wardens are inside the Core.
   Remedy (0.4.16): the shot looks at the Core's door from the south, where they come out. Not yet
   judged: on the 0.4.18 start the Loading issues dialog (finding 8) covered the frame's centre, and a
   vanilla "Drought started" banner stayed up from the Ruins shot to the directive (probably held by the
   modal dialog; look again once the dialog is gone).
7. **`/api/placement/find` offered no Sludge Pump site on the Sump's west shelf**, only three in the
   channel; the author's pump at 32,47 (height 6, west) placed from the tool bar. Source: not read. Remedy: open.
8. **Every load of the remade level 01 deleted two of its three river-head sources and all six
   UndergroundRuins**, behind a Loading issues dialog (seen on the 0.4.18 start; the author's day-3 save
   of the 0.4.9 land has the same two sources and no UndergroundRuins, so it is as old as the remake).
   Player.log: `Can't validate loaded BlockObject BadwaterSource(Clone) at (34, 1, 4). It's not backward
   compatible. Deleting it.` Source (read): `BlockObject.AddToServiceAfterLoad` deletes any block object
   whose blocks do not validate; a `BadwaterSource` is 3x3 (the three were placed one tile apart) and
   `UndergroundRuins` is a 5x5 surface object (`Underground: false`), which mapsmith buried three levels
   deep. Consequence: a dialog over the opening, a river on one third of its designed source (every
   playtest ran on one), and no underground ruins ever. Remedy (0.4.19 map): mapsmith knows the
   footprints (`SIZES`), the checker refuses a footprint that is not flat, overlaps or is buried; level 01
   places the one source it always had and no UndergroundRuins, and its Head caption no longer says three.

Verified on the 0.4.18 start: `/api/tiles` reads `badwater: 1` on the Sump; `speed` answers
`{"was":0,"speed":1,"applied":true}`; a frame's `at` carries `grid` (`{"x":19,"y":59,"z":6}` for the Stairs
at 19,59,6); two flags by the pad's ruins (17,49 and 18,49) never showed "Nothing to do in range", and by
day 1 at 87 % the ruin at 16,48 was gone and the one at 16,46 stood a level lower (H2 → H1).

## The remade level 01 and its opening (2026-09-11, 0.4.9–0.4.10, game machine)

Two fresh starts by the main-menu handoff (`{"level":"01"}`), the author's tutorial setting off, read
through the Wardens MCP server and the log.

| Check | Result |
|---|---|
| Boot (0.4.9) | `replaced 13 starting beavers with bots, 13 charged to full, 10 Scrap Metal in the Core`; `bots unlocked for free in 1 workplace template(s)`; no exception in either session |
| The land | `/api/tiles` heights match the spec: terrace 8 → 7 → shelf 6 at x 26–33, Sump bed 3 at x 34–42, the 3×3 seep at 40–42, 48–50 |
| The Sump fills on day 1 | at day 1, 41 %: water 0.5 on every Sump tile and along the channel to x 47 (y 47–48) |
| The opening (0.4.10) | `level triggers=True` with `tutorial=False`; `cutscene FirstLight: queued by level:01`, `start (trigger, 12 shots, 99 s)`, `finished at shot 12/12`; no Cold Boot line. Screenshots: river, Sump (seep lit), shore (Core above the terraces), ruins (one lit beside the Core), spring (lit, grove around it), crossing, Core (lit), a Warden, the directive; each frames what its caption names |
| Pre-filled water (0.4.13) | a third fresh start: no exception; at tick 1, paused, `water 1.2` on every Sump tile and along the channel to x 47 (`/api/tiles`, y 48); the river shot shows a badwater band across the plateau and the Sump shot a full basin (DPI-aware screenshots, the whole screen with both letterbox bars) |
| Caption args (0.4.12) | the Core shot reads "The Core. 13 Wardens online, charged to full. Power: the Core, and nothing else." |

Findings:

1. **Captions with args print `System.Object[]`.** Symptom: the Core shot read "The Core. System.Object[]
   Wardens online, charged to full." Source (read): `WardensCutscenes.CaptionText` passed the args array to
   `ILoc.T`, which has only `T(key)` and generic `T<T1..T3>` overloads (decompiled `ILoc`), so the array
   bound as one parameter. Consequence: every caption with `args` was wrong: this one, and the Badwater,
   Pods, Green and Archive scenes. Remedy: `Localize()` calls the overload for the count (0.4.11); seen fixed in 0.4.12.
2. **The river is dry while the opening shows it.** Symptom: the river and Sump shots show empty beds.
   Source: maps ship dry (the pre-filled water encoding is undocumented, `design/wardens-wasteland.md`),
   and the scene plays at tick 0. Consequence: the captions describe water the picture does not have.
   Remedy (the author chose it): the map ships pre-filled. The column encoding was decoded from the
   decompile and a real autosave, mapsmith writes it with `[water] fill`, the levels are the ones a
   day-3 autosave of this map settled at (4.17), and the load is clean (0.4.13).
3. **`cutscene_played` stayed false after the opening.** Source (read): the flag tracked the Cold Boot
   only. Consequence: the Warden's boot check could read a played opening as not played. Remedy:
   `OpeningPlayed` counts a `level:` scene too (0.4.11).
4. **The badtide replays finish vanilla's first-badtide tutorial on day 1.** Symptom: `finished:
   ["SurvivedFirstBadtideTrigger"]` and the `Wardens.Badtides` card ("The settlement has weathered its
   first badtide") open right after the opening. Source: `WardensArchivedBadtides` posts the vanilla
   weather events, as designed for the Cold Boot. Consequence: a card about a badtide the colony never
   lived through. Remedy: deferred; it predates the opening (the Cold Boot replays the same three).
5. **The Warden close-up is no closer than the Core shot** (zoom 0.25 against 0.6). Source (read): no
   clamp. The distance is `1.3^ZoomLevel * 32` (`CameraService`, `CameraService.blueprint.json`), so
   0.25 is 34 tiles and 0.6 is 37: the same shot. The director sets `ZoomLevel` directly and nothing
   clamps it; the player's scroll range is -8 to 6. Remedy: the Warden shot flies from -4.5 to -5 (10 to
   9 tiles); the scale is written into the `cutscene-director` agent.
6. **The speed would not leave 0** in the 0.4.9 session (`speed value=3` answered `speed: 0`, no scene
   playing, the author placing Stairs). Source (read): `SpeedManager.ChangeSpeed` only queues the value
   for its `LateUpdate` and drops it while the speed is locked (`OverlayPanelSpeedLocker` locks it for
   every panel with `LockSpeed`), and the tool answered with `CurrentSpeed` read straight after, which is
   always the old speed. The tool also passed the button number as the speed, so `3` was x3, not the fastest button (x7). Remedy
   (0.4.15): the tool maps 0..3 through the buttons (0, 1, 3, 7) like `POST /api/speed`, and answers
   `was`, `speed`, `applied` and, when locked, the reason.

## Level 01 played through the MCP server (2026-09-11, 0.4.5–0.4.6, game machine)

Claude Code played First Light over the `wardens` MCP tools, with the human building alongside, to day 5.
Findings, each with the four slots:

1. **Scavenger Flags never got a worker** (reproduced). *Symptom:* both flags finished, "No available
   workers in district", 11 bots idle. *Source:* the flag is vanilla `ScavengerFlag.IronTeeth`, which
   prices bots at 500 Science; `bot_workforce.py` only rewrites `.Wardens` blueprints. *Consequence:*
   level 01 can never get scrap (softlock). *Remedy:* fixed in 0.4.6, every priced workplace template is
   unlocked for bots on load (`WardensBotWorkforceMigration`); seen: flags 1/1, scrap arriving.
2. **Idle Wardens drain to 0% while working ones hold the posts** (seen). *Symptom:* six unemployed bots
   at 0%, stopped, on day 2; they recovered overnight. *Source:* the Charging Post is an attraction with
   capacity 1 (+0.6/h), used in off-hours. *Consequence:* a morning of stopped Wardens every day with few
   posts. *Remedy:* open. Ending shifts at 14:00 lifted the day's lowest charge from 25% to 33%; more
   capacity per post, or idle bots charging first, belongs in the level's balance pass.
3. **Badwater does not keep one Cell fed** (measured). *Symptom:* the second Cell stays cold, the first
   empties. *Source:* one Sludge Pump makes ~4.3 Badwater/day (the district sampler's own reading); a
   Badwater Cell burns 0.4/h = 9.6/day. *Consequence:* the Power chapter's supply is ~half of what the
   bar implies; a colony that builds six posts and a Cruncher runs at 60%. *Remedy:* open, balance pass
   (cell burn or pump rate).
4. **The second pump site needs Stairs** (seen). *Symptom:* a pump at 29,42 (z 6) "isn't connected to
   any district center by paths". *Source:* the Sump's west rim below the Core is a 2-level step; Stairs
   cost 70 Science. *Consequence:* the second pump waits for the Cruncher. *Remedy:* decide whether level
   01 means that (the Signal chapter pays for it) or needs a ramp in the map.
5. **`/api/placement/find` misses rim pumps** (seen). *Symptom:* only dry far-rim spots returned while a
   hand-placed pump at 26,47 works. *Source:* not read yet: the finder's water-input check for rotated
   intakes. *Consequence:* the agent cannot place pumps on its own. *Remedy:* open, Timberbot placement.
6. **The Gate's sampler over-reported the starting stock** (seen). *Symptom:* Berries 43–129/day of
   "surplus" with a flat 130 in stock, and a killed First Light game's reading kept beside the new one.
   *Remedy:* fixed in 0.4.7 (warm-up before the first sample, one settlement's readings replace its old
   ones, ISO timestamps kept). Not yet run.
7. **This install saves to `ExperimentalSaves/`** (seen: `Saving game to bot - …` wrote
   `ExperimentalSaves/bot/…`). `SaveLevelStarter` and `TimberbotAutoLoad` hard-code `Saves/`. *Remedy:*
   open; harmless until a level ships a save.
8. **`/api/tiles` reports `badwater: 0` everywhere**, at the Badwater sources too (seen on Lakes and on
   level 01). *Remedy:* open; do not trust the field until it is read against the game.

Verified on the way: the in-game `campaign action=next level_id=01 force=true` from a Lakes game
(`started: true, strategy: new game`, exit save written), the Sludge Reed with 42 marked tiles and no
exception (Cattail fix), bots as default workers, and a district reading in `campaign.json`
(`ScrapMetal 26.2/day, Badwater 4.3/day`).

## Checks for the Gate (0.4.5)

Two maps are needed: one to leave, one to link it from.

- [ ] On map A, play at least a day with some surplus (scrap piling up). `Player.log` shows no
      `[Wardens] district exports:` warning; `campaign action=status` → `districts` has map A's district
      with `exports` per day, and `campaign.json` beside the mod has the same.
- [ ] Leave map A (Exit to main menu, or `campaign action=next`). The `districts` entry's `day` and
      `savedUtc` move to the moment you left.
- [ ] On map B, the Gate is under District Management (30 Scrap Metal). Build it; its panel says how many
      districts can be linked. *Link next* shows "Linked: District 1, <settlement A>." and what arrives.
- [ ] Within a game day the Gate's inventory (the vanilla fragment under the panel) fills with those
      goods, and with a Hauling Post in the district they move into storage.
- [ ] Save, load: the Gate is still linked to the same district.
- [ ] On map B, the `districts` entry for B's own district does not count the Gate's deliveries as B's
      surplus (subtracted), so linking B from a map C does not re-export A's goods.

## Playtest findings (2026-09-10, Claude Code via the `wardens` MCP)

A session run entirely through the MCP loop (`manual` → `wardens_status` → `timberbot_ready` →
`frame`), on whatever map/save was already loaded (not confirmed to be Wardens Wasteland). Day 1
Cold Boot, 13 Wardens, 0 beavers, unpaused on player request. Placed a Scavenger Flag on the one
nearby Scrap Pile, got 20 ScrapMetal, placed a Charging Post beside the Core, set worker counts —
then the MCP/Timberbot connection dropped partway through night 1 and did not recover.

**Blocking bug — flooded spawn:** the ground around the Core carried a uniform ~0.1 water depth
(ambient/rain, not a real puddle), which was enough to flag both the **Core** and the nearby
**Scrap Pile** as `"Flooded."` in `/api/alerts`. A flooded Core never assigns workers: all 13 bots
stayed `unemployed` the whole session, so no construction, no scavenging, nothing progressed.
Energy fell 49% → 21% with no charging path — a fresh Cold Boot save can softlock itself before the
player ever gets a card to click. Needs checking against the map's terrain/drainage (see below) and
possibly a flood-immunity or higher threshold for Core/Scrap Pile specifically, since they're both
required for the very first moves.

**Timberbot API bugs found in this session:**

- `/api/tiles?x1&y1&x2&y2`: whichever query param is *last* in the URL is silently dropped
  (reproduced 3× with different param orders — it's positional, not key-specific). Workaround:
  append a harmless trailing param, e.g. `&format=json`.
- `limit`/`offset` pagination is ignored on `/api/gatherables` and `/api/beavers` — always returns
  the same first page.
- `/api/buildings?name=ChargingPost` returned `total:0` immediately after placing one, while the
  unfiltered list showed it present (`finished:0`) — only observed once, possibly a timing race.
- `/api/alerts` returns `type: "Flooded."` (with the period), not in the documented
  `unstaffed`/`unpowered`/`unreachable`/`status` enum.
- `chapter status`'s `complete` list already showed Badwater through Green on a brand-new Cold Boot
  save with nothing built — looks like state not reset per playthrough. *(2026-09-11: the check read the
  unlock state, which the tutorial-off toggle opened at load; `complete` now means the chapter's tutorial
  has finished, and the bar is open from the start regardless.)*

## Level start from the main menu (2026-09-11, 0.4.1, game machine)

`campaign.handoff.json` = `{ "level": "01" }`, game launched through Steam, Mods dialog OK'd. Every
`[Wardens]` line of the run, verbatim from `Player.log`:

```
[Wardens] removed the retired map 'Wardens Wasteland' from C:\Users\micha\Documents\Timberborn\Maps (it shipped under that name in an earlier version; saves made on it are unaffected)
[Wardens] maps: 0 installed, 2 already current, 1 retired removed, list refreshed
[Wardens] handoff: starting level 01 (Wardens 01 First Light); maps 0 installed, 2 already current
[Wardens] transition: starting level 01 (First Light) on 'Wardens 01 First Light' as a new Wardens game
Starting new game at 2026-09-11 11:51:01Z:
FactionId: Wardens, MapFileReference: Name: Wardens 01 First Light, Path: , Resource: False, GameMode: Order: 20
[Wardens] chapters: 5 gates, gating=True, tutorial=False
[Wardens] campaign: level 01 (First Light) on 'Wardens 01 First Light'; completed=[]; ends with Wardens.MoreBeavers
[Wardens] transition: level 01 would start by 'new game'
[Wardens] MCP server listening on http://127.0.0.1:8090/mcp
Load time: 18705ms (scene index: 2)
```

No `missing` lines: nothing in the transition had to fall back. Not run yet: `campaign action=next`
from inside a level (the in-game switch, with its exit save).

## What a map needs for this mod to work

- **The Core's footprint, and its immediate approach tiles, must sit above the flood line** — not
  just look dry. This session's spawn had ambient water pooling to ~0.1 depth across the whole area,
  which was enough to flood the Core itself. `design/wardens-wasteland.md` already describes the
  intended layout (Core on a flat pad, dry basin *east* for badwater to fill later, ruins to the
  north-west/south) — this needs verifying against the actual loaded terrain, since this session's
  map may not have been Wardens Wasteland.
- **At least one Scrap Pile reachable and dry within a few tiles of the Core** — First Light's
  opening move is a Scavenger Flag + scrap run; if the nearest ruin floods too, there's no path to
  the 5 ScrapMetal a Charging Post costs.
- **A buildable, unflooded, `nearPower` tile adjacent to the Core** for the Charging Post —
  `placement/find` returning a valid spot isn't sufficient if the tile floods after the fact.
- **Wet/dry contrast placed deliberately, not uniformly** — the Badwater Sump needs low/wet ground
  east of the Core; Pods and Green need dry ground for pods and planting. A map that's evenly damp
  everywhere (as this session's was) breaks the chapter progression rather than just being ugly.

## Known gaps

- Starting a **new game** from the API (faction + map + mode) is still missing.
- No screenshot endpoint yet; the agent sees the world through the read API only.
- Cutscenes: the game's UI stays visible under the letterbox; whether Escape reaches the overlay (and
  whether it also opens the pause menu) is unverified; the scenes' zoom and angles are untuned; Level 02
  does not exist, so *Continue to Level 02* only records the choice.
- The Core does not charge bots itself (no vanilla building combines a workplace with an
  attraction); the Charging Post does, at 50 hp from the Core.
- Sludge Reed skips the watered/contaminated components; whether growth needs soil moisture
  is unverified until a Reed Bed runs.

## Leaf Coats copies

After `python wardens/tools/import_leafcoats.py` the mod folder also carries Leaf Coats, its add-ons,
the Script Pack DLLs, Vertical Nav Mesh and Harmony (0Harmony.dll). Disable those seven workshop mods
in the mod manager, or two copies of the same assemblies load. To extract the bundle content for the port: load any game, then
`dump_assets what=blueprints filter=LeafCoats`, `what=materials filter=LeafCoats`,
`what=textures filter=LeafCoats max_count=400`; results land in `Documents/Timberborn/WardensDump/`.
