// Records a native window (the generated WPF app) with ffmpeg's gdigrab device, by SCREEN REGION
// rather than by window title: gdigrab's own "-i title=..." window lookup failed outright against
// this generator's WPF apps ("Failed to capture image (error 8)"), most likely because a WPF
// window's surface is DirectX-rendered rather than the classic GDI surface that lookup expects.
// Capturing the desktop at the window's own rectangle - reported by WpfDriver, which already has
// to know it to click into the window - sidesteps that lookup entirely. Resolved via
// imageio_ffmpeg's vendored binary - the same one make_voice.py's Python step already depends on -
// so this needs no separate ffmpeg install.
import { spawn, execFileSync } from 'child_process';

let cachedExe = null;

function ffmpegExe() {
  if (cachedExe) return cachedExe;

  cachedExe = execFileSync(
    'python', ['-c', 'import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())'],
    { encoding: 'utf8' }).trim();

  return cachedExe;
}

/** Starts capturing the desktop region `{x, y, width, height}` to `outPath`. Stop with the
 * returned function, which sends ffmpeg a quit command over stdin so it closes the file properly
 * rather than leaving a truncated, unplayable moov atom behind. */
export function startCapture(region, outPath, { fps = 30 } = {}) {
  const proc = spawn(ffmpegExe(), [
    '-hide_banner', '-loglevel', 'error', '-y',
    '-f', 'gdigrab', '-framerate', String(fps),
    '-offset_x', String(region.x), '-offset_y', String(region.y),
    '-video_size', `${region.width}x${region.height}`, '-i', 'desktop',
    // A window's client area is whatever size it happens to be, odd height included - libx264's
    // yuv420p needs both dimensions even, which is what this trims off rather than assuming.
    '-vf', 'crop=trunc(iw/2)*2:trunc(ih/2)*2',
    '-c:v', 'libx264', '-preset', 'veryfast', '-pix_fmt', 'yuv420p',
    outPath,
  ], { stdio: ['pipe', 'inherit', 'inherit'] });

  return async function stop() {
    proc.stdin.write('q');
    await new Promise(resolve => proc.once('exit', resolve));
  };
}
