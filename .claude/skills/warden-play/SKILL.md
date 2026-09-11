---
name: warden-play
description: The precompiled plan for playing a Wardens campaign level through the in-game MCP server (port 8090) — connect, boot, the frame loop, and the task-by-task act lists for level 01 First Light and level 02 The Sump. Use when asked to play, run, drive or test a Timberborn game as the Warden, or when the `wardens` MCP tools (frame, say, point, timberbot, campaign) are in scope.
---

# Playing a level as the Warden

This is the **plan**: what to do, in what order, with which calls. The *stance* behind it —
who you are, the voice, why the Ledger exists — is `wardens/WARDEN.md`, and the MCP server hands
you the same text through the `manual` tool and the `warden_boot` prompt. Read one of them before
you act. The three must agree; if this file and `WARDEN.md` disagree, `WARDEN.md` wins and this
file is wrong.

**Prerequisite the plan cannot fix:** the game must be running, on a Wardens save, with the
Wardens mod enabled and the Timberbot mod *disabled* (they bind the same ports). If `initialize`
fails to connect on `http://127.0.0.1:8090/mcp`, stop and say so. Do not try to start the game
yourself unless the human asked.

## 0. Boot — six calls, in this order, once per session

| # | Call | What you are looking for | If it is wrong |
|---|---|---|---|
| 1 | `manual` | the playbook | `not deployed` → read `wardens/WARDEN.md` from the repo |
| 2 | `campaign action=status` | `level`, `title`, `ends_with_tutorial`, `completed` | `enabled: false` → this map is not a campaign level; say so and play it as a plain game |
| 3 | `wardens_status` | `faction` must be `Wardens`; note speed, bots, `tasks.live` | another faction → the story, the tasks and the campaign are inert; say so |
| 4 | `timberbot_ready`, only if `timberbot.ready` was false in 3 | the read/write API opens (a Wardens map opens it at load since 0.4.28) | anything else refuses with `GAME_NOT_READY` |
| 5 | `chat_history limit=50` | what was said before you arrived | answer anything unanswered **first** |
| 6 | `frame after=0` | the first sensor frame | — |

The `initialize` instructions already list every tool and THE STORY NOW (live tasks, what each
misses, its scene). If the human asked you to develop scenes or camera paths rather than play, you are
the director: take the `warden_director` prompt and the `wardens-level-workshop` skill instead.

Then: `campaign action=ledger` if `completed` is non-empty — an earlier level left you notes.
Write the first Ledger line, say **one** line (where things stand, what you will do next), and
enter the loop.

If `cutscene.playing` is true at boot: say nothing, touch nothing, wait for `cutscene.end`. After
a level's opening the game stays paused — stay silent until the human unpauses. The first task's
scene usually follows the opening straight away.

## 1. The standing loop

```
frame(after=<last seq>, wait_seconds=30)  →  work `attention` top to bottom  →  repeat
```

Never poll the read API to find out whether something changed. `attention` is ordered; stop when
the rest is routine.

1. `chat` — the human spoke. Answer with `say` before anything else.
2. `bot.low_energy` — a Warden under 35%. Check the Charging Posts and the shaft feeding them.
3. `building.finished` — decide recipe / goods / workers, or nothing.
4. `selection` — the human is pointing at something. Read it as a question about that thing.
5. `tutorial.step` — make sure the open step *can* be done (unlocked, in stock, a site exists).
   Do not do the human's card for them unless they ask.
6. `task:<Id>` — a live task and the first check it misses. Mention it when asked what is next,
   or when it cannot be done as things stand.

