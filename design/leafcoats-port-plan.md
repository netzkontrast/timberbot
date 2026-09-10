# Leaf Coats → Wardens: complete port and style transfer

> 2026-09-03. Analysis of the six workshop mods now copied (local only) into `wardens/src` by
> `wardens/tools/import_leafcoats.py`, and the file-by-file plan to turn that content into the
> Wardens. Everything here is Bobingabout's work; the port stays on this machine until there is
> permission to publish.

## 1. What was analysed

| Mod (workshop id) | Role | What is in it |
|---|---|---|
| Leaf Coats 1.1.2.2 (3552203634) | the faction | 139 loose files: faction spec, 11 collections, 12 goods, 14 needs, 15 recipes, 14 worker outfits, 6 tool groups, 8 decals, 5 material patches, 5 goods visualizations, avatars + skins; **`AssetBundles/leafcoats_win` (113 MB, Unity 6000.5): 243 building/plant blueprints, 750 timbermesh, 35 materials, 104 PNG (icons), 26 prefabs** |
| Leaf Coats: Badwater (3556403458) | add-on | 1 template collection: BadwaterPump, Centrifuge |
| Leaf Coats: Explosives (3556403578) | add-on | collections: ExplosivesFactory, 3 dynamites, Detonator, FireworkLauncher + goods |
| Leaf Coats: Beaver Plants (3621479902) | add-on | bundle (BeaverPlant, AdultBeaverPlant), `LeafCoats.BeaverPlants.dll`, split-breeding setting |
| Bobingabout Script Pack 1.1 (3416879061) | **the code** | 14 DLLs, 35 `[Context("Game")]` configurators, ~70 custom `*Spec` records (list in §3), plus custom beavers/bots content |
| Vertical Nav Mesh 1.0.4 (3631491885) | code | `VerticalNavMesh.dll`, one mesh, `VerticalNavMeshSpec` (ladders / vertical paths) |

The building blueprints live **inside the bundle** as text assets; offline readers fail on this Unity
version, so the exact specs per building are read with the new in-game MCP tool `dump_assets`
(`what=blueprints filter=LeafCoats`, `what=materials`, `what=textures filter=LeafCoats`) while
the copies are loaded. The plan below is written from the manifest, the collections, the loose
files and the decompiled code; the dump confirms per-building details before each batch.

## 2. The rules that decide the port

1. **Ids.** Every `*.LeafCoats` template, collection id `*.LeafCoats`, faction id, loc-key suffix
   and `FactionID` filter becomes `*.Wardens`. Blueprint *paths* inside the bundle cannot change
   (they are bundle assets), so ported buildings are **new loose blueprints in `wardens/src`** that
   reference the bundle's meshes, materials and icons by their existing paths. Nothing inside the
   bundle is edited.
2. **Bots, not beavers.** The Wardens are bots in Act I: every food chain, nutrition need, housing
   for beavers and "beaver wellbeing" building is either dropped for Act I, kept but re-purposed
   (housing → charging bays), or reserved for Act II/III when beavers appear from the pods.
3. **Style transfer, three layers.** (a) Materials: the 30 `Materials/UberAtlas/LeafCoats/*` atlas
   materials are Leaf Coats' whole palette; `WardensMaterialPatcher` gets a color/texture table for
   them (greens → cyan-steel, browns → charcoal, white plaster → cool grey, `Flags`/`Hedge`/`Moss`
   → replaced textures). (b) Illumination: every lit building gets `DefaultIlluminatorColorSpec:
   WardensCyan`. (c) 2D: the 104 icons and 12 good icons are dumped and run through
   `tools/recolor_assets.py` (already proven on the avatars and skins).
4. **Code stays as-is.** The Script Pack DLLs are used unmodified; we only write the specs they
   read. Only `MaterialPatcher`, `CharacterCustomizer` (`BotTexturesSpec`), `DynamicConstructionStages`,
   `MergeableObject`, `RandomVariant`, `Misc`, `Tubeway`, `StockpileVisualizer`, `Wonder` and
   `VerticalNavMesh` are needed by the buildings we keep; `GatherablePollination`, `Needs`
   (PlantMurderer), `ToggleableProcreationHouse`, `PneumaticTubes`, `BeaverPlants` are not.

## 3. Script Pack: what each assembly gives us

