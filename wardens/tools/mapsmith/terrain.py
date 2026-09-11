"""Terrain ops: the verbs a `[[terrain]]` block in a map spec can use.

Every op takes the build and its own keyword parameters, mutates the heightfield, and may tag the
cells it touched (`tag = "badwater"`) and register an anchor (`name = "spring"`) for later ops and
for placement. Ops run in the order they appear in the spec, so the spec reads as a sequence of
landscaping instructions: raise a plateau, ring it with hills, cut a river through it, flatten a pad.

Add an op by writing a function and putting it in OPS — that is the whole extension mechanism.
"""
from __future__ import annotations

import math
from collections.abc import Callable

from .build import MapBuild, SpecError, ellipse_mask
from .grid import Mask, catmull_rom, distance_field, value_noise


def _falloff(t: float, exponent: float) -> float:
    """1 at the centre, 0 at the edge."""
    return max(0.0, min(1.0, t)) ** exponent


def _protected(b: MapBuild, avoid) -> set[tuple[int, int]]:
    """Cells a landscaping op must not touch, named by mask.

    Ops run in order, and water is usually carved late so it cuts through the relief. But a gorge is
    made by raising ground *beside* a channel that already exists, and a hill that also fills in the
    channel is not a gorge. `avoid = "badwater"` lets an op run after the water without undoing it.
    """
    if not avoid:
        return set()
    names = [avoid] if isinstance(avoid, str) else list(avoid)
    out: set[tuple[int, int]] = set()
    for name in names:
        if name not in b.masks:
            raise SpecError(f"avoid: no mask {name!r}; known: {', '.join(sorted(b.masks)) or 'none'}")
        out.update(b.masks[name].points())
    return out


def op_base(b: MapBuild, *, height: float = 6.0, **_) -> None:
    """Set every column to one height. Usually the first op."""
    b.height.cells = [float(height)] * len(b.height)


def op_noise(b: MapBuild, *, amplitude: float = 3.0, octaves=(8, 16, 32), weights=(1.0, 0.5, 0.25),
             contrast: float = 1.9, mode: str = "add", tag: str | None = None, **_) -> None:
    """Rolling relief from summed value noise.

    `contrast` stretches the noise around its midpoint before scaling, so hills and hollows read
    instead of everything sitting at the mean. `mode` is add | set | max.
    """
    if len(octaves) != len(weights):
        raise SpecError(f"noise: {len(octaves)} octaves but {len(weights)} weights; "
                        f"every octave needs its own weight or it is silently dropped")
    noise = value_noise(b.rng, b.size, tuple(octaves), tuple(weights))
    touched = Mask(b.size)
    for i, n in enumerate(noise.cells):
        stretched = max(0.0, min(1.0, (n - 0.5) * contrast + 0.5))
        delta = stretched * amplitude
        if mode == "add":
            b.height.cells[i] += delta
        elif mode == "set":
            b.height.cells[i] = delta
        elif mode == "max":
            b.height.cells[i] = max(b.height.cells[i], delta)
        else:
            raise SpecError(f"noise: unknown mode {mode!r} (add | set | max)")
        touched.cells[i] = True
    b.tag(tag, touched)


def op_rim(b: MapBuild, *, width: float = 10.0, height: float = 5.0, exponent: float = 2.0,
           tag: str | None = None, **_) -> None:
    """Raise the map's border into a ring of hills, so the interior reads as a basin."""
    size = b.size
    touched = Mask(size)
    for y in range(size):
        for x in range(size):
            edge = min(x, size - 1 - x, y, size - 1 - y)
            if edge >= width:
                continue
            lift = _falloff((width - edge) / width, exponent) * height
            b.height.cells[y * size + x] += lift
            if lift >= 0.5:
                touched.set(x, y)
    b.tag(tag, touched)


def op_hill(b: MapBuild, *, at, radius: float = 12.0, height: float = 6.0, exponent: float = 1.5,
            floor: float | None = None, name: str | None = None, tag: str | None = None,
            avoid=None, **_) -> None:
    """A cone of ground rising to `height` above `floor` (or above whatever is there).

    `avoid = "badwater"` (or a list of masks) leaves those cells alone, so a hill raised after the
    river carves becomes a bank instead of a landslide into the channel.
    """
    cx, cy = b.resolve_point(at)
    size = b.size
    touched = Mask(size)
    keep_clear = _protected(b, avoid)
    for y in range(max(0, int(cy - radius - 1)), min(size, int(cy + radius + 2))):
        for x in range(max(0, int(cx - radius - 1)), min(size, int(cx + radius + 2))):
            d = math.hypot(x - cx, y - cy)
            if d > radius or (x, y) in keep_clear:
                continue
            lift = _falloff((radius - d) / radius, exponent) * height
            base = floor if floor is not None else b.height.at(x, y)
            b.height.cells[y * size + x] = max(b.height.at(x, y), base + lift)
            touched.set(x, y)
    b.anchor(name, (cx, cy))
    b.tag(tag, touched)


