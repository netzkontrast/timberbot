# MCP endpoint (`POST /mcp`)

The mod hosts a **stateless Model Context Protocol server, revision 2026-07-28**, on the same port as the HTTP API (default `8085`). Any MCP client that speaks Streamable HTTP can connect: MCP Inspector, Claude Code, Claude Desktop, or your own client. There is no separate process to run and no session to initialise.

```
POST http://127.0.0.1:8085/mcp
MCP-Protocol-Version: 2026-07-28
Mcp-Method: tools/call
Mcp-Name: timberborn_get_summary
Content-Type: application/json
Authorization: Bearer <authToken>        # only when authToken is set in settings.json
```

Design and conformance rules: [`docs/spec/mcp-endpoint.md`](https://github.com/netzkontrast/timberbot/blob/main/docs/spec/mcp-endpoint.md) and [ADR-001](https://github.com/netzkontrast/timberbot/blob/main/docs/adr/ADR-001-mcp-host.md). Protocol core: `timberbot/src/TimberbotMcp.cs` (Unity-free, covered by xUnit).

## Connect

=== "MCP Inspector"

    ```bash
    npx @modelcontextprotocol/inspector
    # Transport: Streamable HTTP · URL: http://127.0.0.1:8085/mcp
    ```

=== "Claude Code"

    ```bash
    claude mcp add --transport http timberborn http://127.0.0.1:8085/mcp
    # with a token: claude mcp add --transport http --header "Authorization: Bearer <token>" timberborn http://127.0.0.1:8085/mcp
    ```

=== "curl"

    ```bash
    curl -s http://127.0.0.1:8085/mcp \
      -H 'Content-Type: application/json' -H 'MCP-Protocol-Version: 2026-07-28' -H 'Mcp-Method: server/discover' \
      -d '{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientCapabilities":{}}}}'
    ```

The client must send the `MCP-Protocol-Version`, `Mcp-Method` and (for `tools/call`) `Mcp-Name` headers and the `_meta` fields shown above; conforming clients do this automatically. A client that still sends `initialize` gets `404` with a JSON-RPC error naming the supported version.

## Ready gate and auth

- `server/discover` and `tools/list` work as soon as a save is loaded.
- `tools/call` respects the in-game **Launch** gate: while the player has not pressed Launch, every tool returns a tool error with `code: "GAME_NOT_READY"` (the same rule as `409 game_not_ready` on `/api/*`).
- `authToken` from `settings.json` applies to `/mcp` exactly as to `/api/*`. The endpoint binds to `listenAddress` (loopback by default) and rejects foreign `Origin` headers with `403`.
- `mcpEnabled: false` in `settings.json` turns the endpoint off (`404`).

## Tools

All tools are prefixed `timberborn_`, take a strict JSON object (`additionalProperties: false`) and return both a text block and `structuredContent`. Annotations are honest: reads are `readOnlyHint`, `demolish` and `unlock_science` are `destructiveHint`.

| Tool | Kind | What it does |
|---|---|---|
| `timberborn_get_summary` | read | Colony snapshot: day, weather, population, housing, employment, wellbeing, science, per-district resources and days-of-supply, alerts, building roles, district centers. Call first. |
| `timberborn_get_region` | read | Rectangle of tiles. `format=map` (default) is a plain-text grid at ~1 token per cell, max 64×64; `format=json` is the raw per-tile data at ~57 tokens per cell, max 20×20; `both` returns both. |
| `timberborn_get_buildings` | read | Paginated buildings (`limit` default 50, `offset`, `name` substring, `x`/`y`/`radius`, `id`, `detail=basic|full`). |
| `timberborn_get_beavers` | read | Paginated beavers (`limit` default 20; `detail=full` adds all needs, ~1.4k tokens each). |
| `timberborn_get_prefabs` | read (game thread) | Placeable prefab names with size, science cost, unlocked flag and material cost; `name` filter, `limit`. |
| `timberborn_find_placement` | read (game thread) | Valid placements for a prefab in a rectangle or radius, scored for path access, reachability, power and flooding. |
| `timberborn_place_building` | write | Place a construction site; failures explain why (`PLACEMENT_OCCUPIED` with the blocker, `NOT_UNLOCKED` with cost vs points, …). |
| `timberborn_place_path` | write | A* path with auto-stairs between two tiles. |
| `timberborn_demolish` | destructive | Demolish a building by id. Requires confirmation. |
| `timberborn_set_speed` | write | 0 pause, 1–3 speeds. |
| `timberborn_set_workers` | write | Desired worker count of a workplace. |
| `timberborn_unlock_science` | destructive | Spend science points on a building type. Requires confirmation. |
| `timberborn_set_distribution` | write | District import option and export threshold for a good. |
| `timberborn_wait_frames` | write | Let 1–600 Unity frames pass on the game thread before returning (frames, not ticks; see ADR-005). |

Write tools run on the game's main thread through the same budgeted write-job queue as `POST /api/*`; the MCP response is sent when the job completes. Read tools run on the listener thread from published snapshots, like `GET /api/*`.

### Confirmation for destructive tools

- Clients that declare the `elicitation` capability get an `InputRequiredResult` (`resultType: "input_required"`) with an `elicitation/create` form and a `requestState`; the client asks the user and retries with `inputResponses.confirm.action = "accept"` plus the same `requestState`. `requestState` is HMAC-signed, bound to the tool and its arguments, and expires after 120 s.
- Clients without elicitation must pass `confirm: true` explicitly; otherwise the tool returns `code: "CONFIRMATION_REQUIRED"`.

### Errors

Protocol problems are JSON-RPC errors (`-32602` unknown tool or invalid arguments, `-32020` header mismatch, `-32022` unsupported protocol version, `-32601` unknown method). Game-side failures are **tool execution errors**: `isError: true`, `content[0].text` = `CODE: reason. hint`, and

```json
{ "ok": false, "code": "PLACEMENT_OCCUPIED", "reason": "occupied by Path at (120,130,2)",
  "hint": "demolish it or try a different location", "at": {"x":120,"y":130,"z":2},
  "details": {"prefab": "LumberjackFlag.IronTeeth"} }
```

in `structuredContent`. The full code table is in [`docs/spec/error-contract.md`](https://github.com/netzkontrast/timberbot/blob/main/docs/spec/error-contract.md).

## Not in this iteration

Resources, prompts, `subscriptions/listen`, SSE streaming, tick-aligned commits, delta reads and handles for large payloads. The Python MCP server under `python/src/timberbot/game_mcp/` (FastMCP, SSE, pre-2026-07-28) is frozen and kept only as test tooling; see ADR-007 in `docs/plan/roadmap-v2-mod-first.md`.
