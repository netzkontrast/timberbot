"""Smoke test for the in-game MCP server (plain urllib, no dependencies).

Run with Timberborn open on any loaded game with The Wardens mod enabled:

    python wardens/playtest/mcp_smoke.py            # default http://127.0.0.1:8090/mcp
    python wardens/playtest/mcp_smoke.py --say hi   # also posts a chat line into the game

Speaks MCP Streamable HTTP (JSON-RPC 2.0 over POST): initialize -> notifications/initialized
-> tools/list -> wardens_status -> optional say. Exit code 1 on the first failure.
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request

PROTOCOL = "2025-06-18"


class Mcp:
    def __init__(self, url: str) -> None:
        self.url = url
        self._id = 0

    def _post(self, payload: dict) -> dict | None:
        data = json.dumps(payload).encode()
        req = urllib.request.Request(
            self.url, data=data, method="POST",
            headers={"Content-Type": "application/json", "Accept": "application/json, text/event-stream"},
        )
        with urllib.request.urlopen(req, timeout=130) as resp:
            body = resp.read().decode("utf-8")
            return json.loads(body) if body else None

    def request(self, method: str, params: dict | None = None) -> dict:
        self._id += 1
        msg = {"jsonrpc": "2.0", "id": self._id, "method": method}
        if params is not None:
            msg["params"] = params
        resp = self._post(msg)
        if resp is None or "error" in resp:
            raise RuntimeError(f"{method}: {resp}")
        return resp["result"]

    def notify(self, method: str) -> None:
        self._post({"jsonrpc": "2.0", "method": method})

    def call(self, name: str, **arguments) -> dict:
        result = self.request("tools/call", {"name": name, "arguments": arguments})
        if result.get("isError"):
            raise RuntimeError(f"tool {name} failed: {result}")
        return result.get("structuredContent") or json.loads(result["content"][0]["text"])


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="http://127.0.0.1:8090/mcp")
    ap.add_argument("--say", default=None, help="post this text into the in-game chat")
    args = ap.parse_args()

    mcp = Mcp(args.url)
    try:
        init = mcp.request("initialize", {
            "protocolVersion": PROTOCOL,
            "capabilities": {},
            "clientInfo": {"name": "mcp_smoke", "version": "0.1"},
        })
    except (urllib.error.URLError, ConnectionError, OSError) as exc:
        sys.exit(f"cannot reach the in-game MCP server at {args.url}: {exc}\n"
                 "Is Timberborn running with a game loaded and The Wardens mod enabled?")
    print(f"server={init['serverInfo']['name']} {init['serverInfo']['version']} protocol={init['protocolVersion']}")
    mcp.notify("notifications/initialized")

    tools = mcp.request("tools/list")["tools"]
    names = sorted(t["name"] for t in tools)
    print(f"tools ({len(names)}): {' '.join(names)}")
    expected = {"wardens_status", "tutorial", "chapter", "point", "say", "chat_read", "camera", "timberbot", "timberbot_ready"}
    missing = expected - set(names)
    if missing:
        print("FAIL missing tools:", sorted(missing))
        return 1

    status = mcp.call("wardens_status")
    pop = status["population"]
    print(f"faction={status['faction']!r} paused={status['paused']} bots={pop['bots']} beavers={pop['beavers']} "
          f"energy_avg={pop['bots_energy_avg']}")
    tut = status["tutorial"]
    print(f"tutorial enabled={tut['enabled']} active={[a['stage'] for a in tut['active']]} finished={tut['finished']}")
    for a in tut["active"]:
        for s in a["steps"]:
            print(f"   [{'x' if s['achieved'] else ' '}] {s['text']}")

    if args.say:
        r = mcp.call("say", text=args.say, toast=True)
        print("say ->", r)

    print("\nPASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
