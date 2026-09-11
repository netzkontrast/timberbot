# The Wardens' wasteland: the shipped map

> **Status:** generated (2026-09-03) by [`wardens/tools/gen_map.py`](../wardens/tools/gen_map.py); **renamed
> 2026-09-10** to `wardens/src/Maps/Wardens 01 First Light.timber` — it *is* level 01 of the campaign, and the
> level table keys on the map name. Terrain, entities and thumbnail are byte-identical to the file that shipped
> as *Wardens Wasteland*; only the description in `map_metadata.json` changed. Deploy copies it to
> `Documents/Timberborn/Maps`, and `WardensMapInstaller` does the same at the main menu for players who never
> run a build. **Still not loaded in-game.** Fills item 5 of the v0.1 scope in
> [`faction-wardens.md`](faction-wardens.md). The other nine maps are
> [`wardens-campaign-map-set.md`](wardens-campaign-map-set.md), which also inherits this file's open questions
> about the water map and the ruin templates.

![top-down preview: ash plateau, the badwater river in purple, the Sump beside the cyan starting pad, grey ruin
columns, the green spring in the north-east](wardens-wasteland.png)

## The remake (2026-09-11)

Level 01 was played through the MCP server on the game machine (`wardens/playtest/PLAYTEST.md`, "Level 01
played through the MCP server") and the land failed the play in three places. The level is now built by
mapsmith from [`wardens/maps/wardens-01-first-light.map.toml`](../wardens/maps/wardens-01-first-light.map.toml)
(same name, seed and size; saves made on the first land keep it, since a save carries its world):

| Seen in play | The first land | Now | Held by |
|---|---|---|---|
| One pump site, on the Core's pad | the Sump against the pad, four levels down; its far rim below the cliff | the river moved east; a terrace 8 → 7 → a shelf at 6 along the Sump | contract `shore` (32 walkable shore tiles off the pad, run of 5; the first land: 11 and 3) |
| The Sump empty until day 5 | filled only from the river | a `BadwaterSource` seep in the Sump, bed 3 (deeper, a drought reserve), and a channel to the river | `require_at_least BadwaterSource = 4` |
| Idle Wardens at 0% on day 2 | first scrap 10+ tiles out | three small ruins 7–14 steps from the start (plus: Wardens boot fully charged and the Core holds 10 scrap, `WardensStartingPopulation.cs`) | `reachable_scatter "first light"` |
| The spring grove unreachable | across the river, with Platforms science-locked | still across, one bridge of at most 5 tiles (Platforms free since the open bar) | contract `crossing` |

The crossing contract passes on the first land too: its spring was always one short bridge away, and what
stood in the way was the 100-Science Platform. The Badwater Cell now burns 0.18/h (4.3 a day), what one pump
makes, instead of 0.4/h.

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
2. **Water starts dry** (until 2026-09-11: level 01 now ships pre-filled, `[water]` in its spec, with
   the encoding decoded in the mapsmith skill's `timber-format.md`). The new water map's column encoding for pre-filled water was undocumented, so the
   sources fill the river and the Sump during the first day. Soil contamination in the file is a cosmetic
   estimate; the game recomputes it from the badwater.

`gen_map.py --check <file>` runs two tiers. **The format**, which the game would fail on: array sizes, top layer
air, entities on the surface and inside bounds, one StartingLocation on a flat pad with air above, a
BadwaterSource. Then **the level's contract** (`FirstLight.contract`), which the *level* would fail on — the
properties the play above depends on, restated as assertions:

| Contract | Why |
|---|---|
| >= 5 ruin columns within 16 tiles of the start; >= 600 total scrap | chapter 1 has no other building material |
| >= 3 BadwaterSources, and bed cells touching both the north and the south edge | the river has to cross the map, not puddle at the rim |
| >= 40 bed-height cells within 12 tiles of the start | the Sump, and the Sludge Pump that goes in it |
| exactly 1 clean WaterSource, >= 20 tiles from any badwater source | "there is not much", and it has to stay clean |
| >= 80 plants | the spring's grove, the only green |
| 3-40% of tiles contaminated above 0.5 | poison is a band; a flood and a rumour are both wrong |
| >= 4 UndergroundRuins | the mines of a later act |

The seed (3000) and the size (96) stay. The heightfield itself changed once, on 2026-09-11 (the remake above,
decided by the author: same name, new land); level 10 (*Home*) regenerates from the same spec, so the two stay
one place.

## Open questions (answered by the first in-game load)

- Does 1.1 accept the 0.7.10 layout through migration, or does it want a 1.1-native water map?
- Do `RuinColumnH*` and `UndergroundRuins` still exist under those names in 1.1?
- Is the Sump deep enough for the Sludge Pump (deep badwater pump) once filled? If not, lower its bed to 3.
- ~~Does the game list a `.timber` shipped inside a mod's `Maps/` folder, or only the copy in
  `Documents/Timberborn/Maps`?~~ **Answered 2026-09-10** by the 1.1.2.4 decompile: only the copy.
  `MapRepository.GetUserMapNames()` enumerates `UserDataFolder.Folder/Maps` and nothing else, and
  `WardensMapInstaller` (MainMenu context) does that copy at runtime. A mod *can* list maps from its own folder,
  but only by implementing `Timberborn.MapItemsUI.ICustomMapItemFactory` — recorded in
  [`wardens-campaign-maps.md`](wardens-campaign-maps.md) §1 as the better mechanism once it can be tested.
