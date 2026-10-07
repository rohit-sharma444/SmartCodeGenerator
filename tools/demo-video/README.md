# build app demo video

These scripts make `build-app-demo.mp4`, a narrated walkthrough of `build app`. It is one
real run, from a CSV of orders to a running WPF desktop application. `build-app-demo.vtt` has the
same captions as a subtitle track.

**No video is committed at the moment.** The old recording was removed because it no longer
matched the product. Record a new one with `make-demo.ps1` (below) when the open gaps are fixed.

The video is recorded from the real product, not a mock-up. Every screen in it is the workflow
page and the app it generated, as they behave on the day of the recording - the workflow pages captured by Playwright in
a browser, and the generated WPF app captured as a native window and driven with UI Automation,
since a desktop app has no page for Playwright to see into.

## What it shows

1. **The console:** `build app: an order management system from csv orders.csv` opens the workflow in the browser.
2. **The workflow:** the ten steps, the theme picker, and going back to a finished step.
3. **Application type:** WPF, a native Windows desktop application.
4. **Data and Look:** company name, dark theme, accent colour, and validation, sample data and login switched on.
5. **Discover:** a repeating group (Item1–3) found in the data and split into an `OrderItem` class.
6. **Review:**
   - a field renamed (`Customer` → `CustomerName`)
   - rules proposed from the data and accepted: unique order numbers, four status values, order date not after delivery date, `Total = Net × (1 + TaxRate)`
   - the status workflow with `Delivered` marked as final
7. **Output:** the relationship diagram, Order changed from master data to a transaction, and the whole run as one command.
8. **Entry Template:** WPF's own step, picking Two-Column - the field layout its own generated forms use.
9. **Generate:** the project is written and compiled. The build wait is played 5× faster, and a badge says so.
10. **Run:** the generated app opens as its own window.
    - Sign in, and the imported orders (with created and modified dates) are listed.
    - A new order dated to be delivered before it was placed is **refused** by the accepted rule, with the reason on screen.
    - After the delivery date is corrected, the order **saves**.

## Remaking it

After a change to `build app`, re-record so the video matches the product:

```powershell
powershell -ExecutionPolicy Bypass -File tools\demo-video\make-demo.ps1
```

This takes about 10 minutes. `-Workspace <folder>` changes the working folder (default `C:\dv`). It overwrites `build-app-demo.mp4` and `build-app-demo.vtt`, and runs these steps:

| Step | What it does |
|---|---|
| `WpfDriver` | Built here (`dotnet build -c Release`). A small .NET console app, driven by `record.mjs` over a JSON-lines protocol on its stdin/stdout, that finds and clicks/types into the generated WPF app via UI Automation (FlaUI). |
| `make_voice.py` | Turns each scene in [narration.json](narration.json) into speech (Microsoft's neural voice *Jenny*, US English), with word timings for the captions. |
| `DemoVideoHost` (a test in `CodeGenerator.Tests\Demo`) | Serves a real `build app` workflow for [data/orders.csv](data/orders.csv) in a fresh workspace (`C:\dv`: keep it short, as a long path breaks the generated app's build). It does nothing in an ordinary test run. |
| `record.mjs` | Drives the workflow page with Playwright from the CSV through Generate, then the generated WPF app itself: `WpfDriver` for clicks and keystrokes, `window-capture.mjs` (ffmpeg's `gdigrab`, by screen region) for the picture. Each scene waits for its narration to finish, so the picture never runs ahead of the voice. |
| `assemble.py` | Puts the recordings and the voice together - see below. |

**Two windows, not one recording.** The generated app shows a login window, then (once signed in) a
separate shell window - a different size, and often a different screen position, from the dialog it
replaced. `record.mjs` captures each as its own file (`out/video/raw-wpf-*.mp4`), by the window's own
screen rectangle rather than by title: ffmpeg's `-i title=...` lookup failed outright against this
generator's WPF apps (their DirectX-rendered surface does not always get on with GDI's `BitBlt`).
`assemble.py` scales each to the workflow's frame size, burns that segment's captions onto it - there
was no DOM here to draw a live caption bar into, as the browser segment gets for free - and
concatenates everything: browser recording, each native window's recording, closing card.

**Keeping the voice in sync.** The browser recording's captions go where its scene's SYNC MARKER
appears in the video - a few magenta pixels record.mjs puts in the top-left corner as the scene
starts - rather than where the recorder's clock said the scene started; under load (the build,
above all) the recording falls behind real time. The native recordings have no marker to look for
(no DOM to draw one in), but also none of what makes the browser recording drift - no page load, no
build wait - so their own wall-clock scene starts, recorded by `record.mjs` itself, land exactly
where they say they do.

To change what she says, edit `narration.json` and run `make-demo.ps1` again. `-SkipVoice` reuses the voice files from the last run.

### Needs

- **.NET 10 SDK.**
- **Node.js:** run `npm install` in this folder. It installs Playwright 1.61.1, which uses the Chromium in `%LOCALAPPDATA%\ms-playwright`; run `npx playwright install chromium` if that folder has none.
- **Python 3:** `pip install edge-tts imageio-ffmpeg`. `imageio-ffmpeg` brings its own `ffmpeg` (with `gdigrab` built in), so nothing else needs installing.
- **Internet access for the voice:** the narration text, and nothing else, is sent to Microsoft's speech service.
- **Windows:** the native section needs a real, unlocked desktop session - `gdigrab` captures the screen, and UI Automation needs a window it can bring to the foreground. It cannot run headless or in a locked/RDP-disconnected session.

The Run step also opens the generated app as its own window on your desktop, just as it does for a real user. It can be closed once the recording finishes; `make-demo.ps1` stops it either way while tidying up.

## Found while making it

The first Blazor-based recordings showed two bugs that no test had caught, and both were fixed before that take:

- A field renamed on Review lost its imported data.
- A Blazor app with a login could not save a transaction's lines.

A smaller problem in the generated Blazor app, not yet fixed:

- The totals row adds up `TaxRate`.

The sidebar group headings (*Masters*, *Transactions*) were nearly invisible against the dark background; that is fixed.

Switching the recording to WPF found more, in the generator rather than in `build app` itself:

- The generated form types a decimal field (`TaxRate`) as plain text with no format applied, so `0.05` displays as `5` the moment the field loses focus.
- `Total` is a plain editable field on the generated form, not computed from `Net` and `TaxRate` the way the accepted rule expects - typing a value into it does not recalculate, and it is easy to leave at `0`.
- The DatePicker's typed text is parsed using **the machine's own date-order locale** (day/month/year here), not a fixed format the generator controls - a script or a person typing `9/20/2026` on this machine gets no date at all rather than the 20th of September.

None of the three block `build app` or the video; they are noted here for whoever next touches the WPF form generator.
