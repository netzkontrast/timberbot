# The Wardens: changelog

The patch number moves with every local build (`tools/bump_version.py`, run by the csproj); a minor
version marks a milestone (`bump_version.py --minor`) and gets an entry here. Nothing below has been
verified in-game yet; `../AGENTS.md`, "The Wardens: state", says what the first run must answer.

## 0.4.20: level tasks, and the level-end card that starts level 02

- **Tasks for the player** (`Levels/<id>.tasks.json`, `WardensLevelTasks.cs`), shown in a panel top right
  (`WardensTaskPanel.cs`) and checked every half second against the game, whatever the vanilla tutorial
  setting: the old ending (the tutorial line) never fired with it off, because a stage advances only on the
  hidden card's Continue. One task at a time; a done task stays done in the save. Check types: built,
  powered, generating, workers, stock, beavers.
- **Level 01's eight tasks**, picked from a playthrough scouted over MCP: Charge · Scavenge · Down to the
  Sump · Store · Reeds · Haul · Second power · First Light (the first pod-born beaver).
- **The level-end card.** When the last task is done the panel becomes a card: *Continue to level 02:
  The Sump* exit-saves this colony and starts the next map; *Stay* folds it. The LevelEnd scene's own
  Continue (tutorial on) starts it too. Level 02 is marked shipped; its map has not been played yet.
- MCP: `campaign action=tasks`, tasks in `campaign status`, the frame's `task`, events `task.done:<id>`
  and `level.complete`. `check_level_tasks.py` (run by `validate.py`): types, templates, goods, loc.
- The save auto-loader (`autoload.json`) looks in the game's own saves folder; it missed
  `ExperimentalSaves` on the experimental branch. Fixed in `timberbot/src` and re-copied.

## 0.4.10: level 01 opens with a cutscene

- **`Cutscenes/FirstLight.json`**, the level's opening: 12 shots, about 100 s, over the new land in the
  order the level plays it. The dark plateau and the three archived badtides; the river's head at the north
  edge; the river across the plateau; the Sump and its seep; the terraced shore; the first ruins; the
  spring; the crossing; the Core with the Warden count; a Warden; the chapter directive, which waits for
  Continue. The captions are `Wardens.Cutscene.FirstLight.*` rows, every number checked against the built
  map (walk report, spec, the archive's badtide count).
- **A `level:<Id>` trigger.** It fires on a new game on campaign level `<Id>` and plays with the vanilla
  tutorial off, since `DisableTutorial` is the player's own setting. On level 01 the opening replaces the
  Cold Boot, which still plays on every other map. `check_cutscenes.py` resolves the id against the level
  table.
- The Cold Boot's Wake card says "Every Warden online" instead of "Five": the count is the difficulty's
  (13 on Normal).
- **`.claude/agents/cutscene-director.md`**: an agent that connects to the running game over the MCP server
  and tunes scenes live (reload, play, screenshot, fix). Todo: `docs/plan/first-light-opening.md`.
- 0.4.11, from the first run of the opening: captions with `args` printed `System.Object[]` (every chapter
  scene too; `ILoc.T` has no `params` overload), and `wardens_status.cutscene_played` now counts a level's
  opening, not only the Cold Boot.

- 0.4.13: **level 01 ships with its water.** The river, the channel and the Sump are pre-filled to 4.2 (the
  surface a day-3 autosave of the map settled at: 0.2 deep in the river bed, 1.2 in the Sump), badwater
  contamination 1; the spring's crater to 12.25, clean. The opening now flies over water, not dry beds.
  mapsmith gained `[water] fill` and a checker for the column encoding, which is decoded from the 1.1.2.4
  decompile (`WaterColumnPackedListSerializer`) and a real save; 9 new tests.
- 0.4.15: the Warden close-up flies in to 9 tiles (zoom -4.5 → -5; 0.25 was 34 tiles, as far as the
  Core shot: the zoom is `1.3^ZoomLevel * 32`, not clamped). The `speed` tool maps 0..3 through the
  speed buttons (x1, x3, x7) and answers `was`, `speed`, `applied`: it used to report the old speed,
  because the game applies a change on the next frame, and a locked speed drops it.
- 0.4.16–0.4.18, from Claude playing level 01 over MCP: **Wardens walk on one level.** The game joins a tile
  only to neighbours of the same height, so a Stairs (3 scrap) is needed for every level; level 01's ruins
  all sat one level below the pad and the opening (two Posts, then flags) softlocked at 0 scrap. mapsmith
  now tells on foot from with Stairs (`on_foot_from`, `[checks] on_foot_scatter`, Stairs in the walk report),
  and level 01's three first ruins stand on the pad (new map; saves on the old land keep it). The opening's
  Ruins and Shore captions follow the land; the Warden shot looks at the Core's door (the Wardens are inside
  it while the scene plays). `/api/tiles` reads the water's contamination (it read 0 everywhere; fixed
  upstream in `timberbot/src` and re-copied). Frame positions carry `at.grid`, the tile `point` takes.
