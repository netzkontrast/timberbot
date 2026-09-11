"""Port a first batch of Leaf Coats buildings into the Wardens, from a live `dump_assets` dump.

    python wardens/tools/gen_port.py

Reads `Documents/Timberborn/Mods/Wardens/dump/blueprints/**/*.leafcoats*.json` (written by the
MCP tool `dump_assets what=blueprints filter=LeafCoats` while a game with the Wardens mod is
running — the game's own SpecService has already resolved every field, so the dumped JSON is
complete and needs no re-merging). Applies the same five edits `gen_buildings.py` uses for the
Iron Teeth re-specs — TemplateName suffix, fresh LabeledEntitySpec loc keys, a Scrap Metal cost,
a DefaultIlluminatorColorSpec recolor where one already exists — and writes new loose blueprints
under `wardens/src`. Model, mesh, collider, transput and Script Pack specs are copied verbatim;
nothing inside the AssetBundle is touched (design/leafcoats-port-plan.md rule 1).

This batch only (design/leafcoats-port-plan.md §5, "Act I keep" + "Act II/III" columns for
DistrictManagement/Power/Science/Water/Metal/Storage): the genuinely new buildings Leaf Coats
adds beyond what `gen_buildings.py` already built from Iron Teeth. Buildings we already have
under a Wardens name (Core, ChargingPost, PowerShaft, HaulingPost, the small storage tier, the
badwater pump) are deliberately skipped so nothing already wired into the live tutorial/save
changes shape. Written to a new collection, `Buildings.WardensPort`, NOT yet added to
`Faction.Wardens.TemplateCollectionIds` — wiring them into the chapter progression is a separate,
deliberate step (design/wardens-chapter-1-plan.md §4).

Cost: Leaf Coats' own material costs (Gear/MetalBlock/TreatedPlank/PineResin/...) don't exist in
the Wardens' Scrap-only economy, so every building gets a Scrap Metal cost from a tier keyed off
its *own* ScienceCost, which is kept as-is (it already encodes Bobingabout's tech-tree ordering,
and design/wardens-chapter-1-plan.md §4 planned to reuse ScienceCost as the chapter padlock).
Since 2026-09-11 the padlock is gone and tools/validate.py demands ScienceCost 0 of every building in
a collection the faction lists: wiring this collection in means writing 0 here first (the scrap tier
stays keyed off the dumped value).
"""
from __future__ import annotations

import json
import os
from pathlib import Path

DUMP = Path(os.environ.get("USERPROFILE", str(Path.home()))) / "Documents/Timberborn/Mods/Wardens/dump/blueprints"
SRC = Path(__file__).resolve().parents[1] / "src"
FACTION = "Wardens"


def load(rel: str) -> dict:
    text = (DUMP / rel).read_text(encoding="utf-8")
    lines = [l for l in text.splitlines() if not l.strip().startswith("//")]
    return json.loads("\n".join(lines))


def scrap_cost(science: int) -> int:
    """A Scrap Metal cost tier keyed off the building's own (unchanged) ScienceCost."""
    for threshold, cost in ((0, 10), (150, 15), (300, 20), (600, 30), (1000, 45), (2000, 70)):
        if science <= threshold:
            return cost
    return 100


def labeled(d: dict, key: str, name: str, desc: str, flavor: str) -> None:
    icon = d.get("LabeledEntitySpec", {}).get("Icon", "")
    d["LabeledEntitySpec"] = {
        "DisplayNameLocKey": f"Building.{key}.DisplayName",
        "DescriptionLocKey": f"Building.{key}.Description",
        "FlavorDescriptionLocKey": f"Building.{key}.FlavorDescription",
        "Icon": icon,
    }
    rows.extend([
        (f"Building.{key}.DisplayName", name, ""),
        (f"Building.{key}.Description", desc, ""),
        (f"Building.{key}.FlavorDescription", flavor, ""),
    ])


