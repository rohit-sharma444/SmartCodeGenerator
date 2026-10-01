# CodeGenerator

A natural-language command-line tool for scaffolding and editing .NET code. You
type plain commands (e.g. `create class Customer with properties Name string`)
and the tool generates projects, classes, members, and validators on disk.

## Running

```bash
dotnet run --project CodeGenerator
```

By default the workspace (where projects and classes are written) is the current
directory. Point it somewhere else with the `PROJECTPATH` environment variable:

```bash
# Windows (PowerShell)
$env:PROJECTPATH = "D:\MyWorkspace"; dotnet run --project CodeGenerator

# bash
PROJECTPATH=/path/to/workspace dotnet run --project CodeGenerator
```

At the `CodeGenerator >>` prompt, type `help` for an overview or `exit` to quit.

### Ctrl-C, and why a second press still quits

`Interrupt` holds the policy: the first press requests cancellation and returns
`false` from `ShouldTerminate()`, so `CancelKeyPress` sets `e.Cancel = true` and the
process lives — a write in flight finishes rather than leaving a half-generated
project. The second press returns `true` and lets it die.

That second press is not a nicety. Much of the work here is synchronous and takes no
token, so a press genuinely cannot stop it; a trap with no way out would be worse
than no trap. `Interrupt.Token` is threaded into `NaturalLanguagePipeline.Handle`,
which already accepted one, and `Interrupt.Reset()` runs as the prompt regains
control so the next command does not start already cancelled.

### The pager

`Pager.Write` pauses a page at a time for `help all` (over 600 lines) and the candidate
model, which is reprinted after every review action. Never when redirected — a pager
waiting on a key press in a pipe is a hang — and never when `Console.WindowHeight`
cannot be read, since a host that cannot report its height cannot answer a key press
either. `PageSize` is floored at 1, because a zero-line page loops forever.

### The spinner

`Spinner.Start` returns an `IDisposable` either way, so callers have no branch. On a
real console it animates beside the label with an elapsed count; redirected, it
prints the label once and nothing else — byte-identical to what piped runs saw
before. Frames are ASCII deliberately: decoration is the last thing worth risking on
a console whose encoding could not be changed.

`Frame` does not use `Math.Abs`, which throws on `int.MinValue` — a spinner must be
incapable of taking down the operation it decorates.

### `demo` runs real commands

`DemoScript` is seven actual commands driven back through
`NaturalLanguagePipeline.Handle`, so the demo cannot drift from what the tool does. A
test asserts every one of them matches a catalog entry, which is the guard that stops
it rotting into the thing it replaced — the old `demo` printed a canned `Customer`
class with its comment markers stripped and ran nothing.

It writes to disk, so it names the path, asks first, refuses if the folder already
exists, and declines entirely when input is redirected (printing the steps instead,
so a scripted reader still learns something).

### Startup flags, and why they come first

`StartupFlags` answers `--version` / `--help` (plus `-v`, `-h`, `-?`, `/?`, `/help`)
in `Program.cs` **before `CodeGeneratorStarter` is constructed**. That ordering is
the point: the constructor resolves the workspace, scans for projects and loads
rules, printing as it goes, so a script reading `CodeGenerator --version` would
otherwise have to strip that noise. `--version` emits the bare version and nothing
else.

Only a lone argument counts as a flag — `build app: the --version endpoint` is a
description, and intercepting it would break a legitimate command. Bare `help` is
deliberately *not* in the flag set: `HelpCommand` already owns it and handles
`help <topic>`, which the flag path would flatten to the overview.

### Console encoding

`ConsoleEncoding.UseUtf8()` runs first of all. The tool emits an em dash in 36
places and `↑↓` in `SelectionPrompt`'s hint; a console left on a legacy code page
renders those as `?`. The arrows are not in Windows-1252 at all — confirmed the
hard way, by a cp1252 host failing outright while enumerating them. Best-effort and
never throws: a host that refuses keeps its previous encoding and everything still
works.

### One word for a generated class

User-facing text says **class**, not "entity" — the review loop, its action labels,
the argument prompts, the catalog descriptions and the manual. `CandidateEntity` and
friends keep their names internally; only prose changed, and the command tokens are
untouched, so no script breaks. The one surviving "entity" is *entity-level*
validation, which is a scope paired with *field-level* rather than a name for the
class.

### Output roles, errors, and the version

`Output` lives in `CodeGenerationEngine/Helper`, not the CLI: most of the messages a
user reads are printed from the engine (`FileWriter`, `EntityExtractor`,
`ProjectManager`, `CommandHandlingService`), and the CLI sits *above* the engine, so it
cannot own something the engine needs to call. That placement is why colour reaches
everyday output at all — while `Output` was CLI-only, the engine's messages could not
use it and a normal session showed no colour whatsoever.

