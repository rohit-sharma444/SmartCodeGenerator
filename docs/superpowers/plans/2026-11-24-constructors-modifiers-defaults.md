# Constructors, Modifiers and Defaults Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `add constructor`, property default values and the `readonly`/`private`/`static`/`required` modifiers in `create class` and `add property`, with no word the user typed dropped silently.

**Architecture:** One engine helper checks a default value against its type in the class's project and one rule check refuses C#-illegal modifier sets; both are used by the `create class` path (`CommandEntityExtractor` → `GenerateClass`) and the `add property` path (`AddPropertyRe` → `ClassPropertyService`). `add constructor` is a new intent with a Roslyn `ConstructorService`.

**Tech Stack:** .NET 10, C#, Roslyn, MSTest.

**Spec:** `docs/superpowers/specs/2026-11-24-constructors-modifiers-defaults-design.md` (read its "Rules settled after the builder check" section — it is binding).

## Global Constraints

- CI: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` — 0 warnings, 0 errors.
- Per-task gate: build + the task's focused `--filter`. The full `CodeGenerator.Tests` suite only in Task 5, one suite at a time.
- Edit files with the Edit tool, not shell heredocs.
- "create class X and add method Y" stays ONE command (`CommandFront.TwoCommands`).
- A new regex guard fills its fields from its own capture groups on the raw text, and its intent goes in `CommandFront.GuardedIntents` (`ReachableIntentGateTests`).
- A refusal writes nothing. A created or edited file must compile in its project; the success message is printed only after the write.
- No word typed after a property's type in a `create class` list is dropped without a message (spec goal 4).
- `required` is the C# keyword, never `[Required]`, on the typed path.
- Kill processes by PID only. Never `git stash`.

## Review Focus

1. `create class Line with properties Qty int default 1 and Price decimal` — the `and` after the default still splits the list; `Price` is kept (Task 2 test).
2. `create class Paint with properties Color string default "Black and White"` — the quoted `and` does not split (Task 2 test).
3. `add property Count int = 5 to Order` when another class is the current class — the property goes to `Order`, not the current class (Task 3 test).
4. `add constructor to Order` when Order has a `required` property and a static property — the static one is not a parameter, and `[SetsRequiredMembers]` is written (Task 4 test).
5. `create class Customer with properties Name string required min length 3` — `required` is the keyword and `min length 3` is reported with the `add validation` pointer (Task 2 test).

---

### Task 1: Default values and modifier rules (engine helpers)

**Files:**
- Modify: `CodeGenerationEngine/Models/PropertyDefinition.cs` (`Modifiers`)
- Create: `CodeGenerationEngine/Models/PropertyModifiers.cs`
- Create: `CodeGenerationEngine/Core/DefaultValues.cs`
- Create: `CodeGenerationEngine/Core/PropertyModifierRules.cs`
- Test: `CodeGenerator.Tests/Core/DefaultValuesTests.cs`

**Interfaces:**
- Produces: `[Flags] public enum PropertyModifiers { None = 0, ReadOnly = 1, Private = 2, Static = 4, Required = 8 }`; `PropertyDefinition.Modifiers` (default `None`). `Initializer` (exists) carries the checked default expression.
- Produces: `public static bool DefaultValues.TryExpression(string type, string rawValue, string? projectFolder, out string expression, out string? error)` — a `string`/`string?` type and an unquoted value → quoted with C# escaping; quoted values kept; then `T x = <expression>;` compiled in a probe class beside the project's own files (with its nullable setting and implicit usings, as `TypeResolution` compiles), or alone when `projectFolder` is null. Error text: `"'{rawValue}' is not a valid {type} value."`.
- Produces: `public static string? PropertyModifierRules.Refusal(PropertyModifiers modifiers, string propertyName)` — null when allowed; `"'{name}' cannot be both required and static."`, `"'{name}' cannot be both required and private."` (C# CS9032/CS9031 territory).
- Produces: `public static string PropertyModifierRules.Accessors(PropertyModifiers m)` → `"{ get; init; }"` for ReadOnly without Static, `"{ get; }"` for ReadOnly with Static, else `"{ get; set; }"`; and `public static string PropertyModifierRules.Prefix(PropertyModifiers m)` → `"private "`/`"public "` + `"static "` + `"required "` in C# order.

- [ ] **Step 1: Write the failing tests** — `[DataRow]`s of `TryExpression` with `projectFolder: null`: `("int","1")`→`1`; `("string","New")`→`"New"`; `("string","\"New\"")`→`"New"`; `("bool","true")`→`true`; `("decimal","9.5")`→`9.5m` or a form that compiles (assert it compiles and keeps the value); `("int","abc")` → false, `"'abc' is not a valid int value."`; `("string","a\"b")` → escaped and compiles. Refusal rows for the two pairs; `Accessors`/`Prefix` rows for each combination used in Tasks 2-3.
- [ ] **Step 2: Run** `dotnet test CodeGenerator.Tests --filter FullyQualifiedName~DefaultValuesTests` — expect compile failure.
- [ ] **Step 3: Implement** as specified. A `decimal` literal without `m` does not compile from a double literal: append `m` for a decimal type when the raw value is a plain number, `f` for float.
- [ ] **Step 4: Run** the filter — expect PASS.
- [ ] **Step 5: Commit** `feat(properties): check default values and modifier rules`.

### Task 2: Defaults and modifiers in a create class list

**Files:**
- Modify: `NaturalLanguageBridge/CommandEntityExtractor.cs` (`MemberListText` shields quoted text; `TypedEntries`/`ExtractProperties` read the words after the type)
- Modify: `CodeGenerationEngine/Core/CodeGenerator.cs` (`GenerateClass` writes `Modifiers` and `Initializer`)
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` (`HandleCreateClass`: check each default with `DefaultValues.TryExpression` against the target project, refuse modifier pairs and a default on a struct property, before generating; report ignored words after the write)
- Test: `CodeGenerator.Tests/Core/PropertyListModifierTests.cs` (Shop workspace fixture as in `TypeKindCreateTests`)

