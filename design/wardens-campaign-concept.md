# The Wardens campaign: initial concept

> **Status:** concept (2026-09-05), first draft. The system that runs it is
> [`wardens-campaign-design.md`](wardens-campaign-design.md); what the game can and cannot do about maps is
> [`wardens-campaign-maps.md`](wardens-campaign-maps.md). The arc it follows is [`faction-wardens.md`](faction-wardens.md)
> §1 (Cold Start → The Pods → Green → The Ark); the voice is [`wardens-play.md`](wardens-play.md). Level 01 is
> the mod as it exists today; every later level names what has to be built for it.

## 1. Premise

You do not play the survivors. You play the machines that made survival possible.

Five bots wake in a poisoned basin with one instruction in their firmware: *make this land livable for
someone who is not you.* They run on badwater, they poison the ground with every building they raise,
and they keep a record of it. The campaign is that record: five levels, five maps, one ledger that
follows the Wardens from the wasteland to the door of the Ark. The player decides where the green goes
and who gets to live there; the Warden, the AI beside them, keeps the machines running and the books
honest.

The campaign's spine is the Ledger from `wardens-play.md`: **poisoned, healed, green, archive, born.**
Every level ends by writing a line into it, and the last level reads it back.

## 2. The shape

| Level | Title | Act | Map | Population at start | Ends when | Ledger line |
|---|---|---|---|---|---|---|
| 01 | **First Light** | I | Wardens Wasteland (today's map, renamed `Wardens 01 First Light`) | 5 bots | the first pod-born beaver (`Wardens.MoreBeavers`) | *poisoned* goes up, *born* becomes 1 |
| 02 | **The Sump** | I → II | a river basin: the badwater river is the only water, one clean spring far upstream | 6 bots + 1 beaver (carried) | a dam holds clean water through a badtide and 5 beavers are alive | *healed* is first written |
| 03 | **The Pods** | II | a lake with contaminated shores and a green island | 6 bots + 4 beavers | 20 beavers, contamination on the island at zero for a full cycle | *born* ≥ 20, *healed* > *poisoned* for the first time |
| 04 | **Green** | III | a wide valley, fertile once irrigated, ruins of a human town in the middle | 8 bots + 12 beavers | the Field Study line delivers the Ark's Data quota (archive ≥ N) | *green* is the largest number on the sheet |
| 05 | **The Ark** | Wonder | the human city: a plateau of ruins with the Ark's foundation, badwater under everything | carried | the Ark is complete (`GameWonderCompletion`) | the Ledger is read back in full |

Five is a first draft; the design supports any count. If the Act II tech (remediation, field studies)
takes longer than the maps, levels 02 and 03 merge, and 04 and 05 stay.

## 3. The levels

### Level 01 — First Light (exists)

- **Map.** `Wardens Wasteland` as shipped: badwater from the north, the Sump beside the Core, ruins in
  scavenging range, one clean spring in the north-east ("there is not much").
- **Opening.** Cold Boot: the paused orbit, three cards, the Core's light comes on.
- **Play.** The 18-tutorial line and the five chapters (Badwater, Signal, Pods, Power, Green) exactly as
  `wardens/README.md` describes them. Scrap is the only building material; power is life.
- **Ends.** The first beaver is born from the Breeding Pod (`Wardens.MoreBeavers`). The Warden's line:
  *"One. Born here, in this. Everything we build from now on is for her. Recorded."*
- **Carries over.** Bot count, the one beaver, the Ledger, the last twenty Uplink lines.
- **Complete card.** *"Level 01 complete. The land here is spent; the badwater will not last another
  season and the beaver cannot drink it. The Core has a bearing on a river to the east. Level 02: The
  Sump."*

### Level 02 — The Sump

- **Map (`gen_map.py --level 02`).** 96×96. A badwater river enters from the west and leaves east
  through a narrow gorge, the natural dam site. Far upstream, past three ruin fields, one clean spring
  feeds a side creek that joins the river above the gorge: dam the gorge and the reservoir is clean
  only if the badwater is diverted first. Contaminated soil along the whole river; a green terrace
  around the spring.
- **Opening.** No orbit; a single card over the gorge: *"Water that a beaver can drink. Behind it,
  water that will kill her. Choose which one you keep."*
- **Play.** Chapters open the Dam (already free), the Floodgate, the water pump for clean water, the
  first remediation building (the Soil Barrier from `Timberborn.SoilBarrierSystem`). New tutorials:
  Diversion, Clean Water, First Barrier. Badtide pressure: the level's weather preset makes the first
  badtide arrive early (a per-level `NewGameMode` override is a §8 extension of the design; v1 uses
  the map's sources instead: a badwater source that strengthens after cycle 3).
- **Ends.** Five beavers alive and the reservoir clean through one badtide (a `GoodStockStep`-style
  custom step over the contamination service, the family `WardensTutorialSteps.cs` already has).
- **Ledger.** *healed* is written for the first time: tiles that were poisoned in this level and are
  clean at its end.
- **Complete card.** *"We held the water. Downstream, the Core reads a lake. Level 03: The Pods."*

### Level 03 — The Pods

- **Map.** A lake fills the middle; its shores are contaminated (the badwater reaches the lake through
  two inlets), an island in the lake is green and reachable only by a dam-and-platform chain (the
  Vertical architecture tutorial finally matters). Ruins under the water for underground mines.
- **Opening.** Card over the island: *"Twenty. The pods can make them. The land has to keep them."*
- **Play.** Breeding Pods at scale, the Advanced Pod, healthcare, the first field study on the
  island. The contamination sensor (Signal chapter of level 01) becomes the level's instrument: the
  Warden reads it every day and says where the poison moved.
- **Ends.** Twenty beavers, island contamination at zero for one full cycle.
- **Ledger.** *healed* exceeds *poisoned* for the first time. The Warden marks the day.
- **Complete card.** *"The books balance. For the first time, we have given back more than we took.
  Level 04: Green."*

### Level 04 — Green

- **Map.** 128×128 (the design's map installer does not care about size; the generator gains a
  `--size 128` preset). A wide valley, dry but not poisoned, a human town's ruins at its centre, one
  river along the north edge. Everything can be green if irrigated; nothing is at the start.
- **Opening.** Card over the ruins: *"They lived here. Find out how."*
- **Play.** Act III: beavers dominant, bots as labor. Field Studies consume crops and berries and
  produce the Data the Ark needs (`faction-wardens.md` §2, "Act III Data gating"). Irrigation at scale,
  the Observation Deck, the Data economy as the level's currency.
- **Ends.** The archive reaches the Ark's quota (Data Cores in stock ≥ N, a `GoodStockStep`).
- **Ledger.** *green* is the largest number.
- **Complete card.** *"Enough is known. The Core has the coordinates it woke up with. Level 05: The Ark."*

### Level 05 — The Ark

- **Map.** The human city: a plateau of dense ruins (the scrap is endless), the Ark's foundation as a
  pre-placed starting structure beside the Core (a `StartingLocation`-adjacent entity the generator
  places), badwater seeping from underground sources so the ground poisons itself unless barriers hold.
- **Opening.** The orbit again, the same three shots as Cold Boot, over a city instead of ash. Card:
  *"We were built so that others could live here. Others live here. Finish it."*
- **Play.** The wonder (`FactionWonderSpec`) consumes Data Cores; the level is an endgame of logistics:
  keep the barriers up, keep the beavers fed, feed the Ark.
- **Ends.** Wonder complete. The epilogue reads the Ledger back level by level, the Warden's
  Archive entries first, in the Uplink panel, and the final card carries the totals.
- **After.** *Continue* becomes *Start again*; the history stays in `campaign.json` under previous runs.

## 4. The Warden's part

The campaign is where the agent's stance (`wardens-play.md`) gets its arc:

- **Level 01:** the builder. Keeps the machines charged, names every tile it poisons.
- **Level 02:** the engineer. Reads the water; the Dam decision is the player's, the numbers are the Warden's.
- **Level 03:** the observer. The contamination sensor is its eye; the daily entry is where the poison went.
- **Level 04:** the archivist. Field studies are literally its job; the Data quota is its own goal.
- **Level 05:** the witness. It reads the Ledger back; it does not build the Ark, the beavers do.

Rules that hold in every level: answer chat first; offer the transition, never force it; the camera
only as the playbook allows; the Ledger entry every day. The `campaign` tool gives it the level, the
ending condition and the carry-over so it can say what it knows without being told.

## 5. What each level needs built (beyond the maps)

| Level | Blueprints | C# | Tutorials | Art |
|---|---|---|---|---|
| 01 | done | done | done (18) | done (2D) |
| 02 | Floodgate, clean-water pump, Soil Barrier in the Wardens collection; loc cards | contamination-through-badtide step; level start with 1 beaver | Diversion, Clean Water, First Barrier | none new |
| 03 | Advanced Pod (exists, gated), healthcare, first Field Study building | beaver count + contamination-at-zero step | Island, Pods at Scale, The Sensor | Field Study recolor |
| 04 | Field Study line, Observation Deck (exists), Data recipes | none expected | Irrigation, Field Studies, The Quota | none new |
| 05 | The Ark (`FactionWonderSpec`), underground badwater sources | wonder-complete level end (`GameWonderCompletion` event) | The Ark | the Ark model (the one 3D asset the whole mod needs) |

## 6. Open questions

- Level 02's premise depends on beavers drinking clean water while bots run on badwater: is the
  pod-born beaver's water need vanilla (yes in design, verify in-game with the level 01 build).
- Whether the badtide can be scheduled per map (a source that strengthens on a cycle is a generator
  trick; a real weather preset per level is the design's §8 extension).
- The Ark's model: the only asset the campaign cannot recolor from Iron Teeth.
- Five levels at 30–40 in-game days each is a 6–8 hour campaign; is that the target, or should
  levels 02 and 03 merge for a first release?