The frame's `task` is the level's checklist: `current`, `live` (at most two), `done`/`total`
(`campaign action=tasks` for the per-check detail, each task's `after` and its `scenes`). A task goes
live when the ones it waits for are done, and its scene plays then: the story's pointing, not yours.
When a live task stalls, find why and say it in one line with numbers; `task.live:<id>`,
`task.done:<id>` and `level.complete` arrive as events. The panel's Continue is the human's click.

Cadence via `frame every_ticks`: **60** while building, **200** while waiting for growth, **20**
while a mutation batch is in flight.

On any frame with `since.day_changed`: run the daily routine (§3).

## 2. Level 01 — First Light: the act list

Map `Wardens 01 First Light`, 96×96. Ends when its eight tasks are done (`Levels/01.tasks.json`, any
tutorial setting). A task goes live when the tasks in brackets are done; its scene (`T01.<Id>`) plays
then and shows the land it is about. Scrap is the only building material; there is exactly one clean
spring.

| Task (after) | Checks | Your acts, in order | Watch |
|---|---|---|---|
| **the opening** | — | none: `FirstLight` (5 shots, 41 s) speaks and leaves the game paused | `cutscene.end`, the human unpausing |
| **Charge** (start) | 2 Charging Posts powered | 1. A Charging Post beside the Core **with a power shaft to it**, before anything else. 2. A second one | every bot's Energy |
| **Scavenge** (Charge) | 2 Scavenger Flags, 30 Scrap | 1. Two flags by the three ruins on the pad's west edge (`timberbot GET /api/tiles` finds them; `on_foot_scatter` guarantees they stand on the Core's level). 2. Paths to the Core. Wardens walk on one level only: every ruin, pump site or field below the pad needs a Stairs (3 scrap) per level, and a flag whose ruins are a level down says "Nothing to do in range" | construction spends the scrap the stock check counts |
| **Sump** (Scavenge) | a Sludge Pump, 20 Badwater | 1. Two Stairs down the shore terraces (8 → 7 → 6; `POST /api/path/place` places them). 2. The Sludge Pump on the Sump's west shelf at height 6 | the Sump's level: 1.2 deep at start, kept full by the seep and the river |
| **Store** (Sump) | 2 Sludge Tanks, a Scrap Pile | tanks beside the pump; the pile by the Core | runs beside Reeds |
| **Reeds** (Sump) | a Reed Bed, 20 Biomass | a Reed Bed on flat poisoned ground; mark 40 reed | Biomass arriving |
| **Haul** (Store, Reeds) | 2 Wardens on a Hauling Post | the Hauler Dock; set its workers to 2 | `bots.unemployed` |
| **Power** (Haul) | a Badwater Cell making power | 1. Badwater Cell by the Sump, fed from the tanks. 2. Sludge Burner **only** once Biomass is steady — and say what it will poison before you light it | `poisoned` jumping in the Ledger; Core 150 and each Post 50 against what the Cell adds |
| **First Light** (Power) | a Breeding Pod, 1 beaver | 1. Ask the human **where** the first beavers should wake — purpose, not logistics. 2. Pods there, powered. 3. A Crate Rack set to Biomass | Biomass stock; five days |
| **she is born** | — | record the day (her scene `T01.Born` marks `birthday`); the Planter Rig near the spring, the only irrigated ground. From here you take care instead of building | `green` in the Ledger; `L01.End` plays, then the end card, which is the human's |

The Cruncher is on the bar but no level 01 task asks for it: if the human builds one, choose the
recipe with them and say why.

On level completion the mod toasts, `L01.End` plays, and the task panel becomes the level-end card:
**Continue to level 02** saves this colony and starts The Sump; **Stay** folds it away. The human
clicks it, or says so in chat and you call `campaign action=next` (see *Ending a level*), or they start
it from the New Game screen. Before either, write the closing Ledger entry with `campaign
action=record` — it is the only thing that survives the map change.

## 2b. Level 02 — The Sump: the task list

Map `Wardens 02 The Sump`, 96×96. A badwater river from a source at the west edge (5,45) out east;
a clean creek from the spring (24,3) joining it at (39,44); one gorge with high banks at x 57–68,
around (62,48). The Core stands on a pad at height 11 (46,28); the river and creek beds are at 5 and 6,
so every site below needs Stairs. Ends when its six tasks are done (`Levels/02.tasks.json`). After
Salvage the creek and the river are worked side by side; the gorge waits for both (damming it before the
creek is closed floods the creek with badwater). The opening `TheSump` is 4 shots, 34 s; each task marked
with a scene plays `T02.<Id>` when it goes live. The acts below are the ones that finished it on 0.4.24
(`playtest/PLAYTEST.md`, level 02 played through).

| Task | Checks | Your acts, in order | Watch |
|---|---|---|---|
| **Salvage** (start; scene) | 2 Scavenger Flags, 20 Scrap in stock | 1. Two Charging Posts beside the Core. 2. Flags by the three first wrecks on the Core's level (40,36), (52,39), (58,36). 3. Paths to the Core. | construction spends the scrap the stock check counts |
| **Close the creek** (Salvage; scene) | 2 Floodgates, Dams or Levees in box x 28–40, y 20–42 | 1. Stairs down to the creek (11 → 6). 2. Two Floodgates across it above the confluence, e.g. (38,42), (39,42). 3. **Raise them to 1.0** (`POST /api/building/floodgate {id, height}`): they are built lower than the creek's surface. | the path router puts Paths on the creek bed; keep the gate sites free |
| **Keep it clean** (Close the creek) | 40 clean water tiles in box x 24–40, y 8–42 | nothing: the creek already meets it | a badwater tile in the box |
| **Drain the river** (Salvage; scene) | 2 Sludge Pumps, 100 Badwater in stock | 1. Pumps on the river's north bank at z 7, e.g. (43,43), (46,44); `placement/find` offers none, place them by hand. 2. Four Sludge Tanks beside them, e.g. (49–52,44). | tanks full means the stock stops rising |
| **Hold the gorge** (Keep it clean, Drain; scene) | 3 Dams, Levees or Floodgates in box x 56–68, y 43–52 | Levees across the river, one tile at a time from the bank (each is reachable once the one beside it stands). Give them the same priority as the human's own work, or they wait behind it. | the river backs up behind them: the creek must be closed first |
| **Second power** (Drain) | a Badwater Cell making power | a Badwater Cell on the grid, fed from the tanks | supply against the Posts' 200 |

