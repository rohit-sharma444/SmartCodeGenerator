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
| `create <Name> class inherit <BaseClass>` | Create a class deriving from a base class. |
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
| `list methods <Class>` \| `show methods <Class>` | List a class's methods. |

### Validation

| Command | Description |
| --- | --- |
| `add validation <Rule> on <Property> in <Class>` | Attach a validation rule (e.g. `Required`) to a property. |
| `show validation <Class>` | Show the validation rules registered for a class. |
| `generate validator <Class>` | Generate a `<Class>.Validation.cs` partial with a `Validate()` method. |

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
| `build app: <description>` | Describe a system in plain English; runs [domain discovery](../DomainDiscovery/README.md), prints the recognized topic and candidate entities, and opens a review loop (`approve` / `add <name>` / `remove <name>` / `rename <old> <new>` / `cancel`). Fetches from Wikipedia the first time (cached under `.dd-cache`). |

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
add validation Required on Name in Product
show validation Product
generate validator Product
analyze class Product
```
