"""Generate the Wardens' Chapter 1 buildings, crop and goods from the vanilla blueprint dump.

    python wardens/tools/gen_buildings.py

Reads the game's own Blueprints.zip (Timberborn_Data/StreamingAssets/Modding), copies the
Iron Teeth blueprints we re-spec, mutates the few specs that make them Wardens buildings, and
writes them under wardens/src. Models, colliders, attachments, transputs: untouched vanilla
references (design/model-catalog.md, technique "re-spec"). Idempotent; re-run after editing.

Produces
  Buildings/DistrictManagement/Core/Core.Wardens             district center + 150 power
  Buildings/Science/ChargingPost/ChargingPost.Wardens        charging station, scrap-priced, free
  Buildings/Science/Cruncher/Cruncher.Wardens                numbercruncher @120 power, science or Data Cores
  Buildings/Power/SludgeBurner/SludgeBurner.Wardens          steam engine burning Biomass, 200 power, pollutes
  Buildings/Power/PowerShaft/PowerShaft.Wardens              power shaft, scrap-priced
  Buildings/Storage/ScrapPile/ScrapPile.Wardens              small industrial pile, scrap-priced
  Buildings/Food/ReedBed/ReedBed.Wardens                     farmhouse, scrap-priced (Chapter 2 building)
  Buildings/Water/SludgePump, Power/BadwaterCell, Storage/SludgeTank, Housing/*Pod, Wood/Planter
  Buildings/Paths/Stairs (alias Stairs.Folktails), Paths/Platform, Landscaping/Dam, Storage/Rack,
  DistrictManagement/HaulingPost, Wellbeing/Deck                the ported tutorial's building set
  Buildings/DistrictManagement/MapGate/MapGate.Wardens         the Gate: a district on another map, linked in
  NaturalResources/Crops/SludgeReed/SludgeReed                cattail re-spec: land crop, ignores contamination
  Goods/Good.Biomass, collections, faction wiring, recipes, loc rows, csproj glob
Chapter gating: buildings that src/WardensChapters.cs unlocks ship with ScienceCost CHAPTER_LOCK
(vanilla's padlock) instead of 0; tools/validate.py checks both tables agree.
Tutorials: tools/gen_tutorial.py, run after this script.
"""
from __future__ import annotations

import copy
import csv
import json
import os
import zipfile
from pathlib import Path

GAME_BLUEPRINTS = Path("F:/Steam/steamapps/common/Timberborn/Timberborn_Data/StreamingAssets/Modding/Blueprints.zip")
SRC = Path(__file__).resolve().parents[1] / "src"
FACTION = "Wardens"
# Story gating (src/WardensChapters.cs): a building the chapter service unlocks ships with this
# ScienceCost so the bar shows vanilla's padlock until its chapter opens. Never buildable by science.
CHAPTER_LOCK = 999999

_zip = zipfile.ZipFile(GAME_BLUEPRINTS)


def vanilla(path: str) -> dict:
    return json.loads(_zip.read(path + ".blueprint.json").decode("utf-8-sig"))


def write(rel: str, obj: dict) -> None:
    p = SRC / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, indent=2)
        f.write("\n")
    print("wrote", rel)


def labeled(d: dict, key: str, keep_icon: bool = True) -> None:
    icon = d["LabeledEntitySpec"].get("Icon", "") if keep_icon else ""
    d["LabeledEntitySpec"] = {
        "DisplayNameLocKey": f"Building.{key}.DisplayName",
        "DescriptionLocKey": f"Building.{key}.Description",
        "FlavorDescriptionLocKey": f"Building.{key}.FlavorDescription",
        "Icon": icon,
    }


def rename(d: dict, template: str) -> None:
    d["TemplateSpec"]["TemplateName"] = template
    d["TemplateSpec"]["BackwardCompatibleTemplateNames"] = []


def cost(d: dict, *pairs: tuple[str, int], science: int = 0) -> None:
    d["BuildingSpec"]["BuildingCost"] = [{"Id": g, "Amount": n} for g, n in pairs]
    d["BuildingSpec"]["ScienceCost"] = science