def port(dump_rel: str, wardens_name: str, key: str, name: str, desc: str, flavor: str, dest_dir: str) -> None:
    d = load(dump_rel)
    science = d.get("BuildingSpec", {}).get("ScienceCost", 0)
    d["TemplateSpec"]["TemplateName"] = wardens_name
    d["TemplateSpec"]["BackwardCompatibleTemplateNames"] = []
    d["BuildingSpec"]["BuildingCost"] = [{"Id": "ScrapMetal", "Amount": scrap_cost(science)}]
    d["BuildingSpec"]["ScienceCost"] = science
    labeled(d, key, name, desc, flavor)
    if "MechanicalNodeIlluminatorSpec" in d:
        d["DefaultIlluminatorColorSpec"] = {"ColorId": "WardensCyan"}
    rel = f"Buildings/{dest_dir}/{wardens_name}.blueprint.json"
    p = SRC / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        json.dump(d, f, indent=2)
        f.write("\n")
    buildings.append(rel[:-5])
    print("wrote", rel, "| scrap", scrap_cost(science), "| sci", science)


buildings: list[str] = []
rows: list[tuple[str, str, str]] = []

# --- District management: infrastructure the Core needs as the settlement grows ------------------
port("buildings/districtmanagement/buildershut.leafcoats.blueprint.json", "BuildersHut.Wardens",
     "BuildersHut", "Builders Hut", "Wardens report here between construction jobs.",
     "Directive 1, staffed.", "DistrictManagement/BuildersHut")
port("buildings/districtmanagement/districtcrossing.leafcoats.blueprint.json", "DistrictCrossing.Wardens",
     "DistrictCrossing", "District Crossing", "Extends the building range of two neighboring districts across a shared border.",
     "The map doesn't care where the line is. Neither do we.", "DistrictManagement/DistrictCrossing")
port("buildings/districtmanagement/districtgate.leafcoats.blueprint.json", "DistrictGate.Wardens",
     "DistrictGate", "District Gate", "Lets Wardens and goods pass between districts without breaking the range.",
     "One settlement, however many lines we draw on it.", "DistrictManagement/DistrictGate")

# --- Power: the generators Chapter 2+ needs once the Core alone can't carry the load -------------
port("buildings/power/geothermalengine.leafcoats.blueprint.json", "GeothermalEngine.Wardens",
     "GeothermalEngine", "Geothermal Engine", "Draws power from heat deep in the wasteland. No fuel, no pollution, no rest.",
     "The ground was already burning. We just gave it somewhere to put the heat.", "Power/GeothermalEngine")
port("buildings/power/gravitybattery.leafcoats.blueprint.json", "GravityBattery.Wardens",
     "GravityBattery", "Gravity Battery", "Stores power by lifting weight, releases it by lowering. No chemistry, no decay.",
     "It forgets nothing and it forgives nothing. Physics rarely does.", "Power/GravityBattery")
port("buildings/power/gravitybattery.treetrunk.leafcoats.blueprint.json", "GravityBattery.Gantry.Wardens",
     "GravityBatteryGantry", "Gravity Battery (Gantry)", "A lighter gravity battery built onto a scaffold, for districts with less to spend.",
     "Smaller counterweight, same patience.", "Power/GravityBattery")
port("buildings/power/gravitybattery.treetrunk2.leafcoats.blueprint.json", "GravityBattery.GantryII.Wardens",
     "GravityBatteryGantryII", "Gravity Battery (Gantry II)", "A taller gravity battery, more capacity than the standard gantry model.",
     "The fall is longer. So is the power it hands back.", "Power/GravityBattery")
port("buildings/power/largewindmill.leafcoats.blueprint.json", "LargeWindmill.Wardens",
     "LargeWindmill", "Large Windmill", "A high-capacity windmill. Needs clear air and a tall mount.",
     "Wind doesn't pollute. It also doesn't show up on schedule.", "Power/LargeWindmill")
port("buildings/power/powerdam.leafcoats.blueprint.json", "PowerDam.Wardens",
     "PowerDam", "Power Dam", "A dam that generates power from the water it holds back.",
     "Two jobs, one wall.", "Power/PowerDam")
