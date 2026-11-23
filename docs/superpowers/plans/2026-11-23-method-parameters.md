# Method Parameters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `add method`, `create class ... methods ...` and `create interface ... methods ...` take method parameters, in a C# form and an English form.

**Architecture:** One parameter-list parser in NaturalLanguageBridge feeds `MethodDefinition.Parameters` (create class/interface, already emitted by `CodeGenerationEngine.Generate`) and `ParsedCommand.Parameters` (add method). `ClassMethodService.AddMethod` checks and writes them.

**Tech Stack:** .NET 10, C#, Roslyn, MSTest.

**Spec:** `docs/superpowers/specs/2026-11-23-method-parameters-design.md`

## Global Constraints

- C# form: `Name(type name, type name)` — type first. English form: `Name with parameters name type, name type` — name first; `with parameter`, `taking`, `that takes` mean the same.
- A parameter is refused, by name, when its name is not an identifier, repeats another parameter's name, or its type does not bind in the class's project. Nothing is written on a refusal.
- Method bodies are unchanged (`ClassMethodService.GenerateBody`). Overloads stay refused (`MemberNames.Taken`).
- CI: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`.
- Per-task gate: build + focused `--filter`. Full suite only at the end.
- Edit files with the Edit tool, not shell heredocs (they corrupt backslashes and line ends).

## Review Focus

1. `create class Order with methods Apply(decimal rate, string code), Save` — the comma inside the parentheses must not split the method list (Task 2 test).
2. `add method Ship with parameters address string to Order` — the English list ends at ` to <Class>`, and `to` is not read as a type (Task 3 test).
3. A parameter typed as a project class (`Customer customer`) binds when the class exists and is refused when it does not (Task 3 tests).
4. A parameter named like a C# keyword (`string class`) is refused, not written as a broken method (Task 1 test).
5. `add method Area(decimal scale) to IShape` — the implementer warning names the method with its parameter types (Task 3 test).

---

### Task 1: The parameter-list parser

**Files:**
- Create: `NaturalLanguageBridge/MethodParameterParser.cs`
- Test: `CodeGenerator.Tests/Bridge/MethodParameterParserTests.cs`

**Interfaces:**
- Produces: `public static class MethodParameterParser` with
  - `public enum Order { TypeFirst, NameFirst }`
  - `public static bool TryParse(string text, Order order, out List<ParameterDefinition> parameters, out string? error)` — splits on commas that are not inside `<>`; each entry is two words; returns false with `error` for an entry that is not two words, a name that is not an identifier (`SyntaxFacts.IsValidIdentifier` and not a keyword), or a repeated name (case-insensitive). Empty text → true with no parameters. Types are kept as typed; nothing binds here.
  - `public const string EnglishLead = @"(?:with\s+parameters?|taking|that\s+takes)"` — the words that start an English list, for Tasks 2 and 3.

- [ ] **Step 1: Write the failing tests** — `[DataRow]`s:
  - `("decimal percent, string code", TypeFirst)` → `[(percent, decimal), (code, string)]`
  - `("percent decimal, code string", NameFirst)` → same
  - `("Dictionary<string, int> map", TypeFirst)` → one parameter of type `Dictionary<string, int>`
  - `("", TypeFirst)` → true, empty
  - refusals, each asserting `error` contains the quoted text: `("decimal", TypeFirst)` → `'decimal'`; `("string class", TypeFirst)` → `'class' is not a valid parameter name`; `("int a, int A", TypeFirst)` → `'A' is repeated`; `("2x int", NameFirst)` → `'2x' is not a valid parameter name`.
- [ ] **Step 2: Run** `dotnet test CodeGenerator.Tests --filter FullyQualifiedName~MethodParameterParserTests` — expect compile failure.
- [ ] **Step 3: Implement** `MethodParameterParser` as specified.
- [ ] **Step 4: Run** the same filter — expect PASS.
- [ ] **Step 5: Commit** `feat(methods): a parser for method parameter lists`.

### Task 2: Parameters in create class / create interface

**Files:**
- Modify: `NaturalLanguageBridge/CommandEntityExtractor.cs` — `ParseMethod` (the `Name(...)` branch keeps the list; new English branch), `ExtractMethods` (split the method list on commas and `and` outside parentheses)
- Test: `CodeGenerator.Tests/Core/MemberListParseTests.cs`

**Interfaces:**
- Consumes: `MethodParameterParser.TryParse`, `EnglishLead`.
- Produces: `MethodDefinition.Parameters` filled. A method whose list does not parse is left out of `command.Methods`, and recorded in a new `public List<(string Entry, string Error)> IgnoredMethods { get; set; } = [];` on `ParsedCommand` (reset in `Clear()`). `CommandHandlingService.ReportIgnoredProperties` prints each as `Ignored method '<entry>': <error>`, and `HandleCreateInterface` calls it after a save too.

- [ ] **Step 1: Write the failing tests:**
  - `create class Order with properties Total decimal and methods Apply(decimal rate, string code), Save` → methods `Apply` (2 parameters, `decimal rate`, `string code`) and `Save` (none).
  - `create class Order with methods Ship with parameters address string, express bool` → `Ship` with `address string`, `express bool`.
  - `create class Order with methods Apply(decimal rate) returns decimal implements IEntity` → `Apply` returns `decimal`, one parameter, and `Interfaces` = `[IEntity]`.
  - `create interface Shape with methods Area(decimal scale) returns decimal` → same on `command.Methods`.
  - Run `create class Order with methods Apply(decimal rate, string code) returns decimal` and assert the file contains `public decimal Apply(decimal rate, string code)` and compiles (`Compiler.Compile`).
  - `create class Order with methods Apply(decimal) ` → class written without `Apply`; output contains `Ignored method 'Apply(decimal)'`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~MemberListParseTests` — expect the new tests to fail.
