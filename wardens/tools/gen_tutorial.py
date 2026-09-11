"""Generate the Wardens tutorial: the Folktails tutorial, rewritten for a settlement of bots.

    python wardens/tools/gen_tutorial.py

Mirrors the vanilla chain (Tutorials/Tutorials.*.blueprint.json in the game's Blueprints.zip)
tutorial for tutorial and stage for stage, with the Wardens' buildings, goods and voice:

  vanilla                  Wardens                       what changed
  Tutorial (intro)         Wardens.ColdBoot              Wake / Directive cards, blinking
  Basics                   Wardens.Basics                camera, pause, speed (same steps)
  Wood                     Wardens.Scrap                 Scavenger Flags instead of Lumberjacks, stock 10 scrap
  WaterAndFood             Wardens.BadwaterAndBiomass    Sludge Pump, Charging Post (+charge check), Reed Bed, plant reed
  WorkingHours             Wardens.WorkingHours          same
  Storage                  Wardens.Storage               Scrap Pile, Sludge Tanks, Crate Rack
  Science                  Wardens.Science               the Cruncher, powered from the Core
  Housing                  Wardens.Housing               Breeding Pods
  PowerAndPlanks           Wardens.Power                 Badwater Cell, power the pods
  MoreBeavers              Wardens.MoreBeavers           first beaver out of a pod (BeaversStepSpec)
  Forestry                 Wardens.Reforestation         build the Planter Rig (free from the start), plant birches
  Wellbeing                Wardens.Wellbeing             select a Warden, well-being panel, default hours
  Dams                     Wardens.Dams                  Wardens.MissingDamTrigger (WardensTriggers.cs)
  VerticalArchitecture     Wardens.VerticalArchitecture  after Maintenance; stairs are free, so no unlock trigger
  Droughts / Badtides      Wardens.Droughts / .Badtides  vanilla weather triggers, new text
  HaulersAndWorkforce      Wardens.Haulers               Wardens.IdleWardensTrigger, Hauler Dock
  LayerTool                Wardens.LayerTool             Wardens.PlatformBuiltTrigger

Writes Tutorials/Tutorials.Wardens.<Name>.blueprint.json, Tutorials/Stages/Wardens.<Name>.<Stage>
.blueprint.json and every Tutorial.Wardens.* row of Localizations/enUS.csv (old rows with that
prefix are replaced, everything else in the csv is kept). Stale files under Tutorials/ are removed.
The vanilla tutorials themselves stay switched off by Factions/VanillaTutorial.WardensModifier.
Run gen_buildings.py first: the stages reference the buildings it writes.
"""
from __future__ import annotations

import csv
import json
from pathlib import Path

SRC = Path(__file__).resolve().parents[1] / "src"
TUT = SRC / "Tutorials"
LOC = SRC / "Localizations/enUS.csv"
SHAFTS = ["PowerShaft.Wardens"]


# --- step builders ------------------------------------------------------------------------------------
def build(*names: str, finished: bool = True, n: int = 1) -> dict:
    return {"BuildingTutorialStepSpec": {"TemplateNames": list(names), "OnlyFinishedBuildings": finished, "RequiredAmount": n}}


def connect(name: str, n: int = 1, unfinished: bool = True, highlight: tuple[str, ...] = ("Path",)) -> dict:
    return {"ConnectBuildingsTutorialStepSpec": {"TemplateName": name, "RequiredAmount": n,
                                                 "CountUnfinishedBuildings": unfinished, "HighlightableBuildingIds": list(highlight)}}


def powered(name: str, n: int = 1) -> dict:
    return {"PoweredBuildingStepSpec": {"TemplateName": name, "RequiredAmount": n, "ShaftTemplateNames": SHAFTS}}


def stock_good(template: str, n: int, good: str) -> dict:
    return {"SelectStockpileGoodTutorialStepSpec": {"TemplateName": template, "RequiredAmount": n, "GoodId": good}}


def plant(template: str, n: int) -> dict:
    return {"MarkPlantablesTutorialStepSpec": {"TemplateName": template, "RequiredAmount": n}}


def select(*names: str, key: str = "Tutorial.Wardens.SelectWarden") -> dict:
    return {"SelectEntityStepSpec": {"TemplateNames": list(names), "DescriptionLocKey": key}}


