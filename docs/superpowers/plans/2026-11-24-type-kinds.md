# Type Kinds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The CLI creates records, structs, abstract/static/sealed classes and enums, edits enum values, and keeps later member edits right for each kind.

**Architecture:** Kind words are read in `CommandFront` ahead of `CreateClassRe`: the sentence is re-parsed as `create class ...` and the result carries `Kind` and `Modifier`, so every existing create-class phrasing works for each kind. `CodeGenerationEngine.GenerateClass` writes the kind; `NewClassCompletion` checks it with the project and adds stubs. The enum has its own intents (`CreateEnum`, `AddEnumValue`, `RemoveEnumValue`) and an `EnumValueService`.

**Tech Stack:** .NET 10, C#, Roslyn, MSTest.

**Spec:** `docs/superpowers/specs/2026-11-24-type-kinds-design.md`

## Global Constraints

- CI: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` — 0 warnings, 0 errors.
- Per-task gate: build + the task's focused `--filter`. The full `CodeGenerator.Tests` suite only in Task 7, one suite at a time.
- Edit files with the Edit tool, not shell heredocs (they corrupt backslashes and line ends).
- "create class X and add method Y" stays ONE command (`CommandFront.TwoCommands`).
- A new regex guard fills its fields from its own capture groups on the raw text (Normalize rewrites model/entity/object → class), and its intent goes in `CommandFront.GuardedIntents` (`ReachableIntentGateTests` reads it).
- A refusal writes nothing. Every created type is compiled with its project before it is saved.
- Kill processes by PID only, never by image name.

## Review Focus

1. `create class Record` and `create class Struct` make a class named Record / Struct — the kind word is read only before the name (Task 1 test).
2. `create record money with properties Amount decimal and methods Total returns decimal` — lower-case name PascalCased, members and methods both kept (Task 2 test).
3. `create enum Status` with no values writes `public enum Status { }` and compiles (Task 4 test).
4. `create enum Status with values Active, active` and `... with values Active, class` are refused by the value's name, nothing written (Task 4 test).
5. `create struct Point inherits Shape` and `create static class Helpers implements IFoo` are refused with the C# rule, nothing written (Task 2 test).

---

### Task 1: Kind words in the parse

**Files:**
- Create: `CodeGenerationEngine/Models/TypeKinds.cs`
- Modify: `CodeGenerationEngine/Models/ParsedCommand.cs` (new properties; reset in `Clear()`)
- Modify: `NaturalLanguageBridge/CommandFront.cs` (new `TypeKindRe` ahead of `CreateClassRe`; `CreatesAType`)
- Test: `CodeGenerator.Tests/Bridge/TypeKindParseTests.cs`

**Interfaces:**
- Produces, in `TypeKinds.cs`: `public enum DeclaredKind { Class, Record, Struct, Enum }` and `public enum TypeModifier { None, Abstract, Sealed, Static }`.
- Produces, on `ParsedCommand`: `public DeclaredKind Kind { get; set; } = DeclaredKind.Class;`, `public TypeModifier Modifier { get; set; } = TypeModifier.None;`, `public const string KindErrorKey = "KindError";` (the refusal text, read by the handler in Task 2).

- [ ] **Step 1: Write the failing tests** — `[DataRow]`s through `CommandFront.Parse(sentence, workspace)`, asserting `Intent`, `ClassName`, `Kind`, `Modifier`, and that `Metadata` has no `KindErrorKey`:
  - `create record Money with properties Amount decimal and Currency string` → CreateClass, `Money`, Record, None, 2 properties
  - `create struct Point with properties X int and Y int` → Struct, None
  - `create abstract class Shape with methods Area returns decimal` → Class, Abstract, 1 method
  - `create static class Helpers` → Class, Static; `create sealed class Token` → Class, Sealed
  - `create abstract record Entity`, `create sealed record Money` → Record, Abstract / Sealed
  - `create a record money` → `Money`
  - `create record Money implements IEquatable` → `Interfaces` = `[IEquatable]`
  - `create class Record` and `create class Struct` → Class, None, `ClassName` `Record` / `Struct`
  - refused pairs, each → CreateClass with `Metadata[KindErrorKey]` containing the quoted rule: `create static struct P` → `"A struct cannot be static."`, `create sealed abstract class S` → `"A class cannot be both sealed and abstract."`, `create static record R` → `"A record cannot be static."`, `create abstract struct P` → `"A struct cannot be abstract."`
  - `create record Money and add method Total to Money` → one command (`TwoCommands` returns null).
- [ ] **Step 2: Run** `dotnet test CodeGenerator.Tests --filter FullyQualifiedName~TypeKindParseTests` — expect compile failure.
- [ ] **Step 3: Implement.** `TypeKindRe`: `^(?<verb>create|add|generate)\s+(?:(?:a|an|new)\s+)?(?<mods>(?:(?:abstract|sealed|static)\s+)*)(?<kind>class|record|struct)\s+(?<rest>.+)$`, matched only when `mods` is non-empty or `kind` is not `class`. Re-parse `$"{verb} class {rest}"` with the same `Parse`; when the result is `CreateClass`, set `Kind`/`Modifier` (two modifier words → the error key), and return it. `CreatesAType` also matches the kind words and `enum`.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~Bridge|FullyQualifiedName~MemberList|FullyQualifiedName~CreateClass"` — expect PASS.
- [ ] **Step 5: Commit** `feat(types): read record, struct and abstract/static/sealed in create commands`.

