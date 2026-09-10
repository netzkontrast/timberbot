---
name: timberborn-mapsmith
description: Author, generate, preview and validate Timberborn `.timber` maps for the Timberbot/Wardens mod using the mapsmith tool in `wardens/tools/mapsmith`. Use this whenever the work touches a Timberborn map or the land a colony starts on — making a new map or campaign level, changing terrain, rivers, badwater, ruins, scrap, trees, water sources or the starting location, tuning a seed, checking why a map will not load or plays badly, or reading an existing `.timber` file. Reach for it even when the request says "level", "world", "terrain", "wasteland", "map file", "map editor" or names a `.timber` rather than saying "mapsmith", and before hand-editing any world.json or writing a one-off generator script.
---

# Making Timberborn maps

Timberborn has no scriptable map editor, so a mod that needs land ships a `.timber` file it wrote
itself. `wardens/tools/mapsmith` is that writer: a spec file describes the map, the tool builds it,
previews it as text you can actually read, and checks it against everything the game would reject.

The thing to internalise before touching anything: **you almost certainly cannot run Timberborn.**
No game means no feedback loop, and a map that loads but plays badly costs the user a whole
playtest evening to discover. The preview and the checker exist to replace that missing loop. Use
them on every change, and treat "the checker passes" as the floor, not the finish line — read the
ASCII map and ask whether the land does what the design asked of it.

## Start here

```bash
python3 wardens/tools/mapsmith preview wardens/maps/wardens-wasteland.map.toml --step 2
python3 wardens/tools/mapsmith build   wardens/maps/wardens-wasteland.map.toml
python3 wardens/tools/mapsmith check   wardens/maps/wardens-wasteland.map.toml
python3 wardens/tools/mapsmith ops     # every terrain verb and what it takes
```

Two things about the command line worth knowing before your first run:

- **`build` writes into the repo by default** — `wardens/src/Maps/<name>.timber`, which is what the
  Wardens build deploys. Pass `--out <path>` (or `--out-dir`) when you are experimenting, trying
  seeds, or working on a copy; otherwise you are editing shipped content by accident.
- **`--strict` (fail on warnings) works on either side of the subcommand.** Both
  `mapsmith --strict build spec.toml` and `mapsmith build spec.toml --strict` are accepted.

**Check the spec, not the built file, whenever you have the choice.** `check <spec>` knows where the
water is and which rule placed what; `check <file.timber>` cannot know either — every bed is written
dry, so a `.timber` alone has no way to say "this tile is a river". The file check is therefore the
*lenient* one: it stops beavers at banks only where the height step happens to, and it cannot
evaluate `reachable_scatter` at all. Use it for files someone else wrote.

Standard library only — it runs in any container, with no install step. (Pillow, if it happens to
be there, is used for a nicer map-browser thumbnail; nothing else changes.)

Specs live in `wardens/maps/*.map.toml`, built maps in `wardens/src/Maps/*.timber`, from where the
Wardens build copies them to `Documents/Timberborn/Maps`.

## The loop

Work in this order. Each step is cheap and catches a different class of mistake.

1. **Say what the land has to provide.** Not "a river in the north" but "badwater the Sludge Pump
   can reach from the Core" and "scrap within a day's walk of the start". A map is a set of
   affordances for the colony; the geometry is downstream of them. If a design doc covers the map
   (`design/wardens-wasteland.md`, `design/wardens-campaign-maps.md`), that list is already
   written — read it first.
2. **Write or edit the spec.** `mapsmith new wardens/maps/level-03.map.toml` gives a working
   skeleton. Copy an existing spec when the new map is a cousin of an old one.
3. **`preview` it, and read the output.** This is the step people skip and regret. See below.
4. **`build` it.** The build re-runs the checker, prints what it made, and prints the walk report:
   how many steps from the starting location to each named group of entities. This is the answer to
   "is the scrap within a day's walk" — you cannot read walking distance off a heightmap, and a
   straight-line radius says nothing about a cliff in the way.

   ```
   walk from the start at 22,48 (4159 tiles reachable on foot):
     across the river         unreachable on foot (9 entities)
     near ruins                10 min   13 median   19 max
     the south field           24 min   33 median   36 max
   ```

   `unreachable on foot` is not automatically a bug — "across the river" is meant to need a bridge.
   It is a bug when the thing the colony opens with is in that list. Name your `[[scatter]]` rules;
   the report and `reachable_scatter` are both keyed on those names.
5. **Fix what the checker names, then look again.** Warnings are not noise: "only 240 of 9216 tiles
   are walkable from the start" means the colony is trapped on a shelf.
6. **Say plainly what is still unverified.** The checker proves the file is well-formed and the
   colony is not boxed in. It cannot prove the game accepts a template name, or that the map is
   fun. Hand those to the user as open questions rather than implying they are settled.

## Reading the ASCII preview

The preview is the only way to see the map without the game, so learn to read it:

```
 .:-=+*#%@   ground, low to high      ~  water (any tag)     O  water source
 A  starting location                 n  ruin / scrap        t  tree or bush
```