def camera_move(direction: str, threshold: float = 3.0) -> dict:
    return {"CameraMovementStepSpec": {"Direction": direction, "Threshold": threshold}}


def camera_rotate(direction: str, angle: float = 30.0) -> dict:
    return {"CameraRotationStepSpec": {"Direction": direction, "Angle": angle}}


def camera_zoom(direction: str, threshold: float = 2.0) -> dict:
    return {"CameraZoomStepSpec": {"Direction": direction, "Threshold": threshold}}


def pause(state: bool, once: bool) -> dict:
    return {"SetPauseStepSpec": {"Pause": state, "OnlyOnce": once}}


def speed(level: int, once: bool) -> dict:
    return {"GameSpeedStepSpec": {"Speed": level, "OnlyOnce": once}}


def hours(target: int) -> dict:
    return {"SetWorkingHoursStepSpec": {"TargetWorkingHours": target}}


def layer(change: str, keys: bool) -> dict:
    return {"VisibleLevelChangeStepSpec": {"VisibleLevelChangeType": change, "ShowKeybindings": keys}}


def stock(good: str, n: int) -> dict:
    return {"GoodStockStepSpec": {"GoodId": good, "Amount": n}}


def charged(min_energy: float = 0.5) -> dict:
    return {"BotsChargedStepSpec": {"MinEnergy": min_energy}}


def beavers(n: int = 1) -> dict:
    return {"BeaversStepSpec": {"RequiredAmount": n}}


def workers(template: str) -> dict:
    return {"IncreaseDesiredWorkersStepSpec": {"TemplateName": template}}


def priority_down(template: str) -> dict:
    return {"DecreasePriorityStepSpec": {"TemplateName": template}}


def paused(template: str, state: bool) -> dict:
    return {"ChangePausedStateStepSpec": {"ShouldBePaused": state, "TemplateName": template}}


