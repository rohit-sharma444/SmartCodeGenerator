"""Puts the recording and the narration together into the finished video.

record.mjs now produces THREE recordings, not one, because the WPF section drives a native
window that Playwright cannot see into and so cannot record into the same file as the rest:

  raw-browser.webm   the workflow, intro through Run       (out/scenes-browser.json)
  video/raw-wpf.mp4  the generated app, sign-in to saved    (out/scenes-wpf.json)
  raw-closing.webm   the closing card                       (out/scenes-closing.json)

Each is normalized to the same picture format on its own (out/norm-*.mp4), then concatenated with
a stream copy - which only works because all three now share one codec, pixel format and frame
rate - into out/picture.mp4. Narration is mixed onto THAT the same way it always was: one voice
clip per scene, delayed to the scene's place in the FINAL video and mixed down.

Each browser scene's line goes where its scene's SYNC MARKER appears in ITS OWN recording - a few
magenta pixels record.mjs puts in the top-left corner as the scene starts - rather than where the
recorder's clock said the scene started. Under load the recording falls behind real time, and a
voice placed by the clock ran seven seconds ahead of the picture by the end of the first take.

The WPF section has no marker to look for - there is no DOM to draw one in - but it also has none
of what makes the browser recording drift: no page load, no build wait, nothing racing the
recording. Its own wall-clock scene starts, recorded by record.mjs itself, land exactly where they
say they do, and its captions are burned onto the picture in this file instead of drawn live.

The stretch between the cyan and yellow markers in the browser segment - the build, which nobody
narrates - is played FAST times faster, and everything after it in that segment moves up to match.
"""

import json
import re
import subprocess
from pathlib import Path

import imageio_ffmpeg

HERE = Path(__file__).resolve().parent
OUT = HERE / "out"
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
FAST = 5.0
SAMPLE_FPS = 25
FRAME_RATE = 25
WIDTH, HEIGHT = 1280, 720

# The caption bar's DOM styling (record.mjs's overlayInitScript), matched here so the WPF
# section's burned-in captions do not read as a different video's captions spliced in.
CAPTION_FONT_SIZE = 26
CAPTION_Y = "h-100"


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


def duration_of(video: Path) -> float:
    """A file's own duration, read from ffmpeg's own stderr banner - imageio_ffmpeg vendors
    ffmpeg, not ffprobe, so this is the one binary assemble.py can rely on being there."""
    result = subprocess.run([FFMPEG, "-i", str(video)], capture_output=True, text=True)
    match = re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)", result.stderr)
    if not match:
        raise RuntimeError(f"could not read {video}'s duration from ffmpeg's own output")
    h, m, s = match.groups()
    return int(h) * 3600 + int(m) * 60 + float(s)


def reencode(video: Path, out_path: Path, *, video_filter: str | None = None) -> None:
    """One shared encode step for every segment, so all three come out with the same codec,
    pixel format and frame rate - the concat demuxer's `-c copy` requires exactly that, and a
    mismatch there fails loudly rather than silently reordering frames."""
    args = [FFMPEG, "-y", "-v", "error", "-i", str(video)]
    if video_filter:
        args += ["-vf", video_filter]
    args += ["-r", str(FRAME_RATE), "-c:v", "libx264", "-preset", "medium", "-crf", "20",
              "-pix_fmt", "yuv420p", "-an", str(out_path)]
    subprocess.run(args, check=True)


def browser_segment() -> tuple[list[dict], Path]:
    """Segment A: the workflow, intro through Run. Marker-timed, with the build sped up - the
    same logic the single-segment assembly always used, now writing its own normalized file
    instead of the final one."""
    scenes = json.loads((OUT / "scenes-browser.json").read_text(encoding="utf-8"))
    raw = OUT / "raw-browser.webm"

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

    def moved(t: float) -> float:
        """Where time t of the recording lands once the build is sped up."""
        if fast_start is None or fast_end is None or t <= fast_start:
            return t
        if t >= fast_end:
            return t - (fast_end - fast_start) * (1 - 1 / FAST)
        return fast_start + (t - fast_start) / FAST

    for scene, start in zip(scenes, located):
        scene["video"] = round(moved(start), 3)
        print(f"{scene['id']:9} clock {scene['start']:7.2f}  video {start:7.2f}  "
              f"drift {start - scene['start']:+5.2f}  final {scene['video']:7.2f}")

    if fast_start is not None and fast_end is not None:
        print(f"build sped up {FAST:g}x: {fast_start:.1f}s - {fast_end:.1f}s")
        # -filter_complex rather than -vf: a 3-way concat needs named pads wired explicitly,
        # which -vf's single-chain syntax has no way to express.
        out_path = OUT / "norm-browser.mp4"
        subprocess.run(
            [FFMPEG, "-y", "-v", "error", "-i", str(raw),
             "-filter_complex",
             f"[0:v]trim=0:{fast_start},setpts=PTS-STARTPTS[a];"
             f"[0:v]trim={fast_start}:{fast_end},setpts=(PTS-STARTPTS)/{FAST}[b];"
             f"[0:v]trim={fast_end},setpts=PTS-STARTPTS[c];"
             f"[a][b][c]concat=n=3:v=1:a=0[v]",
             "-map", "[v]", "-r", str(FRAME_RATE), "-c:v", "libx264", "-preset", "medium",
             "-crf", "20", "-pix_fmt", "yuv420p", "-an", str(out_path)],
            check=True)
    else:
        out_path = OUT / "norm-browser.mp4"
        reencode(raw, out_path)

    return scenes, out_path