- [ ] **Step 3: Implement.** The method-list splitter tracks parenthesis depth; the return type pattern accepts `[\w<>\[\],\.\?]+` as `AddMethodRe` does.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~Bridge|FullyQualifiedName~Method|FullyQualifiedName~Interface"` — expect PASS.
- [ ] **Step 5: Commit** `feat(methods): create class and create interface take method parameters`.

### Task 3: Parameters in add method

**Files:**
- Modify: `NaturalLanguageBridge/CommandFront.cs` — `AddMethodRe` and its `addMethodMatch` block
- Modify: `CodeGenerationEngine/Core/ClassMethodService.cs` — `AddMethod`
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` — `HandleCreateMethod`
- Test: `CodeGenerator.Tests/Core/AddMethodParameterTests.cs` (workspace with `Shop/Shop.csproj`, `Order.cs`, `Customer.cs`, `IShape.cs` + `Square.cs : IShape`, as `CreateClassInProjectTests` sets up)

**Interfaces:**
- Consumes: `MethodParameterParser`.
- Produces: `ClassMethodService.AddMethod(string workspaceRoot, string className, string methodName, string returnType, IReadOnlyList<ParameterDefinition> parameters, out string message)`; the old 5-argument overload is removed and its one caller updated.

- [ ] **Step 1: Write the failing tests** (each runs through `CommandFront.Run` and reads `Order.cs`):
  - `add method ApplyDiscount(decimal percent) returns decimal to Order` → `public decimal ApplyDiscount(decimal percent)`; project compiles.
  - `add method Ship with parameters address string, express bool to Order` → `public void Ship(string address, bool express)`.
  - `add method Ship taking address string to Order` → `public void Ship(string address)`.
  - `add method Assign(Customer customer) to Order` → written; `add method Assign(Ghost ghost) to Order` → false, message names `Ghost`, `Order.cs` unchanged.
  - `add method Bad(int a, int a) to Order` → false, message contains `'a' is repeated`, file unchanged.
  - `add method Scale(decimal factor) to IShape` → output names `Square` and `Scale(decimal)`.
  - `add method Save to Order` → unchanged behaviour: `public void Save()`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~AddMethodParameterTests` — expect failures.
- [ ] **Step 3: Implement.** `AddMethodRe` gets an optional `\((?<cparams>[^)]*)\)` after the name and an optional `\s+{EnglishLead}\s+(?<eparams>.+?)` before ` to <Class>`; the match block parses them into `ParsedCommand.Parameters` and, on a parse error, returns a `CreateMethod` command whose handler refuses with that error (put the error in `ParsedCommand.Metadata["ParameterError"]`). `AddMethod` binds each parameter type with `TypeResolution.Resolves`, as it binds the return type, before writing.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~Method|FullyQualifiedName~Interface|FullyQualifiedName~Bridge"` — expect PASS.
- [ ] **Step 5: Commit** `feat(methods): add method takes parameters`.

### Task 4: Help, docs, full gate

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs` — the `add method` entry
- Regenerate: `docs/commands.md`, the manual appendix

- [ ] **Step 1:** Change the `add method` description to add: "Give parameters as 'add method <name>(<type> <name>, ...) to <class name>' or '... with parameters <name> <type>, ... to <class name>'." Example: `add method ApplyDiscount(decimal percent) returns decimal to Order`.
- [ ] **Step 2:** `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests --filter FullyQualifiedName~CommandReferenceDocTests` and `UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests --filter FullyQualifiedName~ManualAppendixDocTests`; check `git diff docs/` shows only the `add method` row.
- [ ] **Step 3:** CI build command (Global Constraints) — expect 0 warnings, 0 errors; then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build` — expect all pass.
- [ ] **Step 4:** Live check: rebuild `CodeGenerator`, pipe the Task 2 and Task 3 commands into it with `CG_NO_WEB=1` in a fresh project under `C:\cgmp`, and `dotnet build` the result — expect success. Kill only processes by PID.
- [ ] **Step 5: Commit** `docs(methods): help and command reference show method parameters`.
