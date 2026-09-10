"""Grids, noise, splines and distance fields: the arithmetic under the terrain ops.

Standard library only, on purpose. A map generator that needs numpy and Pillow cannot run in a
throwaway container or a fresh checkout, and the one thing this tool must always be able to do is
build and check a map. 96 x 96 x 23 voxels is small enough that pure Python is fast enough
(a full build is well under a second).
"""
from __future__ import annotations

import math
import random
from collections.abc import Callable, Iterable, Sequence

Point = tuple[float, float]


class Grid:
    """A square, row-major grid of floats addressed as (x, y) with y running north to south."""

    __slots__ = ("size", "cells")

    def __init__(self, size: int, fill: float = 0.0, cells: list[float] | None = None):
        self.size = size
        self.cells = cells if cells is not None else [fill] * (size * size)

    def __len__(self) -> int:
        return len(self.cells)

    def at(self, x: int, y: int) -> float:
        return self.cells[y * self.size + x]

    def set(self, x: int, y: int, v: float) -> None:
        self.cells[y * self.size + x] = v

    def inside(self, x: int, y: int) -> bool:
        return 0 <= x < self.size and 0 <= y < self.size

    def copy(self) -> Grid:
        return Grid(self.size, cells=list(self.cells))

    def map(self, fn: Callable[[float], float]) -> Grid:
        return Grid(self.size, cells=[fn(v) for v in self.cells])

    def coords(self) -> Iterable[tuple[int, int, float]]:
        s = self.size
        for i, v in enumerate(self.cells):
            yield i % s, i // s, v

    def min(self) -> float:
        return min(self.cells)

    def max(self) -> float:
        return max(self.cells)


class Mask:
    """A square boolean grid. Terrain ops tag the cells they touch so placement can use them."""

    __slots__ = ("size", "cells")

    def __init__(self, size: int, cells: list[bool] | None = None):
        self.size = size
        self.cells = cells if cells is not None else [False] * (size * size)

    def at(self, x: int, y: int) -> bool:
        return self.cells[y * self.size + x]

    def set(self, x: int, y: int, v: bool = True) -> None:
        self.cells[y * self.size + x] = v

    def any(self) -> bool:
        return any(self.cells)

    def count(self) -> int:
        return sum(1 for c in self.cells if c)

    def points(self) -> list[tuple[int, int]]:
        s = self.size
        return [(i % s, i // s) for i, c in enumerate(self.cells) if c]

    def union(self, other: Mask) -> Mask:
        return Mask(self.size, [a or b for a, b in zip(self.cells, other.cells, strict=True)])

    def grow(self, radius: int) -> Mask:
        """Chebyshev dilation — used for keep-out zones around placed things."""
        out = Mask(self.size)
        s = self.size
        for x, y in self.points():
            for dy in range(-radius, radius + 1):
                for dx in range(-radius, radius + 1):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < s and 0 <= yy < s:
                        out.set(xx, yy)
        return out


def smoothstep(t: float) -> float:
    return t * t * (3.0 - 2.0 * t)


def value_noise(rng: random.Random, size: int, octaves: Sequence[int] = (8, 16, 32),
                weights: Sequence[float] = (1.0, 0.5, 0.25)) -> Grid:
    """Sum of smoothstep-interpolated value-noise octaves, normalised to [0, 1].

    `octaves` are lattice resolutions: 8 gives broad hills, 32 gives the roughness on top, and
    `weights` says how much each contributes. The two must line up one to one.
    """
    if len(octaves) != len(weights):
        raise ValueError(f"noise: {len(octaves)} octaves but {len(weights)} weights — "
                         f"give each octave its own weight")
    out = Grid(size, 0.0)
    total = 0.0
    for cells, weight in zip(octaves, weights, strict=True):
        lattice = [[rng.random() for _ in range(cells + 1)] for _ in range(cells + 1)]
        # Precompute the per-axis lattice index and blend factor for every grid line.
        idx, frac = [], []
        for i in range(size):
            u = i * cells / max(size - 1, 1)
            i0 = min(int(math.floor(u)), cells - 1)
            idx.append(i0)
            frac.append(smoothstep(u - i0))
        for y in range(size):
            y0, fy = idx[y], frac[y]
            row0, row1 = lattice[y0], lattice[y0 + 1]
            base = y * size
            for x in range(size):
                x0, fx = idx[x], frac[x]
                top = row0[x0] * (1 - fx) + row0[x0 + 1] * fx
                bot = row1[x0] * (1 - fx) + row1[x0 + 1] * fx
                out.cells[base + x] += weight * (top * (1 - fy) + bot * fy)
        total += weight
    if total:
        out.cells = [v / total for v in out.cells]
    return out


def catmull_rom(points: Sequence[Point], per_segment: int = 40) -> list[Point]:
    """A smooth curve through every control point — river and road centrelines."""
    if len(points) < 2:
        return list(points)
    pts = [points[0]] + list(points) + [points[-1]]
    out: list[Point] = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for k in range(per_segment):
            t = k / per_segment
            t2, t3 = t * t, t * t * t
            out.append((
                0.5 * (2 * p1[0] + (-p0[0] + p2[0]) * t
                       + (2 * p0[0] - 5 * p1[0] + 4 * p2[0] - p3[0]) * t2
                       + (-p0[0] + 3 * p1[0] - 3 * p2[0] + p3[0]) * t3),
                0.5 * (2 * p1[1] + (-p0[1] + p2[1]) * t
                       + (2 * p0[1] - 5 * p1[1] + 4 * p2[1] - p3[1]) * t2
                       + (-p0[1] + 3 * p1[1] - 3 * p2[1] + p3[1]) * t3),
            ))
    out.append(tuple(points[-1]))  # type: ignore[arg-type]
    return out


def distance_field(size: int, points: Sequence[Point], max_dist: float = 24.0) -> Grid:
    """Distance from each cell centre to the nearest of `points`, capped at `max_dist`.

    The cap keeps this linear in the number of points: only the window that could beat the cap is
    visited. Everything the ops need (valley falloff, contamination bands, keep-out radii) is well
    inside 24 tiles.
    """
    out = Grid(size, max_dist)
    reach = int(math.ceil(max_dist))
    for px, py in points:
        cx, cy = int(px), int(py)
        for y in range(max(0, cy - reach), min(size, cy + reach + 1)):
            base = y * size
            dy = (y + 0.5) - py
            dy2 = dy * dy
            for x in range(max(0, cx - reach), min(size, cx + reach + 1)):
                dx = (x + 0.5) - px
                d = math.sqrt(dx * dx + dy2)
                if d < out.cells[base + x]:
                    out.cells[base + x] = d
    return out