**Interfaces:**
- Consumes: Task 1.
- Produces: `ParsedCommand.IgnoredWords: List<(string Property, string Words)>` (reset in `Clear()`), printed as `Ignored '{Words}' after {Property}` plus, when the words are validation words, `: use add validation ... on {Property} in {Class}`.
- The raw default text travels on `PropertyDefinition.Initializer` from the bridge; the handler replaces it with the checked expression or refuses.

- [ ] **Step 1: Write the failing tests** (each reads `<Class>.cs`, asserts the line, and that the project compiles):
  - `create class Line with properties Qty int default 1 and Price decimal` → `public int Qty { get; set; } = 1;` and `public decimal Price` (Review Focus 1)
  - `... Qty int = 1 ...` → same
  - `create class Order with properties Status string default New` → `= "New";`
  - `create class Paint with properties Color string default "Black and White"` → `= "Black and White";`, one property (Review Focus 2)
  - `create class Customer with properties Name string required and Email string` → `public required string Name { get; set; }`
  - `create class Customer with properties Name string required min length 3` → `required` written; output contains `Ignored 'min length 3' after Name` and `add validation` (Review Focus 5)
  - `create class Order with properties Id int readonly and Secret string private and Max int static default 10` → `public int Id { get; init; }`, `private string Secret { get; set; }`, `public static int Max { get; set; } = 10;`
  - `create class Line with properties Qty int default abc` → refused, `"'abc' is not a valid int value."`, no file
  - `create class Order with properties Code string required static` → refused, `"cannot be both required and static"`, no file
  - `create struct Point with properties X int default 1` → refused, `"add constructor to Point"`, no file
  - `create class City with properties Name string default New York` → `= "New";` and `Ignored 'York' after Name`
  - `create class Employee and add method AddEmployee` → unchanged (one command, method written)
