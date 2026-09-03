# Playtesting the Wardens with an agent

Everything an agent needs is inside the mod: the Timberbot API (HTTP/WS) and an MCP server
that Claude Code connects to directly.

## One-time setup

1. Build + deploy: `dotnet build wardens/src/Wardens.csproj -c Release`.
2. In Timberborn's Mod Manager enable **The Wardens** and **disable Timberbot API** (the same
   code is compiled into the Wardens; two copies fight over port 8085).
3. New Game → faction **The Wardens**, tutorial toggle on → any map for now. The Cold Boot
   orbit plays (14 s, paused), then the cards appear bottom-right.
4. Save as settlement `Wardens`, save `smoke` so `tbot launch --settlement=Wardens --save=smoke` can
   reload it.

Claude Code: the repo's `.mcp.json` registers `wardens` (http://127.0.0.1:8090/mcp). Approve it once
when Claude Code asks; it connects whenever a game is loaded (the server starts with the game
context and stops on exit to the main menu).

## Every run

```bash
python wardens/playtest/mcp_smoke.py --say "hello from the smoke test"   # MCP: initialize, tools, status, chat
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
| `point` / `unpoint` | main | highlight + bobbing arrow + toast on a tile, optional camera pan |
| `say` | main | message into the in-game WARDENS UPLINK panel (optional toast) |
| `chat_read` | listener | long-poll (≤120 s) for the player's next chat message |
| `chat_history` | listener | last N messages |
| `selection` | main | what the player has selected (their way of pointing at something) |
| `camera` | main | get / set / fly keyframes / stop |
| `cutscene` | main | replay the Cold Boot orbit |
| `speed` | main | 0 pause … 3 |
| `timberbot` | listener | GET/POST passthrough to the compiled-in Timberbot API (loopback) |
| `timberbot_ready` | main | open the ready gate in-process |
| `timberbot_routes` | listener | route list |
| `dump_assets` | main | write loaded blueprints / materials / textures to `Mods/Wardens/dump` (for the Leaf Coats port) |

Every tool result may carry `chat`: player messages not yet delivered to the agent.

## Conversation loop (how the agent plays with you)

1. Agent calls `chat_read` (waits up to 20 s), you type in the panel and press Enter.
2. Agent answers with `say`, points with `point` when it talks about a place, acts through
   `timberbot` (`POST /api/building/place` etc.).
3. You point back by selecting something in the game; the agent reads it with `selection`.

## Checks for the tutorial

- `wardens_status` → `tutorial.active` shows `Wardens.ColdBoot.Wake` right after a new game, with
  `stages_left: 1`; no vanilla tutorial ids appear (they are neutered by the faction modifier).
- Click Continue through the two Cold Boot cards. `Wardens.Basics` follows (move, rotate, zoom,
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
- `python wardens/tools/validate.py` must print `problems: none` before every in-game test.
- A Charging Post next to the Core, connected by a shaft, is what keeps the bots alive; build it first.
- The Forestry section must show plant buttons for the common trees (Planter Rig) and Sludge Reed
  (Reed Bed); a missing planter building for a plantable crashes the bottom bar at load.
- Power budget to verify: Core 150 hp; Charging Post 50; Cruncher 120; Badwater Cell +100 (needs a Sludge
  Pump on badwater and a bot working it); Sludge Burner +200 once Reed Beds deliver Biomass.

## Known gaps

- Starting a **new game** from the API (faction + map + mode) is still missing.
- No screenshot endpoint yet; the agent sees the world through the read API only.
- The Core does not charge bots itself (no vanilla building combines a workplace with an
  attraction); the Charging Post does, at 50 hp from the Core.
- Sludge Reed skips the watered/contaminated components; whether growth needs soil moisture
  is unverified until a Reed Bed runs.

## Leaf Coats copies

After `python wardens/tools/import_leafcoats.py` the mod folder also carries Leaf Coats, its add-ons,
the Script Pack DLLs, Vertical Nav Mesh and Harmony (0Harmony.dll). Disable those seven workshop mods
in the mod manager, or two copies of the same assemblies load. To extract the bundle content for the port: load any game, then
`dump_assets what=blueprints filter=LeafCoats`, `what=materials filter=LeafCoats`,
`what=textures filter=LeafCoats max_count=400`; results land in `Documents/Timberborn/Mods/Wardens/dump/`.
