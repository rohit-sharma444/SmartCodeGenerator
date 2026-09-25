"""Turns narration.json into one audio file per scene, plus the captions' timings.

For each scene this writes out/voice/<id>.mp3 and out/voice/<id>.json:

    {"duration": 7.41, "captions": [{"start": 0.1, "end": 3.2, "text": "..."}, ...]}

The captions come from the voice's own word timings, so they change on the word the narrator is
saying rather than on a guess. A caption is a sentence, or part of a long one split at a comma, so
no line is too long to read at a glance.

Needs `pip install edge-tts imageio-ffmpeg`. edge-tts uses Microsoft's online neural voices: the
narration text is sent to that service, and nothing else is.
"""

import asyncio
import json
import re
import subprocess
import sys
from pathlib import Path

import edge_tts
import imageio_ffmpeg

HERE = Path(__file__).resolve().parent
OUT = HERE / "out" / "voice"
MAX_CAPTION = 90


def chunks(text: str) -> list[str]:
    """Sentences, with any longer than MAX_CAPTION split at commas, colons or semicolons."""
    result = []
    for sentence in re.split(r"(?<=[.!?])\s+", text.strip()):
        if len(sentence) <= MAX_CAPTION:
            result.append(sentence)
            continue
        current = ""
        for part in re.split(r"(?<=[,:;])\s+", sentence):
            if current and len(current) + 1 + len(part) > MAX_CAPTION:
                result.append(current)
                current = part
            else:
                current = f"{current} {part}".strip()
        if current:
            result.append(current)
    return result


def words_of(text: str) -> list[str]:
    return [w for w in re.findall(r"[A-Za-z0-9']+", text)]


def duration_of(mp3: Path) -> float:
    """The file's length, read from ffmpeg's own report of it."""
    probe = subprocess.run(
        [imageio_ffmpeg.get_ffmpeg_exe(), "-i", str(mp3), "-f", "null", "-"],
        capture_output=True, text=True)
    stamps = re.findall(r"time=(\d+):(\d+):([\d.]+)", probe.stderr)
    h, m, s = stamps[-1]
    return int(h) * 3600 + int(m) * 60 + float(s)


async def speak(scene: dict, voice: str, rate: str) -> None:
    mp3 = OUT / f"{scene['id']}.mp3"
    communicate = edge_tts.Communicate(scene["text"], voice, rate=rate, boundary="WordBoundary")

    boundaries = []
    with mp3.open("wb") as audio:
        async for part in communicate.stream():
            if part["type"] == "audio":
                audio.write(part["data"])
            elif part["type"] == "WordBoundary":
                start = part["offset"] / 1e7
                boundaries.append((start, start + part["duration"] / 1e7, part["text"]))

    duration = duration_of(mp3)

    # Walk the spoken words in order, giving each caption the time of its first and last word.
    captions = []
    cursor = 0
    for text in chunks(scene["text"]):
        count = len(words_of(text))
        spoken = boundaries[cursor:cursor + count] or boundaries[-1:]
        captions.append({"start": round(spoken[0][0], 3), "end": round(spoken[-1][1], 3), "text": text})
        cursor += count

    # Each caption stays up until the next one replaces it, so a pause never blanks the screen.
    for current, following in zip(captions, captions[1:]):
        current["end"] = following["start"]
    captions[-1]["end"] = round(duration, 3)

    (OUT / f"{scene['id']}.json").write_text(
        json.dumps({"duration": round(duration, 3), "captions": captions}, indent=2), encoding="utf-8")

    print(f"{scene['id']:10} {duration:6.2f}s  {len(captions)} caption(s)")


async def main() -> None:
    narration = json.loads((HERE / "narration.json").read_text(encoding="utf-8"))
    OUT.mkdir(parents=True, exist_ok=True)

    only = set(sys.argv[1:])
    for scene in narration["scenes"]:
        if not only or scene["id"] in only:
            await speak(scene, narration["voice"], narration.get("rate", "+0%"))


if __name__ == "__main__":
    asyncio.run(main())