- [ ] **Step 2: Run** `--filter FullyQualifiedName~PropertyListModifierTests` — expect failures.
- [ ] **Step 3: Implement.** Read each entry as `<name> <type> [modifier|default <value>|= <value>]*` after the existing `TypedEntry` match; the rest of the entry, if any, goes to `IgnoredWords`. Quoted text is shielded in `MemberListText` before the list is split.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~MemberList|FullyQualifiedName~CreateClass|FullyQualifiedName~TypeKind|FullyQualifiedName~Bridge"` — expect PASS.
- [ ] **Step 5: Commit** `feat(properties): defaults and modifiers in a create class property list`.

### Task 3: Defaults and modifiers in add property

**Files:**
- Modify: `NaturalLanguageBridge/CommandEntityExtractor.cs` (`AddPropertyRe`: optional modifiers before `property`, optional `default <value>` / `= <value>` before ` to <Class>`)
- Modify: `NaturalLanguageBridge/CommandFront.cs` (`AddPropertyStartRe` accepts the modifier words)
- Modify: `CodeGenerationEngine/Models/ParsedCommand.cs` (`PropertyModifiers PropertyModifiers`, `string? PropertyDefault`; reset in `Clear()`)
- Modify: `CodeGenerationEngine/Core/ClassPropertyService.cs` (`AddProperty` overload with modifiers and default)
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` (`HandleCreateProperty` passes them)
- Test: `CodeGenerator.Tests/Core/AddPropertyModifierTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `ClassPropertyService.AddProperty(string workspaceRoot, string className, string propertyName, string propertyType, PropertyModifiers modifiers, string? defaultValue, out string message)`; the 5-argument overload forwards with `None, null`.
- Rules: the type's own kind still applies (a static class → `static`; a record → `init` unless the command adds nothing — keep today's behaviour and OR in the command's modifiers); `PropertyModifierRules.Refusal` first; a default on a struct property refused as in Task 2; `DefaultValues.TryExpression` with the class's project folder.

- [ ] **Step 1: Write the failing tests** (fixture with `Order.cs`, `Point.cs` struct, and `Line.cs`; set the current class to `Line` before the run in the Review Focus 3 test):
  - `add property Count int = 5 to Order` → `public int Count { get; set; } = 5;` in Order.cs, Line.cs unchanged (Review Focus 3)
  - `add property Status string default "New" to Order` → `= "New";`
  - `add property Status string default New to Order` → `= "New";`
  - `add readonly property Id int to Order` → `{ get; init; }`
  - `add private property Secret string to Order` → `private string Secret`
  - `add static property Max int default 10 to Order` → `public static int Max { get; set; } = 10;`
  - `add required property Code string to Order` → `public required string Code { get; set; }`
  - `add required readonly property Code2 string to Order` → `public required string Code2 { get; init; }`
  - `add property Count2 int default abc to Order` → refused, `"'abc' is not a valid int value."`, unchanged
  - `add required static property X int to Order` → refused, unchanged
  - `add property X int default 1 to Point` → refused, `"add constructor to Point"`, unchanged
  - `add property Title string to Order` → unchanged behaviour
- [ ] **Step 2: Run** `--filter FullyQualifiedName~AddPropertyModifierTests` — expect failures.
- [ ] **Step 3: Implement** as specified; the success message adds ` (default {expression})` and the modifiers when present.
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~Property|FullyQualifiedName~Bridge|FullyQualifiedName~TypeKind"` — expect PASS.
- [ ] **Step 5: Commit** `feat(properties): add property takes a default value and modifiers`.

### Task 4: add constructor

**Files:**
- Modify: `CodeGenerationEngine/Models/IntentType.cs` (`AddConstructor`)
- Modify: `CodeGenerationEngine/Models/ParsedCommand.cs` (`List<string> ConstructorProperties`, reset in `Clear()`)
- Modify: `NaturalLanguageBridge/CommandFront.cs` (`AddConstructorRe`, `GuardedIntents`)
- Create: `CodeGenerationEngine/Core/ConstructorService.cs`
- Modify: `CommandHandlingService.cs` (handler), `CommandDictionary.cs` (entry), `ActionFormatter.cs` if it lists intents
- Test: `CodeGenerator.Tests/Core/AddConstructorTests.cs`