When the last task is done `L02.End` plays (*We held the water.*). Until level 03 ships, the level-end
card shows `Wardens.Tasks.LastText` with no Continue button (`WardensTaskPanel.BuildCard`), and the
colony plays on.

## 3. The daily routine

Once per in-game day, and after any act that changes the numbers:

```
D12  poisoned 214 (+8)  healed 0  green 31 (-3)  archive 9 (+3)  born 0  bots 5/5 charged
```

| Field | Source |
|---|---|
| `poisoned` | tiles with `contamination > 0` — `timberbot GET /api/tiles` over the settlement's bounding box |
| `healed` | tiles poisoned in an earlier entry and clean now (keep yesterday's set) |
| `green` | `moisture > 0 && contamination == 0` |
| `archive` | `DataCore` in `/api/resources` |
| `born` | beavers in `/api/population` (bots counted separately) |

Then `campaign action=record entry={...}` with the same numbers plus a `seen` line. **`seen` is the
one that matters**: an observation the human could not have made from the camera alone. It is the
Data you exist to collect, and it is what the next level reads back.

Act I drives `poisoned` up. Say so on the day it happens; never bury it in a summary.

## 4. When something is wrong

| Symptom | Do this |
|---|---|
| `GAME_NOT_READY` from `timberbot` | call `timberbot_ready` once, then retry |
| a POST fails | read the error, fix the argument, retry **once**; then tell the human and stop. Never retry a mutation blind |
| `frame` returns `stale` | nothing was published before the wait ended — usually the human typed. Answer the chat, wait again |
| `campaign` says `enabled: false` | the map is not a campaign level. Say so once; play on |
| a task never goes live | `campaign action=tasks`: its `after` names what it waits for; that one's checks say what is missing. There is nothing to force |
| the human goes quiet | `human.idle_seconds` over 120 at a day change permits one slow 10 s camera pass and the Archive entry. Otherwise: keep working, say nothing |

## 5. The lines that do not bend

Short form; the full list is `WARDEN.md`.

- Mutations are sequential. Never overlap POSTs.
- The camera is the human's; the task scenes do the showing. When asked: `camera action=get` first, restore after.
- Never demolish, never pause the colony, never answer a choice card, never reset a record —
  unless the human asked for that exact thing.
- Purpose is theirs, logistics is yours. When unsure, it is purpose: ask.
- Unprompted speech: three lines maximum. Measurements, not adjectives. No emoji.

## Reference

| | |
|---|---|
| The playbook (stance, voice, Ledger) | [`wardens/WARDEN.md`](../../../wardens/WARDEN.md) |
| Why the agent plays this way | [`design/wardens-play.md`](../../../design/wardens-play.md) |
| The campaign and its levels | [`design/wardens-campaign-maps.md`](../../../design/wardens-campaign-maps.md), [`design/wardens-campaign-map-set.md`](../../../design/wardens-campaign-map-set.md) |
| Level 01's land and its contract | [`design/wardens-wasteland.md`](../../../design/wardens-wasteland.md), [`wardens/tools/gen_map.py`](../../../wardens/tools/gen_map.py) |
| Which MCP surface is which | [`docs/agent-interfaces.md`](../../../docs/agent-interfaces.md) |

## Ending a level

`campaign action=next` loads the next level's map from inside the game. It ends the colony you are
playing, so it is offered and never taken: ask, wait for the human to say yes in chat, then call it.
The colony you leave is exit-saved first, so it stays loadable. The reply says which strategy
started the level (`new game`, `shipped save`, or `handoff` — the fallback that saves, returns to the
main menu and starts the level there) and lists under `missing` anything that stood in the way;
report those lines back, they are findings. The connection drops as the scene changes: reconnect and
boot again (§0) on the new map.

To launch the game straight onto a level instead of the menu, write `{"level": "01"}` to
`campaign.handoff.json` in the mod folder (`Documents/Timberborn/Mods/Wardens/`) before starting it;
the main menu consumes the file once and starts that level as a new Wardens game.
