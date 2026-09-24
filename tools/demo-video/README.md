# build app demo video

**[build-app-demo.mp4](build-app-demo.mp4)** is a 5½-minute narrated walkthrough of `build app`. It is one real run, from a CSV of orders to a running Blazor application. [build-app-demo.vtt](build-app-demo.vtt) has the same captions as a subtitle track.

The video is recorded from the real product, not a mock-up. Every screen in it is the workflow page and the app it generated, as they behave today.

## What it shows

1. **The console:** `build app: an order management system from csv orders.csv` opens the workflow in the browser.
2. **The workflow:** the ten steps, the theme picker, and going back to a finished step.
3. **Application type:** Blazor, chosen so the result can be run in the browser.
4. **Data and Look:** company name, dark theme, accent colour, and validation, sample data and login switched on.
5. **Discover:** a repeating group (Item1–3) found in the data and split into an `OrderItem` class.
6. **Review:**
   - a field renamed (`Customer` → `CustomerName`)
   - rules proposed from the data and accepted: unique order numbers, four status values, order date not after delivery date, `Total = Net × (1 + TaxRate)`
   - the status workflow with `Delivered` marked as final
7. **Output:** the relationship diagram, Order changed from master data to a transaction, and the whole run as one command.
8. **Generate:** the project is written and compiled. The build wait is played 5× faster, and a badge says so.
9. **Run:** the generated app opens.
   - Sign in, and the imported orders (with created and modified dates) are listed.
   - A new order dated to be delivered before it was placed is **refused** by the accepted rule.
   - After the delivery date is corrected and an item line added, the order **saves with its line**.

## Remaking it

After a change to `build app`, re-record so the video matches the product:

```powershell
powershell -ExecutionPolicy Bypass -File tools\demo-video\make-demo.ps1
```

This takes about 10 minutes. It overwrites `build-app-demo.mp4` and `build-app-demo.vtt`, and runs these steps:

| Step | What it does |
|---|---|
| `make_voice.py` | Turns each scene in [narration.json](narration.json) into speech (Microsoft's neural voice *Jenny*, US English), with word timings for the captions. |
| `DemoVideoHost` (a test in `CodeGenerator.Tests\Demo`) | Serves a real `build app` workflow for [data/orders.csv](data/orders.csv) in a fresh workspace (`C:\dv`: keep it short, as a long path breaks the generated app's build). It does nothing in an ordinary test run. |
| `record.mjs` | Drives that page with Playwright, scene by scene, and records it. Each scene waits for its narration to finish, so the picture never runs ahead of the voice. |
| `assemble.py` | Puts picture and voice together. |

**Keeping the voice in sync:** each scene starts with a sync marker, a few magenta pixels in the top-left corner. `assemble.py` finds the markers in the video itself and places each line of narration there. The recorder's own clock can't be used for this: under load (the build, above all) the recording falls behind real time, and a voice placed by the clock ended up seven seconds ahead of the picture.

To change what she says, edit `narration.json` and run `make-demo.ps1` again. `-SkipVoice` reuses the voice files from the last run.

### Needs

- **.NET 10 SDK.**
- **Node.js:** run `npm install` in this folder. It installs Playwright 1.61.1, which uses the Chromium in `%LOCALAPPDATA%\ms-playwright`; run `npx playwright install chromium` if that folder has none.
- **Python 3:** `pip install edge-tts imageio-ffmpeg`. `imageio-ffmpeg` brings its own `ffmpeg`, so nothing else needs installing.
- **Internet access for the voice:** the narration text, and nothing else, is sent to Microsoft's speech service.

The Run step also opens the generated app in your default browser, just as it does for a real user. The tab can be closed.

## Found while making it

The first recordings showed two bugs that no test had caught, and both were fixed before the final take:

- A field renamed on Review lost its imported data.
- A Blazor app with a login could not save a transaction's lines.

Two smaller problems in the generated Blazor app, not yet fixed:

- Its sidebar group headings (*Masters*, *Transactions*) are nearly invisible against the dark background.
- The totals row adds up `TaxRate`.
