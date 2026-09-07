# Timberbot

The mod is a pure server: HTTP for reads/writes on port 8085, plus a parallel WebSocket on port 8086 for state pushes and game events. Agents are driven by an out-of-process connector (`tbot watch`) gated by the in-game Launch button.

A C# mod + Python client that exposes a full read/write HTTP API for Timberborn plus a WebSocket event stream, enabling AI agents (Claude, ChatGPT, or custom scripts) to manage a beaver colony.

The repo also carries **The Wardens** (`wardens/`): a playable faction mod in which the AI is a character. Bots are the starting population, their wellbeing is Data, the building bar opens chapter by chapter as a story tutorial advances, the land is a generated wasteland, and an MCP server inside the game lets Claude Code play beside the human through a tick-driven `frame` heartbeat and a written playbook. The Wardens DLL compiles the Timberbot API in verbatim, so the two mods are never enabled together.

## Read First

Beyond this file:

- [`openapi.yaml`](openapi.yaml) — canonical HTTP contract
- [`docs/websocket-protocol.md`](docs/websocket-protocol.md) — canonical WS wire contract (envelope, auth, reconnect, message types)
- [`docs/api-reference.md`](docs/api-reference.md) — human-readable companion to the OpenAPI spec
- [`docs/architecture.md`](docs/architecture.md) — thread model, server split, write-job queue
- [`docs/devenv.md`](docs/devenv.md) — toolchain (.NET, Python, `ilspycmd`)

When touching `wardens/`:

- [`wardens/README.md`](wardens/README.md) — what the faction mod is, file map, how the tutorial and the chapters work, the map, the art
- [`design/faction-wardens.md`](design/faction-wardens.md) — the arc and the two C# spikes; [`design/wardens-chapter-1-plan.md`](design/wardens-chapter-1-plan.md) — Chapter 1 design with status notes
- [`design/wardens-play.md`](design/wardens-play.md) — how the Warden (the agent) plays: the stance, the Ledger, frames, the camera policy; [`wardens/WARDEN.md`](wardens/WARDEN.md) — the playbook the agent follows in-game
- [`design/wardens-wasteland.md`](design/wardens-wasteland.md) — the shipped map and the `.timber` file format; [`design/wardens-campaign-maps.md`](design/wardens-campaign-maps.md) — research: how a campaign mod installs, selects and switches maps (one per level), what is verified and what the decompile must confirm; [`design/wardens-campaign-design.md`](design/wardens-campaign-design.md) — the campaign system design (components, `campaign.json`, `ILevelStarter`, rollout); [`design/wardens-campaign-concept.md`](design/wardens-campaign-concept.md) — the five-level campaign concept (the essential cut); [`design/wardens-campaign-arc.md`](design/wardens-campaign-arc.md) — the full ten-level arc (brainstorm, storyform, requirements brief); [`design/wardens-campaign-research-plan.md`](design/wardens-campaign-research-plan.md) — the research tracks and assumption register the arc still needs; [`design/wardens-campaign-story.md`](design/wardens-campaign-story.md) — the story as the game tells it: every card, line, Archive entry and reading, level by level, in the Wardens' voice; [`wardens/playtest/PLAYTEST.md`](wardens/playtest/PLAYTEST.md) — the in-game checklist and the MCP tool table; [`design/wardens-cutscenes.md`](design/wardens-cutscenes.md) — the cutscene system: the scene format (`Cutscenes/*.json`), the runner, the overlay, the triggers, the checker, and what the first run must answer

## Quick Reference

