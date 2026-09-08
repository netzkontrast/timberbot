# Appendix C — Python layer inventory (REST client, MCP server, agent loop, tests)

> Generated during the Phase 0 audit by a read-only sub-audit over `python/`, spot-checked by the main audit. Anchors are `file:line` relative to `python/` at commit `d21988a`. Where this appendix and `docs/audit/00-repo-audit.md` disagree, the main audit wins.

## 1. MCP server

### 1.1 Framework and versions (from `uv.lock`)

| Package | Version | sdist `upload-time` in lock | Declared |
|---|---|---|---|
| `fastmcp` | **3.3.1** | 2026-05-15 (`uv.lock:700`) | `pyproject.toml:44,50` (`fastmcp>=2.0`) |
| `mcp` (official SDK, transitive) | **1.27.1** | 2026-05-08 (`uv.lock:1344`) | `uv.lock:1325-1327` |
| `agent-client-protocol` (ACP) | **0.10.1** | 2026-05-24 (`uv.lock:18`) | `pyproject.toml:43,49` |
| `anthropic` / `openai` / `claude-agent-sdk` | **absent** | — | no LLM SDK dependency; the layer spawns agent CLIs |

Both MCP packages predate spec revision 2026-07-28 by ~2.5 months. **UNVERIFIED:** which protocol-revision string they negotiate (packages not installed here; `uv.lock` records no protocol constant). Verify in Phase 1 with `python -c "import mcp.types as t; print(t.LATEST_PROTOCOL_VERSION)"` after `uv sync --extra dev`.

### 1.2 Transport — SSE, stateful, hardcoded

- Sole production start: `src/timberbot/user_api/serve.py:746-751` → `mcp.run_http_async(transport="sse", host=cfg.mcp_host, port=cfg.mcp_port, show_banner=False)`.
- Constructor `src/timberbot/game_mcp/server.py:102-112`: `FastMCP("timberbot-game", instructions=...)` — no `stateless_http`, no `json_response`, no auth, no `mask_error_details`.
- ACP client-side server spec: `src/timberbot/user_api/serve.py:399-410` → `{"type": "sse", "name": "game", "url": "http://host:port/sse"}`.
- Defaults `mcp_host="127.0.0.1"`, `mcp_port=8091` (`user_api/serve.py:51-52`; CLI override `cli/commands/serve.py:149-150`).
- stdio is never used for the game server (only as the ACP agent subprocess pipe, `connector/session.py:440-441`). Streamable HTTP never selected; `_to_mcp_server` has an unused `HttpMcpServer` branch (`connector/session.py:41-50`).
- No `Mcp-Session-Id` / `stateless` / MCP `session_id` handling anywhere in `game_mcp/` (grep: 0 hits).

### 1.3 Tools — 61 game tools + 9 delegation tools, zero annotations

All registrations use bare `@mcp.tool` — **no `annotations`, `output_schema`, `title`, `tags`, `meta`** anywhere (grep for `readOnlyHint|destructiveHint|idempotentHint|openWorldHint|outputSchema|output_schema|ttlMs` over `src/` → 0 hits). Every game tool takes `cursor: int = 0` and returns `{result, meta}` via `_make_envelope` (`game_mcp/server.py:45-77`); `meta` carries drained WS events (`meta.events`, ring buffer 256, `game_mcp/bus.py:78-79`).

Read tools (`game_mcp/server.py`): `observe` `:121`, `summary` `:134`, `time` `:141`, `weather` `:148`, `population` `:155`, `resources` `:162`, `districts` `:169`, `buildings` `:176`, `trees` `:198`, `crops` `:217`, `gatherables` `:236`, `beavers` `:255`, `workhours` `:277`, `science` `:284`, `wellbeing` `:291`, `notifications` `:298`, `alerts` `:305`, `distribution` `:312`, `prefabs` `:319`, `power` `:326`, `speed` `:333`, `tree_clusters` `:340`, `food_clusters` `:347`, `find_placement` `:354`, `find_planting` `:377`, `building_range` `:397`, `brain` `:404` (writes local disk), `list_locations` `:413`.