| Assembly | Specs | Used by the port for |
|---|---|---|
| MaterialPatcher | `MaterialPatchSpec {FactionID, MaterialName, TextureEntries, ColorEntries, NumberEntries}`, `MaterialCloneSpec` (+`NewMaterialName`), `BuildingPrefabMaterialPatchSpec`, `ShaderPatchSpec`, liquid variants | the whole style transfer: JSON-driven, faction-filtered, color *and* texture, and it can **clone** a material under a new name (a Wardens copy of an atlas material without a Unity build). Replaces our C# patcher for anything beyond the bot skin. |
| CharacterCustomizer | `BotTexturesSpec {FactionID, BotTextures[]}`, `CharacterAvatarMapSpec`, `BeaverGrowUpTextureMapSpec`, `CustomBeaverByNameSpec`, `CustomBotByNameSpec`, `CustomBotSetSpec` | random bot skins per faction (several Warden liveries), avatar-per-texture, named special bots (the five founders). |
| DynamicConstructionStages | `DynamicConstructionStagesSpec` | keep for buildings that use it (tree structures); harmless elsewhere. |
| MergeableObject | `Bobingabout*MergeableObjectModel*Spec`, `EdgeSpec` | hedges, fences, roofs that join neighbours; keep for Wardens fences/pipes. |
| RandomVariant | `BobingaboutRandomVariantSpec {VariantNames}` | random mesh variants (shrubs); keep. |
| Misc | door toggles, dynamic path models, branch walls, disable-fire, good-consuming fire/range effects, district gate driveway, inventory fixer, path placer, rubble spawner, sky bridge, tunnel | needed by Paths, District Gate, Branch/TreeTrunk set, decoration. |
| Tubeway | tube platforms/bridges | Iron-Teeth-style tubes; useful later for the Wardens' vertical city. |
| StockpileVisualizer | pile/column visualizers | piles and warehouses. |
| Wonder | `BobingaboutDiscoWonder*Spec` | the "Disco Rave" wonder: drop; the Ark is ours. |
| AutomatedManufactory | power switch, particles, water-source production increaser | Refinery/Centrifuge-type buildings; keep where used. |
| GatherablePollination, Needs, ToggleableProcreationHouse, PneumaticTubes | beehive, PlantMurderer need, housing toggles, tubes | beaver-era features: not ported. |
| VerticalNavMesh | `VerticalNavMeshSpec` | ladders and vertical shafts (Ladder.*, LadderTunnel, MetalPlatform*.Ladder). Keep. |

## 4. File-by-file plan (loose files, by folder)

`wardens/src/Factions/Faction.LeafCoats.blueprint.json` → **merge into `Faction.Wardens`**: take
`MaterialCollectionIds` (add `Wardens` collection, see below), `PathMaterial`/`BaseWoodMaterial`
(point at the cloned Wardens atlas materials), `TemplateCollectionIds` → `Buildings.Wardens`,
`Characters.Wardens`, `ModularShaftParts.Wardens`, `NaturalResources.Wardens`. Their three modifiers
(`Path`, `NeedCollection.Common`, `GoodCollection.Common`) → ours: the Path modifier is unnecessary
(vanilla Path already has no faction suffix), the need removal (Campfire, RooftopTerrace) and goods
removal (Explosives, Fireworks) become Wardens modifiers. Delete the LeafCoats faction file after.

`Collections/`
- `TemplateCollection.Buildings.LeafCoats` (217) → `Buildings.Wardens`: rebuilt from the keep-list
  in §5; paths point at our new loose blueprints. Delete theirs.
- `TemplateCollection.Characters.LeafCoats` (BeaverAdult, BeaverChild, Bot.LeafCoats) →
  `Characters.Wardens`: `Bot.LeafCoats` (bundle blueprint, "Treebot") re-specced as `Bot.Wardens`
  loose blueprint; beavers stay vanilla for Act II.
- `ModularShaftParts.LeaCoats` → `.Wardens` referencing the same 20 gear/shaft prefabs.
- `NaturalResources.LeafCoats` (Dandelion, GrapeVine, Eucalyptus, Willow, AppleTree, Mangrove,
  ChestnutTree, Maple) → Act III only; Act I keeps `SludgeReed`. Keep the file renamed but out of
  the faction until Chapter 4 ("Green").
- `MaterialCollection.LeafCoats` (32) → `MaterialCollection.Wardens` listing the **cloned** materials
  `Materials/UberAtlas/Wardens/*` created by `MaterialCloneSpec` (one JSON per material, §6).
