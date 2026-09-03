# Spec — In-mod MCP endpoint (`POST /mcp`)

Status: implemented in `timberbot/src/TimberbotMcp.cs` (protocol core, Unity-free) and `timberbot/src/TimberbotHttpServer.cs` (transport). Protocol revision: **2026-07-28**. Key words MUST/SHOULD/MAY per RFC 2119.

## Requirements

### Transport
- R1 The mod MUST expose exactly one MCP endpoint, `POST /mcp`, on the existing HTTP listener (same port, same `listenAddress`, same `authToken` rules as `/api/*`).
- R2 `GET`, `DELETE` and any other method on `/mcp` MUST return `405 Method Not Allowed`.
- R3 If an `Origin` header is present and is neither a loopback origin nor the configured `corsOrigin`, the server MUST respond `403 Forbidden` with a JSON-RPC error body without `id`.
- R4 The request body MUST be a single JSON-RPC 2.0 request or notification. Batches are not accepted. A notification MUST be answered with `202 Accepted` and an empty body.
- R5 Every response to a request MUST be `Content-Type: application/json` containing one JSON-RPC response object. The server MUST NOT open SSE streams in this iteration.
- R6 The server MUST validate `MCP-Protocol-Version`, `Mcp-Method` and (for `tools/call`) `Mcp-Name` against the body; a missing or mismatching header MUST produce `400` with error `-32020` (`HeaderMismatch`). `Mcp-Name` values in the `=?base64?…?=` sentinel form MUST be decoded before comparison.
- R7 The server MUST ignore `Mcp-Session-Id` and `Last-Event-ID` headers and MUST NOT mint session ids.

### Metadata and versioning
- R8 A request whose `params._meta` lacks `io.modelcontextprotocol/protocolVersion` or `io.modelcontextprotocol/clientCapabilities` MUST be rejected with `400` and error `-32602`.
- R9 A protocol version other than `2026-07-28` MUST be rejected with `400` and error `-32022` whose `data.supported` lists `["2026-07-28"]` and `data.requested` echoes the request.
- R10 Every result MUST carry `resultType` and SHOULD carry `_meta["io.modelcontextprotocol/serverInfo"]` = `{name:"timberbot", version:<mod version>}`.
- R11 An unknown method (including `initialize` and `ping`) MUST return `404` with error `-32601`; for `initialize` the message MUST name the supported protocol version.

