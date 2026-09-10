"""One-shot: call dump_assets (blueprints/materials/textures, filter LeafCoats) on the live MCP
server. See design/leafcoats-port-plan.md §7 step 1. Writes to Documents/Timberborn/WardensDump
(deliberately a sibling of Mods/, not inside Mods/Wardens/ - the mod loader scans every mod folder
recursively with no exclusions, so a dump left inside one gets re-loaded as real mod content).

    python wardens/playtest/run_dump.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mcp_smoke import Mcp  # noqa: E402


def main() -> int:
    mcp = Mcp("http://127.0.0.1:8090/mcp")
    mcp.request("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "run_dump", "version": "0"}})
    mcp.notify("notifications/initialized")

    r = mcp.call("dump_assets", what="blueprints", filter="LeafCoats")
    print("blueprints:", r)
    r = mcp.call("dump_assets", what="materials", filter="LeafCoats")
    print("materials:", r)
    r = mcp.call("dump_assets", what="textures", filter="LeafCoats", max_count=400)
    print("textures:", r.get("written"), "failed:", r.get("failed"), "folder:", r.get("folder"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
