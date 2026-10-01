# Smart Code Generator

Generate and edit .NET code from plain-English commands. You type something like
`create class Customer with properties Name string`, a natural-language pipeline
works out the intent and entities, and a Roslyn-based engine scaffolds the
projects, classes, members, and validators on disk.

## How it fits together

`CodeGenerator` (the CLI) drives three paths. Most commands generate code
directly; `build app` either discovers a domain model from a description, or —
when you already have the data — reads the model out of that data's schema.

```
                      CodeGenerator (CLI)  ← the interactive prompt you drive
                             │
        ┌────────────────────┼───────────────────────────┬──────────────────┐
        │                    │                           │                  │
  most commands       build app: <description>    build app + your data
        │                    │                           │
        ▼                    ▼                           ▼
NaturalLanguageEngine   DomainDiscovery            DataIngestion
  analyzes English into   discovers a candidate      reads CSV / TXT / JSON /
  entities/relationships  domain model               Excel / SQL Server, checks
  /intent                 (Ollama, Wikipedia,        it for normalization
        │                 built-in domains)          problems, and builds the
        │                    │                       model from the schema
        ▼                    │                           │
NaturalLanguageBridge        └─────────┬─────────────────┘
  connects the NLP result              ▼
  to the generator          review: the workflow page, or
        │                  the typed review loop when piped
        ▼                              │
CodeGenerationEngine                   ▼  approve <target>
  resolves the command and     AppScaffolding
  generates/edits code (Roslyn)  writes a runnable app for the chosen target
                                 (WPF, WinForms, console, web API, Blazor,
                                 class library, worker),
                                 seeded with your imported rows
```

Both `build app` paths converge on the same review loop and the same scaffolder,
because both produce the same `CandidateDomainModel`.

For a fuller picture — component/dependency graph, runtime sequence flows for
both paths, and the NLE internal pipeline — see
[`docs/architecture.md`](docs/architecture.md).

## Projects

| Project | Description |
| --- | --- |
| [`CodeGenerator`](CodeGenerator/README.md) | Interactive command-line front end, plus the `build app` workflow page and the `chat` page it serves in the browser. The command reference is [`docs/commands.md`](docs/commands.md). |
| [`CodeGenerationEngine`](CodeGenerationEngine/README.md) | Core library: intent handling, entity extraction, and the project/class/property/method services plus data & business validation on an extensible rule registry (Roslyn-based generation). |
| [`NaturalLanguageEngine`](NaturalLanguageEngine/README.md) | Multi-stage NLP pipeline that turns English text into entities, relationships, and intent. |
| [`NaturalLanguageBridge`](NaturalLanguageBridge/README.md) | Wires the NLP engine to the code generator. |
| [`DomainDiscovery`](DomainDiscovery/README.md) | Turns a plain-English system description into a candidate domain model (used by `build app:`), from a local Ollama model, the Wikipedia store, its 25 built-in domains, or online Wikipedia. |
| [`DataIngestion`](DataIngestion/README.md) | Turns data you already have — CSV, delimited text, JSON, Excel, or a SQL Server database — into the same candidate domain model, checked for normalization problems, with the rows carried through as the generated app's seed data. |
| [`AppScaffolding`](AppScaffolding/README.md) | Turns an approved domain model into a runnable app on disk for the chosen target — WPF, Windows Forms, console, web API, Blazor web app, class library, or worker (the last stage of `build app:`). |
| [`WikipediaDumpImporter`](WikipediaDumpImporter/README.md) | One-off tool that builds the local Wikipedia store `DomainDiscovery` reads offline, and maintains it afterwards. Not part of the normal build-and-run loop. |
| `WikipediaUpdateService` | Windows service (`SmartCodeGenWikipediaUpdater`) that keeps an installed Wikipedia store up to date, once every 24 hours by default. Installed with `tools\install-updater-service.ps1`. |
| `CodeIntelligence`, `CodeIntelligence.Abstractions`, `CodeIntelligence.Onnx` | The AI layer, on by default: for a line the rules do not recognize, it embeds the line with a local ONNX model, finds the nearest known phrasings in a SQLite index, and suggests the command they point to — it never runs one you did not confirm (`ai status`, `ai enable`/`ai disable`). The abstractions project holds the contracts only; the ONNX project holds the model runtime. |
| [`CodeGenerator.Tests`](CodeGenerator.Tests/README.md), [`NaturalLanguageEngine.Tests`](NaturalLanguageEngine.Tests/README.md), [`DomainDiscovery.Tests`](DomainDiscovery.Tests/README.md), [`DataIngestion.Tests`](DataIngestion.Tests/README.md), `CodeIntelligence.Tests` | Test suites for the engine, the generator, domain discovery, data ingestion, and the AI layer. |

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or newer.

