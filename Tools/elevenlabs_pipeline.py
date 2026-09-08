#!/usr/bin/env python3
"""ElevenLabs audio pipeline for Living Diorama.

Generates the narrator voice-over, the interface and world sound effects, and the
looping biome ambience straight into Assets/Resources/Audio/, where Unity imports
them as AudioClips. Files already on disk are skipped, so re-running costs nothing.

Usage:
    set ELEVENLABS_API_KEY first (see .env.example)

    python Tools/elevenlabs_pipeline.py voice     narrator lines
    python Tools/elevenlabs_pipeline.py sfx       one-shot sound effects
    python Tools/elevenlabs_pipeline.py ambience  looping beds
    python Tools/elevenlabs_pipeline.py all
    python Tools/elevenlabs_pipeline.py list      show what would be generated
"""
from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

API = "https://api.elevenlabs.io/v1"
ROOT = Path(__file__).resolve().parent.parent
AUDIO_DIR = ROOT / "Assets" / "Resources" / "Audio"

# George: warm, unhurried, faintly amused British storyteller. The narrator is a
# presence in the room rather than a tutorial system, so the read matters as much
# as the words.
NARRATOR_VOICE = "JBFqnCBsd6RMkjVDRZzb"
TTS_MODEL = "eleven_multilingual_v2"

VOICE_SETTINGS = {
    "stability": 0.45,          # some variation keeps it from sounding read-aloud
    "similarity_boost": 0.80,
    "style": 0.35,
    "use_speaker_boost": True,
}

# --------------------------------------------------------------------- script
# Written to be heard once and remembered, not skipped. Nothing explains a button;
# the narrator describes what the player is about to feel and gets out of the way.

VOICE_LINES: dict[str, str] = {
    # --- title sequence -----------------------------------------------------
    "intro_01": "Somewhere on a quiet shelf, in a box no bigger than your two hands, "
                "a small world is waking up.",
    "intro_02": "You will not command it. You will not steer it. "
                "You will open the box, set someone down, and see what they decide to do.",
    "intro_03": "Welcome to your living diorama.",

    # --- onboarding ---------------------------------------------------------
    "tutor_box": "Every box holds someone. Go on. Open it.",
    "tutor_place": "There. Set them down, and let them be. "
                   "From here, everything they do is their own idea.",
    "tutor_watch": "Watch a moment. The hunger is theirs. So is the curiosity, and the fear.",
    "tutor_interact": "Ah. They have noticed one another. "
                      "This is the part worth staying for.",
    "tutor_economy": "Every moment you witness is worth something. "
                     "A livelier diorama is a richer one.",
    "tutor_expand": "The world can be larger. New ground brings new neighbours, "
                    "and new trouble.",
    "tutor_done": "That is enough from me. The rest belongs to them. "
                  "Come back often. A great deal happens while you are away.",

    # --- milestones ---------------------------------------------------------
    "milestone_first_night": "The light is going. Some of them have been waiting all day for this.",
    "milestone_theft": "Did you see that? Somebody has just helped themselves.",
    "milestone_chase": "And now the reckoning.",
    "milestone_legendary": "Oh. Now that is rare.",
    "milestone_expand": "More ground. More room to get into things.",

    # --- returning ----------------------------------------------------------
    "welcome_back": "You were missed. Mostly by the ones who wanted feeding.",
}

# ------------------------------------------------------------------- effects
# Prompts describe the sound rather than name an instrument, which is what the
# generator responds to best. Everything is kept short and dry so it sits under
# the ambience instead of fighting it.

SFX: dict[str, tuple[str, float]] = {
    "ui_tap": ("a soft muted wooden tap, single short click, warm and gentle, no reverb", 1.0),
    "ui_back": ("a soft low wooden thud, single short click, gentle", 1.0),
    "box_open": ("a small wooden chest creaking open followed by a soft magical sparkle chime, "
                 "cosy storybook fantasy", 2.5),
    "reveal_common": ("a short warm wooden chime, single soft note, pleasant", 1.5),
    "reveal_rare": ("a bright ascending magical chime with light sparkle shimmer, rewarding, short", 2.0),
    "reveal_legendary": ("a grand triumphant magical fanfare with deep resonance and shimmering "
                         "sparkles, short and majestic", 3.5),
    "coin": ("a small soft coin pickup chime, gentle and bright, very short", 0.8),
    "level_up": ("a warm ascending arpeggio of soft bells, achievement, cosy", 2.0),
    "creature_spawn": ("a soft magical poof with a gentle whoosh, something small materialising", 1.5),
    "creature_hurt": ("a soft muffled thud with a small squeak, cartoon impact, harmless", 0.8),
    "water_splash": ("a small playful splash in a shallow puddle, light and wet", 1.2),
    "footstep_soft": ("a single soft footstep on mossy earth, quiet and close", 0.6),
    "expand_tile": ("a deep earthy rumble as ground rises and stone settles into place, "
                    "satisfying and grounded", 3.0),
    "night_fall": ("a soft low swelling drone with distant wind, dusk settling, calm", 4.0),
}

