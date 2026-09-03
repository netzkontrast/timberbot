"""Wardens v0.1 smoke playtest, driven through the Timberbot API.

Run with the game open on a Wardens save (Launch pressed in the Timberbot widget).
The `timberbot` package lives in the repo's Python project, not in the system
interpreter, so run it through uv from the repo root:

    uv run --project python wardens/playtest/smoke.py

Checks the three things Act I promises: the active faction is Wardens, the
population is bots only, and every bot carries the four Data needs. Exit code
is non-zero on the first failed check so this can gate a CI-style loop later.
"""
from __future__ import annotations

import sys

UV_HINT = "    uv run --project python wardens/playtest/smoke.py"

try:
    from timberbot import TimberbotClient
    from timberbot.api.exceptions import TimberbotError
except ModuleNotFoundError:  # system python without the package
    sys.exit("timberbot is not importable here. From the repo root run:\n" + UV_HINT)

DATA_NEEDS = {"Firmware", "Calibration", "Telemetry", "Uplink"}


def main() -> int:
    client = TimberbotClient()
    failures: list[str] = []

    try:
        summary = client.summary()
    except Exception as exc:  # TimberbotError, or requests.ConnectionError when the mod is down
        sys.exit(f"cannot reach the Timberbot mod: {exc}\n"
                 "Is the game on a loaded save with Launch pressed in the widget?")

    print(f"settlement={summary.settlement!r} faction={summary.faction!r} science={summary.science}")
    if summary.faction != "Wardens":
        failures.append(f"faction is {summary.faction!r}, expected 'Wardens'")

    characters = list(client.beavers(detail="full").items or [])
    bots = [c for c in characters if int(c.isBot) == 1]
    beavers = [c for c in characters if int(c.isBot) != 1]
    print(f"population: {len(characters)} total, {len(bots)} bots, {len(beavers)} beavers")
    if not bots:
        failures.append("no bots spawned (Spike A: WardensStartingPopulation did not run?)")
    if beavers:
        failures.append(f"{len(beavers)} beavers present; Act I should be bots only")

    for bot in bots:
        have = {n.id for n in (bot.needs or [])}
        missing = DATA_NEEDS - have
        if missing:
            failures.append(f"bot {bot.name} (#{bot.id}) is missing Data needs: {sorted(missing)}")
    if bots and not any(f.startswith("bot ") for f in failures):
        print(f"all {len(bots)} bots carry {sorted(DATA_NEEDS)}")

    if failures:
        print("\nFAIL")
        for f in failures:
            print(" -", f)
        return 1
    print("\nPASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
