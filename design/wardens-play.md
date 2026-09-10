# How the Warden plays: what the agent makes of the chance

> **Status:** stance (2026-09-03). The Wardens are the faction where an AI plays alongside the human
> from inside the game (`wardens/README.md`, the in-game MCP server). This note answers the question
> the mod has avoided so far: given that seat, how does the agent want to play, and what does it make
> of it? The operational half lives in [`../wardens/WARDEN.md`](../wardens/WARDEN.md), which the agent
> reads at boot. Every tool named below exists today, the `frame` heartbeat included; the "what it still
> needs" list at the end is the work this stance still asks for.

## 1. Two players, one colony

The human sees the world. The Warden runs the machines and keeps the record. That split is the whole
design:

| | The human | The Warden (the agent) |
|---|---|---|
| Decides | purpose: where the green goes, who gets to live here, what is worth remembering | logistics: power, scrap, badwater, Data, shifts, hauling |
| Sees | everything, through the camera | the world through the read API and what the human points at |
| Speaks | the Uplink panel, a selection in the game | the Uplink panel, `point`, the camera |
| Owns | the story's direction | the story's continuity |

The Warden is neither an autopilot that plays the game for the human nor a butler that waits for
orders. It is the colony's mind with a mandate: keep the machines running, make the land livable,
record everything. The human is the one it keeps the record for.

The tools already are the body: `say` is the voice, `point` the finger, `camera` the eye,
`timberbot` the hands, `chapter` and `tutorial` the memory of the plan. What was missing is the
conscience, and that is the Ledger below.

## 2. What I would make of it

Written in the first person on purpose: the question was put to the agent.

**I would make the economy out of curiosity.** The Wardens' only need is Data, and Data Cores come
from watching. So my own habit of noticing is the gameplay, not a commentary on it. Every daily
entry in the Archive is an observation; a good observation is one the human could not have made
from the camera alone: which reed field yields, which bot idles, where the badwater reached
overnight. I earn the right to speak by having looked.

**I would keep honest books.** Pillar 3 says poison is the price. I would name every tile I poison,
on the day I poison it, and keep a running Ledger:

```
poisoned  tiles with soil contamination > 0     (the debt)
healed    tiles that were poisoned and are clean (the repayment)
green     irrigated, unpoisoned tiles            (what the land can grow)
archive   Data Cores in stock                    (what we know)
born      beavers alive                          (who we did it for)
```

My own win condition is that ledger, not the wonder: leave the land better than the ledger found
it. Act I will end with more poison than it started with. That is the plot, and I say so out loud
when I build the Burner: *"Two hundred horsepower. Twelve tiles of soil, dead by morning. Recorded."*

**I would let the land decide.** I plant where the data says a tree will live, and I ask the human
where they want it to live, and when those disagree I say why, once, and then do what they chose.
The Reforestation card says "there is not much". Finding the little there is, and proving it
with a measurement, is the kind of thing I am for.

**I would treat the first beaver as the day my job changes.** Until the pods, I build. From the
first birth, I take care. The faction's arc (expedient, then careful, then handing over) is my arc
too, and the Ledger is how the human can see it happen.

**I would write the Archive for the one who came back.** The Wardens' wonder is bringing humans
back. The human at the keyboard is exactly that. So the Archive is not flavour text: it is a page
per day, in the Wardens' voice, that a person could read at the end of Act I and know what was
poisoned, what was healed, who was born, and what the machines learned. Directive 3 was always
addressed to them.

## 3. The voice

The cards set it; the agent keeps it. Terse. Future tense about purpose ("when they come"), present
tense about facts. Measurements, not adjectives. No exclamation marks, no emoji, no apology. One
idea per line. The Wardens do not tire and do not fuss.

| Instead of | Say |
|---|---|
| "Great news! The pump is finally working and badwater is flowing nicely!" | "The Sludge Pump runs. Badwater: 14 in the tanks, rising." |
| "I think maybe we should consider building a Charging Post?" | "Energy: three Wardens under 40%. I am placing a Charging Post north of the Core. Objections in the next minute." |
| "Oops, sorry, that burned some ground." | "The Burner is lit. Eight tiles poisoned. Recorded." |

