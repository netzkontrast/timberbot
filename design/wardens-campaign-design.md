# The Wardens campaign system: design

> **Status:** design (2026-09-05), written on top of the research in
> [`wardens-campaign-maps.md`](wardens-campaign-maps.md) and for the story in
> [`wardens-campaign-concept.md`](wardens-campaign-concept.md) and the full arc in
> [`wardens-campaign-arc.md`](wardens-campaign-arc.md); what still has to be found out is
> [`wardens-campaign-research-plan.md`](wardens-campaign-research-plan.md). Nothing compiled. Every game API used here
> is in the research doc's "verified" table unless marked **(unverified)**; the unverified ones sit behind
> one interface (`ILevelStarter`, §4.4) so the rest of the system does not depend on how the decompile
> answers. Story arc: [`faction-wardens.md`](faction-wardens.md); chapter mechanics:
> [`wardens-chapter-1-plan.md`](wardens-chapter-1-plan.md) §4 and `wardens/src/WardensChapters.cs`;
> the agent's seat: [`wardens-play.md`](wardens-play.md).

## 1. Overview

**Problem.** The Wardens are a story faction, but Timberborn only knows factions, maps and saves. A
campaign is a sequence of levels, each on its own map, with story state that outlives any single save.
The game gives us none of that; the mod has to add it without patching the game (no Harmony, the repo
rule) and without depending on APIs nobody has read yet.

**Solution in one paragraph.** A level is a map plus a chapter table plus an ending condition. The
mod ships its maps and installs them into the player's `Maps` folder at the main menu (verified path).
Inside a game, a campaign service recognises the level from the map name, runs that level's chapter
gating (the existing service, made per-level), detects the level's end, records progress in a
`campaign.json` in the mod folder (outside any save), and offers the transition. The transition
itself is a strategy: a programmatic new game when the decompile confirms the classes, a shipped save
or a hand-off through the main menu when it does not. The main menu gets one button, *Continue
campaign*, that starts the level the file names.

**Goals.**

- One map per level, ordered, with the New Game screen still usable as the manual path.
- Story continuity across saves: which level, in which mode, what the Ledger says, what the Uplink said.
- The agent (the Warden) can read and drive the campaign through MCP, the same way it reads chapters.
- Every level playable from the New Game screen alone, so the campaign never blocks on unverified code.
- Every name the game resolves at load is checked by `tools/validate.py` before an in-game run.

**Non-goals (this design).** Scripted in-level events beyond the tutorial line (datvm's BeaverChronicles
is the reference if that comes; see research doc sources); custom 3D art; saving the campaign in the
cloud; multiple concurrent campaigns per player (one file, one campaign; `reset` starts over).

## 2. Architecture

```
┌─ MainMenu scene ───────────────────────────────────────────┐   ┌─ Game scene ────────────────────────────────────────────────┐
│ WardensMapInstaller      ILoadableSingleton                │   │ WardensCampaignService   ILoadableSingleton, IUpdatableSingleton │
│   <mod>/Maps/*.timber → UserDataFolder/Maps                │   │   level = Levels.ByMap(MapNameService.Name)                  │
│   MapRepository.NotifyMapRepositoryChanged()               │   │   polls TutorialService._finishedTutorials (like chapters)   │
│                                                            │   │   writes campaign.json on level end; offers transition       │
│ WardensCampaignMenu      IUpdatableSingleton               │   │                                                              │
│   button after LoadMapButton: "Continue campaign"          │   │ WardensChapterService    per-level chapter table (refactor)  │
│   reads campaign.json → ILevelStarter.Start(level)         │   │ WardensStartingPopulation + level start state (carry-over)   │
│                                                            │   │ WardensLevelTransition   ILevelStarter strategies            │
│ WardensHandoff           ILoadableSingleton                │   │   NewGameLevelStarter   GameSceneLoader (unverified)         │
│   campaign.handoff.json → start level, delete (one-shot)   │   │   SaveLevelStarter      ValidatingGameLoader (proven)        │
└────────────────────────────────────────────────────────────┘   │   HandoffLevelStarter   write handoff, return to menu        │
                 ▲                      ▲                        │ WardensMcpTools          `campaign` tool                     │
                 │ campaign.json        │ campaign.handoff.json  │ WardensChat              level cards, transition prompt      │
                 └──────────────────────┴────────────────────────┴──────────────────────────────────────────────────────────────┘
                                   WardensCampaignState   (shared file I/O, both scenes)
```

