#!/usr/bin/env python3
"""Recraft interface-art pipeline for Living Diorama.

Generates the painted interface art described in docs/ui-redesign.md --
frames, plates, icons and box art --
then does the three things that decide whether the result is usable in Unity:

  * alpha-bleed  -- Recraft writes a checkerboard into the RGB channels underneath
                    transparent pixels. Imported as-is, bilinear filtering drags
                    grey into every edge and every plate gets a halo.
  * trim and pad -- so the ornament sits where the nine-slice border expects it.
  * downscale    -- the last art pass shipped 1024x1024 icons that render at 24
                    pixels, and 1536x640 button plates with no alpha channel at
                    all. Nothing here leaves at generation size.

Sprite borders are written to Assets/Resources/UI/Art/_import.json and applied by
the editor script Living Diorama > Import UI Art, rather than by hand-writing
.meta files -- a wrong guid in a .meta is a much worse failure than a wrong
border.

Every step is resumable: task state lives in Tools/.recraft_state.json, so a
re-run never re-spends credits on an asset that already came back.

Usage:
    set RECRAFT_API_KEY first (see .env.example)

    python Tools/recraft_ui_pipeline.py plan
    python Tools/recraft_ui_pipeline.py generate [keys...]
    python Tools/recraft_ui_pipeline.py report
"""
from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

try:
    from PIL import Image
    import numpy as np
except ImportError:
    sys.exit("this pipeline needs Pillow and numpy:  pip install pillow numpy")

API = "https://external.api.recraft.ai/v1"
ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "Assets" / "Resources" / "UI" / "Art"
STATE_PATH = Path(__file__).resolve().parent / ".recraft_state.json"
IMPORT_MANIFEST = OUT_DIR / "_import.json"

CREDITS_PER_IMAGE = 40

# What keeps the set looking like one set. Appended to every prompt.
#
# Note what is *not* in here: any instruction about flatness or perspective. Nine
# generations went into finding out that telling this model "flat, orthographic, no
# perspective" does nothing, and that fighting it produces worse output than
# letting it draw an object the way it wants to. See PAINTABLE below.
SUFFIX = (
    "Stylised hand-painted fantasy game art, warm gold and brass with deep navy "
    "shadow, bold readable silhouette, viewed straight on, crisp clean edges, "
    "no text, no letters, no numbers, no shadow cast on the ground, isolated on a "
    "fully transparent background"
)

# Why this file only generates twenty-three assets and not the fifty a UI needs.
#
# Recraft V3 draws objects well and interface primitives badly. Measured, not
# assumed -- these were the four failures that decided it:
#
#   button plate  -> a photographic brass doorbell in three-quarter perspective
#                    with the word "PRESS" engraved across the middle
#   button, retry -> same, with harder negative wording; still 3D, still lettered
#   button, flat  -> an illustration *of* a game UI mockup, gibberish text, Mario
#   coin icon     -> a dollar sign stamped on the coins and a baked drop shadow
#
# whereas an ornate panel frame, a key, a chest and a scroll all came back usable
# on the first attempt. So the split is: anything that is a *thing* is painted
# here; anything that is a rounded rectangle with a rim is drawn in USS, where it
# costs no texture memory, stays sharp at every density, and can be tinted per
# state; and anything that is a geometric glyph -- a cross, a tick, a chevron --
# is generated as a signed distance field in code, which this project already does
# for the mood icons in EmoteIcons.cs.

ICON_TEMPLATE = "A single {subject}. " + SUFFIX + "."

RARITY_TEMPLATE = (
    "A square rounded ornate picture frame, hollow and completely empty in the middle "
    "so artwork can show through. The frame band is {metal}. Decoration confined to the "
    "frame band itself. " + SUFFIX + "."
)


def icon(key: str, subject: str) -> dict:
    return {
        "key": key,
        "gen": "1024x1024",
        "ship": (128, 128),
        "border": None,
        "prompt": ICON_TEMPLATE.format(subject=subject),
    }