Write tools: `set_speed` `:424`, `pause_building` `:431`, `unpause_building` `:438`, `set_priority` `:445`, `set_haul_priority` `:456`, `set_recipe` `:467`, `set_farmhouse_action` `:476`, `set_plantable_priority` `:487`, `set_workers` `:498`, `set_floodgate` `:507`, `set_workhours` `:518`, `set_distribution` `:527`, `set_storage` `:545`, `set_clutch` `:556`, `unlock_building` `:567`, `migrate` `:576`, `place_building` `:592`, `demolish_building` `:610`, `demolish_crop` `:619`, `mark_trees` `:628`, `clear_trees` `:644`, `plant_crop` `:660`, `clear_planting` `:677`, `place_path` `:693`, `link` `:716`, `unlink` `:730`, `configure_automation` `:739`, `rename_automation` `:750`, `set_location` `:765`, `remove_location` `:781`, `add_task` `:790`, `update_task` `:799`, `complain` `:813` (side-effect into Telegram via `on_complaint` `:834-835`).

Delegation tools (`game_mcp/delegation.py`, registered only when `broker is not None`, `server.py:842-843`; no `cursor`, no `{result, meta}` envelope, in-band `{"error": ...}` dicts): `delegate` `:280`, `subagent_reply` `:365`, `subagent_status` `:437`, `subagent_wait` `:452` (blocks ≤60 s), `subagent_cancel` `:501`, `subagent_close` `:512`, `subagent_list` `:523`, `subagent_wait_all` `:531` (blocks), `subagent_transcript` `:582`.

**Missing tools:** no `map`, no `tiles` — the MCP agent has no spatial read (`docs/timberbot.md:162` tells it to use both).

### 1.4 Confirmation / elicitation / sampling / resources

- No `ctx.elicit`, no `Context` parameter, no sampling in `game_mcp/`.
- Approval lives outside MCP in the ACP client: `connector/session.py:206-217` auto-answers from a static allowlist (`_tool_allowed`, `session.py:93-110`); default `["game.*"]` (`user_api/serve.py:56`, `cli/commands/serve.py:156`) → **everything auto-approved, including `demolish_building`**.
- Legacy elicitation extension is dead (`connector/protocol.py:7-13`); handler kept at `connector/session.py:219-224`, Telegram keyboard at `user_api/telegram/keyboards.py:6`, `bot.py:161-162`.
- No `@mcp.resource` / `@mcp.prompt` anywhere. Server `instructions=` string at `game_mcp/server.py:104-111` is the only prompt surface.

### 1.5 How the server reaches the mod

- HTTP via `TimberbotClient` (`api/client.py:75-101`), default `127.0.0.1:8085`; every tool offloads the blocking `requests` call with `loop.run_in_executor(None, ...)` (e.g. `server.py:136-138`).
- WebSocket inbound-only: `TimberbotWsClient` → `ws://host:8086/api/ws` (`api/wsclient.py:73,103`); `EventIngestor.run()` (`game_mcp/ingest.py:29-43`) feeds the `EventBus` ring buffer.

## 2. Agent loop

Two separate paths, neither an in-process Reason→Act→Observe loop:

1. **`agent/runner.py` — one-shot CLI launcher** (`run_agent`, `:106-198`): resolve backend (`:145-166`), `client.ping()` gate (`:169-171`), `client.brain(goal)` → colony state JSON (`:48-57`), write merged `agent-instructions.md` (`agent/prompts.py:49-65`), `subprocess.run` (`agent/backend.py:57-75`). Observation is injected once into the system prompt; no loop, no tick, no feedback.
   Backends: `claude` (`agent/backends/claude.py:8-19`), `codex` (`codex.py:13-27`), `opencode` (`opencode.py:32-56`, optional `--attach <url>`), `custom` (`custom.py:23-42`) — all subprocess argv builders (`agent/backend.py:99-118` registry).
2. **ACP path (`tbot serve`)**: `ACPConnector.connect` spawns `claude-agent-acp` or `opencode` and does the ACP `initialize` handshake (`connector/connector.py:35`, `connector/session.py:447-453`) over the subprocess stdio (`session.py:440-441`). Runtime loop `_user_message_loop` (`user_api/serve.py:479-484`) is human-driven from Telegram; 4-task `asyncio.TaskGroup` (`serve.py:742-763`).

