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

`Pager.Write` pauses a page at a time for `help all` (~250 lines) and the candidate
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

## Command reference

Angle brackets `<...>` are placeholders. Commands are case-insensitive.

### Projects

| Command | Description |
| --- | --- |
| `create console project <Name>` | Create a console project. |
| `create class library <Name>` | Create a class-library project. |
| `create webapi project <Name>` | Create an ASP.NET Core Web API project. |
| `create worker service <Name>` | Create a Worker Service project. |
| `create wpf project <Name>` | Create a WPF project. |
| `create blazor project <Name>` | Create a Blazor project. |
| `create project <Name>` | Create a project (defaults to console). |
| `list projects` | List projects in the workspace. |
| `use project <Name>` \| `use project <index>` | Select the active project. |
| `current project` | Show the selected project. |

New projects are created in `<workspace>/<Name>/<Name>.csproj`.

### Classes

| Command | Description |
| --- | --- |
| `create class <Name>` | Create an empty class. |
| `create class <Name> with properties <Prop> <type>, <Prop> <type>` | Create a class with properties. |
| `create interface <IName>` | Create an interface. |
| `show class <Name>` | Print a class's source. |
| `list classes` \| `list classes in <Project>` | List classes (whole workspace, or one project). |
| `rename class <Old> to <New>` | Rename a class. |
| `delete class <Name>` | Move a class to the recycle bin. |

New classes are saved into the selected project, or into the workspace when no
project is selected.

### Properties

| Command | Description |
| --- | --- |
| `add property <Name> <type> to <Class>` | Add a property (type optional; defaults to `string`). |
| `remove property <Name> from <Class>` | Remove a property. |
| `list properties <Class>` \| `show properties <Class>` | List a class's properties. |

### Methods

| Command | Description |
| --- | --- |
| `add method <Name> to <Class>` | Add a method to a class. |
| `show methods <Class>` | List a class's methods. |

### Validation

Two independent kinds of rule share one engine. **Data validation** is
field-level (constraints on a single property); **business validation** is
entity-level (rules that span multiple fields or express domain logic).

| Command | Description |
| --- | --- |
| `add validation <Rule> on <Property> in <Class>` | Attach a **data** rule to a property — e.g. `required` (or `should not be blank`), `must be between 18 and 65`, `length must be between 2 and 50`, `must be greater than 0`, `must be a valid email`, `must match <regex>`, `must be India, USA, UK`. |
| `add business validation <Rule> in <Class>` | Attach an entity-level **business** rule — e.g. `EndDate must be after StartDate`, `Discount must not exceed Price`, `TrackingNumber is required when Status is Shipped`. |
| `show validation <Class>` | Show the data rules registered for a class. |
| `show business validation <Class>` | Show the business rules registered for a class. |
| `generate validator <Class>` | Generate a `<Class>.Validation.cs` partial with a `Validate()` method containing both the data-validation and business-rule checks. |

