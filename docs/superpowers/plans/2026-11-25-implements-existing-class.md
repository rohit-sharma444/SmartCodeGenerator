# Implements on an Existing Class Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Order implements IAuditable, IExportable` adds the interfaces to an existing class, struct or record and stubs the members it now lacks, so the project still compiles.

**Architecture:** The stub writer inside `NewClassCompletion` moves to a shared `MemberStubs` helper. `ClassInheritanceService` takes a list of types, accepts structs and records, and after adding the base types inserts the stubs `MemberStubs` returns. `CommandFront.ClassInheritRe` learns `implements` and a list of (possibly generic) names.

**Tech Stack:** C# / .NET, Roslyn (Microsoft.CodeAnalysis.CSharp), MSTest.

**Spec:** `docs/superpowers/specs/2026-11-25-implements-existing-class-design.md`

## Global Constraints

- Edits are text insertions at fixed places; never reformat the file (no `NormalizeWhitespace`). Write through `Validation.SourceFileText.Rewrite` as today.
- Stub shapes are exactly `NewClassCompletion`'s: property `public T Name { get; set; }`, method `public R Name(...) => throw new System.NotImplementedException();`, abstract type → `public abstract ...;`, abstract base member → `override` with the member's accessibility.
- A list is all-or-nothing: any refused name → no write, message names that name.
- `implements <class>` is refused with a hint to use `inherits`.
- Every edited file in a test is checked by compiling it (`Compiler.Compile`), not by substring alone.
- Check any new multi-word phrase against `CommandFront.TwoCommands`.
- Any agent that runs the CLI exe sets `PROJECTPATH` to a scratch folder. Kill processes by PID only.
- Per-task gate: build + focused test filter. Full suite once, at the end.

## Review Focus

1. A positional record with no body (`public record Order(int Id);`) — stubs need a `{ }` body created; expected: compiles.
2. A member already implemented in another `partial` file of the class — no duplicate stub (CS0111).
3. A generic interface (`IComparable<Order>`) — stub `public int CompareTo(Order? other)` compiles.
4. A list where the second name does not exist — file unchanged, message names it.
5. A file with CRLF line endings and the class nested in a block-scoped namespace — stubs use CRLF and the indentation of the type's existing members.

---

### Task 1: Extract `MemberStubs` from `NewClassCompletion`

**Files:**
- Create: `CodeGenerationEngine/Core/MemberStubs.cs`
- Modify: `CodeGenerationEngine/Core/NewClassCompletion.cs` (the loops in `Complete`, `Key`, `StubKind`, `IsOverridden`, `Access`, `Stub` move out)
- Test: `CodeGenerator.Tests/Core/MemberStubsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record MemberStub(string Code, string MemberName, string From);` — `Code` is one line with no indentation; `From` is the interface or abstract base type's `Name`.
  - `public static IReadOnlyList<MemberStub> MemberStubs.Missing(INamedTypeSymbol type, SemanticModel model, int position)` — overrides for abstract bases (only if `type` is not abstract), then interface members not implemented (abstract declarations when `type` is abstract); de-duplicated by the existing `Key`. Order identical to today's `Complete`.

- [ ] **Step 1: Write failing tests** in `MemberStubsTests` (compile a small source with `CSharpCompilation` + `Compiler.FrameworkReferences`, get the symbol, call `Missing`):
  - `Struct_implementing_an_interface_gets_a_method_stub`: `interface IShape { double Area(); } struct Sq : IShape { }` → one stub, `Code == "public double Area() => throw new System.NotImplementedException();"`, `From == "IShape"`.
  - `Member_in_another_partial_part_is_not_stubbed`: `interface I { int Id { get; set; } void Save(); } partial class A : I { } partial class A { public void Save() { } }` (two trees) → only `Id`.
  - `Generic_interface_stub_uses_the_type_argument`: `class O : System.IComparable<O> { }` → `Code == "public int CompareTo(O? other) => throw new System.NotImplementedException();"` (nullable enabled).