- **Build:** Open `timberbot/src/Timberbot.csproj` in an IDE with .NET support, or run `dotnet build` from that directory. The post-build target auto-deploys to the game's mod folder. Override the game DLL path with `-p:GameManagedDir=<path>` if the default doesn't match your install.
- **Run (game side):** Launch Timberborn with the mod enabled. The HTTP server starts on `httpPort` (default `8085`) and the WebSocket server on `wsPort` (default `8086`). Player presses **Launch** in the widget to open the ready gate.
- **Run (client side):** `tbot watch` is the long-running connector — it opens a single WebSocket to the mod. `tbot serve` is the Telegram bot mode — spawns an in-process MCP server (`127.0.0.1:8091` default) and routes agent output to Telegram (requires `TBOT_TELEGRAM_TOKEN` or `[serve.telegram].token` in `config.toml`; needs `pip install 'timberbot[serve]'`). `tbot listen` is a pure WS client for the game-event stream. `tbot <command>` and `tbot agent run` still work for one-shots. Install with `pipx install timberbot`.
- **Tests:** Python unit tests via `python -m pytest python/tests/`; C# xUnit tests via `dotnet test timberbot/test/`.
- **Build (Wardens):** `dotnet build wardens/src/Wardens.csproj -c Release`. Every build bumps the patch version (`wardens/tools/bump_version.py`) and deploys to `Documents/Timberborn/Mods/Wardens`, copies the API docs and `WARDEN.md` into its `docs/`, and installs `Maps/*.timber` into `Documents/Timberborn/Maps`. Uses the git-ignored `wardens/src/Directory.Build.props` for the game path, or `-p:GameManagedDir=… -p:ModDir=… -p:MapsDir=…`.
- **Release (Wardens):** `python wardens/tools/package.py` zips a local Release build into `dist/Wardens-v<version>.zip` (the mod folder, the map for `Documents/Timberborn/Maps`, install steps; `--list` previews); `python wardens/tools/bump_version.py --minor` marks a milestone and `wardens/CHANGELOG.md` records it; `python wardens/tools/gen_thumbnail.py --check` keeps the Mod Manager tile current.
- **Run (Wardens):** enable **The Wardens** in the Mod Manager and **disable Timberbot API** (same code, same ports). New Game → The Wardens, tutorial on, map *[Custom] Wardens Wasteland*. Claude Code connects through `.mcp.json` (`http://127.0.0.1:8090/mcp`); the agent reads `manual` and lives on `frame`. Playtest scripts: `python wardens/playtest/mcp_smoke.py`, `uv run --project python wardens/playtest/smoke.py`.
- **Static checks (Wardens):** `python wardens/tools/validate.py` (every name the game resolves at load, the chapter table against the blueprints, the cutscene files; needs the game's `Blueprints.zip`) and `python wardens/tools/gen_map.py --check "wardens/src/Maps/Wardens Wasteland.timber"` must both print `problems: none` before an in-game test. `python wardens/tools/check_cutscenes.py wardens/src` is the cutscene part alone and needs no game files; `uv run --project python --extra dev pytest wardens/tools` runs the tool tests (the checker, the packager). No CI covers the C# in `wardens/`; the tool tests are a step of the Python workflow, which on this fork does not run because GitHub Actions is off.

## Architecture

```
┌─ Timberborn (game process) ────────────────┐         ┌─ tbot watch (host process) ────────────┐
│  Timberbot.dll                             │         │                                        │
│    TimberbotHttpServer       :8085         │◀─HTTP──▶│  REST reads/writes (one-shots)         │
│    TimberbotWebSocketServer  :8086         │◀── WS ─▶│  long-lived ws://host:8086/api/ws      │
│    TimberbotAgentState  state.json         │         │  receives state + event frames         │
│    TimberbotReadV2 / TimberbotWrite        │         │  sends `heartbeat` every 30 s          │
│    TimberbotPanel       Launch / Stop      │         │  dispatches `tbot agent run` per cycle │
└────────────────────────────────────────────┘         └────────────────────────────────────────┘
```

The mod runs **inside** the Unity game process. It uses Timberborn's `Bindito` DI framework (not BepInEx or Harmony). All game DLLs are referenced with `Publicize="true"` to access internal APIs without reflection.

### Agent connector role

The mod no longer spawns the agent. Instead, the player runs `tbot watch` — a long-running Python process that:

1. Opens a single WebSocket to `ws://host:wsPort/api/ws` with exponential backoff until the game is reachable.
2. Receives `state` frames (full agent state on every change) and `event` frames (game events) push-style — no polling.
3. Sends a `heartbeat` frame every 30 s carrying `{version, agent_status, acked_request_id}`. WS ping/pong + TCP keepalive handle liveness.
4. Dispatches an `agent run` cycle when a new `pendingRequest` arrives via `state` frame, or when autonomous-mode cadence fires.
5. Sends the next `heartbeat` with an advanced `acked_request_id` so the mod clears the single `pendingRequest` slot.

`tbot watch` is the canonical place to add new orchestration logic (queueing, cadence, attach-to-`opencode serve`, multi-backend routing). The mod intentionally stays dumb.

### The Wardens: the agent inside the game

The Wardens invert the connector model. The Wardens DLL (`wardens/src/`) hosts an MCP server (Streamable HTTP, JSON-RPC 2.0, `127.0.0.1:8090/mcp`, `WardensMcpServer.cs`) next to a verbatim copy of the Timberbot HTTP/WS servers, so Claude Code talks to the running game directly:

```
┌─ Timberborn (game process) ── Wardens.dll ─────────────────┐        ┌─ Claude Code ───────────────────┐
│  WardensMcpServer      :8090/mcp   tools/call queued to     │◀─MCP──▶│  .mcp.json → wardens            │
│                                     the main thread          │        │  manual → WARDEN.md (playbook)   │
│  WardensFrames         ITickableSingleton: a sensor frame    │        │  frame  → where to look, in order│
│                        per N ticks or per event, long-polled │        │  say / point / camera / chapter  │
│  WardensChat           WARDENS UPLINK panel (player ↔ agent) │        │  timberbot → HTTP passthrough    │
│  WardensChapterService tutorial progress unlocks the bar     │        └─────────────────────────────────┘
│  Timberbot/*           HTTP :8085 + WS :8086, compiled in    │
└─────────────────────────────────────────────────────────────┘
```

Tools that touch game state run on the main thread (`WardensMcpServer.UpdateSingleton` drains a queue, like `TimberbotService`); I/O-only tools (`chat_read`, `frame`, `manual`, `timberbot`) answer on the listener thread. The full tool table is in `wardens/playtest/PLAYTEST.md`; the division of labour between the human (purpose) and the agent (logistics, the record) is `design/wardens-play.md`.

### Ready gate

The widget's Launch / Stop button toggles `ready` on the mod. While `ready=false`, `TimberbotHttpServer` middleware returns `409 game_not_ready` for **every `/api/*` read and write** except the carve-out: `/api/agent/*`, `/api/ready`, `/api/ping`. The WebSocket on port 8086 is **not** ready-gated — clients stay connected across Launch / Stop toggles and continue to receive game-event frames (they just won't see anything useful happen on `state` frames until the player presses Launch).

