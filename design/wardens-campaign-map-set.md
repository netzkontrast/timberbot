# The Wardens campaign: the map set

> **Status:** concept (2026-09-10); **build-order steps 1 and 2 are done** (level 01 renamed and pinned, the
> generator refactored to `Terrain` + a level registry with byte-identical output). Nothing generated beyond
> level 01. This is the *what* — the land each
> level needs and what the generator must learn to build it. The *how* of getting a map onto the player's
> machine and starting level N on it is [`wardens-campaign-maps.md`](wardens-campaign-maps.md); the levels
> themselves are [`wardens-campaign-concept.md`](wardens-campaign-concept.md) (the five-level essential cut)
> and [`wardens-campaign-arc.md`](wardens-campaign-arc.md) (the full ten); the story each level tells is
> [`wardens-campaign-story.md`](wardens-campaign-story.md). Level 01's map exists and is documented in
> [`wardens-wasteland.md`](wardens-wasteland.md); the generator is
> [`wardens/tools/gen_map.py`](../wardens/tools/gen_map.py).

## 0. Why the map is the rule engine

Timberborn has no objective system, no scenario editor, no win conditions. A level cannot *tell* the player
what to do, and it cannot stop them doing something else. The only thing that reliably constrains play is
**the land itself**.

So every level's design collapses into one question: *what must be true of the ground for the intended play
to be the only play that works?* Level 02 is "keep the clean water clean" only if there is exactly one place
to dam and the badwater arrives above it. Level 08 is "disperse by water" only if the islands cannot be
walked to. When the land does not enforce it, the level is a suggestion, and the Warden is reduced to nagging.

Each map below therefore carries a **contract**: a short list of properties the generator must guarantee and
`gen_map.py --check` must verify. The contract is the level design. The scenery is decoration.

## 1. The set

Ten maps for the full arc, five for the essential cut. Sizes are proposals; the installer and the format do
not care (`wardens-campaign-maps.md` §2 A).

| # | Map name | Level | Size | Cut | Reuses | New work |
|---|---|---|---|---|---|---|
| 01 | `Wardens 01 First Light` | First Light | 96 | ✅ | — | **done** (renamed, pinned, contract checked) |
| 02 | `Wardens 02 The Sump` | The Sump | 96 | ✅ | river, spring, ruins | gorge, confluence |
| 03 | `Wardens 03 The Pods` | The Pods | 96 | ✅ | river, ruins | lake basin, island |
| 04 | `Wardens 04 The Delta` | The Delta | 112 | — | river, lake | braided channels, floodplain |
| 05 | `Wardens 05 The Archive` | The Archive / Green | 128 | ✅ (as *Green*) | ruins, trees | town grid, dry valley |
| 06 | `Wardens 06 The Dry` | The Dry | 112 | — | plateau, seasonal river | drought terracing |
| 07 | `Wardens 07 The Highlands` | The Highlands | 96 | — | river | canyons, waterfalls, two zones |
| 08 | `Wardens 08 The Archipelago` | The Archipelago | 128 | — | lake basin, island | island field, deep channels |
| 09 | `Wardens 09 The City` | The City / The Ark | 128 | ✅ (as *The Ark*) | town grid | dense ruins, sub-surface sources |
| 10 | `Wardens 10 Home` | Home | 96 | ✅ (as epilogue) | **level 01 verbatim** | regeneration pass |

Level 10 is not a new map. It is level 01's seed and heightfield with the contamination zeroed and the trees
grown — the same basin, healed. That is the whole point of the epilogue, and it is a generator flag, not a
design job.

The essential cut is five files: 01, 02, 03, 05 (shipped as *Green*), 09 (shipped as *The Ark*), plus 10 if
the epilogue makes the first release.

## 2. What each map must guarantee

### 01 — First Light *(exists)*

Documented in [`wardens-wasteland.md`](wardens-wasteland.md). Contract already enforced by `check()`: one
`StartingLocation` on a flat 8×8 pad with three blocks of air, at least one `BadwaterSource`, every entity on
the surface and in bounds. Nothing to do but rename the file and keep the seed pinned.

### 02 — The Sump

**The land's job:** make one dam decision matter more than everything else.

- A badwater river enters west, leaves east through **exactly one gorge** — the only column of the map where
  a dam of buildable width spans bank to bank.
