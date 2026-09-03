# Model catalog — what 3D art exists on this machine, and what we can use

> Surveyed 2026-09-03: `F:\Steam\steamapps\workshop\content\1062090` (96 mods), `Documents\Timberborn\Mods`, and the game's `StreamingAssets\Modding`. "Usable" means we can reference or ship it in the Wardens mod without making a mesh.

## 1. Vanilla models — usable by reference, the real toolbox

Every vanilla building, plant and character model is addressable by path from a blueprint, e.g.

```json
"Children": { "#Finished": { "TimbermeshSpec": { "Model": "Buildings/Science/Numbercruncher/Numbercruncher.IronTeeth.Model" } } }
```

The dump at `StreamingAssets/Modding/Blueprints.zip` (1067 blueprints) is the index. Counts that matter for us:

| Set | Models | Notes |
|---|---|---|
| Iron Teeth buildings | 244 distinct | metal, pipes, rigs, pods — the Wardens' native look |
| Folktails buildings | ~230 | wood; useful for Act III only |
| Natural resources | trees × {Seedling, Mature, Dead, Dry, Stump}, crops, bushes | `Cattail`, `Mangrove`, `Mushroom`-style meshes are the candidates for a contamination plant |
| Characters | `Bot.IronTeeth`, `Bot.Folktails`, `BeaverAdult`, `BeaverChild` | bot textures are swappable per faction via `BotTexturesSpec` |
| Construction bases | `ConstructionBases/ConstructionBase{1x1…5x5}` | nested into any unfinished model |

Three techniques turn these into "new" buildings with zero mesh work:

1. **Recolor** — `MaterialPatcher` blueprints swap `_BaseMap` textures / colors per faction (Emberpelts recolors *every* IT building this way).
2. **Kitbash** — `Children.X#nested: { "BlueprintPath": … }` and `TemplateAttachmentsSpec` compose existing models into one entity (vanilla nests construction bases and attaches backpacks the same way).
3. **Re-spec** — same model, different components: a Numbercruncher mesh with our recipe list *is* the Cruncher.

## 2. Loose `.timbermesh` files in installed mods — readable, but other people's work

| Mod | Meshes | What |
|---|---|---|
| Knatte Materials / DamDecoration (`3277416566`, `3296543474`) | 173 | levees in every slope/corner variant, metal + log staircases, pillars, ramps |
| Half Roofs (`3345158183`) | 17 | half roofs 1x1…2x2, both factions |
| Ladder (`3286476486`) | 4 | ladder, both factions |
| Shanty Speaker (`3275060459`) | 1 | decoration |
| `storage` (local) + BerryJam template | 1 | `JamStove.Folktails` — **with its `.blend` source** |

These load straight from disk, so they prove the file format, but they are licensed to their authors. Reference for how a mod ships a mesh (`*.Model.timbermesh` + `*.ConstructionStage0.Model.timbermesh` next to the blueprint); not for shipping in Wardens without asking.

## 3. Faction asset bundles — reference only

| Bundle | Model subjects | Highlights relevant to us |
|---|---|---|
| Emberpelts 1.1 (110 MB) | 379 | full IT building set retextured, `DecontaminationPod`, `ContaminationBarrier` variants, `BadwaterPressurizer`, `PhoenixDistillery`, `Wonder` in 4 stages |
| Leaf Coats 1.1 | 575 | `ChargingStation.Tree`, `GeothermalNumbercruncher`, `BadwaterSeepRig`, `Observatory`, `PowerTreadmill`, a 60-piece staged Wonder |

Unity bundles; contents are visible in the `.manifest` but not extractable into our mod. Their value is as a **design reference for scope**: a "full" faction is ~400–600 model subjects, of which maybe 30 are genuinely new shapes and the rest are recolors and variants.

## 4. Official examples with source

- `StreamingAssets/Modding/TimberbornExampleModels.blend` — Mechanistry's reference scene (naming, pivots, construction stages).
- `ModTemplates/BerryJam/Buildings/Food/JamStove/JamStove.Folktails.blend` + exported `.timbermesh` — the canonical "one building, one blend, one export" example.
- `EditorDll.zip` ships `Timberborn.TimbermeshTools.Editor.dll` and `TimbermeshAnimations.Editor.dll`: the Unity-side tooling, for when we go beyond Blender exports.

## 5. Making new meshes — what's missing on this machine

- **Blender is not installed.** The only exporter for `.timbermesh` is Mechanistry's Blender add-on (github.com/mechanistry/timbermesh). Once Blender 4.x + the add-on are in, meshes can be built **headless and procedurally** (`blender -b -P build_core.py`): low-poly box/cylinder kitbashes with vertex colors are well within what a script can produce, and that is how I'd make the first Wardens-specific shapes.
- Until then the Wardens ship **zero new meshes**: recolor + kitbash + re-spec covers Chapter 1 entirely (see `wardens-chapter-1-plan.md`).

## 6. 2D assets — formats measured from Emberpelts / vanilla

| Asset | Size | Format |
|---|---|---|
| Faction avatars (`Sprites/Avatars/XAdult`, `XChild`, `XBot`, contaminated variants, `XLogo`) | 512×512 | PNG + `.meta.json` `{"isSprite": true}` |
| New-game full avatar (`XFullNewGame`) | 420×560 | PNG |
| Goods / building / tool-group icons | ~98×98 | PNG + meta, referenced as `Sprites/Goods/XIcon` or `Buildings/…/XIcon` |
| Beaver / bot skin textures | 2048×2048 | PNG, `Materials/Beavers/<Faction>/Adult/BeaverAdultN.<Faction>` |
| Wonder completion image | full-screen | `UI/Images/WonderCompletion/…` |

All of these are producible with Pillow/SVG from a script (flat, iconic, vertex-color style matches the game better than painted art anyway), so 2D is not blocked on any install.
