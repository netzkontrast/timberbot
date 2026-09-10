# Timberbot API

<p align="center">
  <img src="thumbnail.png" alt="Timberbot — a cybernetic beaver playing Timberborn at a desk">
</p>

**Full read/write HTTP API for controlling Timberborn with AI.**

Gameplay is stable; AI integration and automation features are under active development.

Timberbot gives Claude, Codex, ChatGPT, or your own scripts complete access to a running Timberborn colony over HTTP. Read game state, place buildings, manage workers, plant crops, wire automation, and keep your beavers alive.

!!! tip "The Wardens"
    The same repository ships a second mod, [The Wardens](https://github.com/netzkontrast/timberbot/tree/main/wardens): a Timberborn faction in which the AI is a character, with an MCP server inside the game so Claude Code plays beside you. Its docs live in the repository, not on this site.

!!! info "Modified fork"
    This project is a modified fork of [abix-/TimberbornMods](https://github.com/abix-/TimberbornMods). It extends the original mod with an expanded read/write HTTP API, automation wiring endpoints, a WebSocket event stream, and AI-agent integrations. All credit for the original mod goes to [abix-](https://github.com/abix-).

---

## Start here

<div class="grid cards" markdown>

- **[Getting Started](getting-started.md)**

    Install the mod, set up the Python client, and run your first commands.

- **[API Reference](api-reference.md)**

    Every HTTP endpoint with request/response examples.

- **[Agent Interfaces](agent-interfaces.md)**

    Four ways an agent reaches the game. Read this first to find the doc that governs yours.

- **[Timberbot Guide](timberbot.md)**

    Full operating guide for AI agents playing Timberborn through the `tbot` CLI.

- **[MCP Endpoint](mcp.md)**

    The in-mod MCP server on `POST /mcp` — tools, ready gate, error contract.

- **[Features](features.md)**

    Compatibility matrix of what's implemented vs gaps.

- **[Events](events.md)**

    Subscribe to game events over the mod's WebSocket.

- **[Architecture](architecture.md)**

    Internals, thread model, read/write pipeline.

</div>

---

## What you can do

| | Read | Write |
|---|---|---|
| **Buildings** | All buildings with workers, power, priority, inventory | Place, demolish, pause, configure |
| **Beavers** | Wellbeing, needs, workplace, contamination | Migrate between districts (in-progress) |
| **Resources** | Per-district stocks, distribution settings | Set import/export, stockpile config |
| **Map** | Terrain, water, occupants, contamination | Plant crops, mark trees, route paths |
| **Automation** | Sensors, relays, memory cells, wiring graph | Link/unlink inputs, configure thresholds and modes |
| **Colony** | Weather, science, alerts, notifications | Speed, work hours, unlock buildings |

---

## Quick taste

```bash
# with Timberborn running + the mod loaded
tbot summary                                          # colony snapshot
tbot map --x1=110 --y1=130 --x2=130 --y2=150          # ASCII map with terrain shading
tbot place_path --x1=110 --y1=130 --x2=130 --y2=150   # A* pathfinding with auto-stairs
tbot set_speed 3                                      # fast forward

# or raw HTTP, no Python required
curl http://127.0.0.1:8085/api/summary
curl -X POST http://127.0.0.1:8085/api/speed -d '{"speed": 3}'
```

Ready to install? Head to **[Getting Started](getting-started.md)**.