**Interfaces:**
- Produces: `public static bool ConstructorService.AddConstructor(string workspaceRoot, string className, IReadOnlyList<string> propertyNames, out string message, out IReadOnlyList<string> notes)` — loads the parts with `ClassMethodEditService.TryLoadParts`; empty `propertyNames` = the spec's default set; writes via `ClassMemberEditor.TryAddMember`; compiles the edited project file set before writing (refuse with the compiler text otherwise).
- Command shape: `add|create [a|an|new] constructor [(with|taking) <P1>(, <P2>| and <P2>)*] (to|for|in) [class] <Class>`.

- [ ] **Step 1: Write the failing tests** (fixture: `Order.cs` = `public class Order { public decimal Total { get; set; } public string Name { get; set; } = string.Empty; public static int Count { get; set; } private string Secret { get; set; } = ""; public decimal Tax => Total * 0.2m; }`):
  - `add constructor to Order` → `public Order(decimal total, string name)` with `Total = total;` and `Name = name;`; no `Count`, `Secret`, `Tax`; compiles; message `"Constructor Order(decimal, string) added to 'Order'."`
  - `add constructor with Name to Order` → `public Order(string name)`
  - `add constructor with Name and Total to Order` → parameters in that order
  - `add constructor with Secret to Order` → `public Order(string secret)` (named private is allowed)
  - `add constructor for Order` → same as the first
  - empty class `public class Empty { }` + `add constructor to Empty` → `public Empty()`
  - `add constructor with Ghost to Order` → refused `"'Ghost' is not a property of 'Order'"`, unchanged
  - twice `add constructor to Order` → second refused (same parameter types), unchanged
  - `Account.cs` with `public required string Id { get; set; }` and `public decimal Balance { get; set; }`: `add constructor to Account` → `[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]` above it; compiles. `add constructor with Balance to Account` → no attribute; output notes `Id` is required and not set by it (Review Focus 4)
  - parameter named like a keyword: property `Class` → parameter `@class`; compiles
  - refused, unchanged: a static class, an enum (`"is an enum"` message as the type-kinds feature gives), an interface, `public record Money(decimal Amount);` (`"positional record"`)
  - a record `public record Point { public int X { get; init; } }` and a struct → constructor written, compiles
  - `ReachableIntentGateTests` passes; `create class X and add method Y` still one command
- [ ] **Step 2: Run** `--filter FullyQualifiedName~AddConstructorTests` — expect failures.
- [ ] **Step 3: Implement.** Parameter name: camelCase of the property name, `@` prefix for a C# keyword. Dictionary entry: Category as `add method`'s, Command `add constructor [with <property>, ...] to <class name>`, Example `add constructor with Total and Name to Order` (fits 80 columns with the help gutter — check `CommandHelpRenderingTests`).
- [ ] **Step 4: Run** the filter, then `--filter "FullyQualifiedName~ReachableIntent|FullyQualifiedName~Bridge|FullyQualifiedName~CommandHelpRendering|FullyQualifiedName~Method"` — expect PASS.
- [ ] **Step 5: Commit** `feat(constructors): add constructor to a class`.

### Task 5: Docs, full gate, live check

- [ ] **Step 1:** Update the `add property` and `create class` dictionary descriptions with one sentence each on defaults/modifiers (examples within 80 columns); `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests --filter FullyQualifiedName~CommandReferenceDocTests` and `UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests --filter FullyQualifiedName~ManualAppendixDocTests`; `CommandHelpRenderingTests` passes.
- [ ] **Step 2:** CI build (Global Constraints) — 0 warnings, 0 errors.
- [ ] **Step 3:** Full `dotnet test CodeGenerator.Tests --no-build` — all pass.
- [ ] **Step 4:** Live check: rebuild `CodeGenerator`; in a fresh classlib under a new folder, pipe (`PROJECTPATH`, `CG_NO_WEB=1`) the Review Focus commands plus `add constructor to Order`, `add readonly property Id int to Order`; `dotnet build` the project — 0 errors.
- [ ] **Step 5: Commit** `docs(properties): help shows defaults, modifiers and add constructor`.