def op_river(b: MapBuild, *, points, bed: float = 4.0, width: float = 1.7, valley=None,
             tag: str = "water", name: str | None = None, per_segment: int = 40, **_) -> None:
    """Carve a watercourse along a smooth curve through `points`.

    `valley` is a list of `[distance, max_height]` steps applied outside the bed: `[[4.5, 7], [3, 6]]`
    means "within 4.5 tiles nothing is higher than 7, within 3 tiles nothing is higher than 6",
    which is what turns a trench into banks. Cells at or inside `width` become the bed and are
    tagged (default tag `water`, so `free()` and the walkability check treat them as water).
    """
    path = catmull_rom([tuple(p) for p in points], per_segment)
    dist = distance_field(b.size, path, max_dist=max(width, 4.0) + max((v[0] for v in (valley or [])), default=0.0) + 2)
    steps = sorted(([float(d), float(h)] for d, h in (valley or [])), key=lambda s: -s[0])
    size = b.size
    bed_mask = Mask(size)
    # A tributary carved after its trunk must not raise the trunk's bed where they meet: level 02's
    # creek (bed 6) overwrote the river (bed 5) at the confluence, and the game pooled the river
    # behind that one-level sill and never let it past (PLAYTEST.md, 2026-09-11).
    earlier = [m for n, m in b.masks.items() if n in b.path_tags or n in ("water", "badwater", "clean")]
    for i, d in enumerate(dist.cells):
        x, y = i % size, i // size
        for limit, cap in steps:
            if d <= limit:
                b.height.cells[i] = min(b.height.cells[i], cap)
        if d <= width:
            if any(m.at(x, y) for m in earlier):
                b.height.cells[i] = min(b.height.cells[i], float(bed))
            else:
                b.height.cells[i] = float(bed)
            bed_mask.set(x, y)
    b.tag(tag, bed_mask)
    key = name or f"{tag}#{len(b.path_tags.get(tag, [])) + 1}"
    if key in b.paths:
        raise SpecError(f"river: two watercourses are both called {key!r}; give each a distinct `name`")
    b.paths[key] = path
    b.path_beds[key] = float(bed)
    b.path_tags.setdefault(tag, []).append(key)
    b.path_tag_of[key] = tag
    b.anchor(name, path[len(path) // 2])


def op_basin(b: MapBuild, *, at, radii, bed: float = 4.0, shore: float | None = None,
             shore_width: float = 2.0, tag: str = "water", name: str | None = None, **_) -> None:
    """An elliptical pit — a pond, a sump, a quarry. `shore` caps the height of the ring around it."""
    cx, cy = b.resolve_point(at)
    rx, ry = (float(radii[0]), float(radii[1])) if isinstance(radii, (list, tuple)) else (float(radii), float(radii))
    inner = ellipse_mask(b.size, cx, cy, rx, ry)
    for x, y in inner.points():
        b.height.set(x, y, float(bed))
    if shore is not None:
        ring = ellipse_mask(b.size, cx, cy, rx + shore_width, ry + shore_width)
        for x, y in ring.points():
            if not inner.at(x, y):
                b.height.set(x, y, min(b.height.at(x, y), float(shore)))
    b.tag(tag, inner)
    b.anchor(name, (cx, cy))


def op_crater(b: MapBuild, *, at, radius: float = 2.6, depth: float = 2.0, tag: str = "water",
              name: str | None = None, **_) -> None:
    """Sink a round hollow relative to the ground already there — a spring pool on a hilltop."""
    cx, cy = b.resolve_point(at)
    floor = b.height.at(int(cx), int(cy)) - depth
    pool = ellipse_mask(b.size, cx, cy, radius, radius)
    for x, y in pool.points():
        b.height.set(x, y, floor)
    b.tag(tag, pool)
    b.anchor(name, (cx, cy))


def op_channel(b: MapBuild, *, start, direction: str = "north", length: int = 20, drop: float = 6.0,
               offset: int = 0, tag: str = "water", **_) -> None:
    """A one-tile notch running off a crater or basin, so an overflow has somewhere to go.

    `drop` is how many tiles of channel it takes to climb one level back up: a large `drop` keeps
    the notch nearly level, a small one makes it a staircase.
    """
    cx, cy = b.resolve_point(start)
    dx, dy = {"north": (0, -1), "south": (0, 1), "east": (1, 0), "west": (-1, 0)}.get(direction, (0, 0))
    if (dx, dy) == (0, 0):
        raise SpecError(f"channel: unknown direction {direction!r} (north | south | east | west)")
    floor = b.height.at(int(cx), int(cy))
    cut = Mask(b.size)
    for step in range(1, length + 1):
        x = int(cx) + dx * (step + offset)
        y = int(cy) + dy * (step + offset)
        if not b.height.inside(x, y):
            break
        b.height.set(x, y, min(b.height.at(x, y), floor + (step / drop if drop else 0)))
        cut.set(x, y)
    b.tag(tag, cut)


def op_pad(b: MapBuild, *, at, size: int = 8, height: float | None = None, name: str | None = None,
           tag: str | None = None, reserve: int = 0, **_) -> None:
    """Flatten a square. The starting location needs one; so does anything with a big footprint.

    `at` is the pad's lower-left corner. `reserve` widens the keep-out ring around it so scatter
    does not drop ruins on the doorstep.
    """
    x0, y0 = (int(v) for v in b.resolve_point(at))
    level = float(height) if height is not None else round(b.height.at(x0, y0))
    flat = Mask(b.size)
    for y in range(y0, min(b.size, y0 + size)):
        for x in range(x0, min(b.size, x0 + size)):
            b.height.set(x, y, level)
            flat.set(x, y)
    b.anchor(name, (x0 + size / 2.0, y0 + size / 2.0))
    if name:
        b.anchors[f"{name}:corner"] = (float(x0), float(y0))
    b.tag(tag, flat)
    if reserve:
        b.reserve(x0 - reserve, y0 - reserve, x0 + size - 1 + reserve, y0 + size - 1 + reserve)


def op_terrace(b: MapBuild, *, box, steps, side: str = "west", avoid=None, name: str | None = None,
               tag: str | None = None, **_) -> None:
    """Flatten a rectangle into flat bands that step down (or up) one after another — a shore a
    colony can walk down to the water, instead of a cliff it can only look over.

    `box` is [x1, y1, x2, y2], inclusive. `steps` is a list of [width, height] bands laid from `side`
    (west | east | north | south) inwards: `[[2, 7], [3, 6]]` against a pad at 8 gives 8 → 7 → 6, and
    beavers climb one level unaided. A band that runs past the box is cut at its edge; columns the
    bands do not reach keep their height. `avoid = "badwater"` leaves those cells alone.
    """
    try:
        x1, y1, x2, y2 = (int(v) for v in box)
    except (TypeError, ValueError) as exc:
        raise SpecError(f"terrace: box must be [x1, y1, x2, y2], got {box!r}") from exc
    if x2 < x1 or y2 < y1:
        raise SpecError(f"terrace: box {box!r} is empty (x2 < x1 or y2 < y1)")
    axis = {"west": (x1, 1, "x"), "east": (x2, -1, "x"), "north": (y1, 1, "y"), "south": (y2, -1, "y")}
    if side not in axis:
        raise SpecError(f"terrace: unknown side {side!r} (west | east | north | south)")
    start, direction, along = axis[side]
    skip = _protected(b, avoid)
    flat = Mask(b.size)
    offset = 0
    for band in steps:
        width, level = int(band[0]), float(band[1])
        if width < 1:
            raise SpecError(f"terrace: band width must be at least 1, got {band!r}")
        for i in range(offset, offset + width):
            line = start + direction * i
            if along == "x" and not x1 <= line <= x2 or along == "y" and not y1 <= line <= y2:
                continue
            cells = ((line, y) for y in range(y1, y2 + 1)) if along == "x" else ((x, line) for x in range(x1, x2 + 1))
            for x, y in cells:
                if b.height.inside(x, y) and (x, y) not in skip:
                    b.height.set(x, y, level)
                    flat.set(x, y)
        offset += width
    b.tag(tag, flat)
    b.anchor(name, ((x1 + x2 + 1) / 2.0, (y1 + y2 + 1) / 2.0))


def op_smooth(b: MapBuild, *, passes: int = 1, strength: float = 1.0, **_) -> None:
    """Box-blur the heightfield. Softens noisy ground without moving carved features much."""
    size = b.size
    for _pass in range(passes):
        src = list(b.height.cells)
        for y in range(size):
            for x in range(size):
                total, n = 0.0, 0
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        xx, yy = x + dx, y + dy
                        if 0 <= xx < size and 0 <= yy < size:
                            total += src[yy * size + xx]
                            n += 1
                avg = total / n
                i = y * size + x
                b.height.cells[i] = src[i] + (avg - src[i]) * strength


def op_clamp(b: MapBuild, *, min: float = 1, max: float = 20, **_) -> None:  # noqa: A002 - spec vocabulary
    """Round every column to an integer level and hold it inside [min, max]. Run this last."""
    b.clamp_height(int(min), int(max))


OPS: dict[str, Callable[..., None]] = {
    "base": op_base,
    "noise": op_noise,
    "rim": op_rim,
    "hill": op_hill,
    "river": op_river,
    "basin": op_basin,
    "crater": op_crater,
    "channel": op_channel,
    "pad": op_pad,
    "terrace": op_terrace,
    "smooth": op_smooth,
    "clamp": op_clamp,
}


def apply(b: MapBuild, step: dict) -> None:
    step = dict(step)
    name = step.pop("op", None)
    if name is None:
        raise SpecError(f"terrain step without an `op` key: {step!r}")
    if name not in OPS:
        raise SpecError(f"unknown terrain op {name!r}; known: {', '.join(sorted(OPS))}")
    try:
        OPS[name](b, **step)
    except TypeError as exc:
        raise SpecError(f"terrain op {name!r}: {exc}") from exc