port("buildings/power/powerlevee.leafcoats.blueprint.json", "PowerLevee.Wardens",
     "PowerLevee", "Power Levee", "A power shaft built into a levee, carrying power across a barrier.",
     "The wall was already there. The power line just needed a reason.", "Power/PowerLevee")
port("buildings/power/powerleveetunnel.leafcoats.blueprint.json", "PowerLeveeTunnel.Wardens",
     "PowerLeveeTunnel", "Power Levee Tunnel", "Carries a power shaft underground through a levee.",
     "Under the wall, not over it.", "Power/PowerLevee")
port("buildings/power/powershaft/clutch.leafcoats.blueprint.json", "Clutch.Wardens",
     "Clutch", "Clutch", "Lets a power shaft be switched off without dismantling it.",
     "Sometimes the machine needs to rest. This is how we let it.", "Power/PowerShaft")
port("buildings/power/powershaft/verticalpowershaft.leafcoats.blueprint.json", "VerticalPowerShaft.Wardens",
     "VerticalPowerShaft", "Vertical Power Shaft", "Carries power between levels.",
     "Power doesn't care which way is up. We built this so it wouldn't have to.", "Power/PowerShaft")
port("buildings/power/powershaft/verticalpowershafttunnel.leafcoats.blueprint.json", "VerticalPowerShaftTunnel.Wardens",
     "VerticalPowerShaftTunnel", "Vertical Power Shaft Tunnel", "Carries power between levels through solid ground.",
     "Through the rock, not around it.", "Power/PowerShaft")
port("buildings/power/powertreadmill.leafcoats.blueprint.json", "PowerTreadmill.Wardens",
     "PowerTreadmill", "Power Treadmill", "A Warden walking in place generates a small, steady trickle of power.",
     "It goes nowhere on purpose. That's the point.", "Power/PowerTreadmill")
port("buildings/power/waterwheel.leafcoats.blueprint.json", "WaterWheel.Wardens",
     "WaterWheel", "Water Wheel", "Generates power from flowing water. Needs a live current, not standing water.",
     "The river never asked for a job. It has one now.", "Power/WaterWheel")
port("buildings/power/windturbine.leafcoats.blueprint.json", "WindTurbine.Wardens",
     "WindTurbine", "Wind Turbine", "A standard wind turbine. Needs open air.",
     "Clean power, no directive required.", "Power/WindTurbine")
port("buildings/power/windturbine.treetop.leafcoats.blueprint.json", "WindTurbine.Gantry.Wardens",
     "WindTurbineGantry", "Wind Turbine (Gantry Top)", "A wind turbine mounted at the top of a tall gantry, catching wind other buildings can't.",
     "Height is free. We take what's free.", "Power/WindTurbine")
port("buildings/power/windturbine.treetrunk.leafcoats.blueprint.json", "WindTurbine.GantryBase.Wardens",
     "WindTurbineGantryBase", "Wind Turbine (Gantry)", "A wind turbine on a shorter gantry mount.",
     "Not as tall. Still catches something.", "Power/WindTurbine")

# --- Science: the buildings that turn power into knowledge beyond the Cruncher -------------------
port("buildings/science/botassembler.leafcoats.blueprint.json", "BotAssembler.Wardens",
     "BotAssembler", "Bot Assembler", "Assembles a new Warden from Bot Chassis, Bot Head and Bot Limb parts.",
     "The Cold Boot said five. This is how there could one day be more.", "Science/BotAssembler")
port("buildings/science/botpartfactory.leafcoats.blueprint.json", "BotPartFactory.Wardens",
     "BotPartFactory", "Bot Part Factory", "Manufactures the chassis, heads and limbs the Bot Assembler needs.",
     "Spare parts, in case the wasteland ever asks for spares.", "Science/BotPartFactory")
port("buildings/science/controltower.leafcoats.blueprint.json", "ControlTower.Wardens",
     "ControlTower", "Uplink Mast", "Extends the Uplink need's range over a wide radius. Every Warden nearby stays in signal.",
     "Directive 3 needs a signal that reaches. This is the mast that gives it one.", "Science/ControlTower")
