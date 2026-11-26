# Known Core Defects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix seven core command-path defects in which a `create class` / `create interface` line loses part of what was typed, or prints a wrong message.

**Architecture:** Each defect has a found root cause (below). The fixes stay in the existing parser (`NaturalLanguageBridge`) and handler (`CodeGenerationEngine`) files and reuse the rules that `add property` already uses (`PropertyInitializers`, project-aware compile in `NewClassCompletion`). No new subsystem.

**Tech Stack:** C# / .NET 10, Roslyn, MSTest.

**Spec:** this plan (root causes were found by live CLI runs on 2026-11-26; decisions by Rohit are quoted).

## Root causes (the spec)

| # | Symptom (live CLI run) | Root cause |
|---|---|---|
| 1 | `create class A inherits Shape` → "Command not recognized" (`Bee inherits Shape` works) | `CommandFront.CreateClassRe` (`CommandFront.cs:123`) accepts only `<name>` or `<name> with ...`; the line falls to the mapper, which drops `A` as an article. `extends` fails the same way. |
| 2 | `create class Order with properties Total decimal and add method Ship to Order` → class written without `Ship`, warning "Ignored 'add method Ship to Order': only built-in types ..." | (a) `ExtractPropertySection` (`CommandEntityExtractor.cs:1243`) stops at `and method`, not `and add method`; (b) `TryParseMethod("Ship to Order")` (`:766`) matches no form and returns true with a null method, so the method is dropped silently. |
| 3 | `create class Customer with properties Name, Kind Buyer` (Buyer = project enum) → "Unsupported type 'Buyer'"; `Name string, Owner Shape` → Owner "Ignored" | The create-class/create-interface property list accepts only built-in types (`BuiltInType`, `:1093`; `TypeValidator` in `ClassValidator.Validate`). `add property Kind Buyer to Customer` accepts it. **Decision: same rule as `add property` — accept every type the project declares (enum, struct, record, class, interface).** |
| 4 | `create class Doc implements INamed` → `public string Title { get; set; }` with no initialiser → CS8618 in a Nullable project | `MemberStubs.cs` does not use `PropertyInitializers`. |
| 5 | `create class B : Shape` → `public class B` with no base, no message | Same as 1: falls to the mapper, which drops `: Shape`. **Decision: accept `:` as `inherits`.** |
| 6 | `with properties Data byte[]` → "Ignored 'Data byte[]': only built-in types (... also with ?, [] ...)" | `BuiltInType` lists only string/int/long/bool/decimal/double/float/datetime/guid; `byte`, `short`, `char`, ... are missing although `TypeValidator` supports them. Fixed by Task 3's rule. |
| 7 | `create interface INamed with properties Title string, Tags List<string>` → "Compilation failed ... CS0246 List<>", then "Saved", file is correct | `GenerateAndSave` (`CommandHandlingService.cs:1343`) sends only CreateClass/CreateEnum through the project-aware `NewClassCompletion`; CreateInterface goes to `SemanticCompilerRunner`, which compiles the file alone (no implicit usings), reports failure, and saves anyway. |

## Global Constraints

