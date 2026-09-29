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
```

`run.ps1` rebuilds the CLI (Release) and the window driver first, then runs `check.mjs`. Once
those are built, `node check.mjs [--only <text>] [--no-launch]` is the same thing.

Needs:

- **an unlocked desktop**: the WPF and WinForms checks drive real windows through UI Automation.
- **Ollama running** for the description prompts. Without it, discovery falls back to Wikipedia,
  and the report's "Classes" line says which source answered.
- **SQL Server** (`.`, Windows authentication) with the `ValidationPhase3Test` database, for
  `shop-sql`.

## What a case is

- Each **data** prompt (CSV, JSON, SQL) is built once for **every** target. The classes come from
  the data, so these are the same every run: a change here means the generator changed.
- Each **description** prompt is built once, as its own target, and additionally scored on
  whether discovery found the classes listed in its `expect`. Discovery varies a little from run
  to run, so read a single miss there with that in mind.

## What "runs" checks, per target

| Target | Checks |
|---|---|
| WPF, WinForms | window opens, signs in as admin / admin, opens the first entity, the list shows seeded rows, New then Save gives a status, app still running |
| Blazor | `dotnet run` serves, signs in, the entity page lists seeded rows, New opens the editor, Save answers, no browser errors or HTTP 500s |
| Web API | every mapped route answers GET with seeded records |
| Worker | still running after 8 seconds, logs no `fail:`/`crit:` |
| Console | lists every entity in its menu, exits when told to |
| Class library | nothing to launch |

## Output

Every run gets its own folder under `C:\ac\runs\<yyyyMMdd-HHmm>\` (short on purpose: a generated
project under a long path hits MAX_PATH during its build):

- `report.html`: the page. It compares itself with the previous run's `results.json`.
- `results.json`: the same results, for a script.
- `<case>\cli.log`: everything the CLI printed; `<case>\screen.png`: the screen as it was checked;
  `<case>\ws\`: the generated app itself, left in place so a failure can be opened and poked.

Both are rewritten after every case, so a run stopped halfway still leaves a report.

## Adding a prompt

Add an entry to `prompts.json`. `{repo}` in a command is replaced with the repository root.