`ready` is **not persisted**: it resets to `false` on every save load. The player has to opt in every session. `mode`, `goal`, and `lastError` persist via `state.json`; `pendingRequest` and `lastAckedRequestId` are in-memory only.

Bearer-token auth (`authToken` in `settings.json`) layers on top: when set, every `/api/*` request needs `Authorization: Bearer <token>` (constant-time compare), and every WS upgrade needs the same token (either `Authorization: Bearer <token>` on the upgrade headers or `?token=<token>` as a query-param fallback). The mod refuses to start if `listenAddress` is non-localhost and `authToken` is empty.

## Project Structure

```
timberbot/
├── docs/                        # Documentation (deployed with mod)
│   ├── api-reference.md         # Full API contract — read this for endpoints
│   ├── timberbot.md             # AI agent boot guide
│   ├── features.md              # Feature matrix
│   ├── getting-started.md       # Install & setup
│   └── architecture.md          # Internal design
├── design/                      # Design proposals + historical investigations
│   ├── automation-plan.md       # Plan for automation wiring extension
│   ├── automation-states.md     # Disambiguation reference for agents
│   └── …                        # Other design docs and implementation notes
├── agents/
│   └── beaver-developer.md      # Dev-agent prompt for working on this codebase
├── .mcp.json                    # Claude Code → the Wardens' in-game MCP server (127.0.0.1:8090/mcp)
├── wardens/                     # The Wardens faction mod (see wardens/README.md for the full file map)
│   ├── README.md                # What it is, file map, tutorial + chapters, cutscenes, the map, the art
│   ├── CHANGELOG.md             # Version history (0.3.0: cutscenes, the release path)
│   ├── WARDEN.md                # The agent's playbook (deployed to the mod's docs/, served by `manual`)
│   ├── src/                     # Blueprints (Factions, Buildings, Tutorials, Needs, Goods, Recipes, Localizations),
│   │   │                        #   Maps/ (the generated wasteland), Sprites/ + Materials/ (recolored art),
│   │   │                        #   Cutscenes/ (scene files, the Cold Boot among them)
│   │   ├── Wardens*.cs          # Configurator, StartingPopulation, CameraDirector, Pointer, Chat,
│   │   │                        #   Cutscenes (+Script, +Overlay), McpServer + McpTools, Chapters, Frames,
│   │   │                        #   Triggers, tutorial steps, AssetDump
│   │   ├── PollutingBuilding.cs # Spike B stub (building-side contamination; water route planned)
│   │   ├── Timberbot/           # Verbatim copy of timberbot/src (paths point at Mods/Wardens)
│   │   └── Wardens.csproj       # Build + deploy (mod folder, docs/, Maps); bumps the version every build
│   ├── tools/                   # Generators and checks: gen_buildings.py, gen_tutorial.py, gen_map.py,
│   │                            #   gen_thumbnail.py, validate.py, check_cutscenes.py, package.py (+ their
│   │                            #   pytests), bump_version.py, recolor_assets.py, import_leafcoats.py
│   └── playtest/                # PLAYTEST.md (checklist + MCP tool table), smoke.py, mcp_smoke.py
├── timberbot/
│   ├── src/                     # C# mod source
│   │   ├── Timberbot.csproj     # MSBuild project; manages game DLL refs & deploy
│   │   ├── TimberbotConfigurator.cs      # Bindito DI registration
│   │   ├── TimberbotHttpServer.cs        # HTTP listener, routing, ready-gate + auth middleware (port 8085)
│   │   ├── TimberbotWebSocketServer.cs   # WebSocket listener (port 8086): state + event broadcasts, heartbeat
│   │   ├── TimberbotReadV2.cs            # All GET endpoints (buildings, beavers, map)
│   │   ├── TimberbotWrite.cs             # All POST endpoints (pause, recipes, floodgates)
│   │   ├── TimberbotPlacement.cs         # Building/planting placement logic
│   │   ├── TimberbotAgentState.cs        # mode/goal/ready/pendingRequest container; state.json persistence; Changed event
│   │   ├── TimberbotEntityRegistry.cs    # Entity lookup by ID
│   │   ├── TimberbotEvents.cs           # [OnEvent] handlers that hand game events to the WS broadcaster
│   │   ├── TimberbotService.cs           # Main lifecycle (Load/Update); owns both listeners
│   │   ├── TimberbotPanel.cs             # In-game UI panel (Launch/Stop, mode dropdown)
│   │   ├── TimberbotDebug.cs             # Debug/diagnostic endpoints
│   │   ├── manifest.json                 # Mod metadata (name, version, min game version)
│   │   └── settings.json                 # Default config (port, listen address, authToken)
│   └── test/                    # C# xUnit tests (Tier 1+2 pure helpers)
├── python/
│   ├── pyproject.toml           # hatchling build; `tbot` console script
│   ├── src/timberbot/           # Python package source
│   │   ├── api/                 # TimberbotClient + Pydantic models
│   │   ├── cli/                 # `tbot` CLI: global-flag parser + python-fire `Tbot` class wrapping every client method as a subcommand
│   │   ├── agent/               # Pluggable backends + runner
│   │   ├── agent_prompts/       # Runtime prompts shipped as package data
│   │   ├── formatters/          # Map, dashboard, table renderers
│   │   ├── game_mcp/            # MCP server wrapping TimberbotClient as tools
│   │   ├── user_api/            # Telegram adapter + SessionManager for tbot serve
│   │   └── connector/           # WS connector shared by watch + serve
│   └── tests/                   # Unit + contract + integration suites
├── openapi.yaml                 # Single source of truth for the HTTP contract
├── mkdocs.yml                   # MkDocs config for documentation site
└── README.md
```