- Every live CLI run sets `PROJECTPATH` to a scratch folder; after `create project` remove its entry from `CodeGenerator/bin/Debug/net10.0/ProjectManager.dat`. Never kill a process by image name — kill by PID.
- Build from the repo root `D:\SmartCodeGenerator-Repository\`. Per-task gate: build + focused `--filter`; the full suite runs once, at the end.
- CI = `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`; it must stay clean.
- `create class X and add method Y` is ONE command (test-pinned). Any phrase change must keep `CommandFront.TwoCommands` behaviour for its existing tests.
- Emitted C# is tested by compiling it (Roslyn), not by substring alone.
- No `git stash`. Commit after each task.

## Review Focus

1. `create a class Customer inherits Shape` / `create class a inherits Shape` — the article and a lower-case name must both still work (Task 1).
2. `create class Order with properties Total decimal and add method Ship to Customer` — a method aimed at ANOTHER class must be refused with a message, not added to Order and not dropped (Task 2).
3. A typo in a project type (`Kind Buyr`) must not be silently dropped: the command is refused, nothing written, message names the type (Task 3).
4. A class-typed property in a struct (`create struct P with properties Owner Shape`) must still compile (no initialiser in a struct) (Task 3).
5. An interface stub of a `string?` / value-type / enum property gets no initialiser; `string` gets `= string.Empty`; `List<T>` gets `= new()`-style value as `add property` gives (Task 4).

---

### Task 1: `create class` with a base-type clause (defects 1, 5)

**Files:**
- Modify: `NaturalLanguageBridge/CommandFront.cs` (`CreateClassRe` ~line 123; the CreateClass branch ~line 442)
- Test: `CodeGenerator.Tests/Bridge/CommandFrontParseTests.cs`, `CodeGenerator.Tests/Core/CreateClassInProjectTests.cs`

**Interfaces:**
- Produces: `ParsedCommand.BaseClass` / `ParsedCommand.Interfaces` filled for every `create class <Name> <clause> <Types> [with ...]` line, where clause ∈ `inherits|inherit|extends|derives from|derived from|implements|implement|:`.

- [ ] **Step 1: Write the failing tests** (CommandFrontParseTests)
  - `Create_class_named_A_inherits_keeps_the_name`: `Parse("create class A inherits Shape")` → Intent CreateClass, ClassName `A`, BaseClass `Shape`.
  - `Create_class_extends_reads_the_base`: `create class A extends Shape` → same.
  - `Create_class_colon_reads_the_base`: `create class B : Shape` → ClassName `B`, BaseClass `Shape`.
  - `Create_class_colon_with_properties`: `create class B : Shape with properties Size int` → BaseClass `Shape`, one property `Size int`.
  - `Create_a_class_a_inherits`: `create a class a inherits Shape` → ClassName `A`, BaseClass `Shape`.
  - `Create_class_implements_after_name`: `create class A implements IShape, IOther` → Interfaces `[IShape, IOther]`.
- [ ] **Step 2: Run** `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandFrontParseTests"` → the new tests FAIL.
- [ ] **Step 3: Implement.** Let `CreateClassRe` accept, after the name, an optional base-type clause (the verbs above, or `\s*:\s*`) followed by type names, then the optional `with ...`. For `:`, read the first type name as the base class when it is not an interface-looking name, exactly as `inherits` would be read: simplest is to rewrite `<name> : <types>` to `<name> inherits <types>` before `CommandEntityExtractor.Extract`, so the extractor's existing `ExtractInheritanceInfo` / `ExtractInterfaceInfo` read it. Keep the name from the regex group (not the mapper).
- [ ] **Step 4: Handler test** (CreateClassInProjectTests, using its `Run`/`Write` helpers): `Run("create class A inherits Base1")` writes `A.cs` containing `class A : Base1` and the project compiles; `Run("create class B : Base1")` the same for `B`.
- [ ] **Step 5: Run** both filters → PASS. Commit `Create class reads inherits/extends/: after any name`.

### Task 2: `... and add method Y [to X]` inside create class (defect 2)

**Files:**
- Modify: `NaturalLanguageBridge/CommandEntityExtractor.cs` (`ExtractPropertySection` stop regex ~1243; `ExtractMethods` / `CleanMethodToken` ~613-698; `TryParseMethod` ~766-828)
- Test: `CodeGenerator.Tests/Bridge/CommandEntityExtractorTests.cs`, `CodeGenerator.Tests/Core/CreateClassCommandTests.cs` (or CreateClassInProjectTests)

**Interfaces:**
- Consumes: `ParsedCommand.IgnoredMethods` (`List<(string Entry, string Error)>`), reported by `ReportIgnoredProperties` as `Ignored method '<entry>': <error>`.

- [ ] **Step 1: Failing tests** (extractor, intent CreateClass):
  - `And_add_method_to_same_class_is_a_method`: `create class Order with properties Total decimal and add method Ship to Order` → Properties `[Total decimal]`, Methods `[Ship]`, IgnoredProperties empty, IgnoredMethods empty.
  - `And_add_method_without_class`: `... with properties Name string and add method Ship` → Methods `[Ship]`, IgnoredProperties empty.
  - `And_create_method_with_returns`: `... with properties Total decimal and create method Total2 returns int to Order` → method `Total2` returning `int`.
  - `And_add_method_to_other_class_is_refused`: `create class Order with properties Total decimal and add method Ship to Customer` → Methods empty; IgnoredMethods has one entry whose error names `Customer` and says to use `add method Ship to Customer` after the class exists.
  - `Unreadable_method_entry_is_reported`: `create class Order with methods Ship quickly now` → Methods empty; IgnoredMethods has `Ship quickly now` (no silent drop).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CommandEntityExtractorTests"` → FAIL.