Two scenes, two configurators, one file. Nothing in the Game scene knows about the main menu except
through `campaign.json` and, for the hand-off strategy, `campaign.handoff.json`. That is the same
shape as `autoload.json` (`TimberbotAutoLoad`), which is the precedent for a file written by one
process state and consumed by another.

## 3. Data design

### 3.1 The level table (C#, the single source; `WardensCampaign.cs`)

```csharp
public sealed class WardensLevel
{
    public string Id;                 // "01"
    public string Title;              // loc key Wardens.Level.01.Title
    public string MapName;            // "Wardens 01 First Light"  == map_metadata / file stem, the identity
    public string MapFile;            // "Maps/Wardens 01 First Light.timber" inside the mod
    public WardensChapter[] Chapters; // the level's gating table (today's Chapters array is level 01's)
    public string EndsWith;           // tutorial id whose completion ends the level ("Wardens.MoreBeavers")
    public string Next;               // next level id or null (campaign complete)
    public LevelStart Start;          // what the level begins with (§3.3)
}
public static readonly WardensLevel[] Levels = { ... };   // order = campaign order
```

Identity is the **map name**: `MapNameService.Name` inside a game, `MapItem.DisplayName` / `MapFileReference.Name`
in the menu, the file stem on disk. The `Wardens NN` prefix keeps the maps together in the custom-maps
list and makes the order visible to the player. `validate.py` checks, per level: the map file exists in
`Maps/`, `EndsWith` is a tutorial id the generator emits, every chapter template exists (and, since
2026-09-11, no building in the faction's collections carries a science cost: the bar is open from the
first frame), loc rows for title and cards exist, `Next` points at a level.

### 3.2 `campaign.json` (mod folder, next to `settings.json`)

```json
{
  "version": 1,
  "startedAt": "2026-09-05T18:00:00Z",
  "level": "02",
  "completed": ["01"],
  "mode": { "kind": "Normal", "tutorial": true },
  "carry": { "ledger": { "poisoned": 212, "healed": 0, "green": 31, "archive": 14, "born": 1 },
             "uplink": ["…last 20 Uplink lines of level 01…"],
             "population": { "bots": 6, "beavers": 1 } },
  "history": [ { "level": "01", "startedAt": "…", "completedAt": "…", "days": 41, "ledger": { … } } ]
}
```

- `level` is the level to start or the one in progress; `completed` is monotonic.
- `mode.kind` names the vanilla difficulty the player chose for level 01 so later levels can default to it;
  the full `NewGameMode` record is not persisted (its shape is verified, but reproducing a custom mode across
  levels is a §8 extension). `tutorial` mirrors the new-game toggle; off = every chapter opens at load,
  as today.
- `carry` is what the next level reads at its first frame (§3.3). `history` is the Archive's spine
  (`wardens-play.md`, Directive 3) and what the epilogue reads back.
- Written atomically (temp file + rename), read with defaults for every missing field, `version` gates
  migrations. A corrupt file is renamed `campaign.json.bad` and treated as "no campaign".

### 3.3 Level start state

```csharp
public sealed class LevelStart
{
    public int Bots = 4;                 // replaces the vanilla starting beavers (Spike A, today: bots = adults)
    public int Beavers = 0;              // pod-born beavers already alive at level start (Act II+)
    public (string good, int amount)[] Goods;   // dropped at the Core on the first frame
    public bool CarryPopulation;         // true: take bots/beavers from campaign.json.carry instead
}
```