def rarity(key: str, metal: str) -> dict:
    return {
        "key": key,
        "gen": "1024x1024",
        "ship": (256, 256),
        "border": (32, 32, 32, 32),
        "prompt": RARITY_TEMPLATE.format(metal=metal),
    }


# border is (left, bottom, right, top) in shipped pixels, or None for "do not slice".
ASSETS: list[dict] = [
    # ---- painted plates, nine-sliced -----------------------------------------
    {
        "key": "panel_modal",
        "gen": "1024x1024",
        "ship": (512, 512),
        "border": (64, 64, 64, 64),
        "prompt": "A tall rectangular ornate panel of dark navy-black glass in a thin warm "
                  "brass frame, with small filigree scrollwork in the four corners only. "
                  "The centre of the panel is plain empty glass with no decoration on it. "
                  + SUFFIX + ".",
    },
    {
        "key": "band_header",
        "gen": "1536x1024",
        "ship": (512, 160),
        "border": (48, 24, 48, 24),
        "prompt": "A long narrow horizontal brass nameplate bar of dark navy glass, with a "
                  "small brass scroll ornament at its far left tip and another at its far "
                  "right tip. The long middle stretch is plain empty glass. " + SUFFIX + ".",
    },
    rarity("frame_common", "plain dull pewter, very simple, no decoration"),
    rarity("frame_uncommon", "weathered bronze with a small leaf motif at the top centre"),
    rarity("frame_rare", "polished blue steel with a single faceted gem at the top centre"),
    rarity("frame_epic", "dark violet metal with ornate scrollwork along the top and bottom"),
    rarity("frame_legendary", "radiant gold with elaborate filigree at all four corners "
                              "and a small crown at the top centre"),

    # ---- painted icons -------------------------------------------------------
    icon("icon_coin", "ornate fantasy gold medallion coin with a smooth blank unmarked face"),
    icon("icon_essence", "teardrop-shaped glass vial of glowing teal liquid, corked with brass"),
    icon("icon_key", "ornate old brass key with a looped bow and a simple toothed bit"),
    icon("icon_chest", "small closed treasure chest of dark wood with brass bands and a lock plate"),
    icon("icon_collection", "open leather-bound book with brass corner fittings"),
    icon("icon_chronicle", "rolled parchment scroll tied with a red ribbon"),
    icon("icon_expand", "small pennant flag on a wooden pole planted in a patch of grass"),
    icon("icon_settings", "brass cog wheel with eight thick teeth"),
    icon("icon_spin", "brass fortune wheel with eight coloured segments"),
    icon("icon_lock", "closed brass padlock"),
    icon("icon_clock", "brass sundial with a triangular gnomon"),
    icon("icon_paw", "small rounded animal paw print"),

    # ---- painted illustrations ----------------------------------------------
    {
        "key": "box_wooden",
        "gen": "1024x1024",
        "ship": (512, 512),
        "border": None,
        "prompt": "A closed rustic wooden treasure chest bound with plain iron bands, humble "
                  "and worn, sitting closed and still. " + SUFFIX + ".",
    },
    {
        "key": "box_arcane",
        "gen": "1024x1024",
        "ship": (512, 512),
        "border": None,
        "prompt": "A closed ornate arcane chest of dark wood and violet crystal with faintly "
                  "glowing runes along its bands, sitting closed and still. " + SUFFIX + ".",
    },
    {
        "key": "rarity_burst",
        "gen": "1024x1024",
        "ship": (512, 512),
        "border": None,
        "prompt": "A radial burst of light rays and small sparkles spreading from a single "
                  "centre point, white and pale gold, soft at the outer edge, nothing at all "
                  "in the middle. " + SUFFIX + ".",
    },
    {
        "key": "title_emblem",
        "gen": "1024x1024",
        "ship": (512, 512),
        "border": None,
        "prompt": "An ornate circular brass emblem containing a tiny stylised floating island "
                  "with one tree on it, filigree running around the rim. " + SUFFIX + ".",
    },
]

BY_KEY = {a["key"]: a for a in ASSETS}