- [ ] **Step 3: Implement.**
  - Property-section stop also at `and (add|create|generate) [a|an|new] [abstract] (method|function)`.
  - In a create-class/interface member list, a method entry ending `(to|for|in|on) [the] [class] <Name>`: same name as the class being created (case-insensitive) → strip it; another name → `IgnoredMethods` with the message above.
  - `TryParseMethod`: an entry that matches none of its forms returns false with error `"'<entry>' is not a method; type a name, Name(type param), or Name returns Type."` (exact wording free, must name the entry). Keep true-with-null only for an empty token.
- [ ] **Step 4: Handler test:** run the Step 1 first line through the create-class handler in a temp project; `Order.cs` contains `void Ship()`, output has no `Ignored` line; compiles.
- [ ] **Step 5: Run** filters + `CommandFrontParseTests` (TwoCommands tests must stay green) → PASS. Commit `Create class reads 'and add method Y to X' and reports unreadable methods`.

### Task 3: Project types and all supported built-in types in create class / create interface (defects 3, 6)

**Files:**
- Modify: `NaturalLanguageBridge/CommandEntityExtractor.cs` (`BuiltInType`, `TypedEntry`, `Ignored`), `CodeGenerationEngine/Models/PropertyDefinition.cs` (or ParsedCommand) if a flag is needed, `CodeGenerationEngine/Core/ClassValidator.cs`, `CodeGenerationEngine/Service/CommandHandlingService.cs` (`HandleCreateClass` ~250-330, create-interface handler ~864, `ReportIgnoredProperties` ~521), `CodeGenerationEngine/Core/CodeGenerator.cs:114` (initialiser).
- Test: `CodeGenerator.Tests/Bridge/CommandEntityExtractorTests.cs`, `CodeGenerator.Tests/Core/CreateClassInProjectTests.cs`

**Interfaces:**
- Consumes: `ClassFiles.DeclaringFile(string searchRoot, string typeName)`, `ProjectLocator.Resolve(...)`, `PropertyInitializers.SafeFor(TypeSyntax, ITypeSymbol?)`, `PropertyInitializers.NullableOn(...)`.
- Produces: a create-class/create-interface property list entry `Name Type` is a property when Type is (a) any type `TypeValidator` supports, with `?`, `[]` or `List<>`, or (b) a type the project declares, with the same wrappers. Anything else refuses the whole command (nothing written) with `Type '<T>' of '<Name>' is not a built-in type and the project does not declare it.`

- [ ] **Step 1: Failing extractor tests:**
  - `Byte_array_property_is_read`: `create class Note with properties Data byte[]` → property `Data byte[]`, IgnoredProperties empty. Same for `short`, `char`, `uint?`, `List<long>`.
  - `Project_type_entry_is_a_property`: `create class Customer with properties Name string, Kind Buyer, Home Shape` → three properties, types `string`, `Buyer`, `Shape` (the extractor does not know the project; it keeps the entry as a property and the handler decides).
  - `Untyped_list_still_infers`: `create class Customer with properties Name, Email` keeps today's behaviour (fallback path, type `string`).
- [ ] **Step 2: Failing handler tests** (temp project with `public enum Buyer { Retail }`, `public class Shape { }`, Nullable on):
  - `Create_class_with_project_enum_and_class`: `create class Customer with properties Kind Buyer, Home Shape` → succeeds; file has `public Buyer Kind { get; set; }` and `public Shape Home { get; set; } = null!;` (same as `add property`); project builds with no CS8618.
  - `Create_struct_with_project_class`: `create struct P with properties Owner Shape` → compiles, no initialiser.
  - `Create_class_with_unknown_type_is_refused`: `create class Customer with properties Kind Buyr` → false, nothing written, output names `Buyr`.
  - `Create_interface_with_project_class`: `create interface INamed with properties Owner Shape` → `Shape Owner { get; set; }`.