### Task 2: Write each kind, and refuse what C# forbids

**Files:**
- Modify: `CodeGenerationEngine/Core/CodeGenerator.cs` (`GenerateClass`)
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` (`HandleCreateClass`)
- Modify: `CodeGenerationEngine/Core/NewClassCompletion.cs` (`Probe` and `Compile` find a `TypeDeclarationSyntax`, not only a class)
- Test: `CodeGenerator.Tests/Core/TypeKindCreateTests.cs` (workspace with `Shop/Shop.csproj`, as `AddMethodParameterTests` sets up; each test runs through `CommandFront.Run` and compiles the project's files)

**Interfaces:**
- Consumes: `ParsedCommand.Kind`, `Modifier`, `KindErrorKey` (Task 1).

- [ ] **Step 1: Write the failing tests** (each reads the written file and asserts the project compiles):
  - `create record Money with properties Amount decimal and Currency string` → contains `public record Money` and `public decimal Amount { get; init; }`
  - `create struct Point with properties X int and Y int` → `public struct Point`
  - `create abstract class Shape with properties Name string and methods Area returns decimal` → `public abstract class Shape`, `public abstract decimal Area();`, `public string Name { get; set; }`
  - `create static class Helpers with properties Count int and methods Format returns string` → `public static class Helpers`, `public static int Count`, `public static string Format()`
  - `create sealed class Token` → `public sealed class Token`; `create sealed record Money` → `public sealed record Money`
  - `create record money with properties Amount decimal and methods Total returns decimal` → file `Money.cs`, both members (Review Focus 2)
  - refusals, each → `Run` false, the quoted text in the output, no new file: `create struct Point inherits Shape` (with `Shape.cs` present) → `"A struct cannot inherit from a class"`; `create static class Helpers implements IShape` → `"A static class cannot implement an interface"`; `create static class Helpers inherits Shape` → `"A static class cannot inherit"`; `create static struct P` → `"A struct cannot be static."`
- [ ] **Step 2: Run** `--filter FullyQualifiedName~TypeKindCreateTests` — expect failures.
- [ ] **Step 3: Implement.** `GenerateClass` writes `public [abstract|sealed|static ]class|record|struct Name`; a record property is `{ get; init; }`; an abstract class's listed methods are `public abstract T M(...);`; a static class's members get `static`. `HandleCreateClass` refuses, before generating, on `KindErrorKey` and on the inherit/implement rules in the spec's table. `NewClassCompletion.Compile` finds the declaration as `TypeDeclarationSyntax` by name.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~CreateClass|FullyQualifiedName~NewClassCompletion|FullyQualifiedName~MemberList"` — expect PASS.
- [ ] **Step 5: Commit** `feat(types): create record, struct and abstract/static/sealed classes`.

