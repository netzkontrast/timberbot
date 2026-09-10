---
title: Agent Interfaces
description: Which interface an AI agent is talking through, which document governs it, and which ones are current.
---
# Agent Interfaces

There are **four** ways an AI agent reaches a running Timberborn colony in this repo. They have different transports, different tool names, and different governing documents. Most confusion — and most wrong-doc answers — comes from not knowing which one you are on.

Start here, then read only the doc for your interface.

## Which interface am I on?

| If you… | You are on | Governing doc | Status |
|---|---|---|---|
| Run `tbot` commands in a shell | **CLI** | [Timberbot Guide](timberbot.md) | current |
| Call tools named `timberborn_*` | **In-mod MCP** | [MCP Endpoint](mcp.md) | current |
| Call tools named `mcp__game__*` (via `tbot serve`) | **Python MCP** | this page + [Configuration](configuration.md) | **frozen** |
| Call `frame`, `manual`, `say`, `chapter` | **Wardens MCP** | [`wardens/WARDEN.md`](https://github.com/netzkontrast/timberbot/blob/main/wardens/WARDEN.md) | current |

If you can see this file in your context but none of the above matches, you are reading documentation rather than playing — see [`AGENTS.md`](https://github.com/netzkontrast/timberbot/blob/main/AGENTS.md) for the developer view.

## The four interfaces

### 1. `tbot` CLI — shell commands

The agent shells out to `tbot <command>` and reads stdout. Talks to the mod over HTTP (`:8085`) and WebSocket (`:8086`).

- **Governing doc:** [Timberbot Guide](timberbot.md) — the full operating guide, boot sequence, factions, automation vocabulary.
- **Exact command shapes:** [API Reference](api-reference.md).
- **Runtime prompt:** `python/src/timberbot/agent_prompts/timberbot.md`, injected by `tbot agent run`.
- **Spatial reads:** `tbot brain` for orientation, `tbot map` for the ASCII grid, `tbot tiles` for raw per-tile data.

This is the only interface with a `tiles` read. The Guide's advice to "use `tiles` when you need raw data" applies **here only**.

### 2. In-mod MCP endpoint — `POST /mcp`

A stateless MCP server (revision 2026-07-28) hosted by `Timberbot.dll` on the **same port as the HTTP API** (default `8085`). No separate process, no session to initialise.

- **Governing doc:** [MCP Endpoint](mcp.md). Conformance rules in [`docs/spec/mcp-endpoint.md`](https://github.com/netzkontrast/timberbot/blob/main/docs/spec/mcp-endpoint.md), decision record in [ADR-001](https://github.com/netzkontrast/timberbot/blob/main/docs/adr/ADR-001-mcp-host.md).
- **Tools:** 14, all prefixed `timberborn_` (`timberborn_get_summary`, `timberborn_place_building`, …).
- **Spatial reads:** `timberborn_get_region` — there is no `tiles` tool. `format=map` is a plain-text grid at ~1 token per cell (max 64×64); `format=json` is raw per-tile data at ~57 tokens per cell (max 20×20).
- **Turn it off:** `mcpEnabled: false` in `settings.json` returns `404`.

This is the **direction of travel** — the current phase plan is mod-first ([`docs/plan/roadmap-v2-mod-first.md`](https://github.com/netzkontrast/timberbot/blob/main/docs/plan/roadmap-v2-mod-first.md)). New tool work belongs here.

### 3. Python MCP server — `tbot serve` (frozen)

A FastMCP server (SSE, `127.0.0.1:8091` by default) that wraps `TimberbotClient` as ~61 game tools plus 9 delegation tools. Started in-process by `tbot serve`, which also routes agent output to Telegram.

!!! warning "Frozen — not extended"
    ADR-007 froze this layer. It is kept as **test tooling** and for the existing `tbot serve` Telegram path; it is not receiving new tools, and removal is revisited after the in-mod endpoint proves out. It predates the 2026-07-28 revision — SSE, stateful, no tool annotations.

    Do not file an in-mod MCP issue against this server, or vice versa. Check the tool prefix: `timberborn_*` is the mod, `mcp__game__*` is this one.

- **Source:** `python/src/timberbot/game_mcp/`.
- **Known gap:** it exposes neither a `map` nor a `tiles` tool, so an agent driven purely through `tbot serve` has no spatial read (contradiction C17 in the [audit log](https://github.com/netzkontrast/timberbot/blob/main/docs/audit/contradictions.md)).
- **Delegation tools:** `delegate`, `subagent_reply`, `subagent_status`, `subagent_wait`, `subagent_wait_all`, `subagent_cancel`, `subagent_close`, `subagent_list`, `subagent_transcript`. Design in [`design/subagent-delegation.md`](https://github.com/netzkontrast/timberbot/blob/main/design/subagent-delegation.md).

### 4. Wardens in-game MCP — `:8090/mcp`

The Wardens faction mod inverts the connector model: the agent is a *character in the game*, not an external operator. `Wardens.dll` hosts its own MCP server and the agent lives on a tick-driven `frame` heartbeat.

- **Governing doc:** [`wardens/WARDEN.md`](https://github.com/netzkontrast/timberbot/blob/main/wardens/WARDEN.md) — the playbook, served in-game by the `manual` tool. Tool table in [`wardens/playtest/PLAYTEST.md`](https://github.com/netzkontrast/timberbot/blob/main/wardens/playtest/PLAYTEST.md).
- **Connect:** `.mcp.json` at the repo root points Claude Code at `http://127.0.0.1:8090/mcp`.
- **Distinct tools:** `frame`, `manual`, `chat_read`, `say`, `point`, `camera`, `chapter`, plus `timberbot` as an HTTP passthrough.

!!! danger "Never enable both mods"
    `wardens/src/Timberbot/` is a verbatim copy of `timberbot/src/`, so the Wardens DLL binds the same ports (`8085`/`8086`). Enable **The Wardens** or **Timberbot API**, never both.

## Rules that hold on every interface

- **The ready gate.** Until the player presses **Launch** in the in-game widget, reads and writes fail: `409 game_not_ready` on `/api/*`, tool error `GAME_NOT_READY` on `/mcp`. Exempt: `/api/ping`, `/api/ready`, `/api/agent/*`. The WebSocket is never gated. `ready` resets to `false` on every save load.
- **Auth.** When `authToken` is set in `settings.json`, every `/api/*` request, every `/mcp` request, and every WS upgrade needs `Authorization: Bearer <token>`. The mod refuses to start with a non-localhost `listenAddress` and an empty token.
- **Mutations are sequential.** The game processes one write job at a time on the Unity main thread. Never issue mutating calls in parallel, on any interface.
- **Re-read after mutating.** Pre-mutation observations are stale the moment a write lands.

## Where the agent personas are defined

The `wirer`, `scout`, and `auditor` subagents exist **twice**, once per dispatch path. They are not generated from each other.

| Path | Definition | Format |
|---|---|---|
| `tbot agent run` / `tbot watch` | `python/src/timberbot/agent_prompts/{wirer,scout,auditor}.md` | Markdown + frontmatter |
| `tbot serve` (ACP) | `python/src/timberbot/connector/agent_spec.py` (`WIRER_SPEC`, `SCOUT_SPEC`, `AUDITOR_SPEC`) | `AgentSpec` dataclass |

Change one, change the other — a capability added to `WIRER_SPEC.allowed_mcp_tools` does **not** reach the markdown prompt, and a rule added to the markdown does not reach ACP.

The main agent differs by path too: `tbot agent run` injects `agent_prompts/timberbot.md`, while `tbot serve` has no markdown at all — its identity, tool scope, and refusal rules live in code at `connector/agent_spec.py:TIMBERBOT_SPEC`. `agent_prompts/connector-mode.md` is a preamble that `tbot watch` prepends on top.

## When docs and code disagree

**Code wins**, and the disagreement is logged in [`docs/audit/contradictions.md`](https://github.com/netzkontrast/timberbot/blob/main/docs/audit/contradictions.md) with `path:line` anchors and a confidence rating. Check that log before trusting a surprising claim in any doc on this site — several entries are still open.
