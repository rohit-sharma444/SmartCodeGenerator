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

An interactive `build app` asks three questions before any work starts: what the application is
for, which kind of app to build, and whether you already have data. The kinds are WPF (Windows
desktop), Windows Desktop (WinForms), ASP.NET Web API, Console, Class Library and Windows
Service; the menu is arrow-key driven, and falls back to a numbered prompt when input or output
is redirected. Typing a description inline (`build app: a library that lends books`) answers the
first question and starts at the second.

Targets: `wpf` (default), `winforms`, `console`, `webapi`, `classlib`, `worker`. `approve
<target>` at the review loop overrides the type chosen at the start. SQLite persistence is
available for every target except `wpf` and `winforms`, which are JSON-only.

Piped or redirected input skips the wizard entirely and uses the inline form
(`build app: <description> from <format> <location>`), picking a target at `approve`.

| Command | Description |
| --- | --- |
| `build app: <description>` | Describe a system in plain English; this answers the wizard's first question — see above. Interactively, the wizard still asks which kind of app to build and whether you have data before running [domain discovery](../DomainDiscovery/README.md), printing the recognized topic and candidate entities, and opening a review loop. [`AppScaffolding`](../AppScaffolding/README.md) writes a runnable project into the workspace on approval. |
| `build app` | Starts the same guided flow with no description typed inline, so it asks for one first — see [From existing data](#from-existing-data) below for what the data question adds. Piped, it prints usage. |
| `build app[: <description>] from <format> <location> [normalize]` | Builds from existing data with no prompts when piped, so it is scriptable; interactively you are still asked the application type. Formats: `csv`, `txt`, `json`, `excel`. Add `normalize` to apply every normalization change the data needs. |

In the review loop:

| Input | Effect |
| --- | --- |
| `approve` \| `a` | Generate an app from the model as it stands, using the type chosen at the wizard's start; a piped run (no wizard, so no type chosen) defaults silently to WPF instead. |
| `approve <target>` | Generate straight to a target, overriding the type chosen at the start (or skipping the prompt, if none was chosen) — one of `wpf` (default), `winforms`, `console`, `webapi`, `classlib`, `worker`. |
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

Guessing at Wikipedia is the wrong pipeline when the user already has the data. A
bare `build app` at an interactive prompt asks for a description and an
application type first (see above), then asks about data:

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

(The application type menu shown here is the numbered fallback used when input or
output is redirected; interactively it is the same list navigated with arrow keys.)

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