## Build & test

```bash
# from the repository root
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx
dotnet test  SmartCodeGenerator/SmartCodeGenerator.slnx
```

## Run

```bash
dotnet run --project CodeGenerator
```

The workspace (where projects and classes are written) defaults to the current
directory; set `PROJECTPATH` to choose another location:

```bash
# Windows (PowerShell)
$env:PROJECTPATH = "D:\MyWorkspace"; dotnet run --project CodeGenerator

# bash
PROJECTPATH=/path/to/workspace dotnet run --project CodeGenerator
```

**Ctrl-C** asks rather than kills: the first press cancels what is running and leaves
the workspace consistent, a second press quits outright. Any command still going
after a moment says so — `Working | 2s` — and leaves the time it took behind when
it finishes; commands that return at once show nothing extra. Long output
(`help all`, a large model) pauses a page at a time.

**`demo`** runs a short guided walkthrough — seven real commands that create a small
project, add a class with two validation rules and generate the validator. It names
the folder it will create, asks first, and refuses if that folder already exists.

`CodeGenerator --version` prints the version alone, and `CodeGenerator --help`
prints the command overview — neither enters the prompt. `-v`, `-h` and `/?` work
too.

Output is colour-coded — green when something was produced (a class written, a
validator generated, a rule registered), yellow for something you can fix (no project
selected, a class not found, a command not recognized), red for a real failure, grey
for secondary detail. Colour is switched off entirely when output is
redirected, and when `NO_COLOR` is set (to any value, per
[no-color.org](https://no-color.org)), so piped transcripts are plain text. Set
`FORCE_COLOR` to colour redirected output anyway — for a host that captures the
stream and renders the escape sequences itself.

Colour is delivered as ANSI escape sequences where the terminal supports them
(virtual-terminal processing is enabled at startup), and as Windows console
attributes where it does not. `status` reports which, which is the quickest way to
diagnose a terminal that shows no colour.

An unexpected failure reports what happened, not its exception type. Set
`CG_VERBOSE=1` to get the type name and stack trace back.

Startup prints three lines and the prompt, the first carrying the version. It
reports its own state only when something is wrong with it — a Wikipedia store that
`discovery.json` pins discovery to and that is missing, nothing updating an installed one, an
AI model it was told to load and could not — so a quiet start means everything it
depends on is in order. `status` prints the full picture: workspace, projects and
which is selected, loaded rules, AI state, and the store and its updater service.

At the `CodeGenerator >>` prompt type `help` or `exit` to quit. `help` shows a
one-screen overview — a few commands to start with, then the categories;
`help <topic>` drills into one category (or one command, so `help approve`
works), and `help all` prints the complete catalog. Press TAB for suggestions,
`history` to see recent commands, and `clear` (or `cls`) to wipe the screen. Commands can also be passed as a single argument
(`dotnet run --project CodeGenerator -- "create console project MyApp"`) or piped
in one per line.

**The command reference is [`docs/commands.md`](docs/commands.md)**: every command
with a description and an example, generated from the command catalog and kept in
sync by a test. The [user manual](docs/user-manual-source.html) explains how to use
them; [`NaturalLanguageEngine/README.md`](NaturalLanguageEngine/README.md) covers
the NLP pipeline.

## Example

```
create console project Shop
use project Shop
create class Product with properties Name string, Price decimal, Cost decimal
add validation Name is required on Name in Product
add business validation Price must be greater than Cost in Product
generate validator Product
analyze class Product
```

## Discovering a domain from a description

Instead of scaffolding class-by-class, describe the system and let
[`DomainDiscovery`](DomainDiscovery/README.md) propose a model, then review it:

```
build app: a library that lends books to members
```

At a console this opens the **build workflow page** in your browser. The console
prints its address, says that Ctrl-C cancels, and waits; the page walks ten
stages — description, application type, data, look, discover, review, output,
entry template, generate, run — and each stage box is also how you go back to
change an answer. The page is served by the tool itself on a loopback address
and closes when the command ends. Chapter 4 of the
[user manual](docs/user-manual-source.html) describes every stage.

Piped or scripted input opens no page and asks no questions. Discovery prints
the source that answered, then a typed review loop reads the next lines:
`approve`, `add <name>`, `remove <name>`, `rename <old> <new>`, `field`,
`why`, `cancel` and more — `?` lists them all. `CG_NO_WEB=1` keeps the
console instead of the page for an interactive session too, asking the
questions there. On `approve`, [`AppScaffolding`](AppScaffolding/README.md)
writes a runnable project for the chosen target.

On the console path it then **compiles what it wrote** and says so, rather than
leaving you to find out yourself:

```
Generated project at D:\Work\Shop

Building...
Build succeeded (4.1s)

Run it now? [y/n]: y
Started Shop.exe

Next steps:
  Run it       dotnet run --project D:\Work\Shop
  Seed data    D:\Work\Shop\Data\Seed\   (edit these, then re-run)
  Tests        dotnet test D:\Work\Shop.Tests\Shop.Tests.csproj
```

A failing build lists the errors and the command to rerun it; the run offer is
skipped, and a class library is never offered as something to run. Answering `y`
starts the app detached, in its own window, so the prompt comes straight back. A
web application has no window of its own, so the tool waits until it is
listening — it restores and builds first — and then opens it in your browser.
On the workflow page the same build and run are its last two stages.

If a project of that name is already there, you are asked before anything is
overwritten:

```
D:\Work\Shop already holds a generated project.
Regenerating overwrites its generated sources — Models, Data, Views and the
*.Generated.cs validator halves. Your .Custom.cs rules, the project file and
anything you added yourself are kept.

  [o]verwrite   [r]ename   [c]ancel   (Enter cancels)
```

`r` generates under another name and leaves the existing project alone; `c` (or
Enter) writes nothing and returns you to the review loop with the model intact.
Enter cancels rather than overwrites on purpose — the effortless answer should be
the one that cannot lose work. When the model has classes the project does not,
`[a]dd the N new` is offered as well, and writes only those.

The build and the run offer are **interactive only** — a piped or scripted run
skips both, so existing scripts behave exactly as before. The next-step lines are
always printed.

The targets are `wpf` (the default), `winforms`, `console`, `webapi`, `blazor`,
`classlib` and `worker`. All of them share the same generated core — the model
classes, a JSON store, and a typed `<Entity>Repository` with
`GetAll`/`GetById`/`Create`/`Update`/`Delete` per entity — and differ only in the
front end. For example, the **console** app is a runnable CRUD menu: a top-level
list of entities, each with list/add/delete backed by its repository. A piped run
names its target at `approve`:

```
build app: a library that lends books to members
approve console
```

This writes a `<AppName>.Console` project you can `dotnet run`. A bare `approve`
in a piped run generates the WPF app.

Discovery tries its sources in this order, moving on whenever one finds no classes:

1. **Ollama**, a local language model, when it is running (`discovery.json`
   names the model; the default is `qwen2.5:3b`).
2. The **local Wikipedia store**, when one is installed — built by
   [`WikipediaDumpImporter`](WikipediaDumpImporter/README.md) at
   `%ProgramData%\SmartCodeGenerator\Wikipedia\enwiki-full.db`, and kept up to
   date by `WikipediaUpdateService`.
3. The **25 built-in domains** that ship inside `DomainDiscovery` (a library,
   a clinic, an online shop and so on).
4. **Online Wikipedia**, through the MediaWiki API with pages cached under
   `.dd-cache` in the workspace — but only when no store is installed. With a
   store, the built-in domains are the last source.

When none of them recognizes the description, the review starts from the classes
named in the description's own words, and you add the rest by hand. Every run
says which source answered, and why it fell back if it did.

## Building an app from data you already have

Describing a system is the right way in when you have nothing but an idea. When
you already have the data, the data says it better: its columns are the fields,
its values are the types, and its rows are the app's starting content. On the
workflow page, the **Data** stage asks for it: a format, then a file (typed or
dropped on the page) or, for SQL Server, a connection string and the tables to
read. Choose no data and you get the description path above, unchanged.
[`DataIngestion`](DataIngestion/README.md) then reads the source, checks it for
normalization problems, and shows what it found. The console form of that check
looks like this:

```
Read 1 table(s) from orders.csv: orders (500 rows)

Normalization check on orders.csv (500 rows):

  1 ! Repeating group: Item1, Item2, Item3
        -> split into OrderItem (many per Order)
  2 ! Partial dependency: CustomerName, CustomerCity
        -> split into Customer (Order references it)

  [n]ormalize   n <1 3> subset   [k]eep as-is   [d]etail <n>:
```

`n` applies every finding, `n 1 3` applies that subset, `k` keeps the table flat,
and `d 2` prints the actual rows behind finding 2 — because a finding you cannot
inspect is a finding you cannot trust. From there it is the same review as
the description path, and `why OrderItem` will explain that the class came from a
repeating group in your file. The review also offers the rules your data
supports — `rules` lists them, `accept <n>` applies one.

On `approve`, the generated app is **seeded with your rows**: the scaffolder writes
`Data/Seed/<Class>.json` per entity and the generated seeder loads it on first run,
falling back to sample rows for any entity with no imported data. Rows are capped
at 1000 per entity, and the cap is stated rather than silent.

To skip every prompt — for a script, or just to type it in one line — name the
source inline:

```
build app: order tracking from csv D:\data\orders.csv
build app from csv D:\data\orders.csv            # app name from the file name
build app: orders from csv D:\data\orders.csv normalize   # apply every finding
build app: a shop from sql "Server=.;Database=Shop" tables Customer
```

`from` is recognized only at its **last** occurrence before a known format
keyword, so a description containing the word "from" is not truncated. Redirected
stdin never prompts, so existing piped scripts behave exactly as before and opt
into this path explicitly with the inline form; a piped run applies no
normalization finding unless `normalize` is on the line.

The formats are `csv`, `txt` (delimiter sniffed), `json` (nested arrays become
child entities), `excel` (one visible sheet per entity) and `sql` (a SQL Server
database: column types come from the database, and declared foreign keys become
relationships). See chapter 5 of the [user manual](docs/user-manual-source.html).

## Changing your mind, and repeating yourself

- **`undo`** reverses the last command that changed something — the files it
  wrote, added or deleted, the validation rules it added, and the current project
  and class — and **`redo`** puts it back. A `build app`, `batch` or `demo` is one
  step.
- **`save session <file>`** writes the commands of this session that worked and
  changed something to a `.cgs` script; **`run <file>`** replays one, and puts
  everything back if a line fails.
- **`chat`** (or `ui`) opens the same tool as a page in your browser: a
  transcript of commands and their output, a Solution Explorer–style tree of the
  loaded workspace, and Undo and Redo buttons beside Send.

The [user manual](docs/user-manual-source.html) covers all three (chapters 3 and
17).

## Checking generated apps end to end

[`tools/app-check`](tools/app-check/README.md) generates an app from every prompt
in its `prompts.json`, then builds each one, runs its tests, launches it, signs in,
opens a screen and saves a record, and writes one `report.html` with a tick or a
cross per step. It is run on demand:
`powershell -ExecutionPolicy Bypass -File tools\app-check\run.ps1`.
