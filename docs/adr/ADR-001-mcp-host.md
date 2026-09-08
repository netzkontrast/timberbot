# ADR-001 — Where the MCP server runs and what implements the protocol

| | |
|---|---|
| Status | Accepted 2026-09-02 (option A). Re-open only if Phase 1 spike S1 shows `ModelContextProtocol.Core` loads cleanly in Unity Mono **and** removes real work. |
| Context | The mod already owns an `HttpListener`, auth, body caps and a main-thread write-job queue (`TimberbotHttpServer.cs`). MCP 2026-07-28 removed sessions, `initialize`, `ping`, the GET stream and server-initiated requests; a stateless server needs `server/discover`, `tools/list`, `tools/call`, MRTR results and a handful of header rules. `ModelContextProtocol.AspNetCore` has no netstandard target; `ModelContextProtocol.Core` 2.2.0 targets netstandard2.0 with a 9-package closure (nuget.org, 2026-09-02). |

## Options

| | A — hand-rolled stateless JSON-RPC on the existing listener | B — `ModelContextProtocol.Core` embedded with a custom transport | C — Python/TypeScript sidecar (kickoff default) |
|---|---|---|---|
| New runtime dependencies in Unity | none (Newtonsoft is game-provided) | 9 packages incl. `System.Text.Json` 10.x — conflict risk with game assemblies | none in the mod; a second process |
| Spec conformance owned by | us (≈6 methods, tested against canned JSON-RPC + Inspector) | SDK (but its transport layer is ASP.NET-shaped) | SDK of the chosen language |
| Observation-layer latency | in-process, zero serialisation hops | in-process | REST hop + double serialisation |
| Drift exposure | none beyond the game APIs we already touch | SDK version × Unity Mono version | SDK version × Python/Node version |
| Main-thread safety | reuses the proven write-job queue unchanged | same, behind an adapter | same, over HTTP |

## Decision

**A.** The endpoint is `POST /mcp` on the existing `HttpListener`. The protocol core (`TimberbotMcp.cs`) is Unity-free and compiled into the xUnit project; the Unity side only routes the request, runs read tools on the listener thread from snapshots, and enqueues write tools as ordinary write jobs.

## Named costs

- We own conformance. Mitigation: `docs/spec/mcp-endpoint.md` Gherkin scenarios are the xUnit suite; MCP Inspector is the acceptance gate in Phase 1 spike S3.
- No SSE streaming, no `subscriptions/listen`, no `resources/*` in this iteration; every response is a single JSON object. The capability set advertises exactly `tools`.
- `requestState` integrity is HMAC-SHA256 with a per-process random key; a mod restart invalidates pending confirmations (acceptable: the game restarted too).