- [ ] **Step 2:** `dotnet test CodeGenerator.Tests --filter FullyQualifiedName~MemberStubsTests` → FAIL (type missing).
- [ ] **Step 3:** Create `MemberStubs` by moving the code; `NewClassCompletion.Complete` calls `Missing` and builds `interfaces` (distinct `From`, in order) and `added` (`MemberName`) from the result. No behaviour change.
- [ ] **Step 4:** Run `--filter "FullyQualifiedName~MemberStubsTests|FullyQualifiedName~NewClassCompletion|FullyQualifiedName~TypeKindCreate|FullyQualifiedName~CreateClassInProject"` → PASS.
- [ ] **Step 5:** Commit `Move the member stub writer to MemberStubs`.

### Task 2: `ClassInheritanceService` — list, stubs, struct and record

**Files:**
- Modify: `CodeGenerationEngine/Core/ClassInheritanceService.cs`
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs:560-574` (`HandleClassInherit`), `CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs:55-66`
- Test: `CodeGenerator.Tests/Core/ClassInheritCommandTests.cs` (service-level tests call the service directly, like the existing `sealed` test)

**Interfaces:**
- Consumes: `MemberStubs.Missing` (Task 1).
- Produces:
  - `public static bool AddInheritance(string workspaceRoot, string className, IReadOnlyList<string> types, bool interfacesOnly, out string message)`.
  - The existing `AddInheritance(workspaceRoot, className, baseClass, out message)` stays and calls the new one with `[baseClass]`, `interfacesOnly: false`.
  - Handlers: when `Command.Interfaces` has any non-blank entry, call with `Command.Interfaces`, `interfacesOnly: true`; otherwise as today with `BaseClass`.

Behaviour:
- Target node: any `TypeDeclarationSyntax` that is a class, interface, struct, record or record struct, by name. A struct / record struct may take only interfaces ("a struct can only implement interfaces"); a record may take only a record as base class.
- Every name is checked with today's rules before anything is written; the first refusal is the message and the file is unchanged.
- `interfacesOnly` and a name binds to a non-interface → refuse: `'AuditableBase' is a class, not an interface; use 'Order inherits AuditableBase'.`
- After the base list edit, compile the edited tree in the project compilation and call `MemberStubs.Missing`. Insert the stubs before the type's closing brace, each on its own line, indented like the type's first member (type indent + 4 spaces when it has none), using the file's line ending. A type with a `;` body (`record Order(int Id);`) gets ` { ... }` in place of the `;`.
- Message: today's "now implements/inherits" sentence(s), then, when stubs were added: ` Added {names}, which it lacked; a method added this way throws NotImplementedException until you fill it in.` (abstract type: `added as abstract declarations.`). Members `MemberStubs` could not stub (indexers, by-ref returns) are still named with today's `Missing` text.

- [ ] **Step 1: Write failing tests** (each asserts the file content and `AssertCompiles`):
  - `Implements_adds_the_interface_and_stubs_its_members`: `interface IAuditable { string Who { get; set; } void Stamp(); }`, class `Order` with `Id` → `class Order : IAuditable`, contains `public string Who { get; set; }` and `public void Stamp() => throw new System.NotImplementedException();`.
  - `Implements_two_interfaces_in_one_command`: types `["IAuditable", "IExportable"]` → `: IAuditable, IExportable`.
  - `A_bad_name_in_the_list_leaves_the_file_unchanged`: `["IAuditable", "INope"]` → returns false, file bytes equal, message contains `INope`.
  - `Implements_refuses_a_class`: `["AuditableBase"]`, `interfacesOnly: true` → false, message contains `inherits`.
  - `Inherits_an_abstract_base_adds_overrides`: `abstract class Shape { public abstract double Area(); }`, `Sq inherits Shape` → `public override double Area() => throw ...`.
  - `Struct_implements_an_interface`: `struct Point { }` + `IShape` → compiles.
  - `Positional_record_without_body_gets_one`: `public record Order(int Id);` + `IAuditable` → compiles, contains `Stamp`.
  - `Stubs_keep_crlf_and_member_indentation`: file written with `\r\n`, block namespace, class with a member at 8 spaces → every stub line starts with 8 spaces, file has no bare `\n`.
  - `Abstract_class_gets_abstract_declarations`: `public abstract class Base` + `IAuditable` → `public abstract void Stamp();`.
- [ ] **Step 2:** Run `--filter FullyQualifiedName~ClassInheritCommandTests` → new tests FAIL, old ones PASS.
- [ ] **Step 3:** Implement as above.
- [ ] **Step 4:** Same filter → all PASS.
- [ ] **Step 5:** Commit `Inherits and implements stub the members a type now lacks`.

### Task 3: Recognise `implements` and a list; help text

**Files:**
- Modify: `NaturalLanguageBridge/CommandFront.cs:55-62` (`ClassInheritRe`) and `:351-360`
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs:236-242`
- Regenerate: `docs/commands.md` (`UPDATE_COMMAND_DOCS=1`, `CommandReferenceDocTests`) and the manual appendix (`UPDATE_MANUAL_APPENDIX=1`, `ManualAppendixDocTests`)
- Test: `CodeGenerator.Tests/Core/ClassInheritCommandTests.cs`