### Task 3: Stubs for abstract members

**Files:**
- Modify: `CodeGenerationEngine/Core/NewClassCompletion.cs` (`Complete`, `Stub`, `Report`)
- Test: `CodeGenerator.Tests/Core/TypeKindCreateTests.cs`

**Interfaces:**
- Consumes: Task 2's `TypeDeclarationSyntax` probe.

- [ ] **Step 1: Write the failing tests:**
  - with `Shape.cs` = `public abstract class Shape { public abstract decimal Area(); public abstract string Label { get; } }`: `create class Circle inherits Shape` → `public override decimal Area() => throw new System.NotImplementedException();` and `public override string Label { get; }`-compatible override; project compiles; output names `Area` and `Label`.
  - `create class Circle inherits Shape implements IShape` (IShape declares `decimal Scale(decimal factor);`) → both the override and the interface stub; compiles.
  - with `IShape.cs` as above: `create abstract class Base implements IShape` → `public abstract decimal Scale(decimal factor);`; compiles.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~TypeKindCreateTests` — expect the new tests to fail.
- [ ] **Step 3: Implement.** For a non-abstract type, walk `BaseType` while it is abstract and stub each abstract member it does not override (`override` instead of the interface form; a get-only property overrides as `{ get; }` with a throw, or an auto-property where C# allows it). For an abstract type, each missing interface member is written `public abstract ...;`. `Report` names base-class members as it names interface members.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~NewClassCompletion|FullyQualifiedName~Interface"` — expect PASS.
- [ ] **Step 5: Commit** `feat(types): a class that inherits an abstract class gets its abstract members`.

### Task 4: create enum

**Files:**
- Modify: `CodeGenerationEngine/Models/IntentType.cs` (`CreateEnum`)
- Modify: `CodeGenerationEngine/Models/ParsedCommand.cs` (`public List<string> EnumValues { get; set; } = [];`, reset in `Clear()`)
- Create: `NaturalLanguageBridge/EnumValueParser.cs`
- Modify: `NaturalLanguageBridge/CommandFront.cs` (`CreateEnumRe`, `GuardedIntents`)
- Modify: `CodeGenerationEngine/Core/CodeGenerator.cs` (`GenerateEnum`), `NaturalLanguageCodeEngine.cs` (route `CreateEnum`), `CommandHandlingService.cs` (`HandleCreateEnum`, `GenerateAndSave` checks an enum too), `NewClassCompletion.cs` (probe finds an `EnumDeclarationSyntax`)
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs` (entry)
- Test: `CodeGenerator.Tests/Core/EnumCommandTests.cs` (same workspace fixture)

**Interfaces:**
- Produces: `public static bool EnumValueParser.TryParse(string text, out List<string> values, out string? error)` — splits on commas and `and`; each value an identifier, not a keyword, not repeated (case-insensitive); empty text → true, empty list. Errors: `"'{v}' is not a valid enum value name."`, `"The value '{v}' is repeated."`.
- Produces: `IntentType.CreateEnum`; a parse error is carried in `Metadata[ParsedCommand.KindErrorKey]` and refused by `HandleCreateEnum`.

- [ ] **Step 1: Write the failing tests:**
  - `create enum Status with values Active, Closed, Pending` → `Status.cs` contains `public enum Status` and `Active,`/`Closed,`/`Pending` in order; compiles.
  - `with`, `with members`, `having`, `and` as the last separator (`Active, Closed and Pending`) → the same three values.
  - `create enum Status` → `public enum Status` with no values; compiles (Review Focus 3).
  - `create enum status with values active` → `Status.cs`, value `active` kept as typed.
  - refused, no file: `... with values Active, active` → `"'active' is repeated"` (case as in the error), `... with values Active, class` → `"'class' is not a valid enum value name"` (Review Focus 4); `create enum Status inherits Base` → `"An enum cannot inherit"`; existing `Status.cs` → the existing "already exists" refusal.
  - `ReachableIntentGateTests` still passes.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~EnumCommandTests` — expect failures.