The rule set is extensible: each rule type (required, length/range, regex,
allowed-values, cross-field comparison, conditional-required, …) is a
self-contained unit discovered at startup, so new rule types can be added
without changing the commands, the store, or the generator. See
[`CodeGenerationEngine`](../CodeGenerationEngine/README.md#validation) for the design.

The rule text you type is composed into a `"<Property> <predicate>"` fragment
before parsing, so a predicate that already carries its own verb
(`must be between 18 and 65`) is used as-is, while a bare one (`required`) has
`is` inserted. Naming the property yourself
(`add validation Age must be between 18 and 65 on Age in Employee`) also works.

### Analysis

| Command | Description |
| --- | --- |
| `analyze class <Class>` | Report a class's properties, methods, LOC, and warnings. |
| `analyze project` | Summarize the current project. |
| `generate project report` | Generate a report for the current project. |

### Recycle bin

| Command | Description |
| --- | --- |
| `show deleted classes` \| `list deleted classes` | List deleted classes. |
| `restore class <Name>` | Restore a specific deleted class. |
| `restore last class` | Restore the most recently deleted class. |

### Domain discovery

An interactive `build app` opens a workflow page in the browser and carries the whole build
there: the console prints the address, says that Ctrl-C cancels, and waits. The page draws the
pipeline as eight connected stages — describe, application type, data, look, discover, review,
generate, run — with the stage being answered open beneath the diagram. The last one starts what
was just built, because a project that compiles is not yet an application the user has seen work.

The boxes are the navigation, so there are no Back and Next buttons anywhere. Each box shows the
answer it holds ("Console", "orders.csv", "12 classes"), and the box being answered carries both
moves: the step forward, and an arrow back to the stage before it. The box that step leads to
lights up as a second way forward — except where the step is an action with a name of its own:
`Generate` and `Run` stay disabled until the project has actually been written, because until then
there is nothing in them to go to. Clicking a finished box goes back to change it, keeping whatever
the open panel had already answered; changing the description or the data source discards the model
that was discovered from the old one, because it was discovered from something the user has since
changed — so that change asks first, counting the classes it would cost, and holds the answer
unapplied until it is confirmed. Nothing is lost while the question is on screen. `Cancel` asks the
same way once there is an answer to lose, saying what it would cost — and saying plainly that a
project already written stays where it is, because cancelling does not delete it; on a first page
with nothing answered it simply cancels. Typing a description inline
(`build app: a library that lends books`) opens the page on the application type instead.

The whole workflow can be walked from the keyboard. The panel that opens takes the keyboard — once
per stage, so a repaint mid-answer does not pull the caret out of a field — and Enter takes the step
the open box offers. Plain steps only, by the same rule that keeps a click on another box from
taking a named one: `Generate` writes a project to disk and stays a deliberate click. Enter in the
description is a new line, that being the one answer here where it means one.

The kinds of app are WPF (Windows desktop), Windows Desktop (WinForms), ASP.NET Web API, Web
Application (Blazor Server), Console, Class Library and Worker Service.

Nothing waits in silence. Each long step — the Wikipedia fetch, writing the project, compiling it,
starting it — names what it is doing with the seconds counting up beside it, ticks itself off when
it finishes, and the compiler's own output appears line by line while the build runs rather than
all at once at the end.

The page is served by Kestrel in this process on a loopback port with a one-off token in its
address, and lives exactly as long as the command. `CG_NO_WEB=1` keeps the console wizard, for a
session with no browser to open — as does redirected input, below.

Exactly as long as the command, in both directions. A second tab on the same address attaches to
the running workflow rather than starting a rival one against the same workspace, and the two stay
in step: whichever tab takes a step, the other follows it. Closing the last one ends the command
and says so, after a grace period long enough that a page refresh is not mistaken for leaving. A
workflow nobody has opened yet is not abandoned, though — the address is printed whether or not a
browser could be launched, and pasting it a minute later finds the page still waiting.

Targets: `wpf` (default), `winforms`, `console`, `webapi`, `blazor`, `classlib`, `worker`.
`approve <target>` at the review loop overrides the type chosen at the start. SQLite persistence
is available for every target except `wpf` and `winforms`, which are JSON-only.

Piped or redirected input opens no page and asks no questions: it uses the inline form
(`build app: <description> from <format> <location>`), picks a target at `approve`, and drives the
typed review loop exactly as it always has.

| Command | Description |
| --- | --- |
| `build app: <description>` | Describe a system in plain English; this answers the workflow's first stage — see above. Interactively the rest happens on the page: application type, data, look, then [domain discovery](../DomainDiscovery/README.md), the model review, generate-and-build, and finally running it. [`AppScaffolding`](../AppScaffolding/README.md) writes a runnable project into the workspace when the review is approved. |
| `build app` | Starts the same workflow with nothing filled in, so the page opens on the description — see [From existing data](#from-existing-data) below for what the data stage adds. Piped, it prints usage. |
| `build app[: <description>] from <format> <location> [normalize]` | Builds from existing data with no prompts when piped, so it is scriptable; interactively the file is already answered and the page opens on what is left. Formats: `csv`, `txt`, `json`, `excel`. Add `normalize` to apply every normalization change the data needs. |

In the review loop:

| Input | Effect |
| --- | --- |
| `approve` \| `a` | Generate an app from the model as it stands, using the type chosen at the wizard's start; a piped run (no wizard, so no type chosen) defaults silently to WPF instead. |
| `approve <target>` | Generate straight to a target, overriding the type chosen at the start (or skipping the prompt, if none was chosen) — one of `wpf` (default), `winforms`, `console`, `webapi`, `blazor`, `classlib`, `worker`. |
| `add <name>` | Add an entity. The name keeps the shape you type (`add CashRegister` gives `CashRegister`), and several words become one class name (`add cash register` gives the same). |
| `remove <name>` | Remove an entity. |
| `rename <old> <new>` | Rename an entity. |
| `field <entity> <name>[:<type>]` | Add a field (type defaults to `string`). |
| `unfield <entity> <name>` | Remove a field. |
| `why [name]` | Explain what discovery decided. Bare, it reports every decision grouped by outcome; with a name, it shows that name's whole trail — the stage, the reason and the evidence behind each step. |
| `cancel` \| `c` | Discard the model and generate nothing. |

Every target shares the same generated core — the model classes, a JSON store,
and a typed `<Entity>Repository` (`GetAll`/`GetById`/`Create`/`Update`/`Delete`)
per entity — and differs only in the front end. The **console** app, for example,
is a runnable CRUD menu over those repositories, written to a `<AppName>.Console`
project you can `dotnet run`:

```
build app: a library that lends books to members
approve console
```

A bare `approve` uses the type the wizard already asked for. A piped run never ran
the wizard, so it has no chosen type; `ResolveTarget` defaults that case silently
to WPF — no prompt, nothing read, so existing scripts are unaffected.
(`ResolveTarget` also has a prompt-and-blank-defaults-to-WPF fallback for when no
target is preselected and input isn't redirected, but the wizard now always
preselects one, so that fallback is unreachable in ordinary interactive use.)

### Overwriting an existing project

`AppScaffolder` reuses a project whose `.csproj` already exists — generated sources
are rewritten, the project file and hand-added files are not. That is deliberate and
tested, but it is not always what the user meant, so `Generate` asks first
(`OverwritePrompt`), offering overwrite / rename / cancel with Enter cancelling.

The trigger is `GeneratedProjectPath.Exists`, which is the same test the scaffolder
uses to decide reuse. Both go through that one type on purpose: a guard that derived
the folder even slightly differently from the writer would not fail loudly, it would
silently stop firing.

`r` re-asks after renaming, since the new name can collide too. `Generate` returns
`false` on a decline and the review loop continues rather than exiting, so the
reviewed model survives. Redirected input never asks and overwrites, exactly as
before.

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

Discovery reads Wikipedia from the local store built by
[`WikipediaDumpImporter`](../WikipediaDumpImporter/README.md). Without that store
it falls back to the live MediaWiki API, caching pages under `.dd-cache` in the
workspace; the CLI warns at startup when the store is missing.

#### From existing data

Guessing at Wikipedia is the wrong pipeline when the user already has the data, so the data
stage asks. Interactively that stage is on the workflow page, which offers a format, a path box
and a drop zone, and refuses a format this build has no reader for before it is chosen. The
transcript below is the console wizard, which is what a redirected run and `CG_NO_WEB=1` still
get:

```
CodeGenerator >> build app

Describe the application you want to build.
(Enter to skip if you'll point at a data file instead, 'c' to cancel)
> a shop that tracks orders

Application type:
  1) WPF (Windows desktop)
  2) Windows Desktop (WinForms)
  3) ASP.NET Web API
  4) Console
  5) Class Library
  6) Worker Service (background)
Choose [1-6] (default 1, c to cancel): 4

Do you have existing data? [y/N]: y
Format [sql/excel/txt/json/csv] (c to cancel): csv
CSV file path (c to cancel): D:\data\orders.csv
```

(The application type menu and the two questions under it are shown here in their
numbered/typed fallback form, which is what a redirected run gets; interactively
each is a list navigated with arrow keys, arriving with your current answer
selected. The file path is typed either way.)

| Answer | Effect |
| --- | --- |
| `n`, or blank | The description path, completely unchanged. |
| `y` | [`DataIngestion`](../DataIngestion/README.md) reads the source and builds the model from its schema — no Wikipedia call at all. |
| `c` at any prompt | Cancels. Deliberately **not** the same as "no data": backing out must not hand you a Wikipedia build you never asked for. |

A mistyped path re-asks rather than aborting, the same way an unknown target key
does. `excel` and `sql` are on the menu but have no reader in this build; choosing
one says so and names what to use instead, then asks again.

Once the source is read, the normalization check runs:

```
Read 1 table(s) from orders.csv: orders (500 rows)

Normalization check on orders.csv (500 rows):

  1 ! Repeating group: Item1, Item2, Item3
        -> split into OrderItem (many per Order)
  2 ! Partial dependency: CustomerName, CustomerCity
        -> split into Customer (Order references it)

  [n]ormalize   n <1 3> subset   [k]eep as-is   [d]etail <n>:
```

| Input | Effect |
| --- | --- |
| `n` | Apply every finding. |
| `n 1 3` | Apply that subset. An out-of-range number is reported, never silently ignored. |
| `k` | Keep the table exactly as it is. |
| `d <n>` | Print the rows behind finding `n` — a finding you cannot inspect is one you cannot trust, the same principle as `why`. |

Then it is the ordinary review loop, with entities marked `[Data]` rather than
`[Seed]` or `[Wiki]`, and `why <name>` able to explain a class that came from a
normalization split. On `approve` the generated app is **seeded with your rows**:
`Data/Seed/<Class>.json` per entity, loaded on first run, capped at 1000 rows per
entity with the cap stated rather than silent.

To skip the description and data questions — for scripts, or just brevity — name
the source inline. Interactively you're still asked the application type (add
`approve <target>` to override it too); piped input skips every question,
including that one:

```
build app: order tracking from csv D:\data\orders.csv
build app from csv D:\data\orders.csv                     # app name from the file
build app: orders from csv "C:\My Data\orders.csv" normalize
```

`from` is recognized only at its **last** occurrence followed by a known format
keyword, so `build app: track orders from suppliers from csv D:\orders.csv` keeps
the whole description. Quoted paths and paths containing spaces are handled.

Redirected stdin **never prompts** — the precedent `ResolveTarget` set, and what
every existing piped script depends on. A piped run with no inline source takes
the description path exactly as before; a piped run with an inline source prints
the findings and applies **nothing** unless `normalize` was given, so scripts stay
deterministic.

### Batch

| Command | Description |
| --- | --- |
| `batch` | Enter several commands, one per line, ending with a blank line. The CLI shows every line it detected — marking any it could not understand, and any whose command it recognizes but cannot yet run — and asks you to approve each of the rest individually; only approved commands run. If an approved command fails, the whole batch is rolled back and the workspace is restored to its pre-batch state. This restores files, the project registry and the learned-intent memory — console output already printed is not undone. If the restore itself fails, the pre-batch snapshot is kept on disk and its path is printed so you can recover by hand. |

### Utility

| Command | Description |
| --- | --- |
| `demo` | Run a short demo workflow. |
| `status` | Workspace, projects, rules, AI, and the store — everything startup no longer prints. |
| `help` | One-screen overview: starter commands, then the categories. |
| `help <topic>` | One category's commands, or one command's entry (`help approve`). |
| `help all` | Every command in one list. |
| `exit` | Quit. |

## Example session

```
create console project Shop
use project Shop
create class Product with properties Name string, Price decimal
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