- A clean spring feeds a side creek that joins the river **above** the gorge. Dam the gorge and you impound
  both; the level is won by diverting the badwater first.
- Contaminated band along the whole river; a green terrace at the spring, the only trees.

**Contract:** exactly one gorge ≤ 6 tiles wide with banks ≥ 3 above the bed; the confluence is upstream of it;
no second crossing where a dam is cheaper; the spring's clean cells never touch a badwater cell before the
confluence.

### 03 — The Pods

**The land's job:** a safe place that is small, and hard to reach.

- A lake fills the middle, fed by two badwater inlets; its shores are the contaminated ground.
- A green island in the lake, **not reachable on foot** — the vertical architecture (platforms, dams) is the
  only way across. This is where the Vertical tutorial finally earns its place.
- Underwater ruins for the later mines.

**Contract:** the island's tile set is land-disconnected from the starting pad at every Z the beavers can walk;
island area between 40 and 80 tiles (enough for a colony, too small to be comfortable); no island cell within
the inlets' contamination radius at map start. **See §5 — this contract is the one the dry-water problem breaks.**

### 04 — The Delta

**The land's job:** punish anything built low.

- Braided channels across a wide, flat floodplain; the badtide raises everything at once.
- Few high spots, and they are small — the plan cannot be "move uphill", it has to be "build the levee".

**Contract:** ≥ 60% of the buildable area within 2 of the badtide flood level; every high refuge under 100
tiles; at least three distinct channels so no single dam solves the map.

### 05 — The Archive *(shipped as* Green *in the cut)*

**The land's job:** be fertile and empty, with the humans' record in the middle.

- A wide valley, dry but **not poisoned** — the first map where the ground is not the enemy. Everything can be
  green once irrigated; nothing is at the start.
- A human town's ruins at the centre, laid out on a **street grid**, not in organic clusters. The player should
  read it as a place people lived, because level 05 is where they find out no one is coming back.
- One river along the north edge: the water is there, the distance is the problem.

**Contract:** contamination zero everywhere at start; ≥ 70% of tiles irrigable from the river within the
game's moisture radius chain; the ruin grid is rectilinear and legible from the map thumbnail.

### 06 — The Dry

**The land's job:** state the beaver thesis literally — dams raise water, water makes green.

- High desert plateau, one seasonal river that runs in temperate weather and stops in drought.
- Terraces, so a dam at each step irrigates a shelf.

**Contract:** the river's source is weak enough to fail under drought; ≥ 4 dammable terrace steps; no natural
standing water — every irrigated tile is one the player made.

### 07 — The Highlands

**The land's job:** two ways of living, physically separated.

- Canyons and waterfalls. Vertical is the whole map.
- **Two buildable zones** with different character — one wide, green, low (Folktails-shaped); one narrow,
  high, mineral (Iron-Teeth-shaped) — connected only by a route the player has to build.

**Contract:** two zones each ≥ 300 buildable tiles, land-disconnected at start; ≥ 3 waterfall drops of ≥ 4
height on the watercourse; both zones self-sufficient in principle (each has water access and scrap).
**Height budget is tight here — see §4.**

### 08 — The Archipelago

**The land's job:** make the machines stop.

- Islands, one per colony, separated by water the bots cannot cross without the beavers' bridges.

**Contract:** ≥ 5 islands, each ≥ 150 tiles, each land-disconnected from every other; every island has fresh
water and at least one scrap source. **The dry-water problem hits this map hardest (§5).**

### 09 — The City *(shipped as* The Ark*)*

**The land's job:** endless material, and ground that poisons itself.

- A plateau of dense human ruins — scrap is effectively unlimited, which is right for an endgame of logistics.
- Badwater seeping from **sub-surface sources**, so the ground contaminates itself unless the barriers hold.
- The Ark's foundation pre-placed beside the Core.

**Contract:** scrap yield across the map ≥ the wonder's full cost plus 50%; ≥ 6 sub-surface badwater sources
distributed so no single barrier line covers them; a flat footprint for the Ark adjacent to the starting pad.

### 10 — Home

**The land's job:** be level 01, and be unrecognisable.

- Same seed, same heightfield, same ruins. Contamination zeroed. Trees grown across the basin. The Sump clean.