**Interfaces:**
- Consumes: Task 2's handler routing (`Command.Interfaces` non-empty → interfaces only).
- Produces: `ParsedCommand { Intent = ClassInherit, ClassName, Interfaces = [...] }` for `implements`; `BaseClass` as today for `inherits|extends|derives from`.

Pattern: `^(?:class\s+)?(?<class>\w+)(?:\s+class)?\s+(?<verb>implements?|inherits?|extends|derives\s+from)\s+(?<types>.+)$`; split `types` on `,` and the word `and` at generic depth 0; each item must match `^[A-Za-z_]\w*(?:<.+>)?$` after trimming, else the guard does not match. `inherits|extends|derives from` keep accepting exactly one name (a list there does not match the guard, as today). Must not match a sentence starting with `create`/`generate` (existing test).

- [ ] **Step 1: Write failing tests**:
  - `Implements_is_recognised`: `CommandFront.Parse("Order implements IAuditable", ws)` → `ClassInherit`, `Interfaces == ["IAuditable"]`.
  - `Implements_reads_a_list_with_commas_and_and`: `"Order implements IAuditable, IExportable and ITracked"` → three names, in order.
  - `Implements_reads_a_generic_name`: `"Order implements IComparable<Order>, IAuditable"` → `["IComparable<Order>", "IAuditable"]`.
  - `Implements_and_a_second_command_is_two_commands`: `CommandFront.TwoCommands("Order implements IAuditable and add method Save")` is not null.
  - `Run_implements_end_to_end`: write the Task 2 fixture, `Run("Order implements IAuditable")` → true, file compiles.
- [ ] **Step 2:** Run `--filter FullyQualifiedName~ClassInheritCommandTests` → new tests FAIL.
- [ ] **Step 3:** Implement the guard. Add a `CommandDictionary` entry: Category `Class`, Command `<class name> implements <interface>[, <interface> ...]`, Description says it adds the interfaces to a class, struct or record and adds a stub for each member it lacks (a method throws `NotImplementedException` until filled in), and that a class name is refused (use inherits); Example `Order implements IAuditable, IExportable`. Update the `inherits` entry's description to say an abstract base's members are added as overrides.
- [ ] **Step 4:** Regenerate the two docs; run `--filter "FullyQualifiedName~ClassInheritCommandTests|FullyQualifiedName~CommandReferenceDoc|FullyQualifiedName~ManualAppendix|FullyQualifiedName~CatalogExampleParse|FullyQualifiedName~CommandCatalog|FullyQualifiedName~CommandHelpRendering|FullyQualifiedName~ReachableIntent"` → PASS.
- [ ] **Step 5:** Commit `Recognise 'implements' with a list of interfaces`.

### Task 4: Phase gate

- [ ] Full suite once: `dotnet test SmartCodeGenerator.slnx -c Release` and `dotnet build SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` → green.
- [ ] Rebuild `CodeGenerator`, then a live CLI run with `PROJECTPATH` = scratch folder: create a project, `create interface IAuditable with properties Who and methods Stamp`, `create class Order with properties Id`, `Order implements IAuditable`, then `dotnet build` the project → 0 errors.
- [ ] Fresh final reviewer (most capable model) over the whole branch.
