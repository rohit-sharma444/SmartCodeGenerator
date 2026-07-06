# Smart Code Generator

Generate and edit .NET code from plain-English commands. You type something like
`create class Customer with properties Name string`, a natural-language pipeline
works out the intent and entities, and a Roslyn-based engine scaffolds the
projects, classes, members, and validators on disk.

## How it fits together

```
user command
     │
     ▼
NaturalLanguageEngine   →  analyzes English into entities, relationships, intent
     │
     ▼
NaturalLanguageBridge   →  connects the NLP result to the generator
     │
     ▼
CodeGenerationEngine    →  resolves the command and generates/edits code
     │
     ▼
CodeGenerator (CLI)     →  the interactive prompt you drive
```

## Projects

| Project | Description |
| --- | --- |
| [`CodeGenerator`](CodeGenerator/README.md) | Interactive command-line front end. **Start here** for the command reference. |
| `CodeGenerationEngine` | Core library: intent handling, entity extraction, and the project/class/property/method/validation services (Roslyn-based generation). |
| [`NaturalLanguageEngine`](NaturalLanguageEngine/README.md) | Multi-stage NLP pipeline that turns English text into entities, relationships, and intent. |
| `NaturalLanguageBridge` | Wires the NLP engine to the code generator. |
| `CodeGenerator.Tests`, `NaturalLanguageEngine.Tests` | Test suites for the engine and the generator. |

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
