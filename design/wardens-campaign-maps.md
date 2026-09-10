# The Wardens as a campaign: putting the player on the right map

> **Status:** research (2026-09-05); §1, §5.1, §5.2 and §5.5 **verified against the 1.1.2.4 decompile
> on 2026-09-10**, and §6 steps 1 and 2 are built (`WardensMapInstaller.cs`, `WardensCampaign.cs`) but
> not yet loaded in-game. The rest is unchanged research.
> either already used in this repo, read from a mod's published source, or quoted from the game's own
> log; the "not verified" list in §5 is what the next decompile session must confirm before code is
> written. Built on it: the system design [`wardens-campaign-design.md`](wardens-campaign-design.md) and the
> campaign concept [`wardens-campaign-concept.md`](wardens-campaign-concept.md). What each level's land has to
> guarantee, and what the generator needs to build it, is [`wardens-campaign-map-set.md`](wardens-campaign-map-set.md).
> The map itself is [`wardens-wasteland.md`](wardens-wasteland.md); the story the maps serve is
> [`faction-wardens.md`](faction-wardens.md) (three acts) and [`wardens-chapter-1-plan.md`](wardens-chapter-1-plan.md).

## 0. The question

A story campaign needs several maps, one per level, loaded in order. That is three separate problems,
and the game solves none of them for a mod:

1. **Distribution:** get the `.timber` files onto the player's machine and into the map list.
2. **Selection:** start level N on map N, either from the main menu or from inside level N-1.
3. **Continuity:** a new map is a new save, so anything the story remembers across levels has to be
   carried by us.

Timberborn has no campaign, scenario or objective system (the feature-upvote board has three open
requests for one); "level" is our word, the game only knows maps, saves and factions.

## 1. What is verified

