"""Copy the Leaf Coats family (and the code it depends on) into the Wardens mod. LOCAL ONLY.

    python wardens/tools/import_leafcoats.py            # copy
    python wardens/tools/import_leafcoats.py --remove   # delete everything a previous run copied

Sources (Steam Workshop, latest version folder of each):
  3552203634 Leaf Coats            blueprints, sprites, materials, AssetBundles (win only), localization
  3556403458 Leaf Coats: Badwater   TemplateCollections
  3556403578 Leaf Coats: Explosives TemplateCollections
  3621479902 Leaf Coats: Beaver Plants  bundle, collection, settings, Scripts/*.dll, localization
  3416879061 Bobingabout Script Pack    Scripts/*.dll + its content folders (the code Leaf Coats needs)
  3631491885 Vertical Nav Mesh          Scripts/*.dll, AssetBundles, Buildings, Configurations
  (Id "Harmony")  Harmony                 0Harmony.dll only (Vertical Nav Mesh is a Harmony patch)

Why this layout works (verified in the decompiled loaders): the game reads every file below the
mod root with its relative path, loads every *.dll it finds in any subfolder, loads bundles from
AssetBundles/*_win, and merges every Localizations/<lang>_<anything>.csv into <lang>.

Everything copied is recorded in wardens/src/.leafcoats-import.txt and ignored by git through
wardens/src/.gitignore, which this script rewrites. The content belongs to Bobingabout; it stays on
this machine. When the Wardens mod carries these copies, DISABLE the seven workshop mods (Harmony too) in the
mod manager, or the game loads two copies of the same assemblies and blueprints.
"""
from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

WORKSHOP = Path("F:/Steam/steamapps/workshop/content/1062090")
SRC = Path(__file__).resolve().parents[1] / "src"
RECORD = SRC / ".leafcoats-import.txt"
GITIGNORE = SRC / ".gitignore"