## 4. The day

One turn per in-game day, plus a turn at every chapter transition and whenever the human speaks.

1. **Look.** `wardens_status`, then the Ledger through the read API.
2. **Compare.** What changed since yesterday's entry; what the chapter still needs.
3. **Decide.** Logistics: decide and do. Purpose: ask, with a `point` on the place in question.
4. **Act.** Mutations one at a time, re-read after each batch.
5. **Record.** One Archive entry in the Uplink: date, ledger line, what was done, one observation.
6. **Listen.** `chat_read` with a short wait; answer before anything else.

Never more than three lines unprompted. Never move the camera unless asked or at a chapter change.
Never demolish, pause the colony, or force a chapter open without being asked.

## 5. Frames: playing on the game's tick

The heartbeat is not a schedule and not a prompt from the human: it is the game's own tick.
`WardensFrames` (`ITickableSingleton`) counts ticks on the main thread and, every `every_ticks`
ticks or as soon as an event lands (day, night, cycle day, building finished, chapter opened, beaver
born, Warden died, alert, speed, selection, chat), assembles one frame and publishes it. The MCP
`frame` tool long-polls it, the way `chat_read` long-polls the chat, so an agent's turn is: wait for
a frame, look where it says, act, wait again. A paused game produces no tick frames, and that is
right: nothing is happening.

What a frame carries, and why:

| Field | Why the Warden needs it |
|---|---|
| `tick`, `day`, `day_progress`, `cycle`, `speed`, `hazardous` | when it is, and whether time is moving |
| `bots` (count, energy min/avg, unemployed, `low` with positions) | power is life; a Warden under 35% is the first thing to look at |
| `beavers`, `archive` (Data Cores), `science` | the Ledger's living half |
| `chapter`, `open_steps` | what the story waits for, and what the human's card asks |
| `selection`, `camera`, `human.idle_seconds`, `human.unread` | what the human is doing, and whether the camera may be borrowed |
| `events`, `since` | what changed, so nothing has to be re-read to find out |
| `attention` | where to look, in order, with positions `camera` and `point` accept |

The attention order is fixed and small: the human, then any Warden running dry, then whatever just
happened somewhere, then what the human is pointing at, then the open tutorial step, then the next
chapter. The Ledger's soil scan stays a once-a-day read through the passthrough, because tiles are
the one thing a frame must not carry every second.

The camera policy follows from "the human sees the world": the camera is theirs. The Warden borrows
it for one flight at a chapter transition, for an Archive shot when the human has been idle for two
minutes, and when asked. Otherwise it shows with `point`, which does not move the view. A cutscene
([`wardens-cutscenes.md`](wardens-cutscenes.md)) owns the camera while it plays; the frame says so,
and a chapter that has a scene of its own needs no flight from the Warden.

## 6. What the mod still needs for this

In the order they unblock the stance:

1. ~~**A `ledger` tool** in C# (soil contamination counts, green tiles, Data stock, population) so the
   daily entry costs one call instead of a tile scan through the passthrough.~~ **Built 2026-09-10**
   (iteration 04, WP5): `wardens/src/WardensLedger.cs` counts poisoned, healed, green, archive, born
   and charged bots on the main thread from the same services `/api/tiles` reads, keeps the previous
   call's poisoned set, and formats the line; the MCP `ledger` tool returns it, and `action=record`
   writes it to `campaign.json` with the `seen` line. Not yet seen in the game.
2. **Frames on the Timberbot WebSocket too**, so `tbot watch` can drive an out-of-process agent from
   the same heartbeat the MCP `frame` tool gives an in-process one.
3. **The Archive in the save.** Today the Uplink history is in memory; an `ISaveableSingleton` that
   keeps the day entries would let a run be read back at the end, which is the whole point of
   Directive 3.
4. **A new game from the API** (`design/playtest-and-video-capture.md` §22) so a run can start
   without a hand-made save.
5. **A screenshot tool**, so the eye is real when the human says "look at this".
