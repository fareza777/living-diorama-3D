#!/usr/bin/env python3
"""Meshy asset pipeline for Living Diorama.

Generates stylized low-poly creature meshes from text prompts, dropping the GLB into
Assets/StreamingAssets/Creatures/ and the render thumbnail into Assets/Art/Creatures/.
Every step is resumable: task ids are cached in Tools/.meshy_state.json so re-running
the script never re-spends credits.

Usage:
    set MESHY_API_KEY first (see .env.example)

    python Tools/meshy_pipeline.py preview [ids...]   start preview tasks
    python Tools/meshy_pipeline.py poll    [ids...]   block until tasks settle
    python Tools/meshy_pipeline.py refine  [ids...]   promote previews to textured refine
    python Tools/meshy_pipeline.py download[ids...]   fetch GLBs into the project
    python Tools/meshy_pipeline.py status
"""
from __future__ import annotations

import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

API = "https://api.meshy.ai/openapi"
ROOT = Path(__file__).resolve().parent.parent
STATE_PATH = Path(__file__).resolve().parent / ".meshy_state.json"
# GLBs ship in StreamingAssets so they can be loaded at runtime and swapped without
# a rebuild; thumbnails live in Assets so Unity imports them as collection icons.
GLB_DIR = ROOT / "Assets" / "StreamingAssets" / "Creatures"
THUMB_DIR = ROOT / "Assets" / "Art" / "Creatures"

STYLE = (
    "stylized low-poly 3D game asset, flat shaded faceted surfaces, clean readable "
    "silhouette, chunky exaggerated cartoon proportions, vibrant saturated colors, "
    "soft ambient occlusion, mobile game character, neutral A-pose, facing forward, "
    "full body, plain background"
)
NEGATIVE = (
    "photorealistic, gritty, gore, blood, text, watermark, multiple characters, "
    "base plate, pedestal, cluttered, thin fragile spikes, high poly noise, blurry texture"
)

CREATURES: dict[str, dict] = {
    "goblin": {
        "polycount": 7000,
        "prompt": "a mischievous little goblin with big pointed ears, wide grin showing one "
                  "crooked tooth, green skin, oversized head, tattered brown loincloth and a "
                  "tiny burlap sack strapped to its back, bare feet, hunched sneaky posture",
    },
    "slime": {
        "polycount": 3000,
        "prompt": "a cute translucent blue slime blob creature, glossy jelly body, rounded "
                  "teardrop shape, two simple shiny black dot eyes, tiny highlight bubbles "
                  "suspended inside the body, no limbs, sitting squat",
    },
    "wolf": {
        "polycount": 8000,
        "prompt": "a sleek grey timber wolf standing on four legs, thick fur ruff around the "
                  "neck, pointed ears up, bushy tail, amber eyes, alert hunting stance, "
                  "quadruped with a readable side silhouette",
    },
    "knight": {
        "polycount": 9000,
        "prompt": "a stout heroic knight in polished steel plate armor with a blue tabard, "
                  "closed visor helmet with a plume, round shield on the left arm, short sword "
                  "held at the side, sturdy boots, noble upright stance",
    },
    "skeleton": {
        "polycount": 7000,
        "prompt": "a cartoon skeleton warrior, clean white bones, hollow eye sockets with a "
                  "faint green glow, cracked ribcage, rusty small buckler shield, tattered dark "
                  "cloth wrap around the hips, standing upright",
    },
    "dragon": {
        "polycount": 10000,
        "prompt": "a small chubby red dragon whelp with oversized bat wings folded, short snout "
                  "with tiny smoke puffs, curved horns, spiked tail, golden belly scales, "
                  "standing on two hind legs, cute but menacing",
    },
}


def api_key() -> str:
    k = os.environ.get("MESHY_API_KEY", "").strip()
    if not k:
        sys.exit("MESHY_API_KEY is not set -- see .env.example")
    return k


def call(method: str, path: str, body: dict | None = None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"{API}{path}", data=data, method=method)
    req.add_header("Authorization", "Bearer " + api_key())
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:600]
        raise SystemExit("Meshy %s %s -> HTTP %d: %s" % (method, path, e.code, detail)) from e


