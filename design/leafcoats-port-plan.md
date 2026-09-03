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
