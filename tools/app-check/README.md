# app-check

Generates an app from every prompt in `prompts.json`, then does to each one what a person
trying it for the first time would: builds it, runs its tests, launches it, signs in, opens a
screen, checks the seeded records are there, and presses New then Save. The result is one page,
`report.html`, with a tick or a cross per step and the reason for every cross.

It exists because the generator's worst defects (an app that did not compile, a worker that was
never built, a desktop app with no tests, a record list squashed to nothing) all passed thousands
of unit tests, and only showed when someone ran the generated app.

## Running it

```powershell
powershell -ExecutionPolicy Bypass -File tools\app-check\run.ps1              # every case
powershell -ExecutionPolicy Bypass -File tools\app-check\run.ps1 -Only sql    # ids containing "sql"
powershell -ExecutionPolicy Bypass -File tools\app-check\run.ps1 -NoLaunch    # generate, build, test only
powershell -ExecutionPolicy Bypass -File tools\app-check\run.ps1 -Runs D:\ac  # runs folder (default C:\ac\runs)
```

`run.ps1` runs `npm install` if `node_modules` is missing, rebuilds the CLI (Release) and the
window driver (`tools\demo-video\WpfDriver`, which the WPF and WinForms checks use), then runs
`check.mjs`. Once those are built, `node check.mjs [--only <text>] [--no-launch] [--runs <folder>]`
is the same thing.

Each case pipes `build app: ...` then `approve <target>` into
`CodeGenerator\bin\Release\net10.0\CodeGenerator.exe`, with `CG_NO_WEB=1` and
`PROJECTPATH=<case folder>\ws` (`lib/generate.mjs`), so it never writes into this repository.

Needs:

- **Node.js**, and Playwright's Chromium for the Blazor checks (`npx playwright install chromium`
  once, in `tools\app-check`).
- **an unlocked desktop**: the WPF and WinForms checks drive real windows through UI Automation.
  Other windows may sit on top of them: the checks sign in, open screens and press buttons
  through UI Automation rather than the mouse and keyboard, and screenshots are drawn by the
  app's own window. A step that could only be done by mouse, with another window covering the
  app, fails and names that window instead of clicking into it.
- **Ollama running** for the description prompts. Without it, discovery falls back to the local
  Wikipedia store if one is installed, then the 25 built-in domains, and online Wikipedia only
  when no store is installed. The report's "Classes" line says which source answered.
- **SQL Server** (`.`, Windows authentication) with the `ValidationPhase3Test` database, for
  `shop-sql`.

## What a case is

`prompts.json` has 3 data prompts, 7 targets and 5 description prompts: 3 × 7 + 5 = 26 cases.

- Each **data** prompt (CSV, JSON, SQL) is built once for **every** target. The classes come from
  the data, so these are the same every run: a change here means the generator changed.
- Each **description** prompt is built once, as its own target, and additionally scored on
  whether discovery found the classes listed in its `expect`. Discovery varies a little from run
  to run, so read a single miss there with that in mind. One of them (gym) is built for Blazor,
  so a described app is run on a stack other than WPF too.
- Every case also checks the generated models: no class may carry a key of its own (an
  `OrderId` or `OrderID` on `Order`) beside its `Id`.

## What "runs" checks, per target

| Target | Checks |
|---|---|
| WPF, WinForms | window opens, signs in as admin / admin, opens the first entity, the list shows seeded rows, New then Save gives a status, the list recounted after Save holds one more record (or the same number when the save was refused), app still running; the screenshot is taken after Save |
| Blazor | `dotnet run` serves, signs in (only when the app has an `Auth` folder), the entity page lists seeded rows, New opens the editor, Save answers, no browser errors or HTTP 500s |
| Web API | every mapped route answers GET with seeded records |
| Worker | still running after 8 seconds, logs no `fail:`/`crit:` |
| Console | lists every entity in its menu, exits when told to |
| Class library | nothing to launch |

## Output

Every run gets its own folder under `<runs>\<yyyyMMdd-HHmm>\`, `C:\ac\runs` by default (short on
purpose: a generated project under a long path hits MAX_PATH during its build):

- `report.html`: the page. It compares itself with the previous run's `results.json`.
- `results.json`: the same results, for a script.
- `<case>\cli.log`: everything the CLI printed; `<case>\screen.png`: the screen as it was checked;
  `<case>\ws\`: the generated app itself, left in place so a failure can be opened and poked.

Both are rewritten after every case, so a run stopped halfway still leaves a report.

## Adding a prompt

Add an entry to `prompts.json`. `{repo}` in a command is replaced with the repository root.