A third path, `tbot watch` (`cli/commands/watch.py`), provides autonomous cadence (`heartbeat_interval=30.0`, `autonomous_interval=60.0`, `:176-177`; `pick_trigger` `:272-311`) and dispatches `run_agent` per trigger (`:190-201`, `:343-345`).

| Capability | Present? | Anchor |
|---|---|---|
| Pause | No (only via game `set_speed 0`) | — |
| Step / tick / `wait_ticks` | No | — |
| Soft cancel `/cancel`, hard halt `/halt` | Yes | `user_api/serve.py:548-558,578-589` |
| Session recording | Partial, in-memory only (`SubagentRun.transcript`) | `game_mcp/delegation.py:226-232,582-611` |
| Replay | No | — |
| Eval / scenario harness | No (integration runners test HTTP endpoints, not agent behaviour) | — |

## 3. Observation formatting

`render_map` (`formatters/map.py:120-211`): symbol table `STYLE` (`:29-106`, ~120 substring rules, first match wins); rows y2→y1 (`:132`); elevation double-encoded as ANSI-256 background (`_zbg`, `:109-117`) plus `terrain % 10` digit (`:177`); water `~` only when no occupant (`:168-173`), depth not encoded; moisture as green/dim (`:178`); `@` entrance (`:147-152`); `t` seedling (`:161-163`); legend auto-built (`:200-203`); height legend only if >1 z-level (`:205-209`). ANSI always emitted; no `isatty`/`NO_COLOR` (grep over `src/` → 0 hits).

**Token-budget logic: none.** No `tiktoken`, no token counting, no context accounting. Only unrelated caps: event ring buffer 256 / per-call 64 (`game_mcp/bus.py:78-79,94-121`), `_EXCERPT_CHARS=400` (`connector/subagent.py:272`), Telegram `MAX_CHARS=4000` (`user_api/telegram/streaming.py:29`), debug-log `_DEBUG_BODY_CHARS=400` (`api/client.py:60`). `buildings(detail="full")` or `beavers` with no `limit` streams an unbounded payload into the model (`server.py:190-193`, default `limit=0`).

## 4. REST client (`api/client.py`)

Thin by design: one `requests.Session` (`:101-105`); `_get` (`:135-158`) / `_post` (`:159-179`) force `format=json`, log, fire, `_check_auth`, `raise_for_status`, `_check` raises `TimberbotError` on any `{"error": …}` body.

| Feature | Present? | Evidence |
|---|---|---|
| Retries | No (single attempt; `ConnectionError`/`Timeout` re-raised `:145-152,169-176`) | backoff only in `serve.py:_probe_mod_until_reachable`, `wsclient.py:318` |
| Timeouts | Fixed: GET 5 s (`:145`), POST `_write_timeout` 60 s (`:79,168`) | not per-call |
| Caching | No | one-shot version-warning flag `:241-243` |
| Pagination | Pass-through `limit`/`offset` only, default `0` = unlimited | `:344` etc. |
| bbox | Two shapes, not unified: `x1/y1/x2/y2[/z]` (`tiles` `:499-501`, `find_placement` `:563-574`, area writes) and `x/y/radius` (`buildings` `:349-353`, `find` `:802-814`) | |
| Typed responses | GETs via generated Pydantic models; **all POSTs return raw `dict`** | `api/models/_generated.py` |

Method → endpoint map: see `openapi.yaml` `operationId`s (test `tests/test_openapi_spec.py` asserts every `operationId` has a client method). Non-HTTP methods delegate to `SettlementContext` (`state.py`): `set_location`, `remove_location`, `list_locations`, `clear_brain`, `add_task`, `update_task`, `list_tasks`, `clear_tasks`, `complain`, `list_complaints`, `resolve_complaint`. `map` (`:706-715`) = `GET /api/tiles` + `render_map`.

## 5. Tests

Default `pytest` excludes integration: `addopts = "-ra --strict-markers -m 'not integration'"` (`pyproject.toml:76`).

