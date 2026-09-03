# The Wardens — Chapter 1 plan: identity, starting buildings, gating, cutscene, art, UI

> **Status:** plan (2026-09-03), grounded in the 1.1.2.4 blueprint dump and decompile. Builds on [`faction-wardens.md`](faction-wardens.md) (the arc) and [`model-catalog.md`](model-catalog.md) (what art exists). Nothing here is implemented yet; the v0.1 scaffold in `wardens/` is the base it lands on.

## 1. What makes a Warden

Five pillars. Every building, resource and rule below should be traceable to one of them.

1. **Data over comfort.** Wardens don't have wellbeing, they have *signal quality*. Every "need" is a data channel (Calibration, Telemetry, Firmware, Uplink). A happy Warden is a well-informed one.
2. **Power is life.** No food, no water. A Warden that runs out of charge stops. Everything traces back to the Core's output, and research literally burns power.
3. **Poison is the price.** Their fuel is badwater, their industry kills soil. Not a penalty — the *plot*. The land they poison is the land they will later have to heal.
4. **Salvage, don't harvest.** A wasteland has no forests. Wardens strip ruins for scrap and farm the only thing that grows in poison. Wood arrives in Act II, with the beavers.
5. **Built for successors.** Every research goal is about someone else living here. The faction's UI copy speaks in the future tense: *"when they come"*.

Visual language: rusted Iron Teeth metal, black slag, warning-stripe yellow, and one accent — **cyan data-light** on anything that carries Data. Sound set stays `Common`.

## 2. The economy in Chapter 1 (bots only, no wood, no water)

```
Ruins ──scavenge──▶ ScrapMetal ──Core furnace──▶ MetalPart      (build material)
Sludge Reed ──harvest──▶ Biomass ──Sludge Burner──▶ power       (second generator)
Badwater + MetalPart ──Cruncher──▶ DataCore                      (research + Firmware)
Core ──charges──▶ bots (Energy)   Core ──shaft──▶ Cruncher (power)
```

**New resource: Sludge Reed.** A crop that grows *because* the soil is contaminated. Vanilla plants carry `ContaminatedNaturalResourceSpec` (die on contaminated soil); a plant that omits it is immune, which is enough for v0.1 ("grows anywhere, thrives in the wasteland"). "Grows *only* on contaminated soil" needs a tiny C# component that reads `ISoilContaminationService.SoilIsContaminated` in the growth tick — Chapter 2. Model: vanilla `Cattail` meshes with a black/cyan recolor. Yields `Biomass`.

**Scrap.** Vanilla ruins already yield scrap through the Scavenger Flag (`ScavengerFlag` exists in both factions). We just make scrap the *only* metal source in Chapter 1.

## 3. The three starting buildings

### 3.1 The Core (district center)

The district center *is* the faction's reactor. One building does three vanilla jobs:

| Job | Spec (all exist in vanilla) | Value |
|---|---|---|
| District center + builder hub | `DistrictCenterSpec`, `BuilderHubSpec`, `WorkplaceSpec` | copied from `DistrictCenter.IronTeeth` |
| Charges bots | `AttractionSpec { Effects: [{ NeedId: "Energy", PointsPerHour: 0.6, SatisfyToMaxValue: true }] }` + `FixedSlotManagerSpec` | copied from `ChargingStation.IronTeeth` (4 slots) |
| Generates power | `MechanicalNodeSpec { PowerOutput: 150 }` + `MechanicalConnectorTargetSpec` | between a compact water wheel (120) and a geothermal engine (400) |

Model: **kitbash** — `DistrictCenter.IronTeeth.Model` as `#Finished`, with `ChargingStation.IronTeeth.Model` nested twice on the flanks, recolored rust + cyan. Blueprint: `Buildings/DistrictManagement/Core/Core.Wardens.blueprint.json`, `StartingBuildingId: "Core.Wardens"`. Placed finished, cost 0, like every district center.

Design note: 150 power is enough for the Cruncher at *reduced* input (see 3.2) **or** two Sludge Burners' worth of lights, not both. That tension is Chapter 1.

### 3.2 The Cruncher (research that burns power)

`Numbercruncher.IronTeeth` re-specced:

- `MechanicalNodeSpec.PowerInput`: 500 → **120**, so the Core alone can run it, barely.
- `ManufactorySpec.ProductionRecipeIds`: `["DataCore", "SciencePointsCruncher"]` — Data Cores *or* science, never both, so the player chooses between research and Firmware.
- `BuildingSpec.BuildingCost`: `MetalPart 20` (scrap economy, no planks).
- `ScienceCost: 0` in Chapter 1 (it is the chapter's goal building, see §4).

Model: Numbercruncher mesh, recolor, cyan emissive on the drums.

### 3.3 The Sludge Burner (second power source, first choice)

`Engine.IronTeeth` (the wood-fuel engine) re-specced with `Fuel: "Biomass"`, `PowerOutput: 200`. It is the building that makes Sludge Reed matter and it is the first *polluter*: it carries `PollutingBuildingSpec { Radius: 2, Strength: 0.6 }` (Spike B). The player's first power upgrade is also their first ecological debt — pillar 3, on screen.

Model: Engine mesh, recolor, black smoke via the existing `PowerGeneratorParticleControllerSpec`.

**Also on the bar in Chapter 1, unchanged from vanilla:** Path, Scavenger Flag, small metal storage pile, Power Shaft. That's **seven buttons total** in Chapter 1.

## 4. Chapters and goal-gated unlocks

### 4.1 How gating works

Verified mechanics:

- `BuildingUnlockingService.Unlock(spec)` / `UnlockIgnoringCost(spec)` / `Unlocked(spec)` are public (`Timberborn.ScienceSystem`). A building with `ScienceCost > 0` shows locked in the bar until unlocked.
- `TemplateSpec.RequiredFeatureToggle` exists but `FeatureToggleService` is a static dev-toggle facility, not a runtime gate. Don't use.
- Vanilla's own `FactionGoalsUnlocker` is a 40-line `ITickableSingleton` that checks a condition every tick and calls an unlocking service. That is the exact shape we need.

So: **`WardensChapterService`** (C#, `ITickableSingleton`, saves current chapter). Each chapter is a small class with `IsComplete()` and a list of building template names to unlock. Chapter-locked buildings ship with `ScienceCost: 999999` so the vanilla UI shows the padlock; on completion we call `UnlockIgnoringCost` for the chapter's list, post a notification, and start that chapter's tutorial (§5). Locked buildings stay *visible* in the bar; hiding them entirely needs a hook into `ToolButtonSystem` — open question, and arguably visible-but-locked is better UX ("this is what you're working toward").

### 4.2 The chapters (Act I only)

| # | Name | Bar | Goal (checked every tick) | Unlocks |
|---|---|---|---|---|
| 0 | Cold Boot | — | cutscene ends | — |
| 1 | **First Light** | Core, Path, Scavenger Flag, Metal Pile, Power Shaft, Cruncher, Sludge Burner | 20 `MetalPart` in stock **and** every bot ≥ 50% Energy for one full day | Sludge Reed planting, Water Pump (badwater), Large Pile |
| 2 | **Signal** | + Reed Bed, Badwater Pump, storage | 10 `DataCore` produced | Calibration Rig, Uplink Mast, Firmware recipe, Contamination Sensor |
| 3 | **Calibration** | + need satisfiers | all four Data needs above 0.5 on every bot for one day | Breeding Pod (Act II begins) |

Each goal is one `ResourceCountingService` / `NeedManager` query — nothing exotic.

## 5. Start-of-run cutscene ("Cold Boot")

The vanilla **tutorial system is a sequence engine**: `TutorialSpec` → ordered `TutorialStageSpec`s, each with an `IntroLocKey` text card and typed steps — `CameraMovementStep`, `CameraRotationStep`, `CameraZoomStep`, `SetPauseStep`, `BuildingTutorialStep`, `AccumulateScienceForBuildingStep`… all data blueprints under `Tutorials/`. It already renders the text card UI and the blinking highlights.

Cold Boot uses it for the *cards* and a small C# `WardensIntroCamera` for the *shots* (the tutorial camera steps wait for the player to move the camera; they don't drive it). Sequence, ~25 seconds, all skippable:

1. `NewGameInitializedEvent` → bots have replaced beavers (Spike A). Pause. Hide UI (`UILayoutSystem`).
2. **Shot 1** — high, slow orbit over the wasteland. Card: *"Nothing has grown here in 3,000 days."*
3. **Shot 2** — push in on the Core; its cyan light comes on (material swap on the Core when the sequence reaches this step). Card: *"We were not built to live here. We were built so that others could."*
4. **Shot 3** — settle into the default gameplay angle over the Core. Card: *"Chapter 1 — First Light. Keep the machines charged. Find metal."* UI back, unpause, Chapter 1 tutorial's first stage starts (build a Scavenger Flag).

Camera poses are the same `{target, h, v, zoom, t}` keyframes proposed for `tbot shoot` in [`playtest-and-video-capture.md`](playtest-and-video-capture.md) — one interpolator serves the cutscene, the agent, and the trailer. Trigger ids for later chapters (`Chapter1CompleteTrigger`) come from the same `WardensChapterService`, mirroring how vanilla feeds `SurvivedFirstBadtideTrigger` into `RequiredTutorialIds`.

## 6. Art plan

**Rule for Chapter 1: no new meshes.** Everything is recolor, kitbash or re-spec of vanilla Iron Teeth models (see model catalog §1). What we do have to produce:

| Asset | Count | How |
|---|---|---|
| Recolor textures for Core / Cruncher / Burner / Reed (`MaterialPatcher` `_BaseMap` swaps) | 4–6 PNG 2048² | script: take the IT albedo tint toward rust, paint cyan on emissive masks |
| Faction avatars (adult, child, bot, 2 contaminated, logo) | 6 × 512² | script-drawn flat icons in the game's style, bot-first (the adult/child ones can stay IT until Act II) |
| New-game full avatar | 1 × 420×560 | same |
| Goods icons: DataCore, Firmware, Biomass, ScrapMetal reuse | 3 × 98² | script |
| Building icons: Core, Cruncher, Burner, Reed Bed | 4 × 98² | script |
| Tool group icon "Wardens" (if we add one) | 1 | script |
| Bot skin textures (`BotTexturesSpec`) | 3 × 2048² | recolor of `Bot.IronTeeth` texture: dark chassis, cyan eyes |
| Wonder completion image | later | — |

When Blender + the timbermesh add-on are installed, the first *real* meshes in priority order: Sludge Reed (3 growth stages), Core silhouette (so the faction is recognisable from the air), Uplink Mast. All are box/cylinder kitbashes a headless Blender script can build.

## 7. UI elements we touch

| Element | Mechanism | New work |
|---|---|---|
| Bottom bar | `BlockObjectToolGroupSpec` + `PlaceableBlockObjectSpec.ToolGroupId/ToolOrder` | either reuse IT groups with a trimmed template collection, or one custom **"Wardens"** group with all seven Chapter 1 buildings — the latter is clearer for "fewer buttons" |
| Locked buildings | vanilla padlock via `ScienceCost` | none |
| Chapter card / objectives | vanilla tutorial panel (`TutorialStageSpec.IntroLocKey`) | localization only |
| Chapter-complete toast | `NotificationSpec` / notification service | one C# call |
| Cutscene text cards | tutorial intro cards; if they prove too small, a UXML overlay built like `TimberbotPanel` (UI Toolkit) | maybe |
| Building panel for the Core | vanilla fragments render from specs (attraction slots, power, district) | none |
| Data needs in the bot panel | vanilla need list; `CriticalNeedSpec.SpriteName` icons | 4 need icons |

## 8. Work plan (in order, each step playable)

1. **Core + Cruncher + Burner blueprints** (re-spec + nest, IT models untouched) and the `Wardens` tool group. Trim `TemplateCollection.Buildings.Wardens` to the Chapter 1 seven. → Playable: bots, one bar, power tension.
2. **Sludge Reed + Biomass** (Cattail re-spec, no contamination-death spec). → Second generator becomes buildable.
3. **`WardensChapterService`** with Chapter 1 → 2 unlock. → Goal gating works.
4. **Tutorial blueprints** for Chapter 1 stages (text only). → Objectives on screen.
5. **`WardensIntroCamera`** + Cold Boot stages. → Cutscene.
6. **Recolors + 2D icons** via script. → Looks like a faction.
7. `PollutingBuilding` water-route implementation on the Burner (Spike B). → Pillar 3 becomes visible.

Steps 1–4 are JSON plus ~150 lines of C#. 5 shares code with the Timberbot camera work. 6 is the first time Pillow gets involved. Blender is not on the critical path for any of it.

## 9. Open questions

- Can bots work the Scavenger Flag and the Reed Bed? (Vanilla bots take any workplace; verify in the smoke run.)
- Does `AttractionSpec` on a `DistrictCenterSpec` building coexist with `BuilderHubSpec` slots, or do slot managers collide? Kitbash the charging station as a *nested child entity* if they do.
- Hide vs. padlock for chapter-locked buttons (§4.1).
- Do tutorial text cards render large enough to carry a cutscene, or do we need the overlay?

## Status 2026-09-03

§5 is implemented as a paused camera orbit (`WardensColdBoot`, `WardensCameraDirector`) with the cards
coming from `Tutorials/Stages/Wardens.ColdBoot.*`. The Chapter 1 tutorial exists with Iron Teeth
placeholder templates (`ScavengerFlag.IronTeeth`, `Path`) and the two goal steps; the Core, Cruncher and
Sludge Burner from §3 are still the next build step, and the chapter unlock service from §4 hooks onto
`TutorialFinishedEvent("Wardens.Chapter1")`.

## Status 2026-09-03, later

§3 is generated (`wardens/tools/gen_buildings.py`): Core (district center + 150 hp; charging moved to a
separate Charging Post because no vanilla building combines WorkplaceSlotManager with FixedSlotManager),
Cruncher (120 hp, science or Data Cores), Sludge Burner (Biomass, 200 hp, polluting spec), plus scrap-priced
Power Shaft, Scrap Pile, Reed Bed, and the Sludge Reed crop with Biomass. Economy change from the plan: the
whole chapter runs on Scrap Metal, no Metal Part before a smelter exists. New: both breeding pods run on
Biomass + power (advanced: + Firmware), per the user's request. §4 (chapter unlock service) is next.
Badwater loop added on request: Sludge Pump (the unlocked pump, badwater only), Badwater Cell (100 hp from
Badwater, the early power source), Sludge Tank; tutorial stage "Power" before "Cruncher". Chapter 1 bar is
now 13 buttons. Art direction for leaving the Iron Teeth look: `wardens-art-path.md`.