- Next build (0.4.19): **the map loads without a Loading issues dialog.** A `BadwaterSource` is 3x3 and
  `UndergroundRuins` a 5x5 surface object; level 01 placed three sources side by side and buried six
  UndergroundRuins, and the game deleted two sources and all six on every load since the remake. mapsmith
  knows the footprints and refuses overlap, uneven ground and burying; level 01 ships the one source every
  playtest ran on and no UndergroundRuins (level 02 and the prototype likewise). The Head caption no
  longer says three sources.

State: the opening `verified` on the game machine (0.4.10: it played by itself on a handoff start with the
tutorial off, all 12 shots, screenshots in PLAYTEST.md); the 0.4.11 fixes deployed as 0.4.12.
`pytest` 139 passed before the water work, mapsmith 100; `check_cutscenes.py wardens/src` → `problems: none`.

## 0.4.9: level 01 remade from its playtest

- **New land for `Wardens 01 First Light`** (same name, seed and size; saves on the first land keep it).
  mapsmith builds it now, from `wardens/maps/wardens-01-first-light.map.toml`:
  - the river runs 20 tiles east of the Core instead of curling against the pad;
  - the Sump is deeper (bed 3), has a Badwater seep of its own (full on day 1, not day 5) and a channel to
    the river so it never runs dry;
  - its shore is a terrace, pad 8 → 7 → a shelf at 6, with 32 walkable tiles of waterside off the pad for
    pumps (the first land: 11, all on or under a cliff);
  - three small ruins sit 7–14 steps from the start, and "south of the pad" moved off the shore;
  - the spring is one bridge of at most 5 tiles away (Platforms are free since the open bar).
- **Two new map contracts** (`shore`, `crossing`) and a `terrace` op in mapsmith, each with tests that
  build the good land and the old one. The first land fails `shore`; that is the finding it encodes.
- **Opening balance:** the Wardens boot fully charged and the Core starts with 10 Scrap Metal
  (`WardensStartingPopulation.cs`); the Badwater Cell burns 0.18 Badwater/h, what one Sludge Pump makes.
- The Badwater chapter's caption no longer says the Sump is dry.

State: `built` (0.4.9, 0 warnings, 0 errors); `pytest` 136 passed; `mapsmith check --level 01` clean with
both contracts; `validate.py wardens/src` only the known level-02 note. In-game run: see PLAYTEST.md.

## 0.4.5: the Gate

- **A new building, the Gate** (`MapGate.Wardens`, District Management, 30 Scrap Metal, no science).
  It links a district you left on another map: whatever that district produced beyond its own needs
  arrives at the Gate every day, and the Hauling Post's haulers carry it into this district. One Gate
  links one district; build more Gates to link more. The panel shows the link and what arrives, with
  *Link next* and *Unlink*.
- **Every map records its districts' surplus** (`WardensDistrictExports`, in `WardensGate.cs`). Each
  finished district's stock is sampled four times a game day, and its surplus per good is the growth
  over the last three days minus what Gates delivered into it, so a chain of maps never re-exports the
  same goods. The readings go into `campaign.json` (`districts`) once a game day and on every way out:
  Exit to main menu, a level change, quitting. A later visit to the same save replaces its reading.
  `campaign action=status` lists them.
- The Gate is the Large Industrial Pile's pad with its stockpile removed and vanilla's public output
  inventory (`SimpleOutputInventorySpec`, the one flags and the district center use) put in. It is a
  source, not storage, so it does not count toward the district's capacity.

