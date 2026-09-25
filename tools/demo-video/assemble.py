"""Puts the recording and the narration together into the finished video.

Reads out/raw.webm and out/scenes.json (from record.mjs) and out/voice/*.mp3 (from make_voice.py),
and writes build-app-demo.mp4 (H.264 and AAC, which plays anywhere) and build-app-demo.vtt, the
same captions as a subtitle track.

Each line of narration goes where its scene's SYNC MARKER appears in the video - a few magenta
pixels record.mjs puts in the top-left corner as the scene starts - rather than where the recorder's
clock said the scene started. Under load the recording falls behind real time, and a voice placed by
the clock ran seven seconds ahead of the picture by the end of the first take.

The stretch between the cyan and yellow markers - the build, which nobody narrates - is played
FAST times faster, and everything after it moves up to match.
"""

import json
import subprocess
from pathlib import Path

import imageio_ffmpeg

HERE = Path(__file__).resolve().parent
OUT = HERE / "out"
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
FAST = 5.0
SAMPLE_FPS = 25


def stamp(seconds: float) -> str:
    h, rest = divmod(seconds, 3600)
    m, s = divmod(rest, 60)
    return f"{int(h):02d}:{int(m):02d}:{s:06.3f}"


def kind(r: int, g: int, b: int) -> str | None:
    """Which marker a corner pixel shows, if any. Generous thresholds: the video is compressed."""
    high, low = 170, 100
    if r > high and g < low and b > high:
        return "scene"
    if r < low and g > high and b > high:
        return "fastStart"
    if r > high and g > high and b < low:
        return "fastEnd"
    return None


def markers(video: Path) -> list[tuple[float, str]]:
    """Every marker's first frame, in order, from the corner of every frame."""
    raw = subprocess.run(
        [FFMPEG, "-v", "error", "-i", str(video),
         "-vf", f"fps={SAMPLE_FPS},crop=6:6:4:4,scale=1:1:flags=area", "-f", "rawvideo",
         "-pix_fmt", "rgb24", "-"],
        capture_output=True, check=True).stdout

    found, previous = [], None
    for index in range(len(raw) // 3):
        current = kind(*raw[index * 3:index * 3 + 3])
        if current and current != previous:
            found.append((index / SAMPLE_FPS, current))
        previous = current
    return found


def main() -> None:
    scenes = json.loads((OUT / "scenes.json").read_text(encoding="utf-8"))
    raw = OUT / "raw.webm"

    found = markers(raw)
    starts = [t for t, k in found if k == "scene"]
    fast_start = next((t for t, k in found if k == "fastStart"), None)
    fast_end = next((t for t, k in found if k == "fastEnd"), None)

    # Each scene takes the first unused marker near where it is expected: its clock time plus the
    # drift measured at the scene before it. A scene whose marker did not make it into the video -
    # the first cards, drawn before the recording has settled - keeps that expectation instead,
    # which at the start, where nothing has drifted yet, is exact enough.
    located, drift, unused = [], 0.0, list(starts)
    for scene in scenes:
        expected = scene["start"] + drift
        near = [t for t in unused if abs(t - expected) < 4.0]
        if near:
            unused = [t for t in unused if t > near[0]]
            drift = near[0] - scene["start"]
            located.append(near[0])
        else:
            located.append(expected)
            print(f"{scene['id']:9} no marker found - placed by the clock")
    starts = located

    def moved(t: float) -> float:
        """Where time t of the recording lands once the build is sped up."""
        if fast_start is None or fast_end is None or t <= fast_start:
            return t
        if t >= fast_end:
            return t - (fast_end - fast_start) * (1 - 1 / FAST)
        return fast_start + (t - fast_start) / FAST

    for scene, start in zip(scenes, starts):
        scene["video"] = round(moved(start), 3)
        print(f"{scene['id']:9} clock {scene['start']:7.2f}  video {start:7.2f}  "
              f"drift {start - scene['start']:+5.2f}  final {scene['video']:7.2f}")

    if fast_start is not None and fast_end is not None:
        print(f"build sped up {FAST:g}x: {fast_start:.1f}s - {fast_end:.1f}s")
        picture = (f"[0:v]trim=0:{fast_start},setpts=PTS-STARTPTS[v0];"
                   f"[0:v]trim={fast_start}:{fast_end},setpts=(PTS-STARTPTS)/{FAST}[v1];"
                   f"[0:v]trim={fast_end},setpts=PTS-STARTPTS[v2];"
                   f"[v0][v1][v2]concat=n=3:v=1:a=0[picture]")
    else:
        picture = "[0:v]null[picture]"

    inputs = ["-i", str(raw)]
    voices = []
    for index, scene in enumerate(scenes, start=1):
        inputs += ["-i", str(OUT / "voice" / f"{scene['id']}.mp3")]
        ms = int(round(scene["video"] * 1000))
        voices.append(f"[{index}:a]adelay={ms}|{ms}[a{index}]")

    mix = ";".join(voices) + ";" + "".join(f"[a{i}]" for i in range(1, len(scenes) + 1))
    mix += f"amix=inputs={len(scenes)}:normalize=0:dropout_transition=0[voice]"

    video = HERE / "build-app-demo.mp4"
    subprocess.run(
        [FFMPEG, "-y", "-v", "error", *inputs,
         "-filter_complex", picture + ";" + mix,
         "-map", "[picture]", "-map", "[voice]",
         "-c:v", "libx264", "-preset", "slow", "-crf", "22", "-pix_fmt", "yuv420p", "-r", "25",
         "-c:a", "aac", "-b:a", "160k",
         "-movflags", "+faststart",
         str(video)],
        check=True)

    cues = ["WEBVTT", ""]
    for scene in scenes:
        spoken = json.loads((OUT / "voice" / f"{scene['id']}.json").read_text(encoding="utf-8"))
        for line in spoken["captions"]:
            cues += [f"{stamp(scene['video'] + line['start'])} --> {stamp(scene['video'] + line['end'])}",
                     line["text"], ""]
    (HERE / "build-app-demo.vtt").write_text("\n".join(cues), encoding="utf-8")

    print("wrote", video)


if __name__ == "__main__":
    main()