### Discovery and listing
- R12 `server/discover` MUST return `supportedVersions: ["2026-07-28"]`, `capabilities: {tools: {}}`, `instructions`, `ttlMs`, `cacheScope`.
- R13 `tools/list` MUST return the full catalog in a deterministic order (sorted by name), each tool with `name`, `title`, `description`, `inputSchema` (`type: object`, `additionalProperties: false`) and `annotations` (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint: false`); the result MUST carry `ttlMs` and `cacheScope: "private"`.
- R14 Tool names MUST be prefixed `timberborn_` and use snake_case.

### Calling tools
- R15 Arguments MUST be validated against `inputSchema` (required, types, `additionalProperties`, `minimum`/`maximum`, `enum`); a violation MUST return error `-32602` naming the argument.
- R16 An unknown tool MUST return error `-32602`.
- R17 While the in-game ready gate is closed, `tools/call` MUST return a tool execution error (`isError: true`) with `structuredContent.code = "GAME_NOT_READY"`; `server/discover` and `tools/list` MUST still succeed.
- R18 Read tools MUST execute on the listener thread from published snapshots (never on live entity graphs) and return both `content[0].text` (compact JSON or plain-text map) and `structuredContent`.
- R19 Write tools MUST execute through the existing main-thread write-job queue; the HTTP response MUST wait for job completion; the game MUST NOT be touched from the listener thread.
- R20 A domain failure returned by the game (any `{"error": …}` payload) MUST become a tool execution error with `isError: true` and `structuredContent` = `{ok:false, code, reason, hint, at?}` per `docs/spec/error-contract.md`; the legacy text MUST be preserved in `content[0].text`.
- R21 Tools marked destructive (`timberborn_demolish`, `timberborn_unlock_science`) MUST NOT execute without consent: if the client declares the `elicitation` capability the server MUST return an `InputRequiredResult` (`resultType: "input_required"`, `inputRequests.confirm` as `elicitation/create`, `requestState`); otherwise it MUST return a tool error with code `CONFIRMATION_REQUIRED` unless the call carries `confirm: true`.
- R22 `requestState` MUST be integrity-protected (HMAC), bound to the tool name and an argument digest, and MUST expire (default 120 s). A retry with tampered or expired state MUST NOT execute the action.
- R23 `timberborn_get_region` MUST cap the region: `format=map` ≤ 64×64 cells, `format=json|both` ≤ 20×20 cells; larger requests MUST fail with `INVALID_PARAM` and a hint naming the cap.
- R24 `timberborn_wait_frames` MUST wait the requested number of Unity frames on the main thread (1–600) and MUST describe itself in frames, not ticks, until tick alignment exists (ADR-005).

### Non-goals (this iteration)
Resources, prompts, `subscriptions/listen`, SSE streaming, `x-mcp-header`, authorization beyond the existing bearer token, tick-aligned commits, delta reads, handles for large payloads.

## Scenarios

```gherkin
Feature: Stateless MCP endpoint in the mod

  Background:
    Given the mod is loaded with a save and the HTTP listener is up
    And every request carries MCP-Protocol-Version "2026-07-28" and matching _meta

  Scenario: Discovery
    When the client POSTs server/discover to /mcp
    Then the status is 200
    And result.supportedVersions is ["2026-07-28"]
    And result.capabilities.tools exists
    And result._meta["io.modelcontextprotocol/serverInfo"].name is "timberbot"

  Scenario: Header mismatch
    When the client POSTs tools/list with header Mcp-Method "tools/call"
    Then the status is 400 and error.code is -32020

  Scenario: Unsupported version
    When the client POSTs tools/list with protocol version "2025-11-25"
    Then the status is 400 and error.code is -32022
    And error.data.supported contains "2026-07-28"

  Scenario: Legacy client
    When the client POSTs initialize
    Then the status is 404 and error.code is -32601
    And error.message mentions "2026-07-28"

  Scenario: Tool list is cacheable and deterministic
    When the client POSTs tools/list twice
    Then both results list the same tool names in the same order
    And each tool has annotations and inputSchema.additionalProperties false
    And the result has ttlMs and cacheScope "private"

  Scenario: Read tool from snapshots
    When the client calls timberborn_get_summary
    Then the status is 200 and result.resultType is "complete"
    And result.structuredContent is the summary object
    And no game service was touched off the main thread

  Scenario: Ready gate closed
    Given the player has not pressed Launch
    When the client calls timberborn_get_summary
    Then result.isError is true and result.structuredContent.code is "GAME_NOT_READY"

  Scenario: Write tool goes through the write queue
    When the client calls timberborn_place_building with prefab "Path", x 10, y 10, z 2
    Then the request is queued as a write job for route /api/building/place
    And the response is sent after the job completes on the main thread

  Scenario: Placement failure is legible
    Given the game answers {"error":"occupied by Path at (10,10,2). demolish it or try a different location","x":10,"y":10,"z":2,"prefab":"Path"}
    Then result.isError is true
    And result.structuredContent.code is "PLACEMENT_OCCUPIED"
    And result.structuredContent.at is {x:10,y:10,z:2}
    And result.structuredContent.hint is "demolish it or try a different location"

  Scenario: Destructive tool needs consent (elicitation client)
    Given the client declares the elicitation capability
    When the client calls timberborn_demolish with id 42
    Then result.resultType is "input_required"
    And result.inputRequests.confirm.method is "elicitation/create"
    And result.requestState is present
    When the client retries with inputResponses.confirm.action "accept" and the same requestState
    Then the request is queued as a write job for route /api/building/demolish

  Scenario: Destructive tool needs consent (plain client)
    Given the client declares no elicitation capability
    When the client calls timberborn_demolish with id 42
    Then result.isError is true and result.structuredContent.code is "CONFIRMATION_REQUIRED"
    When the client calls timberborn_demolish with id 42 and confirm true
    Then the request is queued as a write job

  Scenario: Tampered requestState
    When the client retries timberborn_demolish with a modified requestState
    Then no write job is queued

  Scenario: Region cap
    When the client calls timberborn_get_region for 100x100 cells with format "json"
    Then result.isError is true and result.structuredContent.code is "INVALID_PARAM"
```