buildings: list[str] = []   # blueprint paths (no .json) for the template collection


def building(rel_dir: str, name: str, d: dict) -> None:
    rel = f"Buildings/{rel_dir}/{name}.{FACTION}.blueprint.json"
    write(rel, d)
    buildings.append(rel[:-5])


# --- The Core: district center that is also the faction's reactor -------------------------
core = vanilla("Buildings/DistrictManagement/DistrictCenter/DistrictCenter.IronTeeth")
rename(core, "Core.Wardens")
labeled(core, "Core")
core["MechanicalNodeSpec"] = {"PowerOutput": 150, "PowerInput": 0, "IsShaft": False}
core["MechanicalBuildingSpec"] = {}
core["MechanicalNodeIlluminatorSpec"] = {}
building("DistrictManagement/Core", "Core", core)

# --- Charging Post: the Iron Teeth charging station at scrap prices ----------------------------
post = vanilla("Buildings/Science/ChargingStation/ChargingStation.IronTeeth")
rename(post, "ChargingPost.Wardens")
labeled(post, "ChargingPost")
cost(post, ("ScrapMetal", 5))
post["PlaceableBlockObjectSpec"]["ToolOrder"] = 20
building("Science/ChargingPost", "ChargingPost", post)

# --- The Cruncher: research that burns power ----------------------------------------------------
cruncher = vanilla("Buildings/Science/Numbercruncher/Numbercruncher.IronTeeth")
rename(cruncher, "Cruncher.Wardens")
labeled(cruncher, "Cruncher")
cost(cruncher, ("ScrapMetal", 20), science=CHAPTER_LOCK)   # chapter Signal
cruncher["MechanicalNodeSpec"] = {"PowerOutput": 0, "PowerInput": 120, "IsShaft": False}
cruncher["ManufactorySpec"] = {"ProductionRecipeIds": ["SciencePointsNumbercruncher", "DataCore"]}
cruncher["PlaceableBlockObjectSpec"]["ToolOrder"] = 10
building("Science/Cruncher", "Cruncher", cruncher)

# --- The Sludge Burner: steam engine on Biomass, no water, first polluter -------------------------
burner = vanilla("Buildings/Power/SteamEngine/SteamEngine.IronTeeth")
rename(burner, "SludgeBurner.Wardens")
labeled(burner, "SludgeBurner")
cost(burner, ("ScrapMetal", 15), science=CHAPTER_LOCK)   # chapter Power
burner["MechanicalNodeSpec"] = {"PowerOutput": 200, "PowerInput": 0, "IsShaft": False}
burner["GoodConsumingBuildingSpec"] = {"FullInventoryWorkHours": 50, "ConsumedGoods": [{"GoodId": "Biomass", "GoodPerHour": 0.2}]}
burner["PollutingBuildingSpec"] = {"Radius": 2, "Strength": 0.6}
burner["PlaceableBlockObjectSpec"]["ToolOrder"] = 40
building("Power/SludgeBurner", "SludgeBurner", burner)

# --- Scrap-priced vanilla essentials ---------------------------------------------------------------
shaft = vanilla("Buildings/Power/PowerShaft/PowerShaft.IronTeeth")
rename(shaft, "PowerShaft.Wardens")
cost(shaft, ("ScrapMetal", 1))
building("Power/PowerShaft", "PowerShaft", shaft)

pile = vanilla("Buildings/Storage/SmallIndustrialPile/SmallIndustrialPile.IronTeeth")
rename(pile, "ScrapPile.Wardens")
cost(pile, ("ScrapMetal", 4))
building("Storage/ScrapPile", "ScrapPile", pile)

bed = vanilla("Buildings/Food/FarmHouse/FarmHouse.IronTeeth")
rename(bed, "ReedBed.Wardens")
labeled(bed, "ReedBed")
cost(bed, ("ScrapMetal", 20), science=CHAPTER_LOCK)   # chapter Badwater
building("Food/ReedBed", "ReedBed", bed)

