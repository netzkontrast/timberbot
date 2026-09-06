# The Wardens campaign: the research plan (die Recherche)

> **Status:** plan (2026-09-05). Everything the full arc in [`wardens-campaign-arc.md`](wardens-campaign-arc.md)
> still needs to know before it can be built and told, organised as tracks, each with the questions, the
> method, the sources, the output document and the decision it unblocks. The map-selection research
> ([`wardens-campaign-maps.md`](wardens-campaign-maps.md)) is track D's first entry; the system design
> ([`wardens-campaign-design.md`](wardens-campaign-design.md)) is where technical answers land.

## 0. How the research is run

- **One question, one row.** Every row names its output file and the decision it unblocks. A row
  with no decision behind it is cut.
- **Evidence classes**, in the order we trust them: the game's own files (decompile, `Blueprints.zip`,
  the localization CSV, `dump_assets`), an in-game test, a published mod's source, official
  documentation, community writing, memory. Every finding carries its class.
- **Where findings go.** Technical: `wardens-campaign-design.md` (§12 open questions become
  decisions) and the verified table in `wardens-campaign-maps.md`. Narrative: the canon sheet and the
  ecology notes (new files below). Tuning: the per-level sheet in `playtest/`.
- **The assumption register** (§9) is the checklist; a sprint is done when its rows are marked.

## Track A: canon and lore (what the story may say)

