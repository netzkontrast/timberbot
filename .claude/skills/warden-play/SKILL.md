---
name: warden-play
description: The precompiled plan for playing a Wardens campaign level through the in-game MCP server (port 8090) — connect, boot, the frame loop, and the chapter-by-chapter act list for level 01 First Light. Use when asked to play, run, drive or test a Timberborn game as the Warden, or when the `wardens` MCP tools (frame, say, point, timberbot, campaign) are in scope.
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
| 3 | `wardens_status` | `faction` must be `Wardens`; note speed, bots, chapter | another faction → the story, chapters and campaign are inert; say so |
| 4 | `timberbot_ready` | the read/write API opens | anything else refuses with `GAME_NOT_READY` |
| 5 | `chat_history limit=50` | what was said before you arrived | answer anything unanswered **first** |
| 6 | `frame after=0` | the first sensor frame | — |

Then: `campaign action=ledger` if `completed` is non-empty — an earlier level left you notes.
Write the first Ledger line, say **one** line (where things stand, what you will do next), and
enter the loop.

If `cutscene.playing` is true at boot: say nothing, touch nothing, wait for `cutscene.end`. After
the Cold Boot the game stays paused with the cards up — stay silent until the human unpauses.

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
6. `chapter.next` — mention only when asked what is next.

Cadence via `frame every_ticks`: **60** while building, **200** while waiting for growth, **20**
while a mutation batch is in flight.

On any frame with `since.day_changed`: run the daily routine (§3).

## 2. Level 01 — First Light: the act list

Map `Wardens 01 First Light`, 96×96. Ends when `Wardens.MoreBeavers` finishes (the first pod-born
beaver). Scrap is the only building material; power is life; there is exactly one clean spring.

Each phase: **enter** when the condition holds, do the acts in order, **leave** when the exit holds.

| Chapter | Enter | Your acts, in order | Exit | Watch |
|---|---|---|---|---|
| **Cold Boot** | session start | none — the scene and the cards speak | `cutscene.end`, human unpauses | — |
| **First Light** | after Cold Boot | 1. Charging Post beside the Core **with a power shaft to it**, before anything else. 2. Two Scavenger Flags on the near ruin cluster (`timberbot GET /api/tiles` finds them; the contract guarantees ≥ 5 columns within 16 tiles). 3. Paths from the flags to the Core. | scrap stock ≥ 10 | every bot's Energy; a bot under charge stops where it stands |
| **Badwater** | chapter `Badwater` opens (`Wardens.Scrap` done) | 1. Sludge Pump on the Sump — the basin beside the pad, ≥ 40 cells at bed height. 2. Sludge Tanks next to it. 3. Reed Bed on flat poisoned ground; mark 40 reed. | Biomass arriving | the Sump's level: the map ships it filled (1.2 deep, since 0.4.13), the seep and the river keep it there |
| **Signal** | chapter `Signal` opens | 1. Cruncher, powered from the Core. 2. Choose the recipe and **say why**: Science Points to unlock, or Data Cores to feed Firmware and the Archive. | the recipe is running | the power budget: Core 150, Post 50, Cruncher 120. It does not add up. That is the chapter — say so rather than quietly browning out |
| **Pods** | chapter `Pods` opens | 1. Ask the human **where** the first beavers should wake — this is purpose, not logistics. 2. Two Breeding Pods there. 3. Crate Rack set to Biomass. | pods powered and stocked | Biomass stock; the pods' draw against the same budget |
| **Power** | chapter `Power` opens | 1. Badwater Cell on the Sump. 2. Sludge Burner **only** once Biomass is steady — and say what it will poison before you light it. | power holds through a night | `poisoned` jumping in the Ledger. Name it the day it happens |
| **Green** | chapter `Green` opens | 1. The first beaver is born: record the day. 2. Planter Rig where the data says trees live — near the spring, the only irrigated ground. 3. From here you take care instead of building. | `Wardens.MoreBeavers` finishes → **level complete** | `green` in the Ledger; the end card is the human's |

On level completion the mod toasts and says which map is next. The human either says so in chat and
you call `campaign action=next` (see *Ending a level*), or they start it from the New Game screen.
Before either, write the closing Ledger entry with `campaign action=record` — it is the only thing
that survives the map change.

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
| a chapter will not open | check `chapter action=status` for its gating tutorial. **Never** `chapter action=unlock` to move things along |
| the human goes quiet | `human.idle_seconds` over 120 at a day change permits one slow 10 s camera pass and the Archive entry. Otherwise: keep working, say nothing |

## 5. The lines that do not bend

Short form; the full list is `WARDEN.md`.

- Mutations are sequential. Never overlap POSTs.
- The camera is the human's. One flight per chapter transition, `camera action=get` first, restore after.
- Never demolish, never pause the colony, never force a chapter, never answer a choice card, never
  reset a record — unless the human asked for that exact thing.
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