- [ ] **Step 3: Implement.** `CreateEnumRe`: `^(?:create|add|generate)\s+(?:(?:a|an|new)\s+)?enum\s+(?<name>[A-Za-z_]\w*)(?:\s+(?:with\s+(?:values|members)|with|having)\s+(?<values>.+?))?\s*[.!]?$` plus an `inherits|implements|extends` branch that sets the refusal. `HandleCreateEnum` reuses the existing-file refusal (`ExistingTypeFile`) and `GenerateAndSave`. Dictionary entry: Category `"Classes"`, Command `"create enum <name> with values <value>, ..."`, Example `"create enum Status with values Active, Closed, Pending"`.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~ReachableIntent|FullyQualifiedName~Bridge|FullyQualifiedName~CommandDictionary"` — expect PASS.
- [ ] **Step 5: Commit** `feat(types): create enum with values`.

### Task 5: add value / remove value, and enum refusals in member edits

**Files:**
- Modify: `IntentType.cs` (`AddEnumValue`, `RemoveEnumValue`)
- Create: `CodeGenerationEngine/Core/EnumValueService.cs`
- Modify: `CommandFront.cs` (`EnumValueRe`, `GuardedIntents`), `CommandHandlingService.cs` (two handlers), `CommandDictionary.cs` (two entries)
- Modify: `CodeGenerationEngine/Core/ClassMethodEditService.cs` (`TryLoad` not-found message)
- Test: `CodeGenerator.Tests/Core/EnumCommandTests.cs`

**Interfaces:**
- Produces: `public static bool EnumValueService.AddValue(string workspaceRoot, string enumName, string value, out string message)` and `public static bool EnumValueService.RemoveValue(string workspaceRoot, string enumName, string value, out string message)`. The enum is found with `ClassFiles.TryFind` then an `EnumDeclarationSyntax` by name (case-insensitive, exact first); written with `ClassMethodEditService.TryWrite`.
- Command shape: `add value <V> to <Enum>`, `remove|delete value <V> from <Enum>`; fields from the regex groups. The value is kept as typed; the enum name is matched as `add method` matches a class.

- [ ] **Step 1: Write the failing tests** (fixture `Status.cs` = `public enum Status { Active, Closed, Pending }`):
  - `add value Archived to Status` → `Pending,` then `Archived`; compiles; message `"Value 'Archived' added to 'Status'."`
  - `remove value Pending from Status` → no `Pending`; compiles.
  - with `Order.cs` using `Status.Pending`: `remove value Pending from Status` → removed, and the output names `Order.cs` (the `WarnOfUsers` warning, as `remove property` gives).
  - explicit numbers: `public enum Level { Low = 1, High = 5 }` + `add value Top to Level` → `Top = 6`.
  - `[Flags] public enum Access { Read = 1, Write = 2 }` + `add value Run to Access` → refused, `"is a [Flags] enum"`, file unchanged.
  - refused, file unchanged: `add value Active to Status` → `"already"`; `add value class to Status` → `"'class' is not a valid enum value name"`; `remove value Ghost from Status` → `"'Ghost' is not a value of 'Status'"`; `add value X to Ghost` → `"Enum 'Ghost' not found."`; `add value X to Order` (a class) → `"'Order' is not an enum"`.
  - `add property Name string to Status` and `add method Save to Status` → refused with `"'Status' is an enum; use add value <name> to Status."`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~EnumCommandTests` — expect the new tests to fail.
- [ ] **Step 3: Implement.** Value checks reuse `EnumValueParser` for one value. `RemoveValue` removes the member and its separator; the handler then calls `WarnOfUsers(ctx, value, ...)` as `HandleDeleteProperty` does — extend `MemberReferences` to an enum member if it finds only properties and methods. `TryLoad`'s not-found path looks for an `EnumDeclarationSyntax` of the name and gives the enum message.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~ReachableIntent|FullyQualifiedName~Method|FullyQualifiedName~Property|FullyQualifiedName~MemberReferences"` — expect PASS.
- [ ] **Step 5: Commit** `feat(types): add value and remove value on an enum`.

