#!/usr/bin/env python3
"""Thin wrapper: Ninja draft reset → tools/app_reset.py --app ninja.

See app_reset.py for discovery, blockers, and --apply behaviour.
"""
from __future__ import annotations

import sys

from app_reset import main as app_reset_main


def main(argv: list[str] | None = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if "--app" not in args:
        args = ["--app", "ninja", *args]
    return app_reset_main(args)


if __name__ == "__main__":
    sys.exit(main())