# --- the tutorial line ----------------------------------------------------------------------------------
# (id, display name, required tutorial/trigger ids, skip-if-finished, sort order, blinking, stages)
# stage = (id, intro text, [steps])
BOT = "Bot.IronTeeth"
TUTORIALS = [
    ("ColdBoot", "Cold Boot", [], "", 0, True, [
        ("Wake",
         "SYSTEM RESTART.\n\nThe uptime counter overflowed 3,000 days ago. Sensors report no growth, no water worth drinking, no beavers.\n\nFive Wardens online. Power: the Core, and nothing else.",
         []),
        ("Badtides",
         "The archive kept working after everything else stopped.\n\nThree badtides are logged from before this boot. Nobody was awake to live through them; the sensors recorded them anyway.\n\nThe uplink log is about to read them out.",
         []),
        ("Directive",
         "We were not built to live here. We were built so that others could.\n\nDirective 1: keep the machines running.\nDirective 2: make this land livable.\nDirective 3: record everything. One day, someone will need to know how.",
         []),
    ]),
    ("Basics", "Basics", ["Wardens.ColdBoot"], "", 10, False, [
        ("MoveCamera",
         "First, a survey of the site. Move the view across the ground.\n\nLook for ruins. The old world left its metal behind, and metal is the only thing this ground still gives.",
         [camera_move("Up"), camera_move("Down"), camera_move("Left"), camera_move("Right")]),
        ("RotateCamera", "Now rotate the view. Ruins hide behind ruins.",
         [camera_rotate("Left"), camera_rotate("Right")]),
        ("ZoomCamera", "Zoom in to inspect a single Warden, or pull back to see the whole site.",
         [camera_zoom("In"), camera_zoom("Out")]),
        ("ManagePause",
         "Time runs whether we are ready or not.\n\nPause when you need to think. The Wardens will wait. They are good at waiting.",
         [pause(True, True), pause(False, False)]),
        ("ManageSpeed",
         "Speed up when there is nothing to decide. Wardens do not tire of working, and you should not tire of watching them.",
         [speed(2, True), speed(3, True), speed(1, False)]),
    ]),
    ("Scrap", "Scrap", ["Wardens.Basics"], "", 20, False, [
        ("PlaceScavengers",
         "There is no wood here. There is scrap.\n\nOnly scavengers collect Scrap Metal, and only from ruins within their flag's range. Place two Scavenger Flags beside the ruins.",
         [build("ScavengerFlag.IronTeeth", finished=False, n=2)]),
        ("FinishScavengers",
         "The Flags will not be built unless they are in the district's building range.\n\nSelect the Core to see its range. Paths connected to the Core's entrance extend it.",
         [build("ScavengerFlag.IronTeeth", n=2)]),
        ("ConnectScavengers",
         "Every building with an entrance must be connected to the Core with a Path. Select a building to see its entrance marked by an arrow.\n\nBuild Paths to connect the Scavenger Flags to the Core.",
         [connect("ScavengerFlag.IronTeeth", n=2, unfinished=False)]),
        ("ScrapStock",
         "Scrap Metal is the only material this land gives freely.\n\nGather ten. Everything the Wardens will ever build starts as someone else's wreckage.",
         [stock("ScrapMetal", 10)]),
    ]),
    ("BadwaterAndBiomass", "Badwater and Biomass", ["Wardens.Scrap"], "", 30, False, [
        ("BuildSludgePump",
         "Wardens do not drink. They do need Badwater: it feeds the generators and, later, the Data Cores.\n\nBuild a Sludge Pump on a badwater source and connect it to the Core.",
         [build("SludgePump.Wardens")]),
        ("BuildChargingPost",
         "Power is life. A Warden that runs dry stops where it stands, and it does not get up again.\n\nBuild a Charging Post next to the Core. Then select a Warden and read its Energy; keep every unit above half charge.",
         [build("ChargingPost.Wardens"), select(BOT), charged(0.5)]),
        ("BuildReedBed",
         "Directive 2 needs something that grows. Sludge Reed grows in poisoned soil, and its Biomass will feed the pods.\n\nBuild a Reed Bed. Like flags, it has a limited range.",
         [build("ReedBed.Wardens")]),
        ("PlantReed",
         "The Reed Bed does not start working until you tell it what and where to plant.\n\nUse the Plant Crops tool to designate a Sludge Reed field. Reed asks for no irrigation. It asks for nothing at all.",
         [plant("SludgeReed", 40)]),
    ]),
    ("WorkingHours", "Working hours", ["Wardens.BadwaterAndBiomass"], "Wardens.Wellbeing", 35, False, [
        ("SetWorkingHours",
         "Wardens do not sleep, but the Core sets a shift length for everything that works here.\n\nLonger shifts mean faster progress and a faster drain on the units. For now, extend the shift a little.",
         [hours(18)]),
    ]),
    ("Storage", "Storage", ["Wardens.WorkingHours"], "", 40, False, [
        ("BuildPile",
         "Many buildings have some built-in storage, and it fills up quickly.\n\nBuild a Scrap Pile. Each storage building holds a single good; set it after selecting the building.",
         [build("ScrapPile.Wardens"), stock_good("ScrapPile.Wardens", 1, "ScrapMetal")]),
        ("BuildTanks",
         "All liquids are stored in tanks, Badwater included.\n\nBuild two Sludge Tanks and set them to Badwater.",
         [build("SludgeTank.Wardens", n=2), stock_good("SludgeTank.Wardens", 2, "Badwater")]),
        ("BuildRacks",
         "Biomass is a boxed good, and boxed goods go in racks.\n\nBuild a Crate Rack and set it to Biomass before the first harvest comes in.",
         [build("Rack.Wardens"), stock_good("Rack.Wardens", 1, "Biomass")]),
    ]),
    ("Science", "Science", ["Wardens.WorkingHours"], "", 50, False, [
        ("BuildCruncher",
         "Metal is not the goal. Knowledge is.\n\nBuild the Cruncher and connect it to the Core with a Power Shaft. The Core can barely run it; that is the point.",
         [build("Cruncher.Wardens"), powered("Cruncher.Wardens")]),
        ("SciencePoints",
         "For now, let the Cruncher think.\n\nEvery building is already yours to place; nothing on the bar waits for Science. Science Points are the measure of what the Wardens learn here. Data Cores feed Firmware and the Archive.",
         []),
    ]),
    ("Housing", "Pods", ["Wardens.Storage"], "", 60, False, [
        ("BuildPods",
         "We were built so that others could live here. Breeding Pods grow beavers from Biomass.\n\nBuild two Breeding Pods. They stay dark until they have power; that comes next.",
         [build("BreedingPod.Wardens", n=2)]),
    ]),
    ("Power", "Power and pods", ["Wardens.Housing"], "", 70, False, [
        ("BuildCell",
         "The Core cannot feed everything. Wardens run on what this land has: Badwater.\n\nBuild a Badwater Cell. It burns Badwater from the Sludge Pump for 100 hp of power.",
         [build("BadwaterCell.Wardens")]),
        ("PowerPods",
         "Connect the Cell to a Breeding Pod with Power Shafts.\n\nTip: buildings placed wall to wall pass power to each other. No shafts needed.",
         [powered("BreedingPod.Wardens")]),
    ]),
    ("MoreBeavers", "First beaver", ["Wardens.Power"], "", 75, False, [
        ("FirstBeaver",
         "A pod needs power and Biomass, then five days.\n\nWait for the first beaver. It will not know what the Wardens are. It will not need to.",
         [beavers(1)]),
    ]),
    ("Reforestation", "Reforestation", ["Wardens.Power", "Wardens.Science"], "", 80, False, [
        ("BuildPlanter",
         "Directive 2: make this land livable.\n\nBuild the Planter Rig where you want trees to grow. It plants within its range, and nothing else here does.",
         [build("Planter.Wardens")]),
        ("PlantBirches",
         "Use the Plant Trees and Bushes tool to plant Birches. They grow quickly and ask for little.\n\nTrees only grow on irrigated, green ground. Find some. There is not much.",
         [plant("Birch", 20)]),
    ]),
    ("Wellbeing", "Maintenance", ["Wardens.Housing"], "", 90, False, [
        ("SelectWarden",
         "You can see the state of every unit.\n\nSelect a Warden by clicking on it. Its panel shows Energy, attributes and needs. A need at zero has consequences.",
         [select(BOT)]),
        ("WellbeingPanel",
         "The settlement's average well-being is shown in the top left corner.\n\nClick it to see every need and how well it is met. Wardens have few needs. The beavers will have more.",
         [{"OpenWellbeingPanelStepSpec": {}}]),
        ("SetWorkingHours",
         "Long shifts drain Wardens faster than the Charging Posts can fill them.\n\nFor now, return to the default working hours.",
         [hours(16)]),
    ]),
    ("Dams", "Dams", ["Wardens.MissingDamTrigger"], "", 100, False, [
        ("BuildDam",
         "Tanks hold Badwater for a while. Long-term survival depends on taming the flow of water.\n\nBuild a line of Dams across the nearest river, bank to bank, and observe what happens.",
         [build("Dam.Wardens")]),
    ]),
    # Vanilla waits for StairsUnlockedTrigger here; the Wardens' stairs are free from the start, so there
    # is no unlock for that trigger to see and Maintenance alone opens this one.
    ("VerticalArchitecture", "Vertical architecture", ["Wardens.Wellbeing"], "", 110, False, [
        ("BuildDeck",
         "Space is scarce in the wasteland. The Wardens build upward.\n\nSome buildings have flat roofs that can support other structures. Build an Observation Deck on one; you may need Stairs to connect it.",
         [build("Deck.Wardens"), connect("Deck.Wardens", n=1, unfinished=True, highlight=("Path", "Stairs.Wardens"))]),
    ]),
    ("Droughts", "Droughts", ["SurvivedFirstDroughtTrigger"], "", 150, False, [
        ("Description",
         "The settlement has survived its first drought.\n\nDroughts are part of the seasonal cycle. They return, and each one lasts longer.\n\nIf you have not built a dam, build one before the next.",
         []),
    ]),
    ("Badtides", "Badtides", ["SurvivedFirstBadtideTrigger"], "", 150, False, [
        ("Description",
         "The settlement has weathered its first badtide.\n\nBadwater is nothing new to the Wardens, but the beavers and the reeds are not so indifferent. Future badtides will not be as forgiving.",
         []),
    ]),
    ("Haulers", "Haulers and workforce", ["Wardens.IdleWardensTrigger"], "", 160, False, [
        ("BuildHaulerDock",
         "As the settlement grows, it moves more goods. Transport takes a large part of a unit's shift.\n\nHaulers help. They move goods around the entire district, following demand. Build a Hauler Dock.",
         [build("HaulingPost.Wardens")]),
        ("ChangeWorkforce",
         "Some workplaces, the Hauler Dock included, do not fill all their slots by default. Change the desired number of workers in the building's panel.\n\nWorkplace priorities control staffing too. The Dock can usually run on low priority. Try both now.",
         [workers("HaulingPost.Wardens"), priority_down("HaulingPost.Wardens")]),
        ("PauseHaulerDock",
         "Buildings and construction sites can be paused. Paused workplaces are vacated at once and the units reassign themselves.\n\nSelect the Hauler Dock and pause it from its panel.",
         [paused("HaulingPost.Wardens", True), paused("HaulingPost.Wardens", False)]),
    ]),
    ("LayerTool", "Layer tool", ["Wardens.PlatformBuiltTrigger"], "", 160, False, [
        ("ChangeUsingUI",
         "As the settlement grows upward, working beneath buildings gets difficult.\n\nThe Layer tool hides everything above the selected layer. Change the visible layer with the highlighted buttons.",
         [layer("Decrease", False), layer("Increase", False), layer("Reset", False)]),
        ("ChangeUsingMouse",
         "The visible layer also has key bindings, like most actions. Find them in tooltips or in the Settings menu.",
         [layer("Decrease", True), layer("Increase", True)]),
    ]),
]

