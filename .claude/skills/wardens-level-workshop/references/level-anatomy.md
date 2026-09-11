# A level's anatomy: the files, their formats, their rules

## Contents

- The level table
- The land
- The tasks (`Levels/<id>.tasks.json`)
- The scenes (`Cutscenes/*.json`)
- Captions and loc rows
- The story's voice

## The level table

`wardens/src/WardensCampaign.cs`, `WardensCampaignService.Levels`: one row per level,
`new WardensLevel(id, mapName, title, endsWithTutorial, next, shipped)`. The map name is the key the
running game finds the level by (`MapNameService.Name`); a map not in the table is "not a campaign
level" and everything story-shaped goes quiet. `wardens/maps/levels.toml` says which spec builds each
level's land; `mapsmith levels --verify` cross-checks the two. `shipped: true` means the `.timber` is in
`wardens/src/Maps/`, and `validate.py` refuses a `.timber` no row claims.

State on 2026-09-11: 01 *First Light* and 02 *The Sump* shipped; 03 *The Pods*, 05 *The Archive*,
09 *The Ark* (map `Wardens 09 The City`), 10 *Home* are rows only. The arc's 04, 06, 07, 08 are not in
the table.

## The land

A spec in `wardens/maps/<name>.map.toml`, built by mapsmith (`timberborn-mapsmith` skill). The spec's
`[contract]` states what the level's decision needs (`single_gorge`, `confluence_upstream`,
`never_touch`, `unreachable`, …); `[checks]` states what the game needs to load it. The walk report
tells you what a Warden reaches on foot and what needs Stairs. Coordinates in tasks, sites and scenes
are grid tiles of the built map: read them from the spec's build, never guess.

## The tasks (`Levels/<id>.tasks.json`)

```json
{ "level": "02",
  "about": "one paragraph for the next person: what the tasks are about and why in this order",
  "tasks": [
    { "id": "Gorge", "after": ["Clean", "Drain"],
      "title": "Wardens.Task.02.Gorge", "text": "Wardens.Task.02.Gorge.Text",
      "checks": [ { "type": "built_in", "templates": ["Levee.Wardens", "Dam.Wardens"],
                    "box": [56, 43, 68, 52], "where": "Wardens.Task.02.Where.Gorge", "count": 3 } ] } ] }
```

- **Graph.** A task goes live when every task in `after` is done; without `after`, when the task before
  it in the file is. At most two tasks live at once (`check_level_tasks.py` enforces it): each live task
  gets its scene and its full block on the panel, and a third makes the level talk over itself.
- **Checks** (all must hold; `count` defaults to 1):

| type | counts | needs |
|---|---|---|
| `built` | finished buildings of `template` | template |
| `powered` | finished, on a network with a generator | template with a MechanicalNode |
| `generating` | a generator actually making power | template that outputs power |
| `workers` | Wardens assigned to finished buildings | a workplace template |
| `stock` | a good in the districts' stock | `good` (construction draws stock down: keep targets low) |
| `beavers` | beavers alive | — |
| `built_in` | finished buildings of any of `templates` inside `box` | `box` [x1,y1,x2,y2], `where` loc key |
| `clean_water` | tiles in `box` with water ≥ 0.2 deep and contamination < 0.05 | `box`, `where` |

- **A check must be true only when the task's point is.** Level 02 found both failure shapes: "Keep it
  clean" passed on the creek as it already ran (83 clean tiles before any gate), and "Hold the gorge"
  passed on three one-block Levees one tile short of the narrows. Before shipping a check, ask what the
  cheapest wrong way to satisfy it is, and move the box or add a second check until that fails.
- **Saving.** Done tasks are saved with the game; a reload ticks through already-met tasks silently.
- Loc rows: `Wardens.Task.<level>.<Id>`, `.Text`, and `Wardens.Task.<level>.Where.<Name>` for boxes.

## The scenes (`Cutscenes/*.json`)

Format: `design/wardens-cutscenes.md` §3 (shots with a camera flight, one caption, optional `highlight`,
`point`, `toast`, `say`, `mark`, `badtide`, `choices`/`when`). The file stem is the scene id.

| Trigger (`on`) | Fires | Shape |
|---|---|---|
| `level:<id>` | a new game on the level | the opening: ≤ 5 shots, ≤ 45 s; the land once, the Core, the directive (`wait: continue`); `pause`, `leave_paused` |
| `task:<level>.<Task>` | the task goes live | `T<level>.<Task>`: ≤ 2 shots, ≤ 14 s, `restore_camera: true`, `leave_paused: false`; only what that task asks |
| `task_done:<level>.<Task>` | the task is done | a moment that follows it (the first beaver: `T01.Born`, `mark: birthday`) |
| `level_complete:<id>` | the last task is done (first time) | `L<id>.End`; the panel's card after it carries the one Continue |
| `new_game` | a new game on a non-campaign map | the Cold Boot |
| `tutorial:<Id>` | a vanilla-style tutorial finishes (tutorial on only) | rarely used |

Task and level triggers play with the tutorial off; `new_game` and `tutorial:` need it on. A loaded save
replays nothing. `test_check_cutscenes.py` holds the shipped scenes to the shape column.

Keyframes: `anchor` `grid` (x, y, z = height), `core`, `beaver`, `bot`, `selection`, `start` (the pose at
scene start), `world`; `h`/`v`/`zoom` absolute, `dh`/`dv`/`dzoom` relative to the start pose; `t` is the
second of the shot the pose is reached at (a first keyframe above 0 eases in). Capture real poses with
`cutscene action=keyframe`.

## Captions and loc rows

`Wardens.Cutscene.<SceneId>.<Shot>` in `wardens/src/Localizations/enUS.csv` (`key,text,comment`; quote a
text with commas; the comment names the scene file and what `{0}`… hold). `args` fill the placeholders
from the game: `day`, `cycle`, `cycle_day`, `bots`, `beavers`, `archive`, `science`, `good:<Id>`,
`choice:<key>`, `mark:<name>`; the checker counts them. A new row shows in the game only after a restart;
while tuning, a literal `"text"` works live.

## The story's voice

The Wardens speak as *we*; the Warden's lines are terse: measurements, not adjectives; present tense for
facts, future for purpose; at most two sentences a caption; no exclamation marks, no emoji. Every number
must be true of the built map or the blueprints. The beavers never speak; the first beaver is never
named. A claim the game contradicts (a Warden at 0 Energy "stops") is a finding against the text.