| Fact | Evidence |
|---|---|
| `.timber` is both the map format and the save format: a zip of `world.json`, `map_metadata.json`, `version.txt`, `map_thumbnail.jpg`. Saves live in `Documents/Timberborn/Saves/<Settlement>/<name>.timber`. | [`wardens-wasteland.md`](wardens-wasteland.md); `TimberbotAutoLoad.cs` builds `SettlementReference(settlement, <UserDataFolder>/Saves)` and `SaveReference(name, settlementRef)`. |
| Custom maps are listed from `Documents/Timberborn/Maps` (Proton: `compatdata/1062090/pfx/drive_c/users/steamuser/My Documents/Timberborn/Maps`) with a `[Custom]` prefix, in the New Game screen's *Custom maps* tab. | [Custom Maps, official wiki](https://timberborn.wiki.gg/wiki/Custom_Maps); [Steam guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3262168856). |
| Steam Workshop maps are listed too, straight from `steamapps/workshop/content/1062090/<id>/`. | MapBrowser derives the Workshop id from `MapFileReference.Path` by walking up to an all-digit directory (`MapBrowserDialog.FindPublishedFileId`). |
| **A mod's own folder is not a documented map source.** The official mod directory layout has no `Maps/` entry, and the community guide tells mod.io map users to copy the file into `Documents/Timberborn/Maps` by hand. | [Mod directory structure, official wiki](https://github.com/mechanistry/timberborn-modding/wiki/Mod-directory-structure); [Steam guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3262168856) ("Custom maps obtained outside of Steam Workshop, for example via Mod.io, can be added manually by copying the file to the Maps folder"). |
| The map list can be refreshed at runtime: `MapRepository.NotifyMapRepositoryChanged()`; maps can be removed with `MapRepository.DeleteMap(MapFileReference)` (`Timberborn.MapRepositorySystem`). | [MapBrowser `WorkshopSubscriptionService.cs`](https://github.com/ihsoft/TimberbornMods/blob/main/MapBrowser/Core/WorkshopSubscriptionService.cs), [`MapBrowserDialog.cs`](https://github.com/ihsoft/TimberbornMods/blob/main/MapBrowser/CoreUI/MapBrowserDialog.cs) (v1.0.0, August 2026, so 1.1-era). |
| The list the New Game screen shows is `MapItemProvider.GetCustomMaps()` → `MapItem { MapFileReference, DisplayName }` (`Timberborn.MapItemsUI`); thumbnails via `MapThumbnailCache.GetThumbnail(MapFileReference)` (`Timberborn.MapThumbnail`). | same files. |
| **Corrected 2026-09-10 by the decompile.** `MapFileReference` is a `readonly struct` with **four** properties — `Name`, `Path`, `Resource`, `UserFolder` — a *private* constructor, and three static factories: `FromResource(name)`, `FromUserFolder(name)`, `FromDisk(path)`. The "eight positional parameters" recorded here earlier came from another mod and is not the 1.1 shape; never construct it positionally. | `ilspycmd Timberborn.MapRepositorySystem.dll` (1.1.2.4). |
| `MapRepository.UserMapsDirectory => Path.Combine(UserDataFolder.Folder, "Maps")`; `GetUserMapNames()` enumerates `*.timber` in exactly that directory and nothing else; `NotifyMapRepositoryChanged()` takes no arguments and posts `MapRepositoryChangedEvent`; `MapRepositorySystemConfigurator` binds `MapRepository` in the **MainMenu, Game and MapEditor** contexts. | same decompile. |
| `MapItemProvider.GetCustomMaps()` is `GetUserMaps().Concat(<every ICustomMapItemFactory>.Create())`, so `Timberborn.MapItemsUI.ICustomMapItemFactory` is a **supported extension point for listing maps from anywhere**, a mod's own folder included, via `MapFileReference.FromDisk(path)`. `MapItem` takes (reference, displayName, displayDescription, size, isRecommended, isUnconventional, isDeletable, isDev, mapIcon). | `ilspycmd Timberborn.MapItemsUI.dll` (1.1.2.4). |
| `MapNameService` is in `Timberborn.GameWonderCompletion`, bound in the **Game** context, and its `Name` is the **map file's name** (from `MapFileReference.Name`), persisted with the save and falling back to `GameSceneParameters.NewGameConfiguration.MapFileReference` on a new game. `HasMapName` says whether it is set. | `ilspycmd Timberborn.GameWonderCompletion.dll` (1.1.2.4). |
| The game logs the new-game configuration as `Starting new game at <utc>: FactionId: Folktails, MapFileReference: Name: Lakes, Path: , Resource: True, NewGameMode: StartingAdults: 8, ...` — so the configuration record carries **FactionId, MapFileReference, NewGameMode**. | [Steam bug thread with a Player.log](https://steamcommunity.com/app/1062090/discussions/2/603021231210417409/); [`playtest-and-video-capture.md`](playtest-and-video-capture.md) §1.1 named `NewGameConfiguration → GameSceneParameters → scene load` from the 1.1.2.4 decompile. |
| `NewGameMode` (1.0+, replaces `GameModeSpec`) is a positional record: StartingAdults, AdultAgeProgress, StartingChildren, ChildAgeProgress, FoodConsumption, WaterConsumption, StartingFood, StartingWater, TemperateWeatherDuration (min/max), DroughtDuration, DroughtDurationHandicapMultiplier, DroughtDurationHandicapCycles, CyclesBeforeRandomizingBadtide, ChanceForBadtide, BadtideDuration, BadtideDurationHandicapMultiplier, BadtideDurationHandicapCycles, InjuryChance, DemolishableRecoveryRate. | [EditSaveDifficulty `NewGameParameterService.U7.cs`](https://github.com/datvm/TimberbornMods/blob/master/EditSaveDifficulty/Services/NewGameParameterService.U7.cs). |
| The vanilla new-game mode panel can be driven from code: `NewGameModePanel(VisualElementLoader, <GameSceneLoader>, PanelStack, ILoc, CustomNewGameModeController)`, `Load()`, `SelectFactionAndMap(FactionSpec, MapFileReference)`, `_predefinedNewGameMode`, `OnCustomizeButtonClicked()`, `GetPanel()`; `CustomNewGameModeController.TryGetValidatedNewGameMode(out NewGameMode)`. EditSaveDifficulty binds `CustomNewGameModeController` in `[Context("Game")]` itself, i.e. **a main-menu class can be instantiated inside a running game** when its dependencies resolve. | same mod, `EditDifficultyDialog.U7.cs` + `MConfigs.cs`; the mod's [Steam page](https://steamcommunity.com/sharedfiles/filedetails/?id=3476629381) names `Timberborn.GameSceneLoading.GameSceneLoader` next to that constructor. |
| Inside a game, `MapNameService.Name` is the name of the map the game runs on; `FactionService.Current` the faction. | EditSaveDifficulty `EditDifficultyController.cs`; `WardensChapters.cs`. |
| Loading a save from code is proven in this repo: `ValidatingGameLoader.LoadGame(SaveReference)` (`Timberborn.GameSaveRepositorySystemUI`), `GameSaveRepository.SaveExists / GetSaves`. | `timberbot/src/TimberbotAutoLoad.cs`, MainMenu context. |
| The main menu panel is reachable: `MainMenuPanel.GetPanel()` (`Timberborn.MainMenuPanels`) has a `LoadMapButton`; MapBrowser inserts its own `NineSliceButton` after it from an `IUpdatableSingleton` in the MainMenu context. | [MapBrowser `MainMenuMapBrowserButton.cs`](https://github.com/ihsoft/TimberbornMods/blob/main/MapBrowser/CoreUI/MainMenuMapBrowserButton.cs). |
| Bindito contexts are `MainMenu`, `Game`, `MapEditor`, `Bootstrapper`; singletons live for the scene. | [Timberborn architecture, official wiki](https://github.com/mechanistry/timberborn-modding/wiki/Timberborn-architecture). |
| A new game posts `NewGameInitializedEvent` after the starting beavers spawn (internal, publicized); a loaded game does not. | `WardensStartingPopulation.cs`, [`faction-wardens.md`](faction-wardens.md) §3. |

Consequence for the open question in [`wardens-wasteland.md`](wardens-wasteland.md): assume **no**, the game does
not list a `.timber` shipped inside the mod. The build's copy into `Documents/Timberborn/Maps` is the
mechanism that works, and it has to be replicated at runtime for players who get the mod from the
Workshop or mod.io (§2, method A).

## 2. Three ways to put the player on level N

### A. Maps + the vanilla New Game screen (data only, the selection needs no code)

Ship one `.timber` per level in `wardens/src/Maps/` (`Wardens 01 First Light`, `Wardens 02 The Sump`, ...),
and install them into the player's `Maps` folder at runtime: a MainMenu-context `ILoadableSingleton`
that copies every `<mod>/Maps/*.timber` to `UserDataFolder.Folder/Maps` when the target is missing or
older, then calls `MapRepository.NotifyMapRepositoryChanged()` so the list refreshes without a restart.
Every API in that sentence is verified (§1). The player picks the level in the New Game screen under
*Custom maps*; the mod learns which level it is in from `MapNameService.Name`.

- Pro: nothing unverified; the dev-machine deploy already does the copy; no UI work.
- Con: the player can start level 3 first, and nothing moves them to level 2 when level 1 ends. The
  `Wardens NN` prefix keeps the levels together in a list that also holds every other custom map.
- This is the floor: whatever else is built, A is what makes the maps exist on the player's machine.

### B. Levels as shipped saves (`LoadGame`)

A level does not have to be a fresh map. Ship per-level **saves** (`Saves/Wardens Campaign/Level 02.timber`),
install them like A into `UserDataFolder/Saves/<settlement>/`, and switch levels with
`ValidatingGameLoader.LoadGame(new SaveReference(name, settlementRef))`, the path `TimberbotAutoLoad`
already takes. `ValidatingGameLoader` also drives the in-game Load dialog, so binding it in the Game
context is expected to resolve (verify, §5).

- Pro: a level can start mid-story, with the Core built, bots charged, tutorials finished up to a
  point, goods in stock, Uplink history present; vanilla persists all of it. No `NewGameConfiguration`
  needed, faction and mode are fixed inside the file.
- Con: saves come from playing or the editor, not from `gen_map.py` (a save is the same zip plus more
  singletons; generating one is possible but a bigger format job). A loaded game does not post
  `NewGameInitializedEvent`, so the bot swap and the Cold Boot orbit do not run; a level-start trigger of
  our own replaces them. Game-version migration applies to every shipped save. Every player lands in
  the same settlement name unless the installer copies to a fresh one.

### C. A programmatic new game (`StartNewGame`)

From inside level N (Game context) or from a campaign button in the main menu: build the new-game
configuration (`FactionId: "Wardens"`, the next level's `MapFileReference`, a `NewGameMode`) and hand
it to `GameSceneLoader`. Two rules keep this on verified ground:

- Never construct `MapFileReference` by hand (eight fields, six of them unknown); take the instance from
  `MapItemProvider.GetCustomMaps()` whose `Name` matches the level's map. If `MapItemProvider` is not
  bound in the Game context, `MapRepository` is the fallback source of references (check its API, §5).
- Do not invent the `NewGameMode`: either reuse the vanilla `NewGameModePanel` in a dialog so the
  player confirms difficulty and the tutorial toggle for the next level (EditSaveDifficulty shows this
  works in the Game context), or capture the mode the player chose for level 1 into `campaign.json`
  (§3) and pass it on.

- Pro: a real "next level" button; the New Game path runs (bot spawn, Cold Boot, tutorial reset,
  `NewGameInitializedEvent`); `gen_map.py` stays the single source of the land.
- Con: three signatures to confirm in the decompile (§5); continuity is entirely ours (§3).

**Recommendation.** A now, because the maps must be installed anyway and the code is small. C for the
transition once the decompile confirms `GameSceneLoader` and `NewGameConfiguration` (a day's work, most of
it the dialog). B stays the fallback for the transition if the new-game classes turn out not to be
bindable in the Game context, and the right tool for any level that should begin mid-story.

## 3. Carrying the story across maps

A new map is a new save, so every `ISaveableSingleton` starts empty and vanilla's own per-save state
(finished tutorials, unlocked buildings, science, goods) starts over. Chapter gating therefore restarts
per level, which is what a level wants; the campaign's memory ("level 1 is done, in mode Normal, with
the tutorial on") has to live outside the save.

- **`campaign.json` in the mod folder**, next to `settings.json` and the agent's `state.json`
  (`autoload.json` is the precedent for a file written on one scene and read on another):
  `{ "level": "02", "completed": ["01"], "mode": { ...NewGameMode... }, "tutorial": true, "ledger": [...] }`.
  Written by the Game-context campaign service when a level's final tutorial lands in
  `TutorialService._finishedTutorials` (the chapter service already polls that set); read by the
  MainMenu-context service to offer *Continue campaign* and by the Game-context service to pick the
  level's chapter table.
- **What crosses over.** The Ledger / Archive entries (`wardens-play.md`, planned item 3) append into
  `campaign.json`. Goods do not; if a level should start with scrap or Data Cores, the
  `NewGameInitializedEvent` handler that already swaps beavers for bots is the place to drop them
  (`NewGameMode.StartingFood/StartingWater` only cover food and water).
- **Which level am I in.** `MapNameService.Name`, with the level table keyed by map name; the
  reflection walk in `TimberbotReadV2.ResolveSettlementName` (`_gameCycleService → ... → _sceneParameters`)
  is the fallback if the service is not what we expect.

## 4. Per-level chapter tables

`WardensChapterService.Chapters` is one table today. The campaign shape is `Levels[]`, each with the
map name, its chapter table, the tutorial id that ends the level, and the next level's id. The
service picks the level by map name at `Load()` and behaves exactly as now inside it; when the ending
tutorial finishes it writes `campaign.json`, posts the toast and the Uplink line, and offers the
transition (chat button, MCP `campaign` tool, or the dialog from §2 C). `tools/validate.py` extends its
cross-check to every level (templates padlocked, tutorials exist, loc rows present), and
`tools/gen_map.py` gets a `--level` preset per map so each level's land is reproducible like the first.

## 5. Not verified: what the first decompile session must confirm

In order of how much depends on the answer (decompile setup: [`faction-wardens.md`](faction-wardens.md) §5,
[`../docs/devenv.md`](../docs/devenv.md)):

1. ~~`MapRepository`: which directories it enumerates, the method that lists maps, the exact
   `NotifyMapRepositoryChanged` signature.~~ **Answered 2026-09-10** (§1): only `UserDataFolder.Folder/Maps`,
   `GetUserMapNames()`, and a no-argument `NotifyMapRepositoryChanged()`. Method A is built
   (`wardens/src/WardensMapInstaller.cs`). The open question in `wardens-wasteland.md` is settled: the game
   does **not** list a `.timber` shipped inside a mod folder, but `ICustomMapItemFactory` is the supported
   way to make it do so without copying anything.
2. ~~`MapFileReference`: all eight fields.~~ **Answered 2026-09-10**: four properties, a private constructor
   and three static factories (§1). A user-folder map carries an empty `Path` and `UserFolder = true`;
   `CustomMapNameToFileName` resolves it to `UserMapsDirectory/<name>.timber`.
3. `Timberborn.NewGameConfigurationSystem.NewGameConfiguration` constructor;
   `Timberborn.GameSceneLoading.GameSceneLoader`'s start-new-game method; and which configurator binds
   `GameSceneLoader` (MainMenu only, or Game too). EditSaveDifficulty passes `null` for that parameter of
   `NewGameModePanel` inside a game, which hints it is MainMenu-only; if so, C runs its transition
   through a save-and-return-to-menu (`autoload.json`-style handoff) rather than a direct scene switch.
4. `NewGameModePanel`'s second constructor parameter type; `CustomNewGameModeController`'s
   dependencies; where the per-faction default `NewGameMode` comes from (a spec in the blueprints?).
5. ~~`MapNameService`: namespace, and whether `Name` is the file name or the localized display name.~~
   **Answered 2026-09-10**: `Timberborn.GameWonderCompletion`, Game context, the file name (§1). The
   level table in `WardensCampaign.cs` is keyed on it.
6. `ValidatingGameLoader` in the Game context (for B).
7. `UserDataFolder.Folder` under Proton, against `TimberbotPaths`.

## 6. Work plan (in order, each step testable in-game)

1. ~~**Runtime map installer** (A)~~ — **built 2026-09-10**: `wardens/src/WardensMapInstaller.cs`, MainMenu
   context, copy-if-missing-or-older plus `NotifyMapRepositoryChanged()`, an `installMaps` switch in
   `settings.json`, and a `RetiredMapNames` list so a renamed level does not leave its old copy behind in the
   player's Maps folder. **Not yet loaded in-game.** Playtest: subscribe-style install (mod folder only, empty
   `Maps`), the level maps appear under *Custom maps*.
2. ~~**`WardensCampaign` service + `campaign.json`**~~ — **built 2026-09-10**: `wardens/src/WardensCampaign.cs`.
   Level table keyed by map name, level detection through `MapNameService`, completion detection by polling
   `TutorialService._finishedTutorials` for the level's ending tutorial, toast + Uplink line, and the MCP
   `campaign` tool (`status`, `ledger`, `record`, `complete`, `reset`). No transition: the player starts the
   next level by hand from the New Game screen. **Not yet loaded in-game.**
4. **Main-menu *Continue campaign*** button after `LoadMapButton` (MapBrowser precedent) that starts the
   level named in `campaign.json`.
5. **Second map**: `gen_map.py --level 02` (The Pods: clean water within reach, contaminated core; **corrected 2026-09-10:** level 02 is *The Sump*,
   a gorge and a confluence, as `WardensCampaign.cs`, `wardens-campaign-arc.md` §3 and `wardens-campaign-story.md` §4
   say, and its contract is `wardens-campaign-map-set.md` §2; the brief in this line became the prototype spec
   `wardens/maps/wardens-proto-crater.map.toml`, which claims no level), the
   level's chapter table, `validate.py` per-level checks.

## Sources

- Official modding wiki: [Mod directory structure](https://github.com/mechanistry/timberborn-modding/wiki/Mod-directory-structure),
  [Timberborn architecture](https://github.com/mechanistry/timberborn-modding/wiki/Timberborn-architecture),
  [Mod management](https://github.com/mechanistry/timberborn-modding/wiki/Mod-management)
- [Custom Maps, Timberborn wiki](https://timberborn.wiki.gg/wiki/Custom_Maps);
  [Timberborn: Mods and Custom Maps, Steam guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3262168856)
- ihsoft, [MapBrowser](https://github.com/ihsoft/TimberbornMods/tree/main/MapBrowser) (public domain):
  `MapRepository`, `MapItemProvider`, `MapFileReference.Path/Name`, `MapThumbnailCache`, `MainMenuPanel`
- datvm, [EditSaveDifficulty](https://github.com/datvm/TimberbornMods/tree/master/EditSaveDifficulty):
  `NewGameMode`, `NewGameModePanel`, `CustomNewGameModeController`, `MapNameService`, `MapFileReference` ctor;
  its [Steam page](https://steamcommunity.com/sharedfiles/filedetails/?id=3476629381)
- datvm, [BeaverChronicles](https://github.com/datvm/TimberbornMods/tree/master/BeaverChronicles): a JSON
  event/choice framework (pop-ups, rewards, delayed outcomes, follow-up chapters) worth reading before
  writing any in-level story scripting of our own
- Player.log with the new-game line: [Steam thread](https://steamcommunity.com/app/1062090/discussions/2/603021231210417409/)
- Campaign requests on the feature board:
  [How about a campaign mode?](https://timberborn.featureupvote.com/suggestions/214704/how-about-a-campaign-mode),
  [Map objectives, scenario editor](https://timberborn.featureupvote.com/suggestions/333476/map-objectives-win-conditions-custom-scenario-editor-and-challenge-mission-texts)