def load_state() -> dict:
    if STATE_PATH.exists():
        return json.loads(STATE_PATH.read_text())
    return {"creatures": {}}


def save_state(state: dict) -> None:
    STATE_PATH.write_text(json.dumps(state, indent=2))


def active_task(entry: dict):
    """The most advanced task for a creature: refine wins over preview."""
    if entry.get("refine_id"):
        return "refine", entry["refine_id"]
    if entry.get("preview_id"):
        return "preview", entry["preview_id"]
    return None


def cmd_preview(only: list) -> None:
    state = load_state()
    for cid, spec in CREATURES.items():
        if only and cid not in only:
            continue
        entry = state["creatures"].setdefault(cid, {})
        if entry.get("preview_id"):
            print("[skip] %s: preview already queued (%s)" % (cid, entry["preview_id"]))
            continue
        res = call("POST", "/v2/text-to-3d", {
            "mode": "preview",
            "prompt": spec["prompt"] + ". " + STYLE,
            "negative_prompt": NEGATIVE,
            "art_style": "realistic",
            "ai_model": "meshy-5",
            "topology": "triangle",
            "target_polycount": spec["polycount"],
            "symmetry_mode": "auto",
            "should_remesh": True,
        })
        entry["preview_id"] = res["result"]
        print("[queued] %s: preview %s" % (cid, entry["preview_id"]))
        save_state(state)


def cmd_refine(only: list) -> None:
    state = load_state()
    for cid, entry in state["creatures"].items():
        if only and cid not in only:
            continue
        if entry.get("refine_id") or not entry.get("preview_id"):
            continue
        task = call("GET", "/v2/text-to-3d/" + entry["preview_id"])
        if task.get("status") != "SUCCEEDED":
            print("[wait] %s: preview %s %s%%" % (cid, task.get("status"), task.get("progress", 0)))
            continue
        res = call("POST", "/v2/text-to-3d", {
            "mode": "refine",
            "preview_task_id": entry["preview_id"],
            "enable_pbr": False,
            "texture_prompt": CREATURES[cid]["prompt"] + ". hand-painted stylized game texture, "
                              "bold flat color blocks, soft gradient shading, no photographic detail",
        })
        entry["refine_id"] = res["result"]
        print("[queued] %s: refine %s" % (cid, entry["refine_id"]))
        save_state(state)


def cmd_status(only: list) -> None:
    state = load_state()
    for cid, entry in state["creatures"].items():
        if only and cid not in only:
            continue
        t = active_task(entry)
        if not t:
            print("%-10s -" % cid)
            continue
        task = call("GET", "/v2/text-to-3d/" + t[1])
        print("%-10s %-8s %-12s %3d%%  %s" % (
            cid, t[0], task.get("status"), task.get("progress", 0), t[1]))