North is up, x runs east, y runs south. `--step 2` samples every other tile so a 96-map fits a
terminal; `--step 1` when you need to check a specific corner.

What to actually look at:

- **Find `A` first.** Marks are snapped to the nearest sampled tile, so the starting location shows
  at any `--step`. A blank cell is nothing at all — the lowest ground is `.`, never a space.
- **Is `A` on a plateau or a pinprick?** The `A` should sit in a patch of one character. Different
  characters around it mean the pad is on a slope and the colony has nowhere to build.
- **Does the water go where the design said?** Follow the `~` from edge to edge. A river that stops
  halfway is a `valley`/`bed` mismatch, not a rendering artefact.
- **Are the `O`s on the right water?** A source on the wrong stream is the single easiest mistake
  to make and the hardest to spot in JSON. (Two rivers sharing a `tag` is now an error for exactly
  this reason — name them.)
- **Is the scrap where the colony can reach it?** `n` clusters should ring `A` at the distance the
  design asked for, not hide behind a ridge.
- **Does the green look like the story says?** "Nothing grows here" and forty `t` around the start
  are the same map contradicting itself.

Compare a before/after preview whenever you change terrain. Two ASCII dumps diff by eye in seconds.

## What a spec looks like

TOML, read top to bottom. Terrain ops run in order — the spec is a sequence of landscaping
instructions, so a later op can cut through what an earlier one raised.

```toml
name = "Wardens 02 The Pods"
size = 96
seed = 2200
description = "What the map browser shows."

[[terrain]]                       # 1. a base to carve out of
op = "base"
height = 7

[[terrain]]                       # 2. relief
op = "noise"
amplitude = 4.0
contrast = 1.7

[[terrain]]                       # 3. named landmarks other blocks can point at
op = "hill"
at = [80, 48]
radius = 26
height = 9
name = "ridge"                    # registers an anchor: `around = "ridge"` later

[[terrain]]                       # 4. water carves last, so it cuts through the relief
op = "river"
points = [[86, 26], [70, 30], [42, 24], [6, 20]]
bed = 5
width = 1.3
valley = [[4.0, 8], [2.5, 7]]     # "within 4 tiles nothing above 8; within 2.5, nothing above 7"
tag = "clean"                     # tags the bed as water
name = "north stream"             # name it — `along = "north stream"` puts sources on *this* one

[[terrain]]                       # 5. the pad the colony starts on
op = "pad"
at = [58, 34]
size = 8
height = 9
name = "start"
reserve = 3                       # keep scatter off the doorstep

[[terrain]]                       # 6. always finish here: integer levels, sane bounds
op = "clamp"
min = 3
max = 20

[[place]]                         # one entity, exactly here
template = "StartingLocation"
at = "start"
dx = -2                           # the entity anchors 2 tiles inside its pad
dy = -2
orientation = "Cw0"

[[place]]                         # or `count` of them along a named watercourse
template = "WaterSource"
along = "north stream"
segment = [0.0, 0.03]             # fraction of the path: 0 is the first control point
count = 2
strength = 1.5
orientation = "Cw0"

[[scatter]]                       # many, by rule
name = "the pods"
templates = ["RuinColumnH1", "RuinColumnH2"]
weights = [3, 2]
levels = [1, 2]                   # feeds `amount_per_level` below
around = "start"
radius = [6, 15]                  # an annulus: never closer than 6, never further than 15
count = 8
spacing = 3
height_range = [6, 12]
away_from = { badwater = 5.0 }    # keep-outs, by mask or anchor name
orientation = "Cw0"
variants = ["A", "B", "C"]
yield = { good = "ScrapMetal", amount_per_level = 15 }

[soil]
contamination = { from = "badwater", offset = 1.5, reach = 9.0 }

[checks]                          # what `check` must be able to prove about this map
require = ["StartingLocation", "WaterSource", "BadwaterSource"]
min_reachable = 1200
reachable_scatter = ["the pods"]  # this cluster must be walkable — name it, don't guess a fraction
```

`reachable_from = "start"` on a `[[scatter]]` is the other half of this: it restricts placement to
the walkable component around a point, so a cluster cannot land across a river. Reach for it
whenever water divides the map — it replaces the fiddly business of shrinking radii until the
annulus happens not to cross the water.

`reachable_scatter` is usually the check you want. Five ruin clusters share the `RuinColumnH*`
templates, so a template-prefix check (`reachable = ["RuinColumn"]`) can only say "at least one of
them somewhere is reachable" — it cannot say "the scrap the colony opens with must be walkable while
the field across the river is meant to need a bridge". Naming the rules says exactly that.

`python3 wardens/tools/mapsmith ops` prints every op with its parameters — read that rather than
guessing a key. The full key-by-key reference is
[`references/spec-reference.md`](references/spec-reference.md).

Three mental models that make specs come out right:

- **Anchors and tags are the glue.** An op with `name` registers a point; an op with `tag` registers
  a set of cells. Later blocks refer to them by name (`around = "ridge"`, `away_from = { badwater =
  9.0 }`, `along = "north stream"`). Naming things is what keeps a spec readable when it grows.
