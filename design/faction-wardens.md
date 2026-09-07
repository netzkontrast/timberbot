# The Wardens — a science faction whose win condition is the other two factions

> **Status:** the arc, and two feasibility spikes against Timberborn **1.1.2.4** (decompiled 2026-09-03). The mod in [`../wardens/`](../wardens/README.md) is at v0.3: faction, bot start (Spike A), the 18-tutorial story line, chapter gating, the generated wasteland map, the in-game MCP server with the `frame` heartbeat and the agent's playbook ([`wardens-play.md`](wardens-play.md)), and cutscenes as data with the Cold Boot as the first scene ([`wardens-cutscenes.md`](wardens-cutscenes.md)); Spike B is still a stub. Version history: [`../wardens/CHANGELOG.md`](../wardens/CHANGELOG.md). Built and deployed once, **not yet verified in-game**; the status notes at the end of this file and `../AGENTS.md` ("The Wardens: state") say what the first run must answer. Playtest/video harness requirements: [`playtest-and-video-capture.md`](playtest-and-video-capture.md). Chapter 1 detail (identity, starting buildings, goal gating, cutscene, art, UI): [`wardens-chapter-1-plan.md`](wardens-chapter-1-plan.md); available art: [`model-catalog.md`](model-catalog.md).

You don't play the survivors. You play the machines that made survival possible.

The Wardens are a bot faction stranded in a poisoned wasteland. Their "wellbeing" is **Data**. They run on badwater and their industry contaminates the ground they stand on. The only way out is to research the **breeding pods** — the same pods Iron Teeth beavers come out of in vanilla — hand the land to organic labor, clean up their own mess, and let beavers flourish. Flourishing beavers are the richest source of Data there is. When the archive is full, the faction spends it on the one thing every other faction's wonder is *not* about: **bringing humans back.**

---

## 1. The arc

| Act | Population | Fuel | Data source | Pressure |
|---|---|---|---|---|
| **I — Cold Start** | Bots only | Badwater + Lubricant | Terminals, sensors | Every building bleeds contamination into the soil; bot "needs" are barely met; nothing grows |
| **II — The Pods** | Bots + pod-born beavers | Badwater (bots), clean water (beavers) | Beavers under observation | The Act I contamination is now killing your beavers. Remediation tech unlocks |
| **III — Green** | Beavers dominant, bots as labor | Clean water, food | Living ecosystems (field studies) | Enough Data to build the Ark |
| **Wonder — The Ark** | — | — | Consumes the archive | Revive humans |

Design principle: **the pollution is not a penalty, it's the engine.** Act I's fuel choice creates Act II's problem, and Act II's cleanup is what makes Act III possible. No vanilla faction changes its population type over a run; this one does.

## 2. Core systems, mapped onto vanilla

**Data as wellbeing.** Vanilla `NeedSpec` already has `CharacterType: "Bot"` (Emberpelts ships `Fuel` and `Lubricant` bot needs this way). Wardens bot needs are ordinary needs with a data theme, satisfied at buildings the way Folktails use a Carousel:

| Need | Satisfied by | Vanilla analogue |
|---|---|---|
| Calibration | Calibration Rig (attraction-type building) | Carousel / Shrine |
| Telemetry | Being near an active automation sensor | Weathervane-style decor need |
| Firmware | Consumable good `Firmware` (recipe: Data + Metal) | Food need |
| Uplink | Relay Mast building, range-based | Beaver Statue |

Unmet needs use `PunitiveNeedSpec` (movement/work penalties) exactly like vanilla `Fuel`. All JSON.

**Badwater as fuel.** Bot `Fuel` need satisfied by a badwater-derived good instead of biofuel. Verify the vanilla contamination-liquid good id in the blueprint dump before wiring recipes; otherwise define `Good.Sludge`.

**Data as a good.** `Good.DataCore` — hauled, stored, visible on stockpiles. Produced by Observer buildings, consumed by Firmware recipes and the Ark. Keeps vanilla Science untouched for tech unlocks, so the two currencies never fight.

**Population shift.** Iron Teeth **Breeding Pods** already exist. The Wardens' `Buildings` template collection includes the IT breeding pod with a science cost, so Act II is literally "research the pod". Beavers born from it are ordinary beavers with the faction's textures.

**Act III Data gating.** Late Observer recipes consume organic inputs (`FieldSamples` from a beaver-staffed Field Study that eats berries/crops). Living systems produce more Data because the recipe inputs only exist once agriculture is running. Pure recipe design, no scaling code.