Applied by `WardensStartingPopulation` in its existing `NewGameInitializedEvent` handler (new game only),
so a level started through the New Game screen and one started by the transition behave the same. Goods
use the same placement path the Timberbot `place_building`/inventory code already exercises; if no
verified "add goods to a stockpile" call exists, `Goods` ships empty in v1 and the level's economy is
tuned so it is not needed (the concept doc assumes that).

### 3.4 `campaign.handoff.json` (one-shot, hand-off strategy only)

`{ "level": "02", "mode": {...} }`, written by `HandoffLevelStarter` right before returning to the main
menu, consumed and deleted by `WardensHandoff` at the next menu load, exactly as `autoload.json`.

## 4. Component design

### 4.1 `WardensMapInstaller` (MainMenu, `ILoadableSingleton`)

- For every `Levels[i].MapFile`: if `UserDataFolder/Maps/<name>.timber` is missing, or its size or
  `version.txt` differs from the shipped file, copy; never delete a player's other maps.
- After any copy: `MapRepository.NotifyMapRepositoryChanged()`.
- Logs one line per installed map, `[Wardens] maps: installed 2, up to date 1`.
- Failure (read-only folder, Proton path mismatch): log a warning with both paths, continue; the
  campaign menu then shows *map missing* for that level (§4.2). `TimberbotPaths` is the precedent for
  the Proton `Documents` resolution.

### 4.2 `WardensCampaignMenu` (MainMenu, `IUpdatableSingleton`)

- MapBrowser precedent: wait until `MainMenuPanel.GetPanel()` has `LoadMapButton`, insert a
  `NineSliceButton` after it with the anchor's classes. Text: *Continue campaign* when
  `campaign.json` names a level, *Start campaign* otherwise.
- Click → `ILevelStarter.Start(level, mode)` for the level in the file (or `Levels[0]`).
- If the level's map is not in `MapItemProvider.GetCustomMaps()`: dialog *"Map 'Wardens 02 …' is not
  installed; the New Game screen's Custom maps tab lists what the game sees"* and no start. The button
  is the convenience; the New Game screen stays the fallback for every level.

### 4.3 `WardensCampaignService` (Game, `ILoadableSingleton`, `IUpdatableSingleton`)

- `Load()`: `_level = Levels.ByMap(mapNameService.Name)`; if null, the service is inert (a Wardens
  game on a non-campaign map is free play; other factions are never touched, as today). Reads
  `campaign.json`; if `carry` targets this level and this is a new game (`NewGameInitializedEvent`
  observed this session), hands `carry` to `WardensStartingPopulation` and marks it consumed.
- `UpdateSingleton()` every 0.5 s (same cadence as chapters): when `EndsWith` appears in
  `TutorialService._finishedTutorials` and the level is not yet in `completed`: append `history`,
  set `completed`, set `level = Next`, write the file, post the toast + the Uplink card
  (`Wardens.Level.<Id>.Complete`), and raise `LevelCompleted` for frames (`WardensFrames` adds a
  `level` event, like `chapter`). The first poll after a load is silent, as in the chapter service.
- Exposes `Status()` (level, chapters via the chapter service, ending tutorial, done, next, map
  installed?) for the MCP tool and the Timberbot `/api/campaign` GET.
- `Advance()` = call the transition (§4.4); `Reset()` = archive `campaign.json` to
  `campaign.<timestamp>.json` and start from `Levels[0]` (dev/testing, MCP only).

### 4.4 `ILevelStarter` and the three strategies (`WardensLevelTransition.cs`)