State: `built` (0.4.5, 0 warnings, 0 errors); `pytest wardens/tools` 118 passed; `validate.py
wardens/src` clean apart from the known level-02 note. Not yet seen in the game:
`wardens/playtest/PLAYTEST.md`, "Checks for the Gate", is the first run's list.

## 0.4.2: bots work everywhere, and the Sludge Reed loads

- **Bots are the default worker in every Wardens building, and nothing about bots costs science.**
  The Warden knows everything there is to know about bots. Every Wardens workplace now defaults to
  Bot with no per-building unlock cost (vanilla priced bots at 250 to 10,000 science per building),
  and bot buildings (Bot Assembler, Bot Part Factory) cost no science. The rule lives in
  `tools/bot_workforce.py`: both generators apply it and `validate.py` fails on any blueprint that
  breaks it. Exceptions: a building locked to its own worker type (the Power Treadmill a beaver runs,
  the Data Desk) and a chapter padlock, which is story gating, not science.
- **The district default is Bot too** (`WardensBotWorkforce.cs`). A finishing workplace copies its
  district's default worker type over the blueprint's, and vanilla starts every district as Beaver,
  so the blueprint rule alone never showed. Wardens district centers carry `WardensBotWorkforceSpec`,
  and saves made before this are moved over once.
- **Fixed:** hovering the planting tool threw `Material Cattail not found in repository` (the Sludge
  Reed is vanilla Cattail, whose material only the Folktails collection holds). The faction now also
  loads the `Folktails` material collection; its names are disjoint from IronTeeth's and Common's.

State: `built` (0.4.2, 0 warnings, 0 errors); `pytest wardens/tools` and `validate.py wardens/src`
clean apart from the known level-02 `shipped=false` note. Not yet seen in the game.

## 0.4.1: the level loader, on the game's real API

- **The level transition is rewritten against the 1.1.2.4 decompile** (`WardensLevelTransition.cs`,
  `WardensHandoff.cs`). The first compile of the cloud version (0.3.7, 0 errors) showed the problem
  was not the compiler: every reflection guess missed the real shape (`NewGameConfiguration` takes
  four arguments, the mode type is `GameModeSpec`, `GameSceneLoader` has no main-menu method), so it
  could never have started a level. It now calls `GameSceneLoader.StartNewGameInstantly("Wardens",
  MapFileReference.FromUserFolder(map), title)` directly, exit-saves the colony it leaves
  (`Autosaver.CreateExitSave()`), and falls back to `MainMenuSceneLoader.SaveAndOpenMainMenu()` plus
  the handoff file when the map is not installed. `WardensReflect.cs` and `WardensServiceLocator.cs`
  are gone.
- **Launch straight onto a level.** `{"level": "01"}` in `campaign.handoff.json` (mod folder) makes the
  main menu start that level as a new Wardens game, once.
- **Fixed:** a retired map (`Wardens Wasteland`) was only removed on a launch that also installed
  something, so it survived in the player's Maps folder forever. It is now removed on every launch.
- The install is idempotent (`EnsureInstalled`), so the handoff can make sure the map is there
  whichever MainMenu singleton loads first.

State: `built` (game machine, `dotnet build -c Release` → 0 warnings, 0 errors). **`verified`**: the
main-menu start (Player.log: `FactionId: Wardens, MapFileReference: Name: Wardens 01 First Light`).
The in-game `campaign action=next` switch is `built`, not yet run.

## Unreleased

### Added
- **Level 02, *The Sump*** (`wardens/src/Maps/Wardens 02 The Sump.timber`, spec
  `wardens/maps/wardens-02-the-sump.map.toml`). Badwater from the west, exactly one gorge worth
  damming, a clean creek joining above it. Built by mapsmith and checked against the level's
  contract, not the eye.
- **Contracts in mapsmith** (`[contract]` beside `[checks]`): `single_gorge`, `confluence_upstream`,
  `never_touch`, `unreachable`. `[checks]` asks whether the game will load the map; a contract asks
  whether it is still the level it was meant to be, which is the thing that breaks silently when a
  seed or a valley width changes. `mapsmith contracts` lists them.