**The wasteland.** A shipped map with high soil contamination and badwater sources. Map editor only.

**The Ark.** `FactionWonderSpec` (completion image, sound, loc keys — see Emberpelts). Wonder building recipe consumes DataCores.

## 3. Feasibility (verified against 1.1.2.4)

| Piece | Data-only? | Evidence |
|---|---|---|
| Faction definition | ✅ | `FactionSpec` fields: `TemplateCollectionIds`, `NeedCollectionIds`, `GoodCollectionIds`, `BlueprintModifiers`, `StartingBuildingId`, avatars/textures (`Timberborn.FactionSystem`) |
| Custom bot template in the faction | ✅ | Emberpelts `TemplateCollection.Characters.*` lists `Characters/Bot/Bot.Emberpelts.blueprint` |
| Bot needs with penalties | ✅ | `NeedSpec.CharacterType: "Bot"` + `PunitiveNeedSpec` + `CriticalNeedSpec` (Emberpelts `Need.Fuel`) |
| New goods, recipes, tool groups | ✅ | Emberpelts: 22 goods / 30 recipes / 3 tool groups, all JSON |
| Reusing vanilla buildings (IT breeding pod, water pumps) | ✅ | Template collections reference vanilla blueprint paths |
| Wonder | ✅ | `FactionWonderSpec` in Emberpelts' faction blueprint |
| Wasteland map | ✅ | Map editor, or a generated `.timber` (shipped: [`wardens-wasteland.md`](wardens-wasteland.md)) |
| **Bots as *starting* population** | ❌ C# | Spike A below |
| **Buildings that emit contamination** | ❌ C# | Spike B below |

Two C# components. Both small. The mod already publicizes vanilla assemblies (`Publicize="true"` in [`Timberbot.csproj`](../timberbot/src/Timberbot.csproj)), so `internal` game types are reachable without Harmony. Note that even Emberpelts requires a script pack (`BobingaboutScriptPack`) — a data-only faction with zero C# is the exception, not the norm.

## 4. Spike A — starting population is hardcoded to beavers

Code path in `Timberborn.GameStartup`:

```
GameInitializer.SpawnBeavers()
  → GameModeSpec.StartingAdults / StartingChildren        (global game-mode spec, not per faction)
  → StartingBeaversInitializer.Initialize(position, …)
  → BeaverFactory.CreateAdult / CreateChild
  → GameInitializer.PostSpawnBeavers()  posts NewGameInitializedEvent
```

There is no faction hook, no `StartingPopulationSpec`, and `GameModeSpec` (Easy/Normal/Hard) is shared by all factions — setting `StartingAdults: 0` would break every faction.