# --- Badwater loop: the Wardens run on badwater ---------------------------------------------------------
# Sludge Pump: the Iron Teeth deep badwater pump, open from chapter Badwater, bots may work it for free.
pump = vanilla("Buildings/Water/DeepBadwaterPump/DeepBadwaterPump.IronTeeth")
rename(pump, "SludgePump.Wardens")
labeled(pump, "SludgePump")
cost(pump, ("ScrapMetal", 12), science=CHAPTER_LOCK)   # chapter Badwater
pump["WorkplaceSpec"]["DefaultWorkerType"] = "Bot"
pump["WorkplaceSpec"]["WorkerTypeUnlockCosts"] = []
pump["PlaceableBlockObjectSpec"]["ToolOrder"] = 10
building("Water/SludgePump", "SludgePump", pump)

# Badwater Cell: early generator that burns Badwater. Same body as the Sludge Burner for now
# (design/wardens-art-path.md covers how to give it its own look), cyan light instead of orange.
cell = vanilla("Buildings/Power/SteamEngine/SteamEngine.IronTeeth")
rename(cell, "BadwaterCell.Wardens")
labeled(cell, "BadwaterCell")
cost(cell, ("ScrapMetal", 8), science=CHAPTER_LOCK)   # chapter Power
cell["MechanicalNodeSpec"] = {"PowerOutput": 100, "PowerInput": 0, "IsShaft": False}
cell["GoodConsumingBuildingSpec"] = {"FullInventoryWorkHours": 40, "ConsumedGoods": [{"GoodId": "Badwater", "GoodPerHour": 0.4}]}
cell["PollutingBuildingSpec"] = {"Radius": 1, "Strength": 0.3}
cell["DefaultIlluminatorColorSpec"] = {"ColorId": "WardensCyan"}
cell["PlaceableBlockObjectSpec"]["ToolOrder"] = 30
building("Power/BadwaterCell", "BadwaterCell", cell)

tank = vanilla("Buildings/Storage/SmallTank/SmallTank.IronTeeth")
rename(tank, "SludgeTank.Wardens")
cost(tank, ("ScrapMetal", 6), science=CHAPTER_LOCK)   # chapter Badwater
building("Storage/SludgeTank", "SludgeTank", tank)

# The faction's light: cyan illuminators on the Core and the Cruncher too.
core["DefaultIlluminatorColorSpec"] = {"ColorId": "WardensCyan"}
cruncher["DefaultIlluminatorColorSpec"] = {"ColorId": "WardensCyan"}
write("Buildings/DistrictManagement/Core/Core.Wardens.blueprint.json", core)
write("Buildings/Science/Cruncher/Cruncher.Wardens.blueprint.json", cruncher)
write("IlluminationColors/IlluminationColor.WardensCyan.blueprint.json", {
    "IlluminationColorSpec": {"Id": "WardensCyan", "Color": {"r": 0.25, "g": 0.85, "b": 1.0, "a": 1.0}},
    "IlluminationPresetSpec": {"Order": 110},
})

# --- Breeding pods: Biomass + power instead of berries and water ---------------------------------
def powered(d: dict, power_input: int) -> None:
    """Give a non-mechanical vanilla building a power input: node, building, connector target and
    one transput per ground tile (all four directions) so shafts can attach on any side."""
    size = d["BlockObjectSpec"]["Size"]
    d["MechanicalNodeSpec"] = {"PowerOutput": 0, "PowerInput": power_input, "IsShaft": False}
    d["MechanicalBuildingSpec"] = {}
    d["MechanicalNodeIlluminatorSpec"] = {}
    d["MechanicalConnectorTargetSpec"] = {}
    d["TransputProviderSpec"] = {"Transputs": [
        {"Coordinates": {"X": x, "Y": y, "Z": 0}, "Directions": "Left, Up, Right, Bottom", "ReverseRotation": False}
        for x in range(size["X"]) for y in range(size["Y"])], "IgnoreRotation": False}


