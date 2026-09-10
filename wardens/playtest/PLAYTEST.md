# Playtesting the Wardens with an agent

Everything an agent needs is inside the mod: the Timberbot API (HTTP/WS) and an MCP server
that Claude Code connects to directly.

## One-time setup

1. Build + deploy: `dotnet build wardens/src/Wardens.csproj -c Release` (or install a packaged build:
   `python wardens/tools/package.py` after the build writes `dist/Wardens-v<version>.zip`, whose `README.txt`
   has the two copy steps).
2. In Timberborn's Mod Manager enable **The Wardens** and **disable Timberbot API** (the same
   code is compiled into the Wardens; two copies fight over port 8085).
3. New Game → faction **The Wardens**, tutorial toggle on → map **[Custom] Wardens 01 First Light** (the
   build installs it to `Documents/Timberborn/Maps`; any map works if it is missing). The Cold Boot
   cutscene plays (22 s, paused: letterbox, three captions, one orbit around the Core, Skip at the top
   right); the tutorial cards are bottom-right throughout and the game stays paused afterwards.
4. Save as settlement `Wardens`, save `smoke` so `tbot launch --settlement=Wardens --save=smoke` can
   reload it.

Claude Code: the repo's `.mcp.json` registers `wardens` (http://127.0.0.1:8090/mcp). Approve it once
when Claude Code asks; it connects whenever a game is loaded (the server starts with the game
context and stops on exit to the main menu).

## Every run

```bash
python wardens/playtest/mcp_smoke.py --say "hello from the smoke test"   # MCP: initialize, tools, status, chat
python wardens/playtest/mcp_concurrency.py                              # MCP: a ping answers while a frame long-poll waits
uv run --project python wardens/playtest/smoke.py                       # Timberbot API: faction/bots/needs
```

The Timberbot API refuses reads/writes until the ready gate is open: press **Launch** in the widget,
or call the MCP tool `timberbot_ready`.

## MCP tools