**Fix (C#, ~60 lines, no Harmony):** an `ILoadableSingleton` bound in the `Game` context that handles `NewGameInitializedEvent` (internal, publicized). If the active faction is Wardens: enumerate the freshly spawned beavers, delete them via `EntityService.Delete`, and call `BotFactory.Create(position)` the same number of times. `BotFactory.Create(Vector3)` is public in `Timberborn.Bots`. `StartingBuildingPlacedEvent` is the public alternative but fires *before* beavers spawn, so the internal event is the right one.

Risk to test first: **can bots staff the Breeding Pod?** If the pod is beaver-only, Act II needs a different bridge (e.g. a Wardens "Incubator" building cloned from the pod with the worker restriction removed).

## 5. Spike B — nothing in vanilla lets a *building* emit contamination

- `Timberborn.WaterContaminationBuildings` contains only `ContaminationBlockableBuilding` — a consumer (buildings that stop working when flooded with badwater), not a source.
- Water buildings (`WaterInput`, `StreamGauge`) *read* `ContaminationPercentage`; `WaterOutput` dumps clean water only.
- Contamination sources are `WaterSource` map entities carrying `WaterSourceContamination` (`SetContamination(float)` is public) and badtide weather.
- `ISoilContaminationService` is read-only. The writers `SetSoilContamination` / `SetContaminationLevel` exist on the internal `SoilContaminationSimulator` in `Timberborn.SoilContaminationSystem`.

**Fix (C#, two options):**

1. **Soil route** — a `PollutingBuilding` component that writes contamination into the soil arrays directly. **Second look says no:** `SoilContaminationSimulator.StartParallelTick` hands `_contaminationLevels` to a parallel task that recomputes soil contamination from the water columns every tick, and the only writers (`SetContaminationLevel` on the service, `SetSoilContamination` on the terrain material map) are the *outputs* of that recompute, not inputs to it. Anything we write would be overwritten on the next tick unless we also inject into the candidate arrays the task reads, which are private to the simulator.
2. **Water route** — the building owns a hidden `WaterSource` entity with `WaterSourceContamination.SetContamination(1.0)` (public API). The water sim then contaminates the water and the soil sim contaminates the ground from it, exactly the vanilla badwater pipeline, with rendered badwater for free. Couples pollution to water flow: a dry site pollutes nothing, which is a reasonable rule ("the sludge has to go somewhere").

Prototype option 2. The scaffold's `PollutingBuilding` component is stubbed for it.

Cleanup already exists in vanilla: contamination decays once the source stops, and `Timberborn.SoilBarrierSystem` provides barriers. Act II tech = those buildings plus a Wardens "Scrubber" variant.

## 6. Blueprint skeleton

Mod layout follows the versioned convention both faction mods on disk use:

```
Wardens/
  version-1.1/
    manifest.json               { "Id": "Wardens", "MinimumGameVersion": "1.1.2.0", "RequiredMods": [] }
    Factions/Faction.Wardens.blueprint.json
    Collections/                TemplateCollection.Buildings.Wardens, .Characters.Wardens,
                                NeedCollection.Wardens, GoodCollection.Wardens, MaterialCollection.Wardens
    Needs/                      Need.Bot.Calibration, .Telemetry, .Firmware, .Uplink
    Goods/                      Good.DataCore, Good.Firmware, Good.FieldSamples, Good.Sludge
    Recipes/
    BlockObjectToolGroups/
    Localizations/enUS.csv
    Materials/  Sprites/        recolors via MaterialPatcher — no AssetBundles in v0.1
    Wardens.dll                 PollutingBuilding + WardensStartingPopulation (Spikes A + B)
  thumbnail.png
```

Faction blueprint, trimmed to what matters:

```json
{
  "FactionSpec": {
    "Id": "Wardens",
    "Order": 5200,
    "TemplateCollectionIds": ["Buildings.Wardens", "Characters.Wardens", "NaturalResources.Wardens"],
    "NeedCollectionIds":     ["Wardens"],
    "GoodCollectionIds":     ["Wardens"],
    "MaterialCollectionIds": ["IronTeeth", "Wardens"],
    "StartingBuildingId":    "DistrictCenter.Wardens",
    "BlueprintModifiers": [
      { "Original": "buildings/paths/path/path.blueprint", "Modifier": "Factions/Path.WardensModifier.blueprint" }
    ],
    "DisplayNameLocKey": "Faction.Wardens.DisplayName",
    "DescriptionLocKey": "Faction.Wardens.Description"
  },
  "FactionWonderSpec": {
    "WonderCompletionFlavorLocKey":  "Faction.Wardens.WonderCompletionFlavor",
    "WonderCompletionMessageLocKey": "Faction.Wardens.WonderCompletionMessage"
  }
}
```

A Data need, cloned from the shape of Emberpelts' `Need.Fuel`:

```json
{
  "NeedSpec": {
    "Id": "Calibration", "NeedGroupId": "BasicNeeds", "CharacterType": "Bot",
    "StartingValue": 0.8, "DailyDelta": -0.4, "FavorableWellbeing": 2, "UnfavorableWellbeing": -2,
    "DisplayNameLocKey": "Need.Calibration.DisplayName"
  },
  "PunitiveNeedSpec": { "Penalties": [ { "Id": "WorkingSpeed", "MultiplierDelta": -0.5 } ] }
}
```

## 7. v0.1 scope — Act I only

Ship the cold start and nothing else. If bots-in-a-wasteland with Data needs is fun for one in-game drought, the rest of the arc earns itself.

1. Faction blueprint, bot template, four Data needs, `DataCore` + `Firmware` + `Sludge` goods, one Observer recipe.
2. Reuse Iron Teeth buildings for everything; recolor via MaterialPatcher. **Zero asset bundles.**
3. `WardensStartingPopulation` (Spike A).
4. `PollutingBuilding` on the Observer and the Sludge refinery (Spike B, soil route).
5. One wasteland map.
6. Timberbot API sanity: `/api/beavers` must report bots with the new needs; `/api/science` unchanged.

Explicitly out of v0.1: breeding pods, remediation tech, field studies, the Ark, custom models, custom beaver textures.

## 8. Open questions

- Can bots work the Iron Teeth Breeding Pod? (blocks Act II design)
- Exact id of the vanilla contamination liquid good, if any, for the fuel chain.
- Does the hidden `WaterSource` need terrain below it, or can it sit inside the building's footprint? (Spike B, water route)
- Do `CharacterType: "Bot"` needs show in the vanilla wellbeing UI, or only in the bot panel? Affects how "unhappy" the Act I misery reads to the player.
- Faction name.

## 9. References on this machine

- Faction mods with `version-1.1` folders: **Emberpelts** (`workshop/content/1062090/3346318229`) and **Leaf Coats** (`3552203634`), both by Bobingabout, both requiring `BobingaboutScriptPack`.
- Local blueprint-only mod as a minimal example: `Documents/Timberborn/Mods/storage`.
- Modding stack already subscribed: TimberAPI, Harmony, TimberCommons, Mod Settings, Moddable Timberborn.
- Decompile with `ilspycmd` 8.2.0.7535 (the latest tool version does not install on .NET SDK 8); game assemblies at `F:\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`. See [`../docs/devenv.md`](../docs/devenv.md).

## Status 2026-09-03 (evening): tutorial, in-game MCP, Timberbot merged

- **Tutorial gate found and used.** `TutorialService` only loads tutorials for the faction carrying
  `StartingFactionSpec`; the Wardens now carry it. The 18 vanilla tutorials are switched off for the
  Wardens with one faction blueprint modifier (`RequiredTutorialIds: ["Wardens.NeverFinished"]`), which
  also confirmed that `FactionSpec.BlueprintModifiers` apply to *any* blueprint path, not just buildings.
- **Story tutorials shipped:** `Wardens.ColdBoot` (4 cards) and `Wardens.Chapter1` (4 stages), plus two
  goal steps vanilla lacks (`GoodStockStepSpec`, `BotsChargedStepSpec`). See `wardens/README.md`.
- **Cold Boot cutscene:** `Cutscenes/ColdBoot.json` played by `WardensCutscenes` (paused, speed-locked, letterbox, three captions, 22 s, skippable); the system is [`wardens-cutscenes.md`](wardens-cutscenes.md).
- **Timberbot is compiled into the Wardens mod** (`wardens/src/Timberbot/`), and an **MCP server runs
  inside the game** (`WardensMcpServer`, port 8090) with chat, pointer, camera and Timberbot passthrough
  tools. `wardens/playtest/PLAYTEST.md` has the tool table.
- Not yet verified in-game: this whole batch (built and deployed, awaiting a run).

## Status 2026-09-03 (later): chapter gating

- **Chapter unlock service shipped** (`WardensChapterService`, `wardens/src/WardensChapters.cs`): nine
  buildings now ship padlocked (`ScienceCost: 999999`) and open in five chapters as the tutorial line
  advances (Badwater, Signal, Pods, Power, Green); toast + chat line per chapter, MCP `chapter` tool,
  `tools/validate.py` cross-checks the table. Details in `wardens/README.md`.
- **The wasteland map shipped** (`wardens/src/Maps/Wardens Wasteland.timber`, generated by
  `wardens/tools/gen_map.py`; design and format notes in `wardens-wasteland.md`). Badwater river from the
  north edge, the Sump beside the Core, ruin clusters, one clean spring in the north-east.
- **Campaign maps researched** ([`wardens-campaign-maps.md`](wardens-campaign-maps.md), 2026-09-05): one map per
  level means a runtime installer into `Documents/Timberborn/Maps`, a `campaign.json` for continuity, and a
  transition through the new-game path once `GameSceneLoader` / `NewGameConfiguration` are confirmed in the decompile.
  The system design is [`wardens-campaign-design.md`](wardens-campaign-design.md); the five-level concept
  (First Light, The Sump, The Pods, Green, The Ark) is [`wardens-campaign-concept.md`](wardens-campaign-concept.md); the full
  ten-level arc, in which the Ark's human payload is unrecoverable and the beavers are the someone the firmware meant,
  is [`wardens-campaign-arc.md`](wardens-campaign-arc.md) with its research plan [`wardens-campaign-research-plan.md`](wardens-campaign-research-plan.md).
  The arc supersedes the "revive humans" ending in §1 once it is adopted; until then §1 stands as the Wardens' belief.
- Not yet verified in-game.