pod = vanilla("Buildings/Housing/BreedingPod/BreedingPod.IronTeeth")
rename(pod, "BreedingPod.Wardens")
labeled(pod, "WardensPod")
cost(pod, ("ScrapMetal", 10), science=CHAPTER_LOCK)   # chapter Pods
pod["BreedingPodSpec"]["NutrientsPerCycle"] = [{"Id": "Biomass", "Amount": 1}]
powered(pod, 100)
building("Housing/BreedingPod", "BreedingPod", pod)

apod = vanilla("Buildings/Housing/AdvancedBreedingPod/AdvancedBreedingPod.IronTeeth")
rename(apod, "AdvancedBreedingPod.Wardens")
labeled(apod, "WardensAdvancedPod")
cost(apod, ("ScrapMetal", 20), ("DataCore", 5), science=CHAPTER_LOCK)   # chapter Green
apod["BreedingPodSpec"]["NutrientsPerCycle"] = [{"Id": "Biomass", "Amount": 1}, {"Id": "Firmware", "Amount": 1}]
powered(apod, 150)
building("Housing/AdvancedBreedingPod", "AdvancedBreedingPod", apod)

# --- Planter Rig: the game demands exactly one Forester-group building per faction ------------------
# PlantingToolButtonFactory.GetPlanterBuildingName does .Single() over PlanterBuildingSpec for every
# plantable the faction can see; the common trees/bushes are "Forester" plantables, so the bar must
# carry one (and only one) Forester-group building even before the story plants trees.
planter = vanilla("Buildings/Wood/Forester/Forester.IronTeeth")
rename(planter, "Planter.Wardens")
labeled(planter, "Planter")
cost(planter, ("ScrapMetal", 15), science=60)   # locked at start: the Reforestation tutorial unlocks it
planter["WorkplaceSpec"]["DefaultWorkerType"] = "Bot"
planter["WorkplaceSpec"]["WorkerTypeUnlockCosts"] = []
building("Wood/Planter", "Planter", planter)

# --- The tutorial's building set: Iron Teeth bodies at scrap prices -------------------------------------
# Stairs carry "Stairs.Folktails" as an alias: vanilla's StairsUnlockedTrigger (Timberborn.TutorialSteps)
# resolves that name at load for every faction with a StartingFactionSpec and throws if it is missing.
# With the alias the trigger fires when the Wardens' stairs are unlocked, which is what it is for.
stairs = vanilla("Buildings/Paths/Stairs/Stairs.IronTeeth")
rename(stairs, "Stairs.Wardens")
stairs["TemplateSpec"]["BackwardCompatibleTemplateNames"] = ["Stairs.Folktails"]
cost(stairs, ("ScrapMetal", 3), science=70)
building("Paths/Stairs", "Stairs", stairs)

platform = vanilla("Buildings/Paths/Platform/Platform.IronTeeth")
rename(platform, "Platform.Wardens")
cost(platform, ("ScrapMetal", 4), science=100)
building("Paths/Platform", "Platform", platform)

dam = vanilla("Buildings/Landscaping/Dam/Dam.IronTeeth")
rename(dam, "Dam.Wardens")
cost(dam, ("ScrapMetal", 8))
building("Landscaping/Dam", "Dam", dam)

rack = vanilla("Buildings/Storage/SmallWarehouse/SmallWarehouse.IronTeeth")
rename(rack, "Rack.Wardens")
labeled(rack, "Rack")
cost(rack, ("ScrapMetal", 4), science=CHAPTER_LOCK)   # chapter Badwater
building("Storage/Rack", "Rack", rack)

dock = vanilla("Buildings/DistrictManagement/HaulingPost/HaulingPost.IronTeeth")
rename(dock, "HaulingPost.Wardens")
labeled(dock, "HaulerDock")
cost(dock, ("ScrapMetal", 15))
dock["WorkplaceSpec"]["DefaultWorkerType"] = "Bot"
dock["WorkplaceSpec"]["WorkerTypeUnlockCosts"] = []
building("DistrictManagement/HaulingPost", "HaulingPost", dock)

