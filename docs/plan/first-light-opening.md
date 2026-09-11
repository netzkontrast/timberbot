# Level 01 opens with a cutscene: the todo

> **Goal (the author, 2026-09-11):** the new First Light map starts with an extensive cutscene.
> **Agent:** [`.claude/agents/cutscene-director.md`](../../.claude/agents/cutscene-director.md) connects to
> the running game over the Wardens MCP server (`http://127.0.0.1:8090/mcp`) and tunes the scene live.
> **Branch:** `feat/first-light-opening`, stacked on `feat/level-01-remake` (PR #21).

## Why the Cold Boot is not enough

Seen on the game machine on 2026-09-11 (0.4.9, the remade map loaded by the main-menu handoff):

```
[Wardens] cutscenes: 8 loaded ... triggers=False (wardens=True, setting=True, tutorial=False)
```

- **It does not play.** The trigger policy wants the tutorial on. `DisableTutorial` is the player's
  global game setting (`TutorialSettings`, key `DisableTutorial2022-12-05`), not something the handoff
  sets, so a player who turned the vanilla tutorial off never sees the story's first scene.
- **It does not know the land.** Three shots orbit the Core. The remade map has a story on the
  ground (the badwater river from the north edge, the Sump and its seep, the terraced shore, the first
  ruins, the one spring across the river) and nothing shows it to the player.
- **It miscounts.** The Wake card says "Five Wardens online"; the game started 13 (Normal: 9 adults +
  4 children, all replaced by bots).

## The todo

State words are the driving-iterations skill's: `written`, `checked`, `built`, `tested`, `verified`.

| # | Package | Acceptance (type) | State |
|---|---|---|---|
| 1 | **`level:<Id>` trigger.** `WardensCutsceneScript` accepts `level:<Id>`; the runner fires it on `NewGameInitializedEvent` when the campaign says this map is level `<Id>`. Its policy is Wardens + `"cutscenes": true`, **not** the tutorial: a campaign level's opening is the level, not a tutorial. When a `level:` scene fires, the `new_game` scenes do not (the opening replaces the Cold Boot on that level). | builds (programmatic); log line `cutscene FirstLight: queued by level:01` on a fresh level-01 start with the tutorial off (human/game) | built (0.4.10); verified: `queued by level:01` with tutorial=False, 2026-09-11 |
| 2 | **Checker.** `check_cutscenes.py` accepts `level:<Id>` with an id from the level table in `WardensCampaign.cs`, rejects an unknown id; tests for both. | `pytest wardens/tools` (programmatic) | checked: pytest 139 passed |
| 3 | **The scene.** `Cutscenes/FirstLight.json`: 12 shots, about 100 s, over the land's features in the order the level plays them: the dark plateau with the archived badtides, the river's head, the river across the plateau, the Sump and its seep, the terraced shore, the first ruins, the spring, the crossing, the Core, a Warden, the directive (waits for Continue). Grid anchors from the built map, not guesses. | `check_cutscenes.py wardens/src` → `problems: none` (programmatic) | checked: `problems: none`; played in the game 2026-09-11 |
| 4 | **Captions.** `Wardens.Cutscene.FirstLight.<Shot>` rows in `enUS.csv`, in the Wardens' voice (measurements, not adjectives), every number true of the built map (walk report, spec, blueprints). | checker (programmatic); a read against `mapsmith build --level 01` output (judge) | checked; numbers read against the walk report. Seen in the game: the Core caption printed `System.Object[]` (args bug, fixed in 0.4.11, seen fixed in 0.4.12) |
| 5 | **Live tuning.** The agent plays the scene in the running game (`cutscene reload`, `cutscene play id=FirstLight`), screenshots each shot, fixes framing (zoom, tilt, anchors), copies the file back to `wardens/src/Cutscenes/`. | a screenshot per shot, each showing what its caption names (human/judge) | first pass done: every shot frames its subject (two runs, the second DPI-aware); the Warden close-up's zoom still open |
| 6 | **Build and deploy** with the game closed (a DLL deployed under a running game mixes versions). Version bump. | `dotnet build -c Release` → 0 warnings, 0 errors (programmatic) | built: 0.4.10, 0 warnings, 0 errors |
| 7 | **The real start.** Handoff `{"level":"01"}`, launch; the scene plays by itself with the tutorial off; Skip works; the game is paused afterwards. | `PLAYTEST.md` row (human/game) | verified: played by itself on a handoff start, tutorial off, all 12 shots; Skip not pressed in this run |
| 8 | **Docs.** `design/wardens-cutscenes.md` (the trigger table, the policy, §8 no longer lists level triggers as future), `CHANGELOG`, `PLAYTEST.md` smoke list, HANDOVER entry. | doc read (judge) | written |
| 9 | **The Wake card's count** ("Five Wardens online") no longer states a number the difficulty can change. | loc row (programmatic) | checked: gen_tutorial.py reproduces the row |
| 10 | **The dry river.** The map ships dry and the scene plays at tick 0, so the river and Sump shots show empty beds. Water arrives fast: the Sump stood at 0.5 by day 1, 41 %. | the author's decision (human): pre-fill the map | verified (0.4.13): the Sump reads 1.2 at tick 1, the opening flies over water |

## Out of scope

- Hiding the game's UI during the scene, material swaps (the Core's light), sound: §8 of the cutscene
  design, unchanged.
- A cutscene for level 02 onwards: those maps are not shipped.
- Turning the player's tutorial setting on for campaign starts: it is their setting.

## Open questions

- The zoom scale is still unmeasured in the design (§12). Readings from this session: the camera
  spawned at `ZoomLevel` 0.8; the author's wide view of the plateau was 2.99. The distance is
  `ZoomBase^ZoomLevel * BaseDistance` (decompiled `CameraService`), so the scene uses absolute zooms in
  that range and the live pass (package 5) corrects them.
- The speed stayed at 0 after `speed value=3` with no scene playing (2026-09-11, the author placing
  Stairs). Something else holds the speed lock; not caused by this work, recorded for the playtest.
