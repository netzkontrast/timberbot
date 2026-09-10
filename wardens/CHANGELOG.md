# The Wardens: changelog

The patch number moves with every local build (`tools/bump_version.py`, run by the csproj); a minor
version marks a milestone (`bump_version.py --minor`) and gets an entry here. Nothing below has been
verified in-game yet; `../AGENTS.md`, "The Wardens: state", says what the first run must answer.

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

- **Iteration 04, the cloud half, is written; the game half is open** (2026-09-10, evening; the plan is
  `docs/plan/iteration-04-first-light-verified.md`, the record `docs/plan/HANDOVER.md`). Written and not yet
  compiled against the game: the `timberbot` passthrough keeps a query carried in `path`
  (`WardensPure.BuildLoopbackUrl`, unit-tested in `wardens/test/`, the first C# tests for this mod; three of the
  playtest's four API findings were that one line), the MCP listener handles each request on a pool thread
  (a `frame` long-poll no longer blocks every other call; `playtest/mcp_concurrency.py` is the check), and the
  `ledger` tool (`WardensLedger.cs`: the daily line in one main-thread call, `action=record` writes it to
  `campaign.json`; `WARDEN.md`, the skill, the instructions and the stance updated together). Done outright: the
  level numbering (02 is *The Sump*; the spec that claimed 02 is now `maps/wardens-proto-crater.map.toml`) and
  level 10 *Home* in `gen_map.py` as an unshipped regeneration pass over level 01. Still open, and the point of
  the iteration: a new game on `Wardens 01 First Light`, played through, with the run recorded.

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