deck = vanilla("Buildings/Wellbeing/RooftopTerrace/RooftopTerrace.IronTeeth")
rename(deck, "Deck.Wardens")
labeled(deck, "Deck")
cost(deck, ("ScrapMetal", 10))
building("Wellbeing/Deck", "Deck", deck)

# --- Gate: a district on another map, linked in (src/WardensGate.cs) ----------------------------------
# The Large Industrial Pile's 3x3 pad, entrance and access, with its stockpile taken out: goods from
# the linked district arrive in a public output inventory (vanilla SimpleOutputInventorySpec, the one
# flags and the district center use) and the Hauling Post's haulers carry them in. IgnorableCapacity:
# a source, not storage, so it does not count toward the district's capacity.
gate = vanilla("Buildings/Storage/LargeIndustrialPile/LargeIndustrialPile.IronTeeth")
rename(gate, "MapGate.Wardens")
labeled(gate, "MapGate")
for spec in ("StockpileSpec", "StockpileGoodPileVisualizerSpec", "StockpilePlaneVisualizerSpec"):
    gate.pop(spec, None)
gate["SimpleOutputInventorySpec"] = {"Capacity": 30, "IgnorableCapacity": True}
gate["WardensMapGateSpec"] = {}
gate["PlaceableBlockObjectSpec"]["ToolGroupId"] = "DistrictManagement"
gate["PlaceableBlockObjectSpec"]["ToolOrder"] = 50
gate["BuildingSpec"]["SelectionSoundName"] = "Pile"
cost(gate, ("ScrapMetal", 30))
building("DistrictManagement/MapGate", "MapGate", gate)

# --- Sludge Reed: cattail models on a land-crop body, immune to contamination and drought ------------
reed = vanilla("NaturalResources/Crops/Cattail/Cattail")
land = vanilla("NaturalResources/Crops/Kohlrabi/Kohlrabi")
reed["BlockObjectSpec"] = land["BlockObjectSpec"]
rename(reed, "SludgeReed")
reed["NaturalResourceSpec"] = {"Order": 30}
reed["GrowableSpec"] = {"GrowthTimeInDays": 4.0}
reed["PlantableSpec"] = {"ResourceGroup": "Farmhouse", "PlantTimeInHours": 0.2}
reed["CuttableSpec"]["Yielder"]["Yield"] = {"Id": "Biomass", "Amount": 3}
reed["CuttableSpec"]["Yielder"]["ResourceGroup"] = "Farmhouse"
reed["FloodableNaturalResourceSpec"] = {"MinWaterHeight": 0, "MaxWaterHeight": 0, "DaysToDie": 2.0}
reed.pop("ContaminatedNaturalResourceSpec", None)   # grows on poisoned soil
reed.pop("WateredNaturalResourceSpec", None)        # does not dry out
reed["LabeledEntitySpec"] = {
    "DisplayNameLocKey": "NaturalResource.SludgeReed.DisplayName",
    "DescriptionLocKey": "",
    "FlavorDescriptionLocKey": "NaturalResource.SludgeReed.FlavorDescription",
    "Icon": reed["LabeledEntitySpec"]["Icon"],
}
write("NaturalResources/Crops/SludgeReed/SludgeReed.blueprint.json", reed)

# --- Biomass ----------------------------------------------------------------------------------
write("Goods/Good.Biomass.blueprint.json", {"GoodSpec": {
    "Id": "Biomass", "BackwardCompatibleIds": [], "ConsumptionEffects": [],
    "GoodType": "Box", "StockpileVisualization": "Box", "VisibleContainer": "Box",
    "ContainerColor": {"r": 0.12, "g": 0.42, "b": 0.38, "a": 1.0}, "ContainerMaterial": "",
    "CarryingAnimation": "CarryInHands", "Weight": 2, "GoodGroupId": "Materials", "GoodOrder": 3500,
    "Icon": "Sprites/Goods/CattailRootIcon", "ForceImport": False,
    "DisplayNameLocKey": "Good.Biomass.DisplayName", "PluralDisplayNameLocKey": "Good.Biomass.PluralDisplayName",
}})

