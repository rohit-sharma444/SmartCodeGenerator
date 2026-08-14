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
  analyzes English into   discovers a candidate      reads CSV / TXT / JSON,
  entities/relationships  domain model               checks it for normalization
  /intent                 (NLE + Wikipedia)          problems, and builds the
        │                    │                       model from the schema
        ▼                    │                           │
NaturalLanguageBridge        └─────────┬─────────────────┘
  connects the NLP result              ▼
  to the generator             interactive review loop
        │                      (approve / add / remove / rename / why)
        ▼                              │
CodeGenerationEngine                   ▼  approve <target>
  resolves the command and     AppScaffolding
  generates/edits code (Roslyn)  writes a runnable app for the chosen target
                                 (WPF, console, web API, class lib, worker),
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
| [`CodeGenerator`](CodeGenerator/README.md) | Interactive command-line front end. **Start here** for the command reference. |
| [`CodeGenerationEngine`](CodeGenerationEngine/README.md) | Core library: intent handling, entity extraction, and the project/class/property/method services plus data & business validation on an extensible rule registry (Roslyn-based generation). |
| [`NaturalLanguageEngine`](NaturalLanguageEngine/README.md) | Multi-stage NLP pipeline that turns English text into entities, relationships, and intent. |
| [`NaturalLanguageBridge`](NaturalLanguageBridge/README.md) | Wires the NLP engine to the code generator. |
| [`DomainDiscovery`](DomainDiscovery/README.md) | Turns a plain-English system description into a candidate domain model (used by `build app:`). |
| [`DataIngestion`](DataIngestion/README.md) | Turns data you already have — CSV, delimited text, or JSON — into the same candidate domain model, checked for normalization problems, with the rows carried through as the generated app's seed data. |
| [`AppScaffolding`](AppScaffolding/README.md) | Turns an approved domain model into a runnable app on disk for the chosen target — WPF, console, web API, class library, or worker (the last stage of `build app:`). |
| [`WikipediaDumpImporter`](WikipediaDumpImporter/README.md) | One-off tool that builds the local Wikipedia store `DomainDiscovery` reads offline, and maintains it afterwards. Not part of the normal build-and-run loop. |
| [`CodeGenerator.Tests`](CodeGenerator.Tests/README.md), [`NaturalLanguageEngine.Tests`](NaturalLanguageEngine.Tests/README.md), [`DomainDiscovery.Tests`](DomainDiscovery.Tests/README.md), [`DataIngestion.Tests`](DataIngestion.Tests/README.md) | Test suites for the engine, the generator, domain discovery, and data ingestion. |

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

Startup prints three lines and the prompt. It reports its own state only when
something is wrong with it — a missing Wikipedia store, nothing updating it, an
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

The full command list lives in [`docs/commands.md`](docs/commands.md), which is
generated from the command catalog and kept in sync by a test.

See [`CodeGenerator/README.md`](CodeGenerator/README.md) for the full command
reference and examples, and [`NaturalLanguageEngine/README.md`](NaturalLanguageEngine/README.md)
for the NLP pipeline.

## Example

```
create console project Shop
use project Shop
create class Product with properties Name string, Price decimal
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

The CLI runs domain discovery, prints the recognized topic and candidate
entities, and drops you into a review loop where you can `approve`, `add <name>`,
`remove <name>`, `rename <old> <new>`, or `cancel`. On `approve`, it asks which
kind of app to generate, then [`AppScaffolding`](AppScaffolding/README.md) writes
a runnable project for that target.

It then **compiles what it wrote** and says so, rather than leaving you to find
out yourself:

```
Generated project at D:\Work\Shop

Building...
Build succeeded (4.1s)

Run it now? [y/N]: y
Started Shop.exe

Next steps:
  Run it       dotnet run --project D:\Work\Shop
  Seed data    D:\Work\Shop\Data\Seed\   (edit these, then re-run)
  Tests        dotnet test D:\Work\Shop.Tests
```

A failing build lists the errors and the command to rerun it; the run offer is
skipped, and a class library is never offered as something to run. Answering `y`
starts the app detached, in its own window, so the prompt comes straight back.

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
the one that cannot lose work.

The build and the run offer are **interactive only** — a piped or scripted run
skips both, so existing scripts behave exactly as before. The next-step lines are
always printed.

The targets are `wpf` (the default), `console`, `webapi`, `classlib`, and
`worker`. All of them share the same generated core — the model classes, a JSON
store, and a typed `<Entity>Repository` with `GetAll`/`GetById`/`Create`/
`Update`/`Delete` per entity — and differ only in the front end. For example, the
**console** app is a runnable CRUD menu: a top-level list of entities, each with
list/add/delete backed by its repository. Pick it by answering `console` at the
prompt, or skip the prompt with an inline target:

```
build app: a library that lends books to members
approve console
```

This writes a `<AppName>.Console` project you can `dotnet run`. Answering with a
blank line (or piping input, so scripts stay unchanged) keeps the original WPF
CRUD app.

Discovery reads Wikipedia from a **local store** built by
[`WikipediaDumpImporter`](WikipediaDumpImporter/README.md) — local, deterministic,
and free of the API's rate limit. If that store is absent (a fresh clone), it
falls back to the live MediaWiki API, caching pages under `.dd-cache` in the
workspace.

## Building an app from data you already have

Describing a system is the right way in when you have nothing but an idea. When
you already have the data, the data says it better: its columns are the fields,
its values are the types, and its rows are the app's starting content. Run
`build app` with no description and it asks:

```
CodeGenerator >> build app
Do you have existing data? [y/N]: y
Format [sql/excel/txt/json/csv] (c to cancel): csv
CSV file path (c to cancel): D:\data\orders.csv
```

Answer `n` (or just press Enter) and you get the description path above,
unchanged. Answer `y` and [`DataIngestion`](DataIngestion/README.md) reads the
file, checks it for normalization problems, and shows what it found:

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
inspect is a finding you cannot trust. From there it is the same review loop as
the description path, and `why OrderItem` will explain that the class came from a
repeating group in your file.

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
```

`from` is recognized only at its **last** occurrence before a known format
keyword, so a description containing the word "from" is not truncated. Redirected
stdin never prompts, so existing piped scripts behave exactly as before and opt
into this path explicitly with the inline form.

`csv`, `txt` (delimiter sniffed), `json` (nested arrays become child entities) and
`excel` (one visible sheet per entity) work today. `sql` is offered at the prompt
and answered with what to use instead — it lands in a follow-on branch.
