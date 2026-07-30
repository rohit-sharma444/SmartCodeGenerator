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

At the `CodeGenerator >>` prompt, type `help` for the full command list or `exit`
to quit.

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

| Command | Description |
| --- | --- |
| `build app: <description>` | Describe a system in plain English; runs [domain discovery](../DomainDiscovery/README.md), prints the recognized topic and candidate entities, and opens a review loop. On approval you pick an output target, and [`AppScaffolding`](../AppScaffolding/README.md) writes a runnable project for it into the workspace. |

In the review loop:

| Input | Effect |
| --- | --- |
| `approve` \| `a` | Generate an app from the model as it stands, prompting for the output target. |
| `approve <target>` | Generate straight to a target, skipping the prompt — one of `wpf` (default), `console`, `webapi`, `classlib`, `worker`. |
| `add <name>` | Add an entity. |
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

A bare `approve` prompts for the target; a blank answer (or piped input, so
existing scripts are unaffected) keeps the WPF default.

Discovery reads Wikipedia from the local store built by
[`WikipediaDumpImporter`](../WikipediaDumpImporter/README.md). Without that store
it falls back to the live MediaWiki API, caching pages under `.dd-cache` in the
workspace; the CLI warns at startup when the store is missing.

### Batch

| Command | Description |
| --- | --- |
| `batch` | Enter several commands, one per line, ending with a blank line. The CLI shows every line it detected — marking any it could not understand, and any whose command it recognizes but cannot yet run — and asks you to approve each of the rest individually; only approved commands run. If an approved command fails, the whole batch is rolled back and the workspace is restored to its pre-batch state. This restores files, the project registry and the learned-intent memory — console output already printed is not undone. If the restore itself fails, the pre-batch snapshot is kept on disk and its path is printed so you can recover by hand. |

### Utility

| Command | Description |
| --- | --- |
| `demo` | Run a short demo workflow. |
| `help` | Show all commands. |
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
