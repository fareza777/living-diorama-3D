#!/usr/bin/env python3
"""Fetch the game's typefaces from Google Fonts.

The single loudest "unfinished prototype" signal in a mobile game is Unity's default
system font, so the interface ships with real typefaces: an inscriptional serif for
titles, which suits the brass-and-wood plates, and a rounded sans for everything that
has to be read at a glance on a phone.

Both are under the SIL Open Font Licence, which permits bundling and redistribution
inside a game. The licences are written out beside the files.

Resolving the files through the CSS endpoint rather than hardcoding gstatic URLs
means this keeps working when Google reissues a version.

Usage:
    python Tools/fetch_fonts.py
"""
from __future__ import annotations

import re
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "Assets" / "Resources" / "Fonts"

CSS_URL = (
    "https://fonts.googleapis.com/css2"
    "?family=Cinzel:wght@600;700"
    "&family=Nunito:wght@400;600;700"
)

# The CSS endpoint serves whatever format the requesting browser claims to support.
# A modern user agent is given TrueType here, which is what Unity imports; omitting
# the header entirely, or claiming to be ancient, yields formats it cannot read.
USER_AGENT = "Mozilla/5.0"

FAMILIES = {
    ("Cinzel", "600"): "Cinzel-SemiBold.ttf",
    ("Cinzel", "700"): "Cinzel-Bold.ttf",
    ("Nunito", "400"): "Nunito-Regular.ttf",
    ("Nunito", "600"): "Nunito-SemiBold.ttf",
    ("Nunito", "700"): "Nunito-Bold.ttf",
}

LICENCE = """These typefaces are licensed under the SIL Open Font Licence, Version 1.1,
which permits bundling them inside an application and redistributing them.

  Cinzel   (c) Natanael Gama          https://fonts.google.com/specimen/Cinzel
  Nunito   (c) Vernon Adams and       https://fonts.google.com/specimen/Nunito
           the Nunito Project Authors

Full licence text: https://scripts.sil.org/OFL

Fetched by Tools/fetch_fonts.py.
"""


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def parse_css(css: str) -> dict[tuple[str, str], str]:
    """Map (family, weight) to the ttf url in a Google Fonts stylesheet."""
    found: dict[tuple[str, str], str] = {}

    for block in css.split("@font-face"):
        family = re.search(r"font-family:\s*'([^']+)'", block)
        weight = re.search(r"font-weight:\s*(\d+)", block)
        url = re.search(r"src:\s*url\(([^)]+\.ttf)\)", block)

        if family and weight and url:
            found[(family.group(1), weight.group(1))] = url.group(1)

    return found


def is_truetype(data: bytes) -> bool:
    return data[:4] in (b"\x00\x01\x00\x00", b"true", b"ttcf", b"OTTO")


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    css = fetch(CSS_URL).decode("utf-8", errors="replace")
    urls = parse_css(css)

    if not urls:
        print("could not parse any font urls out of the stylesheet", file=sys.stderr)
        return 1

    failures = 0
    for key, filename in FAMILIES.items():
        url = urls.get(key)
        if url is None:
            print("[miss] %s %s not offered by the stylesheet" % key)
            failures += 1
            continue

        data = fetch(url)
        if not is_truetype(data):
            # A rate limit or a redirect to an error page returns HTML with a 200,
            # which would otherwise be written out as a corrupt font.
            print("[fail] %s: not a TrueType file (%d bytes, starts %r)"
                  % (filename, len(data), data[:8]))
            failures += 1
            continue

        (OUT_DIR / filename).write_bytes(data)
        print("[ok] %s (%d KB)" % (filename, len(data) // 1024))

    (OUT_DIR / "OFL-LICENCE.txt").write_text(LICENCE, encoding="utf-8")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