- `GoodCollection.LeafCoats` (20) → keep only `TreatedPlank`, `BotChassis`, `BotHead`, `BotLimb`,
  `Shovel`, `Lubricant`, `Bark`, `Branch`; drop fruit/food goods; add ours (DataCore, Firmware,
  Biomass).
- `NeedCollection.LeafCoats` (36) → keep `Energy`, `Lubricant`, `ControlTower`, and the wellbeing
  buildings we port as *bot boosts*; drop all Nutrition needs. `NeedCollection.Folktails/IronTeeth.
  LeafCoatsModifier` (adds Campfire, RooftopTerrace to other factions): delete.
- `*LeafCoatsAddons` (empty collections used by the add-ons): rename to `WardensAddons`; the
  Badwater and Explosives collections append to `Buildings.Wardens`.

`Goods/` (12): keep `Lubricant` (→ bot need, rename key), `Shovel`, `Bark`, `Branch`, `Leaf`;
drop `Apple`, `Grapes`, `FancyApples`, `FermentedChestnut/Dandelion/Fruit`, `FruitSalad`. Icons come
from the bundle (`Sprites/Goods/*Icon`), recolored.

`Needs/` (14): keep `Need.Bot.Lubricant` (Boosts, matches our Data needs' shape); the 13 beaver
needs are Act II material; move them to `Needs/ActII/` and out of the collection.

`Recipes/` (15): keep `Shovel`, `Lubricant`, `MetalBlock`, `ScrapMetal.Efficient`, `Plank.Press`,
`Log.Press` (branches → planks: the Wardens' wood substitute once trees exist), `Water.Large`,
`SciencePoints.GeothermalNumbercruncher`; drop the six food recipes and `Antidote`, `Extract.Extracted`.

`WorkerOutfits/` (14): keep the 7 `*.Bot.LeafCoats` files renamed `FactionId: Wardens`; the beaver
outfits wait for Act II.

`BlockObjectToolGroups/` (6): `Platforms`, `TreeBuildings`, `Zipline`, `Industry`, `Dams`,
`Landscaping.optional` are global groups. Rename `TreeBuildings` → `Scaffolds` (the Wardens build
gantries, not trees), keep the others, recolor the two group icons.

`Decals/` (8): banners already redone; the 4 `Tail*.LeafCoats` decals are beaver tail decals → Act II.

`MaterialPatcher/` (5): replace with Wardens patches (§6). `GoodsVisualization/` (5, dirt/liquid
planes): rename suffix, keep. `RemoveYieldStrategies/Uncuttable` (adds `Pruning`): keep as is.
`CharacterCustomizer/` (2): rewrite for Wardens textures; add `BotTexturesSpec` with 3 bot liveries.
`CustomBeaverByName/` (eMka, Norman): delete (personal tributes). Script Pack's `CustomBeavers/`,
`CustomBotByName/`, `CustomBeaverNamesList/`: delete from our copy, they are the pack's own examples.

`Localizations/enUS_LeafCoats.csv` (163 rows): rewrite as `enUS_WardensPort.csv` with the same keys
under Wardens names and new flavor text (bot voice; see the tutorial cards for tone). `deDE`, `ukUA`:
drop until the English is final.

`Scripts/`: keep `BobingaboutScriptPack/*.dll` except `GatherablePollination`, `Needs`,
`ToggleableProcreationHouse`, `PneumaticTubes`; keep `VerticalNavMesh.dll`; drop
`LeafCoats.BeaverPlants.dll` and its bundle.

## 5. Buildings (bundle blueprints → new loose Wardens blueprints)

Per building the change is the same five edits: `TemplateName` suffix, `LabeledEntitySpec` loc keys,
`BuildingCost` (scrap economy: Scrap Metal / Metal Part / Treated Plank), `ScienceCost` (chapter
padlock), `DefaultIlluminatorColorSpec: WardensCyan` where an illuminator exists. Model, colliders,
construction stages, transputs and Script Pack specs are copied verbatim from the dumped JSON.

| Folder (count) | Keep for Act I | Re-purpose | Act II/III | Drop |
|---|---|---|---|---|
| DistrictManagement (5) | DistrictCenter → **Core** (+150 hp), BuildersHut, HaulingPost, DistrictCrossing, DistrictGate | | | |
| Power (18) | PowerShaft, VerticalPowerShaft(+Tunnel), Clutch, GravityBattery(+TreeTrunk variants as "Gantry battery"), GeothermalEngine, WindTurbine, LargeWindmill, WaterWheel, PowerDam, PowerLevee(+Tunnel) | PowerTreadmill → bots walking = "Treadmill" stays | | |
| Science (9) | ChargingStation (+.Tree → "Mast charger"), BotPartFactory, BotAssembler, GeothermalNumbercruncher → Cruncher II, Inventor → Data Desk, Refinery, ControlTower → Uplink Mast, Observatory → Telemetry Dish | | | |
| Water (12+2) | WaterPump/LargeWaterPump/MechanicalPump/CompactMechanicalPump, BadwaterPump (add-on) → **Sludge Pump II**, BadwaterRig, BadwaterSeepRig, SeepCover, BadwaterDome, Centrifuge (add-on), Discharge, AquiferDrill | | | |
| Metal (3) | ScavengerFlag, Shredder, Mine | | | |
| Storage (11) | all piles, warehouses, tanks | | | |
| Housing (4) | | HousingUnit.* → **Charging Bays** (bots sleep here: AttractionSpec Energy instead of DwellingSpec) | beaver housing later | |
| Wood (8) | LumberjackFlag, PruningFlag, Forester → Planter Rig, LumberPress (branches → planks) | GearWorkshop, LumberMill, WoodWorkshop, TappersShack | | |
| Food (5) | | | GathererFlag/Hut, FoodProcessor, Fermenter | Beehive |
| Wellbeing (17) | WindTunnel, Detailer, TeethGrindstone → "Calibration Rig" (Calibration need), Shower → "Coolant Shower" (Telemetry), ContemplationSpot(.Branch) → "Uplink Bench" (Uplink), Carousel/DancePit/SwimmingPool/MudPit → Act II | MedicalBed(+Triple), Herbalist → "Repair Bay" | Garden, DomedGarden, ObservationTerrace, Plaza | |
| Monuments (5) | | LaborerMonument, TributeToIngenuity, FountainOfJoy, HallOfAbundance (recolored) | | Wonder (Disco Rave; the Ark replaces it) |
| TreeBuildings (27) | all Branch/TreeTrunk/BranchPlatform pieces → **Scaffold** set (gantries, catwalks); same meshes, steel tint | | | |
| Paths (20), Platforms (13), Dams (13), Landscaping (13), Automation (19), Decoration (26), Zipline (5) | all, renamed; hedges/shrubs become cable bundles and fences by tint | | | |
| Plants (8), NaturalResources (1) | | | Act III trees (Apple, Chestnut, Maple, Eucalyptus, Willow, Mangrove), Dandelion, GrapeVine, BlueberryBush | |
| Characters/Bot (1) | Bot.LeafCoats → **Bot.Wardens** with our skin and 3 liveries via `BotTexturesSpec` | | | |

Explosives add-on (6): keep as Chapter 3 unlock (terraforming). Beaver Plants: drop.

## 6. Style transfer: the material table

`dump_assets what=materials filter=LeafCoats` lists each atlas material with its `_BaseMap`/`_BaseColor`.
For each of the 30 write `MaterialPatcher/MaterialClone.Wardens.<Name>.blueprint.json`:

```json
{ "MaterialCloneSpec": { "FactionID": "Wardens", "MaterialName": "BaseWood_Green.LeafCoats",
    "NewMaterialName": "BaseWood_Green.Wardens",
    "TextureEntries": [{ "Name": "_BaseMap", "Path": "Materials/UberAtlas/Wardens/BaseWood_Green.Wardens" }],
    "ColorEntries": [], "NumberEntries": [] } }
```

with `Materials/UberAtlas/Wardens/*.png` produced by `tools/recolor_assets.py` from the dumped
`textures/` (same hue map as the avatars: greens → 196–214°, browns → slate, cyan glow on highlights;
`Flags`, `Hedge`, `Moss`, `ThatchedRoof` get a second pass that flattens them to cable/plate textures).
The faction's `MaterialCollectionIds` then lists the Wardens clones; the meshes keep referencing the
LeafCoats material *names*, which the Script Pack redirects per faction. Result: every one of the 750
meshes appears in Wardens colors without touching the bundle.

## 7. Order of work

1. Run the dump (`dump_assets` ×3) with the copies loaded; commit nothing from it.
2. Materials first (§6): clones + recolored atlas → every mesh already looks Wardens.
3. Generator `tools/gen_port.py`: reads `dump/blueprints/**/*.LeafCoats.blueprint.json`, applies the
   five edits and the keep-list, writes `wardens/src/Buildings/**.Wardens.blueprint.json`, collections,
   loc rows. Batch order: DistrictManagement + Power + Science + Water + Metal + Storage (Chapter 1–2),
   then Scaffolds/Paths/Platforms/Dams/Landscaping/Automation/Decoration, then Wellbeing re-purposes.
4. Bot: `Bot.Wardens` + `BotTexturesSpec` + `CharacterAvatarMapSpec`.
5. Remove the LeafCoats faction, collections and loose blueprints from the copy (`import_leafcoats.py
   --remove`, then keep only `AssetBundles/leafcoats_win*` and the needed `Scripts/`).
6. Tutorial/chapters point at the new templates; `wardens-chapter-1-plan.md` §3 buildings become
   the Leaf Coats meshes.

Licensing stays the gate for anything beyond this machine.

## Status 2026-09-03

Step 1 (dump) and the first slice of step 3 (generator) are done, against a live game
(`wardens/playtest/run_dump.py`, `dump_assets what=blueprints filter=LeafCoats` → 287 files under
`Documents/Timberborn/Mods/Wardens/dump/blueprints`, one clean fully-resolved JSON per building —
no re-merging needed on our end).

`wardens/tools/gen_port.py` ported the first batch: **44 buildings** (DistrictManagement, Power,
Science, Water, Metal, Storage — everything in §5's table for those folders that we did **not**
already have under a Wardens name from `gen_buildings.py`'s Iron Teeth re-specs; Core, ChargingPost,
PowerShaft, HaulingPost, the small storage tier and the badwater pump were deliberately left alone
so nothing already wired into the live tutorial/save changes shape). Each got the five edits
(TemplateName, fresh loc keys, a Scrap Metal cost tiered off its own unchanged ScienceCost, a
WardensCyan illuminator recolor where one already exists) with model/mesh/collider/transput data
copied verbatim. They live in a new `Buildings.WardensPort` collection that is **not** yet added to
`Faction.Wardens.TemplateCollectionIds` — wiring them into the bar/chapters is a separate step.
Deployed as v0.2.7; `validate.py` clean.

Blocked, needs a short live-game step: `dump_assets what=materials|textures filter=LeafCoats`
returned **zero** results (checked with no filter too, against 50k+ loaded materials — genuinely
none loaded). Unity only loads an AssetBundle's materials/textures into memory once something
references them; since no Leaf Coats building has ever been placed or previewed in this game, and
the `timberbot` placement API only accepts prefabs already in the active faction's toolbar
(`invalid_prefab` for `Dam.LeafCoats`), nothing has forced them into memory yet. Next session: add
`Buildings.LeafCoats` (or just the 44 ported ones, via `Buildings.WardensPort`) to
`Faction.Wardens.TemplateCollectionIds` for one test session, rebuild, reload the save, place (or
just preview in the build menu — worth testing whether a hover is enough) one building per material
family, dump materials + textures, then revert the collection change. That unblocks §6 (the 30
`MaterialCloneSpec` files + recolored atlas) and the icon recolor pass.

Remaining building batches (§5, unstarted): Scaffolds/Paths/Platforms/Dams/Landscaping/Automation/
Decoration/Zipline (the bulk of the 243 — mostly rename + tint, low risk, high volume), then the
Wellbeing re-purposes (need individual judgment calls per row, not a mechanical port), then
Characters/Bot → `Bot.Wardens` + `BotTexturesSpec` (blocked on the same material work above, since
the bot skin is a texture, not a blueprint).

## Status 2026-09-03, first real crash: duplicate loose files

The port's §7 step 5 ("remove the LeafCoats faction, collections and loose blueprints from the
copy") was written but never executed, and it bit: `RecipeSpecService.Load()` throws
`ArgumentException: An item with the same key has already been added. Key: Antidote.LeafCoats`
whenever a loose recipe we copied duplicates an Id that also exists inside
`AssetBundles/leafcoats_win` (the dumped copy earlier only showed one source because
`dump_assets`/`BlueprintFileBundleLoader.GetBundles` groups by *file path*, and the bundle's
internal path for the same recipe apparently doesn't contain "LeafCoats" — so the earlier
`filter=LeafCoats` dump silently missed it). Any singleton scheduled after `RecipeSpecService` in
`SingletonLifecycleService.LoadAll()` never loads at all, which is why the *next* symptom looked
unrelated: `TutorialStageService`'s stage dictionary was simply empty, so every stage lookup threw
`KeyNotFoundException`, `Wardens.ColdBoot.Badtides` included — that stage was never the bug.

Same story for the faction menu showing "Leaf Coats" twice: `Factions/Faction.LeafCoats
.blueprint.json` (also a loose copy the plan said to delete) registers a second playable faction
on top of whatever the bundle itself already contributes.

Fixed: removed the loose `Faction.LeafCoats.blueprint.json` and the loose recipes §4 always meant
to drop (`Antidote`, `Extract.Extracted`, the five food recipes). `tools/import_leafcoats.py` now
has a `SKIP_PATHS` set so a future re-run of the import won't resurrect exactly this bug.
`Wardens.csproj`'s deploy `RemoveDir` now also wipes `Factions` before each redeploy (matching
Tutorials/Buildings/Collections/Recipes) so a source deletion can't leave a stale file behind.
Deployed as v0.2.9.

**Not yet checked**: the same loose-vs-bundle duplication could exist in `Goods/`, `Needs/`,
`WorkerOutfits/`, `Decals/`, `Collections/*.LeafCoats.*`, `CharacterCustomizer/`, `MaterialPatcher/`
— every category `import_leafcoats.py` copied verbatim. Antidote was caught because
`RecipeSpecService` happens to hard-fail with `Dictionary.Add`; a duplicate elsewhere might behave
differently (silent overwrite, a different exception, or nothing at all) depending on how that
spec type's own service builds its index. Worth a systematic pass — cross-reference every loose
file's declared Id against a *fresh, unfiltered* `dump_assets what=blueprints` (no `filter=LeafCoats`,
since path-based filtering is what let Antidote slip through) — before porting more buildings.

