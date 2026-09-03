# The Wardens' wasteland: the shipped map

> **Status:** generated (2026-09-03) by [`wardens/tools/gen_map.py`](../wardens/tools/gen_map.py) into
> `wardens/src/Maps/Wardens Wasteland.timber`; deploy copies it to `Documents/Timberborn/Maps`. Not yet
> loaded in-game. Fills item 5 of the v0.1 scope in [`faction-wardens.md`](faction-wardens.md).

![top-down preview: ash plateau, the badwater river in purple, the Sump beside the cyan starting pad, grey ruin
columns, the green spring in the north-east](wardens-wasteland.png)

## What the map has to do

Every pillar of the Chapter 1 plan needs something from the ground:

| Need | On the map |
|---|---|
| Scrap is the only building material | ruin clusters 8 to 13 tiles from the Core (small columns), two rich fields further out, six underground ruins for later mines |
| Badwater is fuel and Data feedstock | three `BadwaterSource`s at the north edge feed a river that meanders across the map and leaves at the south edge; the Sump, a basin beside the start, fills from it (the Sludge Pump goes there) |
| Poison is the plot | the river's contamination band covers the middle of the map; the plateau is ash |
| "Trees only grow on irrigated, green ground. Find some. There is not much." | one clean `WaterSource` in a crater on a hill in the north-east, its overflow running north off the map; pines, birches and blueberries around it, lone birches in the far corners |
| Dams tutorial ("the nearest river") | the badwater river |
| Cold Boot orbit | the Core stands on a flat 8x8 pad at Z 8 with the Sump below it |

96 x 96, heights 4 (river bed) to 16 (rim), no overhangs. Seeded, so `gen_map.py` reproduces the file byte
for byte; `--seed` and `--size` give variants.

## File format

A `.timber` is a zip of `world.json`, `map_metadata.json`, `version.txt` and `map_thumbnail.jpg`. The layout
was verified against a map that loads in 0.7.10 and the exporter that wrote it
([lawless-m/Toberboon](https://github.com/lawless-m/Toberboon), `TIMBERBORN_FORMAT.md`), and the entity shapes
against a map written by the game's own editor (Thunderstore, *WaterfallAndRuinsOfTheRuins*, 0.5.6):

- `TerrainMap.Voxels.Array`: `width x depth x 23` values, `1` solid `0` air, Z-major then Y then X; layer 22 air.
- `WaterMapNew` (`Levels`, `WaterColumns`, `ColumnOutflows` as `down|north:east|south:west`),
  `WaterEvaporationMap`, `SoilMoistureSimulator` and `SoilContaminationSimulator` (both with `Size`),
  `HazardousWeatherHistory`, `MapThumbnailCameraMover.CurrentConfiguration`.
- Entities: `StartingLocation` (needs `Orientation`, a flat pad, 3 blocks of air), `BadwaterSource` and
  `WaterSource` (both carry a `WaterSource` component with `SpecifiedStrength`), `RuinColumnH1..H5`
  (`RuinModels.VariantId`, `Yielder:Ruin` = ScrapMetal 15 per level), `UndergroundRuins`, trees and bushes
  with `Growable.GrowthProgress`. Entity Z is the first air layer above the column.

Two deliberate choices:

1. **The file says `GameVersion 0.7.10.0`.** That is the format we verified; the game migrates older maps on
   load (expect an "older version" notice at worst). Claiming 1.1 for a layout nobody has seen from a 1.1
   editor would skip that migration.
2. **Water starts dry.** The new water map's column encoding for pre-filled water is undocumented, so the
   sources fill the river and the Sump during the first day. Soil contamination in the file is a cosmetic
   estimate; the game recomputes it from the badwater.

`gen_map.py --check <file>` runs the static checks the game would fail on (array sizes, top layer air, entities
on the surface and inside bounds, one StartingLocation on a flat pad with air above, a BadwaterSource).

## Open questions (answered by the first in-game load)

- Does 1.1 accept the 0.7.10 layout through migration, or does it want a 1.1-native water map?
- Do `RuinColumnH*` and `UndergroundRuins` still exist under those names in 1.1?
- Is the Sump deep enough for the Sludge Pump (deep badwater pump) once filled? If not, lower its bed to 3.
- Does the game list a `.timber` shipped inside a mod's `Maps/` folder, or only the copy in `Documents/Timberborn/Maps`?