| Tool | Thread | What |
|---|---|---|
| `wardens_status` | main | faction, speed, bots/beavers + avg Energy, tutorial + chapter state, pointers, camera, ready gate |
| `tutorial` | main | `status`, or `next` to force the next stage of a tutorial id |
| `chapter` | main | `status`: every story chapter with its gating tutorial and per-building lock state; `unlock` forces `chapter_id` open |
| `campaign` | main | `status` (the level table, this map's level, completion, the next map), `ledger` / `record` (the cross-level memory in `campaign.json`), `complete` / `reset` (testing) |
| `ledger` | main | the Ledger in one call: poisoned, healed, green, archive, born, bots charged, up to five newly poisoned tiles, deltas since the previous call, and the formatted `line`; `action=record seen=...` also writes the entry to `campaign.json` |
| `frame` | listener | long-poll for the next sensor frame: every `every_ticks` game ticks or on an event (chat, day, building, chapter, birth, alert, selection); carries `attention` (where to look) |
| `manual` | listener | the Warden's playbook, `docs/WARDEN.md` from the mod folder |
| `point` / `unpoint` | main | highlight + bobbing arrow + toast on a tile, optional camera pan |
| `say` | main | message into the in-game WARDENS UPLINK panel (optional toast) |
| `chat_read` | listener | long-poll (≤120 s) for the player's next chat message |
| `chat_history` | listener | last N messages |
| `selection` | main | what the player has selected (their way of pointing at something) |
| `camera` | main | get / set / fly keyframes / stop |
| `cutscene` | main | `status` / `list` the scenes from `Cutscenes/*.json`, the running one and the story record; `play` `id` (default ColdBoot, replaces a running scene, ignores the trigger policy; `Archive` reads the Ledger back), `skip`, `continue`, `choose` `choice` (an open card), `reload` (edit in the mod folder, reload, play), `reset` (archive `story.json`) |
| `speed` | main | 0 pause … 3 |
| `timberbot` | listener | GET/POST passthrough to the compiled-in Timberbot API (loopback) |
| `timberbot_ready` | main | open the ready gate in-process |
| `timberbot_routes` | listener | route list |
| `dump_assets` | main | write loaded blueprints / materials / textures to `Documents/Timberborn/WardensDump` (for the Leaf Coats port; deliberately not inside `Mods/Wardens/`) |

Every tool result may carry `chat`: player messages not yet delivered to the agent.

## Conversation loop (how the agent plays with you)

The full loop is `wardens/WARDEN.md`: the agent reads it with `manual`, then lives on `frame`, which
wakes it every 60 game ticks or when something happens, and tells it where to look. The short form:

1. Agent calls `frame` (or `chat_read`, waits up to 20 s), you type in the panel and press Enter.
2. Agent answers with `say`, points with `point` when it talks about a place, acts through
   `timberbot` (`POST /api/building/place` etc.).
3. You point back by selecting something in the game; the agent reads it with `selection`.

## Checks for the tutorial

- `wardens_status` → `tutorial.active` shows `Wardens.ColdBoot.Wake` right after a new game, with
  `stages_left: 2`; no vanilla tutorial ids appear (they are neutered by the faction modifier).
- During the orbit, 3 badtide notification toasts + sounds fire at roughly 1.5 s/5.5 s/9.5 s
  ("archived" badtides, see design/wardens-chapter-1-plan.md), each dropping a line into the
  WARDENS UPLINK chat with a real logged duration. `chat_history` should show 3 "Archive: badtide
  N of 3" lines after the orbit finishes.
- Click Continue through the three Cold Boot cards (Wake, Badtides, Directive). `Wardens.Basics`
  follows (move, rotate, zoom,
  pause/unpause, speed 2 → 3 → 1), then `Wardens.Scrap`: "Place: Scavenger Flag (0/2)",
  "Build: Scavenger Flag (0/2)", "Connect: Scavenger Flag (0/2)", "Stock: Scrap Metal (0/10)".
- Then Badwater and Biomass (Sludge Pump, Charging Post + "Select a Warden" + "Charge: every Warden
  above 50% (n/5)", Reed Bed, "Plant: Sludge Reed (0/40)"), Working hours (18), Storage (Scrap Pile
  → Scrap Metal, 2 Sludge Tanks → Badwater, Crate Rack → Biomass), Science ("Build: The Cruncher",
  "Power: The Cruncher"), Pods (2 Breeding Pods), Power and pods (Badwater Cell, "Power: Breeding
  Pod"), First beaver ("Beavers: (0/1)"), Reforestation (accumulate 60 science, unlock + build the
  Planter Rig, "Plant: Birch (0/20)"), Maintenance (select a Warden, well-being panel, hours 16).
- Event tutorials: Dams (end of cycle 3 without a Dam), Vertical architecture (unlock Stairs, 70
  science), Layer tool (first Platform finished), Haulers (cycle ≥ 3 with 3 idle Wardens or 20 bots),
  Droughts / Badtides (after the first one ends).
- The building bar shows only the Wardens set (Core is pre-placed; Charging Post, Cruncher, Sludge
  Burner, Power Shaft, Scrap Pile, Reed Bed, Sludge Pump, Badwater Cell, Sludge Tank, two pods,
  Planter Rig (locked), Stairs (locked), Platform (locked), Dam, Crate Rack, Hauler Dock,
  Observation Deck, Scavenger Flag, Path). Placing the Cruncher next to the Core with a Power Shaft
  between them should light its illuminator (Core outputs 150 hp).
- Chapters (`wardens/README.md`, "How the chapters work"): on a new game the padlock sits on the
  Sludge Pump, Reed Bed, Sludge Tank, Crate Rack, Cruncher, both pods, Badwater Cell and Sludge
  Burner; `chapter status` shows `next: Badwater`, `next_waits_for: Wardens.Scrap`. Finishing the
  Scrap tutorial (or `tutorial next` through it) must clear the first four padlocks within a second,
  show the "Chapter 2: Badwater." toast and put the same line in the chat panel; the Badwater and
  Biomass tutorial then starts with its buildings buildable. Reloading the save keeps them unlocked
  and shows no toast. To test a later building out of order use `chapter unlock chapter_id=Signal`
  (or start with the tutorial off, which opens every chapter). `"chapterGating": false` in
  `settings.json` does the same for every game.
- `python wardens/tools/validate.py` must print `problems: none` before every in-game test (it includes
  `check_cutscenes.py`, which also runs alone and without the game's files:
  `python wardens/tools/check_cutscenes.py wardens/src`).
- The map (`design/wardens-wasteland.md`): the game may show an "older version" notice on load (the file
  claims 0.7.10 on purpose). Expect the Core on a flat pad with a dry basin east of it that the badwater
  from the north edge fills during the first day; ruin columns to the north-west and south of the Core;
  a pond with pines and birches on the hill in the north-east. If the map does not appear in the list,
  check `Documents/Timberborn/Maps/Wardens 01 First Light.timber` exists; if it fails to load, the log names
  the singleton or template, and `python wardens/tools/gen_map.py --check "<file>"` rules out the
  static causes.
- A Charging Post next to the Core, connected by a shaft, is what keeps the bots alive; build it first.
- The Forestry section must show plant buttons for the common trees (Planter Rig) and Sludge Reed
  (Reed Bed); a missing planter building for a plantable crashes the bottom bar at load.
- Power budget to verify: Core 150 hp; Charging Post 50; Cruncher 120; Badwater Cell +100 (needs a Sludge
  Pump on badwater and a bot working it); Sludge Burner +200 once Reed Beds deliver Biomass.

## Checks for cutscenes

Design and open questions: `design/wardens-cutscenes.md` (§9 is this list, §12 what the run answers).

- A new Wardens game (tutorial on): the letterbox comes up with the Uplink line "Cold Boot...", the
  three captions follow each other (orbit 8 s, push in 8 s, settle 6 s) with the dots filling, the
  camera orbits the Core once and ends where it started, the tutorial cards on the right stay clickable,
  and afterwards the game is paused but not locked (the speed buttons work). The log has
  `[Wardens] cutscenes: 1 loaded from ...` and `cutscene ColdBoot: start (trigger, 3 shots, 22 s)`.
- Skip at any point: the overlay goes, the camera stops where it is, the game is paused and unlocked.
- `cutscene status` while it plays shows `playing`, `id`, `shot`, `caption`, `waiting` (`flight`, `time`);
  `wardens_status.cutscene_played` is true afterwards; `frame` carries `cutscene` and the events
  `cutscene.start:ColdBoot` / `cutscene.end:ColdBoot`, with `attention[0].what == "cutscene"` while it plays.
- On a loaded save nothing plays; `cutscene play` replays it (on any faction, any map); edit
  `Documents/Timberborn/Mods/Wardens/Cutscenes/ColdBoot.json`, `cutscene reload`, `play`: the change shows.
  A broken edit lands in `errors` and the other scenes still load.
- Tutorial off, or `"cutscenes": false` in `settings.json`: nothing plays on a new game; `play` still works.
- `chapter unlock chapter_id=Badwater` opens the chapter and plays `Badwater`: two shots around the Core,
  the first caption with the scrap count filled in ("Scrap: 10. ..."), the second with a Continue button
  (Return or Space also continues); the camera flies back to where it was. Signal, Pods (three stock
  counts in the first caption) and Power the same way.
- `chapter unlock chapter_id=Green` plays `Green` (the camera on the first beaver, or the Core when there
  is none; "Day N. Born M.") and then `LevelEnd`: the level's end card with *Continue to Level 02* and
  *Stay*. A click writes `Documents/Timberborn/Mods/Wardens/story.json` (`choices.LevelEnd.end`, and
  `marks.birthday` from the Green scene), plays the matching closing shot, and `cutscene status` shows
  the record under `story`. `cutscene choose choice=stay` answers a card from the agent's side.
- `cutscene play id=Archive`: five cards with today's day and cycle, Wardens and beavers, Data Cores,
  science and scrap, the birthday mark and the level-end choice (`none` until they exist), then
  "Recorded."; Escape skips it (if the game's pause menu opens too, note it: the key goes, the button stays).
- `cutscene reset` archives `story.json` as `story.<timestamp>.json`; the next Green plays the card again.
- What to tune first: the zoom scale (`dzoom` ±0.15 is a placeholder), the tilt (`v` 60 and 35), whether
  the letterbox collides with the tutorial panel, and whether the caption is centered (the game's
  `text--centered` class) or needs a fixed width.

## Playtest findings (2026-09-10, Claude Code via the `wardens` MCP)

A session run entirely through the MCP loop (`manual` → `wardens_status` → `timberbot_ready` →
`frame`), on whatever map/save was already loaded (not confirmed to be Wardens Wasteland). Day 1
Cold Boot, 13 Wardens, 0 beavers, unpaused on player request. Placed a Scavenger Flag on the one
nearby Scrap Pile, got 20 ScrapMetal, placed a Charging Post beside the Core, set worker counts —
then the MCP/Timberbot connection dropped partway through night 1 and did not recover.

**The dropped connection** (traced by reading on 2026-09-10 and fixed the same day, iteration 04 WP2;
in-game confirmation pending): the MCP listener handled every request inline on its one thread, so a
pending `frame` or `chat_read` long-poll (up to 120 s) blocked every other call, the client's own `ping`
included, and a client request timeout then read as a dead server. Each request now runs on a pool
thread (`WardensMcpServer.ListenLoop`); `python wardens/playtest/mcp_concurrency.py` proves a `ping`
answers while a 20 s `frame` waits.

**Blocking bug — flooded spawn:** the ground around the Core carried a uniform ~0.1 water depth
(ambient/rain, not a real puddle), which was enough to flag both the **Core** and the nearby
**Scrap Pile** as `"Flooded."` in `/api/alerts`. A flooded Core never assigns workers: all 13 bots
stayed `unemployed` the whole session, so no construction, no scavenging, nothing progressed.
Energy fell 49% → 21% with no charging path — a fresh Cold Boot save can softlock itself before the
player ever gets a card to click. Needs checking against the map's terrain/drainage (see below) and
possibly a flood-immunity or higher threshold for Core/Scrap Pile specifically, since they're both
required for the very first moves.

**Timberbot API bugs found in this session:**

- Three findings with one cause (traced by reading on 2026-09-10 and fixed the same day, iteration 04
  WP1; in-game confirmation pending): the `/api/tiles` bound "dropped" whenever it was last in the URL,
  `limit`/`offset` "ignored" on `/api/gatherables` and `/api/beavers`, and `/api/buildings?name=ChargingPost`
  returning `total:0`. The `timberbot` MCP tool appended `?format=json` *after* a query the agent had
  put inside `path`, so the last parameter's value arrived as `55?format=json` and parsed as 0 (and the
  name filter as `ChargingPost?format=json`). The direct HTTP API never had the bug; the workaround of
  appending a trailing parameter only moved the corruption onto one nobody read. `WardensPure.BuildLoopbackUrl`
  now merges the two queries (`wardens/test/WardensPureTests.cs`). Confirm with
  `timberbot path=/api/tiles?x1=15&y1=40&x2=30&y2=55`: the result must reach x 30 and y 55, and
  `path=/api/beavers?limit=2&offset=2` must return a different page than `offset=0`.
- `/api/alerts` returns `type: "Flooded."` (with the period), not in the documented
  `unstaffed`/`unpowered`/`unreachable`/`status` enum.
- `chapter status`'s `complete` list already showed Badwater through Green on a brand-new Cold Boot
  save with nothing built — looks like state not reset per playthrough.

## What a map needs for this mod to work

- **The Core's footprint, and its immediate approach tiles, must sit above the flood line** — not
  just look dry. This session's spawn had ambient water pooling to ~0.1 depth across the whole area,
  which was enough to flood the Core itself. `design/wardens-wasteland.md` already describes the
  intended layout (Core on a flat pad, dry basin *east* for badwater to fill later, ruins to the
  north-west/south) — this needs verifying against the actual loaded terrain, since this session's
  map may not have been Wardens Wasteland.
- **At least one Scrap Pile reachable and dry within a few tiles of the Core** — First Light's
  opening move is a Scavenger Flag + scrap run; if the nearest ruin floods too, there's no path to
  the 5 ScrapMetal a Charging Post costs.
- **A buildable, unflooded, `nearPower` tile adjacent to the Core** for the Charging Post —
  `placement/find` returning a valid spot isn't sufficient if the tile floods after the fact.
- **Wet/dry contrast placed deliberately, not uniformly** — the Badwater Sump needs low/wet ground
  east of the Core; Pods and Green need dry ground for pods and planting. A map that's evenly damp
  everywhere (as this session's was) breaks the chapter progression rather than just being ugly.

## Known gaps

- Starting a **new game** from the API (faction + map + mode) is still missing.
- No screenshot endpoint yet; the agent sees the world through the read API only.
- Cutscenes: the game's UI stays visible under the letterbox; whether Escape reaches the overlay (and
  whether it also opens the pause menu) is unverified; the scenes' zoom and angles are untuned; Level 02
  does not exist, so *Continue to Level 02* only records the choice.
- The Core does not charge bots itself (no vanilla building combines a workplace with an
  attraction); the Charging Post does, at 50 hp from the Core.
- Sludge Reed skips the watered/contaminated components; whether growth needs soil moisture
  is unverified until a Reed Bed runs.

## Leaf Coats copies

After `python wardens/tools/import_leafcoats.py` the mod folder also carries Leaf Coats, its add-ons,
the Script Pack DLLs, Vertical Nav Mesh and Harmony (0Harmony.dll). Disable those seven workshop mods
in the mod manager, or two copies of the same assemblies load. To extract the bundle content for the port: load any game, then
`dump_assets what=blueprints filter=LeafCoats`, `what=materials filter=LeafCoats`,
`what=textures filter=LeafCoats max_count=400`; results land in `Documents/Timberborn/WardensDump/`.