**Contract:** heightfield byte-identical to level 01; contamination array all zeros; tree count ≥ 20× level 01's;
no `BadwaterSource` (the one map that legitimately fails the current check — see §6).

## 3. Terrain primitives

The generator already has most of the toolkit. The gap is smaller than the map count suggests.

| Primitive | Have | Needed by |
|---|---|---|
| Value noise, Catmull-Rom splines, distance fields | ✅ `value_noise`, `catmull_rom`, `distance_field` | all |
| Meandering watercourse from control points | ✅ `build_terrain` | 02, 04, 06, 07 |
| Basin / crater pond | ✅ (the Sump, the spring) | 03, 08 |
| Rim hills, plateau relief | ✅ | 05, 06, 09 |
| Organic ruin clusters with scrap yields | ✅ `cluster()` | 02, 03, 04 |
| Underground ruins | ✅ | 03, 09 |
| Tree planting with growth progress | ✅ `plant()` | 05, 10 |
| Contamination from distance-to-badwater | ✅ `contamination()` | 02, 03, 04 |
| **Gorge / choke point** | ✗ | 02 |
| **Confluence of two watercourses** | ✗ | 02 |
| **Lake basin with inlets** | ✗ | 03, 08 |
| **Island (land-disconnected region)** | ✗ | 03, 08 |
| **Braided channels / floodplain** | ✗ | 04 |
| **Rectilinear town grid** | ✗ | 05, 09 |
| **Terraced steps** | ✗ | 06 |
| **Canyon walls + waterfall drops** | ✗ | 07 |
| **Sub-surface water sources** | ✗ | 09 |
| **Regeneration pass** (zero contamination, grow trees) | ✗ | 10 |

Eleven new primitives across nine maps. Six of them (gorge, lake, island, town grid, terraces, regeneration)
carry two maps each or more.

## 4. The generator has to stop being one map

`gen_map.py` today is a single `Wasteland` class whose design anchors are literals scaled by `f = size / 96`
— the start pad, the Sump ellipse, the nine river control points, the spring. That is exactly right for one
map and wrong for ten.

The shape it needs:

- A `Terrain` base holding the shared toolkit (§3) and the `world()` / `write_timber()` / `render()` plumbing,
  which is level-independent already.
- One class per level composing primitives, declaring its own `MAP_NAME`, `seed`, `size` and — the new part —
  its **contract assertions**.
- A registry so `--level 02` selects it, `--level all` regenerates the set, and the default stays level 01.
- `MAP_NAME`, `GAME_VERSION` and `TIMESTAMP` move from module globals onto the level.

Two constraints that bite:

**`LAYERS = 23` is hardcoded**, the top layer must be air, and level 01 clips heights to `3..19`. That leaves
about 18 usable levels of relief. Level 07 (canyons, waterfalls, two vertical zones) and level 09 (a plateau
over sub-surface sources) both want more. `LAYERS` becomes per-level, and the checker's `sx * sy * LAYERS`
voxel-count assertion reads it from the level rather than the module.

**Seeds must be pinned per level and never changed after release.** The maps are reproducible byte-for-byte
today, and level 10 depends on regenerating level 01's exact heightfield. A seed change after a player starts
the campaign is a different map under the same name.

## 5. The problem that changes three designs: water starts dry

`wardens-wasteland.md` records it as a limitation: the `WaterMapNew` column encoding for pre-filled water is
undocumented, so maps ship with **zero water** and the sources fill the channels over the first day.

For level 01 that is a cosmetic delay. For any map whose puzzle is *water separates things*, it is fatal:

- **Level 03:** the island is "reachable only by platform" — until the lake fills. On day 1 the player walks
  across a dry lakebed and the level's central constraint never happens.
- **Level 08:** every island is connected on day 1. The map is one landmass with decorative dips.
- **Level 04:** the floodplain is a plain. The badtide threat arrives later than the buildings do.

Three ways out, in order of preference:

1. **Decode the water column encoding** and ship maps pre-filled. This is the real fix and it unblocks all
   three maps. It is a format job on `WaterMapNew.WaterColumns` / `ColumnOutflows` — the same class of work
   that produced the current generator, and the one open question most worth answering before level 03 is
   designed in detail.
2. **Separate with terrain, not water.** Cut the channels *below* the depth beavers can path through and let
   the water be scenery on top. The separation holds from frame one and does not depend on the fill. Costs the
   visual read — a dry chasm is not a strait.