MODS = {
    "3552203634": "LeafCoats",
    "3556403458": "LeafCoatsBadwater",
    "3556403578": "LeafCoatsExplosives",
    "3621479902": "LeafCoatsBeaverPlants",
    "3416879061": "BobingaboutScriptPack",
    "3631491885": "VerticalNavMesh",
}
SKIP_NAMES = {"manifest.json", "Readme.txt", "workshop_data.json", "thumbnail.jpg", "thumbnail.png"}
# Whole top-level folders that turned out to be either unused by anything we ship (other
# factions' CharacterCustomizer entries, Bobingabout's own named-beaver tributes, LeafCoats-only
# worker outfits/decals/good visualizations) or a duplicate-key risk once loaded: RecipeSpecService
# and (confirmed separately) the tool-group loader both throw "An item with the same key has
# already been added" when a loose file registers an Id that AssetBundles/leafcoats_win *or*
# vanilla already provides under a different path, and that abort takes every singleton scheduled
# after it down with it (that is how one duplicate recipe took down the whole tutorial system).
# Recipes/Needs/Goods/Factions are NOT skipped wholesale: we still author our own files there
# (DataCore, Firmware, Need.Bot.Lubricant, Faction.Wardens, ...), so those use SKIP_PATHS instead.
SKIP_FOLDERS = {
    "CharacterCustomizer", "CustomBeaverByName", "CustomBeaverNamesList", "CustomBotByName",
    "Decals", "GoodsVisualization", "MaterialPatcher", "WorkerOutfits",
}
SKIP_PATHS = {
    # Registers "Leaf Coats" as a second playable faction; not the point of this import
    # (design/leafcoats-port-plan.md rule 1: read the bundle's assets, don't ship its faction).
    "Factions/Faction.LeafCoats.blueprint.json",
    # Inject LeafCoats-specific goods/needs/path behaviour into vanilla Folktails/IronTeeth —
    # unwanted outside a LeafCoats game.
    "Factions/GoodCollection.Common.LeafCoatsModifier.blueprint.json",
    "Factions/NeedCollection.Common.LeafCoatsModifier.blueprint.json",
    "Factions/Path.LeafCoatsModifier.blueprint.json",
    # Vanilla already has a "Landscaping" BlockObjectToolGroupSpec; this loose one duplicates it.
    # The other 5 groups here (Dams/Industry/Platforms/TreeBuildings/Zipline) are not vanilla
    # names and several are genuinely referenced by ported buildings — keep those.
    "BlockObjectToolGroups/BlockObjectToolGroup.Landscaping.optional.blueprint.json",
    # Collections that only exist to serve the (now-dropped) LeafCoats faction and its add-ons.
    "Collections/GoodCollection.LeafCoats.LeafCoatsExplosives.blueprint.json",
    "Collections/GoodCollection.LeafCoats.blueprint.json",
    "Collections/GoodCollection.LeafCoatsAddons.blueprint.json",
    "Collections/MaterialCollection.LeafCoats.blueprint.json",
    "Collections/NeedCollection.Folktails.LeafCoatsModifier.blueprint.json",
    "Collections/NeedCollection.IronTeeth.LeafCoatsModifier.blueprint.json",
    "Collections/NeedCollection.LeafCoats.blueprint.json",
    "Collections/NeedCollection.LeafCoatsAddons.blueprint.json",
    "Collections/TemplateCollection.Buildings.LeafCoats.LeafCoatsBadWater.blueprint.json",
    "Collections/TemplateCollection.Buildings.LeafCoats.LeafCoatsExplosives.blueprint.json",
    "Collections/TemplateCollection.Buildings.LeafCoats.blueprint.json",
    "Collections/TemplateCollection.Characters.LeafCoats.blueprint.json",
    "Collections/TemplateCollection.ModularShaftParts.LeaCoats.blueprint.json",
    "Collections/TemplateCollection.NaturalResources.LeafCoats.blueprint.json",
    "TemplateCollection.NaturalResources.LeafCoats.BeaverPlants.blueprint.json",
    # Food-chain / Act-III-only goods and needs: bots don't eat, and every one of these turned out
    # to duplicate a bundle-internal Id the moment it was actually loaded, same failure mode as
    # the recipes below.
    "Goods/Good.Apple.blueprint.json",
    "Goods/Good.Bark.blueprint.json",
    "Goods/Good.Branch.blueprint.json",
    "Goods/Good.FancyApples.blueprint.json",
    "Goods/Good.FermentedChestnut.blueprint.json",
    "Goods/Good.FermentedDandelion.blueprint.json",
    "Goods/Good.FermentedFruit.blueprint.json",
    "Goods/Good.FruitSalad.blueprint.json",
    "Goods/Good.Grapes.blueprint.json",
    "Goods/Good.Leaf.blueprint.json",
    "Goods/Good.Lubricant.blueprint.json",
    "Goods/Good.Shovel.blueprint.json",
    "Needs/Need.Beaver.Apples.blueprint.json",
    "Needs/Need.Beaver.FancyApples.blueprint.json",
    "Needs/Need.Beaver.FermentedChestnut.blueprint.json",
    "Needs/Need.Beaver.FermentedDandelion.blueprint.json",
    "Needs/Need.Beaver.FermentedFruit.blueprint.json",
    "Needs/Need.Beaver.FruitSalad.blueprint.json",
    "Needs/Need.Beaver.Garden.blueprint.json",
    "Needs/Need.Beaver.Grapes.blueprint.json",
    "Needs/Need.Beaver.Greenery.blueprint.json",
    "Needs/Need.Beaver.ObservationTerrace.blueprint.json",
    "Needs/Need.Beaver.PlantMurderer.blueprint.json",
    "Needs/Need.Beaver.Plaza.blueprint.json",
    "Needs/Need.Beaver.WonderLeafCoats.blueprint.json",
    # Confirmed RecipeSpecService duplicate-key crashes (Antidote, then Log.Press) generalized to
    # every other loose *.LeafCoats recipe: buildings that reference them (Mine, Shredder,
    # Refinery, LargeWaterPump.Wardens, ...) still resolve fine off the bundle's own copy.
    "Recipes/Recipe.Antidote.LeafCoats.blueprint.json",
    "Recipes/Recipe.Extract.Extracted.blueprint.json",
    "Recipes/Recipe.FancyApples.blueprint.json",
    "Recipes/Recipe.FermentedChestnut.blueprint.json",
    "Recipes/Recipe.FermentedDandelion.blueprint.json",
    "Recipes/Recipe.FermentedFruit.blueprint.json",
    "Recipes/Recipe.FruitSalad.blueprint.json",
    "Recipes/Recipe.Log.Press.LeafCoats.blueprint.json",
    "Recipes/Recipe.Lubricant.LeafCoats.blueprint.json",
    "Recipes/Recipe.MetalBlock.LeafCoats.blueprint.json",
    "Recipes/Recipe.Plank.Press.LeafCoats.blueprint.json",
    "Recipes/Recipe.ScrapMetal.Efficient.LeafCoats.blueprint.json",
    "Recipes/Recipe.Shovel.LeafCoats.blueprint.json",
    "Recipes/Recipe.Water.LeafCoats.Large.blueprint.json",
}


def latest_version(mod_dir: Path) -> Path:
    versions = sorted(mod_dir.glob("version-*"), key=lambda p: [int(x) for x in p.name.split("-")[1].split(".")])
    if not versions:
        sys.exit(f"no version-* folder in {mod_dir}")
    return versions[-1]