| Question | Method | Sources | Output | Unblocks |
|---|---|---|---|---|
| What does the game itself say about humans, the ruins, badwater, the beavers' origin? | Read every vanilla loc string that touches lore: wonders, ruins, tutorial cards, faction descriptions, map descriptions. Grep the game's `enUS.csv` (in the install) for "human", "ancient", "ruin", "badwater", "before". | game localization file (class 1); [Custom Maps / lore pages on the wiki](https://timberborn.wiki.gg); community readings ([lore thread](https://steamcommunity.com/app/1062090/discussions/3/6597293740381548718/), [TV Tropes](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/Timberborn)) (class 4–5) | `design/wardens-canon.md`: one page of what is stated, what is implied, what is open; a **do-not-contradict list** | arc §1 row 1; every card |
| Do the wonders' completion texts imply anything about humans returning or the beavers' future? | read the two factions' `FactionWonderSpec` loc keys | loc file | canon sheet | the Ark's two completion texts |
| What are the ruins made of, and what do they yield in 1.1? | blueprint dump (`dump_assets`), `RuinColumnH*`, `UndergroundRuins` | `Blueprints.zip` | canon sheet + `wardens-wasteland.md` open question 2 | levels 05, 09 |
| How do Folktails and Iron Teeth describe themselves? | faction descriptions, tutorial voice | loc file | canon sheet | level 07's two districts |

## Track B: beaver ecology (the thesis has to be true)

| Question | Method | Sources | Output | Unblocks |
|---|---|---|---|---|
| What do beaver dams do to a landscape: water table, drought resilience, fire breaks, sediment, pollution filtering? | literature pass; one popular synthesis plus primary reviews | Goldfarb, *Eager* (2018); reintroduction reports (Scotland's Knapdale trial, English river trials); hydrology reviews on beaver meadows; to be listed with citations in the notes | `design/wardens-ecology-notes.md`: ten facts, each with a citation and the mechanic it maps to | arc §1 thesis row; levels 02, 04, 06 |
| Which of those the game's water and soil simulation already models (moisture radius, evaporation, contamination decay)? | read `SoilMoistureSimulator`, `WaterEvaporationMap`, `SoilContaminationSimulator` behaviour in the decompile; in-game measurement through `/api/tiles` | decompile (class 1), Timberbot API runs (class 2) | ecology notes, "modelled / not modelled" column | which facts become mechanics and which become cards |
| Where beavers are a problem (invasive populations, flooded infrastructure)? | same pass | Tierra del Fuego literature | ecology notes | level 04's "the machines' plan fails" and level 08's dispersal tone |
| The one-line version of each fact in the Warden's voice | write and register-test | `wardens-play.md` §3 | card drafts | cards |

## Track C: engine capability per act (what vanilla can do without patches)

| Question | Method | Output | Unblocks |
|---|---|---|---|
| Can contamination kill beavers at a tunable rate, and does the Soil Barrier stop it? (Act II) | decompile `BeaverContaminationSystem`, `SoilBarrierSystem`; in-game test on the level 01 map with the Sump full | capability matrix `design/wardens-capability-matrix.md` | levels 03, 04 |
| Can a badtide be made to arrive on a schedule per map? Is a strengthening `BadwaterSource` enough? | decompile `HazardousWeatherSystem`; generator test | matrix | level 04, level 02 |
| Drought length per map without a difficulty preset? | same; `NewGameMode` per level is the design's §8 extension | matrix | level 06 |
| Can terrain be tinted per map (ash, sand, silt) through the material patcher path that already recolors the bot? | `WardensMaterialPatcher` precedent; terrain material names via `dump_assets materials` | matrix + `wardens-art-path.md` note | the look of levels 04, 06, 07 |
| Map size limits (128, 256) and the water simulation's cost at speed 3 | in-game test, the "big maps unplayable at high speed" thread as the warning | matrix | level 06's 128×128, level 08 |
| Can bots be decommissioned gracefully (deconstruct the post, let them run down) and does the game handle a colony with zero bots and no beavers dying? | in-game test | matrix | level 10 |
| Does the district system carry a "style" (which buildings a district may build) without code? | decompile `GameDistricts`, `BuildingAvailability` | matrix | level 07 |
| Can the pod's beaver factory spawn at level start (Act II+ start state)? | decompile the pod's spawn call | matrix + design §12 | design §3.3 |
| A verified "add goods to a stockpile" call? | Timberbot inventory write path | matrix | design §3.3 |

## Track D: campaign infrastructure (the APIs)

The checklist in [`wardens-campaign-maps.md`](wardens-campaign-maps.md) §5, unchanged: `MapRepository`'s
directories and list method, `MapFileReference`'s eight fields, `NewGameConfiguration` and
`GameSceneLoader`'s start method and binding context, `NewGameModePanel`'s second parameter,
`MapNameService`, `ValidatingGameLoader` in the Game context, `UserDataFolder.Folder` under Proton.
Method: the decompile (`ilspycmd`, `docs/devenv.md`). Output: the verified table extended, design §4.4's
strategy order decided. Unblocks: the transition, the *Continue campaign* button.

## Track E: precedents (how other campaigns solve the same problems)

| Question | Method | Candidates | Output | Unblocks |
|---|---|---|---|---|
| How long is a good level in a city-builder campaign, and what ends it? | play or read design analyses of 4–5 campaigns; note the ending condition type (quota, survival, construction, choice) | Frostpunk scenarios; Anno 1800 campaign; Surviving Mars mysteries; Against the Storm's settlement loop; Timberborn's own tutorial | `design/wardens-precedents.md`: five rules we adopt, five we reject | arc §6 constraint 13 |
| How do they carry state between maps? | same | same | precedents note | design §3.2 |
| How do they present a choice with two valid answers? | same | Frostpunk's laws; Surviving Mars mysteries | precedents note | the midpoint card |
| How do story mods for other builders script events? | read datvm's BeaverChronicles JSON model (already located) | GitHub | precedents note | design §8 (events) |

## Track F: art and assets

| Question | Method | Output | Unblocks |
|---|---|---|---|
| Which vanilla models can stand in for the Ark, the levees, the field study, the archive? | `model-catalog.md`, the blueprint dump | catalog update | level 09, arc §8 |
| Can the Leaf Coats bundle or any other be opened now (the UnityPy failure)? | retry with the current Unity serialization tooling; else Blender | `wardens-art-path.md` | 3D assets at all |
| Card art: none, a still per level, or the map thumbnail? | mock three cards | `wardens-chapter-1-plan.md` §6 update | cards |
| Map thumbnails per level | generator renders them already; check the size the menu expects | `gen_map.py` | the map list |

## Track G: audience and scope

| Question | Method | Output | Unblocks |
|---|---|---|---|
| Who plays story maps in Timberborn, and how long do they play them? | read Workshop comments on story-style maps and the campaign requests on the feature board | `wardens-precedents.md` §audience | arc §6 constraints 13–14 |
| English first or German and English together? | the author decides; the loc pipeline (`gen_tutorial.py` writes rows) supports both from the start | decision in the arc's §8 | every card |
| Difficulty: does the campaign fix the mode, or does the player choose per level? | design §3.2 keeps the player's choice; confirm with the precedents | design §3.2 | `NewGameMode` handling |
| Free play as a feature: do players want later maps unlocked from the start? | Workshop reading | arc §6 requirement 5 | the New Game screen policy |

## Track H: playtest research (the numbers)

| Question | Method | Output | Unblocks |
|---|---|---|---|
| Days per level, deaths, Ledger deltas, at Normal, agent-driven | the harness in `playtest-and-video-capture.md` (new game from the API, stepping, screenshots) plus `campaign status` per day | `playtest/levels.md`: one tuning sheet per level | arc §6 constraint 13, every ending condition |
| Where a human stalls | one human run per level with the Warden active, the Uplink transcript kept | same sheet | cards, chapter order |
| Whether the Warden's voice holds over ten levels | read the transcripts against `wardens-play.md` §3 | playbook revisions | arc §6 requirement 8 |

## 8. Sequence

| Sprint | Tracks | Done when |
|---|---|---|
| 0 (first session with a game install) | A canon sheet; C rows 1–2, 8; D checklist | the do-not-contradict list exists; Act II is known to be possible; the transition strategy is chosen |
| 1 | B ecology notes; C rows 3–7; E precedents | ten cited facts with mechanics; the level length rule; terrain tinting answered |
| 2 | F assets; G audience; the midpoint card drafted and register-tested | the Ark's model path decided; the language decided |
| continuous | H playtests, one per level as each ships | the tuning sheet has a row per level |

## 9. Assumption register

Marked **verified** only with an evidence class 1 or 2 finding recorded in the named output.

| # | Assumption | Verify by | Status |
|---|---|---|---|
| 1 | Humans are extinct and unexplained in canon | A, loc file | community readings only |
| 2 | A mod's `Maps/` folder is not listed by the game | D, decompile `MapRepository` | documentation + a guide |
| 3 | `GameSceneLoader` can start a new game from the Game context | D | unverified (a null argument in a mod hints no) |
| 4 | Contamination kills beavers at a rate a map can tune | C | unverified |
| 5 | A strengthening badwater source makes a badtide-like event on a schedule | C | unverified |
| 6 | Terrain can be tinted through the material patcher | C | unverified |
| 7 | Bots can be run down without the game ending the colony | C | unverified |
| 8 | Beaver dams measurably raise water tables and buffer droughts | B | widely reported, citations pending |
| 9 | The game's moisture model rewards dams the way the thesis needs | B/C | design belief |
| 10 | A 128×128 map runs at speed 3 on a mid-range machine | C | one forum thread says large maps do not |
| 11 | A city-builder level of 15–30 days holds attention | E/G/H | precedent reading pending |
| 12 | The essential cut's `campaign.json` survives the full arc | design §7 | by construction, untested |

## 10. What this plan does not research

The Wardens' economy inside a level (that is the faction design's job and is playtested, not
researched); vanilla modding basics (documented); Unity asset pipelines beyond the one question in
track F; anything about the human at the keyboard (they are the reader, not a variable).
