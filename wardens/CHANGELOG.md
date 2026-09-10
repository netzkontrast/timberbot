# The Wardens: changelog

The patch number moves with every local build (`tools/bump_version.py`, run by the csproj); a minor
version marks a milestone (`bump_version.py --minor`) and gets an entry here. Nothing below has been
verified in-game yet; `../AGENTS.md`, "The Wardens: state", says what the first run must answer.

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