def wpf_segments() -> list[tuple[list[dict], Path]]:
    """Segment B: the generated app, sign-in to saved - as however many recordings record.mjs
    made, one per DISTINCT window it captured (the login dialog, then the shell it opens once
    signed in: two different windows, at two different positions and sizes, which is why this is
    a list rather than the single file every other segment is). No marker to find in any of them,
    so each one's own wall-clock scene starts (recorded by record.mjs) are used exactly as given.
    Scaled and letterboxed to the browser segment's frame size - a native window's client area is
    whatever size it happens to be, never guaranteed to match the workflow's fixed 1280x720
    viewport - and captioned here, since there was no DOM in either recording to draw a caption
    bar into as it happened."""
    manifest = json.loads((OUT / "scenes-wpf.json").read_text(encoding="utf-8"))

    scale = (f"scale={WIDTH}:{HEIGHT}:force_original_aspect_ratio=decrease,"
             f"pad={WIDTH}:{HEIGHT}:(ow-iw)/2:(oh-ih)/2:color=black")

    segments = []
    for index, part in enumerate(manifest):
        scenes = part["scenes"]
        raw = OUT / "video" / part["file"]

        drawtext = []
        for scene in scenes:
            spoken = json.loads((OUT / "voice" / f"{scene['id']}.json").read_text(encoding="utf-8"))
            for line in spoken["captions"]:
                start = scene["start"] + line["start"]
                end = scene["start"] + line["end"]
                text = line["text"].replace("\\", "\\\\").replace(":", "\\:").replace("'", "’")
                drawtext.append(
                    f"drawtext=text='{text}':fontcolor=white:fontsize={CAPTION_FONT_SIZE}:"
                    f"box=1:boxcolor=black@0.55:boxborderw=16:"
                    f"x=(w-text_w)/2:y={CAPTION_Y}:enable='between(t,{start:.3f},{end:.3f})'")

        video_filter = ",".join([scale, *drawtext]) if drawtext else scale

        out_path = OUT / f"norm-wpf-{index}.mp4"
        reencode(raw, out_path, video_filter=video_filter)

        for scene in scenes:
            scene["video"] = round(scene["start"], 3)

        segments.append((scenes, out_path))

    return segments


def closing_segment() -> tuple[list[dict], Path]:
    """Segment C: the closing card. One scene, marker-timed the same way the browser segment's
    scenes are - it comes from a Playwright recording too, just a much shorter one."""
    scenes = json.loads((OUT / "scenes-closing.json").read_text(encoding="utf-8"))
    raw = OUT / "raw-closing.webm"

    found = markers(raw)
    starts = [t for t, k in found if k == "scene"]

    for scene, start in zip(scenes, starts or [s["start"] for s in scenes]):
        scene["video"] = round(start, 3)

    out_path = OUT / "norm-closing.mp4"
    reencode(raw, out_path)

    return scenes, out_path


def main() -> None:
    browser_scenes, browser_path = browser_segment()
    wpf_parts = wpf_segments()
    closing_scenes, closing_path = closing_segment()

    # Every scene's place in the ONE final video: each segment's own local "video" time, plus
    # however much of every earlier segment (browser, then each native window in turn) played
    # before it starts.
    scenes = []
    offset = 0.0

    for scene in browser_scenes:
        scenes.append({**scene, "video": scene["video"]})
    offset += duration_of(browser_path)

    for part_scenes, part_path in wpf_parts:
        for scene in part_scenes:
            scenes.append({**scene, "video": round(scene["video"] + offset, 3)})
        offset += duration_of(part_path)

    for scene in closing_scenes:
        scenes.append({**scene, "video": round(scene["video"] + offset, 3)})

    for scene in scenes:
        print(f"{scene['id']:9} final {scene['video']:7.2f}")

    concat_list = OUT / "concat.txt"
    ordered_paths = [browser_path, *(p for _, p in wpf_parts), closing_path]
    concat_list.write_text(
        "".join(f"file '{p.resolve().as_posix()}'\n" for p in ordered_paths),
        encoding="utf-8")

    picture = OUT / "picture.mp4"
    subprocess.run(
        [FFMPEG, "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", str(concat_list),
         "-c", "copy", str(picture)],
        check=True)

    inputs = ["-i", str(picture)]
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
         "-filter_complex", mix,
         "-map", "0:v", "-map", "[voice]",
         "-c:v", "copy",
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
