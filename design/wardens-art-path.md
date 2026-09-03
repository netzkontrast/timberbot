# Getting the Wardens out of Iron Teeth clothes

> 2026-09-03. Every Wardens building is a vanilla Iron Teeth mesh with our specs. This is the
> route from "Iron Teeth in a blue light" to buildings that read as the Wardens', ordered by
> cost. Each rung stands on the one below; none needs the ones above.

## What is fixed and what is free

A Timberborn building is four things: a **blueprint** (JSON, ours), a **timbermesh** (a `.timbermesh`
file, vanilla or Blender-exported), **materials** (Unity assets inside the game bundles, referenced by
name; `MaterialCollection.IronTeeth` lists the 34 the faction uses) and **prefabs** for attachments
(smoke, fire, lights; bundle assets too). Blueprints and meshes can be ours. Materials and prefabs
cannot be created from a mod folder, only *referenced* or *patched at runtime*. That single fact
decides the order below.

## Rung 1: tint the shared materials (hours, no tools)

All Iron Teeth buildings draw from one UberAtlas: `BaseMetal`, `PaintedMetal`, `Plaster_Orange`,
`PlasteredWood_Orange`, `BaseWood_Grey/DarkBrown/Indigo`, `IrregularPlanks_*`, `RoofPlanks`,
`Details`, `Paper`, `WindowsAtlas`. `WardensMaterialPatcher` already patches textures of three
materials by name; extending it with a **color table** (`_BaseColor` tint per material, the same
property the game's own `MeshDrawer` sets) recolors *every* building in a Wardens game at once:

| Material | Iron Teeth | Wardens tint |
|---|---|---|
| PaintedMetal, Plaster_Orange, PlasteredWood_Orange | rust orange | steel blue `(0.55, 0.72, 0.85)` |
| BaseMetal, Details | grey metal | keep, slightly cooler `(0.85, 0.92, 1.0)` |
| BaseWood_*, IrregularPlanks_*, RoofPlanks | wood browns | charcoal `(0.45, 0.47, 0.5)` |
| WindowsAtlas, Paper | warm glass, paper | cyan glass `(0.6, 0.95, 1.0)` |

Restored on unload, faction-gated, zero art. This is also the only rung that touches the *whole*
Iron Teeth set at once, so it stays useful even after custom meshes exist (decorations, paths, shafts).
Limit: a tint multiplies the base map; it cannot add the warning-stripe yellow or a cyan emissive.

**Already done in this rung:** the faction illumination color `WardensCyan` (Core, Cruncher, Badwater
Cell glow cyan; the Burner keeps the orange fire light), and the bot / carrying / zipline textures.

## Rung 2: swap and kitbash vanilla meshes (days, no tools)

Two blueprint techniques, both verified in the vanilla dump:

1. **Model swap.** `Children.#Finished.TimbermeshSpec.Model` can point at any vanilla mesh with the
   same footprint; colliders and construction-stage children come along by copying the donor's
   `Children` and `BlockObjectSpec`. Same-footprint donors for our roles:

   | Building | Now | Candidates (same footprint) |
   |---|---|---|
   | Badwater Cell 3x3x3 | Steam Engine | **Centrifuge** (spinning drum, Water group), Food Factory (vat + goo material), Large Tank |
   | Sludge Burner 3x3x3 | Steam Engine | keep: the smoke attachments are its point |
   | Cruncher 4x4x3 | Numbercruncher | keep (only 4x4 in the set) |
   | Reed Bed 2x2x2 | Farmhouse | Mud Bath, Tapper's Shack |
   | Sludge Pump 2x3x3 | Deep Badwater Pump | Grease Factory, Coffee Brewery, Industrial Lumber Mill |
   | Charging Post 1x1x1 | Charging Station | Massager, Scratcher, Medical Bed |
   | Core 3x3x5 | District Center | none; kitbash instead |

   Caveat: specs that reference mesh internals (`TemplateAttachmentsSpec` parents like `#EmptySmoke1`,
   `*AnimatorSpec`, `WorkshopWorkerHiderSpec`) must be dropped or taken from the donor.

2. **Kitbash by nesting.** A child with `TimbermeshSpec` + `TransformSpec {Position, Rotation, Scale}`
   places any mesh inside another building; `"Name#nested": {"BlueprintPath": ..., "Modification":
   {"TransformSpec": ...}}` nests a whole blueprint (vanilla does this 309 times for construction
   bases). Plan for the three signature buildings:
   - **Core** = District Center + two Charging Station meshes on the flanks (Position x=-1 / x=3, the
     1x1 stations tucked against the 3x3) + a Lantern mesh on top as the data-light.
   - **Cruncher** = Numbercruncher + a Speaker/Pole Banner mast (antenna) on a corner.
   - **Sludge Burner** = Steam Engine + Brazier meshes at two corners (open flames).
   The nested meshes render with their own materials, so Rung 1 tints them too.

The generator (`wardens/tools/gen_buildings.py`) is the place for both; nothing here needs C#.

## Rung 3: own low-poly meshes through Blender (weeks, needs Blender)

The `.timbermesh` exporter is Mechanistry's Blender add-on (github.com/mechanistry/timbermesh).
Blender is not installed on this machine; once it is, meshes can be built **headless and procedurally**
(`blender -b -P build_core.py`): box/cylinder kitbashes with vertex colors match the game's flat look
better than sculpted art, and a script gives every building the same silhouette language (angular
housings, external pipes, one cyan slot light). The vanilla `TimberbornExampleModels.blend` and the
JamStove template show naming, pivots and the construction-stage convention (`*.ConstructionStage0`).

Order of new meshes, by how much they carry the identity: Core, Cruncher, Sludge Burner, Badwater
Cell, then the Chapter 2 set. Everything else stays vanilla + tint.

## Rung 4: the Leaf Coats route (blocked)

Their 575-subject bundle would be the fastest way to a full non-Iron-Teeth set, but UnityPy cannot parse
this game version's serialization, AssetRipper would need a download you have not approved, and the
models are Bobingabout's. Not a path for a shippable mod.

## Recommendation

Rung 1 now (a 30-line color table in `WardensMaterialPatcher`), Rung 2 for the Core, Cruncher and
Badwater Cell next, Rung 3 when Blender is in. Rung 1 + 2 together already make a Wardens town read
as blue-lit steel with three distinctive landmarks, without a single new asset file.

## 2D character art: image-gen prompt (start-screen portrait)

`WardensFullNewGame.png` (the New Game faction-select portrait, see `wardens/README.md` "Art") is
now AI-generated rather than recolored. This is the prompt that produced it (ChatGPT image
generation); reuse it verbatim for re-rolls, and feed anything it returns through
`wardens/tools/install_avatar.py` to fit the mod's 420x560 canvas exactly.

```
Character lore: the Wardens are bot laborers stranded alone in a poisoned wasteland long after
whatever catastrophe emptied it of people. They were built for exactly one purpose: keep the
machines running, make the land livable again, and record everything, so that one day beavers
can live here and, eventually, humans can be brought back. They have no comfort needs, only
power and data; a Warden that runs out of charge simply stops where it stands. They scavenge
wrecked structures for scrap rather than harvest anything living, because in Chapter 1 nothing
living has grown here yet. The overall feel is a patient, weathered guardian, more monk-soldier
than machine: stone-carved, cloaked, quietly devoted to a task that started before it can
remember and won't end until the wasteland is green again.

A single full-body character, front-facing hero pose, chibi-proportioned (large head, short
stocky 4-5 head-tall body). A stone-and-ceramic construct in the shape of a beaver: a rounded
segmented head with two large glowing cyan hexagonal eye-lenses, a dark recessed grille mouth
with two visible front teeth, and a flat beaver-tail-like crest at the back of the skull. Body
built from chunky interlocking armor plates in pale weathered grey-white stone/ceramic, each
plate lightly speckled with small dark pockmarks, with dark grey-black joints and hex-bolt
details at the shoulders, elbows, hips and knees. A deep royal-blue cloth cape, torn and
tattered at the hem, draped over both shoulders and falling to calf height. A circular medallion
on the chest and a matching hexagonal medallion on the belt, both pale stone rimmed, each
holding a small glowing cyan pine-tree silhouette icon; a triangular blue tabard hangs from the
belt medallion. Big blocky fists, thick stubby legs, standing solidly with feet shoulder-width
apart. Digitally painted illustration style, soft cel-influenced shading, subtle rim light in
cyan along the edges facing the eyes. Plain solid background, single character only, no ground
shadow, no other props, no text or logo. Portrait orientation, 3:4 aspect ratio (e.g. 1024x1365),
transparent background PNG.
```

Notes for reuse:
- "Transparent background PNG" is load-bearing: without it, `install_avatar.py` pads/resizes a
  flat-color background instead of cutting the character out, and the result shows a colored box
  behind the portrait on the faction-select screen.
- The 3:4 target is the mod's convention, not a hard game requirement (the panel scales-to-fit
  any aspect ratio), so a close miss is fine; `install_avatar.py` pads rather than crops, so it
  never needs the source to be exact.
- For the companion avatars (`WardensChild.png`, `WardensContaminatedAdult/Child.png`,
  `WardensBot.png`, `WardensLogo.png`) the same description works with the pose/scope changed
  (a smaller/younger build for the child, a cracked-and-dim variant of the eye lenses and one or
  two plates for "contaminated", a head-and-shoulders crop for the logo) — swap the framing
  instruction, keep the material, color and style language identical so the set reads as one
  faction.
