#!/usr/bin/env python3
"""Pull the albedo texture out of each creature GLB.

Meshy's rigged FBX exports carry the mesh and the skeleton but not the texture, so a
rigged creature imports into Unity as untextured grey plastic while the same creature
looks right on Meshy's own site. The texture is sitting in the original GLB the whole
time; this lifts it out as a PNG that Unity imports normally and the creature shader
can be pointed at.

Usage:
    python Tools/extract_glb_textures.py
"""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
# Creature textures sit beside the creature art and are referenced by the Creature
# asset. Prop textures go into Resources instead: the unboxing props are loaded by
# name at runtime with no asset to hang a reference on.
SOURCES = [
    (ROOT / "Assets" / "StreamingAssets" / "Creatures",
     ROOT / "Assets" / "Art" / "Creatures", True),
    (ROOT / "Assets" / "StreamingAssets" / "Props",
     ROOT / "Assets" / "Resources" / "Props", False),
]

# GLB chunk types, little-endian ASCII as stored in the header.
CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942

EXTENSIONS = {"image/png": ".png", "image/jpeg": ".jpg"}


def read_glb(path: Path) -> tuple[dict, bytes]:
    """Return the glTF json and the binary chunk of a .glb file."""
    with path.open("rb") as handle:
        magic, version, _total = struct.unpack("<III", handle.read(12))
        if magic != 0x46546C67:
            raise ValueError(f"{path.name} is not a GLB (magic {magic:#x})")
        if version != 2:
            raise ValueError(f"{path.name} is GLB version {version}, expected 2")

        gltf: dict = {}
        binary = b""

        while True:
            header = handle.read(8)
            if len(header) < 8:
                break

            length, kind = struct.unpack("<II", header)
            payload = handle.read(length)

            if kind == CHUNK_JSON:
                gltf = json.loads(payload.decode("utf-8"))
            elif kind == CHUNK_BIN:
                binary = payload

        return gltf, binary


def base_colour_image(gltf: dict) -> int | None:
    """Index of the image used as base colour, preferring the material's own reference."""
    for material in gltf.get("materials") or []:
        pbr = material.get("pbrMetallicRoughness") or {}
        texture_ref = pbr.get("baseColorTexture")
        if not texture_ref:
            continue

        texture = (gltf.get("textures") or [])[texture_ref["index"]]
        source = texture.get("source")
        if source is not None:
            return source

    # No material said so explicitly, but a single image is unambiguous enough.
    images = gltf.get("images") or []
    return 0 if len(images) == 1 else None


def extract(path: Path, art_dir: Path, in_subfolder: bool) -> bool:
    creature = path.stem
    gltf, binary = read_glb(path)

    index = base_colour_image(gltf)
    if index is None:
        print(f"[warn] {creature}: no base colour image in the glb")
        return False

    image = (gltf.get("images") or [])[index]
    view = (gltf.get("bufferViews") or [])[image["bufferView"]]

    offset = view.get("byteOffset", 0)
    data = binary[offset:offset + view["byteLength"]]

    suffix = EXTENSIONS.get(image.get("mimeType", ""), ".png")
    out_dir = art_dir / creature if in_subfolder else art_dir
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / f"{creature}_albedo{suffix}"
    out.write_bytes(data)

    print(f"[ok] {creature}: {out.name} ({len(data) // 1024} KB)")
    return True


def main() -> int:
    extracted = 0
    for glb_dir, art_dir, in_subfolder in SOURCES:
        if not glb_dir.exists():
            print(f"[skip] nothing at {glb_dir}")
            continue

        for path in sorted(glb_dir.glob("*.glb")):
            try:
                if extract(path, art_dir, in_subfolder):
                    extracted += 1
            except Exception as error:  # a bad file must not stop the rest
                print(f"[fail] {path.name}: {error}")

    print(f"{extracted} texture(s) extracted")
    return 0 if extracted else 1


if __name__ == "__main__":
    raise SystemExit(main())
