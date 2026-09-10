# mapsmith spec reference

Every key a `*.map.toml` can carry. `python3 wardens/tools/mapsmith ops` prints the same op list
from the source docstrings, so it is never out of date — read this for the parts the CLI does not
print (placement keys, `[soil]`, `[checks]`, variants).

- [Top level](#top-level)
- [Terrain ops](#terrain-ops)
- [`[[place]]`](#place)
- [`[[scatter]]`](#scatter)
- [`[soil]`](#soil)
- [`[checks]`](#checks)
- [Variants](#variants)
- [Python API](#python-api)

## Top level

| Key | Default | Meaning |
|---|---|---|
| `name` | required | Map name; also the built file's stem (`<name>.timber`). |
| `size` | required | Edge length in tiles. Square. 96 is the shipped size; 64 is a fast one to iterate on. |
| `seed` | `0` | Seeds noise and scatter. Same spec + same seed = byte-identical file. |
| `description` | `""` | Shown in the map browser. |
| `game_version` | `"0.7.10.0"` | Written to `version.txt` and `world.json`. See the format reference before changing. |
| `timestamp` | fixed | Kept constant so rebuilds are reproducible. |
| `recommended` / `dev` | `false` | `IsRecommended` / `IsDev` in the metadata. |
| `name_loc_key` / `description_loc_key` | `""` | Localisation keys, if the mod ships loc rows for the map. |
| `thumbnail_camera` | derived | `{x, y, z, shadow_distance}` for the map-browser camera. |
| `palette` | built-in | PNG preview colours: `ground_low`, `ground_high`, `water`, `badwater`, `clean`. |

## Terrain ops

`[[terrain]]` blocks, applied in order. Every op takes an optional `tag` (add the cells it touched
to a named mask) and most take `name` (register an anchor point).

| `op` | Key parameters |
|---|---|
| `base` | `height` — flatten everything to one level. Start here. |
| `noise` | `amplitude`, `contrast` (stretch around the midpoint), `octaves`, `weights`, `mode` (`add`/`set`/`max`). |
| `rim` | `width`, `height`, `exponent` — raise the border into hills. |
| `hill` | `at`, `radius`, `height`, `exponent`, `floor` (build up from a fixed level rather than the ground). |
| `river` | `points` (control points for a smooth curve), `bed`, `width`, `valley` (`[[dist, max_height], …]`), `tag`, `name`, `per_segment`. |
| `basin` | `at`, `radii` (scalar or `[rx, ry]`), `bed`, `shore`, `shore_width`, `tag`, `name`. |
| `crater` | `at`, `radius`, `depth` — sinks relative to the ground already there. |
| `channel` | `start`, `direction` (`north`/`south`/`east`/`west`), `length`, `drop` (tiles per level climbed), `offset`. |
| `pad` | `at` (lower-left corner), `size`, `height`, `name`, `reserve` (keep-out ring for scatter). |
| `smooth` | `passes`, `strength` — box blur. |
| `clamp` | `min`, `max` — round to integer levels. Run last; the voxel writer needs integers. |

`at` / `start` accept `[x, y]` or the name of an anchor registered earlier.

## `[[place]]`

One entity at a known spot, or `count` of them along a watercourse.

| Key | Meaning |
|---|---|
| `template` | Game template name, e.g. `StartingLocation`, `BadwaterSource`, `RuinColumnH3`. |
| `at` | `[x, y]` or an anchor name. |
| `dx`, `dy` | Offset applied after `at` — `StartingLocation` anchors 2 tiles inside its pad, hence `dx = -2`. |
| `along` | A river's `name`, or a `tag` carried by exactly one river. Two rivers sharing a tag is an error: name the one you mean. |
| `segment` | `[t0, t1]` fraction of that path, `0` at its first control point. Default `[0.0, 0.05]`. |
| `count` | How many to place along the segment. Fewer than asked for is an error. |
| `spread` | How far either side of the centreline to try. Default 1. |
| `margin` | Keep this many tiles off the map border. Default 1. |
| `orientation` | `Cw0`/`Cw90`/`Cw180`/`Cw270`. Set it on everything except plants. |
| `strength` | Writes the `WaterSource` component (`SpecifiedStrength` = `CurrentStrength`). |
| `buried` | Levels below the surface. Only for templates the checker's `buried_ok` allows. |
| `footprint` | Tiles the entity claims, so later placement keeps clear. Default 1. |
| `components` | Raw component table, merged in verbatim, for anything the convenience keys do not cover. |

## `[[scatter]]`

Many entities by rule. Rejection-sampled and bounded — a rule that cannot be satisfied raises
rather than under-filling, because a map that quietly lost half its scrap wastes a playtest.

| Key | Meaning |
|---|---|
| `name` | Label for errors and `describe`. Worth setting. |
| `templates` + `weights` | What to sow and in what proportion. `template` alone works for one. |
| `levels` | Per-template level, feeding `yield.amount_per_level` (`RuinColumnH3` → 3). |
| `around` | Centre: `[x, y]` or an anchor. |
| `radius` | Scalar (disc) or `[inner, outer]` (annulus). |
| `count` / `min_count` | Target, and the floor below which it is an error. |
| `spacing` | Minimum Chebyshev gap between entities from this rule. |
| `margin` | Keep this far from the map border. |
| `height_range` | `[min, max]` ground level the entity may stand on. |
| `away_from` | `{ name = distance }`. Names may be masks (`badwater`) or anchors (`start`). |
| `attempts` | Sampling budget. Default `max(400, count * 60)`. |
| `growth` | `[min, max]` `Growable.GrowthProgress` — plants. |
| `variants` | `RuinModels.VariantId` pool — ruins. |
| `yield` | `{ good = "ScrapMetal", amount_per_level = 15 }` or `{ good = …, amount = … }`. |
| `buried`, `orientation`, `footprint`, `components` | As for `[[place]]`. |

Scatter never lands on water masks, reserved pads, or a tile another entity already claims.

## `[soil]`

Per-cell fields derived from distance to a mask:

```toml
[soil]
contamination = { from = "badwater", offset = 1.5, reach = 9.0 }
moisture      = { from = "clean",    offset = 0.0, reach = 7.0, peak = 0.8 }
```

`offset` is how far the field stays at full strength; `reach` is how far it fades over; `peak`
caps it. `{ value = 0.3 }` sets a flat field instead. The game recomputes contamination from the
actual badwater, so this is a starting estimate, not a simulation.

## `[checks]`

What `mapsmith check` must be able to prove. Errors fail the build's exit code; warnings do not
unless `--strict`.

| Key | Meaning |
|---|---|
| `require` | Templates that must appear at least once. |
| `require_at_least` | `{ Template = n }`. |
| `min_reachable` | Minimum tiles walkable on foot from the starting location (one level of climb, water blocks). |
| `reachable` | Template prefixes that must have at least one instance reachable on foot. |
| `reachable_fraction` | Warn below this fraction of them being reachable. Default 0.2. |
| `buried_ok` | Templates allowed to sit inside terrain. Default `["UndergroundRuins"]`. |
| `start_pad` / `start_anchor` / `headroom` | Pad geometry the checker assumes. Defaults 8 / 2 / 3. |
| `max_step` | Levels a beaver climbs unaided, for the walkability flood fill. Default 1. |

## Variants

One file, a family of maps. `[variants.<name>]` is deep-merged over the base
(tables merge key by key; arrays and scalars are replaced whole):

```toml
[variants.small]
size = 64
seed = 11
```

```bash
python3 wardens/tools/mapsmith build spec.toml --variant small
```

## Python API

For anything the spec cannot express — a generated campaign, a batch sweep over seeds:

```python
import sys; sys.path.insert(0, "wardens/tools")
from pathlib import Path
from mapsmith import load, build, ascii_map, write_timber, check_file

spec = load(Path("wardens/maps/wardens-wasteland.map.toml"))
for seed in (1, 2, 3):
    m = build({**spec, "seed": seed})
    print(f"--- seed {seed} ---")
    print(ascii_map(m, step=3))
```

`build()` returns a `MapBuild` with `height`, `masks`, `anchors`, `paths`, `entities`, and
`walkable_from((x, y))` if you want to measure something the checker does not.
