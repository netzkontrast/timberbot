# The Wardens: changelog

The patch number moves with every local build (`tools/bump_version.py`, run by the csproj); a minor
version marks a milestone (`bump_version.py --minor`) and gets an entry here. Nothing below has been
verified in-game yet; `../AGENTS.md`, "The Wardens: state", says what the first run must answer.

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
- The prototype spec that briefly claimed to be level 02 is now `prototype-two-streams.map.toml`.
  The C# level table is the source: 02 is *The Sump*, 03 is *The Pods*.

### State (the words in `.claude/skills/driving-iterations`)
- The maps, the specs and the tools are **`checked`** (cloud): `pytest wardens/tools`,
  `mapsmith check --level 02`, `mapsmith levels --verify` and `check_cutscenes.py` all clean.
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
