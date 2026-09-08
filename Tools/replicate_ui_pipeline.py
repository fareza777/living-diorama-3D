#!/usr/bin/env python3
"""Replicate image pipeline for the Living Diorama interface.

Generates the interface art -- icons, panel plates, button plates, box portraits and
the title emblem -- with FLUX, cuts the background out of the pieces that need
transparency, and writes PNGs into Assets/Resources/UI/Art/.

Two families of asset, handled differently on purpose:

  * cutouts (icons, box art, emblem) are generated on a plain background and then
    passed through a background remover, so they can sit on any panel colour;
  * plates (panels, buttons, chips) stay opaque and are nine-sliced by the UI, which
    is both sharper and avoids asking a diffusion model for a clean hollow centre.

Files already on disk are skipped, so re-running is free.

Usage:
    set REPLICATE_API_TOKEN first (see .env.example)

    python Tools/replicate_ui_pipeline.py icons
    python Tools/replicate_ui_pipeline.py plates
    python Tools/replicate_ui_pipeline.py art
    python Tools/replicate_ui_pipeline.py all
    python Tools/replicate_ui_pipeline.py list
"""
from __future__ import annotations

import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

API = "https://api.replicate.com/v1"
ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "Assets" / "Resources" / "UI" / "Art"

FLUX = "black-forest-labs/flux-schnell"
REMOVE_BG_VERSION = "95fcc2a26d3899cd6c2691c900465aaeff466285a65c14638cc5f36f34befaf1"

# One art direction sentence, appended to everything, so twenty separate generations
# still look like they came out of the same studio.
HOUSE_STYLE = (
    "stylised fantasy game interface asset, hand-painted look with clean bold shapes, "
    "warm brass and dark carved wood palette, soft rim light, crisp readable silhouette, "
    "no text, no lettering, no watermark, centred composition"
)

ICON_STYLE = HOUSE_STYLE + ", single object, plain flat white background, even lighting"
PLATE_STYLE = HOUSE_STYLE + ", symmetrical, seamless border decoration, empty flat centre panel"

# --------------------------------------------------------------------- cutouts

ICONS: dict[str, str] = {
    "icon_coin": "a single thick gold coin stamped with an embossed oak leaf, seen face on",
    "icon_essence": "a glowing faceted teal crystal shard floating upright",
    "icon_key": "a small ornate brass key with a clover-shaped bow",
    "icon_chest": "a small closed wooden treasure chest with iron bands and a brass lock",
    "icon_collection": "an open leather-bound bestiary book with a ribbon bookmark",
    "icon_expand": "a rolled parchment map with a small brass compass rose resting on it",
    "icon_settings": "a single ornate brass gear cog",
    "icon_turntable": "a circular arrow curving around a small round display pedestal",
    "icon_day": "a small stylised sun with soft rounded rays",
    "icon_night": "a small stylised crescent moon with two tiny stars",
}

ART: dict[str, tuple[str, str]] = {
    "box_wooden": (
        "a closed wooden treasure chest with iron banding and a brass lock plate, "
        "three quarter view, resting on nothing",
        "1:1",
    ),
    "box_arcane": (
        "a closed dark violet mystery box inlaid with glowing arcane runes and a purple "
        "crystal set into the lid, three quarter view, faint magical glow",
        "1:1",
    ),
    "title_emblem": (
        "an ornate circular emblem containing a miniature world under a glass dome on a "
        "brass stand, a tiny pine tree and a crescent moon inside the dome, gold line art, "
        "perfectly symmetrical heraldic badge",
        "1:1",
    ),
}

# ---------------------------------------------------------------------- plates
# Kept opaque and nine-sliced. The border must be decorative and the centre must be
# quiet, or text laid over it becomes unreadable.

PLATES: dict[str, tuple[str, str]] = {
    "panel_frame": (
        "a rectangular fantasy interface panel, dark carved wood border with brass corner "
        "fittings and a plain dark navy leather centre",
        "4:3",
    ),
    "button_primary": (
        "a horizontal pill shaped brass interface button plate, warm polished gold metal "
        "with small rivets along the rim and a plain slightly darker centre",
        "21:9",
    ),
    "button_ghost": (
        "a horizontal pill shaped dark carved wood interface button plate with thin iron "
        "trim and a plain dark centre",
        "21:9",
    ),
    "chip_plate": (
        "a small horizontal pill shaped dark stone interface plate with a thin brass rim "
        "and a plain dark centre",
        "21:9",
    ),
}