- **A campaign map index** (`wardens/maps/levels.toml`, `mapsmith levels --verify`): level id → map
  name → spec, cross-checked against the `Levels` table in `WardensCampaign.cs`. A map name that
  does not match makes the running game treat the map as a non-campaign one, silently; now the tools
  and the tests catch it.
- **Loading the next level's map from inside the game** (`WardensLevelTransition.cs`,
  `WardensHandoff.cs`, `campaign action=next`). Three strategies behind one interface, chosen at
  load and logged: a programmatic new game, a shipped save, and a handoff file the main menu picks
  up. The transition is offered, never forced — `action=next` refuses while the level is unfinished
  unless forced, and the playbook now says to ask the player first.
- `avoid` on the `hill` terrain op: landscape after the water without filling in the channel. This
  is what makes a gorge possible — the spurs are raised after the river carves.

### Changed
- **Every building, on every level, from the first frame.** The chapter padlocks are gone: the nine
  buildings that shipped with `ScienceCost: 999999` (Sludge Pump, Reed Bed, Sludge Tank, Crate Rack,
  the Cruncher, both pods, Badwater Cell, Sludge Burner) and the three with vanilla's science price
  (Planter Rig 60, Stairs 70, Platform 100) all ship at 0, in the blueprints and in
  `tools/gen_buildings.py`. `WardensChapters.cs` keeps the chapters as story beats: the toast, the
  Uplink line and the cutscene still fire when a chapter's tutorial finishes, but nothing is unlocked,
  and `chapter status` reports `complete` as the chapters the story has reached (the old check read
  the unlock state, which is why a fresh save listed every chapter complete). A safety net at load
  unlocks anything that still carries a cost and names it in the log (`unlocked_at_load`).
  `tools/validate.py` now refuses a science cost anywhere in the faction's collections, and
  `tools/test_validate.py` guards the same rule offline. The Leaf Coats port stays out of the faction:
  its 44 blueprints reference the local-only bundle.
- The tutorial line follows: Reforestation builds the Planter Rig straight away (no accumulate/unlock
  steps; the stage is `Wardens.Reforestation.BuildPlanter`), the Science card no longer promises
  unlocks, and Vertical architecture requires Maintenance alone instead of `StairsUnlockedTrigger`
  (stairs are free, so there is no unlock for that trigger to see). The chapter opening lines say what
  the chapter is about instead of what became available.
- `chapterGating` is removed from `settings.json` and `WardensSettings`; a leftover key logs one line
  and is ignored.
- The prototype spec that briefly claimed to be level 02 is now `prototype-two-streams.map.toml`.
  The C# level table is the source: 02 is *The Sump*, 03 is *The Pods*.

### State (the words in `.claude/skills/driving-iterations`)
- The maps, the specs and the tools are **`checked`** (cloud): `pytest wardens/tools`,
  `mapsmith check --level 02`, `mapsmith levels --verify` and `check_cutscenes.py` all clean.
- The free bar is **`checked`** (cloud, 2026-09-11): `pytest wardens/tools` with the new `test_validate.py`,
  `check_cutscenes.py`, and `gen_tutorial.py` regenerated the stages from its table.
- Everything under `wardens/src/*.cs` is **`written`** — this was authored where there is no .NET SDK
  and no game DLLs, so no compiler has seen it. Not `built`, not `tested`, not `verified`.
- Nothing here has been loaded in Timberborn. The level-transition code reaches every unverified game
  API by reflection and logs the exact member when one is missing, so the first build-and-run turns
  `design/wardens-campaign-maps.md` §5 into findings with names in them.

## Unreleased (0.3.5): the campaign's first level

- **Level 01 is a campaign level.** `Maps/Wardens Wasteland.timber` is now
  `Maps/Wardens 01 First Light.timber`. `world.json`, `version.txt` and the thumbnail are byte-identical
  to the file that shipped under the old name; only the description in `map_metadata.json` changed, and
  only to name the level. The seed (3000) and the size (96) are pinned in the level class and will not
  change again: the same map name with a different seed is a different map, and level 10 regenerates
  this exact heightfield.
- **`gen_map.py` is a level registry, not one map.** `Terrain` holds the shared toolkit and the file
  plumbing; `FirstLight` composes level 01 out of it. `--level`, `--all`, `--list`. Each level declares
  its own name, seed, size, layer count and **contract** — the properties its play depends on, checked
  by `--check` alongside the format checks: scrap within reach of the starting pad, the river crossing
  the map, the Sump deep enough for the Sludge Pump, exactly one clean spring far from the badwater,
  contamination a band rather than a flood.