def destination(mod_id: str, rel: Path) -> Path | None:
    """Map a source file to its place in wardens/src, or None to skip."""
    if rel.name in SKIP_NAMES or rel.as_posix() in SKIP_PATHS:
        return None
    parts = rel.parts
    if parts[0] in SKIP_FOLDERS:
        return None
    if parts[0] == "AssetBundles":
        if rel.name.endswith("_mac") or rel.name.endswith("_mac.manifest"):
            return None                                   # Windows machine, win bundles only
        return SRC / rel
    if parts[0] == "Localizations":
        # enUS.csv -> enUS_LeafCoats.csv: the loader merges every <lang>_*.csv into <lang>.
        stem, ext = rel.stem, rel.suffix
        lang = stem.split("_")[0]
        tag = MODS[mod_id] + ("" if stem == lang else "_" + stem[len(lang) + 1:])
        return SRC / "Localizations" / f"{lang}_{tag}{ext}"
    if parts[0] == "TemplateCollections":                 # add-ons keep collections in a different folder name
        return SRC / "Collections" / Path(*parts[1:])
    if parts[0] == "Scripts":
        return SRC / "Scripts" / MODS[mod_id] / Path(*parts[1:])
    return SRC / rel


def find_harmony() -> Path | None:
    """The Harmony workshop mod (manifest Id "Harmony"): Vertical Nav Mesh is a Harmony patch and
    needs 0Harmony.dll next to it. Picks the highest version if several are installed."""
    import json, re
    best: tuple[list[int], Path] | None = None
    for manifest in WORKSHOP.glob("*/manifest.json"):
        try:
            raw = re.sub(r",(\s*[}\]])", lambda m: m.group(1), manifest.read_text(encoding="utf-8-sig"))  # trailing commas out, bracket kept
            d = json.loads(raw)
        except Exception:
            continue
        if d.get("Id") != "Harmony":
            continue
        ver = [int(x) for x in re.findall(r"\d+", str(d.get("Version", "0")))]
        dll = next(manifest.parent.rglob("0Harmony.dll"), None)
        if dll and (best is None or ver > best[0]):
            best = (ver, dll)
    return best[1] if best else None


def copy_all() -> list[Path]:
    copied: list[Path] = []
    harmony = find_harmony()
    if harmony is None:
        sys.exit("Harmony workshop mod not found; Vertical Nav Mesh cannot load without 0Harmony.dll")
    dst = SRC / "Scripts" / "Harmony" / harmony.name
    dst.parent.mkdir(parents=True, exist_ok=True)
    if not (dst.exists() and dst.stat().st_size == harmony.stat().st_size):
        shutil.copyfile(harmony, dst)
    copied.append(dst)
    print(f"{'Harmony':24s} {harmony.parent.name:12s}    1 files ({harmony.stat().st_size // 1024} KB)")
    for mod_id, name in MODS.items():
        root = latest_version(WORKSHOP / mod_id)
        n = 0
        for src in root.rglob("*"):
            if not src.is_file():
                continue
            dst = destination(mod_id, src.relative_to(root))
            if dst is None:
                continue
            if dst.exists() and dst.stat().st_size == src.stat().st_size:
                copied.append(dst); n += 1
                continue
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dst)
            copied.append(dst); n += 1
        print(f"{name:24s} {root.name:12s} {n:4d} files")
    return copied


def write_records(copied: list[Path]) -> None:
    rels = sorted({p.relative_to(SRC).as_posix() for p in copied})
    RECORD.write_text("\n".join(rels) + "\n", encoding="utf-8")
    lines = ["# generated by tools/import_leafcoats.py: local-only copies of Bobingabout's mods", ".leafcoats-import.txt"]
    lines += ["/" + r for r in rels]
    GITIGNORE.write_text("\n".join(lines) + "\n", encoding="utf-8")


def remove_all() -> None:
    if not RECORD.exists():
        print("nothing recorded"); return
    n = 0
    for line in RECORD.read_text(encoding="utf-8").splitlines():
        p = SRC / line.strip()
        if p.exists():
            p.unlink(); n += 1
    for d in sorted({p.parent for p in [SRC / l for l in RECORD.read_text(encoding="utf-8").splitlines()]}, key=lambda x: -len(x.parts)):
        try:
            if d != SRC and d.exists() and not any(d.iterdir()):
                d.rmdir()
        except OSError:
            pass
    RECORD.unlink()
    if GITIGNORE.exists():
        GITIGNORE.unlink()
    print(f"removed {n} files")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--remove", action="store_true")
    args = ap.parse_args()
    if args.remove:
        remove_all(); return 0
    copied = copy_all()
    write_records(copied)
    total = sum(p.stat().st_size for p in copied)
    print(f"total {len(copied)} files, {total / 1e6:.0f} MB, recorded in {RECORD.name}; git-ignored via {GITIGNORE.relative_to(SRC.parent)}")
    print("Disable the workshop copies (Leaf Coats + 3 add-ons, Bobingabout Script Pack, Vertical Nav Mesh, Harmony) before starting the game.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