## Key Conventions

### C# Mod Side
- **DI framework:** Bindito, not Unity's built-in. Register services in `TimberbotConfigurator.cs` with `Bind<T>().AsSingleton()`.
- **Game DLL access:** Add references in `Timberbot.csproj` with `Publicize="true"` and `<Private>false</Private>`. Never ship game DLLs.
- **Thread safety:** HTTP requests arrive on a background thread. All game state mutations must be dispatched to the main thread via `ITimberbotWriteJob` queue pattern.
- **Entity lookup:** Use `TimberbotEntityRegistry` to find entities by integer ID. The registry is populated on game load.
- **State reading:** `TimberbotReadV2.cs` serializes game state to JSON. It uses `GetComponent<T>()` on entities to extract data from Timberborn's ECS-like component system.
- **State writing:** `TimberbotWrite.cs` processes mutations. Each write method finds the target entity, gets the relevant component, and calls the game's own setter methods.
- **Agent state:** `TimberbotAgentState` is the only source of truth for `mode`, `goal`, `ready`, `pendingRequest`, `lastAckedRequestId`, `lastError`. Reads/writes go through the container — don't add ad-hoc fields elsewhere. Mutations raise the `Changed` event (outside the lock) so the WS broadcaster can fan a `state` frame out to every subscriber.
- **Ready gate:** new `/api/*` endpoints must explicitly opt into the carve-out (`/api/agent/*`, `/api/ready`, `/api/ping`) or accept that they 409 when `ready=false`. Default is gated. The WebSocket on port 8086 is not ready-gated — events keep flowing.