# --- Collections --------------------------------------------------------------------------------
write("Collections/TemplateCollection.Buildings.Wardens.blueprint.json", {"TemplateCollectionSpec": {
    "CollectionId": "Buildings.Wardens",
    "Blueprints": buildings + ["Buildings/Metal/ScavengerFlag/ScavengerFlag.IronTeeth.blueprint"],
}})
write("Collections/TemplateCollection.NaturalResources.Wardens.blueprint.json", {"TemplateCollectionSpec": {
    "CollectionId": "NaturalResources.Wardens",
    "Blueprints": ["NaturalResources/Crops/SludgeReed/SludgeReed.blueprint"],
}})

gc_path = SRC / "Collections/GoodCollection.Wardens.blueprint.json"
gc = json.load(open(gc_path, encoding="utf-8"))
goods = gc["GoodCollectionSpec"]["Goods"]
if "Biomass" not in goods:
    goods.append("Biomass")
write("Collections/GoodCollection.Wardens.blueprint.json", gc)

# --- Faction wiring -------------------------------------------------------------------------------
fac_path = SRC / "Factions/Faction.Wardens.blueprint.json"
fac = json.load(open(fac_path, encoding="utf-8"))
f = fac["FactionSpec"]
# No NaturalResources.IronTeeth: bots do not eat, and every plantable the faction can see needs
# exactly one planter building in its bar (see Planter.Wardens).
f["TemplateCollectionIds"] = ["Buildings.Wardens", "Characters.IronTeeth", "ModularShaftParts.IronTeeth",
                             "NaturalResources.Wardens", "Planes.IronTeeth"]
# Borrowed Folktails models (the Sludge Reed is vanilla Cattail; the Badwater Rig's underground part is
# the Folktails one) look their materials up by name in the faction's collections, and without
# Folktails the Reed's prefab throws "Material Cattail not found in repository". The three vanilla
# collections have disjoint material names, so loading Folktails adds and never shadows.
f["MaterialCollectionIds"] = ["IronTeeth", "Folktails"]
f["StartingBuildingId"] = "Core.Wardens"
f["BlueprintModifiers"] = [m for m in f["BlueprintModifiers"] if m["Original"].startswith("tutorials/")]
write("Factions/Faction.Wardens.blueprint.json", fac)
for stale in ("Factions/Numbercruncher.WardensModifier.blueprint.json", "Factions/BotPartFactory.WardensModifier.blueprint.json"):
    if (SRC / stale).exists():
        os.remove(SRC / stale)
        print("removed", stale)

# --- Recipes: scrap economy, no MetalPart before the Smelter exists ------------------------------------
for name, ingredients in (("DataCore", [("ScrapMetal", 1), ("Badwater", 5)]), ("Firmware", [("DataCore", 2), ("ScrapMetal", 1)])):
    p = SRC / f"Recipes/Recipe.{name}.blueprint.json"
    r = json.load(open(p, encoding="utf-8"))
    r["RecipeSpec"]["Ingredients"] = [{"Id": g, "Amount": n} for g, n in ingredients]
    write(f"Recipes/Recipe.{name}.blueprint.json", r)

# Tutorials and their stages: tools/gen_tutorial.py (run it after this script).

