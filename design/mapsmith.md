# mapsmith: maps as specs

> **Status:** built 2026-09-10, `wardens/tools/mapsmith/` + `wardens/maps/*.map.toml`. Two specs
> ship: the wasteland (the same design as [`wardens-wasteland.md`](wardens-wasteland.md), as data)
> and level 02, "The Pods", the second campaign level asked for by
> [`wardens-campaign-maps.md`](wardens-campaign-maps.md) §6.5. Neither has been loaded in-game.
> The agent-facing manual is the `timberborn-mapsmith` skill in `.claude/skills/`.

## Why

`gen_map.py` wrote one map well. The campaign plan asks for a map per level, each with the same
bones and different intent, and every one of them written by someone — usually an agent — who
cannot run Timberborn to see what they made. Two problems follow from that, and they are the whole
justification for this tool:

1. **A map per level cannot be a script per level.** Hand-written numpy for each map means the
   fifth map re-derives the first map's river maths, and a change to the format touches five files.
2. **No game means no feedback.** The only signals available are a preview you can read and a
   checker that knows what the game rejects. If those are weak, a bad map is discovered by a human
   spending an evening on it.

So: the land is data, the ops are shared, and the checker is the part we are strict about.

## Shape

```
wardens/maps/*.map.toml          the specs — what each map is
wardens/tools/mapsmith/
  grid.py        noise, splines, distance fields          (stdlib)
  build.py       the build state: heights, masks, anchors, entities, walkability
  terrain.py     the eleven ops a spec can use            <- extend here
  placement.py   [[place]] and [[scatter]]
  world.py       world.json / metadata / zip              <- the format lives here
  checks.py      everything the game would reject         <- be strict here
  preview.py     ASCII for agents, PNG for docs           (stdlib PNG writer)
  spec.py        TOML in, MapBuild out
  cli.py         build | preview | check | describe | new | ops
wardens/tools/test_mapsmith.py   43 tests, ~0.5 s
```

**Standard library only.** `gen_map.py` needs numpy and Pillow, which means it does not run in a
fresh container or CI — exactly where an agent works. A 96×96×23 map is 211k voxels; pure Python
builds it in well under a second, so the dependency bought nothing and cost the ability to run
anywhere. Pillow is used opportunistically for a nicer map-browser thumbnail and for nothing else.

## The three decisions worth recording

**Terrain ops are a sequence, not a scene graph.** A spec reads top to bottom as landscaping
instructions: base, relief, hills, then water carving through them, then the pad, then clamp. That
ordering is load-bearing and visible, which is what makes a spec reviewable by someone who has
never read the code.

**Names are the interface between blocks.** An op registers an anchor (`name = "spring"`) or a mask
(`tag = "badwater"`); placement points at those names (`around = "spring"`, `away_from = { badwater
= 9.0 }`, `along = "north stream"`). The alternative — coordinates repeated in six places — is how
a spec drifts out of sync with itself.

**Refuse rather than under-deliver.** A scatter rule that cannot place what it was asked for
raises, naming the constraint to loosen. A `[[place]] along` that names a tag two rivers share
raises rather than picking the last one defined — that bug (sources on the wrong stream) was found
by reading an ASCII preview during development, and it is invisible in the JSON.

## Checking, in place of a game

`mapsmith check` proves what can be proved offline:

- structural: four zip members, JPEG magic, voxel and per-cell array lengths, top layer air,
  metadata size agreeing with `MapSize`, unique entity ids;
- placement: every entity on its column's surface, nothing floating or swallowed by terrain, only
  `UndergroundRuins` buried, exactly one `StartingLocation` with an orientation, its pad flat with
  headroom;
- playability: a flood fill from the starting location over ground a beaver can climb (one level,
  water blocks), against `min_reachable`, plus "is any scrap actually reachable on foot".

Each check was written by breaking a good map in one specific way and asserting the report names
it — including the two the original `gen_map.py --check` missed (a `StartingLocation` swallowed by
raised terrain, and an entity buried where it has no business being). The checker validates the
shipped `gen_map.py` output cleanly, so the two generators cross-check each other.

What it cannot prove, and what therefore stays an open question for the first in-game load: whether
the game accepts a template name, whether the 0.7.10 layout still migrates, and whether the map is
any fun. See the skill's `references/timber-format.md` for the verified/assumed split.

## Relationship to gen_map.py

`gen_map.py` remains the provenance of the shipped `Wardens Wasteland.timber` and still runs.
`wardens/maps/wardens-wasteland.map.toml` is the same design as a spec; its output is equivalent,
not byte-identical (different RNG). Swap the shipped file over once the spec-built wasteland has
been seen to load in-game, and retire `gen_map.py` then — not before, because replacing a
format-verified artefact with an unverified one on the strength of an offline checker is the exact
trade this tool exists to avoid.

## Next

- Load both maps in the game; answer the open questions in `references/timber-format.md`.
- Level 02's chapter table and the per-level `validate.py` checks (`wardens-campaign-maps.md` §6.5).
- If the campaign grows past a handful of levels, `[variants]` already covers families of one map;
  a `levels.toml` index tying map name to chapter table is the next thing, not more ops.
