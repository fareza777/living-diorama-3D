#!/usr/bin/env python3
"""Generate the diorama's scenery props with Meshy.

The terrain has to stay procedural -- the simulation asks it for heights thousands of
times a second and the expand system builds new tiles that must meet their neighbours --
but nothing about the *things standing on it* needs to be. Trees, rocks and mushrooms
were cones and blobs assembled from primitives, and they are the first thing anyone
looks at.

Same shape as the creature pipeline, kept separate so it cannot disturb that state file.

    python Tools/meshy_props_pipeline.py preview
    python Tools/meshy_props_pipeline.py refine
    python Tools/meshy_props_pipeline.py download
"""
from __future__ import annotations

import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GLB_DIR = ROOT / "Assets" / "StreamingAssets" / "Props"
STATE_PATH = ROOT / "Tools" / ".meshy_props_state.json"
API = "https://api.meshy.ai/openapi"

STYLE = (
    "stylized low poly game asset, hand painted texture, clean flat faceted surfaces, "
    "warm saturated colours, single object, plain background, no ground plane"
)
NEGATIVE = (
    "photorealistic, gritty, text, watermark, multiple objects, base plate, pedestal, "
    "terrain, grass patch, cluttered, thin fragile geometry, high poly noise, blurry texture"
)

PROPS: dict[str, dict] = {
    "pine_tree": {
        "polycount": 2500,
        "prompt": "a stylized conifer pine tree with a straight brown trunk and three tiers "
                  "of dark green needled foliage, tapering to a point at the top",
    },
    "broadleaf_tree": {
        "polycount": 3000,
        "prompt": "a stylized broadleaf tree with a sturdy curved brown trunk and a full "
                  "rounded canopy of bright green leaves",
    },
    "rock": {
        "polycount": 1200,
        "prompt": "a single weathered grey granite boulder with flat chipped faces and "
                  "moss in the crevices",
    },
    "mushroom": {
        "polycount": 900,
        "prompt": "a cluster of three storybook mushrooms with fat cream stalks and domed "
                  "red caps speckled with white spots",
    },
    "bush": {
        "polycount": 1400,
        "prompt": "a low rounded leafy shrub, dense mid-green foliage with a few small "
                  "berries, no visible soil",
    },
}


def api_key() -> str:
    env = ROOT / ".env"
    if env.exists():
        for line in env.read_text().splitlines():
            if line.startswith("MESHY_API_KEY="):
                return line.split("=", 1)[1].strip()
    raise SystemExit("MESHY_API_KEY not found in .env")


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
        detail = e.read().decode(errors="replace")[:400]
        raise SystemExit("Meshy %s %s -> HTTP %d: %s" % (method, path, e.code, detail)) from e


def load_state() -> dict:
    return json.loads(STATE_PATH.read_text()) if STATE_PATH.exists() else {"props": {}}


def save_state(state: dict) -> None:
    STATE_PATH.write_text(json.dumps(state, indent=2))


def cmd_preview(only: list) -> None:
    state = load_state()
    for pid, spec in PROPS.items():
        if only and pid not in only:
            continue
        entry = state["props"].setdefault(pid, {})
        if entry.get("preview_id"):
            print("[skip] %s: already queued" % pid)
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
        print("[queued] %s: preview %s" % (pid, entry["preview_id"]))
        save_state(state)


def status(task_id: str) -> dict:
    return call("GET", "/v2/text-to-3d/" + task_id)


def cmd_refine(only: list) -> None:
    """Texture each preview once its geometry is done."""
    state = load_state()
    for pid, entry in state["props"].items():
        if only and pid not in only:
            continue
        if entry.get("refine_id") or not entry.get("preview_id"):
            continue

        task = status(entry["preview_id"])
        if task.get("status") != "SUCCEEDED":
            print("[wait] %s: preview %s %s%%" % (pid, task.get("status"), task.get("progress", 0)))
            continue

        res = call("POST", "/v2/text-to-3d", {
            "mode": "refine",
            "preview_task_id": entry["preview_id"],
            "enable_pbr": False,
        })
        entry["refine_id"] = res["result"]
        print("[queued] %s: refine %s" % (pid, entry["refine_id"]))
        save_state(state)


def cmd_download(only: list) -> None:
    state = load_state()
    GLB_DIR.mkdir(parents=True, exist_ok=True)
    done = 0

    for pid, entry in state["props"].items():
        if only and pid not in only:
            continue

        task_id = entry.get("refine_id") or entry.get("preview_id")
        if not task_id:
            continue

        task = status(task_id)
        if task.get("status") != "SUCCEEDED":
            print("[wait] %s: %s %s%%" % (pid, task.get("status"), task.get("progress", 0)))
            continue

        url = (task.get("model_urls") or {}).get("glb")
        if not url:
            print("[warn] %s: no glb on the finished task" % pid)
            continue

        out = GLB_DIR / f"{pid}.glb"
        with urllib.request.urlopen(url, timeout=300) as r:
            out.write_bytes(r.read())

        entry["downloaded"] = "refine" if entry.get("refine_id") else "preview"
        save_state(state)
        print("[ok] %s -> %s (%d KB)" % (pid, out.name, out.stat().st_size // 1024))
        done += 1

    print("%d prop(s) downloaded" % done)


COMMANDS = {"preview": cmd_preview, "refine": cmd_refine, "download": cmd_download}


def main() -> int:
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        return 1
    COMMANDS[sys.argv[1]](sys.argv[2:])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