# Step descriptions used by the Wardens' own step types (WardensTutorialSteps.cs & co).
STEP_ROWS = [
    ("Tutorial.Wardens.SelectWarden", "Select a Warden", "Tutorial step"),
    ("Tutorial.Wardens.GoodStock", "Stock: {0} ({1}/{2})", "{0} good name, {1} amount in stock, {2} amount required"),
    ("Tutorial.Wardens.BotsCharged", "Charge: every Warden above {0}% ({1}/{2})", "{0} percent, {1} charged bots, {2} all bots"),
    ("Tutorial.Wardens.Beavers", "Beavers: ({0}/{1})", "{0} beavers alive, {1} required"),
    ("Tutorial.Wardens.Never", "(disabled)", "Placeholder name for vanilla tutorials switched off for the Wardens"),
]


def write(rel: str, obj: dict) -> Path:
    p = SRC / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, indent=2)
        f.write("\n")
    return p


def main() -> None:
    written: set[Path] = set()
    rows: list[tuple[str, str, str]] = []
    for tid, name, required, skip, order, blink, stages in TUTORIALS:
        full = f"Wardens.{tid}"
        tut = {"TutorialSpec": {
            "Id": full, "NameLocKey": f"Tutorial.{full}", "RequiredTutorialIds": required,
            "SkipIfTutorialFinished": skip, "SortOrder": order,
            "Stages": [f"{full}.{sid}" for sid, _, _ in stages],
        }}
        if blink:
            tut["BlinkingTutorialSpec"] = {}
        written.add(write(f"Tutorials/Tutorials.{full}.blueprint.json", tut))
        rows.append((f"Tutorial.{full}", name, "Tutorial title"))
        for sid, text, steps in stages:
            stage = {"TutorialStageSpec": {"Id": f"{full}.{sid}", "IntroLocKey": f"Tutorial.{full}.{sid}"}}
            if steps:
                stage["Children"] = {f"Step{i + 1}": s for i, s in enumerate(steps)}
            written.add(write(f"Tutorials/Stages/{full}.{sid}.blueprint.json", stage))
            rows.append((f"Tutorial.{full}.{sid}", text, ""))
    rows += STEP_ROWS

    stale = [p for p in TUT.rglob("*.json") if p not in written]
    for p in stale:
        p.unlink()
        print("removed", p.relative_to(SRC).as_posix())

    existing = [r for r in csv.reader(open(LOC, encoding="utf-8", newline="")) if r]
    kept = [r for r in existing if not r[0].startswith("Tutorial.Wardens")]
    with open(LOC, "w", encoding="utf-8", newline="") as fh:
        w = csv.writer(fh, lineterminator="\n")
        for r in kept:
            w.writerow(r)
        for r in rows:
            w.writerow(r)
    print(f"tutorials: {len(TUTORIALS)}, stages: {sum(len(t[6]) for t in TUTORIALS)}, "
          f"loc rows: {len(rows)} replaced {len(existing) - len(kept)}")


if __name__ == "__main__":
    main()