- **Order is meaning.** `noise` then `river` gives a river cut through hills. `river` then `noise`
  gives a lumpy riverbed. `clamp` last, always.
- **Seeds are cheap, but every edit reseeds what follows it.** `seed` reshuffles noise and scatter
  without touching the design, so try three seeds before editing numbers. The other half of that
  coin surprises people: the RNG is one stream consumed in spec order, so moving a scatter's centre
  changes every draw *after* it. Moving two coordinates in the wasteland shifted `RuinColumnH1` from
  7 to 4 and the tree mix from 74/71 to 66/81. Nothing is wrong — but if a `require_at_least` fails
  after an unrelated edit, that is why. Re-read the counts in the build output after any change.

## When something will not build

The tool refuses rather than shipping a map that quietly lost half its content, and the errors are
written to tell you the fix:

| What you see | What it means |
|---|---|
| `scatter 'x': placed 4 of 20 in 1200 attempts` | The rule cannot be satisfied — the annulus is over water, the `height_range` excludes the ground there, or `spacing` is too big for the area. Widen `radius`, loosen the band, or lower `count`. |
| `place along 'clean': 2 watercourses share that tag` | Name the river you mean. This error exists because sources silently landing on the wrong stream is a bug that survives review. |
| `wanted 3 X, placed 0 — N candidate tiles were outside the map` | The `segment` starts before the path enters the map. Move it inland. |
| `unknown anchor 'spring'` | The op that would register it runs later, or is spelled differently. Ops run top to bottom. |
| `[error] ... floating — the ground top here is 9` | An entity's Z is not its column's surface. Almost always a hand-edited coordinate. |
| `[error] ... buried 3 level(s) under the surface` | Only `UndergroundRuins` (and anything you list in `[checks] buried_ok`) may sit inside terrain. |
| `[error] StartingLocation ... pad is not flat` | The pad op ran before something that raised the ground back up, or `at` moved. |
| `[error] 3 of 7 entities from 'near ruins' cannot be reached on foot` | A cluster listed in `[checks] reachable_scatter` is cut off. Move it to the colony's side, or drop it from the list if being cut off is the point. |
| `[warning] only N of M tiles are walkable from the start` | Beavers climb one level unaided; the start is ringed by cliffs. |
| `[warning] reachable_scatter names 'x', which is not a named rule` | A typo, or you are checking a `.timber` instead of the spec — rule names do not survive into the file. |

## Facts about the format worth not rediscovering

A `.timber` is a zip of `world.json`, `map_metadata.json`, `version.txt`, `map_thumbnail.jpg`.
The details, and which parts are verified versus assumed, are in
[`references/timber-format.md`](references/timber-format.md). The ones that bite:

- **Entity Z is the first air layer above the column**, not the top solid block.
- **Plants carry no `Orientation`; every other block object does.** This matches the map the game's
  own editor wrote. Set `orientation = "Cw0"` on ruins, sources and the starting location.
- **The top voxel layer (Z 22) must be air.**
- **Water starts dry.** Sources fill their beds during the first in-game day; the pre-filled column
  encoding is undocumented. Do not fake it.
- **The file claims the GameVersion whose layout was verified** (`0.7.10.0`), so the game migrates it
  on load. Do not bump this to look current — that skips the migration for a layout nobody checked.
- **Template names are unverified against the running game.** `RuinColumnH1..H5`, `UndergroundRuins`,
  `BadwaterSource`, `WaterSource`, `Pine`, `Birch`, `BlueberryBush` come from a map that loaded;
  anything else is a guess, and a guess the game does not recognise is a load-time crash. Say so
  when you introduce one.

## Getting a map into the game

The Wardens build copies `wardens/src/Maps/*.timber` into `Documents/Timberborn/Maps`, where the
New Game screen lists it under *Custom maps*. A map shipped inside the mod folder is **not** listed
by the game — the copy is what makes it exist. `design/wardens-campaign-maps.md` has the runtime
installer plan for players who install from the Workshop.

## Extending the tool

Prefer composing existing ops. When a spec genuinely cannot say what you mean, add an op: write a
function in `wardens/tools/mapsmith/terrain.py`, put it in `OPS`, and give it a docstring — `ops`
prints that docstring, so it is the documentation. Add a test in `wardens/tools/test_mapsmith.py`
next to the others.

The checker is the part worth being strict about. Every check there was written by breaking a good
map in one specific way and asserting the report names it. Keep that habit: a check that quietly
passes a broken map is worse than no check, because it spends someone's playtest.

```bash
uv run --project python --extra dev pytest wardens/tools/test_mapsmith.py -q
```

## Relationship to gen_map.py

`wardens/tools/gen_map.py` is the older, numpy-based generator that wrote the currently shipped
`Wardens Wasteland.timber`. It still works and is still the provenance of that file.
`wardens/maps/wardens-wasteland.map.toml` is the same design as a spec, and mapsmith's checker
validates the old file too. Until someone loads the spec-built wasteland in the game, do not
overwrite the shipped `.timber` with it — swap over once it has been seen to load, and retire
`gen_map.py` then.