### Task 6: Member edits that follow the kind

**Files:**
- Modify: `CodeGenerationEngine/Core/ClassPropertyService.cs` (setter, `static`)
- Modify: `CodeGenerationEngine/Core/ClassMethodService.cs` (`AddMethod`: `static`; abstract)
- Modify: `NaturalLanguageBridge/CommandFront.cs` (`AddMethodRe` takes an optional `abstract` before `method`; sets `Modifier = TypeModifier.Abstract` on the `CreateMethod` command)
- Modify: `CommandHandlingService.cs` (`HandleCreateMethod` passes it), `CommandDictionary.cs` (`add method` description: one sentence on `add abstract method`)
- Test: `CodeGenerator.Tests/Core/TypeKindEditTests.cs`

**Interfaces:**
- Changes: `ClassMethodService.AddMethod(string workspaceRoot, string className, string methodName, string returnType, IReadOnlyList<ParameterDefinition> parameters, bool isAbstract, out string message)`; update its one caller.

- [ ] **Step 1: Write the failing tests** (fixture files: `public record Money { }`, `public static class Helpers { }`, `public abstract class Shape { }`, `public class Order { }`; each asserts the project compiles after a success):
  - `add property Amount decimal to Money` → `{ get; init; }`
  - `add property Count int to Helpers` → `public static int Count { get; set; }`; `add method Format returns string to Helpers` → `public static string Format()`
  - `add method Describe returns string to Shape` → a method with a body (not abstract)
  - `add abstract method Area returns decimal to Shape` → `public abstract decimal Area();`
  - `add abstract method Area(decimal scale) returns decimal to Shape` → `public abstract decimal Area(decimal scale);`
  - `add abstract method Area to Order` → refused, `"'Order' is not abstract"`, file unchanged.
  - `add method Save to Order` → unchanged: `public void Save()` with a body.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~TypeKindEditTests` — expect failures.
- [ ] **Step 3: Implement.** The setter is `init` for a record or a readonly type, `set` otherwise; `static` when the declaration has the `static` modifier. `isAbstract` writes `public abstract T M(...);` and needs the `abstract` modifier on the type.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~Method|FullyQualifiedName~Property|FullyQualifiedName~Bridge"` — expect PASS.
- [ ] **Step 5: Commit** `feat(types): member edits follow the kind of the type`.

### Task 7: Docs, full gate, live check

**Files:**
- Regenerate: `docs/commands.md`, the manual appendix

- [ ] **Step 1:** `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests --filter FullyQualifiedName~CommandReferenceDocTests` and `UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests --filter FullyQualifiedName~ManualAppendixDocTests`; `git diff docs/` shows only the new and changed command rows.
- [ ] **Step 2:** CI build (Global Constraints) — 0 warnings, 0 errors.
- [ ] **Step 3:** Full `dotnet test CodeGenerator.Tests --no-build` and `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build`, one at a time — all pass.
- [ ] **Step 4:** Live check: rebuild `CodeGenerator`; in a fresh classlib under a new folder (`C:\cgtk2`), pipe with `PROJECTPATH` and `CG_NO_WEB=1` the Review Focus commands plus `create abstract class Shape with methods Area returns decimal`, `create class Circle inherits Shape`, `create enum Status with values Active, Closed`, `add value Archived to Status`, `remove value Closed from Status`; then `dotnet build` the project — 0 errors.
- [ ] **Step 5: Commit** `docs(types): help and command reference show the type kinds`.