### Python Client Side
- **Persistent state:** The agent uses `brain.toon` files in `memory/` subdirectories, keyed by settlement name. The `goal` parameter is saved here for cross-session persistence.
- **CLI pattern:** `tbot <command> [args]`, dispatched via [python-fire](https://github.com/google/python-fire). Pass arguments either positionally (`tbot set_speed 3`) or as flags (`tbot set_speed --speed=3`, `tbot place_building --prefab=Path --x=120 --y=130 --z=2`). Hyphens and underscores are interchangeable in flag names. Global flags (`--json`, `-v`/`--verbose`, `--debug`, `--host=`, `--port=`, `--auth-token=`) are stripped *before* Fire sees argv. `-v` logs the resolved endpoint + each HTTP request to stderr; `-vv` / `--debug` also logs request/response bodies. `TBOT_DEBUG=1` env var forces DEBUG when an agent is shelling out to `tbot`. Use `tbot <command> --help` to see per-command positional/flag layout (Fire renders the typed signature).
- **Sequential mutations:** Always run mutating game API calls sequentially, never in parallel.
- **Boot flow:** Run the `brain` command once at session start to establish settlement context.

### Wardens Side
- **Generators are the source.** `tools/gen_buildings.py` writes the building blueprints from the game's `Blueprints.zip`, `tools/gen_tutorial.py` writes the 18 tutorials and their loc rows, `tools/gen_map.py` writes the map. A hand edit to a generated file must be mirrored in its generator (the chapter padlock costs are the precedent: both the blueprints and `CHAPTER_LOCK` in the generator carry them). The generators need the game's files (`Blueprints.zip` at the path in each script); `gen_map.py` does not.
- **Chapters.** The chapter table lives once, in `WardensChapters.cs`; gated buildings ship with `ScienceCost: 999999` and `validate.py` fails if the table and the blueprints disagree, if a gating tutorial does not exist, or if a chapter lacks its `Wardens.Chapter.<Id>.Title/.Unlocked` loc rows. Never gate the Charging Post (power is life).
- **Every name the game resolves at load is a crash risk.** Template names in tutorial steps, loc keys, stage ids, planter groups, illumination colors. Run `validate.py` after any blueprint or generator change; every crash so far was a name.
- **Game-context singletons only.** `WardensConfigurator` is `[Context("Game")]`; faction-specific behaviour checks `FactionService.Current?.Id == "Wardens"` at runtime, the MCP server, chat, pointer, camera and frames load for every faction. Prefer APIs already used somewhere in `wardens/src` or `timberbot/src`: nothing here is compiled in CI, so an unproven signature is found only by the next local build.
- **Threading in the MCP server.** Tools touching game state are queued to the main thread; `OffThread` tools must be pure I/O. Long-polls (`chat_read`, `frame`) wait on a lock the main thread pulses; never block the main thread.
- **The agent's contract.** The server's initialize `instructions`, `WARDEN.md` and `design/wardens-play.md` must agree: the agent reads `manual`, lives on `frame`, answers `chat` first, follows `attention` in order, borrows the camera only as the playbook allows (never while a cutscene plays), and keeps the Ledger. Change one, change all three.
- **Cutscenes are data.** A scene is `src/Cutscenes/<Id>.json` (`design/wardens-cutscenes.md` §3 is the format; `WardensCutsceneScript.cs` parses it, `WardensCutscenes.cs` plays it); its captions are `Wardens.Cutscene.<Scene>.<Shot>` rows in `Localizations/enUS.csv` (both generators keep rows they do not own). Never add a second runner or pause/lock the speed for a scene elsewhere; a new story moment is a new file plus its loc rows, checked by `check_cutscenes.py`. Tune with the game running (`cutscene reload` / `play`) and copy the file back.
- **The Timberbot copy.** `wardens/src/Timberbot/` is a verbatim copy of `timberbot/src/`; fix Timberbot bugs upstream and re-copy, do not fork them in the copy.

### Documentation
- `docs/timberbot.md` — primary AI-agent operating guide. Read this if you're touching the in-game agent behavior or the prompts.
- `docs/events.md` — user-facing guide for consuming the WS event stream.
- `python/src/timberbot/agent_prompts/timberbot.md` — system prompt shipped as `timberbot` package data and injected at runtime by `tbot agent run`.

`openapi.yaml`, `docs/websocket-protocol.md`, `docs/api-reference.md`, and `docs/architecture.md` are listed in [Read First](#read-first) above — they're load-bearing for any contract or threading change.

## Agent Tooling

### Developer-Agent Prompt

The repo ships [`agents/beaver-developer.md`](agents/beaver-developer.md) as a primary prompt for AI coding agents (Claude Code, Codex, etc.) working on this mod. It enforces the subagent-delegation pattern, the read-first list, and build/verify discipline.

### Understudy (optional in-game verification)

For behavioral changes where `dotnet build` and the Python smoke tests aren't enough, agents may install [Understudy](https://github.com/impuls42/understudy) — a Claude Code skill that runs Timberborn headless under `gamescope`/`sway`, injects synthetic input, and captures screenshots. Once installed per its README (`uv sync`, then `us stack install`), the agent can launch the game, exercise an endpoint, and verify the result without a human in the loop. Use it when the question is "did the game actually behave the way I expected?" — not as a replacement for unit tests.

## Game DLL Paths

The project references Timberborn game DLLs via the `$(GameManagedDir)` MSBuild property in `Timberbot.csproj`. All game DLLs use `Publicize="true"` and `<Private>false</Private>` — they are never shipped with the mod.

**Default paths (auto-detected by OS):**

| Platform | Default Path |
|----------|-------------|
| Linux | `~/.local/share/Steam/steamapps/common/Timberborn/Timberborn_Data/Managed/` |
| Windows | `C:\Games\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\` |

**Override at build time:**
```
dotnet build -p:GameManagedDir=/path/to/Timberborn/Timberborn_Data/Managed
```

**Key automation DLLs** (referenced with `Publicize="true"`):
- `Timberborn.Automation.dll` — Core wiring system (`Automator`, `Automatable`, `AutomatorConnection`)
- `Timberborn.AutomationBuildings.dll` — Sensor/relay/memory/timer/lever components (`Relay`, `Memory`, `DepthSensor`, etc.)

To inspect the API surface, decompile the DLLs locally with `ilspycmd` (see `docs/devenv.md`).

## Game Version Compatibility

- **Minimum game version:** 1.0.0.0 (set in `manifest.json`)
- **Automation system:** Introduced in Timberborn 1.0 (the full release, not an early access update). All automation buildings (sensors, relays, memory, timers, levers, gates) and the wiring system (`Automator`/`Automatable`/`AutomatorConnection`) are 1.0 features.
- **Modding framework:** The game uses `BaseComponent` (no longer inherits from MonoBehaviour as of 1.0). Components must implement interfaces like `IAwakableComponent` for lifecycle hooks.

## Current Limitations & Planned Work

The mod currently does **not** support:
- Reading automation wiring connections (which sensor is connected to which building input) — only the `inputName` of the connected transmitter is shown in building state
- Configuring automation components that lack a public setter (e.g., some sensor thresholds that are read-only at runtime)

See `design/automation-plan.md` for the full implementation plan with decompiled API surface from `Timberborn.Automation.dll` and `Timberborn.AutomationBuildings.dll`.

### The Wardens: state on 2026-09-06 (v0.3.0)

Built and deployed once (the v0.2 batch: faction, tutorial line, in-game MCP, art) but **not yet verified in-game**; everything after that is **not yet compiled anywhere**, because the development environment had no game install. v0.3.0 adds the release path: a local Release build plus `python wardens/tools/package.py` produces the ZIP, and the Mod Manager shows `thumbnail.png`. In order of what the next local build and a twenty-minute smoke run should answer:

1. `Wardens.dll` compiles. The only game APIs not already used elsewhere in the repo are `BuildingUnlockingService.UnlockIgnoringCost` (chapters) and `ITickableSingleton` (frames); both are named in the design docs from the 1.1.2.4 decompile.
2. The map loads: `Wardens Wasteland.timber` claims game version 0.7.10.0 on purpose so the game's migration runs; open questions (does 1.1 migrate that layout, do `RuinColumnH*` and `UndergroundRuins` still exist, is the Sump deep enough for the Sludge Pump, does a mod's `Maps/` folder get listed) are in `design/wardens-wasteland.md`.
3. Chapter gating: padlocks on a new game, the Badwater toast after the Scrap tutorial, no toast on reload.
4. Frames: `frame` returns within `every_ticks` ticks while unpaused, at once on chat, and carries `attention`.
5. Cutscenes (2026-09-06, not compiled anywhere): the Cold Boot plays as `Cutscenes/ColdBoot.json` through the new runner (letterbox, three captions, Skip); `design/wardens-cutscenes.md` §12 lists what the run must answer (the zoom scale, the letterbox against the tutorial panel, the `text--centered` class). No game API in it is new to the repo; `Length.Percent` is the one UI Toolkit style not used before.

Stubs and planned work, in the order `design/wardens-play.md` argues for: a native `ledger` tool (soil contamination counts), the Archive persisted in the save (`ISaveableSingleton`), frames on the Timberbot WebSocket for out-of-process agents, a new game from the API (`design/playtest-and-video-capture.md`), a screenshot tool, and Spike B (`PollutingBuilding`, water route). Out of scope for now: remediation tech, the Ark, custom 3D art.

## External References

- Game automation guide: https://timberborn.org/articles/automation-guide
- Official modding tools: https://github.com/mechanistry/timberborn-modding/wiki
- Timberborn uses Unity Engine 6000.3.6f1 as of 1.0
