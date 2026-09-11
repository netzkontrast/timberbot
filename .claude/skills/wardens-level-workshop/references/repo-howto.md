# Working this repo: the things that each cost a session to learn

Facts as of 2026-09-11 (Wardens 0.4.27, Timberborn 1.1.2.4). When one turns out wrong, fix it here in
the same commit as the discovery.

## Contents

- Machine and paths
- Build and deploy
- Launch a level, load a save
- Talk to the running game
- Checks (offline)
- Generators and where each file comes from
- Shell traps (Bash tool on Windows)
- Git, branches, handover
- Game facts learned the hard way

## Machine and paths

| What | Where |
|---|---|
| Game | `F:\Steam\steamapps\common\Timberborn` (not the csproj default `C:\Games\Steam`) |
| Game DLLs, for decompiling | `F:\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\Timberborn.*.dll` |
| Vanilla blueprints | `…\Timberborn_Data\StreamingAssets\Modding\Blueprints.zip` (the generators read it) |
| Build paths | `wardens/src/Directory.Build.props` (git-ignored, present on the game machine) |
| Deployed mod | `%USERPROFILE%\Documents\Timberborn\Mods\Wardens\` — `campaign.json`, `story.json`, `settings.json`, `Cutscenes\`, `Levels\`, `docs\WARDEN.md` |
| Maps the game lists | `%USERPROFILE%\Documents\Timberborn\Maps\` (the mod installs its maps there at the main menu) |
| Saves | `%USERPROFILE%\Documents\Timberborn\ExperimentalSaves\<settlement>\` on this install (not `Saves\`); campaign settlements are named after the level: `First Light`, `The Sump` |
| Log | `%USERPROFILE%\AppData\LocalLow\Mechanistry\Timberborn\Player.log` (and `Player-prev.log`); `findstr "[Wardens]"` |
| Python | `uv run --project python --extra dev …`; there is no pipx |
| .NET | SDK 10.0.401, so `dotnet test` on the net10 test projects runs here |
| Decompiler | `ilspycmd -p -o <scratch>\Name "…\Managed\Timberborn.Name.dll"`; find the DLL that defines a type with `grep -l TypeName …/Managed/Timberborn.*.dll` |

## Build and deploy

```bash
dotnet build wardens/src/Wardens.csproj -c Release
```

- Every build bumps the patch version (`wardens/tools/bump_version.py` in manifest.json, the csproj and
  `WardensMcpServer.Version`). The author reads the version in the Mod Manager to know which build is
  deployed: say the new number in the recap. Commit the three version files with the change.
- The build deploys into the mod folder and the Maps folder. **Only with the game closed** (check
  `tasklist | findstr Timberborn`): Timberborn loads the DLL once at start but re-reads blueprints on
  every load, so a deploy under a running game gives an old DLL with new specs and `No type found for
  key <Spec>` on the next load. The fix for that error is a full restart, not code.
- Compile without deploying (game running, or just to see errors):
  `-p:ModDir=<scratch>\moddir -p:MapsDir=<scratch>\mapsdir` (still bumps the version).
- The deploy replaces `Cutscenes\`, `Levels\`, `Buildings\` and the other generated folders whole, so
  a scene renamed or removed in the repo disappears from the game too.
- `wardens/src/Wardens.csproj` must not publicize `Timberborn.BlueprintSystem` (records deriving from
  `ComponentSpec` then fail with CS0507), and copies use `%(RelativeDir)`.

## Launch a level, load a save

- New game on a level: write `{"level":"02"}` to `Documents\Timberborn\Mods\Wardens\campaign.handoff.json`,
  start the game. The main menu consumes the file once and starts the level as a new Wardens game.
- Load a save at the main menu: `autoload.json` in the same folder,
  `{"settlement": "First Light", "save": "<file name without .timber>"}` (one-shot).
- The vanilla Mods dialog needs one click at every launch; the handoff runs after it.
- In a game, `campaign action=next` exit-saves and starts the next level (only when the human said so).

## Talk to the running game

- Wardens MCP server: `http://127.0.0.1:8090/mcp` (`.mcp.json` names it `wardens`). If the session's
  MCP connection failed because the game was not running at session start, reconnect it, or drive it
  over HTTP with `python wardens/playtest/mcp_smoke.py` as a template.
- The server's `initialize` instructions list every tool (generated from its table) and THE STORY NOW
  (the live tasks, what each misses, its scene). Prompts: `warden_boot` (the playbook), `warden_level`
  (campaign record), `warden_director` (the scene harness).
- `timberbot` is the passthrough to the compiled-in Timberbot HTTP API (port 8085); it refuses until
  `timberbot_ready` is called once. Useful reads: `/api/tiles?x1=&y1=&x2=&y2=` (heights, water,
  contamination), `/api/buildings`, `/api/alerts`. Writes are POSTs, one at a time.