# ---------------------------------------------------------------------- api

def api_key() -> str:
    key = os.environ.get("RECRAFT_API_KEY", "").strip()
    if not key:
        sys.exit("RECRAFT_API_KEY is not set -- see .env.example")
    return key


def generate(asset: dict) -> str:
    """Ask Recraft for one image and return its url."""
    body = {
        "model": "recraftv3",
        "style": "digital_illustration",
        "size": asset["gen"],
        "prompt": asset["prompt"] + " " + SUFFIX,
    }
    request = urllib.request.Request(
        API + "/images/generations", data=json.dumps(body).encode(), method="POST")
    request.add_header("Authorization", "Bearer " + api_key())
    request.add_header("Content-Type", "application/json")

    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            payload = json.loads(response.read().decode())
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors="replace")[:400]
        raise SystemExit("Recraft HTTP %d: %s" % (error.code, detail)) from error

    return payload["data"][0]["url"]


def load_state() -> dict:
    return json.loads(STATE_PATH.read_text()) if STATE_PATH.exists() else {"assets": {}}


def save_state(state: dict) -> None:
    STATE_PATH.write_text(json.dumps(state, indent=2))


# ------------------------------------------------------------ post-process

def alpha_bleed(rgba: np.ndarray, rounds: int = 12) -> np.ndarray:
    """
    Push colour outward into the transparent margin.

    Recraft leaves a checkerboard in the RGB channels where alpha is zero. The
    alpha is correct, so the image looks right -- until Unity filters it, and
    every edge pixel blends toward grey. Flooding the opaque colour outward first
    means there is nothing wrong to blend with.
    """
    out = rgba.astype(np.float32).copy()
    known = out[..., 3] > 8

    for _ in range(rounds):
        if known.all():
            break

        # Average of whichever neighbours already have a colour.
        total = np.zeros(out.shape[:2] + (3,), np.float32)
        count = np.zeros(out.shape[:2], np.float32)

        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(out[..., :3], (dy, dx), axis=(0, 1))
            mask = np.roll(known, (dy, dx), axis=(0, 1)).astype(np.float32)
            total += shifted * mask[..., None]
            count += mask

        fillable = (~known) & (count > 0)
        safe = np.maximum(count, 1)[..., None]
        out[..., :3] = np.where(fillable[..., None], total / safe, out[..., :3])
        known = known | fillable

    return out.astype(np.uint8)


def trim_to_alpha(rgba: np.ndarray, pad: int = 2) -> np.ndarray:
    """Crop to what is actually drawn, keeping a hair of margin."""
    rows = np.any(rgba[..., 3] > 8, axis=1)
    cols = np.any(rgba[..., 3] > 8, axis=0)
    if not rows.any() or not cols.any():
        return rgba

    y0, y1 = np.where(rows)[0][[0, -1]]
    x0, x1 = np.where(cols)[0][[0, -1]]

    y0 = max(0, y0 - pad)
    x0 = max(0, x0 - pad)
    y1 = min(rgba.shape[0] - 1, y1 + pad)
    x1 = min(rgba.shape[1] - 1, x1 + pad)

    return rgba[y0:y1 + 1, x0:x1 + 1]


def process(url: str, asset: dict) -> dict:
    """Download, clean up and write one asset. Returns a line for the report."""
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    raw = OUT_DIR / ("_raw_" + asset["key"] + ".png")
    urllib.request.urlretrieve(url, raw)

    source = Image.open(raw).convert("RGBA")
    source.load()
    width, height = source.size
    rgba = np.array(source.getdata(), dtype=np.uint8).reshape(height, width, 4)

    coverage = float((rgba[..., 3] > 8).mean())

    rgba = alpha_bleed(trim_to_alpha(rgba))

    image = Image.fromarray(rgba).resize(tuple(asset["ship"]), Image.LANCZOS)
    destination = OUT_DIR / (asset["key"] + ".png")
    image.save(destination, "PNG", optimize=True)
    raw.unlink(missing_ok=True)

    return {
        "key": asset["key"],
        "size": list(asset["ship"]),
        "border": list(asset["border"]) if asset["border"] else None,
        "coverage": round(coverage, 3),
        "bytes": destination.stat().st_size,
    }