> **2026-09-11 (game machine, 0.4.1): rewritten on the read API, `built`; the main-menu start is
> `verified`.** The 2026-09-10 cloud version reached every game API by reflection; the 1.1.2.4
> decompile showed each guess missed (`NewGameConfiguration` takes four arguments, the mode type is
> `GameModeSpec`, `GameSceneLoader` has no main-menu method), so it compiled and could never have
> started a level. The rewrite injects `GameSceneLoader`, `Autosaver`, `ValidatingGameLoader` and
> `MainMenuSceneLoader`, all read as bound in the Game context, and `WardensReflect.cs` /
> `WardensServiceLocator.cs` are deleted. `campaign.handoff.json` containing `{"level":"01"}` at
> launch opened the game on `Wardens 01 First Light` as a new Wardens game (Player.log:
> `FactionId: Wardens, MapFileReference: Name: Wardens 01 First Light`). The in-game half
> (`campaign action=next` from inside a level) is `built`, not yet run.

```csharp
public interface ILevelStarter { bool CanStart(WardensLevel level, out string why); WardensTransition Start(WardensLevel level, CampaignMode mode); }
```

Tried in this order; the first whose `CanStart` is true wins, and the choice is logged.

| Strategy | Scene | Mechanism | Status |
|---|---|---|---|
| `NewGameLevelStarter` | Game + MainMenu | In a game, `Autosaver.CreateExitSave()` first (what *Exit to main menu* does); then `GameSceneLoader.StartNewGameInstantly("Wardens", MapFileReference.FromUserFolder(map), level.Title)`, which takes the default `GameModeSpec` itself. | Read in the decompile; MainMenu half **verified** 2026-09-11, Game half `built`. |
| `SaveLevelStarter` | Game + MainMenu | Shipped save `Saves/Wardens Campaign/<Level>.timber`, installed like maps; `ValidatingGameLoader.LoadGame(new SaveReference(...))`. Level start state comes from the save, not from §3.3. | Binding read (MainMenu + Game); inert, no level ships a save. |
| `HandoffLevelStarter` | Game | Reached when the map is not installed. Write `campaign.handoff.json`, then `MainMenuSceneLoader.SaveAndOpenMainMenu()` (the pause menu's own action, exit save included); `WardensHandoff` installs the maps and starts the level from the menu. | Read; the menu half is the verified path above. |

The transition is always **offered, not forced**: the level-complete card has *Continue to Level 02*
and *Stay* (the player may want to finish something). The Warden may call `campaign next` only after
the player said so in chat (playbook rule, same family as the camera policy).

### 4.5 `WardensChapterService` (refactor)

`Chapters` becomes `_level.Chapters`; no behaviour change inside a level. `Force`, `IsComplete`,
the toolbar refresh and the toast stay. The `chapter` MCP tool reports the level id with its list.

### 4.6 `WardensStartingPopulation` (extension)

Today it swaps the spawned beavers for bots. It gains `LevelStart`: `Bots` replaces the count,
`Beavers` spawns pod-born beavers through the same factory vanilla uses for the pod (verify the
call, it is the pod's own), `Goods` if a verified call exists. Only on `NewGameInitializedEvent`.

### 4.7 MCP `campaign` tool (`WardensMcpTools.cs`, main thread)

| action | returns |
|---|---|
| `status` | level id/title/map, chapters (delegates), ending tutorial + finished?, next level + map installed?, mode, `carry`, `history` summary, transition strategy in use |
| `next` | offers/starts the transition (refused with a reason if the level is not complete, unless `force=true`) |
| `card` | the level's intro/complete cards' text (what the player was told) |
| `reset` | archive the file, start over (dev) |

`wardens_status` gains a `campaign` block (level, done, next) so the agent sees it on every frame.

### 4.8 Timberbot API (read-only mirror)

`GET /api/campaign` returns `Status()` for out-of-process agents and `tbot`; the WebSocket gains
`campaign.level.complete`. Same code path, no second source of truth.

## 5. Interface contracts (what the concept doc and the generators rely on)

| Contract | Owner | Checked by |
|---|---|---|
| Level ids are two digits, map names `Wardens NN <Title>`, files `Maps/Wardens NN <Title>.timber` | `WardensCampaign.cs` | `validate.py` |
| One `gen_map.py --level NN` preset per level, seeded, byte-reproducible | `tools/gen_map.py` | `gen_map.py --check` after every write |
| Every level's `EndsWith` is a tutorial in `gen_tutorial.py`'s table (new levels add tutorials there) | `tools/gen_tutorial.py` | `validate.py` |
| Loc rows `Wardens.Level.<Id>.Title / .Intro / .Complete` | `Localizations/enUS.csv` (generated) | `validate.py` |
| The Warden's playbook names the `campaign` tool and the "offer, don't force" rule | `wardens/WARDEN.md`, server `instructions`, `wardens-play.md` | the three-way agreement rule in `AGENTS.md` |

## 6. Flows

**First launch after install.** Menu loads → installer copies maps, notifies the repository → the
New Game screen lists `[Custom] Wardens 01 First Light` → *Start campaign* (or New Game → Wardens →
that map). Both paths land in the same Game scene with the same `NewGameInitializedEvent`.

**Level end.** `Wardens.MoreBeavers` finishes (the first beaver) → service writes `campaign.json`
(`level: 02`, `carry` from the Ledger and population) → toast + Uplink card → frame `level` event →
the Warden speaks to it (the concept doc has the line) → player clicks *Continue* (or says so; the
Warden calls `campaign next`) → `ILevelStarter.Start(02)`.

**Reload mid-level.** The service's first poll is silent; `campaign.json` is untouched unless the level
end is newly observed. A player who loads an older save of a completed level sees the level as
complete (file) and the ending tutorial as finished (save) and nothing fires twice.

**Out-of-order start.** New Game on `Wardens 03` with `completed = ["01"]`: allowed; the level plays
with its own chapter table and an empty `carry` (defaults from `LevelStart`). `campaign.json` records it
in `history` with `"outOfOrder": true` and does not change `level`. Free play is a feature, not an error.

**Campaign complete.** `Next == null` → `level` stays, `completed` has every id, the epilogue card reads
`history` back (the Archive, Directive 3); *Continue* becomes *Start again* (`reset` with the history kept
under `history.previousRuns`).

## 7. Failure handling

| Failure | Behaviour |
|---|---|
| Map file missing on disk | installer logs, menu button explains, New Game screen simply lacks the map; the level is skippable by playing the next one out of order |
| `campaign.json` unreadable | renamed `.bad`, campaign restarts; the Uplink says so once |
| Game version migrated the map with warnings | the existing `wardens-wasteland.md` guidance (the file claims 0.7.10 on purpose); nothing campaign-specific |
| `NewGameLevelStarter.Start` throws | caught; the service falls back to `HandoffLevelStarter`, then `SaveLevelStarter` if a save is shipped; the card tells the player to use New Game → the map name |
| Faction is not Wardens on a campaign map | service inert, like the chapter service |
| Transition requested with the level incomplete | refused with the missing tutorial's name (`force` for dev) |
| Two processes writing the file | temp + rename; last writer wins; the file is small and rewritten whole |

Threading: file I/O and the MCP `status` run off the main thread; anything touching game state (`next`,
`reset` spawning, chapter unlocks) goes through the main-thread queue `WardensMcpServer.UpdateSingleton`
drains, as every other tool does.

## 8. Extensions (not in v1)

- Persist the full custom `NewGameMode` across levels (the record's 19 fields are known).
- In-level scripted events (choice cards, delayed outcomes) on top of the tutorial engine, or by
  reading BeaverChronicles' JSON event model.
- Ship saves for levels that begin mid-story (`SaveLevelStarter` as a first-class path, not a fallback).
- A campaign panel in the main menu (level list with thumbnails from `MapThumbnailCache`, Ledger totals).
- Cloud/Steam sync of `campaign.json` (out of scope while the game has no per-mod cloud storage).

## 9. Testing

- **Static (no game):** `validate.py` per-level checks (§5); a Python test for `campaign.json`
  round-trip and migration (`wardens/tools/test_campaign_state.py`, pure JSON); `gen_map.py --check` for
  every level map.
- **Smoke (with the game):** extend `playtest/PLAYTEST.md`: (1) fresh Documents tree, mod only →
  maps listed; (2) *Start campaign* lands on level 01 with the Cold Boot orbit; (3) `chapter unlock` ×5 +
  `tutorial next` to the ending tutorial → `campaign.json` written, card shown; (4) `campaign next` →
  level 02 loads, `carry` applied (bots + beavers count), first frame silent; (5) reload level 01's
  save → nothing fires; (6) corrupt the file → `.bad` + restart.
- **MCP:** `playtest/mcp_smoke.py` gains `campaign status/next/reset` assertions.
- **Agent:** the Warden's playbook gets a "level end" section; `wardens-play.md` the stance.

## 10. Rollout (each step playable, in order)

1. `WardensMapInstaller` + `Levels` table with level 01 only (today's map and chapter table moved
   into it) + `validate.py` checks. No visible change except maps installing at runtime.
2. `WardensCampaignService` + `campaign.json` + MCP `campaign status` + the level-complete card. The
   transition is "use New Game → Wardens 02" in the card.
3. Level 02 map (`gen_map.py --level 02`), its chapter table, its tutorials, its cards.
4. `ILevelStarter`: `HandoffLevelStarter` first (needs only the MainMenu half of the new-game API), then
   `NewGameLevelStarter` in-game once the decompile answers §5.3 of the research doc; `SaveLevelStarter` if
   a level needs a mid-story start.
5. *Continue campaign* button; `/api/campaign`; `wardens_status.campaign`.
6. Levels 03–05 per the concept doc, one at a time, each with a smoke run.

Feature flag: `"campaign": true` in `settings.json`; off = today's behaviour (one map, one chapter table).

## 11. Decisions (ADR-style, short)

1. **Campaign state lives outside the save.** A save is a level; the story spans saves. `campaign.json`
   in the mod folder, `autoload.json` as precedent. Rejected: `ISaveableSingleton` only (dies with the
   save), Unity `PlayerPrefs` (opaque, not portable).
2. **A level is identified by its map name.** Nothing to inject, nothing to persist; works for both the
   New Game screen and the transition. Rejected: a marker entity in the map (needs a custom template),
   a settlement-name convention (players rename).
3. **Transition is a strategy behind one interface.** The only unverified APIs are isolated; the campaign
   ships before they are confirmed. Rejected: block the whole feature on the decompile.
4. **Never construct `MapFileReference` by hand.** Eight fields, six unknown; the game's own list is the
   source. Rejected: a reflection-built record (breaks silently on a game update).
5. **Offer, don't force, the transition.** The player owns purpose (`wardens-play.md` §1); the Warden
   asks. Rejected: auto-advance on level end.
6. **Free play is allowed.** Any level from the New Game screen, out of order, with defaults. Rejected:
   hiding later maps (impossible without patching the map list, and hostile).
7. **No Harmony, no patches.** Everything is a singleton in a Bindito context, the repo rule.

## 12. Open questions

- The verified-list gaps in [`wardens-campaign-maps.md`](wardens-campaign-maps.md) §5 (they decide
  which `ILevelStarter` ships first).
- Does the pod's beaver factory accept a position for a spawn at level start (Act II levels)?
- Is a verified "add goods to a stockpile" call available for `LevelStart.Goods`, or is the Timberbot
  inventory write path enough?
- Should `campaign.json` live per settlement instead of per mod folder if a player runs two campaigns?
  (v1: one file; `reset` archives.)
