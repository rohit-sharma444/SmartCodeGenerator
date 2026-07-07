# Smart Code Generator

Generate and edit .NET code from plain-English commands. You type something like
`create class Customer with properties Name string`, a natural-language pipeline
works out the intent and entities, and a Roslyn-based engine scaffolds the
projects, classes, members, and validators on disk.

## How it fits together

`CodeGenerator` (the CLI) drives two paths. Most commands generate code
directly; `build app: <description>` instead discovers a domain model first.

```
                      CodeGenerator (CLI)  ← the interactive prompt you drive
                             │
        ┌────────────────────┴─────────────────────┐
        │                                           │
  most commands                            build app: <description>
        │                                           │
        ▼                                           ▼
NaturalLanguageEngine                      DomainDiscovery
  analyzes English into                      discovers a candidate domain
  entities/relationships/intent              model (NLE + Wikipedia)
        │                                           │
        ▼                                           ▼
NaturalLanguageBridge                      interactive review loop
  connects the NLP result                    (approve / add / remove / rename)
  to the generator
        │
        ▼
CodeGenerationEngine
  resolves the command and
  generates/edits code (Roslyn)
```

For a fuller picture — component/dependency graph, runtime sequence flows for
both paths, and the NLE internal pipeline — see
[`docs/architecture.md`](docs/architecture.md).

## Projects

| Project | Description |
| --- | --- |
| [`CodeGenerator`](CodeGenerator/README.md) | Interactive command-line front end. **Start here** for the command reference. |
| [`CodeGenerationEngine`](CodeGenerationEngine/README.md) | Core library: intent handling, entity extraction, and the project/class/property/method/validation services (Roslyn-based generation). |
| [`NaturalLanguageEngine`](NaturalLanguageEngine/README.md) | Multi-stage NLP pipeline that turns English text into entities, relationships, and intent. |
| [`NaturalLanguageBridge`](NaturalLanguageBridge/README.md) | Wires the NLP engine to the code generator. |
| [`DomainDiscovery`](DomainDiscovery/README.md) | Turns a plain-English system description into a candidate domain model (used by `build app:`). |
| [`CodeGenerator.Tests`](CodeGenerator.Tests/README.md), [`NaturalLanguageEngine.Tests`](NaturalLanguageEngine.Tests/README.md), [`DomainDiscovery.Tests`](DomainDiscovery.Tests/README.md) | Test suites for the engine, the generator, and domain discovery. |

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

At the `CodeGenerator >>` prompt type `help` for all commands or `exit` to quit.
Commands can also be passed as a single argument (`dotnet run --project
CodeGenerator -- "create console project MyApp"`) or piped in one per line.

See [`CodeGenerator/README.md`](CodeGenerator/README.md) for the full command
reference and examples, and [`NaturalLanguageEngine/README.md`](NaturalLanguageEngine/README.md)
for the NLP pipeline.

## Example

```
create console project Shop
use project Shop
create class Product with properties Name string, Price decimal
add validation Required on Name in Product
generate validator Product
analyze class Product
```

## Discovering a domain from a description

Instead of scaffolding class-by-class, describe the system and let
[`DomainDiscovery`](DomainDiscovery/README.md) propose a model, then review it:

```
build app: a library that lends books to members
```

The CLI runs domain discovery (fetching from Wikipedia the first time; results
are cached under `.dd-cache` in the workspace), prints the recognized topic and
candidate entities, and drops you into a review loop where you can `approve`,
`add <name>`, `remove <name>`, `rename <old> <new>`, or `cancel`.