port("buildings/science/geothermalnumbercruncher.leafcoats.blueprint.json", "Cruncher.Geothermal.Wardens",
     "CruncherGeothermal", "Cruncher II", "A second-generation Cruncher, geothermally powered, far past what the first can compute.",
     "The first Cruncher barely ran. This one doesn't remember what barely running felt like.", "Science/Cruncher")
port("buildings/science/inventor.leafcoats.blueprint.json", "DataDesk.Wardens",
     "DataDesk", "Data Desk", "Turns raw observation into Science Points, the same job the Inventor does for beavers.",
     "No beaver has sat here yet. The desk doesn't mind waiting.", "Science/DataDesk")
port("buildings/science/observatory.leafcoats.blueprint.json", "TelemetryDish.Wardens",
     "TelemetryDish", "Telemetry Dish", "Reads the sky and the weather, feeding the Telemetry need across the district.",
     "It has been listening since before we woke up. It hasn't stopped.", "Science/TelemetryDish")
port("buildings/science/refinery.leafcoats.blueprint.json", "Refinery.Wardens",
     "Refinery", "Refinery", "Processes raw scavenged material into refined components.",
     "Wreckage in, something useful out.", "Science/Refinery")

# --- Water: the badwater/clean-water infrastructure beyond the starting Sludge Pump ---------------
port("buildings/water/aquiferdrill.leafcoats.blueprint.json", "AquiferDrill.Wardens",
     "AquiferDrill", "Aquifer Drill", "Drills deep for water no surface pump can reach.",
     "Whatever is down there, it's ours now.", "Water/AquiferDrill")
port("buildings/water/badwaterdome.leafcoats.blueprint.json", "BadwaterDome.Wardens",
     "BadwaterDome", "Badwater Dome", "Contains a large badwater source, keeping it from spreading further.",
     "We didn't make the poison. We can still put a lid on it.", "Water/BadwaterDome")
port("buildings/water/badwaterrig.leafcoats.blueprint.json", "BadwaterRig.Wardens",
     "BadwaterRig", "Badwater Rig", "A high-capacity badwater extraction rig, far past what the Sludge Pump can draw.",
     "The first pump was a scavenged part. This one was designed.", "Water/BadwaterRig")
port("buildings/water/badwaterseeprig.leafcoats.blueprint.json", "BadwaterSeepRig.Wardens",
     "BadwaterSeepRig", "Badwater Seep Rig", "Draws badwater from a seep rather than an open source.",
     "It finds the poison that hasn't surfaced yet.", "Water/BadwaterSeepRig")
port("buildings/water/compactmechanicalpump.leafcoats.blueprint.json", "CompactMechanicalPump.Wardens",
     "CompactMechanicalPump", "Compact Mechanical Pump", "A small mechanical water pump, no worker needed.",
     "One moving part. It doesn't ask for a shift schedule.", "Water/MechanicalPump")
port("buildings/water/discharge.leafcoats.blueprint.json", "Discharge.Wardens",
     "Discharge", "Discharge", "Releases excess water back into the world, keeping a district's tanks from overflowing.",
     "Some of what we pump, we have to give back.", "Water/Discharge")
port("buildings/water/largewaterpump.leafcoats.blueprint.json", "LargeWaterPump.Wardens",
     "LargeWaterPump", "Large Water Pump", "A high-throughput clean water pump, staffed.",
     "The beavers will need this before they know they need it.", "Water/WaterPump")
port("buildings/water/mechanicalpump.leafcoats.blueprint.json", "MechanicalPump.Wardens",
     "MechanicalPump", "Mechanical Pump", "An unstaffed water pump, powered instead of worked.",
     "No shift, no rest, no complaint.", "Water/MechanicalPump")
port("buildings/water/seepcover.leafcoats.blueprint.json", "SeepCover.Wardens",
     "SeepCover", "Seep Cover", "Caps a water seep, redirecting its flow to where a district can use it.",
     "The ground leaks. We decide where.", "Water/SeepCover")