- **Unit / contract suites (`tests/*.py`, `tests/contract/`)** — none need a running game. Highlights: `tests/test_mcp_server.py` (tool set of 61 asserted via `mcp.list_tools()` `:114-137`, envelope shape, `MagicMock(spec=TimberbotClient)` `:26`, **in-process `mcp.call_tool` — no transport exercised**); `tests/test_delegate_mcp.py` (9 delegation tools, stubbed broker); `tests/test_acp_connector.py`; `tests/test_wsclient.py` / `test_listen.py` / `test_watch.py` (in-process aiohttp WS server); `tests/test_cli_dispatch.py` (pytest-httpserver); `tests/test_openapi_spec.py`; `tests/contract/test_openapi_responses.py` (golden fixtures in `tests/fixtures/openapi/`, live layer gated by `TBOT_OPENAPI_LIVE=1` `:10-15`).
- **Integration suite (`tests/integration/`)** — all require a running game: `v2_runner.py` (1572 LOC, modes smoke/freshness/write_to_read/performance/concurrency), `validation_runner.py` (5976 LOC, ~70 `test_*` across 15 groups), `test_v2_modes.py`, `test_validation_methods.py`. Gated three ways: marker exclusion, `live_game` fixture `pytest.skip` when the mod is unreachable (`conftest.py:50-68`), `--tbot-host/--tbot-port` (`conftest.py:23-47`). Not run in CI (`.github/workflows/python-tests.yml` runs `pytest -q` only).

**Coverage gap:** nothing exercises `run_http_async`, the SSE handshake, session negotiation, or `tools/list` over the wire. A transport-level spec migration would pass this suite while the wire behaviour changed.

## 6. Spec-drift vs MCP 2026-07-28

| Spec area | Current state | Anchor | Change required |
|---|---|---|---|
| Stateless core, no `initialize` | SSE is structurally stateful; framework default handshake | `server.py:102-112`, `serve.py:746-751` | Transport swap (streamable HTTP, stateless) + ACP spec `"type":"http"` (`serve.py:405-409`); client branch exists (`session.py:41-43`) |
| No `Mcp-Session-Id` | App never touches it | grep 0 hits | Nothing to remove; cheap |
| Server-minted handles | Already the pattern for `subagent_id` (`delegation.py:353-362`, nonce id `connector/subagent.py:125`) | | Handle→state must not depend on connection; today single-tenant broker (`delegation.py:82-90`) |
| MRTR / input-required | **Absent, worked around** by blocking waits (`subagent_wait` 60 s `:452-455`, `subagent_wait_all` `:531`, `delegate(wait=True)` `:313-344`) and a background-task + poll + `meta.subagent_events` side channel (`_drive_turn` `:199-263`, `_drain_background_turn` `:180-196`, `models.py:45-66`) | | Largest single change; the polling scaffolding is what MRTR replaces |
| Tool annotations | None on 70 tools | all registrations | Read/write split already exists as data in three drifting places: section comments (`server.py:130,420,712,761`), `_READ_TOOLS/_MUTATION_TOOLS/_SEARCH_TOOLS` (`connector/agent_spec.py:88-114`), `_WRITE_TOOL_PREFIXES` (`connector/session.py:56-61`) |
| Cacheable `tools/list` (`ttlMs`) | Not settable at this version; tool set static after startup (`server.py:842-843`) | | Good fit once framework supports it |
| Elicitation / sampling | Not used | | None; MRTR is the confirmation vehicle |
| `outputSchema` / structured content | Never set; `{result, meta}` hand-rolled; `EventEnvelope` model exists unused (`models.py:90-93`) | `server.py:66-77` | Declare models → real `outputSchema` |
| Error signalling | In-band `{"error": ...}` (`delegation.py:294,298-301,377`) instead of `isError` | | Normalise |

**Judgement:** a complete, well-tested 2025-era stateful-SSE MCP server (70 tools, event-cursor envelope, delegation family) with no annotations, no output schemas, no cacheability, no MRTR, on framework pins that cannot express the 2026-07-28 features. Cheap: annotations, `outputSchema`, `ttlMs`, transport swap. Expensive: MRTR (replaces the async-delegation scaffolding). The test suite cannot detect any of this.
