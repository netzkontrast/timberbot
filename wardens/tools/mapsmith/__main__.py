"""Entry point so `python wardens/tools/mapsmith <command>` works from anywhere in the repo."""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from mapsmith.cli import main  # noqa: E402 - the sys.path line above has to run first

raise SystemExit(main())