def token() -> str:
    value = os.environ.get("REPLICATE_API_TOKEN", "").strip()
    if not value:
        sys.exit("REPLICATE_API_TOKEN is not set -- see .env.example")
    return value


def request(method: str, url: str, body: dict | None = None) -> dict:
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Authorization", "Bearer " + token())
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=180) as response:
            return json.loads(response.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:400]
        raise SystemExit("Replicate %s %s -> HTTP %d: %s" % (method, url, e.code, detail)) from e


def wait_for(prediction: dict, label: str) -> list[str]:
    """Poll a prediction to completion and return its output URLs."""
    url = prediction["urls"]["get"]
    deadline = time.time() + 300

    while time.time() < deadline:
        status = prediction.get("status")

        if status == "succeeded":
            output = prediction.get("output")
            if isinstance(output, str):
                return [output]
            return list(output or [])

        if status in ("failed", "canceled"):
            print("  [fail] %s: %s" % (label, prediction.get("error")))
            return []

        time.sleep(2)
        prediction = request("GET", url)

    print("  [fail] %s: timed out" % label)
    return []


def generate(prompt: str, aspect: str, label: str) -> str | None:
    prediction = request("POST", f"{API}/models/{FLUX}/predictions", {
        "input": {
            "prompt": prompt,
            "aspect_ratio": aspect,
            "output_format": "png",
            "output_quality": 100,
            "num_outputs": 1,
            # Four steps is what schnell is tuned for; more is slower without being better.
            "num_inference_steps": 4,
            "go_fast": True,
            "disable_safety_checker": False,
        },
    })
    urls = wait_for(prediction, label)
    return urls[0] if urls else None


def remove_background(image_url: str, label: str) -> str | None:
    prediction = request("POST", f"{API}/predictions", {
        "version": REMOVE_BG_VERSION,
        "input": {"image": image_url},
    })
    urls = wait_for(prediction, label + " (cutout)")
    return urls[0] if urls else None


def download(url: str, out: Path) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    urllib.request.urlretrieve(url, out)
    print("  [ok] %s (%d KB)" % (out.name, out.stat().st_size // 1024))


def produce(key: str, prompt: str, aspect: str, cutout: bool) -> None:
    out = OUT_DIR / (key + ".png")
    if out.exists():
        print("[skip] %s" % key)
        return

    print("[gen ] %s" % key)
    image_url = generate(prompt, aspect, key)
    if image_url is None:
        return

    if cutout:
        cut = remove_background(image_url, key)
        if cut is not None:
            image_url = cut
        else:
            print("  [warn] %s: background removal failed, keeping the flat version" % key)

    download(image_url, out)


def cmd_icons(_: list) -> None:
    for key, subject in ICONS.items():
        produce(key, subject + ". " + ICON_STYLE, "1:1", cutout=True)


def cmd_art(_: list) -> None:
    for key, (subject, aspect) in ART.items():
        produce(key, subject + ". " + ICON_STYLE, aspect, cutout=True)


def cmd_plates(_: list) -> None:
    for key, (subject, aspect) in PLATES.items():
        produce(key, subject + ". " + PLATE_STYLE, aspect, cutout=False)


def cmd_all(args: list) -> None:
    cmd_icons(args)
    cmd_art(args)
    cmd_plates(args)


def cmd_list(_: list) -> None:
    print("Icons (cut out): %d" % len(ICONS))
    for key, subject in ICONS.items():
        print("  %-18s %s" % (key, subject))
    print("\nArt (cut out): %d" % len(ART))
    for key, (subject, aspect) in ART.items():
        print("  %-18s %-5s %s" % (key, aspect, subject))
    print("\nPlates (opaque, nine-sliced): %d" % len(PLATES))
    for key, (subject, aspect) in PLATES.items():
        print("  %-18s %-5s %s" % (key, aspect, subject))


COMMANDS = {
    "icons": cmd_icons,
    "art": cmd_art,
    "plates": cmd_plates,
    "all": cmd_all,
    "list": cmd_list,
}

if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        sys.exit(__doc__)
    COMMANDS[sys.argv[1]](sys.argv[2:])