It has exactly four roles — `Success`, `Error`, `Warning`, `Detail` — plus a
plain `Line`. Four and no more: a screen painted in six colours reads as less
considered, not more, so anything that is not one of those goes through `Line` and
keeps the terminal's own colour.

`ColourDelivery` decides **how** colour is delivered, once per process:

- **ANSI escape sequences** when virtual-terminal processing can be enabled — which
  is every modern terminal. `Console.ForegroundColor` is not universal: on Windows it
  calls `SetConsoleTextAttribute`, and a host that renders only escape sequences
  drops it silently. That was a real user report.
- **Windows console attributes** as the fallback for an older console.
- **Nothing**, when `NO_COLOR` is set or output is redirected without `FORCE_COLOR`.

`FORCE_COLOR` exists for a host that captures the stream and renders the sequences
itself — the only route by which the VS Code extension panel could ever show colour,
since it spawns the CLI with a pipe and console attributes cannot cross that. Note
that when VT cannot be enabled *because* stdout is a pipe, forced colour still emits
ANSI: falling back to attributes would send such a host nothing at all.

`NO_COLOR` beats `FORCE_COLOR` — asking for no colour is the stronger request, being
the one a person makes to fix something they are looking at.

Without `FORCE_COLOR`, piped output contains **zero** escape bytes, so a captured
transcript is byte-for-byte what it was before colour existed. Verified by checking
for ESC bytes in both directions.

Routing rule: **success** is something produced (a class written, a validator
generated, a rule registered); **warning** is a precondition the user can fix (no
project selected, class not found, a command not recognized — nothing failed, so red
would read as a fault in the tool); **error** is a real failure. Everything else keeps
the terminal's own colour.

`BuildReport` and `NextSteps` stay pure and keep returning plain strings; the
caller, which knows the outcome, picks the colour. Only the *first* line of a build
report is coloured — painting the quoted compiler errors red too is noise, not
emphasis.

`ErrorText` handles a failure nothing else caught. The exception type used to be
printed at the user; it and the stack now sit behind `CG_VERBOSE` (the convention
`CG_NO_LEARN` set), which also replaces the old "stack only when a debugger is
attached" — no use to the person actually hitting it. The innermost exception
message is appended when it differs, because the outer one is often the vague half.

`VersionInfo` reads `AssemblyInformationalVersion` and trims at `+`: the SDK appends
the commit sha there, so a naive read prints a hash at the user. `<Version>` lives in
`Directory.Build.props`, shared, since the projects ship together.

### Startup is quiet when nothing is wrong

`PrintBanner` is three lines. The store report and the AI line are conditional:
`StoreStatusReport.IsHealthy` decides the first, and the second prints only when
`AiLayer.Unavailable` has a reason — an AI that was meant to load and could not.
"Enabled" and a deliberate "off (disabled)" are states the user chose and say
nothing useful at every launch.

`IsHealthy` is deliberately asymmetric: anything not plainly well counts as
unhealthy, including both `Unknown` states ("we could not tell" is not "fine") and
a stale `Applying` snapshot left by a process that stopped. A false positive there
would hide a real problem behind a clean-looking prompt; a false negative only
prints a line nobody needed.

`status` (`StatusCommand`, intercepted like `help`) prints everything through
`StatusReport.Lines`. Both it and the startup warning take their store lines from
one `StoreReport()`, so they cannot disagree about what the store is doing.

`help` is handled in `HelpCommand`, ahead of the recognition pipeline, because
`LiteralCommandTable` keys on the exact word — a line with a topic on it matches
nothing there and would fall through to the NLP path. It comes in three forms:
bare `help` renders `CommandDictionary.GetOverview()` (starter commands plus the
categories and their counts), `help <topic>` renders `GetTopicHelp` for a category
or a single command, and `help all` renders `GetHelpText()`, the full catalog that
plain `help` used to print. An unrecognized topic names itself back and lists the
categories rather than printing nothing or, worse, everything.

### Non-interactive / scripted use

A single command can be passed as arguments:

```bash
dotnet run --project CodeGenerator -- "create console project MyApp"
```

Commands can also be piped in, one per line (the session runs them in order and
stops at end-of-input):

```bash
printf 'create console project MyApp\nuse project MyApp\ncreate class Customer\nexit\n' \
  | dotnet run --project CodeGenerator
```

Inside a session, `save session <file>` writes the commands that worked and changed something
to a `.cgs` script, and `run <file>` replays one, all or nothing. See *Scripts: save session
and run* in the [user manual](../docs/user-manual-source.html).

Inside a session, `save session <file>` writes the commands that worked and changed something
to a `.cgs` script, and `run <file>` replays one, all or nothing. See *Scripts: save session
and run* in the [user manual](../docs/user-manual-source.html).

