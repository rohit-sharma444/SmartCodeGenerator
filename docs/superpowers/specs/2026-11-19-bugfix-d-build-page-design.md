# Bug fixes D — build app web page — design

Branch: `bug-hunt-specs`. Date: 2026-11-19. Status: draft for review.
Base: `main` = 10224d0. Project: `CodeGenerator/Web/Workflow`. Evidence key as in spec A.

Rule for this page: bUnit tests assert CSS class names, so add rules; never rename classes.

## D1. A long path overflows the Generate panel (PROVEN in headless Chromium)

**Where:** `GenerateStage.razor:81` and `:88` render `<p class="report-line">` (text from `:227`
and `:417`). `workflow.css` has no `.report-line` rule; only `.fix-report` (line 801,
`overflow-wrap:anywhere`) and `.command-echo` wrap.

**Failure:** at 1024 px width, app name "HospitalPatientAppointmentScheduling": the test-project
path ends at x=1171, the panel at x=992 — about 180 px of overflow. At 1280 px it just fits.

**Fix:** add to `workflow.css`:

```css
.report-line { overflow-wrap: anywhere; }
```

**Tests:** a CSS test that `.report-line` declares `overflow-wrap`. Browser check at 1024 px and
at phone width (390 px): no horizontal page scroll; screenshot looked at.

## D2. "A fix is already running" shows when another page is writing (TRACED)

**Where:** `GenerateStage.razor:656-659` `FixErrors` shows "A fix is already running for this
project." whenever `FixLocks.TryEnter` fails. `Go()` takes the same lock at `:337` for the whole
write and build, so the holder can be a write. In reverse, `FixRunningNotice` (`:323`, "A fix is
running on this app") shows when the holder is another tab's write. The doc comment on
`FixLocks.TryEnter` repeats the wrong wording.

**Fix:** `FixLocks.TryEnter(folder, purpose)` records the holder's purpose
(`LockPurpose.Write` or `LockPurpose.Fix`) and returns it on failure. Messages:
- holder Fix: "A fix is already running for this app. Wait for it to finish."
- holder Write: "This app is being written or built on another page. Wait for it to finish."
Update the doc comment.

**Tests:** next to `Two_pages_fixing_one_project_run_one_fixer` (`GenerateFixTests.cs:803`):
page A blocks in its build runner during Write; page B clicks `button.fix-yes`; B's report says
"being written or built" and not "A fix is already running".

## Order of work

D1 → D2. After both: the bUnit suite and a browser check of `/build` at 1024 px.