- Grid coordinates everywhere (Timberbot, mapsmith, scenes, `point`): x, y = the map plane, north up,
  z = height. A frame's `at` is a world position; `at.grid` is the tile.

## Checks (offline)

All must print clean before a push; they need no game except `validate.py` (needs `Blueprints.zip`,
present on the game machine):

```bash
uv run --project python --extra dev pytest -q wardens/tools .claude/skills
python wardens/tools/check_cutscenes.py wardens/src
python wardens/tools/check_level_tasks.py wardens/src
python wardens/tools/validate.py wardens/src          # the argument matters: without it, it checks the deployed copy
python wardens/tools/mapsmith check --level 02        # every level you touched
python wardens/tools/mapsmith levels --verify
python .claude/skills/driving-iterations/scripts/check_doc_drift.py --root . wardens/WARDEN.md .claude/skills/warden-play/SKILL.md design/wardens-play.md
```

`ruff` over `wardens/tools` reports ten findings that predate 2026-09-11; compare against `main`
before calling one yours.

## Generators and where each file comes from

| File | Source | Note |
|---|---|---|
| `wardens/src/Buildings/**`, building loc rows | `wardens/tools/gen_buildings.py` (reads Blueprints.zip) | re-specs Iron Teeth blueprints; ScienceCost 0 on the bar |
| `wardens/src/Tutorials/**`, tutorial loc rows | `wardens/tools/gen_tutorial.py` | the Folktails line ported to bots |
| `wardens/src/Maps/*.timber` | `python wardens/tools/mapsmith build --level <id>` from `wardens/maps/*.map.toml` | never hand-edit a `.timber` |
| `Levels/*.tasks.json`, `Cutscenes/*.json` | hand-written | checked by the two checkers |
| task and cutscene loc rows | hand-written in `Localizations/enUS.csv` | the generators append only rows they own and keep the rest |
| the level table | `WardensCampaign.cs` `Levels`, one `new WardensLevel(...)` per line (parsed by the tools) | mirrored by `wardens/maps/levels.toml` |

The agent contract is five documents that change together: `wardens/WARDEN.md` (the source),
`.claude/skills/warden-play/SKILL.md`, `WardensMcpTools.BuildInstructions`, `design/wardens-play.md`,
and the `warden_boot` prompt (which returns WARDEN.md). The verbatim Timberbot copy in
`wardens/src/Timberbot/` is fixed upstream in `timberbot/src` and re-copied, never edited in place.

## Shell traps (Bash tool on Windows)

- The Bash tool collapses a doubled backslash to one before bash sees it, and a heredoc holding Python
  with `'''` strings can fail with `unexpected EOF`. For any edit script longer than a few lines,
  write the `.py` with the Write tool into the session scratchpad and run it. Same for anything with
  Windows paths or regexes.
- PowerShell here is 5.1: no `&&`, use `;` and `if ($?)`.
- Edit scripts that replace text should assert the old text occurs exactly once; a silent miss is
  worse than a crash.

## Git, branches, handover

- One branch per package, pushed before the HANDOVER entry is written; the entry quotes
  `git ls-remote origin <branch>`. Stacked branches (a package on top of an unmerged one) are normal.
- `gh pr create` for the pull request; commit messages end with the attribution line the session gives.
- State words and the entry template: the `driving-iterations` skill.

## Game facts learned the hard way

- **Start order.** `GameInitializer` runs as an `IUpdatableSingleton` state machine: spawn beavers →
  `NewGameInitializedEvent` → unpause → `ShowPrimaryUIEvent`. The first two happen a few frames into
  play, so anything that must follow a level's opening waits for `ShowPrimaryUIEvent` (posted on a new
  game and a load alike).
- **Wardens walk on one level.** The nav mesh joins a tile only to same-height neighbours; a ruin one
  level below a flag is "Nothing to do in range" until a Stairs stands. mapsmith models it
  (`on_foot_from`, `on_foot_scatter`).
- **Big entities validate block by block on load.** A `BadwaterSource` is 3×3, `UndergroundRuins` a 5×5
  surface object; an overlap or a buried block is deleted with `Can't validate loaded BlockObject`.
- **Water.** Maps can ship pre-filled (`[water] fill`); the game halves `SpecifiedStrength` of sources in
  files without its migration key, and the playtested strengths are the halved ones.
- **Speed.** `SpeedManager.ChangeSpeed` queues for `LateUpdate` and drops the change while a panel or a
  scene holds the speed lock.
- **Bots.** Normal difficulty spawns 13 starting beavers, all replaced by Wardens. A Warden at 0 Energy
  keeps walking; do not write that it "stops where it stands".
- **New Floodgates** stand below the creek's surface (0.6–0.7); nothing pools until they are raised.
- **`placement/find`** misses bank sites for pumps; place those by hand at a known tile.
- **Screenshots** only while Timberborn is the foreground window (`scripts/shot.ps1` checks); the author
  keeps private apps open beside the game.