def cmd_download(only: list) -> None:
    state = load_state()
    for cid, entry in state["creatures"].items():
        if only and cid not in only:
            continue
        t = active_task(entry)
        if not t:
            continue
        stage, tid = t
        task = call("GET", "/v2/text-to-3d/" + tid)
        if task.get("status") != "SUCCEEDED":
            print("[wait] %s: %s %s %s%%" % (cid, stage, task.get("status"), task.get("progress", 0)))
            continue
        url = (task.get("model_urls") or {}).get("glb")
        if not url:
            print("[warn] %s: no glb url on %s task" % (cid, stage))
            continue
        GLB_DIR.mkdir(parents=True, exist_ok=True)
        out = GLB_DIR / (cid + ".glb")
        urllib.request.urlretrieve(url, out)

        if task.get("thumbnail_url"):
            thumb_dir = THUMB_DIR / cid
            thumb_dir.mkdir(parents=True, exist_ok=True)
            urllib.request.urlretrieve(task["thumbnail_url"], thumb_dir / (cid + "_thumb.png"))
        entry["downloaded_stage"] = stage
        save_state(state)
        print("[ok] %s -> %s (%d KB, %s)" % (cid, out.name, out.stat().st_size // 1024, stage))


def cmd_poll(only: list) -> None:
    deadline = time.time() + 2400
    while time.time() < deadline:
        state = load_state()
        pending = []
        for cid, entry in state["creatures"].items():
            if only and cid not in only:
                continue
            t = active_task(entry)
            if not t:
                continue
            task = call("GET", "/v2/text-to-3d/" + t[1])
            if task.get("status") in ("PENDING", "IN_PROGRESS"):
                pending.append("%s:%s:%s%%" % (cid, t[0], task.get("progress", 0)))
        if not pending:
            print("[poll] all tasks settled")
            return
        print("[poll] " + " ".join(pending), flush=True)
        time.sleep(20)
    print("[poll] timed out")


# Meshy's texturing stage sometimes returns a nearly colourless atlas -- the goblin
# and the wolf both came back as grey plastic while the slime came back vivid. These
# prompts lead with the colour and say so twice, and the negative prompt names the
# failure mode explicitly.
RETEXTURE: dict[str, str] = {
    "goblin": "vivid grass green skin, strongly saturated green, warm tan leather "
              "loincloth, amber yellow eyes, pink inner ears",
    "wolf": "rich warm grey brown fur with chestnut undertones, cream chest and muzzle, "
            "bright amber eyes, strongly saturated",
}

RETEXTURE_STYLE = (
    "hand-painted stylised game texture, bold flat colour blocks, high colour saturation, "
    "vibrant, clean readable colour zones, soft gradient shading, no photographic detail"
)

RETEXTURE_NEGATIVE = (
    "desaturated, greyscale, monochrome, washed out, colourless, grey, muddy, dull, "
    "pale, text, watermark"
)


def cmd_retexture(only: list) -> None:
    """Regenerate the albedo for creatures whose texture came back grey."""
    state = load_state()

    for cid, prompt in RETEXTURE.items():
        if only and cid not in only:
            continue

        entry = state["creatures"].setdefault(cid, {})
        if entry.get("retexture_id"):
            print("[skip] %s: retexture already queued (%s)" % (cid, entry["retexture_id"]))
            continue

        source = entry.get("refine_id") or entry.get("preview_id")
        if not source:
            print("[warn] %s: no mesh to retexture" % cid)
            continue

        task = call("GET", "/v2/text-to-3d/" + source)
        url = (task.get("model_urls") or {}).get("glb")
        if not url:
            print("[warn] %s: source mesh has no glb" % cid)
            continue

        res = call("POST", "/v1/retexture", {
            "input_task_id": source,
            "model_url": url,
            "text_style_prompt": prompt + ". " + RETEXTURE_STYLE,
            "negative_prompt": RETEXTURE_NEGATIVE,
            "enable_original_uv": True,
            "enable_pbr": False,
        })
        entry["retexture_id"] = res["result"]
        print("[queued] %s: retexture %s" % (cid, entry["retexture_id"]))
        save_state(state)


def retexture_status(task_id: str) -> dict:
    return call("GET", "/v1/retexture/" + task_id)


def cmd_retexture_download(only: list) -> None:
    """Replace the shipped GLB with the retextured one once it is ready."""
    state = load_state()

    for cid, entry in state["creatures"].items():
        if only and cid not in only:
            continue

        task_id = entry.get("retexture_id")
        if not task_id:
            continue

        task = retexture_status(task_id)
        if task.get("status") != "SUCCEEDED":
            print("[wait] %s: retexture %s %s%%" % (cid, task.get("status"), task.get("progress", 0)))
            continue

        urls = task.get("model_urls") or task.get("result") or {}
        url = urls.get("glb") if isinstance(urls, dict) else None
        if not url:
            print("[warn] %s: retexture has no glb (%s)" % (cid, urls if not isinstance(urls, dict) else list(urls)))
            continue

        GLB_DIR.mkdir(parents=True, exist_ok=True)
        out = GLB_DIR / (cid + ".glb")
        urllib.request.urlretrieve(url, out)
        print("[ok] %s -> %s (%d KB, retextured)" % (cid, out.name, out.stat().st_size // 1024))


COMMANDS = {
    "retexture": cmd_retexture,
    "retexture-download": cmd_retexture_download,
    "preview": cmd_preview,
    "refine": cmd_refine,
    "download": cmd_download,
    "poll": cmd_poll,
    "status": cmd_status,
}

if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        sys.exit(__doc__)
    COMMANDS[sys.argv[1]](sys.argv[2:])