port("buildings/water/waterpump.leafcoats.blueprint.json", "WaterPump.Wardens",
     "WaterPump", "Water Pump", "A staffed clean water pump. The wasteland has none of its own; this is for what comes after it.",
     "We don't drink. They will.", "Water/WaterPump")

# --- Metal: heavier scavenging than the Scavenger Flag can manage ---------------------------------
port("buildings/metal/mine.leafcoats.blueprint.json", "Mine.Wardens",
     "Mine", "Mine", "Extracts ore from deep deposits, far beyond what surface scavenging can find.",
     "The ruins ran dry. The ground underneath hasn't.", "Metal/Mine")
port("buildings/metal/shredder.leafcoats.blueprint.json", "Shredder.Wardens",
     "Shredder", "Shredder", "Breaks down scavenged wreckage into usable Scrap Metal faster than a flag alone can.",
     "It doesn't care what the wreckage used to be.", "Metal/Shredder")

# --- Storage: the larger tier beyond the starting Scrap Pile / Sludge Tank / Rack -----------------
port("buildings/storage/largepile.leafcoats.blueprint.json", "LargePile.Wardens",
     "LargePile", "Large Scrap Pile", "A large open pile for a single good.",
     "More wreckage than the first pile ever expected to hold.", "Storage/LargePile")
port("buildings/storage/largetank.leafcoats.blueprint.json", "LargeTank.Wardens",
     "LargeTank", "Large Tank", "A large liquid tank for a single good.",
     "Badwater doesn't get less dangerous by the barrel. Just more of it.", "Storage/LargeTank")
port("buildings/storage/largewarehouse.leafcoats.blueprint.json", "LargeWarehouse.Wardens",
     "LargeWarehouse", "Large Warehouse", "A large enclosed store for a single good.",
     "Everything has a place. This is where the overflow goes.", "Storage/LargeWarehouse")
port("buildings/storage/mediumpile.leafcoats.blueprint.json", "MediumPile.Wardens",
     "MediumPile", "Medium Scrap Pile", "A mid-size open pile for a single good.",
     "Between the first pile and the last.", "Storage/MediumPile")
port("buildings/storage/mediumtank.leafcoats.blueprint.json", "MediumTank.Wardens",
     "MediumTank", "Medium Tank", "A mid-size liquid tank for a single good.",
     "Enough badwater to run the Cell for a while.", "Storage/MediumTank")
port("buildings/storage/mediumwarehouse.leafcoats.blueprint.json", "MediumWarehouse.Wardens",
     "MediumWarehouse", "Medium Warehouse", "A mid-size enclosed store for a single good.",
     "Room enough to stop counting every crate by hand.", "Storage/MediumWarehouse")

# --- Collection: not yet added to the faction, see module docstring ------------------------------
write = SRC / "Collections/TemplateCollection.Buildings.WardensPort.blueprint.json"
write.parent.mkdir(parents=True, exist_ok=True)
with open(write, "w", encoding="utf-8", newline="\n") as f:
    json.dump({"TemplateCollectionSpec": {"CollectionId": "Buildings.WardensPort", "Blueprints": buildings}}, f, indent=2)
    f.write("\n")
print("wrote", write.relative_to(SRC.parent))

loc = SRC / "Localizations/enUS.csv"
import csv
existing = list(csv.reader(open(loc, encoding="utf-8", newline="")))
have = {r[0] for r in existing if r}
with open(loc, "w", encoding="utf-8", newline="") as fh:
    w = csv.writer(fh, lineterminator="\n")
    for r in existing:
        if r:
            w.writerow(r)
    added = 0
    for r in rows:
        if r[0] not in have:
            w.writerow(r)
            added += 1
print(f"buildings ported: {len(buildings)}, loc rows added: {added}")

# The workforce rule (tools/bot_workforce.py): Leaf Coats prices bots per building; the Wardens do not.
import bot_workforce  # noqa: E402
for p in bot_workforce.apply_tree(SRC):
    print("botified", p.relative_to(SRC).as_posix())