3. **Accept a grace period** and design around it: make the island unattractive (no scrap, no water access)
   until the lake fills, so walking there early gains nothing. Weakest option; it relies on the player not
   doing the obvious thing.

Recommendation: attempt (1) once, timeboxed, before committing level 03's design. Fall back to (2). Level 08
should not be built at all until this is settled — it is the map with no fallback.

## 6. Per-level checks

`check()` today verifies the format and the things the *game* would choke on. The map contracts need a second
tier: the things the *level* would choke on. Same function, level-aware.

New assertions the contracts imply, all computable from the heightfield and the entity list:

- **Land connectivity** — flood-fill the walkable surface from the starting pad and assert a named region is
  reachable, or is not. Serves 03, 07, 08 directly; it is the single most valuable check in the set.
- **Buildable area per region** — tile counts against the contract's floor (07, 08).
- **Channel width at a named location** — the gorge (02).
- **Contamination coverage** — fraction of tiles above a threshold (04, 05, 10).
- **Total scrap yield** — sum `Yielder:Ruin` amounts against the wonder cost (09).
- **Irrigable fraction** — tiles within moisture reach of a water cell (05, 06).

Two existing assertions need to become per-level rather than global: `no BadwaterSource` is a failure
everywhere except level 10, where it is required; and the voxel count must read the level's `LAYERS`.

`tools/validate.py` gains the campaign cross-check named in `wardens-campaign-maps.md` §4 — every level in the
table has a map file, every map file has a level, and the map name in the level table matches the name inside
the `.timber`.

## 7. Build order

Each step is testable before the next begins.

1. ~~**Rename level 01** to `Wardens 01 First Light` and pin its seed.~~ **Done 2026-09-10.** The installer
   (`WardensMapInstaller.cs`) and the level-detection-by-map-name path (`WardensCampaign.cs`) are built against
   it; neither has been loaded in-game yet.
2. ~~**Refactor the generator** to `Terrain` + level registry, with level 01 as the only entry.~~ **Done
   2026-09-10.** `world.json`, `version.txt` and the thumbnail are byte-identical to the pre-refactor file;
   only `map_metadata.json` changed, and only because the description now names the level. Level 01's contract
   (§2) is implemented as `FirstLight.contract` and runs on every `--check`.
3. **Level 10 (`Home`)** next, not level 02. It is the regeneration pass over level 01: no new primitives, and
   it proves the registry, per-level contracts and the per-level `BadwaterSource` rule for the cost of a flag.
4. **Answer the water question** (§5). Timeboxed. Everything after this depends on the answer.
5. **Level 02 (`The Sump`)** — gorge + confluence, the first genuinely new terrain, and the first map whose
   contract is a real constraint.
6. **Level 03 (`The Pods`)** — lake, island, and the first use of the connectivity check.
7. **Level 05 (`The Archive`)** — town grid and the dry valley. Completes the essential cut except the endgame.
8. **Level 09 (`The City`)** — dense ruins, sub-surface sources, the Ark footprint. The cut ships.
9. **Levels 04, 06, 07, 08** — the full arc, in whatever order the story work reaches them.

Steps 1–3 need no new terrain code and no in-game verification beyond a load. That is the cheapest possible
proof that a campaign of maps works at all.

## 8. Open questions

- **The water encoding** (§5). The one that gates three maps. Everything else can proceed without it.
- **Does the game list a map shipped inside the mod?** Assumed no (`wardens-campaign-maps.md` §1); the runtime
  installer is the mechanism either way. Ten maps make the copy-on-start cost worth measuring.
- **Do `RuinColumnH*`, `UndergroundRuins` and the tree templates still exist under those names in 1.1?** Open
  since level 01 and unverified — a rename breaks every map in the set at once, which is an argument for
  answering it before nine more files depend on it.
- **Height budget for level 07.** Whether 23 layers is enough for canyons that read as canyons, or whether the
  format tolerates more.
- **Map size and generation time.** Level 01 at 96² is fast; the checks proposed in §6 are flood fills over
  128² × 23. Worth measuring before `--level all` becomes a routine command.
- **Is ten the right number?** The essential cut is five and the arc's §7 already folds the other five into
  them. Nothing in this document argues for building 04, 06, 07 or 08 before the cut ships.