- **The maps install themselves** (`WardensMapInstaller.cs`, `[Context("MainMenu")]`). The game lists
  custom maps from `MapRepository.UserMapsDirectory` and nowhere else — a mod's own `Maps/` folder is
  not a map source (confirmed by decompiling `Timberborn.MapRepositorySystem` from 1.1.2.4). The mod
  copies its maps there at the main menu, calls `NotifyMapRepositoryChanged()` so the list refreshes
  without a restart, and removes a map it used to ship under a retired name. `"installMaps": false` in
  `settings.json` turns it off.
- **The campaign remembers across maps** (`WardensCampaign.cs`, `campaign.json` beside `settings.json`).
  A level is a map plus the chapter line that runs on it; the level table is keyed by map name, because
  `MapNameService.Name` is how a running game says which map it is on. Entering a level records it;
  finishing the level's ending tutorial (level 01: `Wardens.MoreBeavers`, the first pod-born beaver)
  completes it, toasts, and names the next map. A map not in the table means "not a campaign level" and
  the service stays quiet. `validate.py` cross-checks the table against the shipped `.timber` files.
- **A `campaign` MCP tool**: `status` (the level table and where this run is), `ledger` / `record` (the
  cross-level memory — the only thing that survives a map change), `complete` / `reset` for testing.
- **Stronger prompt injection.** The `initialize` reply's `instructions` are rebuilt from live state on
  the main thread each tick (faction, level, bots, speed, ready gate, unread chat) instead of being a
  fixed paragraph, and the server now answers `prompts/list` / `prompts/get`: `warden_boot` returns the
  whole playbook plus that state, `warden_level` the campaign record and the Ledger.
- **The plan is a skill.** `.claude/skills/warden-play/SKILL.md`: the boot sequence, the frame loop, the
  chapter-by-chapter act list for level 01, the daily Ledger routine and the failure table.

Verified in-game on 2026-09-10 (v0.3.5): the mod loads, the map installer runs at the main menu, the
MCP server answers `initialize` with the live-state instructions, `prompts/list` and `prompts/get`
work, and the `campaign` tool correctly reports "not a campaign level" on a vanilla map. **Not yet
verified: a new game on `Wardens 01 First Light`** — level detection, completion and the toast.

- **Iteration 04 is planned, not built** (2026-09-10, evening; `docs/plan/HANDOVER.md` and
  `docs/plan/iteration-04-first-light-verified.md`): the proof run of level 01 on its own map, the two
  MCP server fixes the playtest findings trace to by reading (the `timberbot` passthrough drops the last
  query parameter when the query is carried in `path`; the MCP listener handles one request at a time, so
  a `frame` long-poll blocks every other call), a `ledger` tool for the daily line, one answer to the
  level-02 numbering, and level 10 *Home* as a generator flag. Nothing in it changes this version.

## 0.3.0 (2026-09-06): cutscenes, a release path

- **Cutscenes as data** (`design/wardens-cutscenes.md`). A scene is `Cutscenes/<Id>.json`: shots with a
  camera flight (keyframes relative to a named anchor and to the pose at scene start), one caption, and
  optionally a pointer, a toast, an Uplink line. One runner (`WardensCutscenes.cs`) pauses and locks the
  game, draws a letterbox with the caption (`WardensCutsceneOverlay.cs`), flies the camera through the
  existing director, and hands the game back in every path. Triggers: a new game, a chapter opening
  (new `ChapterOpened` event on the chapter service), a tutorial finishing; `"cutscenes": true` in
  `settings.json` keeps them on. Skip button; Continue button for shots that wait.
- **The Cold Boot is the first scene** (`Cutscenes/ColdBoot.json`): the three shots of the mockup in
  `design/wardens-ui/ColdBoot.dc.html` (a high orbit around the Core, a push in with the Core's
  highlight coming on, a settle back on the gameplay angle; 22 s, one caption each).
  `WardensColdBoot.cs` and its 14 s orbit are gone.