def write_manifest(state: dict) -> None:
    """What the editor importer reads: which sprites get which slice border."""
    entries = []
    for asset in ASSETS:
        record = state["assets"].get(asset["key"])
        if not record or "size" not in record:
            continue
        entries.append({
            "key": asset["key"],
            "border": record["border"],
            "maxSize": max(record["size"]),
        })

    IMPORT_MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    IMPORT_MANIFEST.write_text(json.dumps({"sprites": entries}, indent=2))


# ------------------------------------------------------------------ commands

def cmd_plan(only: list) -> None:
    wanted = [a for a in ASSETS if not only or a["key"] in only]
    sliced = sum(1 for a in wanted if a["border"])

    print("%-24s %-11s %-11s %s" % ("key", "generate", "ship", "9-slice border"))
    print("-" * 66)
    for asset in wanted:
        border = "-" if not asset["border"] else ",".join(str(v) for v in asset["border"])
        print("%-24s %-11s %-11s %s"
              % (asset["key"], asset["gen"], "x".join(str(v) for v in asset["ship"]), border))

    print("-" * 66)
    print("%d assets, %d of them nine-sliced" % (len(wanted), sliced))
    print("%d credits at %d each" % (len(wanted) * CREDITS_PER_IMAGE, CREDITS_PER_IMAGE))


def cmd_generate(only: list) -> None:
    state = load_state()
    wanted = [a for a in ASSETS if not only or a["key"] in only]

    for asset in wanted:
        key = asset["key"]
        done = state["assets"].get(key)

        if done and (OUT_DIR / (key + ".png")).exists():
            print("[skip] %s: already on disk" % key)
            continue

        print("[gen ] %s ..." % key, flush=True)
        url = generate(asset)
        record = process(url, asset)

        state["assets"][key] = record
        save_state(state)
        write_manifest(state)

        print("[done] %s  %sx%s  %.0f%% opaque  %d KB"
              % (key, record["size"][0], record["size"][1],
                 record["coverage"] * 100, record["bytes"] // 1024))

    print("\nnow run  Living Diorama > Import UI Art  in the editor to apply the borders")


def cmd_report(only: list) -> None:
    state = load_state()
    wanted = [a for a in ASSETS if not only or a["key"] in only]

    missing = 0
    total = 0

    print("%-24s %-10s %-9s %-7s %s" % ("key", "size", "opaque", "KB", "note"))
    print("-" * 70)

    for asset in wanted:
        record = state["assets"].get(asset["key"])
        path = OUT_DIR / (asset["key"] + ".png")

        if not record or not path.exists():
            missing += 1
            print("%-24s %-10s %-9s %-7s %s" % (asset["key"], "-", "-", "-", "not generated"))
            continue

        total += record["bytes"]

        # An asset that came back with no transparent margin is a picture of a
        # button rather than a button, which is how the last set failed.
        note = ""
        if record["coverage"] > 0.985:
            note = "no transparent margin -- regenerate"
        elif asset["border"] and record["coverage"] < 0.25:
            note = "mostly empty -- check the ornament survived the trim"

        print("%-24s %-10s %-9s %-7s %s"
              % (asset["key"],
                 "%dx%d" % tuple(record["size"]),
                 "%.0f%%" % (record["coverage"] * 100),
                 record["bytes"] // 1024,
                 note))

    print("-" * 70)
    print("%d of %d generated, %d KB total" % (len(wanted) - missing, len(wanted), total // 1024))


def main() -> None:
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__)

    command, only = args[0], args[1:]

    unknown = [k for k in only if k not in BY_KEY]
    if unknown:
        sys.exit("unknown asset key(s): " + ", ".join(unknown))

    if command == "plan":
        cmd_plan(only)
    elif command == "generate":
        cmd_generate(only)
    elif command == "report":
        cmd_report(only)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