- [ ] **Step 3: Run** both filters → FAIL.
- [ ] **Step 4: Implement.** Widen `BuiltInType` to `TypeValidator`'s set (keep `DateTime`/`Guid` casing as today). Make the extractor keep a two-word `Name Type` entry whose type is a valid C# type name (identifier, optional `?`, `[]`, or `List<...>`) as a property. In the handler, before `ClassValidator.Validate`, check every non-built-in type (element type for `[]`/`List<>`/`?`) with `ClassFiles.DeclaringFile` in the target project; refuse the unknown ones; let `ClassValidator` skip `TypeValidator` for the project-declared ones. Give the initialiser the same value `add property` gives (bind the type in the project compile, or `null!` for a project class/record/interface, none for enum/struct, none inside a struct). Delete the "Relationships between classes are set separately" sentence where it no longer applies; keep the warning only for entries that are genuinely unreadable (bad name).
- [ ] **Step 5: Run** filters + `CreateClassCommandTests` → PASS. Commit `Create class and interface accept project types and every supported built-in type`.

### Task 4: Initialisers on interface property stubs (defect 4)

**Files:**
- Modify: `CodeGenerationEngine/Core/MemberStubs.cs` (and its callers `NewClassCompletion.cs`, `ClassInheritanceService.cs` if they print the property)
- Test: `CodeGenerator.Tests/Core/MemberStubsTests.cs`, `CodeGenerator.Tests/Core/CreateClassInProjectTests.cs`

- [ ] **Step 1: Failing tests:** in a Nullable-enabled compilation, the stub for interface `string Title {get;set;}` is `public string Title { get; set; } = string.Empty;`; `List<string> Tags` gets the value `PropertyInitializers.SafeFor` gives; `string? Note`, `int Count`, an enum, and any stub inside a struct get none; with Nullable disabled, none. Handler test: `create class Doc implements INamed` (Title string, Tags List<string>) → project builds with zero CS8618 warnings (`--no-incremental`).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~MemberStubsTests"` → FAIL.
- [ ] **Step 3: Implement** by calling `PropertyInitializers.SafeFor(type syntax, the interface property's type symbol)` when the containing type can have an initialiser and Nullable is on at the insertion point — the same three checks `ClassPropertyService.cs:126-129` uses. Same for `implements` on an existing class (`ClassInheritanceService`).
- [ ] **Step 4: Run** filters → PASS. Commit `Interface property stubs get add property's initialiser`.

### Task 5: `create interface` compiled with its project (defect 7)

**Files:**
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` (`GenerateAndSave` ~1337)
- Test: `CodeGenerator.Tests/Core/CreateClassInProjectTests.cs` (or a create-interface test file)

- [ ] **Step 1: Failing tests:** in a temp project, `create interface INamed with properties Title string, Tags List<string>` → output contains no `Compilation failed`, contains `Compiled`, file saved. `create interface IBad with properties Owner Nowhere1` (after Task 3 this is refused at validation; instead use a method whose return type is unknown, e.g. `create interface IBad with methods Load returns Nowhere1`) → returns false, nothing written, the error is printed.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement:** send `IntentType.CreateInterface` through the same `NewClassCompletion.Complete` / refuse-on-error path as CreateClass/CreateEnum. Check `NewClassCompletion` does not add stubs or members to an interface.
- [ ] **Step 4: Run** filters → PASS. Commit `Create interface is checked with its project and refused on a real error`.

### Task 6: Final gate

- [ ] Rebuild `CodeGenerator/CodeGenerator.csproj`; rerun every symptom line from the root-cause table in a scratch project (PROJECTPATH set) and check the files and `dotnet build --no-incremental` of the scratch project (zero errors, zero CS8618).
- [ ] Full suite once: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx` (if "Test Run Aborted", rerun `CodeGenerator.Tests` alone). CI build command clean.
- [ ] Update `docs/commands.md` generation source (`CommandDictionary`) only if a documented phrase changed (`create class X : Base` now documented next to `inherits`).
- [ ] Fresh opus whole-branch review; fix Critical/Important findings.
