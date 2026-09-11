# The `.timber` format, and how much of it is actually known

Read this before changing `world.py`, before claiming a newer `GameVersion`, or before introducing
a template name. The distinction that matters is **verified** (seen in a file the game loaded or
wrote) versus **assumed** (reasoned about, never observed) — the repo's map work has been done
without a game install, and pretending otherwise is how a load-time crash gets shipped.

## The container

A `.timber` is a plain zip with exactly four members. It is both the map format and the save
format; saves simply carry more singletons.

| Member | Contents |
|---|---|
| `world.json` | `GameVersion`, `Timestamp`, `Singletons`, `Entities` |
| `map_metadata.json` | `Width`, `Height`, `MapDescription`, loc keys, `IsRecommended`, `IsDev` |
| `version.txt` | the game version string, again |
| `map_thumbnail.jpg` | must start with the JPEG magic `ff d8` |

## Terrain

`Singletons.TerrainMap.Voxels.Array` is a space-separated string of `width × depth × 23` values,
`1` solid and `0` air, ordered **Z-major, then Y, then X**. Layer 22 is always air.

mapsmith writes no overhangs — one contiguous column per tile — so "the ground top" is just the
count of solid blocks in the column. The checker relies on that; a future op that carves overhangs
has to revisit `_heights` in `checks.py`.

## Per-cell singletons

All of these carry one value per tile and are checked for length:

| Singleton | Arrays |
|---|---|
| `WaterMapNew` | `WaterColumns`, `ColumnOutflows` (`down\|north:east\|south:west`), plus `Levels` |
| `WaterEvaporationMap` | `EvaporationModifiers` |
| `SoilMoistureSimulator` | `MoistureLevels`, plus `Size` |
| `SoilContaminationSimulator` | `ContaminationCandidates`, `ContaminationLevels`, plus `Size` |

`HazardousWeatherHistory` and `MapThumbnailCameraMover` are singletons too, without per-cell data.

**Water.** Decoded on 2026-09-11 from the 1.1.2.4 decompile (`WaterColumnPackedListSerializer`,
`ColumnOutflowsPackedListSerializer`, `WaterSimulationMigrator`) and a day-15 autosave of a map this
tool wrote:

- A `WaterColumns` token is `0` for a dry column, else `depth:contamination:overflow:floor:oldDepth`.
  The reader also takes the first three fields alone (`floor` then defaults to 0, `oldDepth` to
  `depth`). `floor` is the column's ground top, the first air layer. Contamination runs 0..1.
- A `ColumnOutflows` token is `0` when empty, else four `index3D|flow` pairs (bottom, left, top,
  right; `0` for none) plus any extra outflows. The simulation recomputes them; mapsmith writes no
  flow.
- A file without a `WaterSimulationMigrator` singleton gets migrated on load, which **halves every
  water source's `SpecifiedStrength`**. Every mapsmith map is in that case, and its playtested
  strengths are already the halved ones. Writing the key would double every source against what
  has been played, so mapsmith does not write it.

Without a `[water]` section mapsmith writes every column dry and the sources fill their beds during
the first day. With one, the columns it names are written in the encoding above, and the checker
rejects a token the reader would misparse (field count, a non-number, depth below 0, contamination
outside 0..1, a floor that is not the ground top).

## Entities

```json
{"Id": "<uuid>", "Template": "RuinColumnH3",
 "Components": {"BlockObject": {"Coordinates": {"X": 12, "Y": 43, "Z": 7}, "Orientation": "Cw0"},
                "RuinModels": {"VariantId": "A"},
                "Yielder:Ruin": {"Yield": {"Good": {"Id": "ScrapMetal"}, "Amount": 45}}}}
```

- `Z` is the **first air layer above the column** — the tile the entity stands on top of, not the
  last solid block.
- Ids are deterministic (uuid5 over seed, template and position), so rebuilding a spec produces the
  same file.
- **Plants carry no `Orientation`. Everything else does.** This is not a guess: the map written by
  the game's own editor has `Orientation: Cw0` on ruins, sources and the starting location, and
  omits it on `Pine`, `Birch` and `BlueberryBush`.
- `StartingLocation` needs an orientation, a flat pad, and clear air above it.
- `UndergroundRuins` is the one template that legitimately sits *inside* terrain.

## Template names: verified vs. guessed

Verified (present in a map that loaded): `StartingLocation`, `BadwaterSource`, `WaterSource`,
`RuinColumnH1`–`RuinColumnH5`, `UndergroundRuins`, `Pine`, `Birch`, `BlueberryBush`.

Everything else — other trees, ore, decorative props — is a guess until someone confirms it in the
game's `Blueprints.zip` or a map the game wrote. The game resolves template names at load, so a
wrong one is a crash, not a missing tree. When a spec introduces an unverified name, say so in the
same breath as delivering the map.

## GameVersion

Files claim `0.7.10.0`: the layout that was verified. The game migrates older maps on load (at
worst an "older version" notice). Claiming a newer version for a layout nobody has seen from that
version's editor skips the migration and is strictly worse. Change this only after a map written
by the newer editor has been read and the layout compared.

## Still open

Answered only by loading a map in the game — carry them forward rather than quietly assuming:

1. Does the current game accept the `0.7.10.0` layout through migration?
2. Do `RuinColumnH*` and `UndergroundRuins` still exist under those names?
3. Is a basin dug to bed 4 deep enough for the deep badwater pump once filled?
4. Does the game list a `.timber` shipped inside a mod folder, or only the copy in
   `Documents/Timberborn/Maps`? (Research says only the copy — see `design/wardens-campaign-maps.md`.)

## Sources

`design/wardens-wasteland.md` (format verification and the shipped map),
`design/wardens-campaign-maps.md` (distribution, map repository APIs, campaign levels),
`design/faction-wardens.md` (what the land has to do for the story).
