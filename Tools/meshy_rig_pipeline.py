#!/usr/bin/env python3
"""Meshy rigging and animation pipeline for Living Diorama.

Takes the creature meshes produced by meshy_pipeline.py, rigs the humanoid ones, and
pulls a set of animation clips for each from Meshy's library. The results land in
Assets/Art/Creatures/<id>/Animations/ where Unity imports them as clips.

Only humanoids are rigged. A wolf on four legs and a slime with no limbs cannot be
fitted to a humanoid skeleton, and they do not need to be: the procedural animator
already gives them a gait, and a badly retargeted quadruped looks far worse than a
well tuned procedural one.

Every step is resumable -- task ids are cached in Tools/.meshy_rig_state.json -- so
re-running never re-spends credits.

Usage:
    set MESHY_API_KEY first (see .env.example)

    python Tools/meshy_rig_pipeline.py rig       [ids...]
    python Tools/meshy_rig_pipeline.py animate   [ids...]
    python Tools/meshy_rig_pipeline.py download  [ids...]
    python Tools/meshy_rig_pipeline.py status
    python Tools/meshy_rig_pipeline.py all
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
MESH_STATE = Path(__file__).resolve().parent / ".meshy_state.json"
STATE_PATH = Path(__file__).resolve().parent / ".meshy_rig_state.json"
ART_DIR = ROOT / "Assets" / "Art" / "Creatures"

# Which creatures stand on two legs with two arms. Everything else keeps the
# procedural animator.
HUMANOIDS = {
    "goblin": 1.2,     # character height in metres, used to scale the rig
    "knight": 1.8,
    "skeleton": 1.7,
}

# Behaviour name -> Meshy action id. These map one to one onto the utility AI's
# behaviours, so the animator can be driven straight from CreatureAgent.Current.Id.
CLIPS: dict[str, int] = {
    "idle": 11,          # Idle 1
    "walk": 30,          # Casual Walk
    "run": 16,           # Run Fast
    "attack": 4,         # Attack
    "hit": 178,          # Hit Reaction
    "knockout": 8,       # Dead
    "sleep": 269,        # Sleep
    "eat": 342,          # Stand and Drink
    "sneak": 559,        # Sneaky Walk
    "socialise": 290,    # Wave One Hand
    "celebrate": 59,     # Victory Cheer
}


def api_key() -> str:
    key = os.environ.get("MESHY_API_KEY", "").strip()
    if not key:
        sys.exit("MESHY_API_KEY is not set -- see .env.example")
    return key


class QueueFull(Exception):
    """The account already has as many tasks in flight as its plan allows."""


class RigUnusable(Exception):
    """The rig reports success but has no model behind it."""


def call(method: str, path: str, body: dict | None = None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header("Authorization", "Bearer " + api_key())
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            return json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:400]
        # A full queue is a normal condition on a small plan, not a failure: the
        # caller waits for the running tasks to finish and asks again.
        if e.code == 429 and "NoMorePendingTasks" in detail:
            raise QueueFull() from e
        # One creature failing to rig must not stop the others from being animated
        # and downloaded, so this is a skip rather than an abort.
        if e.code == 400 and "not rigged properly" in detail:
            raise RigUnusable() from e
        raise SystemExit("Meshy %s %s -> HTTP %d: %s" % (method, path, e.code, detail)) from e


def load(path: Path, default: dict) -> dict:
    return json.loads(path.read_text()) if path.exists() else default


def save_state(state: dict) -> None:
    STATE_PATH.write_text(json.dumps(state, indent=2))


def mesh_task_id(creature: str) -> str | None:
    entry = load(MESH_STATE, {"creatures": {}})["creatures"].get(creature, {})
    return entry.get("refine_id") or entry.get("preview_id")


def model_url(task_id: str) -> str | None:
    task = call("GET", "/v2/text-to-3d/" + task_id)
    return (task.get("model_urls") or {}).get("glb")


# ---------------------------------------------------------------- rigging

def cmd_rig(only: list) -> None:
    state = load(STATE_PATH, {"creatures": {}})

    for creature, height in HUMANOIDS.items():
        if only and creature not in only:
            continue

        entry = state["creatures"].setdefault(creature, {})
        if entry.get("rig_id"):
            print("[skip] %s: already rigged (%s)" % (creature, entry["rig_id"]))
            continue

        task_id = mesh_task_id(creature)
        if not task_id:
            print("[warn] %s: no mesh task; run meshy_pipeline.py first" % creature)
            continue

        url = model_url(task_id)
        if not url:
            print("[warn] %s: mesh has no glb yet" % creature)
            continue

        res = call("POST", "/v1/rigging", {
            "input_task_id": task_id,
            "model_url": url,
            "character_height": height,
        })
        entry["rig_id"] = res["result"]
        print("[queued] %s: rig %s" % (creature, entry["rig_id"]))
        save_state(state)


def rig_status(rig_id: str) -> dict:
    return call("GET", "/v1/rigging/" + rig_id)


# ------------------------------------------------------------- animations

def cmd_animate(only: list) -> None:
    state = load(STATE_PATH, {"creatures": {}})

    for creature, entry in state["creatures"].items():
        if only and creature not in only:
            continue
        if not entry.get("rig_id") or entry.get("rig_failed"):
            continue

        task = rig_status(entry["rig_id"])
        if task.get("status") != "SUCCEEDED":
            print("[wait] %s: rig %s %s%%" % (creature, task.get("status"), task.get("progress", 0)))
            continue

        animations = entry.setdefault("animations", {})
        for name, action_id in CLIPS.items():
            if name in animations:
                continue

            try:
                res = call("POST", "/v1/animations", {
                    "rig_task_id": entry["rig_id"],
                    "action_id": action_id,
                    "model_format": "fbx",
                })
            except QueueFull:
                print("[hold] queue is full; run again once the current batch finishes")
                return
            except RigUnusable:
                print("[skip] %s: rig produced no model; keeping the procedural animator"
                      % creature)
                entry["rig_failed"] = True
                save_state(state)
                break

            animations[name] = res["result"]
            print("[queued] %s/%s: %s" % (creature, name, res["result"]))
            save_state(state)


def animation_status(task_id: str) -> dict:
    return call("GET", "/v1/animations/" + task_id)


# ---------------------------------------------------------------- status

def cmd_status(only: list) -> None:
    state = load(STATE_PATH, {"creatures": {}})

    for creature, entry in state["creatures"].items():
        if only and creature not in only:
            continue

        if entry.get("rig_id"):
            task = rig_status(entry["rig_id"])
            print("%-10s rig      %-12s %3d%%" % (
                creature, task.get("status"), task.get("progress", 0)))

        for name, task_id in (entry.get("animations") or {}).items():
            task = animation_status(task_id)
            print("%-10s %-8s %-12s %3d%%" % (
                creature, name, task.get("status"), task.get("progress", 0)))


def cmd_poll(only: list) -> None:
    deadline = time.time() + 3600
    while time.time() < deadline:
        state = load(STATE_PATH, {"creatures": {}})
        pending = []

        for creature, entry in state["creatures"].items():
            if only and creature not in only:
                continue

            if entry.get("rig_id"):
                task = rig_status(entry["rig_id"])
                if task.get("status") in ("PENDING", "IN_PROGRESS"):
                    pending.append("%s:rig:%s%%" % (creature, task.get("progress", 0)))

            for name, task_id in (entry.get("animations") or {}).items():
                task = animation_status(task_id)
                if task.get("status") in ("PENDING", "IN_PROGRESS"):
                    pending.append("%s:%s" % (creature, name))

        if not pending:
            print("[poll] all settled")
            return

        print("[poll] " + " ".join(pending[:10]), flush=True)
        time.sleep(20)

    print("[poll] timed out")


# -------------------------------------------------------------- download

def download(url: str, out: Path) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    urllib.request.urlretrieve(url, out)
    print("  [ok] %s (%d KB)" % (out.name, out.stat().st_size // 1024))


def cmd_download(only: list) -> None:
    state = load(STATE_PATH, {"creatures": {}})

    for creature, entry in state["creatures"].items():
        if only and creature not in only:
            continue

        out_dir = ART_DIR / creature / "Animations"

        for name, task_id in (entry.get("animations") or {}).items():
            out = out_dir / ("%s@%s.fbx" % (creature, name))
            if out.exists():
                continue

            task = animation_status(task_id)
            if task.get("status") != "SUCCEEDED":
                print("[wait] %s/%s: %s" % (creature, name, task.get("status")))
                continue

            # The animation endpoint nests its files under "result", unlike the
            # text-to-3d endpoint which uses a "model_urls" map.
            urls = task.get("result") or {}
            url = urls.get("animation_fbx_url") or urls.get("animation_glb_url")
            if not url:
                print("[warn] %s/%s: no animation url (%s)" % (creature, name, list(urls)))
                continue

            print("[get ] %s/%s" % (creature, name))
            download(url, out)

            # The bare rigged mesh in its bind pose, saved once per creature: Unity
            # needs one skinned model to own the avatar that the clips retarget onto.
            armature = out_dir / ("%s_rig.fbx" % creature)
            if not armature.exists() and urls.get("processed_armature_fbx_url"):
                download(urls["processed_armature_fbx_url"], armature)


COMMANDS = {
    "rig": cmd_rig,
    "animate": cmd_animate,
    "status": cmd_status,
    "poll": cmd_poll,
    "download": cmd_download,
}


def total_wanted(state: dict, only: list) -> int:
    creatures = [c for c, e in state["creatures"].items()
                 if (not only or c in only) and not e.get("rig_failed")]
    return len(creatures) * len(CLIPS)


def cmd_all(args: list) -> None:
    """Rig, then fill the animation queue in as many passes as the plan needs."""
    cmd_rig(args)
    cmd_poll(args)

    for attempt in range(1, 30):
        cmd_animate(args)
        state = load(STATE_PATH, {"creatures": {}})

        queued = sum(len(e.get("animations") or {}) for c, e in state["creatures"].items()
                     if not args or c in args)
        wanted = total_wanted(state, args)

        print("[pass %d] %d/%d animations queued" % (attempt, queued, wanted))
        cmd_poll(args)
        cmd_download(args)

        if queued >= wanted:
            break


COMMANDS["all"] = cmd_all

if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        sys.exit(__doc__)
    COMMANDS[sys.argv[1]](sys.argv[2:])