# --- Localization rows ----------------------------------------------------------------------------------
rows = [
    ("Building.Core.DisplayName", "The Core", "Wardens district center"),
    ("Building.Core.Description", "District center and reactor. Every Warden reports here, and the Core's 150 hp of power runs the first machines.", ""),
    ("Building.Core.FlavorDescription", "It was never meant to run this long. It has not stopped.", ""),
    ("Building.ChargingPost.DisplayName", "Charging Post", "Wardens charging station"),
    ("Building.ChargingPost.Description", "A Warden standing here recharges. Needs 50 hp of power.", ""),
    ("Building.ChargingPost.FlavorDescription", "Power is life.", ""),
    ("Building.Cruncher.DisplayName", "The Cruncher", "Wardens research building"),
    ("Building.Cruncher.Description", "Turns power into Science Points, or Badwater and Scrap Metal into Data Cores. Never both.", ""),
    ("Building.Cruncher.FlavorDescription", "Research burns power. Choose what to think about.", ""),
    ("Building.SludgeBurner.DisplayName", "Sludge Burner", "Wardens generator"),
    ("Building.SludgeBurner.Description", "Burns Biomass from Sludge Reed for 200 hp of power. Poisons the ground around it.", ""),
    ("Building.SludgeBurner.FlavorDescription", "The first ecological debt. It will not be the last.", ""),
    ("Building.ReedBed.DisplayName", "Reed Bed", "Wardens farmhouse"),
    ("Building.ReedBed.Description", "Plants and harvests Sludge Reed, the only crop that grows in poisoned soil.", ""),
    ("Building.ReedBed.FlavorDescription", "Something grows here. Nobody said it had to be pretty.", ""),
    ("Building.WardensPod.DisplayName", "Breeding Pod", "Wardens pod"),
    ("Building.WardensPod.Description", "Grows a new beaver from Biomass. Needs 100 hp of power. The pod the Iron Teeth will inherit.", ""),
    ("Building.WardensPod.FlavorDescription", "The first thing the Wardens ever built for someone else.", ""),
    ("Building.WardensAdvancedPod.DisplayName", "Advanced Breeding Pod", "Wardens pod"),
    ("Building.WardensAdvancedPod.Description", "Grows an adult beaver from Biomass and Firmware. Needs 150 hp of power.", ""),
    ("Building.WardensAdvancedPod.FlavorDescription", "It comes out knowing things. Nobody is sure that is a kindness.", ""),
    ("Building.SludgePump.DisplayName", "Sludge Pump", "Wardens badwater pump"),
    ("Building.SludgePump.Description", "Pumps Badwater, the only liquid this land has to offer. Wardens work it without complaint.", ""),
    ("Building.SludgePump.FlavorDescription", "Do not drink. Nobody here drinks.", ""),
    ("Building.BadwaterCell.DisplayName", "Badwater Cell", "Wardens early generator"),
    ("Building.BadwaterCell.Description", "Burns Badwater for 100 hp of power. Cheap, dirty, and the first thing that keeps the lights on.", ""),
    ("Building.BadwaterCell.FlavorDescription", "The Wardens run on badwater. So does everything they build.", ""),
    ("Building.Rack.DisplayName", "Crate Rack", "Wardens small warehouse"),
    ("Building.Rack.Description", "Stores one kind of boxed good, Biomass included. Set the good after selecting the rack.", ""),
    ("Building.Rack.FlavorDescription", "Everything in its place. The Wardens like it that way.", ""),
    ("Building.HaulerDock.DisplayName", "Hauler Dock", "Wardens hauling post"),
    ("Building.HaulerDock.Description", "Wardens stationed here carry goods around the district wherever they are needed.", ""),
    ("Building.HaulerDock.FlavorDescription", "Someone has to carry it. Nobody said it had to be the builder.", ""),
    ("Building.Deck.DisplayName", "Observation Deck", "Wardens rooftop terrace"),
    ("Building.Deck.Description", "A place to stand and look at the land. Beavers gather here, and their well-being rises.", ""),
    ("Building.Deck.FlavorDescription", "Directive 3: record everything. The view is part of the record.", ""),
    ("Building.Planter.DisplayName", "Planter Rig", "Wardens forester"),
    ("Building.Planter.Description", "Plants trees and bushes in its range. Nothing grows here yet; one day it will.", ""),
    ("Building.Planter.FlavorDescription", "Directive 2, in hardware.", ""),
    ("Building.MapGate.DisplayName", "Gate", "Wardens: links a district on another map"),
    ("Building.MapGate.Description", "Links a district you left on another map. What it produced beyond its own needs arrives here every day, for the haulers to carry in.", ""),
    ("Building.MapGate.FlavorDescription", "The maps are not separate. They were never separate; we only lacked the link.", ""),
    # The Gate's panel (src/WardensGateFragment.cs)
    ("Wardens.Gate.Linked", "Linked: {0}, {1}.", "Gate panel; {0} district name, {1} settlement name"),
    ("Wardens.Gate.Rates", "Arrives per day: {0}", "Gate panel; {0} a list like 'Scrap Metal 2.4, Biomass 1.1'"),
    ("Wardens.Gate.NoSurplus", "It had no surplus when you left it.", "Gate panel"),
    ("Wardens.Gate.Unlinked", "Not linked. Districts on other maps you can link: {0}.", "Gate panel; {0} count"),
    ("Wardens.Gate.None", "No district on another map yet. Every map you leave records what its districts produce beyond their needs.", "Gate panel"),
    ("Wardens.Gate.Next", "Link next", "Gate panel button"),
    ("Wardens.Gate.Unlink", "Unlink", "Gate panel button"),
    ("NaturalResource.SludgeReed.DisplayName", "Sludge Reed", "Wardens crop"),
    ("NaturalResource.SludgeReed.FlavorDescription", "Thrives on contamination. Harvested for Biomass.", ""),
    ("Good.Biomass.DisplayName", "Biomass", "Wardens fuel"),
    ("Good.Biomass.PluralDisplayName", "Biomass", "Wardens fuel, plural"),
    # Chapter toasts (src/WardensChapters.cs): Wardens.Chapter.<Id>.Title / .Unlocked
    ("Wardens.Chapter.Badwater.Title", "Chapter 2: Badwater.", "Story chapter title"),
    ("Wardens.Chapter.Badwater.Unlocked", "Scrap in hand. The Sludge Pump, Reed Bed, Sludge Tank and Crate Rack are now available.", "Chapter toast"),
    ("Wardens.Chapter.Signal.Title", "Chapter 3: Signal.", "Story chapter title"),
    ("Wardens.Chapter.Signal.Unlocked", "Shifts are set. The Cruncher is now available: power in, Science or Data Cores out.", "Chapter toast"),
    ("Wardens.Chapter.Pods.Title", "Chapter 4: Pods.", "Story chapter title"),
    ("Wardens.Chapter.Pods.Unlocked", "Stores are full. The Breeding Pod is now available. Built for someone else.", "Chapter toast"),
    ("Wardens.Chapter.Power.Title", "Chapter 5: Power.", "Story chapter title"),
    ("Wardens.Chapter.Power.Unlocked", "The pods are waiting. The Badwater Cell and the Sludge Burner are now available. Every hour of power is an hour of poison.", "Chapter toast"),
    ("Wardens.Chapter.Green.Title", "Chapter 6: Green.", "Story chapter title"),
    ("Wardens.Chapter.Green.Unlocked", "The first beaver is awake. The Advanced Breeding Pod is now available.", "Chapter toast"),
]
loc = SRC / "Localizations/enUS.csv"
existing = list(csv.reader(open(loc, encoding="utf-8", newline="")))
have = {r[0] for r in existing if r}
with open(loc, "w", encoding="utf-8", newline="") as fh:
    w = csv.writer(fh, lineterminator="\n")
    for r in existing:
        if r:
            w.writerow(r)
    for r in rows:
        if r[0] not in have:
            w.writerow(r)
print("loc rows:", sum(1 for r in csv.reader(open(loc, encoding="utf-8", newline="")) if r))

# --- csproj: ship the new folders ---------------------------------------------------------------------
cp = SRC / "Wardens.csproj"
s = open(cp, encoding="utf-8").read()
bs = chr(92)
for folder in ("Buildings", "NaturalResources", "IlluminationColors"):
    token = f"{folder}{bs}**;"
    if token not in s:
        s = s.replace(f"Factions{bs}**;", f"Factions{bs}**;{token}", 1)
open(cp, "w", encoding="utf-8", newline="\n").write(s)

# --- The workforce rule: bots by default, no bot science (tools/bot_workforce.py) ----------------------
import bot_workforce  # noqa: E402  (same folder; last, so it sees every blueprint written above)
for p in bot_workforce.apply_tree(SRC):
    print("botified", p.relative_to(SRC).as_posix())
print("done")