## Signing in to a generated app

Every generated WPF, Windows Forms and web application opens at a login screen, and nothing else
is reachable until someone signs in — on the web that includes the home page, and typing a page's
address directly does not get past it.

The first run creates one account, `admin`, with the password `admin`. The login screen says so
while that password is still in place and stops once it is changed. Passwords are never stored:
the store holds a PBKDF2 hash and the random salt it was made with.

An account is an **Admin** or a **User**. Only an Admin sees the Users screen or can delete a
master record; both roles can create, edit and save everything else. A new account has no password
and cannot sign in until an administrator sets one, and nobody can delete the account they are
signed in with — which is what keeps at least one administrator in place.

`nologin` on the `build app:` line, or answering *no* to *Require a login*, omits the login screen,
the accounts and the Users screen entirely:

```
build app: a shop that sells books nologin
```

## Commands

The command reference is [`docs/commands.md`](../docs/commands.md). It is generated from the
catalog in `CodeGenerationEngine/Core/CommandDictionary.cs`, and a test fails when the two
differ, so it lists every command this build understands. This file used to keep its own table
by hand, and that table had fallen far behind — undo and redo, `run` and
`save session`, the method and property edits, the AI and learning commands, `chat` among them.

At the prompt the same catalog is one word away: `help` for the overview, `help <topic>` for
one category or one command (`help approve`), and `help all` for every command with an example.
The larger flows — the `build app` workflow page, building from data, the review loop, undo and
redo, scripts and the chat page — are explained in the
[user manual](../docs/user-manual-source.html).

What follows is how the CLI side of `build app` is put together.

### `build app` has two front ends

An interactive `build app` opens the workflow page and carries the whole build there:
`BuildAppWebHost`, on `LoopbackHost`, serves it from Kestrel in this process on a loopback port
with a one-off token in its address, and it lives exactly as long as the command. The console
prints the address, says that Ctrl-C cancels, and waits. The page has the ten stages of
`BuildStage`, from the description to running the generated app.

If the page cannot be started, the console says so and runs the console wizard
(`BuildAppWizard`) instead. `CG_NO_WEB=1` keeps the console wizard too, for a session with no
browser to open.

Redirected input opens no page and asks no questions. It uses the inline form
(`build app: <description> from <format> <location>`), picks a target at `approve <target>`, and
drives the typed review loop; `?` in that loop lists every action it accepts. A piped run that
never names a target generates WPF.

### Overwriting an existing project

`AppScaffolder` reuses a project whose `.csproj` already exists — generated sources
are rewritten, the project file and hand-added files are not. That is deliberate and
tested, but it is not always what the user meant, so `Generate` asks first
(`OverwritePrompt`), offering overwrite / rename / cancel with Enter cancelling, and
adding only the new classes when the model has some the project lacks.

The trigger is `GeneratedProjectPath.Exists`, which is the same test the scaffolder
uses to decide reuse. Both go through that one type on purpose: a guard that derived
the folder even slightly differently from the writer would not fail loudly, it would
silently stop firing.

`r` re-asks after renaming, since the new name can collide too. `Generate` returns
`false` on a decline and the review loop continues rather than exiting, so the
reviewed model survives. Redirected input never asks and overwrites, exactly as
before — except inside a `run` script, which refuses to replace an app that is
already there.

### After it generates

Generation does not stop at a path on disk. The CLI compiles the project it just
wrote and reports the outcome (`BuildReport`), then — on a green build, and for
anything but a class library — offers to start it. The app is launched detached
into its own window (`AppLauncher`), so the prompt returns immediately: MSBuild's
`Name -> path.dll` line gives the apphost beside it, which is what keeps a console
window from sitting behind a WPF app, with `dotnet run --project` as the fallback.

A failed build lists up to ten distinct errors — deduplicated, because MSBuild
repeats each one per referencing project — and the command to rerun it. A timeout
is reported as its own outcome rather than as a failure, since nothing is then
known to be wrong with the generated code.

Last come the next-step lines (`NextSteps`): how to run it, where the seed rows
are, and how to run the generated tests, varying by target — a class library is
told how to be referenced rather than run, since `dotnet run` on one fails with
MSB4057.

The build and the run offer are gated on `Console.IsInputRedirected`, the same
gate the wizard and the review loop use: a piped run does neither, so scripted
end-to-end runs neither slow down nor change shape. The next-step lines are
always printed.

## Example session

```
create console project Shop
use project Shop
create class Product with properties Name string, Price decimal, Cost decimal
add property Sku string to Product
list properties Product
add method Save to Product
add validation Name is required on Name in Product
add business validation Price must be greater than Cost in Product
show validation Product
show business validation Product
generate validator Product
analyze class Product
```