## Status 2026-09-03, actual root cause found: the dump lived inside the mod folder

Every crash chased today (`Antidote.LeafCoats`, `Log.Press.LeafCoats`, then `Need "Garden" not
found`) traced back to one mistake: `WardensAssetDump.DumpRoot` wrote to
`Documents/Timberborn/Mods/Wardens/dump/`, a folder *inside* the live mod directory.
`ModSystemFileProvider.CacheFilesFromMod` (decompiled) scans every enabled mod with
`modDirectory.GetFiles("*", SearchOption.AllDirectories)` — no folder exclusion, extension only —
so the 287-file blueprint dump taken mid-session for the port work was being re-indexed as real,
current mod content on every subsequent load, frozen at whatever it looked like the moment it was
dumped. That explains all three symptoms: a loose file we still shipped collided with its own
frozen dump copy (duplicate-key crash); once we deleted the loose original in response, the *dump
copy* became the sole survivor, so deleting further things from `src` didn't remove them from the
game's eyes at all; and the "Garden" need vanished from `src` while the dump's frozen
`needcollection.leafcoats.blueprint.json` still listed it as a member, an orphaned reference the
verifier caught.

The "loose file duplicates the bundle" theory from earlier today was likely wrong, or at best only
part of the picture — the dump folder alone is sufficient to explain everything, and no offline
tool can actually confirm bundle-internal duplication either way. The Recipes/Goods/Needs/etc.
cleanup done under that theory is not undone (Leaf Coats' own faction, food chain, and tribute
content genuinely don't belong in this mod regardless), but it likely fixed nothing by itself; the
one file that mattered was deleting `Mods/Wardens/dump/` itself.

Fixed: deleted the deployed `dump/` folder, moved `WardensAssetDump.DumpRoot` to
`Documents/Timberborn/WardensDump` (a sibling of `Mods/`, never inside a mod's own scanned tree),
updated every doc reference to the old path. Deployed as v0.2.12.

Consequence for future port sessions: `dump_assets` output now lives outside the mod folder by
construction, so re-running the port's step 1 dump can no longer resurrect this bug — but the
Recipes/Goods/Needs cleanup should be treated as *unverified* against the real "does the bundle
also define this Id" question. If a genuinely new duplicate-key crash shows up now that the dump
folder is out of the loading path, that would be the first real evidence either way.
