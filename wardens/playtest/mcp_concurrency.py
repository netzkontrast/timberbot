"""Is the MCP server answering while a long-poll waits? Run with a game loaded.

    python wardens/playtest/mcp_concurrency.py          # default http://127.0.0.1:8090/mcp
    python wardens/playtest/mcp_concurrency.py http://127.0.0.1:8090/mcp

Starts a `frame` with wait_seconds=20 on one thread and a `ping` on another. Before iteration 04's
WP2 the ping took about 20 s (the listener handled one request at a time, so the long-poll blocked
everything behind it, which is what the 2026-09-10 playtest saw as a dropped connection); after,
well under a second. Exit code 1 if the ping takes longer than 2 s. Standard library only, like
mcp_smoke.py.
"""
from __future__ import annotations

import json
import sys
import threading
import time
import urllib.request

URL = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8090/mcp"
PROTOCOL = "2025-06-18"


def post(payload: dict, timeout: int = 60) -> dict | None:
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(), method="POST",
                                 headers={"Content-Type": "application/json", "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        body = resp.read().decode("utf-8")
        return json.loads(body) if body else None


def main() -> int:
    post({"jsonrpc": "2.0", "id": 1, "method": "initialize",
          "params": {"protocolVersion": PROTOCOL, "capabilities": {},
                     "clientInfo": {"name": "mcp_concurrency", "version": "0"}}})
    post({"jsonrpc": "2.0", "method": "notifications/initialized"})
    # `after` far beyond any seq the server can have handed out, so the call waits the full 20 s.
    frame = threading.Thread(target=post, args=({"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                                                 "params": {"name": "frame",
                                                            "arguments": {"wait_seconds": 20, "after": 10 ** 12}}},))
    frame.start()
    time.sleep(0.5)                      # let the frame call reach the server first
    t0 = time.monotonic()
    post({"jsonrpc": "2.0", "id": 3, "method": "ping"})
    took = time.monotonic() - t0
    frame.join()
    print(f"ping answered in {took:.2f} s while a 20 s frame long-poll was pending")
    if took >= 2.0:
        print("FAIL: the listener is serialising requests (WardensMcpServer.ListenLoop)")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