AMBIENCE: dict[str, tuple[str, float]] = {
    "amb_forest_day": ("gentle woodland ambience, distant birdsong, soft breeze through leaves, "
                       "calm and continuous with no sudden events", 22.0),
    "amb_forest_night": ("quiet night woodland ambience, crickets, faint owl, soft cool wind, "
                         "calm and continuous with no sudden events", 22.0),
    "amb_cave": ("deep cave ambience, slow water drips, distant hollow air movement, "
                 "calm and continuous", 22.0),
    "amb_menu": ("very soft warm music bed, slow gentle marimba and soft pad, cosy and hopeful, "
                 "unobtrusive, continuous", 22.0),
}


def api_key() -> str:
    key = os.environ.get("ELEVENLABS_API_KEY", "").strip()
    if not key:
        sys.exit("ELEVENLABS_API_KEY is not set -- see .env.example")
    return key


def post(path: str, body: dict, out_path: Path) -> bool:
    """POST and stream the audio response straight to disk."""
    request = urllib.request.Request(
        API + path,
        data=json.dumps(body).encode(),
        method="POST",
        headers={"xi-api-key": api_key(), "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            payload = response.read()
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:400]
        print("  [fail] HTTP %d: %s" % (e.code, detail))
        return False
    except urllib.error.URLError as e:
        print("  [fail] %s" % e.reason)
        return False

    if len(payload) < 512:
        print("  [fail] response too small to be audio (%d bytes)" % len(payload))
        return False

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(payload)
    print("  [ok] %s (%d KB)" % (out_path.name, len(payload) // 1024))
    return True


def cmd_voice(_: list) -> None:
    out_dir = AUDIO_DIR / "VO"
    for key, text in VOICE_LINES.items():
        out = out_dir / (key + ".mp3")
        if out.exists():
            print("[skip] %s" % key)
            continue
        print("[gen ] %s: %s" % (key, text[:60] + ("..." if len(text) > 60 else "")))
        post("/text-to-speech/" + NARRATOR_VOICE, {
            "text": text,
            "model_id": TTS_MODEL,
            "voice_settings": VOICE_SETTINGS,
        }, out)


def generate_effects(table: dict, subfolder: str) -> None:
    out_dir = AUDIO_DIR / subfolder
    for key, (prompt, duration) in table.items():
        out = out_dir / (key + ".mp3")
        if out.exists():
            print("[skip] %s" % key)
            continue
        print("[gen ] %s (%.1fs)" % (key, duration))
        post("/sound-generation", {
            "text": prompt,
            "duration_seconds": duration,
            # Lean on the prompt rather than letting the model improvise; these have to
            # sit in a mix with a dozen other cues.
            "prompt_influence": 0.55,
        }, out)


def cmd_sfx(_: list) -> None:
    generate_effects(SFX, "SFX")


def cmd_ambience(_: list) -> None:
    generate_effects(AMBIENCE, "Ambience")


def cmd_all(args: list) -> None:
    cmd_voice(args)
    cmd_sfx(args)
    cmd_ambience(args)


def cmd_list(_: list) -> None:
    print("Voice lines (%d):" % len(VOICE_LINES))
    for key, text in VOICE_LINES.items():
        print("  %-24s %s" % (key, text))
    print("\nSound effects (%d):" % len(SFX))
    for key, (prompt, duration) in SFX.items():
        print("  %-20s %4.1fs  %s" % (key, duration, prompt))
    print("\nAmbience (%d):" % len(AMBIENCE))
    for key, (prompt, duration) in AMBIENCE.items():
        print("  %-20s %4.1fs  %s" % (key, duration, prompt))


COMMANDS = {
    "voice": cmd_voice,
    "sfx": cmd_sfx,
    "ambience": cmd_ambience,
    "all": cmd_all,
    "list": cmd_list,
}

if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        sys.exit(__doc__)
    COMMANDS[sys.argv[1]](sys.argv[2:])