- **Captions with the day's numbers, choice cards, marks, conditions, highlights, keys.** A shot's
  `args` fill its caption's `{0}`.. from the game (day, cycle, bots, beavers, Data Cores, science,
  any good's stock, a recorded choice or mark); `choices` put a card up and record the pick in the
  story record; `when` plays a shot for one answer only; `mark` records the day; `highlight` tints
  an object without the arrow; the `beaver` anchor; Escape skips, Return or Space continue.
- **The story record** (`WardensStoryState.cs`, `story.json` next to `settings.json`): choices and
  marks, outside any save, one file per mod folder; `cutscene reset` archives it.
- **Prototype content**, eight scenes: one per chapter (`Badwater`, `Signal`, `Pods`, `Power`,
  `Green`, two shots each, the camera restored afterwards), the level's end card (`LevelEnd`:
  *Continue to Level 02* or *Stay*, recorded), and the Archive reading (`Archive`, on request:
  five cards reading the Ledger back). The captions are fixed text in
  `design/wardens-campaign-story.md` §3.
- **MCP:** the `cutscene` tool is `status | list | play id | skip | continue | choose choice | reload |
  reset` (edit the file in the mod folder, reload, play). `wardens_status` gains a `cutscene` block;
  frames carry `cutscene`, the events `cutscene.start:<id>` / `cutscene.end:<id>`, and put `cutscene`
  first in `attention` while a scene plays (a choice card is the human's to answer). `WARDEN.md`: a
  playing scene owns the camera and the Warden's silence; the Archive plays on request.
- **Checks:** `tools/check_cutscenes.py` resolves every name a scene uses (captions and their
  placeholder counts against `args`, chapter and tutorial ids, anchors, choice ids, `when` keys across
  scenes, field types, unknown fields) without the game's files; `validate.py` includes it; the tool
  tests (`tools/test_*.py`) run in the repository's Python workflow.
- **Release path:** `tools/package.py` zips a local Release build into `dist/Wardens-v<version>.zip` (the
  mod folder, the map for `Documents/Timberborn/Maps`, install steps); `thumbnail.png` for the Mod
  Manager (`tools/gen_thumbnail.py`, the bot avatar with the logo badge); the deploy replaces the
  `Cutscenes/` folder whole, like the generated folders.

## 0.2.x (2026-09-03 to 2026-09-05): chapters, the map, the heartbeat, the campaign on paper

- **Chapter gating** (`WardensChapters.cs`): nine buildings ship padlocked and open in five chapters as
  the tutorial line advances (Badwater, Signal, Pods, Power, Green); toast and Uplink line per chapter;
  MCP `chapter` tool; `validate.py` cross-checks the table against the blueprints.
- **The wasteland map** (`Maps/Wardens Wasteland.timber`, `tools/gen_map.py`): a badwater river from the
  north edge, the Sump beside the Core, ruin clusters, one clean spring; installed into
  `Documents/Timberborn/Maps` by the build.
- **The Warden's heartbeat** (`WardensFrames.cs`): a sensor frame per N game ticks or per event for the
  MCP `frame` tool, with `attention` (where to look, in order); the playbook `WARDEN.md`, served by the
  `manual` tool; the stance in `design/wardens-play.md`.
- **Art:** avatars, logo, new-game portrait, beaver skins, the bot skin, banners, carrying-model and
  zipline textures, recolored from Leaf Coats (`tools/recolor_assets.py`, `Sprites/ATTRIBUTION.md`);
  `WardensMaterialPatcher` applies the runtime textures.
- **The campaign, designed:** map research, the campaign system design, the five-level concept, the
  ten-level arc with its research plan, and the story's text (`design/wardens-campaign-*.md`).

## 0.1 (2026-09-03): the faction

- Bots as the starting population (Spike A), four Data needs, Data Core and Firmware goods, the
  buildings re-specced from Iron Teeth (`tools/gen_buildings.py`), the 18-tutorial story line ported
  from Folktails (`tools/gen_tutorial.py`), the vanilla tutorials switched off for the faction.
- The Timberbot API compiled in (`src/Timberbot/`), and an MCP server inside the game
  (`WardensMcpServer.cs`): chat, pointer, camera, tutorial state, the Timberbot passthrough.
- Spike B (`PollutingBuilding.cs`) as a stub: the water route for building-side contamination is
  designed, not built.
