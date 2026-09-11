# mapsmith spec reference

Every key a `*.map.toml` can carry. `python3 wardens/tools/mapsmith ops` prints the same op list
from the source docstrings, so it is never out of date — read this for the parts the CLI does not
print (placement keys, `[soil]`, `[checks]`, variants).

- [Command line](#command-line)
- [Top level](#top-level)
- [Terrain ops](#terrain-ops)
- [`[[place]]`](#place)
- [`[[scatter]]`](#scatter)
- [`[soil]`](#soil)
- [`[checks]`](#checks)
- [`[contract]`](#contract)
- [The level registry](#the-level-registry)
- [Variants](#variants)
- [Python API](#python-api)

## Command line

```
mapsmith build <spec>     write the .timber, then check it
        --out PATH          explicit output file
        --out-dir DIR       directory to write into
        --preview PATH      also write a PNG
        --ascii [--step N]  also print the ASCII map
        --variant NAME
mapsmith preview <spec>   build in memory, print the ASCII map, write nothing
mapsmith check <target>   validate a .timber or a spec  [--spec SPEC to apply a spec's [checks]]
mapsmith describe <target> [--build]
mapsmith new <path> [--size N] [--seed N] [--name NAME] [--force]
mapsmith ops              the terrain vocabulary, printed from the source
```

**`build` writes to `wardens/src/Maps/<name>.timber` by default** — the directory the Wardens build
deploys from. Pass `--out` whenever you are trying seeds, working on a copy, or experimenting;
otherwise a casual run edits shipped content.

`--strict` (treat warnings as failures) is accepted on either side of the subcommand:
`mapsmith --strict build spec.toml` and `mapsmith build spec.toml --strict` both work.

Exit code 0 when there is nothing to fix, 1 when there is.

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
| `hill` | `at`, `radius`, `height`, `exponent`, `floor` (build up from a fixed level rather than the ground), `avoid` (mask or list of masks to leave untouched — how you raise a bank *after* the river carves without filling the channel). |
| `river` | `points` (control points for a smooth curve), `bed`, `width`, `valley` (`[[dist, max_height], …]`), `tag`, `name`, `per_segment`. |
| `basin` | `at`, `radii` (scalar or `[rx, ry]`), `bed`, `shore`, `shore_width`, `tag`, `name`. |
| `crater` | `at`, `radius`, `depth` — sinks relative to the ground already there. |
| `channel` | `start`, `direction` (`north`/`south`/`east`/`west`), `length`, `drop` (tiles per level climbed), `offset`. |
| `pad` | `at` (lower-left corner), `size`, `height`, `name`, `reserve` (keep-out ring for scatter). |
| `terrace` | `box` (`[x1, y1, x2, y2]`, inclusive), `steps` (`[[width, height], …]` bands laid from `side`), `side` (`west`/`east`/`north`/`south`), `avoid`, `name`, `tag` — a shore reached with one Stairs per band instead of a cliff (level 01's Sump: `[[2, 7], [3, 6]]` down from a pad at 8, two Stairs). |
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
| `segment` | `[t0, t1]` fraction of that path. **`0` is the first control point, which for a river entering from a map edge is off the map** — a segment starting at 0 can select nothing but border tiles and fail. Start around `0.05` unless the river begins inland. Default `[0.0, 0.05]`. |
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
| `radius` | Scalar for a disc (`radius = 5.0`, anywhere within 5 tiles) or `[inner, outer]` for an annulus (`radius = [6, 15]`, never closer than 6). Both forms are common; the shipped specs use each. |
| `count` | How many to place. Sampling stops here. |
| `min_count` | The floor below which falling short is an error. Defaults to `count`, so by default a rule that cannot place everything raises. Lower it when "up to 20, at least 12" is genuinely what you mean. |
| `spacing` | Minimum Chebyshev gap between entities from this rule. |
| `margin` | Keep this far from the map border. |
| `height_range` | `[min, max]` ground level the entity may stand on. |
| `away_from` | `{ name = distance }`. Names may be masks (`badwater`) or anchors (`start`). |
| `reachable_from` | An anchor or `[x, y]`. Only place where a beaver coming from there could stand beside the entity, Stairs allowed (one level per Stairs, water blocks). This is how you keep a cluster on the colony's own bank: a river splits the map in two and `around` + `radius` is a disc that knows nothing about it, so without this you hand-pick centres and radii until the annulus geometrically cannot cross the water. Pairs with `[checks] reachable_scatter`. |
| `on_foot_from` | An anchor or `[x, y]`. Stricter: only tiles on that point's level and joined to it by flat ground, the entity's own tile included. Wardens walk on one level, and a Scavenger Flag counts a ruin only when the ruin's own tile is in its walking range, so this is "no Stairs needed". Use it for the scrap the colony opens with. Pairs with `[checks] on_foot_scatter`. |
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

## `[water]`

Water the map holds at tick 0. Without this section every column is dry and the sources fill their
beds during the first day.

```toml
[water]
fill = [
  { tag = "badwater", level = 4.2, contamination = 1.0 },   # an absolute surface height
  { tag = "clean",    depth = 0.3 },                         # or a depth above each cell's floor
]
```

Each entry fills the cells of a water mask (`tag`, from a `river`, `basin`, `channel` or `crater`
op) with exactly one of `level` or `depth`; a cell whose floor is at or above `level` stays dry.
`contamination` is 0 (clean) to 1 (badwater), default 0. Later entries win on shared cells.

Take the levels from the game, not from a guess: build without `[water]`, play a day or three,
and read the surface (`floor + depth`) off the autosave's `WaterMapNew.WaterColumns`. Water
filled to the level the simulation settles at loads without a flood wave. The encoding is the
game's own serializer: `timber-format.md`, "Water".

## `[checks]`

What `mapsmith check` must be able to prove. Errors fail the build's exit code; warnings do not
unless `--strict`.

| Key | Meaning |
|---|---|
| `require` | Templates that must appear at least once. |
| `require_at_least` | `{ Template = n }`. |
| `min_reachable` | Minimum tiles reachable from the starting location with Stairs (one level per Stairs, water blocks). |
| `reachable_scatter` | **Usually the one you want.** Names of `[[scatter]]`/`[[place]]` rules whose entities must all be reachable from the start, Stairs allowed. Clusters left off the list may be cut off on purpose. Needs the spec — a `.timber` does not carry rule names. |
| `on_foot_scatter` | Names of rules whose entities must stand on the start's level, joined to it by flat ground: reachable before the colony builds a single Stairs. The game joins a tile only to neighbours of the same height (`TerrainNavMeshUpdater`); level 01 softlocked when its opening scrap sat one level below the pad. |
| `reachable` | Template prefixes that must have at least one instance reachable (Stairs allowed). Blunt when several rules share templates, which is the normal case for ruins. |
| `reachable_fraction` | Warn below this fraction of a prefix's instances being reachable. Default 0.2. A proxy you have to hand-compute against how many clusters are cut off by design; prefer `reachable_scatter`. |
| `buried_ok` | Templates allowed to sit inside terrain. Default none: `UndergroundRuins` is a 5x5 surface object and the game deletes it when buried. |
| `start_pad` / `start_anchor` / `headroom` | Pad geometry the checker assumes. Defaults 8 / 2 / 3. |
| `max_step` | Levels crossed per step by the "with Stairs" flood fill (`min_reachable`, `reachable_scatter`, `reachable`). Default 1: one Stairs spans one level. On foot is always 0. |

### Checking a spec vs. checking a `.timber`

They are not the same check, and the difference is not cosmetic:

| | `check <spec>` | `check <file.timber>` |
|---|---|---|
| where the water is | from the build's masks | unknown — every bed is written dry, so only tiles holding a `*Source` entity are treated as water |
| walkability | strict: banks are walls | lenient: beavers stop only where the height step stops them |
| `reachable_scatter` | evaluated | warns that it cannot be evaluated |
| walk report | printed | not available |

So a map can pass the file check and fail the spec check. Check the spec whenever you have it; the
file check is for maps someone else wrote. Pass `--spec <spec>` alongside a `.timber` to at least
apply the spec's `[checks]` table to it.

## `[contract]`

What must stay true of the ground for the level to still be the level. Each key is a contract name;
the value is its parameters. `mapsmith contracts` prints them from the source, with the reasoning.

| Contract | Asserts |
|---|---|
| `single_gorge` | Exactly `count` places along a watercourse are narrow enough (`max_width`) and banked enough (`min_bank`) to dam. `max_length` caps how long one may run — a 25-tile "gorge" means a dam anywhere is as good as anywhere else, which is the same as no decision. `min_length`, `ignore_ends` tune what counts. |
| `confluence_upstream` | A `tributary` joins a `trunk` above the trunk's gorge, measured along the trunk's own path, so one dam impounds both. |
| `never_touch` | Two masks (`a`, `b`) stay `gap` tiles apart, except within `merge` tiles of the `confluence` of two named watercourses. This is how "the spring's clean cells never touch badwater *before* the confluence" is stated. |
| `unreachable` | A `mask` cannot be walked to `from` an anchor, with optional `min_area`/`max_area`. Written for level 03's island, which earns the Vertical tutorial only if the beavers genuinely cannot walk there. |

Contracts add `note` findings when they pass — the evidence that the level is intact. Notes never
fail a run, not even under `--strict`.

## The level registry

`wardens/maps/levels.toml` is the index: level id → map name → the spec that builds it.

```bash
mapsmith levels            # the table, plus any disagreement with WardensCampaign.cs
mapsmith levels --verify   # exit 1 on disagreement
mapsmith build --level 02
mapsmith check  --level 02
```

`wardens/src/WardensCampaign.cs` stays the campaign (which levels exist, what ends them). The index
says only where each level's land comes from, and `--verify` cross-checks the two — a spec whose
`name` is not exactly the level's map name would make the running game treat it as a non-campaign
map, silently.

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

spec = load(Path("wardens/maps/wardens-01-first-light.map.toml"))
for seed in (1, 2, 3):
    m = build({**spec, "seed": seed})
    print(f"--- seed {seed} ---")
    print(ascii_map(m, step=3))
```

`build()` returns a `MapBuild`. The parts worth knowing, because reading the source to find them is
a waste of your time:

| | |
|---|---|
| `b.h(x, y)` | ground level at a tile (int) |
| `b.height` | the `Grid`: `.at(x, y)`, `.set(x, y, v)`, `.min()`, `.max()`, `.coords()` |
| `b.masks["badwater"]` | a `Mask`: `.at(x, y)`, `.count()`, `.points()`, `.grow(r)`, `.union(m)` |
| `b.anchors`, `b.paths` | named points, and river centrelines by name |
| `b.entities` | `Entity(template, x, y, z, components, orientation, rule)` — `rule` is the `name` of the block that placed it |
| `b.walkable_from((x, y), max_step)` | `Mask` of ground reachable from a point: `WITH_STAIRS` (default, one level per step) or `ON_FOOT` (one level only); water blocks |
| `b.stairs_from((x, y))` | `{(x, y): (stairs, steps)}` — the fewest Stairs to each tile, then the shortest walk |
| `b.walk_distances((x, y))` | `{(x, y): steps}` — 4-neighbour, so a lower bound on the real path |
| `b.water_cells()` | every tile any water mask covers |
| `b.distance_to("badwater")` | a `Grid` of distance to the nearest cell of a mask |

And in `mapsmith.spec`: `check(spec, b)` (the strict check, with water and rule names),
`walk_report(b)` (the per-cluster distance table), `groups_of(b)` (entities by rule name).
